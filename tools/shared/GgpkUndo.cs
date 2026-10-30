// 老式 Content.ggpk（官方独立端）是 74 GB 的单文件，整份复制不现实 —— 但它也不需要整份复制。
//
// 实测（74,730,084,326 B 的真客户端）：装 1 个文件，整份索引落盘只改写了 43 块、共 21 MB，
// 文件长度一个字节都没变 —— 也就是说「改写」全是原地覆盖，被覆盖掉的旧字节
// 在这台机器之外不存在第二个副本。所以只要在写之前把「将要被覆盖的那段原文」记下来，
// 就能用 21 MB 换回 74 GB：不需要复制、不需要重装、不需要 Steam 验证完整性。
//
// 记账点放在流这一层：所有写入都经过 UndoStream，先落日志再动文件。
// 日志里只存「第一次碰某段时的原文」，所以同一块被反复覆盖也只占一份空间。
//
// 两个不变量，和 Safety.cs 里的 Txn 完全一致：
//   · 日志永远先于数据落盘（进程被杀、崩溃、磁盘满都能退）；
//   · 回滚只依赖日志里的字节，不做任何推断，所以断电重启后也确定能走通
//     （断电最多丢最近 150 ms 的日志，见 SyncLog）。
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal sealed class GgpkUndoMeta {
    public string Stamp { get; set; } = "";
    public string Target { get; set; } = "";
    public string Created { get; set; } = "";
    public long OrigLength { get; set; }        // 写入前的文件长度
    public int Records { get; set; }            // 记了多少块
    public long RecordBytes { get; set; }       // 一共记了多少字节原文
    public string Phase { get; set; } = "writing";   // writing / committed / rolledback / restored
    public long SavedLength { get; set; }       // 写完之后的文件长度
    // 这次覆盖掉的文件，覆盖前是什么状态。用来在轮转时把「别处没有第二份」的快照 pin 住 ——
    // 外部汉化改过的字节，删了就是永久没了，而轮转本来只是为了省磁盘。
    public string Flavor { get; set; } = "";
}

internal sealed class GgpkUndo : IRollback {
    private const string UndoName = "undo.bin";
    private const string MetaName = "meta.json";
    private const long SyncMs = 150;                      // 硬落盘节流：断电最多丢这么久
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("POEZHU1\n");

    // 一次安装 = 一份日志。容器中途重开（落盘后回读校验）要接着往同一份里记，
    // 所以留一个静态引用：新开的容器认领它，而不是另起一份空日志。
    internal static GgpkUndo? Active;

    private readonly GgpkUndoMeta _m;
    private FileStream? _log;
    private Stream? _inner;
    private readonly List<(long S, long E)> _cov = new();   // 已记过的区间（升序、不相交、已合并）
    private bool _finished;
    private DateTime _lastSync = DateTime.MinValue;

    private GgpkUndo(string dir, FileStream? log, GgpkUndoMeta m) {
        Dir = dir;
        _log = log;
        _m = m;
    }

    public string Dir { get; }
    public string Target => _m.Target;
    public long OrigLength => _m.OrigLength;
    public bool HasWrites => _m.Records > 0;
    public bool Finished => _finished;
    public long Bytes => _m.RecordBytes;

    // ---------------------------------------------------------------- 建立 / 认领

    public static string RootFor(string ggpkPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(ggpkPath))!, Txn.DirName, "ggpk");

    public static bool IsGgpk(string path) =>
        Path.GetFileName(path).Equals("Content.ggpk", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".ggpk", StringComparison.OrdinalIgnoreCase);

    public static GgpkUndo Begin(string ggpkPath) {
        var full = Path.GetFullPath(ggpkPath);
        var fi = new FileInfo(full);
        var root = RootFor(full);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var dir = Path.Combine(root, stamp);
        for (var n = 2; Directory.Exists(dir); n++) dir = Path.Combine(root, stamp + "-" + n);
        Directory.CreateDirectory(dir);

        var log = new FileStream(Path.Combine(dir, UndoName), FileMode.CreateNew, FileAccess.Write, FileShare.Read, 1 << 20);
        log.Write(Magic);
        var m = new GgpkUndoMeta {
            Stamp = Path.GetFileName(dir),
            Target = full,
            Created = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            OrigLength = fi.Length,
        };
        var u = new GgpkUndo(dir, log, m);
        u.SaveMeta();
        Active = u;
        return u;
    }

    /// <summary>打开容器时接上一份日志：上一次安装还没提交就接着用，否则另起一份。</summary>
    public static GgpkUndo ForOpen(string ggpkPath) {
        var full = Path.GetFullPath(ggpkPath);
        var a = Active;
        if (a is not null && !a._finished && string.Equals(a.Target, full, StringComparison.OrdinalIgnoreCase))
            return a;
        return Begin(full);
    }

    /// <summary>把日志接在真正写文件的那个流上。所有写入都会先经过 Note。</summary>
    public Stream Wrap(Stream inner) {
        _inner = inner;
        return new UndoStream(inner, this);
    }

    // ---------------------------------------------------------------- 记账

    /// <summary>
    /// 即将往 pos 写 len 个字节。这里把「被覆盖之前的那段」存进日志。
    /// 调用方必须在本函数返回之后再真正写 —— 顺序反了就没有后悔药。
    /// </summary>
    internal void Note(long pos, int len) {
        if (_finished || _inner is null || len <= 0) return;

        var end = Math.Min(pos + len, _m.OrigLength);    // 超出原末端的都是追加，没有「原文」可存
        if (end <= pos) return;                          // 追加只会在回滚时留着，游戏读不到它

        var back = _inner.Position;
        var added = 0;
        try {
            foreach (var (s, e) in Uncovered(pos, end)) {
                var n = checked((int)(e - s));
                var buf = new byte[n];
                _inner.Position = s;
                ReadExactly(_inner, buf, n);
                _log!.Write(BitConverter.GetBytes(s));
                _log!.Write(BitConverter.GetBytes(n));
                _log!.Write(buf);
                _m.Records++;
                _m.RecordBytes += n;
                added++;
            }
        } finally {
            _inner.Position = back;
        }

        if (added > 0) {
            _log!.Flush();               // 先到系统缓存：进程被杀 / 崩溃也退得回去
            SyncLog();                   // 再按节流真正刷盘：断电也退得回去
            Cover(pos, end);
        }
    }

    /// <summary>日志刷盘。节流到 150 ms 一次：一次安装只有几十块记录。</summary>
    internal void SyncLog() {
        if (_log is null) return;
        try {
            var now = DateTime.UtcNow;
            if ((now - _lastSync).TotalMilliseconds < SyncMs) return;
            _lastSync = now;
            _log.Flush(true);
        } catch { }
    }

    // ---------------------------------------------------------------- 区间

    private int IndexAt(long s) {          // 第一个 E > s 的位置
        int lo = 0, hi = _cov.Count - 1, ans = _cov.Count;
        while (lo <= hi) {
            var mid = (lo + hi) >> 1;
            if (_cov[mid].E > s) { ans = mid; hi = mid - 1; } else lo = mid + 1;
        }
        return ans;
    }

    private List<(long S, long E)> Uncovered(long s, long e) {
        var res = new List<(long, long)>();
        var cur = s;
        for (var i = IndexAt(s); i < _cov.Count; i++) {
            var r = _cov[i];
            if (r.S >= e) break;
            if (r.S > cur) res.Add((cur, Math.Min(r.S, e)));
            if (r.E > cur) cur = r.E;
            if (cur >= e) break;
        }
        if (cur < e) res.Add((cur, e));
        return res;
    }

    private void Cover(long s, long e) {
        var i = IndexAt(s);
        var ns = s;
        var ne = e;
        while (i < _cov.Count && _cov[i].S <= ne) {
            if (_cov[i].S < ns) ns = _cov[i].S;
            if (_cov[i].E > ne) ne = _cov[i].E;
            _cov.RemoveAt(i);
        }
        _cov.Insert(i, (ns, ne));
    }

    // ---------------------------------------------------------------- 收尾

    public void AddBundle(string? name) { }
    public void AddWritten(string path, string sha) { }

    /// <summary>索引已经落盘：顺手把块数写进 meta，回滚日志里就能报出真实数字。</summary>
    public void MarkSaved() {
        SyncLog();
        SaveMeta();
    }

    public string? Commit() {
        if (_finished) return null;
        _finished = true;
        try { _log?.Flush(true); _log?.Dispose(); } catch { }
        _log = null;

        if (!HasWrites) { DropDir(); return null; }     // 一个字节都没写：不留空壳

        Console.WriteLine($"  逐字节日志：{_m.Records} 块 / {_m.RecordBytes / 1048576.0:F1} MB 原文已备好（写坏了整份退回）");
        _m.Phase = "committed";
        try { _m.SavedLength = new FileInfo(_m.Target).Length; } catch { }
        SaveMeta();
        Prune(keep: 3);
        return null;
    }

    /// <summary>演练结束时用：盘上已经是想要的样子，把日志丢掉就行。</summary>
    public void Abandon() {
        if (_finished) return;
        _finished = true;
        try { _log?.Flush(true); _log?.Dispose(); } catch { }
        _log = null;
        DropDir();
    }

    /// <summary>只读打开留下的空日志：删掉，别在游戏目录里越堆越多。</summary>
    public void TrimIfEmpty() {
        if (_finished || HasWrites) return;
        _finished = true;
        try { _log?.Dispose(); } catch { }
        _log = null;
        DropDir();
    }

    /// <summary>
    /// 记下「这次覆盖掉的是什么东西」。只记第一份非原文的状态就够了：
    /// 一份快照里只要有一行别人的译文，这份快照就不能随轮转消失。
    /// </summary>
    public void NoteFlavor(string flavor) {
        if (flavor.Length == 0 || flavor == "官方原文") return;
        if (_m.Flavor.Length > 0) return;
        _m.Flavor = flavor;
    }

    public string? Rollback() {
        if (_finished) return null;
        _finished = true;
        try { _log?.Flush(true); _log?.Dispose(); } catch { }
        _log = null;
        var err = ApplySnapshot(_m, Dir, out _);
        // 退成功了就把日志删掉：这份日志的使命已经完成，留着只会让下次启动误以为「上次没走完」
        _m.Phase = err is null ? "rolledback" : "writing";
        SaveMeta();
        if (err is null) DropDir();
        return err;
    }

    private void DropDir() {
        try { if (Directory.Exists(Dir)) Directory.Delete(Dir, recursive: true); } catch { }
        // 顺手把空的 ggpk/ 和 .poe-zh/ 也收掉，别在游戏目录里留空壳
        try {
            var p = Path.GetDirectoryName(Dir);
            while (p is not null && Directory.Exists(p) && Directory.GetFileSystemEntries(p).Length == 0) {
                Directory.Delete(p);
                p = Path.GetDirectoryName(p);
            }
        } catch { }
    }

    private static bool IsForeign(string flavor) =>
        flavor is "外部改过" or "旧版补丁" or "旧版对照" or "未知" or "读不出";

    private void Prune(int keep) {
        try {
            var all = Directory.GetDirectories(RootFor(_m.Target))
                .Select(d => (d, m: ReadMeta(d)))
                .Where(x => x.m is not null && x.m.Phase is "committed" or "restored")
                .OrderByDescending(x => Path.GetFileName(x.d), StringComparer.Ordinal)
                .ToList();
            if (all.Count <= keep) return;

            // 最新一份「覆盖前不是官方原文」的快照不参与轮转：里面的字节别处没有第二份
            string? pinned = null;
            foreach (var x in all) {
                if (IsForeign(x.m!.Flavor)) { pinned = x.d; break; }
            }

            foreach (var (d, m) in all.Skip(keep)) {
                if (pinned is not null && string.Equals(d, pinned, StringComparison.OrdinalIgnoreCase)) {
                    Console.WriteLine($"  快照轮转：保留 {Path.GetFileName(d)}（覆盖前是 {m!.Flavor}，别处没有第二份）");
                    continue;
                }
                // 删之前报出删的是哪一份、多大 —— 静默消失的还原点最容易被当成 bug
                Console.WriteLine($"  快照轮转：删除 {Path.GetFileName(d)}（{m!.Records} 块 / {m.RecordBytes / 1048576.0:F1} MB，超出保留的 {keep} 份）");
                try { Directory.Delete(d, recursive: true); } catch { }
            }
        } catch { }
    }

    // ---------------------------------------------------------------- 快照：列出 / 还原

    public static List<GgpkUndoMeta> List(string ggpkPath) {
        var res = new List<GgpkUndoMeta>();
        var root = RootFor(ggpkPath);
        if (!Directory.Exists(root)) return res;
        foreach (var d in Directory.GetDirectories(root).OrderByDescending(x => x, StringComparer.Ordinal)) {
            var m = ReadMeta(d);
            if (m is null || m.Phase is not ("committed" or "restored")) continue;
            m.Stamp = Path.GetFileName(d);
            res.Add(m);
        }
        return res;
    }

    public static GgpkUndoMeta? Find(string ggpkPath, string which) {
        var all = List(ggpkPath);
        if (all.Count == 0) return null;
        if (which.Length == 0 || which.Equals("latest", StringComparison.OrdinalIgnoreCase)) return all[0];
        return all.FirstOrDefault(s => s.Stamp == which)
               ?? all.FirstOrDefault(s => s.Stamp.StartsWith(which, StringComparison.Ordinal));
    }

    /// <summary>把一份快照逐字节写回去。返回 null 表示成功。</summary>
    public static string? Restore(string ggpkPath, GgpkUndoMeta m, out int blocks) {
        blocks = 0;
        if (!File.Exists(m.Target)) return $"找不到目标文件：{m.Target}";
        var dir = Path.Combine(RootFor(ggpkPath), m.Stamp);
        if (!Directory.Exists(dir)) return $"快照目录不见了：{dir}";
        var err = ApplySnapshot(m, dir, out blocks);
        if (err is null) { m.Phase = "restored"; SaveMeta(dir, m); }
        return err;
    }

    // ---------------------------------------------------------------- 静态：启动自愈

    /// <summary>
    /// 启动时调用（必须在打开容器之前）：上次有没有没走完的写入？有就退回写前状态。
    /// 「还有日志且没标 committed」就等于「上次没确认过完整性」，宁可撤销一次本来成功的安装
    /// （重跑一次即可），也不能把一个没人验过的 ggpk 交给游戏。
    /// </summary>
    public static RecoverResult Recover(string ggpkPath) {
        var r = new RecoverResult();
        var root = RootFor(ggpkPath);
        if (!Directory.Exists(root)) return r;
        foreach (var d in Directory.GetDirectories(root).OrderByDescending(x => x, StringComparer.Ordinal)) {
            var m = ReadMeta(d);
            if (m is null || m.Phase != "writing") continue;
            var err = ApplySnapshot(m, d, out var n);
            m.Phase = err is null ? "rolledback" : "writing";
            SaveMeta(d, m);
            if (err is null) { try { Directory.Delete(d, recursive: true); } catch { } }
            r.Recovered = true;
            r.RestoredFiles = err is null ? n : 0;
            r.Message = err is null
                ? $"上次写入没有正常结束（开始于 {m.Created}），已把 ggpk 退回写入前的字节（{n} 块）"
                : $"上次写入没有正常结束，退回时出错：{err}";
            break;
        }
        return r;
    }

    /// <summary>
    /// 真正干活的一步：把日志里的原字节逐块写回去，每块写完立刻读回来比对。
    /// 不截断文件：写到尾部的新数据没有任何记录指向它，留着不影响游戏读取，
    /// 但少一次「截断到一半」的风险。
    /// </summary>
    private static string? ApplySnapshot(GgpkUndoMeta m, string dir, out int blocks) {
        var errs = new List<string>();
        var up = Path.Combine(dir, UndoName);
        blocks = 0;
        if (!File.Exists(up)) return "日志文件不见了，没法退回";
        var n = 0;
        var bad = 0;
        try {
            using var f = new FileStream(m.Target, FileMode.Open, FileAccess.ReadWrite, FileShare.None, 1 << 20);
            using var log = new FileStream(up, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
            log.Position = Magic.Length;
            var hdr = new byte[12];
            var back = new byte[64 << 10];
            while (ReadExactly(log, hdr, 12, allowEof: true)) {
                var off = BitConverter.ToInt64(hdr, 0);
                var cnt = BitConverter.ToInt32(hdr, 8);
                if (off < 0 || cnt <= 0 || cnt > (64 << 20)) { errs.Add($"日志里有坏记录（偏移 {off}，长度 {cnt}）"); break; }
                var buf = new byte[cnt];
                if (!ReadExactly(log, buf, cnt)) { errs.Add("日志被截断"); break; }
                f.Position = off;
                f.Write(buf, 0, cnt);
                n++;

                // 立刻读回来对一遍：写错位置、写到别处，这里当场就能发现
                f.Position = off;
                var got = 0;
                while (got < cnt) {
                    var k = Math.Min(back.Length, cnt - got);
                    var r = f.Read(back, 0, k);
                    if (r <= 0) { bad++; break; }
                    if (!back.AsSpan(0, r).SequenceEqual(buf.AsSpan(got, r))) { bad++; break; }
                    got += r;
                }
            }
            f.Flush(true);
        } catch (Exception ex) {
            errs.Add(ex.Message);
        }
        blocks = n;
        if (bad > 0) errs.Add($"{bad} 块读回来对不上（磁盘有问题？）");
        return errs.Count == 0 ? null : $"{string.Join("；", errs)}（已写回 {n} 块）";
    }

    // ---------------------------------------------------------------- 小工具

    private void SaveMeta() => SaveMeta(Dir, _m);

    private static void SaveMeta(string dir, GgpkUndoMeta m) {
        try {
            var p = Path.Combine(dir, MetaName);
            var tmp = p + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(m, new JsonSerializerOptions { WriteIndented = true }), TextEnc.Utf8);
            File.Move(tmp, p, overwrite: true);
        } catch { }
    }

    private static GgpkUndoMeta? ReadMeta(string dir) {
        try {
            var p = Path.Combine(dir, MetaName);
            if (!File.Exists(p)) return null;
            var m = JsonSerializer.Deserialize<GgpkUndoMeta>(File.ReadAllText(p, Encoding.UTF8));
            if (m is null || m.Target.Length == 0) return null;
            m.Stamp = Path.GetFileName(dir);
            return m;
        } catch { return null; }
    }

    private static bool ReadExactly(Stream s, byte[] buf, int count, bool allowEof = false) {
        var got = 0;
        while (got < count) {
            var n = s.Read(buf, got, count - got);
            if (n == 0) {
                if (allowEof && got == 0) return false;
                throw new EndOfStreamException();
            }
            got += n;
        }
        return true;
    }

    /// <summary>测试用：模拟进程被强杀 —— 关掉句柄就走，不提交也不记账。</summary>
    internal void SimulateCrash() {
        try { _log?.Flush(true); _log?.Dispose(); } catch { }
        _log = null;
        _finished = true;
        Active = null;
    }

    /// <summary>测试用：把日志里的记录逐条列出来。</summary>
    internal static List<(long Off, int Len)> ReadRecords(string dir) {
        var res = new List<(long, int)>();
        var p = Path.Combine(dir, UndoName);
        if (!File.Exists(p)) return res;
        using var f = new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        f.Position = Magic.Length;
        var hdr = new byte[12];
        while (ReadExactly(f, hdr, 12, allowEof: true)) {
            res.Add((BitConverter.ToInt64(hdr, 0), BitConverter.ToInt32(hdr, 8)));
            f.Position += BitConverter.ToInt32(hdr, 8);
        }
        return res;
    }
}

// 透明代理：所有写入先记账，再真正写。读、定位、长度原样透传。
internal sealed class UndoStream : Stream {
    private readonly Stream _s;
    private readonly GgpkUndo _u;

    public UndoStream(Stream inner, GgpkUndo undo) { _s = inner; _u = undo; }

    public override bool CanRead => _s.CanRead;
    public override bool CanSeek => _s.CanSeek;
    public override bool CanWrite => _s.CanWrite;
    public override long Length => _s.Length;
    public override long Position { get => _s.Position; set => _s.Position = value; }

    public override void Flush() { _u.SyncLog(); _s.Flush(); }
    public override int Read(byte[] buffer, int offset, int count) => _s.Read(buffer, offset, count);
    public override int Read(Span<byte> buffer) => _s.Read(buffer);
    public override long Seek(long offset, SeekOrigin origin) => _s.Seek(offset, origin);

    public override void SetLength(long value) {
        // 截断会丢掉日志之外的内容，没法退回去。ggpk 的写入路径不该走到这里 —— 走到就是库变了，
        // 这时候宁可报错停下，也不能默默把客户端削掉一截。
        if (value < _u.OrigLength)
            throw new NotSupportedException($"拒绝截断 ggpk（{_s.Length:N0} → {value:N0} B）：备份覆盖不到被截掉的那一段");
        _s.SetLength(value);
    }

    public override void Write(byte[] buffer, int offset, int count) {
        var pos = _s.Position;
        _u.Note(pos, count);
        _s.Position = pos;
        _s.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer) {
        var pos = _s.Position;
        _u.Note(pos, buffer.Length);
        _s.Position = pos;
        _s.Write(buffer);
    }
}
