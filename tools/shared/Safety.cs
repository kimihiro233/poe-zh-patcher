// 写入安全层：**任何情况下不能让客户端变成「游戏读不出来」的状态**。
//
// 这个文件管的是 Bundles2（Steam / WeGame）：索引就是 20 MB 的 _.index.bin，整份复制最快。
// 官方独立端的 Content.ggpk（74 GB）走 GgpkUndo.cs，思路相同但换成了逐字节日志。
//
// 现实的失败模式不是「写错一个字节」，而是写到一半断掉：
// 断电、任务管理器结束进程、磁盘满、库自己抛异常。LibBundle3 的 Index.Save() 是把
// 20 MB 的 _.index.bin 整个覆盖重写，一旦中途停止，索引就是半截 —— 游戏直接读不了。
// 而索引里指向的那条自建 bundle 可能已经写好了，也可能没写，谁都不知道。
//
// 所以每次写入都开一个事务：
//   1. 写之前把 _.index.bin 整份复制一份（20 MB，几百毫秒），并落一份 journal；
//   2. 写文件、存索引；
//   3. 关掉索引重新打开、逐个回读校验；
//   4. 校验通过才提交（删 journal、清孤儿 bundle），任何一步不对就整份回滚。
//
// journal 是「上次没走完」的证据：下次启动先看它，有就无条件回滚到写前状态。
// 回滚只做两件确定性的事 —— 索引换回备份、删掉本次新建的 bundle 文件 ——
// 都不依赖任何中间状态，所以断电也能恢复。
using System.Text;
using System.Text.Json;

internal sealed class JournalFile {
    public string Started { get; set; } = "";
    public string Index { get; set; } = "";
    public string IndexBackup { get; set; } = "";
    public string Phase { get; set; } = "writing";     // writing / saved
    public List<string> NewBundles { get; set; } = new();
    public List<JournalWritten> Written { get; set; } = new();
}

internal sealed class JournalWritten {
    public string Path { get; set; } = "";
    public string Sha { get; set; } = "";
}

internal sealed class RecoverResult {
    public bool Recovered;
    public string Message = "";
    public int RestoredFiles;
}

// 「后悔药」的统一接口。上层（安装 / 还原 / 只改语系）只关心三件事：
// 写坏了能不能退、退的时候要不要登记新 bundle、退完能不能提交。
// Bundles2 用 Txn（整份复制 20 MB 的 _.index.bin），Content.ggpk 用 GgpkUndo
// （74 GB 复制不了，改成「写之前逐块记下被覆盖的原字节」）。
internal interface IRollback {
    string Dir { get; }
    void AddBundle(string? name);
    void AddWritten(string path, string sha);
    void MarkSaved();
    string? Commit();
    string? Rollback();
}

internal sealed class Txn : IRollback {
    public const string DirName = ".poe-zh";
    private const string JournalName = "txn.json";
    private const long MaxIndexBackup = 512L * 1024 * 1024;   // 超过这个尺寸不做整份备份（Content.ggpk）

    private readonly JournalFile _j;
    private readonly string _dir;
    private bool _done;

    private Txn(JournalFile j, string dir) { _j = j; _dir = dir; }

    public string Dir => _dir;
    public bool CanRollback => _j.IndexBackup.Length > 0;

    /// <summary>
    /// 开事务：备份索引 + 落 journal。返回 null 表示这个容器没法整份备份
    /// （Content.ggpk 这种 74 GB 的单文件），调用方应打印警告并只依赖文件级备份。
    /// </summary>
    public static Txn? Begin(string indexPath) {
        if (!File.Exists(indexPath)) return null;
        var fi = new FileInfo(indexPath);
        if (fi.Length > MaxIndexBackup) return null;

        var dir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(indexPath))!, DirName);
        Directory.CreateDirectory(dir);

        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var bak = Path.Combine(dir, $"index-{stamp}.bin");
        try {
            File.Copy(indexPath, bak, overwrite: true);
        } catch {
            return null;      // 复制不了就别写：没有退路的安装不允许开始
        }

        var j = new JournalFile {
            Started = stamp,
            Index = Path.GetFullPath(indexPath),
            IndexBackup = bak,
            Phase = "writing",
        };
        var t = new Txn(j, dir);
        t.SaveJournal();
        return t;
    }

    public void AddBundle(string? name) {
        if (string.IsNullOrEmpty(name)) return;
        if (_j.NewBundles.Contains(name)) return;
        _j.NewBundles.Add(name);
        SaveJournal();
    }

    public void AddWritten(string path, string sha) {
        _j.Written.Add(new JournalWritten { Path = path, Sha = sha });
        SaveJournal();
    }

    /// <summary>索引已经落盘。从这一刻起「写」这件事成功了，剩下的只是校验与收尾。</summary>
    public void MarkSaved() {
        if (_done) return;
        _j.Phase = "saved";
        SaveJournal();
    }

    /// <summary>校验通过：删 journal，留下最近的备份作为后悔药。</summary>
    public string? Commit() {
        if (_done) return null;
        _done = true;
        try { File.Delete(JournalPath); } catch { }
        PruneBackups(keep: 3);
        return null;
    }

    /// <summary>
    /// 回滚到写前状态。只用「备份文件 + journal 里记的新 bundle 名」，
    /// 不做任何推断 —— 所以即使进程是被强杀的，这一步也确定能走通。
    /// </summary>
    public string? Rollback() {
        if (_done) return null;
        _done = true;
        var err = RollbackFiles(_j);
        try { File.Delete(JournalPath); } catch { }
        return err;
    }

    private void PruneBackups(int keep) {
        try {
            var files = Directory.GetFiles(_dir, "index-*.bin")
                .OrderByDescending(f => f, StringComparer.Ordinal).ToList();
            foreach (var f in files.Skip(keep)) File.Delete(f);
        } catch { }
    }

    private string JournalPath => Path.Combine(_dir, JournalName);

    private void SaveJournal() {
        // 先写临时文件再换名：journal 自己半截就等于没有 journal
        var tmp = JournalPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_j, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        File.Move(tmp, JournalPath, overwrite: true);
    }

    // ---------------------------------------------------------------- 静态：启动自愈

    /// <summary>
    /// 启动时调用：上次有没有没走完的写入？有就回滚。
    /// 判断只看 journal 是否存在 —— 提交成功会把 journal 删掉，所以「还有 journal」就等于
    /// 「上次没有确认过完整性」。宁可撤销一次本来成功的安装（重跑一次即可），
    /// 也不能把一个没人验过的索引交给游戏。
    /// </summary>
    public static RecoverResult Recover(string indexPath) {
        var r = new RecoverResult();
        var dir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(indexPath))!, DirName);
        var jp = Path.Combine(dir, JournalName);
        if (!File.Exists(jp)) return r;

        JournalFile? j = null;
        try { j = JsonSerializer.Deserialize<JournalFile>(File.ReadAllText(jp, Encoding.UTF8)); } catch { }
        if (j is null) {
            // journal 坏了：至少把最近的备份换回去，这比留着一个可疑索引强
            try {
                var latest = Directory.GetFiles(dir, "index-*.bin")
                    .OrderByDescending(f => f, StringComparer.Ordinal).FirstOrDefault();
                if (latest is not null) {
                    File.Copy(latest, indexPath, overwrite: true);
                    r.Recovered = true;
                    r.Message = "上次写入的日志已损坏，已按最近一次索引备份恢复";
                }
            } catch { }
            try { File.Delete(jp); } catch { }
            return r;
        }

        var err = RollbackFiles(j);
        try { File.Delete(jp); } catch { }

        r.Recovered = true;
        r.RestoredFiles = j.Written.Count;
        r.Message = err is null
            ? $"上次写入没有正常结束（开始于 {j.Started}），已回滚到写入前的状态"
            : $"上次写入没有正常结束，已回滚索引，但有文件没能清理：{err}";
        return r;
    }

    private static string? RollbackFiles(JournalFile j) {
        var err = new List<string>();
        // 1) 索引换回备份。这一步是「游戏能不能读」的关键，先做。
        if (j.IndexBackup.Length > 0 && File.Exists(j.IndexBackup)) {
            try { File.Copy(j.IndexBackup, j.Index, overwrite: true); }
            catch (Exception ex) { err.Add($"索引恢复失败：{ex.Message}"); }
        }

        // 2) 删掉本次新建的 bundle。索引已经换回去了，这些文件没有任何记录引用，
        //    留着只是占地方；删失败也无害，下次提交时 TrimOrphans 会再清一遍。
        var dir = Path.GetDirectoryName(j.Index)!;
        foreach (var name in j.NewBundles) {
            try {
                var p = Path.Combine(dir, name.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(p)) File.Delete(p);
            } catch (Exception ex) { err.Add($"{name}：{ex.Message}"); }
        }
        return err.Count == 0 ? null : string.Join("；", err);
    }
}

// ---------------------------------------------------------------- 文件级备份
//
// 事务解决的是「索引能不能读」。这份备份解决的是另一个问题：
// 我们要覆盖的那个文件，原来是 **别人改过的** 内容 —— 那份内容在这台机器之外
// 不存在任何副本，覆盖掉就永远找不回来了。所以写之前先把原字节落盘，
// 并记下「容器内路径 → 备份文件」，随时可以整份写回去。
internal sealed class BackupSet {
    public string Stamp { get; set; } = "";
    public List<BackupItem> Items { get; set; } = new();
    [System.Text.Json.Serialization.JsonIgnore] public string Dir { get; set; } = "";
}

internal sealed class BackupItem {
    public string Path { get; set; } = "";     // 容器内路径，如 metadata/statdescriptions/xxx.txt
    public string File { get; set; } = "";     // 备份目录里的文件名
    public string Sha { get; set; } = "";
    public long Bytes { get; set; }
    public string Flavor { get; set; } = "";   // 覆盖前的状态：原文 / 全量对照 / 外部改过 …
}

internal static class BackupStore {
    public static string RootFor(string containerPath) {
        var dir = Path.GetDirectoryName(Path.GetFullPath(containerPath))!;
        return Path.Combine(dir, Txn.DirName, "backup");
    }

    public static string ManifestName => "manifest.json";

    public static BackupSet Create(string containerPath) {
        var root = RootFor(containerPath);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var dir = Path.Combine(root, stamp);
        Directory.CreateDirectory(dir);
        var s = new BackupSet { Stamp = stamp, Dir = dir };
        Save(s);
        Prune(root, keep: 5);
        return s;
    }

    /// <summary>只保留最近几份。备份是「后悔药」，留太多会把客户端目录撑大。</summary>
    private static void Prune(string root, int keep) {
        try {
            var dirs = Directory.GetDirectories(root).OrderByDescending(d => d, StringComparer.Ordinal).ToList();
            foreach (var d in dirs.Skip(keep)) Directory.Delete(d, recursive: true);
        } catch { }
    }

    /// <summary>本次一个文件都没覆盖：把空壳目录删掉，别留下看不懂的空备份。</summary>
    public static void DropIfEmpty(BackupSet s) {
        try {
            if (s.Items.Count == 0 && Directory.Exists(s.Dir)) Directory.Delete(s.Dir, recursive: true);
        } catch { }
    }

    public static void Save(BackupSet s) {
        var tmp = Path.Combine(s.Dir, ManifestName + ".tmp");
        File.WriteAllText(tmp, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        File.Move(tmp, Path.Combine(s.Dir, ManifestName), overwrite: true);
    }

    /// <summary>把一个文件的「覆盖前字节」存下来。同一个路径只存第一次（那才是真正的原始内容）。</summary>
    public static void Add(BackupSet s, string containerPath, byte[] bytes, string flavor) {
        try {
            if (s.Items.Any(i => i.Path == containerPath)) return;
            var name = s.Items.Count.ToString("D3") + "-" + SafeName(Path.GetFileName(containerPath));
            File.WriteAllBytes(Path.Combine(s.Dir, name), bytes);
            s.Items.Add(new BackupItem {
                Path = containerPath, File = name, Sha = Sha256(bytes), Bytes = bytes.Length, Flavor = flavor,
            });
            Save(s);
        } catch { /* 备份失败不该打断安装：事务本身还能保底回滚 */ }
    }

    public static List<BackupSet> List(string containerPath) {
        var root = RootFor(containerPath);
        var list = new List<BackupSet>();
        if (!Directory.Exists(root)) return list;
        foreach (var d in Directory.GetDirectories(root).OrderByDescending(x => x, StringComparer.Ordinal)) {
            var mf = Path.Combine(d, ManifestName);
            if (!File.Exists(mf)) continue;
            try {
                var s = JsonSerializer.Deserialize<BackupSet>(File.ReadAllText(mf, Encoding.UTF8));
                if (s is null) continue;
                if (s.Items.Count == 0) {           // 空壳（一个文件都没覆盖到）：不列出来，顺手清掉
                    try { Directory.Delete(d, recursive: true); } catch { }
                    continue;
                }
                s.Dir = d;
                list.Add(s);
            } catch { }
        }
        return list;
    }

    public static BackupSet? Find(string containerPath, string which) {
        var all = List(containerPath);
        if (all.Count == 0) return null;
        if (which.Length == 0 || which.Equals("latest", StringComparison.OrdinalIgnoreCase)) return all[0];
        return all.FirstOrDefault(s => s.Stamp == which)
               ?? all.FirstOrDefault(s => s.Stamp.StartsWith(which, StringComparison.Ordinal));
    }

    /// <summary>备份目录里那份原始字节；文件被删掉了返回 null。</summary>
    public static byte[]? Read(BackupSet s, BackupItem it) {
        var p = Path.Combine(s.Dir, it.File);
        if (!File.Exists(p)) return null;
        var b = File.ReadAllBytes(p);
        return Sha256(b) == it.Sha ? b : b;    // sha 对不上也返回：那是用户唯一的一份
    }

    private static string SafeName(string s) {
        foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s.Length > 60 ? s[..60] : s;
    }

    private static string Sha256(byte[] b) {
        using var h = System.Security.Cryptography.SHA256.Create();
        return Convert.ToHexString(h.ComputeHash(b)).ToLowerInvariant();
    }
}
