// PoE 繁中改良汉化 · 拖放式补丁器
//
//   PoE-zh-Patcher.exe                       显示用法
//   PoE-zh-Patcher.exe <Content.ggpk>        交互菜单（拖放走这条）
//   PoE-zh-Patcher.exe <Content.ggpk> --set=translated|dual|dualall|original
//   PoE-zh-Patcher.exe <Content.ggpk> --status
//   PoE-zh-Patcher.exe <Content.ggpk> --selftest   校验数据与机器上的文件是否对得上
//
// 设计要点：内嵌 bundle.gz（行级结构），运行时从 ggpk 当前内容原地改写目标行，
// 不做整文件替换，所以三套 flavor 之间可以任意来回切，不会残留。
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LibBundledGGPK3;
using LibBundle3;
using Idx = LibBundle3.Index;

namespace PoeZhPatcher;

internal enum Flavor { Original, Translated, Dual, DualAll, Custom, Unknown }

// 标记层 / 文本层分离。
//
// statdesc 的引号内文本混着两类东西：标记层（<enchanted> / <rgb(…)>{{ }}）
// 与文本层（其余字符，含 {0} 这类占位符）。译文只对文本层负责。
// 客户端那份文件即使被第三方改过标记（换色、删标签），也能靠 Merge 把译文
// 叠上去而一个标记都不动 —— 这就是 Flavor.Custom 的依据。
internal static class Markup {
    private const int MaxTag = 40;

    private static bool IsMarkupAt(string s, int i, out int len) {
        len = 0;
        if (i + 1 < s.Length && ((s[i] == '{' && s[i + 1] == '{') || (s[i] == '}' && s[i + 1] == '}'))) {
            len = 2;
            return true;
        }
        if (s[i] == '<') {
            for (var k = i + 1; k < s.Length && k - i <= MaxTag; k++) {
                if (s[k] == '<') return false;
                if (s[k] == '>') { len = k - i + 1; return true; }
            }
        }
        return false;
    }

    private static List<(bool mk, string seg)> Tokens(string s) {
        var list = new List<(bool, string)>();
        var i = 0;
        while (i < s.Length) {
            if (IsMarkupAt(s, i, out var len)) {
                list.Add((true, s.Substring(i, len)));
                i += len;
                continue;
            }
            var j = i;
            while (j < s.Length && !IsMarkupAt(s, j, out _)) j++;
            list.Add((false, s.Substring(i, j - i)));
            i = j;
        }
        return list;
    }

    public static string Core(string s) {
        var sb = new StringBuilder(s.Length);
        foreach (var (mk, seg) in Tokens(s)) if (!mk) sb.Append(seg);
        return sb.ToString();
    }

    private static string Seq(string s) {
        var sb = new StringBuilder();
        foreach (var (mk, seg) in Tokens(s)) if (mk) sb.Append('\u0001').Append(seg);
        return sb.ToString();
    }

    public static bool SameMarkup(string a, string b) => Seq(a) == Seq(b);

    /// 这一行一个标记都没有（原版有、这里没有 = 被整段删光）
    public static bool IsBare(string s) => Seq(s).Length == 0;

    // 把 old→new 的文本改动叠到 shell 上，shell 的标记层原样保留。
    //
    // 标记层三分法：
    //   1 对方留着标记（换了颜色/标签）→ 沿用对方的，一个字节都不动
    //   2 对方整段删光（原版有、现在一个标记都没有）→ 判为机械替换的破坏，还原原版标记
    //   3 双方一致 → 原样
    public static string Merge(string shell, string oldText, string newText, bool restoreBare = true) {
        var cn = Core(shell);
        var co = Core(oldText);
        var ct = Core(newText);
        if (cn != co)
            throw new InvalidDataException(
                $"这一行的文本层与数据包原文不符，拒绝合并\n    当前：{Clip(cn)}\n    原文：{Clip(co)}");
        if (restoreBare && IsBare(shell) && !IsBare(oldText))
            return newText;                 // 覆盖伤：按原版标记重建
        // 底稿的标记与原文逐段相同 = 没被任何人动过，old→new 的差量（含标记层改动，
        // 比如修官方笔误时补回漏掉的 }}）可以整套套用。
        // 依据：标记序列 + 文本层都相同 → 两个字符串必然完全相同。
        if (SameMarkup(shell, oldText)) return newText;
        if (co == ct || cn == ct) return shell;

        var ops = Opcodes(co, ct);

        // shell 里每个位置是否属于文本层
        var isLit = new List<bool>(shell.Length);
        foreach (var (mk, seg) in Tokens(shell)) for (var k = 0; k < seg.Length; k++) isLit.Add(!mk);

        var sb = new StringBuilder(shell.Length + 32);
        int i = 0, ci = 0, n = shell.Length;

        void Flush() {
            while (i < n && !isLit[i]) { sb.Append(shell[i]); i++; }
        }

        foreach (var (tag, i1, i2, j1, j2) in ops) {
            while (ci < i1) { Flush(); sb.Append(shell[i]); i++; ci++; }
            if (tag == "equal") continue;
            if (tag == "insert") {
                // 插入落在当前位置、且不抢走后面的标记（"秒}}" 的追加要进到 }} 里面）
                if (i1 == 0) Flush();
                sb.Append(ct.Substring(j1, j2 - j1));
                continue;
            }
            sb.Append(ct.Substring(j1, j2 - j1));
            while (ci < i2) { Flush(); i++; ci++; }
        }
        while (i < n) { sb.Append(shell[i]); i++; }
        return sb.ToString();
    }

    private static string Clip(string s) => s.Length <= 60 ? s : s[..60] + "…";

    private static List<(string tag, int i1, int i2, int j1, int j2)> Opcodes(string a, string b) {
        int n = a.Length, m = b.Length, p = 0;
        while (p < n && p < m && a[p] == b[p]) p++;
        int s = 0;
        while (s < n - p && s < m - p && a[n - 1 - s] == b[m - 1 - s]) s++;
        var midA = a.Substring(p, n - s - p);
        var midB = b.Substring(p, m - s - p);
        var ops = new List<(string, int, int, int, int)>();
        if (p > 0) ops.Add(("equal", 0, p, 0, p));
        if (midA.Length == 0 && midB.Length == 0) { }
        else if (midA.Length == 0) ops.Add(("insert", p, p, p, p + midB.Length));
        else if (midB.Length == 0) ops.Add(("delete", p, p + midA.Length, p, p));
        else ops.AddRange(LcsOps(midA, midB, p, p));
        if (s > 0) ops.Add(("equal", n - s, n, m - s, m));
        return ops;
    }

    private static List<(string tag, int i1, int i2, int j1, int j2)> LcsOps(string a, string b, int oa, int ob) {
        int n = a.Length, m = b.Length;
        var dp = new int[n + 1, m + 1];
        for (var i = n - 1; i >= 0; i--)
            for (var j = m - 1; j >= 0; j--)
                dp[i, j] = a[i] == b[j] ? dp[i + 1, j + 1] + 1 : Math.Max(dp[i + 1, j], dp[i, j + 1]);
        var ops = new List<(string, int, int, int, int)>();
        int x = 0, y = 0;
        while (x < n && y < m) {
            if (a[x] == b[y]) { x++; y++; continue; }
            if (dp[x + 1, y] >= dp[x, y + 1]) {
                var k = x;
                while (x < n && (y >= m || dp[x + 1, y] >= dp[x, y + 1])) x++;
                ops.Add(("delete", oa + k, oa + x, ob + y, ob + y));
            } else {
                var k = y;
                while (y < m && (x >= n || dp[x, y + 1] > dp[x + 1, y])) y++;
                ops.Add(("insert", oa + x, oa + x, ob + k, ob + y));
            }
        }
        if (x < n) ops.Add(("delete", oa + x, oa + n, ob + y, ob + y));
        if (y < m) ops.Add(("insert", oa + x, oa + x, ob + y, ob + m));
        // 相邻的 delete+insert 合成 replace，保证单调递增
        ops.Sort((u, v) => u.Item2 != v.Item2 ? u.Item2.CompareTo(v.Item2) : u.Item4.CompareTo(v.Item4));
        var merged = new List<(string, int, int, int, int)>();
        foreach (var op in ops) {
            if (merged.Count > 0) {
                var pv = merged[^1];
                if (op.Item2 == pv.Item3 && op.Item4 == pv.Item5 && (pv.Item1 == "delete" || op.Item1 == "insert")) {
                    merged[^1] = ("replace", pv.Item2, op.Item3, pv.Item4, op.Item5);
                    continue;
                }
            }
            merged.Add(op);
        }
        return merged;
    }
}

internal sealed class Row {
    public int No;
    public string O = "";
    public string? T;
    public string? D;
    public string? E;
    // 上一版补丁写过、这一版已经换掉的文本。玩家升级补丁器后容器里留着旧字节，
    // sha 认不出，但逐行文本能认出 —— 认出才敢覆盖，否则会判「未知」整体拒写。
    public string[] Legacy = Array.Empty<string>();
}

internal sealed class SdEntry {
    public string Path = "";
    public string ShaOriginal = "", ShaDualAll = "";
    public string? ShaTranslated, ShaDual;
    public List<Row> Rows = new();
    // 基线文件的行数 / 字节数：采集诊断时用来判断客户端那份是否整体错位
    public int BaseLines;
    public long BaseBytes;
}

internal sealed class DatEntry {
    public string Path = "", Table = "", ShaOrig = "", ShaPatch = "";
    public byte[] Orig = Array.Empty<byte>(), Patch = Array.Empty<byte>();
    public List<DatEdit> Edits = new();
}

internal readonly struct DatEdit {
    public readonly int Row, Rel;
    public readonly string Old, New;
    public DatEdit(int row, int rel, string oldText, string newText) { Row = row; Rel = rel; Old = oldText; New = newText; }
}

// ---------------------------------------------------------------- dat 表：叠到别人的底稿上
//
// .datc64 没法像 statdesc 那样整行文本重写：行区是定长的二进制，文本在池里。
// 所以合并只做两件事 —— 把新串追加到池尾，把目标槽位的 8 字节指针改过去。
// 行区与旧池逐字节不动，别人改过的行（换色、别的译文）一处都不碰。
//
// 能不能合，看两条：结构一致（行数 / 行宽 / 池起点），且每个要动的槽位，
// 现存文本要么是原文、要么已经是我们的译文。缺一条就退回「未知」，整体拒写。
internal static class DatMerge {
    public sealed class View {
        public byte[] Raw = Array.Empty<byte>();
        public int RowCount, RowSize, PoolBase;
    }

    public static View Load(byte[] raw, string name) {
        if (raw.Length < 12) throw new InvalidDataException($"{name}：文件过短");
        var rows = BinaryPrimitives.ReadUInt32LittleEndian(raw);
        if (rows == 0) return new View { Raw = raw, RowCount = 0, RowSize = 0, PoolBase = 4 };
        var rc = checked((int)rows);
        var rs = FindRowSize(raw, rc);
        return new View { Raw = raw, RowCount = rc, RowSize = rs, PoolBase = 4 + rc * rs };
    }

    // 行宽没写在文件头里：从 1 试到 2048，看 4 + 行数×行宽 处是否正好是
    // 8 个 0xBB（字符串池标记）。多个候选取行宽最大的，避免把行数据当池起点。
    private static int FindRowSize(byte[] raw, int rows, int limit = 2048) {
        var best = -1;
        for (var rs = 1; rs < limit; rs++) {
            var b = 4L + (long)rows * rs;
            if (b + 8 > raw.Length) break;
            var hit = true;
            for (var k = 0; k < 8 && hit; k++) hit = raw[b + k] == 0xBB;
            if (hit) best = rs;
        }
        if (best < 0) throw new InvalidDataException($"未找到字符串池标记（{raw.Length:N0} 字节, {rows} 行）");
        return best;
    }

    private static ulong U64(View v, int row, int rel) =>
        BinaryPrimitives.ReadUInt64LittleEndian(v.Raw.AsSpan(4 + row * v.RowSize + rel));

    // 合法串起点：池首，或紧跟在上一条终止符之后（前两字节为 0）
    private static bool IsStart(View v, int pos) {
        if (pos == v.PoolBase + 8) return true;
        if (pos < 4) return false;
        return v.Raw[pos - 2] == 0 && v.Raw[pos - 1] == 0;
    }

    public static string? ReadAt(View v, long posL) {
        var raw = v.Raw;
        if (posL < 0 || posL >= raw.Length - 1) return null;
        var pos = (int)posL;
        if ((pos & 1) != 0 || !IsStart(v, pos)) return null;
        var end = pos;
        while (end + 1 < raw.Length && !(raw[end] == 0 && raw[end + 1] == 0)) end += 2;
        if (end + 1 >= raw.Length) return null;
        var s = Encoding.Unicode.GetString(raw, pos, end - pos);
        foreach (var c in s) if (c < 32 && c != '\t' && c != '\n' && c != '\r') return null;
        return s;
    }

    // 指针与真实串起点偶尔有 ±2 偏差（同一串在池里留有旧副本），精确失败就两侧各试一次
    public static string? StringField(View v, int row, int rel) {
        if (row < 0 || row >= v.RowCount || rel + 8 > v.RowSize) return null;
        var off = U64(v, row, rel);
        if (off < 8) return "";
        foreach (var d in new[] { 0, -2, 2 }) {
            var s = ReadAt(v, v.PoolBase + (long)off + d);
            if (s is null) continue;
            if (d != 0 && s.Length == 0) continue;
            return s;
        }
        return null;
    }

    private static string Norm(string s) => s.Replace("\r\n", "\n");

    private static string Short(string s) {
        s = s.Replace("\n", "\\n");
        return s.Length <= 60 ? s : s[..60] + "…";
    }

    public static bool TryCustomBase(DatEntry e, byte[] cur, out string why) {
        why = "";
        if (e.Edits.Count == 0) { why = "数据包里没有这个表的逐行改动，无法合并"; return false; }
        View v, b;
        try { v = Load(cur, e.Table); } catch (Exception ex) { why = ex.Message; return false; }
        try { b = Load(e.Orig, e.Table + "（包内原文）"); } catch (Exception ex) { why = "包内原文读不出：" + ex.Message; return false; }
        if (v.RowCount != b.RowCount) { why = $"行数 {v.RowCount:N0} ≠ 基线 {b.RowCount:N0}"; return false; }
        if (v.RowSize != b.RowSize) { why = $"行宽 {v.RowSize} ≠ 基线 {b.RowSize}"; return false; }
        var done = 0;
        foreach (var ed in e.Edits) {
            var s = StringField(v, ed.Row, ed.Rel);
            if (s is null) { why = $"行 {ed.Row} 槽位 {ed.Rel} 读不出字符串"; return false; }
            var t = Norm(s);
            if (t == Norm(ed.New)) { done++; continue; }
            if (t != Norm(ed.Old)) { why = $"行 {ed.Row} 文本已变：{Short(t)}"; return false; }
        }
        why = done == 0 ? "结构与待改行都一致" : $"结构与待改行都一致；{done}/{e.Edits.Count} 行已是本补丁文本";
        return true;
    }

    /// revert=true 表示把我们的文本改回原文（保留对方自己的改动），用于「还原官方原文」
    public static byte[] Apply(DatEntry e, byte[] cur, bool revert) {
        var t = Load(cur, e.Table);
        var buf = new List<byte>(cur.Length + 8192);
        buf.AddRange(cur);
        if (buf.Count % 2 != 0) buf.Add(0);
        if (buf.Count < 2 || buf[^1] != 0 || buf[^2] != 0) { buf.Add(0); buf.Add(0); }

        var plan = new List<(DatEdit Ed, string To)>();
        foreach (var ed in e.Edits) {
            var s = StringField(t, ed.Row, ed.Rel);
            if (s is null) throw new InvalidDataException($"{e.Table} 行 {ed.Row} 槽位 {ed.Rel} 读不出字符串");
            var from = revert ? ed.New : ed.Old;
            var to = revert ? ed.Old : ed.New;
            var now = Norm(s);
            if (now == Norm(to)) continue;                     // 已经是目标文本
            if (now != Norm(from)) throw new InvalidDataException($"{e.Table} 行 {ed.Row} 现值与预期不符：{Short(now)}");

            // 追加前补 2 个 0：原文件末尾常有「空串哨兵」的指针正好等于池长，
            // 不补的话它会读成我们新写的串，等于凭空改了别的行。
            buf.Add(0); buf.Add(0);
            var pos = buf.Count;
            buf.AddRange(Encoding.Unicode.GetBytes(to));
            buf.Add(0); buf.Add(0);
            while (buf.Count % 2 != 0) buf.Add(0);

            var slot = 4 + ed.Row * t.RowSize + ed.Rel;
            BinaryPrimitives.WriteUInt64LittleEndian(CollectionsMarshal.AsSpan(buf).Slice(slot, 8), (ulong)(pos - t.PoolBase));
            plan.Add((ed, to));
        }

        var outBytes = buf.ToArray();
        Verify(cur, outBytes, t, plan);
        return outBytes;
    }

    // 三重自检：结构不变 / 目标槽位回读正确 / 原文件范围内只有被改的 8 字节指针不同
    private static void Verify(byte[] before, byte[] after, View t, List<(DatEdit Ed, string To)> plan) {
        var t2 = Load(after, "verify");
        if (t2.RowCount != t.RowCount || t2.RowSize != t.RowSize || t2.PoolBase != t.PoolBase)
            throw new InvalidDataException("合并后结构改变");
        foreach (var (ed, to) in plan)
            if (StringField(t2, ed.Row, ed.Rel) != to)
                throw new InvalidDataException($"合并后回读不符：行 {ed.Row} 槽位 {ed.Rel}");
        var allowed = new HashSet<int>();
        foreach (var (ed, _) in plan) {
            var slot = 4 + ed.Row * t.RowSize + ed.Rel;
            for (var k = 0; k < 8; k++) allowed.Add(slot + k);
        }
        for (var i = 0; i < before.Length; i++)
            if (before[i] != after[i] && !allowed.Contains(i))
                throw new InvalidDataException($"合并时意外改动了偏移 {i} 的字节");
    }
}

// 目标容器。官方安装包是 Content.ggpk（单文件 ggpk），Steam / WeGame 版是
// Bundles2 目录（_.index.bin + 一堆 *.bundle.bin）。两者对外都只是「路径 → 字节」，
// 所以抽一层，预检 / 改写 / 自检这些上层逻辑完全不区分。
internal interface IContainer : IDisposable {
    string Kind { get; }        // "ggpk" / "Bundles2"
    string Display { get; }     // 展示用路径
    string RootDir { get; }     // 游戏根目录
    long Size { get; }
    bool TryRead(string path, out byte[] bytes);
    bool TryRead(string path, out byte[] bytes, out string? error);   // error 非空 = 路径在但数据读不出来
    bool Write(string path, byte[] bytes);
    void Flush();               // 一次落盘；ggpk 与 Bundles2 都是收起改动后统一存
    bool IsSelfBundle(string path);            // 这个文件是不是挂在历史自建 bundle 上
    void Detach();                             // 换一条全新命名的自建 bundle 承接本次写入
    string? LastBundle { get; }                // 本次新建的自建 bundle 文件名（回滚时要删它）
    void DumpBundles(bool all);                // 诊断：列出索引里的 bundle 记录
    bool Alive { get; }         // 容器还活着吗（采集/演练会中途换容器，旧句柄不能再写）
    string IndexFile { get; }   // 索引所在的物理文件：Bundles2 是 _.index.bin，ggpk 是 Content.ggpk
    void TrimAfterCommit();     // 提交之后才清孤儿 bundle（先删文件再回滚 = 把记录变成悬空）
    GgpkUndo? Undo { get; }     // ggpk 的逐字节撤销日志；Bundles2 没有（它整份复制索引就够）
}

internal sealed class GgpkContainer : IContainer {
    private readonly BundledGGPK _g;
    private readonly FileStream? _fs;
    private readonly GgpkUndo? _undo;

    public GgpkContainer(string path) {
        Display = path;
        var full = System.IO.Path.GetFullPath(path);

        // 自己开流再包一层「写入前记账」的代理：库的所有写入都会先落到 Undo 里，
        // 然后才真正改动文件。这一层就是 74 GB 单文件能全身而退的原因。
        var fs = new FileStream(full, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, 1 << 16, FileOptions.None);
        var undo = GgpkUndo.ForOpen(full);
        try {
            _g = new BundledGGPK(undo.Wrap(fs), leaveOpen: true, parsePathsInIndex: true);
            _fs = fs;
            _undo = undo;
        } catch (Exception ex) {
            // 库改版、不接受自建流：退回原来的开法。这样只读功能照旧，
            // 但这次没有逐字节还原点 —— 上层会因为拿不到还原点而拒绝写入。
            Console.WriteLine($"  ! 接管 ggpk 写入通道失败（{ex.GetType().Name}: {ex.Message}）");
            Console.WriteLine("    这次没法给 ggpk 留还原点，安装会被拒绝；--status / --backups 这类只读功能不受影响。");
            try { fs.Dispose(); } catch { }
            undo.Abandon();
            _undo = null;
            _fs = null;
            _g = new BundledGGPK(full, true);
        }
        Size = new FileInfo(full).Length;
    }


    public string Kind => "ggpk";
    public string Display { get; }
    public string RootDir => Path.GetDirectoryName(Display)!;
    public long Size { get; }

    public bool TryRead(string p, out byte[] bytes) => TryRead(p, out bytes, out _);

    public bool TryRead(string p, out byte[] bytes, out string? error) {
        bytes = Array.Empty<byte>();
        error = null;
        if (!_g.Index.TryGetFile(p, out var rec)) return false;
        try { bytes = rec.Read().ToArray(); return true; }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    public bool Write(string p, byte[] bytes) {
        if (!_g.Index.TryGetFile(p, out var rec)) return false;
        rec.Write(bytes, saveIndex: false);   // 统一在 Flush 里存一次。逐个存会把 20 MB 索引反复重写进 ggpk，
        return true;                          // 每次都要腾/让空闲块，历史上就是这么把自建 bundle 弄没的
    }

    public void Flush() {
        BundleHygiene.DropEmpty(_g.Index);
        _g.Index.Save();
    }
    public bool IsSelfBundle(string p) =>
        _g.Index.TryGetFile(p, out var rec) && BundleHygiene.IsSelf(rec);
    public string? LastBundle { get; private set; }
    public GgpkUndo? Undo => _undo;
    public string IndexFile => Display;
    // ggpk 的写入直接在容器内腾空闲块，没有独立的 bundle 文件要清
    public void TrimAfterCommit() { }
    public void DumpBundles(bool all) => BundleHygiene.Dump(_g.Index, all);
    public void Detach() {
        var r = BundleHygiene.Detach(_g.Index);
        LastBundle = r.Name.Length > 0 ? r.Name : null;
        if (r.Retired > 0) Console.WriteLine($"  自建 bundle 换新：{r.Name}（旧记录 {r.Retired} 条不再使用）");
        if (r.Error is not null) Console.WriteLine($"  ! 无法新建自建 bundle：{r.Error}");
    }
    private bool _dead;
    public bool Alive => !_dead;
    public void Dispose() {
        if (_dead) return;
        _dead = true;
        try { _g.Dispose(); } catch { }
        try { _fs?.Dispose(); } catch { }
        _undo?.TrimIfEmpty();       // 只看状态、没写过：把空日志删掉
    }
}

internal sealed class BundlesContainer : IContainer {
    private readonly Idx _index;

    public BundlesContainer(string indexPath) {
        IndexPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(indexPath))!, "_.index.bin");
        RootDir = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetFullPath(indexPath)))!;
        _index = new Idx(IndexPath, parsePaths: false, bundleFactory: new DriveBundleFactory(Path.GetDirectoryName(IndexPath)!));
        Size = new FileInfo(IndexPath).Length;
    }

    public string IndexPath { get; }
    public string Kind => "Bundles2";
    public string Display => IndexPath;
    public string RootDir { get; }
    public long Size { get; }

    public bool TryRead(string p, out byte[] bytes) => TryRead(p, out bytes, out _);

    public bool TryRead(string p, out byte[] bytes, out string? error) {
        bytes = Array.Empty<byte>();
        error = null;
        if (!_index.TryGetFile(p, out var rec)) return false;
        try { bytes = rec.Read().ToArray(); return true; }
        catch (Exception ex) { error = ex.Message; return false; }   // bundle 缺失 / 损坏
    }

    public bool Write(string p, byte[] bytes) {
        if (!_index.TryGetFile(p, out var rec)) return false;
        rec.Write(bytes, saveIndex: false);   // 统一在 Flush 里存一次 index
        return true;
    }

    public void Flush() {
        BundleHygiene.DropEmpty(_index);
        _index.Save();
    }
    public bool IsSelfBundle(string p) =>
        _index.TryGetFile(p, out var rec) && BundleHygiene.IsSelf(rec);
    public string? LastBundle { get; private set; }
    public string IndexFile => IndexPath;
    // 必须在「重新打开校验通过」之后再清：先删文件、再回滚索引，那些记录就变成悬空的，
    // 游戏读到它们就是一条读不出来的文件。顺序反过来才是安全的。
    public void TrimAfterCommit() =>
        BundleHygiene.TrimOrphans(_index, System.IO.Path.Combine(System.IO.Path.GetDirectoryName(IndexPath)!, "LibGGPK3"));
    public GgpkUndo? Undo => null;       // Bundles2 整份复制 _.index.bin 就够，不需要逐字节日志
    public void DumpBundles(bool all) => BundleHygiene.Dump(_index, all);
    public void Detach() {
        var r = BundleHygiene.Detach(_index);
        LastBundle = r.Name.Length > 0 ? r.Name : null;
        if (r.Retired > 0) Console.WriteLine($"  自建 bundle 换新：{r.Name}（旧记录 {r.Retired} 条不再使用）");
        if (r.Error is not null) Console.WriteLine($"  ! 无法新建自建 bundle：{r.Error}");
    }
    private bool _dead;
    public bool Alive => !_dead;
    public void Dispose() {
        if (_dead) return;
        _dead = true;
        _index.Dispose();
    }
}

internal static class Program {
    private const string Indent = "  ";
    private static readonly Dictionary<string, string> Args = new(StringComparer.OrdinalIgnoreCase);
    private static string _target = "";      // Content.ggpk 或 Bundles2\_.index.bin
    private static bool _restoreBare = true;   // 底稿标记被整段删光时补回官方标记（--keep-markup 关闭）
    private static string _ver = "?";
    private static string _bundleBuilt = "?";
    private static string _gameClient = "?", _gamePatch = "", _gameLocked = "";
    private static List<SdEntry> _sd = new();
    private static List<DatEntry> _dat = new();

    private static int Main(string[] raw) {
        Console.OutputEncoding = Encoding.UTF8;
        try { Console.Title = "PoE 繁中改良汉化"; } catch { }

        var positional = new List<string>();
        foreach (var a in raw) {
            if (a.StartsWith("--", StringComparison.Ordinal)) {
                var body = a[2..];
                var eq = body.IndexOf('=');
                if (eq < 0) Args[body] = "";
                else Args[body[..eq]] = body[(eq + 1)..];
            } else positional.Add(a);
        }

        LoadBundle();

        _restoreBare = !Args.ContainsKey("keep-markup");

        // 标记层合并自检：与 tools/markup.py 的用例逐条对齐，防止两侧实现漂移
        if (Args.ContainsKey("markup-test")) return MarkupTest();
        // ggpk 逐字节还原自检：不碰真实客户端，在临时目录里造一份「同结构」的文件跑
        if (Args.ContainsKey("undo-test")) return UndoTest();
        // 外部改过的底稿演练：拿一份现成文件跑 TryCustomBase + Rewrite，只读不写
        if (Args.TryGetValue("sd-test", out var sdArg) && sdArg.Length > 0) return SdTest(sdArg);
        // 外部改过的数据表演练：拿一份现成 .datc64 跑 TryCustomBase + Apply，只读不写
        if (Args.TryGetValue("dat-test", out var datArg) && datArg.Length > 0) return DatTest(datArg);
        // 数据包自检：不需要游戏，拿官方基线文件把每条重新生成一遍，比对发布产物 sha
        if (Args.TryGetValue("data-test", out var dataArg) && dataArg.Length > 0) return DataTest(dataArg);
        // 语系 / 国旗切换的往返自检：拿一份现成文件跑「启用 → 还原」，比对是否逐字节回原样
        if (Args.TryGetValue("lang-test", out var langT) && langT.Length > 0) return LangTest(langT);
        if (Args.TryGetValue("flag-test", out var flagT) && flagT.Length > 0) return FlagTest(flagT);

        if (positional.Count == 0) {
            var auto = FindDefaultGgpk();
            if (auto is null) {
                PrintUsage();
                Pause();
                return 0;
            }
            Console.WriteLine($"自动找到游戏：{auto}");
            Console.WriteLine();
            positional.Add(auto);
        }

        var gp = ResolveTarget(positional[0]);
        if (gp is null) {
            Console.WriteLine($"这不像是 PoE 的数据：{positional[0]}");
            Console.WriteLine("  可以是 Content.ggpk，也可以是 Bundles2 目录 / 其中的 _.index.bin。");
            Pause();
            return 1;
        }
        _target = gp;

        // 上次写入没走完？先把客户端还原到写前状态，再谈别的。
        // 必须在打开容器之前做：回滚要覆盖 _.index.bin / Content.ggpk，句柄被占着就换不回去。
        var rec = GgpkUndo.IsGgpk(gp) ? GgpkUndo.Recover(gp) : Txn.Recover(gp);
        if (rec.Recovered) {
            Console.WriteLine();
            Console.WriteLine("==============================================================");
            Console.WriteLine($" 检测到上次写入没有正常结束：{rec.Message}");
            Console.WriteLine($" 已回滚到写入前的状态{(rec.RestoredFiles > 0 ? $"（涉及 {rec.RestoredFiles} 个文件）" : "")}，客户端可以正常启动。");
            Console.WriteLine(" 直接重新运行本程序即可，不用先验证游戏完整性。");
            Console.WriteLine("==============================================================");
            Console.WriteLine();
        }

        // 写入通道演练：在临时目录的索引副本上写一遍，不碰真实客户端
        if (Args.ContainsKey("writetest")) return WriteTest(Args.GetValueOrDefault("writetest") ?? "");

        // 把整个 ggpk 退回某次安装之前：不打开容器，文件要独占写句柄
        if (Args.TryGetValue("restore-ggpk", out var rgArg)) {
            if (!GgpkUndo.IsGgpk(gp)) {
                Console.WriteLine("--restore-ggpk 只对官方独立端的 Content.ggpk 有效。");
                Console.WriteLine("  Steam/WeGame 的 Bundles2 请用 --restore-backup（它整份复制 _.index.bin，还原更彻底）。");
                return 1;
            }
            return RestoreGgpk(gp, rgArg.Length == 0 ? "latest" : rgArg, ask: !Args.ContainsKey("yes"));
        }

        var store = OpenContainer();
        if (store is null) { Pause(); return 1; }
        try {

        // --locale-only[=original]：只处理语系与国旗，不碰任何译文。
        // 译文早就装好、只差「游戏里还是英文」的时候用它，也方便单独验证这两步。
        if (Args.TryGetValue("locale-only", out var locOnly)) {
            var lt = locOnly.Trim().Equals("original", StringComparison.OrdinalIgnoreCase) ? Flavor.Original : Flavor.DualAll;
            var nf = ApplyLocale(store, lt);
            Console.WriteLine();
            Console.WriteLine(nf == 0 ? "语系与国旗处理完成。" : $"语系与国旗有 {nf} 处失败。");
            return nf == 0 ? 0 : 3;
        }

        var prog = Args.GetValueOrDefault("set");
        if (prog is not null) {
            var f = ParseFlavor(prog);
            if (f is null) { Console.WriteLine($"未知版本名：{prog}（translated / dual / dualall / original）"); return 1; }
            return RunInstall(store, f.Value);
        }
        if (Args.ContainsKey("status")) return ShowStatus(store);
        // 诊断：索引里的 bundle 记录长什么样（自建记录全列，--all 连游戏自带的也列）
        if (Args.ContainsKey("bundles")) {
            store.DumpBundles(Args.GetValueOrDefault("bundles") is "all");
            return 0;
        }
        if (Args.ContainsKey("detach-test")) {
            store.Detach();
            store.DumpBundles(false);
            return 0;
        }
        // 备份：安装时被覆盖掉的「原字节」都留在这里，随时可以整份写回去
        if (Args.ContainsKey("backups")) return ShowBackups(store);
        if (Args.TryGetValue("restore-backup", out var rbArg)) return RestoreBackup(store, rbArg);
        // 把容器里的文件原样导出来看（排查结构问题用，只读不写）
        if (Args.TryGetValue("dump", out var dumpArg) && dumpArg.Length > 0) return Dump(store, dumpArg);
        if (Args.ContainsKey("selftest")) return SelfTest(store);
        if (Args.ContainsKey("struct")) return StructTest(store, Preflight(store));
        if (Args.ContainsKey("collect")) return Collect(store, Preflight(store));

        // 拖放进来的默认路径：交互菜单（菜单返回即代表用户选了退出，不再多要一次按键）
        return Menu(store);
        } finally { CloseStore(); }
    }

    // ---------------------------------------------------------------- 数据包

    private static void LoadBundle() {
        var asm = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("bundle.gz", StringComparison.Ordinal))
                   ?? throw new InvalidOperationException("内嵌数据包缺失");
        using var s = asm.GetManifestResourceStream(name)!;
        using var gz = new GZipStream(s, CompressionMode.Decompress);
        using var ms = new MemoryStream();
        gz.CopyTo(ms);

        using var doc = JsonDocument.Parse(ms.ToArray());
        var root = doc.RootElement;
        _ver = root.GetProperty("version").GetString() ?? "?";
        _bundleBuilt = root.GetProperty("built_at").GetString() ?? "?";
        var game = root.GetProperty("game");
        _gameClient = game.GetProperty("client_version").GetString() ?? "?";
        _gamePatch = game.GetProperty("patch_version").GetString() ?? "";
        _gameLocked = game.GetProperty("locked_at").GetString() ?? "";

        foreach (var e in root.GetProperty("statdesc").EnumerateArray()) {
            var ent = new SdEntry {
                Path = e.GetProperty("path").GetString()!,
                ShaOriginal = e.GetProperty("sha").GetProperty("original").GetString()!,
                ShaDualAll = e.GetProperty("sha").GetProperty("dualall").GetString()!,
            };
            var sh = e.GetProperty("sha");
            if (sh.GetProperty("translated").ValueKind == JsonValueKind.String) ent.ShaTranslated = sh.GetProperty("translated").GetString();
            if (sh.GetProperty("dual").ValueKind == JsonValueKind.String) ent.ShaDual = sh.GetProperty("dual").GetString();
            if (e.TryGetProperty("lines", out var ln) && ln.ValueKind == JsonValueKind.Number) ent.BaseLines = ln.GetInt32();
            if (e.TryGetProperty("bytes", out var by) && by.ValueKind == JsonValueKind.Number) ent.BaseBytes = by.GetInt64();
            foreach (var r in e.GetProperty("rows").EnumerateArray()) {
                var arr = r.EnumerateArray().ToArray();
                ent.Rows.Add(new Row {
                    No = arr[0].GetInt32(),
                    O = arr[1].GetString() ?? "",
                    T = arr[2].ValueKind == JsonValueKind.String ? arr[2].GetString() : null,
                    D = arr[3].ValueKind == JsonValueKind.String ? arr[3].GetString() : null,
                    E = arr[4].ValueKind == JsonValueKind.String ? arr[4].GetString() : null,
                });
            }
            if (e.TryGetProperty("legacy", out var lg) && lg.ValueKind == JsonValueKind.Object) {
                foreach (var prop in lg.EnumerateObject()) {
                    if (!int.TryParse(prop.Name, out var no)) continue;
                    var row = ent.Rows.FirstOrDefault(x => x.No == no);
                    if (row is null) continue;
                    row.Legacy = prop.Value.EnumerateArray()
                        .Select(v => v.GetString() ?? "").Where(s => s.Length > 0).ToArray();
                }
            }
            _sd.Add(ent);
        }

        foreach (var e in root.GetProperty("dat").EnumerateArray()) {
            var ent = new DatEntry {
                Path = e.GetProperty("path").GetString()!,
                Table = e.GetProperty("table").GetString()!,
                ShaOrig = e.GetProperty("sha_original").GetString()!,
                ShaPatch = e.GetProperty("sha_patched").GetString()!,
                Orig = Convert.FromBase64String(e.GetProperty("orig").GetString()!),
                Patch = Convert.FromBase64String(e.GetProperty("patch").GetString()!),
            };
            if (e.TryGetProperty("edits", out var eds) && eds.ValueKind == JsonValueKind.Array) {
                foreach (var arr in eds.EnumerateArray()) {
                    if (arr.GetArrayLength() < 4) continue;
                    ent.Edits.Add(new DatEdit(
                        arr[0].GetInt32(), arr[1].GetInt32(),
                        arr[2].GetString() ?? "", arr[3].GetString() ?? ""));
                }
            }
            _dat.Add(ent);
        }
    }

    // 语系切换自检：拿 data/languages.dat 的一份实文件，跑「启用 → 还原」，要求逐字节回到原样
    private static int LangTest(string path) {
        if (!File.Exists(path)) { Console.WriteLine($"找不到文件：{path}"); return 1; }
        var cur = File.ReadAllBytes(path);
        Console.WriteLine($"languages.dat  {cur.Length:N0} B");
        var st = LanguageSwitch.Detect(cur);
        Console.WriteLine($"  当前    {LanguageSwitch.Describe(st)}");
        if (st == LanguageSwitch.State.Unknown) return 5;

        var on = LanguageSwitch.Swap(cur);
        if (on is null) { Console.WriteLine("  ✗ 对调失败（定位不到两行）"); return 5; }
        var st2 = LanguageSwitch.Detect(on);
        Console.WriteLine($"  启用后  {LanguageSwitch.Describe(st2)}  {on.Length:N0} B");
        if (st2 != LanguageSwitch.State.Applied) { Console.WriteLine("  ✗ 启用后状态不对"); return 5; }

        var back = LanguageSwitch.Swap(on);
        if (back is null || !back.AsSpan().SequenceEqual(cur)) { Console.WriteLine("  ✗ 还原后没有逐字节回到原样"); return 5; }
        Console.WriteLine("  还原后  逐字节回到原样 ✓");
        return 0;
    }

    private static int FlagTest(string path) {
        if (!File.Exists(path)) { Console.WriteLine($"找不到文件：{path}"); return 1; }
        var cur = File.ReadAllBytes(path);
        Console.WriteLine($"uiimages1.txt  {cur.Length:N0} B");
        var st = FlagIcon.Detect(cur);
        Console.WriteLine($"  当前    {FlagIcon.Describe(st)}");
        if (st == FlagIcon.State.Unknown) return 5;

        if (!FlagIcon.TryBuild(cur, true, out var on, out var msg)) { Console.WriteLine($"  ! {msg}"); return 5; }
        var st2 = FlagIcon.Detect(on!);
        Console.WriteLine($"  启用后  {FlagIcon.Describe(st2)}  {on!.Length:N0} B");
        if (st2 != FlagIcon.State.Applied) { Console.WriteLine("  ✗ 启用后状态不对"); return 5; }

        if (!FlagIcon.TryBuild(on!, false, out var back, out var msg2)) { Console.WriteLine($"  ! {msg2}"); return 5; }
        if (back is null || !back.AsSpan().SequenceEqual(cur)) {
            Console.WriteLine($"  ✗ 还原后与原文件不同（{back?.Length ?? 0:N0} B vs {cur.Length:N0} B）");
            return 5;
        }
        Console.WriteLine("  还原后  逐字节回到原样 ✓");
        return 0;
    }

    // 数据包自检：不需要游戏客户端。拿基线文件（raw_sd 里的繁中原版）把每一条重新生成成
    // 「全量对照」，比对发布产物 sha —— 生成端（patch_sd.py）与补丁器（这里）只要有一处
    // 拼法不同，全新客户端就会在写入前被判「生成内容 sha 不符」整体拒写。这个测试提前拦住它。
    private static int DataTest(string dir) {
        Console.WriteLine($"数据包 {_ver}（{_bundleBuilt}）  基线目录 {dir}");
        var ok = 0;
        var bad = 0;
        foreach (var e in _sd) {
            var name = System.IO.Path.GetFileName(e.Path);
            var p = System.IO.Path.Combine(dir, "metadata_statdescriptions_" + name);
            if (!File.Exists(p)) p = System.IO.Path.Combine(dir, name);
            if (!File.Exists(p)) { Console.WriteLine($"  ? {name,-52} 基线文件缺失"); bad++; continue; }
            var cur = File.ReadAllBytes(p);
            var s0 = Sha(cur);
            if (s0 != e.ShaOriginal) {
                Console.WriteLine($"  ! {name,-52} 基线不是官方原文（{s0[..12]}）");
                bad++;
                continue;
            }
            byte[] tgt;
            try { tgt = Rewrite(e, cur, Flavor.Original, Flavor.DualAll); }
            catch (Exception ex) {
                Console.WriteLine($"  ✗ {name,-52} {ex.GetType().Name}: {ex.Message}");
                bad++;
                continue;
            }
            var got = Sha(tgt);
            if (got != e.ShaDualAll) {
                Console.WriteLine($"  ✗ {name,-52} 生成 {got[..12]} ≠ 产物 {e.ShaDualAll[..12]}");
                bad++;
                continue;
            }
            ok++;
        }
        Console.WriteLine($"  通过 {ok} / {_sd.Count}，异常 {bad}");
        return bad == 0 ? 0 : 4;
    }

    // 拿一份真实的「外部改过」文件跑一遍判定 + 合并，只打印不写入。
    // 参数：--sd-test=文件路径（按文件名匹配数据包里的条目）
    private static int SdTest(string path) {
        var name = System.IO.Path.GetFileName(path);
        var e = _sd.FirstOrDefault(x => System.IO.Path.GetFileName(x.Path) == name);
        if (e is null) { Console.WriteLine($"数据包里没有这个文件：{name}"); return 1; }
        if (!File.Exists(path)) { Console.WriteLine($"找不到文件：{path}"); return 1; }
        var cur = File.ReadAllBytes(path);

        var ok = TryCustomBase(e, cur, out var why, out var knownOnly);
        Console.WriteLine($"{name}");
        Console.WriteLine($"  行数 {SplitLines(cur).Count:N0} / 基线 {e.BaseLines:N0}   待改行 {e.Rows.Count}");
        Console.WriteLine($"  可叠文本判定：{(ok ? "是" : "否")}  （{why}）");
        if (ok && !knownOnly) Console.WriteLine("  注意：这一份含旧版补丁文本，会按当前数据包重写");
        if (!ok) return 5;

        var tgt = Rewrite(e, cur, Flavor.Custom, Flavor.Translated);
        var a = SplitLines(cur);
        var b = SplitLines(tgt);
        var changed = 0;
        var restored = 0;
        var kept = 0;
        var bad = 0;
        for (var i = 0; i < Math.Min(a.Count, b.Count); i++) {
            if (a[i] == b[i]) continue;
            changed++;
            var (j1, k1) = TextSpan(a[i]);
            var (j2, k2) = TextSpan(b[i]);
            var was = a[i][j1..k1];
            var now = b[i][j2..k2];
            if (Markup.SameMarkup(was, now)) {
                kept++;                                  // 标记原样沿用了底稿
            } else if (Markup.IsBare(was)) {
                restored++;                              // 底稿裸文本 → 补回官方标记
            } else {
                bad++;                                   // 其它任何标记变动都不该发生
            }
            if (changed <= 6) {
                Console.WriteLine($"  行{i + 1}");
                Console.WriteLine($"    底稿 {Clip(was)}");
                Console.WriteLine($"    合并 {Clip(now)}");
            }
        }
        Console.WriteLine($"  改动 {changed} 行：沿用底稿标记 {kept}，还原官方标记 {restored}，异常 {bad}");
        Console.WriteLine($"  输出 sha256 {Sha(tgt)}");
        return bad == 0 ? 0 : 7;
    }

    // 拿一份真实的「外部改过」数据表跑一遍判定 + 合并，只打印不写入。
    // 参数：--dat-test=文件路径（按文件名匹配数据包里的条目）
    private static int DatTest(string path) {
        var name = System.IO.Path.GetFileName(path);
        var e = _dat.FirstOrDefault(x => x.Table == name);
        if (e is null) { Console.WriteLine($"数据包里没有这个表：{name}"); return 1; }
        if (!File.Exists(path)) { Console.WriteLine($"找不到文件：{path}"); return 1; }
        var cur = File.ReadAllBytes(path);

        var ok = DatMerge.TryCustomBase(e, cur, out var why);
        var v = DatMerge.Load(cur, name);
        var b = DatMerge.Load(e.Orig, name + "(包内原文)");
        Console.WriteLine($"{name}");
        Console.WriteLine($"  行数 {v.RowCount:N0} / 基线 {b.RowCount:N0}   行宽 {v.RowSize} / {b.RowSize}   待改槽位 {e.Edits.Count}");
        Console.WriteLine($"  sha256 {Sha(cur)}");
        Console.WriteLine($"  可叠改动判定：{(ok ? "是" : "否")}  （{why}）");
        if (!ok) return 5;

        var tgt = DatMerge.Apply(e, cur, revert: false);
        var again = DatMerge.Apply(e, tgt, revert: false);       // 再叠一次应当是空操作
        var idem = again.AsSpan().SequenceEqual(tgt);
        Console.WriteLine($"  合并 {cur.Length:N0} → {tgt.Length:N0} B（+{tgt.Length - cur.Length}）  幂等：{(idem ? "是" : "否")}");
        foreach (var ed in e.Edits) {
            var before = DatMerge.StringField(v, ed.Row, ed.Rel) ?? "";
            var after = DatMerge.StringField(DatMerge.Load(tgt, name), ed.Row, ed.Rel) ?? "";
            Console.WriteLine($"  行 {ed.Row} 槽位 {ed.Rel}");
            Console.WriteLine($"    底稿 {Clip(before)}");
            Console.WriteLine($"    合并 {Clip(after)}");
        }
        Console.WriteLine($"  输出 sha256 {Sha(tgt)}");
        return 0;
    }

    /// <summary>
    /// 写入通道演练：把目标容器的索引复制到临时目录，在那里做 N 次写入 + 一次落盘 + 回读。
    /// 全程不碰真实客户端（ggpk 除外：它太大，复制不了，只能拿 index 里的记录做只读核对）。
    /// </summary>
    private static int WriteTest(string nArg) {
        var n = int.TryParse(nArg, out var k) ? Math.Clamp(k, 1, 200) : 30;
        var isBundles = System.IO.Path.GetFileName(_target).Equals("_.index.bin", StringComparison.OrdinalIgnoreCase);
        if (!isBundles) {
            Console.WriteLine("写入演练只对 Steam/WeGame 的 Bundles2 有效（ggpk 是 70 GB 的单文件，复制一份不现实）。");
            Console.WriteLine("  ggpk 请用 --struct 结构演练，或在副本上跑。");
            return 1;
        }
        var idxPath = _target;
        if (!File.Exists(idxPath)) { Console.WriteLine($"找不到 {idxPath}"); return 1; }

        var tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "poe-zh-writetest-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmp);
        var copy = System.IO.Path.Combine(tmp, "_.index.bin");
        File.Copy(idxPath, copy);
        // 自建 bundle 也要一起搬过去，否则库在副本里找不到它们，会新建一个空的 —— 那是演练环境的假象
        var srcCustom = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(idxPath)!, "LibGGPK3");
        if (Directory.Exists(srcCustom)) {
            var dstCustom = System.IO.Path.Combine(tmp, "LibGGPK3");
            Directory.CreateDirectory(dstCustom);
            foreach (var f in Directory.GetFiles(srcCustom, "*.bundle.bin"))
                File.Copy(f, System.IO.Path.Combine(dstCustom, System.IO.Path.GetFileName(f)), true);
        }
        Console.WriteLine($"写入演练（只动临时副本，真实客户端一个字节不碰）");
        Console.WriteLine($"  源索引 {idxPath}");
        Console.WriteLine($"  演练目录 {tmp}");

        Idx? idx = null;
        var bad = 0;
        try {
            idx = new Idx(copy, parsePaths: false, bundleFactory: new DriveBundleFactory(tmp));
            Console.WriteLine($"  索引里 {idx.Files.Count:N0} 个文件 / {idx.Bundles.Length:N0} 个 bundle");
            var recs = idx.Files.Where(kv => kv.Value.Size is > 0 and < (4 << 20)).Take(n).ToArray();
            Console.WriteLine($"  取 {recs.Length} 个文件依次写入");
            Console.WriteLine();

            var expect = new Dictionary<ulong, string>();       // pathHash → sha（重开后靠它找，FileRecord 对象会换）
            for (var i = 0; i < recs.Length; i++) {
                var hash = recs[i].Key;
                var rec = recs[i].Value;
                var data = new byte[Math.Min(rec.Size + 17, 1 << 20)];
                for (var j = 0; j < data.Length; j++) data[j] = (byte)(i * 31 + j);
                try {
                    rec.Write(data, saveIndex: false);
                    expect[hash] = Sha(data);
                    Console.WriteLine($"  [{i + 1,2}/{recs.Length}] bundle#{rec.BundleRecord.BundleIndex,-5} {rec.Size,9:N0} → {data.Length,9:N0} B   ✓");
                } catch (Exception ex) {
                    Console.WriteLine($"  [{i + 1,2}/{recs.Length}] bundle#{rec.BundleRecord.BundleIndex,-5} {rec.Size,9:N0} B   ✗ {ex.GetType().Name}: {ex.Message}");
                    bad++;
                }
            }

            Console.WriteLine();
            var sw = Stopwatch.StartNew();
            try {
                idx.Save();
                Console.WriteLine($"  落盘索引  {sw.ElapsedMilliseconds} ms  ✓");
            } catch (Exception ex) {
                Console.WriteLine($"  落盘索引  ✗ {ex.GetType().Name}: {ex.Message}");
                bad++;
            }

            // 用同一个内存对象回读是自欺：index 写错了它照样读得出来。
            // 游戏是「关掉再重新解析磁盘上的 _.index.bin」，这里就按游戏的方式做。
            Console.WriteLine();
            Console.WriteLine("  重新解析磁盘 index（模拟游戏启动）");
            idx.Dispose();
            idx = null;
            var re = new Idx(copy, parsePaths: false, bundleFactory: new DriveBundleFactory(tmp));
            try {
                var misaligned = 0;
                var rbs = re.Bundles;
                for (var i = 0; i < rbs.Length; i++)
                    if (rbs.Span[i].BundleIndex != i) {
                        Console.WriteLine($"  ✗ bundle 错位：第 {i} 个记录的 BundleIndex = {rbs.Span[i].BundleIndex}（{rbs.Span[i].Path}）");
                        if (++misaligned >= 5) { Console.WriteLine("    ..."); break; }
                    }
                if (misaligned > 0) { Console.WriteLine($"  共 {misaligned} 处错位 —— 文件记录会指向别的 bundle，游戏读到垃圾"); bad += misaligned; }
                else Console.WriteLine($"  bundle 记录 {rbs.Length:N0} 个，BundleIndex 全部与位置一致  ✓");

                // 文件记录里的 offset 是「解压后空间」里的偏移，边界是 bundle 的 UncompressedSize
                var overrun = 0;
                var checkedFiles = 0;
                foreach (var fr in re.Files.Values) {
                    var br2 = fr.BundleRecord;
                    if (!File.Exists(System.IO.Path.Combine(tmp, br2.Path))) continue;   // 原始 bundle 不在演练目录，跳过
                    checkedFiles++;
                    if (fr.Offset < 0 || fr.Size < 0 || fr.Offset + (long)fr.Size > br2.UncompressedSize) {
                        Console.WriteLine($"  ✗ 越界：{br2.Path}  offset={fr.Offset:N0} + size={fr.Size:N0} > 解压空间 {br2.UncompressedSize:N0}");
                        if (++overrun >= 5) { Console.WriteLine("    ..."); break; }
                    }
                }
                if (overrun > 0) { Console.WriteLine($"  共 {overrun} 个文件记录越界"); bad += overrun; }
                else Console.WriteLine($"  记录边界  {checkedFiles:N0} 个文件已核对，全部落在解压空间内  ✓");

                var ok = 0;
                var fail = 0;
                foreach (var (h, sha) in expect) {
                    if (!re.Files.TryGetValue(h, out var fr)) { Console.WriteLine("  ✗ 重开后找不到文件（pathHash 丢失）"); fail++; continue; }
                    try {
                        var got = Sha(fr.Read().ToArray());
                        if (got == sha) ok++;
                        else { Console.WriteLine($"  ✗ 重开后内容不符：bundle#{fr.BundleRecord.BundleIndex} {fr.BundleRecord.Path}"); fail++; }
                    } catch (Exception ex) {
                        Console.WriteLine($"  ✗ 重开后读不出：bundle#{fr.BundleRecord.BundleIndex} {fr.BundleRecord.Path} — {ex.GetType().Name}: {ex.Message}");
                        fail++;
                    }
                }
                Console.WriteLine($"  重开校验  {ok}/{expect.Count} 通过" + (fail > 0 ? $"，{fail} 个失败" : ""));
                bad += fail;
            } finally {
                re.Dispose();
            }
        } catch (Exception ex) {
            Console.WriteLine($"✗ 演练中断：{ex.GetType().Name}: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return 3;
        } finally {
            try { idx?.Dispose(); } catch { }
            try { Directory.Delete(tmp, true); } catch { }
        }
        return bad == 0 ? 0 : 3;
    }

    // ---------------------------------------------------------------- 标记层自检

    // ---------------------------------------------------------------- ggpk 逐字节还原自检
    //
    // 老式 Content.ggpk 没法在别的机器上复现，所以这里用一份「结构相同」的假文件验证引擎：
    //   原地覆盖、多块重叠、写到末尾之外（追加）三种写入混在一起，模拟三种收尾：
    //   A) 写到一半进程没了（不提交）→ 下次启动 Recover 必须退回原样
    //   B) 写完提交 → 之后用 --restore-ggpk 退回原样
    //   C) 同一块被反复覆盖（区间去重）→ 退回的仍是最早那份原字节
    private static int UndoTest() {
        var root = Path.Combine(Path.GetTempPath(), "poezh-undo-" + DateTime.Now.ToString("HHmmssfff"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "Content.ggpk");
        const long dataStart = 2 << 20;
        const long bodyLen = 8 << 20;
        var rnd = new Random(20261029);
        var orig = new byte[bodyLen];
        rnd.NextBytes(orig);
        File.WriteAllBytes(path, orig);
        var bad = 0;
        Console.WriteLine($"自检文件 {path}  {orig.Length:N0} B");

        void Check(string what, bool ok, string extra = "") {
            Console.WriteLine($"  {(ok ? "✓" : "✗")} {what}{(extra.Length > 0 ? "  " + extra : "")}");
            if (!ok) bad++;
        }
        byte[] Rnd(int n) { var b = new byte[n]; rnd.NextBytes(b); return b; }

        // 当前 8 MB 内容里，[0, min(len,8MB)) 必须还是原来的字节
        bool SameAsOrig(string why) {
            var now = File.ReadAllBytes(path);
            if (now.Length < bodyLen) { Console.WriteLine($"    ! 文件比原来短：{now.Length:N0} < {bodyLen:N0}"); return false; }
            for (long i = 0; i < bodyLen; i++)
                if (now[i] != orig[i]) {
                    Console.WriteLine($"    ! {why}：第 {i:N0} 个字节不一样（{orig[i]:X2} → {now[i]:X2}）");
                    return false;
                }
            return true;
        }

        // ---- A) 写到一半断电
        var u = GgpkUndo.Begin(path);
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)) {
            var st = u.Wrap(fs);
            void Put(long off, byte[] b) { st.Position = off; st.Write(b, 0, b.Length); }
            Put(1 << 20, Rnd(4096));                       // 靠前的一块
            Put(dataStart + 100, Rnd(4096));                // 记一份原文
            Put(dataStart + 200, Rnd(4096));                // 重叠：只该记第一次没记到的部分
            Put(dataStart + 200, Rnd(64));                  // 同一块再改一次
            Put(bodyLen + (5 << 20), Rnd(8192));            // 追加到末尾之外
            st.Flush();
            Console.WriteLine($"  写了 5 次：日志记下 {u.Bytes:N0} B 原文");
            foreach (var (off, len) in GgpkUndo.ReadRecords(u.Dir))
                Console.WriteLine($"      记录 [{off:N0}, +{len:N0})");
            Console.WriteLine("  … 现在模拟进程被杀");
        }
        u.SimulateCrash();
        var rec = GgpkUndo.Recover(path);
        Check("启动自愈：认出了没走完的写入", rec.Recovered, rec.Message);
        Check("自愈后 8 MB 内容逐字节回到写前", SameAsOrig("自愈后"));

        // ---- B) 正常写完提交，再用快照退回去
        File.WriteAllBytes(path, orig);         // 复位
        var u2 = GgpkUndo.Begin(path);
        var fs2 = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        var st2 = u2.Wrap(fs2);
        var mod = orig[(int)(dataStart + 100)..(int)(dataStart + 100 + 4096)];
        rnd.NextBytes(mod);
        st2.Position = dataStart + 100;
        st2.Write(mod, 0, mod.Length);
        st2.Flush();
        Console.WriteLine($"  正常写入 4 KB，日志 {u2.Bytes:N0} B");
        var err = u2.Commit();
        fs2.Dispose();
        Check("提交成功", err is null, err ?? "");
        Check("提交后内容确实变了", !SameAsOrig("提交后"));

        var snap = GgpkUndo.Find(path, "latest");
        Check("能找到刚提交的快照", snap is not null);
        if (snap is not null) {
            var e2 = GgpkUndo.Restore(path, snap, out var bn);
            Check("用快照退回去", e2 is null, e2 ?? $"{bn} 块");
            Check("退回后 8 MB 内容逐字节回到写前", SameAsOrig("退回后"));
        }

        // ---- C) 只读打开不该留下垃圾
        var u3 = GgpkUndo.Begin(path);
        var before = Directory.GetDirectories(GgpkUndo.RootFor(path)).Length;
        u3.TrimIfEmpty();
        var after = Directory.GetDirectories(GgpkUndo.RootFor(path)).Length;
        Check("只读会话的空日志会被清掉", after == before - 1, $"{before} → {after}");

        Console.WriteLine();
        Console.WriteLine($"日志目录还剩 {Directory.GetDirectories(GgpkUndo.RootFor(path)).Length} 份（提交过的快照会留着当后悔药）");
        Console.WriteLine($"ggpk 逐字节还原自检：{(bad == 0 ? "全部通过" : $"{bad} 项失败")}");
        try { Directory.Delete(root, recursive: true); } catch { }
        return bad == 0 ? 0 : 6;
    }

    private static int MarkupTest() {
        var cases = new (string shell, string oldT, string newT, string want)[] {
            ("<enchanted>{{稀有珠寶}}</enchanted>", "<enchanted>{{稀有珠寶}}</enchanted>",
             "<enchanted>{{稀有飾品}}</enchanted>", "<enchanted>{{稀有飾品}}</enchanted>"),
            ("發掘保險箱含有 1 個額外稀有珠寶", "<enchanted>{{發掘保險箱含有 1 個額外稀有珠寶}}",
             "<enchanted>{{發掘保險箱含有 1 個額外稀有飾品}}",
             "<enchanted>{{發掘保險箱含有 1 個額外稀有飾品}}"),
            ("稀有珠寶", "稀有珠寶", "稀有飾品", "稀有飾品"),
            ("<rgb(209,46,46)>{{增加 20% 伤害}}", "<nemesismod>{{增加 20% 伤害}}",
             "<nemesismod>{{增加 25% 伤害}}", "<rgb(209,46,46)>{{增加 25% 伤害}}"),
            ("{{發掘保險箱含有 1 個額外稀有珠寶}}", "<enchanted>{{發掘保險箱含有 1 個額外稀有珠寶}}",
             "<enchanted>{{發掘保險箱含有 1 個額外稀有飾品}}", "{{發掘保險箱含有 1 個額外稀有飾品}}"),
            ("<enchanted>{{有 5% 機率}}</enchanted>", "<enchanted>{{有 5% 機率}}</enchanted>",
             "<enchanted>{{有 5% 的機率}}</enchanted>", "<enchanted>{{有 5% 的機率}}</enchanted>"),
            ("<rgb(1,2,3)>{{相同}}", "<enchanted>{{相同}}", "<enchanted>{{相同}}", "<rgb(1,2,3)>{{相同}}"),
            ("<rgb(200,200,200)>{{每秒造成 {0} 點傷害，持續 {1} 秒}}",
             "<normal>{{每秒造成 {0} 點傷害，持續 {1} 秒}}",
             "<normal>{{每秒造成 {0} 點傷害，持續 {1} 秒，可疊加}}",
             "<rgb(200,200,200)>{{每秒造成 {0} 點傷害，持續 {1} 秒，可疊加}}"),
            // 修官方笔误：{0] → {0}（标记层没动，纯文本）
            ("法術擁有等同於你已裝備雙手武器{0]%物理傷害的附加法術傷害",
             "法術擁有等同於你已裝備雙手武器{0]%物理傷害的附加法術傷害",
             "法術擁有等同於你已裝備雙手武器{0}%物理傷害的附加法術傷害",
             "法術擁有等同於你已裝備雙手武器{0}%物理傷害的附加法術傷害"),
            // 修官方笔误：漏掉的 }} 要补回来（底稿标记与原文一致 = 没被动过，可整套套用）
            ("<enchanted>{{몬스터가 떨어뜨리는 아이템의 수량 {0}% 감소",
             "<enchanted>{{몬스터가 떨어뜨리는 아이템의 수량 {0}% 감소",
             "<enchanted>{{몬스터가 떨어뜨리는 아이템의 수량 {0}% 감소}}",
             "<enchanted>{{몬스터가 떨어뜨리는 아이템의 수량 {0}% 감소}}"),
            // 同一行的漏 }}，但底稿被别人换了颜色 → 不补 }}，保留对方的样式
            ("<rgb(1,2,3)>{{몬스터가 떨어뜨리는 아이템의 수량 {0}% 감소",
             "<enchanted>{{몬스터가 떨어뜨리는 아이템의 수량 {0}% 감소",
             "<enchanted>{{몬스터가 떨어뜨리는 아이템의 수량 {0}% 감소}}",
             "<rgb(1,2,3)>{{몬스터가 떨어뜨리는 아이템의 수량 {0}% 감소"),
        };
        var bad = 0;
        foreach (var (shell, o, n, want) in cases) {
            string got;
            try { got = Markup.Merge(shell, o, n); }
            catch (Exception ex) { got = "!" + ex.Message; }
            var ok = got == want;
            if (!ok) bad++;
            Console.WriteLine($"  [{(ok ? "ok " : "FAIL")}] {Clip(shell)}  ->  {Clip(got)}");
            if (!ok) Console.WriteLine($"         want {Clip(want)}");
        }
        Console.WriteLine($"markup-test: {cases.Length - bad}/{cases.Length} passed");
        return bad == 0 ? 0 : 7;
    }

    // ---------------------------------------------------------------- 基础

    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    private static string FlavorName(Flavor f) => f switch {
        Flavor.Original => "官方原文",
        Flavor.Translated => "纯译文",
        Flavor.Dual => "对照版",
        Flavor.DualAll => "全量对照",
        _ => "未知",
    };

    private static Flavor? ParseFlavor(string s) => s.ToLowerInvariant() switch {
        "translated" or "pure" or "1" => Flavor.Translated,
        "dual" or "2" => Flavor.Dual,
        "dualall" or "dual-all" or "3" => Flavor.DualAll,
        "original" or "restore" or "4" => Flavor.Original,
        _ => null,
    };

    // 双击启动时（没有拖放参数）自动找游戏
    private static readonly string[] GuessDirs = {
        // 官方独立安装包（单文件 ggpk）
        @"C:\Program Files (x86)\Grinding Gear Games\Path of Exile",
        @"C:\Program Files\Grinding Gear Games\Path of Exile",
        @"D:\Grinding Gear Games\Path of Exile",
        @"E:\Grinding Gear Games\Path of Exile",
        @"D:\Games\Path of Exile",
        @"E:\Games\Path of Exile",
        // Steam（Bundles2 目录）
        @"C:\Program Files (x86)\Steam\steamapps\common\Path of Exile",
        @"D:\SteamLibrary\steamapps\common\Path of Exile",
        @"E:\SteamLibrary\steamapps\common\Path of Exile",
        @"D:\Steam\steamapps\common\Path of Exile",
        @"E:\Steam\steamapps\common\Path of Exile",
        // 腾讯 WeGame 国服（同样是 Bundles2 目录）
        @"E:\games\WeGameApps\流放之路",
        @"C:\games\WeGameApps\流放之路",
    };

    private static string? FindDefaultGgpk() {
        foreach (var d in GuessDirs) {
            var ggpk = System.IO.Path.Combine(d, "Content.ggpk");
            if (File.Exists(ggpk)) return ggpk;
            var idx = System.IO.Path.Combine(d, "Bundles2", "_.index.bin");
            if (File.Exists(idx)) return idx;
        }
        return null;
    }

    // 拖放目标归一到「真正要改的那个文件」：Content.ggpk 或 Bundles2/_.index.bin
    private static string? ResolveTarget(string p) {
        if (Directory.Exists(p)) {
            foreach (var rel in new[] { "Content.ggpk", "_.index.bin", @"Bundles2\_.index.bin" }) {
                var c = System.IO.Path.Combine(p, rel);
                if (File.Exists(c)) return c;
            }
            return null;
        }
        return File.Exists(p) ? p : null;
    }

    private static IContainer? _store;
    private static bool _detachedThisRun;    // 本次运行是否已经换过自建 bundle（一次运行只换一条）
    private static IRollback? _txn;          // 当前写入事务；容器在写入途中新建的 bundle 要登记到它上面

    // 还原点：Bundles2 复制 20 MB 的 _.index.bin；ggpk 74 GB 复制不了，
    // 用容器自带的那份逐字节撤销日志（写前记下被覆盖的每一块原文）。
    private static IRollback? BeginRollback(IContainer store) =>
        store.Kind == "ggpk" ? store.Undo : Txn.Begin(store.IndexFile);

    // 索引复制都失败还硬要写：只留一个必须显式加的口子，默认一律拒绝。
    private static bool AllowNoRollback() => Args.ContainsKey("allow-no-rollback");

    // Steam/Bundles2 的 _.index.bin 只能被一个写句柄打开，所以同一时刻只留一个
    private static void CloseStore() {
        try { _store?.Dispose(); } catch { }
        _store = null;
        _detachedThisRun = false;
    }

    private static IContainer? Reopen() { CloseStore(); return OpenContainer(); }

    private static IContainer? OpenContainer() {
        CloseStore();
        try {
            _store = System.IO.Path.GetFileName(_target).Equals("_.index.bin", StringComparison.OrdinalIgnoreCase)
                ? new BundlesContainer(_target)
                : new GgpkContainer(_target);
            return _store;
        } catch (Exception ex) {
            Console.WriteLine($"打开数据失败：{ex.Message}");
            Console.WriteLine("  · 游戏是否还在运行？");
            Console.WriteLine("  · 是否需要以管理员身份运行？");
            Console.WriteLine("  · Steam 版需要 Bundles2 目录里的 _.index.bin 和对应 .bundle.bin");
            return null;
        }
    }

    private static void PrintUsage() {
        Console.WriteLine($"PoE 繁中改良汉化 补丁器  v{_ver}");
        Console.WriteLine();
        Console.WriteLine("把下面任意一个拖到本程序上：");
        Console.WriteLine("  · 官方独立端：游戏目录里的 Content.ggpk");
        Console.WriteLine("  · Steam 端  ：游戏目录里的 Bundles2 目录（内含 _.index.bin）");
        Console.WriteLine();
        Console.WriteLine("直接双击也可以，会自动找默认安装路径。");
        Console.WriteLine();
        Console.WriteLine("命令行：");
        Console.WriteLine("  PoE-zh-Patcher.exe <Content.ggpk | Bundles2>              交互菜单");
        Console.WriteLine("  PoE-zh-Patcher.exe <...> --set=translated                 纯译文");
        Console.WriteLine("  PoE-zh-Patcher.exe <...> --set=dual                       对照版");
        Console.WriteLine("  PoE-zh-Patcher.exe <...> --set=dualall                    全量对照");
        Console.WriteLine("  PoE-zh-Patcher.exe <...> --set=original                   还原官方原文");
        Console.WriteLine("  PoE-zh-Patcher.exe <...> --status                         只看状态");
        Console.WriteLine("  PoE-zh-Patcher.exe <...> --selftest                       数据与文件对得上吗");
        Console.WriteLine("  PoE-zh-Patcher.exe <...> --struct                         结构演练（只验读写通道）");
        Console.WriteLine("  PoE-zh-Patcher.exe <...> --collect [--out=目录]           采集诊断包（只读，发给作者排查）");
        Console.WriteLine("  PoE-zh-Patcher.exe <...> --writetest[=N]                  写入演练（在索引副本上写 N 个文件，不碰客户端）");
        Console.WriteLine("  PoE-zh-Patcher.exe <...> --bundles[=all]                 看索引里的 bundle 记录（排查 bad allocation / Buffer Underflow）");
        Console.WriteLine("  PoE-zh-Patcher.exe <...> --set=translated --partial       只处理对得上的文件，跳过对不上的");
        Console.WriteLine("  PoE-zh-Patcher.exe <...> --set=translated --plan          只演算校验，不写文件");
        Console.WriteLine("  PoE-zh-Patcher.exe <...> --set=translated --only=<关键词>  只改文件名含该关键词的文件（最小验证用）");
        Console.WriteLine("  PoE-zh-Patcher.exe --data-test=<基线目录>                 数据包自检（不需要游戏：拿官方原文重算一遍，比对发布 sha）");
        Console.WriteLine("  PoE-zh-Patcher.exe <...> --locale-only[=original]        只处理语系与国旗，不碰译文");
        Console.WriteLine();
        Console.WriteLine("安全相关：");
        Console.WriteLine("  --on-modified=merge|ours|original|skip   遇到被别的汉化改过的文件怎么办（默认 merge）");
        Console.WriteLine("                                           merge 只叠译文、ours 换成我们的、original 退回官方原文");
        Console.WriteLine("  <...> --backups                          看有哪些还原点（整份 / 单个文件）");
        Console.WriteLine("  <...> --restore-backup=<时间戳|latest>   把某次备份的文件整份写回去");
        Console.WriteLine("  <Content.ggpk> --restore-ggpk[=时间戳]    把整个 ggpk 退回那次安装之前（游戏打不开时用）");
        Console.WriteLine("  每次写入前都留还原点：Bundles2 复制 _.index.bin；ggpk 抄索引区 + 逐块记原字节。");
        Console.WriteLine("  写坏了自动整份退回；进程被杀 / 断电也能在下次启动时自动退回。");
        Console.WriteLine("  排障用：--simulate-fail=N 故意在第 N 个文件失败，验证退回流程（不写坏客户端）");
    }

    private static void Pause() {
        if (Console.IsInputRedirected) return;
        Console.WriteLine();
        Console.Write("按任意键退出...");
        try { Console.ReadKey(true); } catch { }
        Console.WriteLine();
    }

    private static (string client, string patch) DetectGameVersion() {
        // ggpk 在游戏根目录；Steam 的 _.index.bin 在 Bundles2 下，日志在上一级
        var dirs = new List<string> { System.IO.Path.GetDirectoryName(_target)! };
        var up = System.IO.Path.GetDirectoryName(dirs[0]);
        if (up is not null) dirs.Add(up);
        foreach (var dir in dirs)
        foreach (var rel in new[] { "logs/LatestClient.txt", "logs/Client.txt" }) {
            var p = System.IO.Path.Combine(dir, rel);
            if (!File.Exists(p)) continue;
            string? c = null, pa = null;
            try {
                foreach (var line in File.ReadLines(p)) {
                    var m = System.Text.RegularExpressions.Regex.Match(line, @"User agent: PoE release/tags/([\w.\-]+)");
                    if (m.Success) c = m.Groups[1].Value;
                    m = System.Text.RegularExpressions.Regex.Match(line, @"Web root: https://patch\.poecdn\.com/([\w.\-]+)/");
                    if (m.Success) pa = m.Groups[1].Value;
                }
            } catch { }
            if (c is not null) return (c, pa ?? "");
        }
        return ("", "");
    }

    private static bool VersionOk() {
        var (c, pa) = DetectGameVersion();
        if (c.Length == 0) return true;                 // 拿不到就不拦
        if (c != _gameClient) return false;
        if (pa.Length > 0 && _gamePatch.Length > 0 && pa != _gamePatch) return false;
        return true;
    }

    // ---------------------------------------------------------------- 文本层

    private static List<string> SplitLines(byte[] b) {
        if (b.Length < 2 || b[0] != 0xFF || b[1] != 0xFE)
            throw new InvalidDataException("不是 UTF-16LE 带 BOM 的文件");
        var s = Encoding.Unicode.GetString(b, 2, b.Length - 2);
        return s.Split(new[] { "\r\n" }, StringSplitOptions.None).ToList();
    }

    private static byte[] Encode(List<string> lines) {
        var text = string.Join("\r\n", lines);
        var body = Encoding.Unicode.GetBytes(text);
        var res = new byte[body.Length + 2];
        res[0] = 0xFF; res[1] = 0xFE;
        body.CopyTo(res, 2);
        return res;
    }

    private static (int j, int k) TextSpan(string raw) {
        var i = raw.IndexOf(" \"", StringComparison.Ordinal);
        if (i < 0) return (-1, -1);
        var j = i + 2;
        var k = j;
        while (k < raw.Length) {
            var c = raw[k];
            if (c == '\\') { k += 2; continue; }
            if (c == '"') break;
            k++;
        }
        return (j, k);
    }

    // 文本内换行是字面「反斜杠 + n」两个字符，不是 0x0A
    private const string Nl = "\\n";

    private static string DualAllText(Row r) =>
        r.E is null ? (r.T ?? r.O) : DualBody(r.T ?? r.O, r.E);

    // 与 tools/patch_sd.py 的 dual_body 逐字对齐 —— 这里少一层缩进，整份文件就对不上，
    // 全新客户端走 sha 校验时会被判「生成内容 sha 不符」整体拒写。
    // 中文段数与英文段数相同：一组中文配一组英文，逐组穿插；
    // 不同（实测 135 行）：整段英文附在末尾，但每一组都补缩进，不能顶格。
    private static string DualBody(string body, string en) {
        var zh = body.Split(Nl);
        var ee = en.Split(Nl);
        if (ee.Length > 0 && body.Contains(Indent + ee[0])) return body;
        if (zh.Length == ee.Length) {
            var outp = new List<string>(zh.Length * 2);
            for (var i = 0; i < zh.Length; i++) {
                outp.Add(zh[i]);
                if (ee[i].Length > 0) outp.Add(Indent + ee[i]);
            }
            return string.Join(Nl, outp);
        }
        return body + Nl + Indent + string.Join(Nl + Indent, ee);
    }

    /// 这一行现有的文本层，对应我们已知的哪一版文本（原文/译文/对照/全量对照）。
    /// 外部改过标记的底稿靠它辨认当前处在哪个状态。
    private static string? KnownText(Row r, string core) {
        foreach (var c in new[] { r.O, r.T, r.D, DualAllText(r) }) {
            if (c is not null && Markup.Core(c) == core) return c;
        }
        foreach (var c in r.Legacy) {
            if (Markup.Core(c) == core) return c;
        }
        return null;
    }

    // 这一行只是「上一版补丁写的」，不是当前四态里的任何一种
    private static bool IsLegacyOnly(Row r, string core) {
        foreach (var c in new[] { r.O, r.T, r.D, DualAllText(r) }) {
            if (c is not null && Markup.Core(c) == core) return false;
        }
        return true;
    }

    private static string TextFor(Row r, Flavor f) => f switch {
        Flavor.Original => r.O,
        Flavor.Translated => r.T ?? r.O,
        Flavor.Dual => r.D ?? r.T ?? r.O,
        Flavor.DualAll => DualAllText(r),
        _ => throw new ArgumentOutOfRangeException(nameof(f)),
    };

    // 外部改过的底稿没有「预期 sha」——生成结果是什么就是什么，只能在生成后自洽比对
    private static string? TargetSha(SdEntry e, Flavor f, Flavor curF = Flavor.Original) {
        if (curF == Flavor.Custom) return null;
        return f switch {
            Flavor.Original => e.ShaOriginal,
            Flavor.Translated => e.ShaTranslated ?? e.ShaOriginal,
            Flavor.Dual => e.ShaDual ?? e.ShaOriginal,
            _ => e.ShaDualAll,
        };
    }

    private static Flavor Detect(SdEntry e, string sha) {
        if (sha == e.ShaOriginal) return Flavor.Original;
        if (e.ShaTranslated is not null && sha == e.ShaTranslated) return Flavor.Translated;
        if (e.ShaDual is not null && sha == e.ShaDual) return Flavor.Dual;
        if (sha == e.ShaDualAll) return Flavor.DualAll;
        return Flavor.Unknown;
    }

    private static byte[] Rewrite(SdEntry e, byte[] cur, Flavor curF, Flavor tgtF, bool forceOurs = false) {
        var lines = SplitLines(cur);
        foreach (var r in e.Rows) {
            if (r.No >= lines.Count)
                throw new InvalidDataException($"{e.Path}:{r.No + 1} 行超出文件范围");
            var raw = lines[r.No];
            var (j, k) = TextSpan(raw);
            if (j < 0 || k <= j)
                throw new InvalidDataException($"{e.Path}:{r.No + 1} 行内找不到引号文本");
            var now = raw[j..k];

            if (curF == Flavor.Custom) {
                // 外部改过标记：以「这一行现在是什么文本」认状态，只叠文本层。
                var known = KnownText(r, Markup.Core(now))
                    ?? throw new InvalidDataException(
                        $"{e.Path}:{r.No + 1} 认不出这行是哪一版文本\n    当前：{Clip(now)}");
                var want = TextFor(r, tgtF);
                var merged = Markup.Merge(now, known, want, _restoreBare || forceOurs);
                if (Markup.Core(merged) != Markup.Core(want))
                    throw new InvalidDataException($"{e.Path}:{r.No + 1} 合并后文本层与目标不符");
                if (merged == now) continue;
                lines[r.No] = raw[..j] + merged + raw[k..];
                continue;
            }

            var expect = TextFor(r, curF);
            var target = TextFor(r, tgtF);
            if (expect == target) continue;
            if (now != expect)
                throw new InvalidDataException(
                    $"{e.Path}:{r.No + 1} 内容与预期不符\n    当前：{Clip(now)}\n    预期：{Clip(expect)}");
            lines[r.No] = raw[..j] + target + raw[k..];
        }
        return Encode(lines);
    }

    // 认不出结构的文件（别的汉化改过、或版本错位）：把「我们写进去过的行」逐行撤回官方原文。
    // 只碰 rows —— 也就是数据包里记录的那几行；其余行一个字节不动。
    // 认不出的行保持原样（它可能是别人翻的，撤掉等于破坏别人的成果）。
    // 结果是「官方原文 + 别人的译文」混排，但文件结构完整、游戏能读，比整份跳过更接近还原。
    private static byte[] RewriteUnknownToOriginal(SdEntry e, byte[] cur, out int nRev, out int nKeep) {
        var lines = SplitLines(cur);
        nRev = 0;
        nKeep = 0;
        foreach (var r in e.Rows) {
            if (r.No < 0 || r.No >= lines.Count) { nKeep++; continue; }
            var raw = lines[r.No];
            var (j, k) = TextSpan(raw);
            if (j < 0 || k <= j) { nKeep++; continue; }
            var now = raw[j..k];
            var core = Markup.Core(now);
            if (core == Markup.Core(r.O)) continue;                 // 已经是原文
            var known = KnownText(r, core);
            if (known is null) { nKeep++; continue; }               // 不是我们写的，别动
            var merged = Markup.Merge(now, known, r.O, _restoreBare);
            if (merged == now) continue;
            lines[r.No] = raw[..j] + merged + raw[k..];
            nRev++;
        }
        return Encode(lines);
    }

    private static string Clip(string s) {
        s = s.Replace("\n", "\\n");
        return s.Length <= 70 ? s : s[..70] + "…";
    }

    // ---------------------------------------------------------------- 预检

    private sealed class Item {
        public string Label = "";
        public string Path = "";
        public bool IsSd;
        public bool Missing;             // 容器里根本没有这个文件（客户端版本不同）
        public bool Broken;              // 路径在，但数据读不出来（索引里的自建 bundle 被删掉过）
        public string BrokenWhy = "";
        public SdEntry? Sd;
        public DatEntry? Dat;
        public byte[] Cur = Array.Empty<byte>();
        public Flavor CurFlavor;
        public string Why = "";          // 判成「未知」或被判成「外部改过」的依据，展示用
        public bool Legacy;              // 认出来了：里面是上一版补丁写的文本
        public bool Force;               // 还挂在旧的自建 bundle 上：不管状态如何都重写一遍搬进新 bundle
    }

    // 外部改过标记的底稿：sha 对不上，但**结构没变**——
    // 行数与基线一致，且每个要动的行，文本层要么是原文、要么已是译文。
    // 这种情况不该拒写：只叠文本、标记照旧，就不会碰到别人的颜色体系。
    private static bool TryCustomBase(SdEntry e, byte[] cur, out string why, out bool legacyOnly) {
        why = "";
        legacyOnly = false;
        List<string> lines;
        try { lines = SplitLines(cur); } catch (Exception ex) { why = ex.Message; return false; }
        if (e.BaseLines > 0 && lines.Count != e.BaseLines) {
            why = $"行数 {lines.Count:N0} ≠ 基线 {e.BaseLines:N0}";
            return false;
        }
        foreach (var r in e.Rows) {
            if (r.No < 0 || r.No >= lines.Count) { why = $"第 {r.No + 1} 行超出文件范围"; return false; }
            var (j, k) = TextSpan(lines[r.No]);
            if (j < 0) { why = $"第 {r.No + 1} 行找不到引号文本"; return false; }
            var cn = Markup.Core(lines[r.No][j..k]);
            if (KnownText(r, cn) is null) { why = $"第 {r.No + 1} 行文本已变：{cn}"; return false; }
        }
        var tags = 0;
        var legacyRows = 0;
        foreach (var r in e.Rows) {
            var (j, k) = TextSpan(lines[r.No]);
            if (j < 0) continue;
            if (!Markup.SameMarkup(lines[r.No][j..k], r.O)) tags++;
            if (IsLegacyOnly(r, Markup.Core(lines[r.No][j..k]))) legacyRows++;
        }
        why = tags == 0 ? "文本层一致" : $"{tags}/{e.Rows.Count} 行的标记与官方不同";
        if (legacyRows > 0) why += $"；{legacyRows} 行是旧版补丁写的";
        legacyOnly = legacyRows == 0;
        return true;
    }

    private static List<Item> Preflight(IContainer store) {
        var list = new List<Item>();
        foreach (var e in _sd) {
            var it = new Item { Label = System.IO.Path.GetFileName(e.Path), Path = e.Path, IsSd = true, Sd = e };
            if (!store.TryRead(e.Path, out var cur, out var err)) {
                it.Missing = err is null;
                it.Broken = err is not null;
                it.BrokenWhy = err ?? "";
                it.CurFlavor = Flavor.Unknown;
                it.Cur = Array.Empty<byte>();
                list.Add(it);
                continue;
            }
            it.Cur = cur;
            it.CurFlavor = Detect(e, Sha(it.Cur));
            var why = "";
            if (it.CurFlavor == Flavor.Unknown) {
                if (TryCustomBase(e, cur, out why, out var knownOnly)) {
                    it.CurFlavor = Flavor.Custom;
                    it.Legacy = !knownOnly;      // 只有旧版补丁才会用到 legacy 文本
                }
                it.Why = why;
            }
            list.Add(it);
        }
        foreach (var e in _dat) {
            var it = new Item { Label = e.Table, Path = e.Path, IsSd = false, Dat = e };
            if (!store.TryRead(e.Path, out var cur, out var err)) {
                it.Missing = err is null;
                it.Broken = err is not null;
                it.BrokenWhy = err ?? "";
                it.CurFlavor = Flavor.Unknown;
                it.Cur = Array.Empty<byte>();
                list.Add(it);
                continue;
            }
            it.Cur = cur;
            var sha = Sha(it.Cur);
            it.CurFlavor = sha == e.ShaOrig ? Flavor.Original : sha == e.ShaPatch ? Flavor.DualAll : Flavor.Unknown;
            if (it.CurFlavor == Flavor.Unknown) {
                // 数据表也走「叠文本」这条路：sha 认不出但结构没变，就只改我们要动的那几个槽位
                if (DatMerge.TryCustomBase(e, cur, out var why)) it.CurFlavor = Flavor.Custom;
                it.Why = why;
            }
            list.Add(it);
        }
        return list;
    }

    private static string ShowFlavor(Item i) => i.CurFlavor switch {
        Flavor.Unknown when i.Broken => "读不出",
        Flavor.Unknown => "未知",
        Flavor.Custom => i.Legacy ? "旧版补丁" : "外部改过",
        _ when !i.IsSd && i.CurFlavor != Flavor.Original => "已打补丁",
        _ => FlavorName(i.CurFlavor),
    };

    private static string Summary(List<Item> items) {
        var c = items.GroupBy(ShowFlavor).OrderBy(g => g.Key)
            .Select(g => $"{g.Key} {g.Count()}");
        return string.Join(" / ", c);
    }

    private static void PrintHeader(List<Item> items, IContainer? store = null) {
        var (c, pa) = DetectGameVersion();
        var ok = VersionOk();
        Console.WriteLine(new string('=', 66));
        Console.WriteLine($" PoE 繁中改良汉化 补丁器  v{_ver}");
        Console.WriteLine(new string('=', 66));
        Console.WriteLine($" 数据包    {_bundleBuilt}   （适用游戏 {_gameClient}{( _gamePatch.Length > 0 ? " / patch " + _gamePatch : "")}）");
        if (c.Length == 0)
            Console.WriteLine(" 游戏版本  (读不到 logs/LatestClient.txt，跳过版本核对)");
        else
            Console.WriteLine($" 游戏版本  {c}{(pa.Length > 0 ? " / patch " + pa : "")}   {(ok ? "✓ 与数据包一致" : "✗ 与数据包不一致")}");
        if (store is not null) {
            Console.WriteLine($" 目标      [{store.Kind}] {store.Display}");
            Console.WriteLine($"           {store.Size:N0} B");
        }
        if (items.Count > 0) Console.WriteLine($" 当前状态  {Summary(items)}");
        Console.WriteLine();
    }

    // ---------------------------------------------------------------- 主流程

    private static int Menu(IContainer? store) {
        var items = Preflight(store!);
        while (true) {
            // 采集/结构演练会中途让出句柄再重开，手里这个可能已经释放 —— 每轮都取当前活着的那个
            if (store is null || !store.Alive) {
                if (Reopen() is null) return 1;
                store = _store!;
                items = Preflight(store);
            }
            try { if (!Console.IsOutputRedirected) Console.Clear(); } catch { }
            PrintHeader(items, store);
            var missing = items.Count(i => i.Missing);
            var unknown = items.Count(i => i.CurFlavor == Flavor.Unknown && !i.Missing && !i.Broken);
            if (missing > 0 || unknown > 0) {
                if (missing > 0) {
                    Console.WriteLine($" ! 有 {missing} 个目标文件在这个客户端里找不到（多半是版本不同）。");
                }
                if (unknown > 0) {
                    Console.WriteLine($" ! 有 {unknown} 个文件既不是原文也不是已知版本，内容与数据包不符。");
                }
                Console.WriteLine("   这时不会写入任何内容。数据包只适用于繁中客户端对应版本。");
                Console.WriteLine();
            }
            Console.WriteLine("  [1] 纯译文        —— 日常游玩，160 处修正");
            Console.WriteLine("  [2] 对照版        —— 修正过的 160 行下方显示英文原文");
            Console.WriteLine("  [3] 全量对照      —— 所有词缀下方显示英文原文（体积最大）");
            Console.WriteLine("  [4] 还原官方原文  —— 撤销全部修改");
            Console.WriteLine("  [t] 结构演练      —— 只验证读写通道（改写后原样写回），不改内容");
            Console.WriteLine("  [c] 采集诊断包    —— 把这份客户端的原文与对比结果打包（只读，给作者排查）");
            Console.WriteLine("  [b] 备份与还原    —— 看每次安装前的原始内容，整份写回去");
            Console.WriteLine("  [s] 刷新状态");
            Console.WriteLine("  [0] 退出");
            Console.WriteLine();
            Console.Write(" 请选择：");
            var key = ReadKeyLine();
            if (key is null) return 0;
            var k = key.Trim().ToLowerInvariant();

            if (k is "0" or "q") return 0;
            if (k is "s" or "") {
                if (Reopen() is not null) items = Preflight(_store!);
                continue;
            }
            if (k is "c") {
                Collect(store, items);
                Console.WriteLine();
                Console.WriteLine(" 按回车返回菜单");
                ReadKeyLine();
                if (Reopen() is not null) items = Preflight(_store!);   // 采集把句柄让出去了，必须重开
                continue;
            }
            if (k is "t") {
                var rt = StructTest(store, items);
                Console.WriteLine();
                Console.WriteLine(" 按回车返回菜单");
                ReadKeyLine();
                Reopen();
                items = Preflight(_store!);
                continue;
            }
            if (k is "b") {
                var snaps = GgpkUndo.List(store.IndexFile);
                var sets = BackupStore.List(store.IndexFile);
                if (snaps.Count == 0 && sets.Count == 0) {
                    Console.WriteLine(" 还没有还原点。每次安装前，被覆盖的那份内容会自动存到 .poe-zh/ 下。");
                } else {
                    Console.WriteLine();
                    if (snaps.Count > 0) {
                        Console.WriteLine(" 整份：把整个 ggpk 退回那次安装之前（游戏打不开就用这个）");
                        for (var n = 0; n < snaps.Count; n++)
                            Console.WriteLine($"   [{n + 1}] {snaps[n].Stamp}   {snaps[n].Created}   {snaps[n].RecordBytes / 1048576.0:F1} MB");
                    }
                    if (sets.Count > 0) {
                        Console.WriteLine(" 单个：只把被覆盖过的那几个文件写回去");
                        for (var n = 0; n < sets.Count; n++)
                            Console.WriteLine($"   [{snaps.Count + n + 1}] {sets[n].Stamp}   {sets[n].Items.Count} 个文件");
                    }
                    Console.Write(" 选哪个？(序号 / 回车 = 取消)：");
                    var pick = ReadKeyLine()?.Trim();
                    if (int.TryParse(pick, out var pi) && pi >= 1 && pi <= snaps.Count + sets.Count) {
                        if (pi <= snaps.Count) {
                            RestoreGgpk(store.IndexFile, snaps[pi - 1].Stamp, ask: true);
                        } else {
                            Console.Write($" 确认用 {sets[pi - snaps.Count - 1].Stamp} 覆盖当前的译文？(y = 确认)：");
                            if ((ReadKeyLine() ?? "").Trim().ToLowerInvariant() is "y" or "yes")
                                RestoreBackup(store, sets[pi - snaps.Count - 1].Stamp);
                        }
                    }
                }
                Console.WriteLine();
                Console.WriteLine(" 按回车返回菜单");
                ReadKeyLine();
                Reopen();
                items = Preflight(_store!);
                continue;
            }

            Flavor? target = k switch {
                "1" => Flavor.Translated,
                "2" => Flavor.Dual,
                "3" => Flavor.DualAll,
                "4" => Flavor.Original,
                _ => null,
            };
            if (target is null) { Console.WriteLine(" 没看懂，回车继续"); ReadKeyLine(); continue; }

            var bad = items.Count(i => i.CurFlavor == Flavor.Unknown);
            var partial = false;
            if (bad > 0) {
                Console.WriteLine();
                Console.WriteLine($" 有 {bad} 个文件与数据包对不上（客户端版本不同），正常安装会整体拒绝写入。");
                Console.Write(" 只处理对得上的那些？(y = 部分安装，其余保持官方原文 / 回车 = 放弃)：");
                var a = ReadKeyLine();
                if (a is null || !a.Trim().Equals("y", StringComparison.OrdinalIgnoreCase)) continue;
                partial = true;
            }

            var r = ApplyInPlace(store, items, target.Value, partial);
            if (r != 0) { Console.WriteLine(); Console.WriteLine(" 按回车返回菜单"); ReadKeyLine(); }
            if (Reopen() is not null) items = Preflight(_store!);
        }
    }

    private static string? ReadKeyLine() {
        try {
            if (Console.IsInputRedirected) {
                var l = Console.ReadLine();
                return l;
            }
            return Console.ReadLine();
        } catch { return null; }
    }

    private static int RunInstall(IContainer store, Flavor f) {
        var items = Preflight(store);
        PrintHeader(items, store);
        return ApplyInPlace(store, items, f, Args.ContainsKey("partial"));
    }

    // 容器里的文件原样导出（逗号分隔多个），排查结构与编码问题用，只读
    private static int Dump(IContainer store, string inner) {
        var dir = Args.GetValueOrDefault("out") ?? "";
        if (dir.Length == 0) dir = "dump";
        Directory.CreateDirectory(dir);
        foreach (var p in inner.Split(',', StringSplitOptions.RemoveEmptyEntries)) {
            if (!store.TryRead(p.Trim(), out var b, out var err)) {
                Console.WriteLine($"  ✗ {p,-46} {err ?? "找不到"}");
                continue;
            }
            var f = System.IO.Path.Combine(dir, System.IO.Path.GetFileName(p.Trim()));
            File.WriteAllBytes(f, b);
            Console.WriteLine($"  {p.Trim(),-46} {b.Length,10:N0} B -> {f}");
        }
        return 0;
    }

    private static int ShowStatus(IContainer store) {
        var items = Preflight(store);
        PrintHeader(items, store);
        foreach (var i in items)
            Console.WriteLine($"  {i.Label,-46} {(i.Missing ? "缺失" : ShowFlavor(i)),-8} {i.Cur.Length,12:N0} B"
                              + (i.Why.Length > 0 ? "   " + i.Why : ""));
        // 语系这一项不在 items 里（它不是译文文件），单独报一行：
        // 没启用的话，就算译文全都装好了，游戏里看到的还是英文。
        var langLabel = "[语系] " + LanguageSwitch.Path;
        if (store.TryRead(LanguageSwitch.Path, out var lang, out _))
            Console.WriteLine($"  {langLabel,-46} {LanguageSwitch.Describe(LanguageSwitch.Detect(lang))}");
        else
            Console.WriteLine($"  {langLabel,-46} 读不到，装补丁时会跳过语系切换");
        // 国旗同理：繁中那一项的语言代码是 fr，配图默认还是法国国旗
        var flagLabel = "[国旗] " + FlagIcon.Path;
        if (store.TryRead(FlagIcon.Path, out var flag, out _))
            Console.WriteLine($"  {flagLabel,-46} {FlagIcon.Describe(FlagIcon.Detect(flag))}");
        else
            Console.WriteLine($"  {flagLabel,-46} 读不到，装补丁时会跳过国旗替换");
        return 0;
    }

    private static int ApplyInPlace(IContainer store, List<Item> items, Flavor target, bool partial = false) {
        if (!store.Alive) {
            Console.WriteLine("容器已经释放（采集或结构演练之后没有重新打开），这次一个都没写。请重新运行本程序。");
            return 6;
        }
        var missing = items.Where(i => i.Missing).ToList();
        var broken = items.Where(i => i.Broken).ToList();
        var unknown = items.Where(i => i.CurFlavor == Flavor.Unknown && !i.Missing && !i.Broken).ToList();

        // 遇到「被别人改过、和我们的基线对不上」的文件，只有四条路，没有第五条：
        //   merge    在对得上结构的前提下，只把译文叠上去，别人的改动一律保留（默认）
        //   ours     整份换成我们验过的成品（会盖掉别人的改动，但原字节先备份）
        //   original 还原成官方原文（等于把这个文件的汉化撤销掉）
        //   skip     一个字都不动，保持它现在能被游戏读到的样子
        // 无论哪条，容器里最终都必须是「一份完整可读的文件」，绝不能是两种内容拼出来的半成品。
        var policy = ParsePolicy(Args.GetValueOrDefault("on-modified"));
        // .dat 表内嵌了官方原文与成品两套字节，所以 Unknown 也能整份换；
        // statdesc 没有内嵌原文字节，能做的只有「逐行把我们写过的行撤回去」——
        // 认不出的行（别的汉化写的）一个字节都不动，所以它仍然是完整可读的一份文件。
        var forced = policy switch {
            Policy.Ours => unknown.Where(i => !i.IsSd).ToList(),
            Policy.Original => unknown.ToList(),
            // 整体还原时也尽量退回原文，否则「还原」会卡在几个认不出的文件上走不下去
            _ => target == Flavor.Original ? unknown.ToList() : new List<Item>(),
        };
        var skip = missing.Concat(broken).Concat(unknown.Except(forced)).ToList();

        if (broken.Count > 0) {
            Console.WriteLine();
            Console.WriteLine($"!! {broken.Count} 个目标文件数据已损坏，读不出来：");
            foreach (var u in broken.Take(6)) Console.WriteLine($"    {u.Label}  —  {u.BrokenWhy}");
            if (broken.Count > 6) Console.WriteLine($"    ... 还有 {broken.Count - 6} 个");
            Console.WriteLine("  这些文件的索引记录指向一个已经被删掉的自建 bundle（Bundles2/LibGGPK3/*.bundle.bin）。");
            Console.WriteLine("  内容无法在本机重建，先修客户端再打补丁：");
            Console.WriteLine("    Steam 版 → 库 → 右键游戏 → 属性 → 已安装文件 → 验证文件完整性");
            Console.WriteLine("    （只会重下 _.index.bin，约 20 MB；我们写的 bundle 不在清单里，删掉不影响校验）");
        }

        if (skip.Count > 0) {
            if (!partial) {
                if (missing.Count > 0) {
                    Console.WriteLine($"中止：{missing.Count} 个目标文件在 {store.Kind} 里找不到，一个都没写。");
                    foreach (var u in missing.Take(6)) Console.WriteLine($"    {u.Label}");
                    if (missing.Count > 6) Console.WriteLine($"    ... 还有 {missing.Count - 6} 个");
                    Console.WriteLine("  这通常是客户端语言/版本与数据包不同（国服简中、Steam 刚更新等）。");
                }
                if (unknown.Count > 0) {
                    Console.WriteLine($"中止：{unknown.Count} 个文件内容与数据包不符，一个都没写。");
                    foreach (var u in unknown.Take(8)) Console.WriteLine($"    {u.Label}  {u.Cur.Length:N0} B  {u.Why}");
                    if (unknown.Count > 8) Console.WriteLine($"    ... 还有 {unknown.Count - 8} 个");
                    Console.WriteLine("  可能游戏更新过、这是简中客户端，或者被别的汉化改过。现在三种选择：");
                    Console.WriteLine("    --on-modified=skip      保持它们现在的样子（游戏能正常读），只翻能对上的");
                    Console.WriteLine("    --on-modified=ours      整份换成我们验过的版本（原字节会先备份）");
                    Console.WriteLine("    --on-modified=original  把这几个还原成官方原文（撤销它们的汉化）");
                    Console.WriteLine("  后面两种只对 .dat 表有效；statdesc 的原文丢了就重建不出来，仍然会跳过。");
                }
                Console.WriteLine("  想先验通道可以用 --struct；只想先翻能对上的那些，可以用 --partial（跳过对不上的）。");
                return 5;
            }
            Console.WriteLine($"部分安装：跳过 {skip.Count} 个与数据包不符的文件，它们保持官方原文");
            foreach (var u in skip.Take(10)) Console.WriteLine($"    跳过 {u.Label}");
            if (skip.Count > 10) Console.WriteLine($"    ... 还有 {skip.Count - 10} 个");
            Console.WriteLine("  （想把这些也翻掉，用 --collect 采集诊断包发给作者适配）");
            Console.WriteLine();
        }

        if (!VersionOk()) {
            if (!partial) {
                Console.WriteLine($"中止：游戏版本与数据包不一致（数据包 {_gameClient}）。");
                Console.WriteLine("  重启游戏让它更新一次日志，或等新版数据包。");
                return 4;
            }
            Console.WriteLine($"注意：游戏版本与数据包不一致（数据包 {_gameClient}），部分安装会跳过对不上的文件。");
            Console.WriteLine();
        }

        var plan = Args.ContainsKey("plan");
        var pool = partial
            ? items.Where(i => i.CurFlavor != Flavor.Unknown || forced.Contains(i)).ToList()
            : items;
        if (forced.Count > 0 && !partial) {
            Console.WriteLine($"  按 --on-modified={PolicyName(policy)} 处理 {forced.Count} 个对不上基线的数据表"
                              + "（原字节会先备份，随时可以还原）");
        }

        // 历史自建 bundle（历次运行留下的 LibGGPK3/N）里记的 offset / UncompressedSize
        // 已经和物理文件对不上：复用它就是 Buffer Underflow 的来源。
        // 先只做只读清点，把还挂在旧记录上的文件圈出来；确认真的要写之后才换新 bundle。
        var inherited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!plan)
            foreach (var i in items)
                if (store.IsSelfBundle(i.Path)) inherited.Add(i.Path);
        if (inherited.Count > 0) {
            var movable = items.Count(i => inherited.Contains(i.Path) && i.CurFlavor != Flavor.Unknown);
            foreach (var i in items)
                if (inherited.Contains(i.Path) && i.CurFlavor != Flavor.Unknown) i.Force = true;
            Console.WriteLine($"  {inherited.Count} 个文件还挂在历史自建 bundle 上，其中 {movable} 个可以重新落一遍");
        }

        var todo = pool.Where(i => i.Force || EffectiveFlavor(i, target) != i.CurFlavor).ToList();
        Console.WriteLine($"目标：{FlavorName(target)}    需要改动 {todo.Count} 个文件（可用 {pool.Count} / 共 {items.Count} 个）");

        // --only=<关键词>：只动匹配的文件。用来在刚修好的客户端上做最小验证 ——
        // 先打 1 个文件、启动游戏确认没问题，再全量。失败也只需要还原那一两个。
        if (Args.TryGetValue("only", out var onlyPat) && onlyPat.Length > 0) {
            var all = todo.Count;
            todo = todo.Where(x =>
                x.Label.Contains(onlyPat, StringComparison.OrdinalIgnoreCase) ||
                x.Path.Contains(onlyPat, StringComparison.OrdinalIgnoreCase)).ToList();
            Console.WriteLine($"  只处理匹配 \"{onlyPat}\" 的 {todo.Count} 个文件（本来要改 {all} 个）");
            if (todo.Count == 0) {
                Console.WriteLine("  没有匹配的文件，什么都没做。");
                return 0;
            }
        }

        if (todo.Count == 0) {
            Console.WriteLine("已经就是这个版本，无需改动。");
            // 译文不用动，但语系/国旗可能还没处理 —— 用旧版补丁器打过的客户端就是这种状态：
            // 文件早就是全量对照了，语系却一次都没写进去过。以前这里直接 return，
            // 于是「重跑一次新补丁器」永远补不上语系。
            if (!Args.ContainsKey("plan")) {
                // 语系与国旗同样是「写文件」，所以同样要有还原点：写坏了也是游戏读不出来。
                var t2 = BeginRollback(store);
                if (t2 is null && !AllowNoRollback()) {
                    Console.WriteLine("中止：无法创建还原点，语系与国旗一个字都没写。");
                    Console.WriteLine("  · 确认游戏已退出、磁盘还有空间（索引约 20 MB）");
                    return 7;
                }
                var n0 = 0;
                _txn = t2;
                try {
                    n0 = ApplyLocale(store, target);
                    if (t2 is not null) t2.MarkSaved();
                } catch (Exception ex) {
                    Console.WriteLine($"  ✗ {ex.GetType().Name}: {ex.Message}");
                    n0 = 1;
                } finally { _txn = null; }
                if (n0 > 0) {
                    CloseStore();            // 回滚要独占写句柄，两个容器都是
                    var err = t2?.Rollback();
                    Console.WriteLine(err is null
                        ? "  已回滚，语系与国旗保持原样，游戏照常能读。"
                        : $"  ! 回滚时出错：{err}（备份在 {t2?.Dir}）");
                    return 3;
                }
                t2?.Commit();
                Console.WriteLine();
                Console.WriteLine("现在可以启动游戏了。");
            }
            return 0;
        }

        var legacy = todo.Where(i => i.CurFlavor == Flavor.Custom && i.Legacy).ToList();
        if (legacy.Count > 0) {
            Console.WriteLine();
            Console.WriteLine($"其中 {legacy.Count} 个文件是上一版补丁写的（sha 对不上，但逐行认得出来）：");
            foreach (var u in legacy.Take(6)) Console.WriteLine($"    {u.Label}  —  {u.Why}");
            if (legacy.Count > 6) Console.WriteLine($"    ... 还有 {legacy.Count - 6} 个");
            Console.WriteLine("  按当前数据包重写这些行，文件其余部分原样保留。");
        }
        var custom = todo.Where(i => i.CurFlavor == Flavor.Custom && !i.Legacy).ToList();
        var customSd = custom.Where(i => i.IsSd).ToList();
        if (customSd.Count > 0) {
            Console.WriteLine();
            Console.WriteLine($"其中 {customSd.Count} 个文件的标记被外部程序改过（sha 对不上，但行数与文本层没变）：");
            foreach (var u in customSd.Take(6)) Console.WriteLine($"    {u.Label}  —  {u.Why}");
            if (customSd.Count > 6) Console.WriteLine($"    ... 还有 {customSd.Count - 6} 个");
            Console.WriteLine("  对它们只叠译文：留着标记的（换色/换标签）原样不动；");
            Console.WriteLine("  被整段删光的（原版有、这里寸草不生）按官方原样补回 —— 那是覆盖伤，不是风格。");
        }
        var customDat = custom.Where(i => !i.IsSd).ToList();
        if (customDat.Count > 0) {
            Console.WriteLine();
            Console.WriteLine($"其中 {customDat.Count} 个数据表被外部程序改过（sha 对不上，但行数与待改行都没变）：");
            foreach (var u in customDat.Take(6)) Console.WriteLine($"    {u.Label}  —  {u.Why}");
            if (customDat.Count > 6) Console.WriteLine($"    ... 还有 {customDat.Count - 6} 个");
            Console.WriteLine("  对它们只动我们那几处：新串追加到池尾、改指向，别人的行一个字节都不碰。");
        }
        Console.WriteLine();

        // ---- 事务开始：先拿到「退路」，再动第一个字节 ----
        // 没有还原点就不写：宁可这次不装，也不能留下一个游戏读不出来的客户端。
        IRollback? txn = null;
        BackupSet? backupSet = null;
        if (!plan) {
            txn = BeginRollback(store);
            if (txn is null && !AllowNoRollback()) {
                Console.WriteLine("中止：无法创建还原点（_.index.bin 复制失败，或磁盘满了），一个字节都没写。");
                Console.WriteLine($"  目标：{store.IndexFile}");
                Console.WriteLine("  · 确认游戏已退出、磁盘还有空间（索引约 20 MB）");
                return 7;
            }
            if (txn is not null) {
                Console.WriteLine($"  还原点：{txn.Dir}");
                _txn = txn;
            } else {
                Console.WriteLine("  注意：这个容器没法整份备份索引，只提供文件级备份。写入失败时请按提示手动还原。");
            }
            backupSet = BackupStore.Create(store.IndexFile);
        }

        // 到这里确定要写了，才换新 bundle：一次写入只落一条新记录，绝不碰历史记录
        if (!plan && inherited.Count > 0) {
            store.Detach();
            _detachedThisRun = true;
            txn?.AddBundle(store.LastBundle);
        }

        var backupRoot = backupSet?.Dir ?? "";
        var sw = Stopwatch.StartNew();
        var nDone = 0;
        var nFail = 0;
        var idx = 0;
        var written = new List<(Item it, string sha)>();
        // 演练用：故意在第 N 个文件上失败，验证「一失败就整份退回」这条路真的能走通。
        // 只在手动排障时用，正常安装不会传。
        var simFail = int.TryParse(Args.GetValueOrDefault("simulate-fail"), out var sf) ? sf : 0;
        if (plan) Console.WriteLine("（--plan：只演算并校验，不写任何文件）");
        foreach (var i in todo) {
            idx++;
            var label = i.Label.Length > 44 ? i.Label[..44] : i.Label;
            try {
                if (simFail > 0 && idx == simFail)
                    throw new InvalidOperationException($"模拟第 {idx} 个文件写入失败（--simulate-fail）");
                byte[] targetBytes;
                string? expectSha;
                // --on-modified=original：被外部改过的文件一律退回官方原文，与目标版本无关
                var effTarget = policy == Policy.Original && i.CurFlavor is Flavor.Custom or Flavor.Unknown
                    ? Flavor.Original
                    : target;
                var others = i.CurFlavor is Flavor.Custom or Flavor.Unknown;
                if (i.IsSd) {
                    var e = i.Sd!;
                    if (i.CurFlavor == Flavor.Unknown) {
                        // 行数/文本都对不上，重建不出「整份原文」。能做的只有逐行把我们写过的行撤回去，
                        // 认不出的行（别的汉化写的）原样保留 —— 文件仍然完整可读，不会变成半截。
                        targetBytes = RewriteUnknownToOriginal(e, i.Cur, out var nRev, out var nKeep);
                        expectSha = null;
                        Console.WriteLine($"  {label,-46} 逐行退回原文：撤销 {nRev} 行，{nKeep} 行认不出（保持原样）");
                    } else {
                        // 底稿是别人的：merge 只叠文本层（别人的标记/颜色原样保留）；
                        // ours 连标记一起换回我们的，等于用我们的版本覆盖这一行
                        targetBytes = Rewrite(e, i.Cur, i.CurFlavor, effTarget,
                                              forceOurs: policy == Policy.Ours && i.CurFlavor == Flavor.Custom);
                        expectSha = TargetSha(e, effTarget, i.CurFlavor);
                    }
                } else {
                    var e = i.Dat!;
                    if (others && policy == Policy.Merge && i.CurFlavor == Flavor.Custom) {
                        // 底稿是别人的：只把我们的改动叠上去；还原时反向叠回来，对方自己的改动保留
                        targetBytes = DatMerge.Apply(e, i.Cur, revert: effTarget == Flavor.Original);
                        expectSha = null;
                    } else {
                        // 其余情况都用数据包里验过的整份字节：官方原文或我们的成品。
                        // 别人的改动不会有一处混进来 —— 这比「合并出个四不像」安全。
                        targetBytes = effTarget == Flavor.Original ? e.Orig : e.Patch;
                        expectSha = effTarget == Flavor.Original ? e.ShaOrig : e.ShaPatch;
                    }
                }
                var got = Sha(targetBytes);
                if (expectSha is not null && got != expectSha) {
                    Console.WriteLine($"  [{idx,2}/{todo.Count}] {label,-46} ✗ 生成内容 sha 不符（拒绝写入）");
                    // 逐行文本都对得上，只有「没动的部分」不同 —— 那是客户端文件本身和我们的基线不是同一份
                    var hint = i.IsSd
                        ? $"客户端 {SplitLines(i.Cur).Count:N0} 行 / 基线 {i.Sd!.BaseLines:N0} 行"
                        : "字节数不同";
                    Console.WriteLine($"      未改动的部分和基线不一致（{hint}）：这份客户端不是数据包锁定的 {_gameClient} 版本，");
                    Console.WriteLine($"      按它写会得到一份谁都没验过的混合文件，所以不写。先升级补丁器；补丁器已是最新就重新采集。");
                    nFail++;
                    continue;
                }
                expectSha ??= got;       // 外部改过的底稿：结果自带身份，回读就拿它比
                if (plan) {
                    // 外部改过的底稿没有预期 sha，但可以顺手核对「算出来的和标准产物是不是同一份」
                    var hint = i.IsSd ? TargetSha(i.Sd!, target) : (target == Flavor.Original ? i.Dat!.ShaOrig : i.Dat!.ShaPatch);
                    var same = hint is not null && hint == got;
                    Console.WriteLine($"  [{idx,2}/{todo.Count}] {label,-46} {ShowFlavor(i)} → {FlavorName(EffectiveFlavor(i, target))}  ✓ 演算通过"
                                      + (same ? "" : "（与标准产物不同，按底稿内容生成）"));
                    nDone++;
                    continue;
                }
                // 覆盖之前先把「现在这份」落盘。如果它是别的汉化改过的，
                // 这份字节在这台机器之外不存在第二个副本，丢了就永久没了。
                if (backupSet is not null) BackupStore.Add(backupSet, i.Path, i.Cur, ShowFlavor(i));

                var t0 = Stopwatch.StartNew();
                if (!store.Write(i.Path, targetBytes)) {
                    Console.WriteLine($"  [{idx,2}/{todo.Count}] {label,-46} ✗ {store.Kind} 里找不到");
                    nFail++;
                    continue;
                }
                written.Add((i, expectSha!));
                txn?.AddWritten(i.Path, expectSha!);
                Console.WriteLine($"  [{idx,2}/{todo.Count}] {label,-46} {ShowFlavor(i)} → {FlavorName(EffectiveFlavor(i, effTarget))}  {t0.ElapsedMilliseconds} ms");
                nDone++;
            } catch (Exception ex) {
                Console.WriteLine($"  [{idx,2}/{todo.Count}] {label,-46} ✗ {ex.GetType().Name}: {ex.Message}");
                nFail++;
                if (ex is ObjectDisposedException) {
                    Console.WriteLine("  容器被中途释放了，剩下的文件没有写。请重新运行本程序。");
                    break;
                }
            }
        }

        // Bundles2 的 index 是最后统一落盘的；ggpk 每写一次就已经进文件
        if (written.Count > 0) {
            var tf = Stopwatch.StartNew();
            try {
                store.Flush();
                txn?.MarkSaved();      // 索引已经落盘，从这一刻起「写」成功了
                Console.WriteLine($"  落盘索引  {store.Kind}  {tf.ElapsedMilliseconds} ms");
            } catch (Exception ex) {
                Console.WriteLine($"  ✗ 索引落盘失败：{ex.GetType().Name}: {ex.Message}");
                nFail++;
            }
        }

        // ---- 语系与国旗 ----
        if (!plan && nFail == 0) nFail += ApplyLocale(store, target);

        // ---- 落盘校验：关掉索引重新打开，按游戏的启动方式把写过的文件逐个读回来 ----
        // 以前是拿同一个内存索引对象回读，索引写错了也照样读得出来（假阳性），
        // 而这个错要到游戏启动时才炸成 bad allocation / Buffer Underflow。
        var nBack = 0;
        if (!plan && written.Count > 0 && nFail == 0) nBack = ReloadVerify(written, ref nFail);

        Console.WriteLine();

        // ---- 判定：要么整份成功，要么整份退回。不存在「一半新一半旧」这种状态。----
        if (!plan) {
            _txn = null;
            if (nFail > 0) {
                Console.WriteLine("  有项目没有通过校验，正在整份退回写入前的状态 …");
                CloseStore();                       // 先放掉写句柄，免得库在 Dispose 时又往盘上补一笔
                var err = txn?.Rollback();
                if (txn is null) {
                    Console.WriteLine("  ! 这个容器没有还原点，没法自动回滚。");
                    Console.WriteLine($"    被覆盖的原始内容在：{backupRoot}");
                    if (store.Kind == "Bundles2")
                        Console.WriteLine("    Steam 版可以「验证文件完整性」，只会重下 _.index.bin（约 20 MB）。");
                } else if (err is null) {
                    Console.WriteLine("  ✓ 已回滚：索引和本次新建的 bundle 都退回去了，游戏能读到写入前的内容。");
                    Console.WriteLine("    直接重跑本程序即可，不需要验证游戏完整性。");
                } else {
                    Console.WriteLine($"  ! 回滚时有问题：{err}");
                    Console.WriteLine($"    还原点在：{txn.Dir}（用 --restore-ggpk / --backups 可以再退一次）");
                }
                Console.WriteLine($"失败 {nFail} 个，{nDone} 个已写的已一并撤销 —— 客户端仍是写入前的状态。");
                return 3;
            }
            txn?.Commit();
            if (backupSet is not null) BackupStore.DropIfEmpty(backupSet);
            try { store.TrimAfterCommit(); } catch (Exception ex) {
                Console.WriteLine($"  ! 清理旧 bundle 时出错（不影响游戏）：{ex.Message}");
            }
        }

        Console.WriteLine(plan
            ? $"演算 {nDone} 个（未写入任何文件），失败 {nFail} 个，用时 {sw.Elapsed.TotalSeconds:F1} s"
            : $"完成 {nDone} 个，回读通过 {nBack} 个，失败 {nFail} 个，用时 {sw.Elapsed.TotalSeconds:F1} s");
        if (!plan && nDone > 0 && backupRoot.Length > 0) Console.WriteLine($"备份：{backupRoot}（用 --restore-backup 可以整份写回去）");
        if (nFail == 0 && !plan) {
            Console.WriteLine();
            Console.WriteLine("现在可以启动游戏了。");
            if (target != Flavor.Original) Console.WriteLine("想换版本或还原，随时再把数据拖进来。");
            if (partial)
                Console.WriteLine($"注意：跳过的 {skip.Count} 个文件保持原样（没有改动过）。要连它们一起翻，用 --on-modified=ours 或采集诊断包。");
        }
        return nFail == 0 ? 0 : 3;
    }

    // 落盘校验：关掉索引、重新打开，再按路径把写过的文件读回来比 sha。
    // 这是唯一能发现「索引记录写错」的检查 —— 同一个内存索引对象回读永远是对的。
    private static int ReloadVerify(List<(Item it, string sha)> written, ref int nFail) {
        Console.WriteLine();
        Console.WriteLine("  关掉索引重新打开，逐个回读校验 …");
        if (Reopen() is null) {
            Console.WriteLine("  ✗ 索引重新打开失败，客户端已无法按游戏的方式读取");
            Console.WriteLine("    Steam 版：Steam 库 → 右键游戏 → 属性 → 已安装文件 → 验证文件完整性（只会重下 _.index.bin，约 20 MB）");
            nFail += written.Count;
            return 0;
        }
        var s = _store!;
        var ok = 0;
        foreach (var (it, sha) in written) {
            try {
                if (!s.TryRead(it.Path, out var back, out var err)) {
                    Console.WriteLine($"  ✗ {it.Label,-46} 读不出来：{err ?? "索引里找不到"}");
                    nFail++;
                    continue;
                }
                if (Sha(back) != sha) {
                    Console.WriteLine($"  ✗ {it.Label,-46} 落盘内容与写入的不一致（{back.Length:N0} B）");
                    nFail++;
                    continue;
                }
                ok++;
            } catch (Exception ex) {
                Console.WriteLine($"  ✗ {it.Label,-46} 回读异常：{ex.GetType().Name}: {ex.Message}");
                nFail++;
            }
        }
        Console.WriteLine($"  重新打开后回读：{ok}/{written.Count} 通过");
        return ok;
    }

    // 语系与国旗：译文写完之后的两步收尾，等价于 PoeChinese3 做的那两件事。
    // 不做语系这一步，译文写得再全，游戏里还是英文（或法文）。
    // 装补丁 → 启用；还原 → 关掉；已是目标状态就不写（幂等）。
    private static int ApplyLocale(IContainer store, Flavor target) {
        var nFail = 0;
        var want = target != Flavor.Original;

        // 语系与国旗也是写文件，同样要落在新 bundle 上。已经换过就不再换（一次运行只留一条新记录）。
        if (!_detachedThisRun && (store.IsSelfBundle(LanguageSwitch.Path) || store.IsSelfBundle(FlagIcon.Path))) {
            store.Detach();
            _detachedThisRun = true;
            _txn?.AddBundle(store.LastBundle);
        }

        if (store.TryRead(LanguageSwitch.Path, out var langCur, out var langErr)) {
            if (LanguageSwitch.TryBuild(langCur, want, out var langNext, out var langMsg)) {
                if (store.Write(LanguageSwitch.Path, langNext!)) {
                    store.Flush();
                    var ok = store.TryRead(LanguageSwitch.Path, out var langBack) && Sha(langBack) == Sha(langNext!);
                    if (ok) Console.WriteLine($"  {LanguageSwitch.Path,-46} -> {langMsg}");
                    else { Console.WriteLine($"  ✗ {LanguageSwitch.Path} 回读不符，语系可能没写进去"); nFail++; }
                } else { Console.WriteLine($"  ✗ {LanguageSwitch.Path} 写入失败"); nFail++; }
            } else {
                Console.WriteLine($"  {LanguageSwitch.Path,-46} = {langMsg}");
            }
        } else {
            Console.WriteLine($"  ! 读不到 {LanguageSwitch.Path}（{langErr ?? "路径不存在"}），语系未处理");
        }

        // 语系切到繁中之后，列表那一项的语言代码仍是 fr，配图会拿法国国旗。
        // 图集里本来就有五星红旗（zhCN），把两行的图标名对调即可，不碰任何 DDS。
        if (store.TryRead(FlagIcon.Path, out var flagCur, out var flagErr)) {
            if (FlagIcon.TryBuild(flagCur, want, out var flagNext, out var flagMsg)) {
                if (store.Write(FlagIcon.Path, flagNext!)) {
                    store.Flush();
                    var ok = store.TryRead(FlagIcon.Path, out var flagBack) && Sha(flagBack) == Sha(flagNext!);
                    if (ok) Console.WriteLine($"  {FlagIcon.Path,-46} -> {flagMsg}");
                    else { Console.WriteLine($"  ✗ {FlagIcon.Path} 回读不符，国旗可能没写进去"); nFail++; }
                } else { Console.WriteLine($"  ✗ {FlagIcon.Path} 写入失败"); nFail++; }
            } else {
                Console.WriteLine($"  {FlagIcon.Path,-46} = {flagMsg}");
            }
        } else {
            Console.WriteLine($"  ! 读不到 {FlagIcon.Path}（{flagErr ?? "路径不存在"}），国旗未处理");
        }
        return nFail;
    }

    private static Flavor EffectiveFlavor(Item i, Flavor target) {        // dat 表只有「原文 / 已打补丁」两态：三个 flavor 共用同一份补丁字节
        if (!i.IsSd) return target == Flavor.Original ? Flavor.Original : Flavor.DualAll;
        var e = i.Sd!;
        // 该文件在这个 flavor 下没有专门内容时，等于原文
        return target switch {
            Flavor.Translated => e.ShaTranslated is null ? Flavor.Original : Flavor.Translated,
            Flavor.Dual => e.ShaDual is null ? Flavor.Original : Flavor.Dual,
            _ => target,
        };
    }

    // 遇到「被别人改过、与数据包对不上」的文件时的处置方式。见 ApplyInPlace 开头。
    private enum Policy { Merge, Ours, Original, Skip }

    private static Policy ParsePolicy(string? s) => (s ?? "").Trim().ToLowerInvariant() switch {
        "" or "merge" => Policy.Merge,
        "ours" or "mine" => Policy.Ours,
        "original" or "orig" or "restore" => Policy.Original,
        "skip" or "off" => Policy.Skip,
        _ => Policy.Merge,
    };

    private static string PolicyName(Policy p) => p switch {
        Policy.Ours => "ours",
        Policy.Original => "original",
        Policy.Skip => "skip",
        _ => "merge",
    };

    // ---------------------------------------------------------------- 备份与还原
    //
    // 事务管的是「索引还能不能读」；这份备份管的是另一件事：被我们覆盖掉的那份内容
    // 本身还有没有。尤其是别的汉化改过、和我们对不上的文件 —— 那份字节只存在于这台机器上。

    private static int ShowBackups(IContainer store) {
        var snaps = GgpkUndo.List(store.IndexFile);
        if (snaps.Count > 0) {
            Console.WriteLine($"ggpk 整份还原点：{GgpkUndo.RootFor(store.IndexFile)}");
            Console.WriteLine("  （写入前逐块记下被覆盖的原字节，能把整个 ggpk 退回那一刻）");
            Console.WriteLine();
            foreach (var m in snaps) {
                var mb = m.RecordBytes / 1048576.0;
                var state = m.Phase == "restored" ? "已还原过" : "可用";
                Console.WriteLine($"  [{m.Stamp}]  {m.Created}  {mb:F1} MB  {state}");
                Console.WriteLine($"      写入前 {m.OrigLength:N0} B → 写入后 {m.SavedLength:N0} B，{m.Records} 块原地覆盖");
            }
            Console.WriteLine();
            Console.WriteLine("整份退回去：PoE-zh-Patcher.exe <Content.ggpk> --restore-ggpk=latest");
            Console.WriteLine();
        }

        var all = BackupStore.List(store.IndexFile);
        if (all.Count == 0) {
            Console.WriteLine("还没有文件级备份。装补丁时会自动把「被覆盖前的原字节」存到 .poe-zh/backup/ 下。");
            return 0;
        }
        Console.WriteLine($"文件级备份：{BackupStore.RootFor(store.IndexFile)}");
        Console.WriteLine();
        foreach (var s in all) {
            Console.WriteLine($"  [{s.Stamp}]  {s.Items.Count} 个文件  {s.Items.Sum(i => i.Bytes) / 1048576.0:F1} MB");
            if (ReferenceEquals(s, all[0])) {
                foreach (var it in s.Items.Take(6)) Console.WriteLine($"      {it.Flavor,-8} {it.Path}");
                if (s.Items.Count > 6) Console.WriteLine($"      ... 还有 {s.Items.Count - 6} 个");
            }
        }
        Console.WriteLine();
        Console.WriteLine("整份写回去：PoE-zh-Patcher.exe <目标> --restore-backup=latest");
        return 0;
    }

    // 把整个 ggpk 退回某次安装之前。不需要 Steam、不需要重下 74 GB ——
    // 逐块写回原字节，每块写完立刻读回来比对。
    private static int RestoreGgpk(string target, string which, bool ask) {
        var m = GgpkUndo.Find(target, which);
        if (m is null) {
            Console.WriteLine($"找不到 ggpk 还原点「{which}」，用 --backups 看看有哪些。");
            return 1;
        }
        var mb = m.RecordBytes / 1048576.0;
        Console.WriteLine($"用还原点 [{m.Stamp}]（{m.Created}，{mb:F1} MB）退回 {m.Target}");
        Console.WriteLine($"  那时长度 {m.OrigLength:N0} B，现在是 {new FileInfo(m.Target).Length:N0} B");
        if (ask) {
            Console.WriteLine("  这份快照只对「装完之后没再更新过」的客户端有效。");
            Console.Write("  确认退回？(y = 确认)：");
            if ((ReadKeyLine() ?? "").Trim().ToLowerInvariant() is not ("y" or "yes")) {
                Console.WriteLine("  已取消，一个字节都没改。");
                return 0;
            }
        }
        CloseStore();                 // 还原要独占写句柄
        var err = GgpkUndo.Restore(target, m, out var blocks);
        if (err is not null) {
            Console.WriteLine($"  ✗ 还原时出错：{err}");
            Console.WriteLine($"    快照还留着：{Path.Combine(GgpkUndo.RootFor(target), m.Stamp)}");
            Console.WriteLine("    可以再跑一次 --restore-ggpk；实在不行就重装客户端。");
            return 3;
        }
        Console.WriteLine($"  ✓ 已退回：{blocks} 块原字节写回并逐块读回比对一致，游戏读到的就是那一刻的内容。");
        Console.WriteLine("    现在可以启动游戏了。");
        return 0;
    }

    private static int RestoreBackup(IContainer store, string which) {
        if (!store.Alive) {
            Console.WriteLine("容器已经释放，请重新运行本程序。");
            return 6;
        }
        var set = BackupStore.Find(store.IndexFile, which);
        if (set is null) {
            Console.WriteLine($"找不到备份集「{which}」，用 --backups 看看有哪些。");
            return 1;
        }
        Console.WriteLine($"用备份 [{set.Stamp}] 写回 {set.Items.Count} 个文件");
        Console.WriteLine($"  来源：{set.Dir}");
        Console.WriteLine("  （写之前会再建一个还原点，写坏了同样能退回来）");
        Console.WriteLine();

        var txn = BeginRollback(store);
        if (txn is null && !AllowNoRollback()) {
            Console.WriteLine("中止：无法创建还原点，一个字节都没写。");
            return 7;
        }
        _txn = txn;
        try {
            store.Detach();
            _detachedThisRun = true;
            txn?.AddBundle(store.LastBundle);

            var done = new List<(Item it, string sha)>();
            foreach (var it in set.Items) {
                var bytes = BackupStore.Read(set, it);
                if (bytes is null) { Console.WriteLine($"  ✗ {it.Path}：备份文件不见了"); continue; }
                if (!store.Write(it.Path, bytes)) {
                    Console.WriteLine($"  ✗ {it.Path}：容器里找不到这条记录");
                    continue;
                }
                var sha = Sha(bytes);
                txn?.AddWritten(it.Path, sha);
                done.Add((new Item { Label = System.IO.Path.GetFileName(it.Path), Path = it.Path }, sha));
                Console.WriteLine($"  {it.Path,-58} ← {it.Flavor}");
            }
            if (done.Count == 0) {
                Console.WriteLine("  一个都没写回去。");
                CloseStore();
                txn?.Rollback();
                return 3;
            }
            store.Flush();
            txn?.MarkSaved();

            var nFail = 0;
            ReloadVerify(done, ref nFail);
            if (nFail > 0) {
                Console.WriteLine("  回读没通过，正在整份退回 …");
                CloseStore();
                var err = txn?.Rollback();
                Console.WriteLine(err is null ? "  ✓ 已回滚，客户端保持原样。" : $"  ! 回滚出错：{err}");
                return 3;
            }
            txn?.Commit();
            try { store.TrimAfterCommit(); } catch { }
            Console.WriteLine();
            Console.WriteLine($"已把 {set.Stamp} 的内容写回去。想再装汉化，直接跑一次安装即可。");
            return 0;
        } finally { _txn = null; }
    }

    private static string SafeName(string s) {
        foreach (var c in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return s;
    }

    // ---------------------------------------------------------------- 自检

    private static int SelfTest(IContainer store) {
        var items = Preflight(store);
        PrintHeader(items, store);
        Console.WriteLine("自检：从当前内容重建四个版本，逐个比对内嵌的 sha256");
        Console.WriteLine();

        var notOriginal = items.Where(i => i.CurFlavor != Flavor.Original).ToList();
        if (notOriginal.Count > 0) {
            Console.WriteLine($"注意：{notOriginal.Count} 个文件不是原文状态，只能验证可逆的那部分。");
            Console.WriteLine();
        }

        var bad = 0;
        var tested = 0;
        foreach (var i in items) {
            if (i.CurFlavor == Flavor.Unknown) { Console.WriteLine($"  跳过 {i.Label}（状态未知）"); continue; }
            var flavors = i.IsSd
                ? new[] { Flavor.Translated, Flavor.Dual, Flavor.DualAll, Flavor.Original }
                : new[] { Flavor.DualAll, Flavor.Original };
            foreach (var f in flavors) {
                if (i.IsSd && EffectiveFlavor(i, f) != f) continue;
                if (!i.IsSd && f != Flavor.Original && f != Flavor.DualAll) continue;
                byte[] got;
                try {
                    got = i.IsSd
                        ? Rewrite(i.Sd!, i.Cur, i.CurFlavor, f)
                        : (f == Flavor.Original ? i.Dat!.Orig : i.Dat!.Patch);
                } catch (Exception ex) {
                    Console.WriteLine($"  ✗ {i.Label} → {FlavorName(f)}：{ex.Message}");
                    bad++;
                    continue;
                }
                var expect = i.IsSd ? TargetSha(i.Sd!, f, i.CurFlavor) : (f == Flavor.Original ? i.Dat!.ShaOrig : i.Dat!.ShaPatch);
                tested++;
                if (expect is null) continue;      // 外部改过的底稿：没有预期 sha，能生成即通过
                if (Sha(got) != expect) {
                    Console.WriteLine($"  ✗ {i.Label} → {FlavorName(f)}：sha 不符");
                    bad++;
                }
            }
        }
        Console.WriteLine();
        Console.WriteLine($"自检完成：{tested} 项，异常 {bad}");
        return bad == 0 ? 0 : 6;
    }

    // ---------------------------------------------------------------- 结构演练
    //
    // 只验证「读 → 写 → 落盘 → 重新打开读回」这条通道，不碰译文内容。
    // 国服（简中）也能跑，用来模拟 Steam 的 Bundles2 结构：
    // 它的 statdescriptions 与数据包对不上，装不了译文，但读写机制完全一样。

    private static int StructTest(IContainer store, List<Item> items) {
        PrintHeader(items, store);

        var orig = new Dictionary<string, byte[]>();
        foreach (var i in items.Where(x => !x.Missing)) orig[i.Path] = i.Cur;

        Console.WriteLine($"结构演练：{orig.Count} 个目标文件（缺失 {items.Count - orig.Count} 个跳过）");
        Console.WriteLine("  1) 原样写回 → 落盘索引 → 重开校验 sha 不变");
        Console.WriteLine("  2) 抽样改 1 个文件 → 落盘 → 重开校验已变 → 写回原样 → 再校验复原");
        Console.WriteLine("  不改译文，只验通道。");
        Console.WriteLine();
        if (orig.Count == 0) { Console.WriteLine("没有可用的目标文件。"); return 6; }

        CloseStore();          // 让出 index 文件，后面每一步都新开容器
        var bad = 0;
        var sw = Stopwatch.StartNew();
        var before = Snap();

        // --- 1) 原样写回
        using (var s = OpenContainer()) {
            if (s is null) return 6;
            var n = 0;
            foreach (var kv in orig) {
                if (!s.Write(kv.Key, kv.Value)) { Console.WriteLine($"  ✗ 写入失败 {kv.Key}"); bad++; continue; }
                n++;
            }
            var t = Stopwatch.StartNew();
            s.Flush();
            Console.WriteLine($"  写入 {n} 个 + 落盘 {t.ElapsedMilliseconds} ms（合计 {sw.Elapsed.TotalSeconds:F1} s）");
        }

        using (var s = OpenContainer()) {
            if (s is null) return 6;
            var ok = 0;
            foreach (var kv in orig) {
                if (!s.TryRead(kv.Key, out var b)) { Console.WriteLine($"  ✗ 重开后读不到 {System.IO.Path.GetFileName(kv.Key)}"); bad++; continue; }
                if (Sha(b) != Sha(kv.Value)) { Console.WriteLine($"  ✗ 重开后内容变了 {System.IO.Path.GetFileName(kv.Key)}"); bad++; continue; }
                ok++;
            }
            Console.WriteLine($"  重开校验 {ok}/{orig.Count} 一致");
        }

        // --- 2) 真改一次再复原
        var probe = orig.Keys.First();
        var mod = orig[probe].Concat(new byte[] { 0x20, 0x00 }).ToArray();
        using (var s = OpenContainer()) {
            if (s is null) return 6;
            if (!s.Write(probe, mod)) { Console.WriteLine("  ✗ 写入失败"); bad++; }
            s.Flush();
        }
        using (var s = OpenContainer()) {
            if (s is null) return 6;
            if (!s.TryRead(probe, out var b)) { Console.WriteLine("  ✗ 改动后读不到"); bad++; }
            else if (Sha(b) == Sha(orig[probe])) { Console.WriteLine("  ✗ 改动没生效（写入通道可能是假的）"); bad++; }
            else Console.WriteLine($"  ✓ 改动生效 {System.IO.Path.GetFileName(probe)}  {orig[probe].Length:N0} → {b.Length:N0} B");
        }
        using (var s = OpenContainer()) {
            if (s is null) return 6;
            if (!s.Write(probe, orig[probe])) { Console.WriteLine("  ✗ 复原写入失败"); bad++; }
            s.Flush();
        }
        using (var s = OpenContainer()) {
            if (s is null) return 6;
            if (s.TryRead(probe, out var b) && Sha(b) == Sha(orig[probe]))
                Console.WriteLine($"  ✓ 复原成功 {System.IO.Path.GetFileName(probe)}");
            else { Console.WriteLine("  ✗ 复原失败"); bad++; }
        }

        var after = Snap();
        Console.WriteLine();
        if (store.Kind == "Bundles2") {
            Console.WriteLine($"  _.index.bin  {before.idx:N0} → {after.idx:N0} B（Δ {after.idx - before.idx:+#;-#;0}）");
            Console.WriteLine($"  自建 bundle   {before.nb} 个 → {after.nb} 个（新增 {after.nb - before.nb}）");
            Console.WriteLine($"  新 bundle 卷  {before.bytes:N0} → {after.bytes:N0} B（Δ {(after.bytes - before.bytes) / 1024.0:N0} KB）");
        } else {
            Console.WriteLine($"  ggpk  {before.idx:N0} → {after.idx:N0} B（Δ {after.idx - before.idx:+#;-#;0}）");
        }
        Console.WriteLine();
        Console.WriteLine($"结构演练完成：{orig.Count} 个文件，异常 {bad}，用时 {sw.Elapsed.TotalSeconds:F1} s");
        // 演练已经把原样写回去了：把 ggpk 的日志丢掉，免得下次启动误以为上次没走完
        if (store.Kind == "ggpk") GgpkUndo.Active?.Abandon();
        return bad == 0 ? 0 : 6;
    }

    // ---------------------------------------------------------------- 采集诊断包
    //
    // 在异地机器上跑一次，把「客户端版本 + 每个目标文件的状态 + 对不上的文件的原文
    // 与逐行对照」打成一个 zip。作者拿这个就能针对那份客户端重做数据包，不用远程连。
    // 全程只读，不写容器。

    private static string ClassifyText(Row r, string now) {
        if (now == TextFor(r, Flavor.Original)) return "原文";
        if (r.T is not null && now == TextFor(r, Flavor.Translated)) return "纯译文";
        if (r.D is not null && now == TextFor(r, Flavor.Dual)) return "对照";
        if (now == TextFor(r, Flavor.DualAll)) return "全量对照";
        return "其它";
    }

    private static string LineText(List<string> lines, int no) {
        if (no < 0 || no >= lines.Count) return "";
        var raw = lines[no];
        var (j, k) = TextSpan(raw);
        return j >= 0 && k > j ? raw[j..k] : "";
    }

    private static int Collect(IContainer store, List<Item> items) {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var dir = Args.GetValueOrDefault("out");
        if (string.IsNullOrWhiteSpace(dir)) {
            // 优先放在 exe 旁边（用户刚从这里启动，最容易找到），
            // 不可写就退到桌面、当前目录。
            var exeDir = "";
            try { exeDir = System.IO.Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? ""; } catch { }
            var candidates = new[] {
                exeDir,
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Directory.GetCurrentDirectory(),
            };
            foreach (var c in candidates) {
                if (string.IsNullOrWhiteSpace(c)) continue;
                var probe = System.IO.Path.Combine(c, $"poe-zh-collect-{stamp}");
                try { Directory.CreateDirectory(probe); dir = probe; break; } catch { }
            }
            if (string.IsNullOrWhiteSpace(dir))
                dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"poe-zh-collect-{stamp}");
        }
        dir = System.IO.Path.GetFullPath(dir);
        var dumpDir = System.IO.Path.Combine(dir, "files");
        Directory.CreateDirectory(dumpDir);

        var (client, patch) = DetectGameVersion();
        var sb = new StringBuilder();
        var files = new List<Dictionary<string, object?>>();
        var unknown = items.Where(i => i.CurFlavor == Flavor.Unknown && !i.Missing).ToList();
        var missing = items.Where(i => i.Missing).ToList();

        sb.AppendLine("PoE 繁中改良汉化 · 采集报告");
        sb.AppendLine($"采集时间    {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"补丁器版本  {_ver}（数据包构建于 {_bundleBuilt}）");
        sb.AppendLine($"数据包适用  游戏 {_gameClient}{(_gamePatch.Length > 0 ? " / patch " + _gamePatch : "")}");
        sb.AppendLine($"游戏版本    {(client.Length == 0 ? "(读不到 logs/LatestClient.txt)" : client + (patch.Length > 0 ? " / patch " + patch : ""))}" +
                      (client.Length > 0 ? (VersionOk() ? "  ✓ 与数据包一致" : "  ✗ 与数据包不一致") : ""));
        sb.AppendLine($"目标        [{store.Kind}] {store.Display}");
        sb.AppendLine($"容器大小    {store.Size:N0} B");
        sb.AppendLine();
        sb.AppendLine($"目标文件 {items.Count} 个：{Summary(items)}");
        sb.AppendLine($"对不上的（未知）{unknown.Count} 个，缺失 {missing.Count} 个。");
        sb.AppendLine();

        foreach (var i in items) {
            var rec = new Dictionary<string, object?> {
                ["file"] = i.Label,
                ["path"] = i.Path,
                ["kind"] = i.IsSd ? "statdesc" : "dat",
                ["missing"] = i.Missing,
                ["size"] = i.Cur.Length,
                ["sha256"] = i.Cur.Length > 0 ? Sha(i.Cur) : "",
                ["state"] = ShowFlavor(i),
            };
            files.Add(rec);

            if (i.Missing || i.CurFlavor != Flavor.Unknown) continue;

            // 未知文件：落原文 + 逐行核对我们想改的那几行，看是否还落在原处
            var dump = System.IO.Path.Combine(dumpDir, i.Label);
            try { File.WriteAllBytes(dump, i.Cur); rec["dump"] = "files/" + i.Label; }
            catch (Exception ex) { rec["dump_error"] = ex.Message; }

            sb.AppendLine(new string('-', 70));
            sb.AppendLine($"{i.Label}");
            sb.AppendLine($"  {i.Path}");
            sb.AppendLine($"  {i.Cur.Length:N0} B   sha256 {rec["sha256"]}");

            if (!i.IsSd) {
                sb.AppendLine($"  （数据表，无法按行合并：{(i.Why.Length > 0 ? i.Why : "版本不同")}）");
                sb.AppendLine();
                continue;
            }

            List<string> lines;
            try { lines = SplitLines(i.Cur); }
            catch (Exception ex) { sb.AppendLine($"  ! 解不开：{ex.Message}"); sb.AppendLine(); continue; }

            var stat = new Dictionary<string, int>();
            var rows = new List<Dictionary<string, object?>>();
            foreach (var r in i.Sd!.Rows) {
                string kind, now;
                if (r.No >= lines.Count) { kind = "行号越界"; now = ""; }
                else {
                    now = LineText(lines, r.No);
                    kind = ClassifyText(r, now);
                }
                stat[kind] = stat.GetValueOrDefault(kind) + 1;

                // 对不上的行：在附近 ±40 行找同一段文本，看是不是整体错位（插入/删除行导致）
                var off = 0;
                var foundAt = 0;
                if ((kind is "其它" or "行号越界") && !string.IsNullOrWhiteSpace(r.O)) {
                    var lo = Math.Max(0, r.No - 40);
                    var hi = Math.Min(lines.Count - 1, r.No + 40);
                    for (var q = lo; q <= hi; q++) {
                        if (q == r.No) continue;
                        if (LineText(lines, q) == r.O) { off = q - r.No; foundAt = q + 1; break; }
                    }
                }
                rows.Add(new Dictionary<string, object?> {
                    ["line"] = r.No + 1, ["kind"] = kind,
                    ["expect_original"] = Clip(r.O), ["actual"] = Clip(now),
                    ["offset"] = off, ["found_at"] = foundAt,
                });
            }
            rec["rows"] = rows;

            var bl = i.Sd.BaseLines;
            sb.AppendLine(bl > 0
                ? $"  本文件 {i.Cur.Length:N0} B / {lines.Count:N0} 行；基线 {i.Sd.BaseBytes:N0} B / {bl:N0} 行（行数差 {lines.Count - bl:+#;-#;0}）"
                : $"  本文件 {i.Cur.Length:N0} B / {lines.Count:N0} 行");
            sb.AppendLine($"  要改 {i.Sd.Rows.Count} 行");
            sb.AppendLine($"  逐行核对：{string.Join(" / ", stat.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {kv.Value}"))}");

            var badRows = rows.Where(x => (string?)x["kind"] is "其它" or "行号越界").ToList();
            var shifted = badRows.Where(x => (int)x["offset"]! != 0).ToList();
            if (shifted.Count > 0) {
                var top = shifted.GroupBy(x => (int)x["offset"]!).OrderByDescending(g => g.Count()).First();
                sb.AppendLine($"  错位探测：{shifted.Count} 行能在附近找到同一段文本，最常见的偏移是 {top.Key:+#;-#;0} 行（{top.Count()} 行）");
            }
            if (badRows.Count == 0)
                sb.AppendLine("  → 所有目标行都能对上，这份客户端可以直接做行级改写（只差把 sha 基线更新进去）");
            else {
                sb.AppendLine($"  → 有 {badRows.Count} 行对不上：");
                foreach (var b in badRows.Take(20)) {
                    var tail = (int)b["offset"]! != 0 ? $"   → 在第 {b["found_at"]} 行找到（{(int)b["offset"]!:+#;-#;0}）" : "";
                    sb.AppendLine($"      第 {(int)b["line"]!} 行  实际：{b["actual"]}   预期：{b["expect_original"]}{tail}");
                }
                if (badRows.Count > 20) sb.AppendLine($"      ... 还有 {badRows.Count - 20} 行");
            }
            sb.AppendLine();
        }

        if (unknown.Count == 0)
            sb.AppendLine("（没有未知文件，数据包与这份客户端完全对得上）");

        // 客户端日志前 60 行，方便看版本与语言
        try {
            var root = store.RootDir;
            foreach (var rel in new[] { "logs/LatestClient.txt", "logs/Client.txt" }) {
                var p = System.IO.Path.Combine(root, rel);
                if (!File.Exists(p)) continue;
                var head = File.ReadLines(p).Take(60).ToArray();
                File.WriteAllLines(System.IO.Path.Combine(dir, "client-log.txt"), head, Encoding.UTF8);
                break;
            }
        } catch { }

        var readme = new[]
        {
            "这个文件夹是 PoE 繁中汉化补丁器生成的采集包，只用于让作者针对你的客户端重做数据包。",
            "",
            "请把整个文件夹（或同级的 .zip）发回给作者。内容说明：",
            "  report.txt     —— 客户端版本、每个文件的状态、对不上的文件的逐行对照",
            "  report.json    —— 同样的内容，机器可读",
            "  files/         —— 状态对不上的那几个词缀文件原文（UTF-16 文本）",
            "  client-log.txt —— 客户端日志开头几行，含版本号",
            "",
            "采集过程只读，没有改动游戏文件。",
        };
        File.WriteAllLines(System.IO.Path.Combine(dir, "README.txt"), readme, Encoding.UTF8);
        File.WriteAllText(System.IO.Path.Combine(dir, "report.txt"), sb.ToString(), Encoding.UTF8);
        File.WriteAllText(System.IO.Path.Combine(dir, "report.json"),
            JsonSerializer.Serialize(new {
                collected_at = DateTime.Now.ToString("s"),
                patcher = _ver, bundle_built = _bundleBuilt,
                expected_game = _gameClient, expected_patch = _gamePatch,
                game = client, patch,
                container = new { kind = store.Kind, path = store.Display, size = store.Size },
                files,
            }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
            Encoding.UTF8);

        var zip = dir.TrimEnd('\\', '/') + ".zip";
        try {
            if (File.Exists(zip)) File.Delete(zip);
            ZipFile.CreateFromDirectory(dir, zip, CompressionLevel.Optimal, false);
        } catch (Exception ex) {
            Console.WriteLine($"（打包 zip 失败：{ex.Message}，直接把文件夹发回也行）");
            zip = "";
        }

        Console.WriteLine();
        Console.WriteLine($"采集完成：未知 {unknown.Count} 个 / 缺失 {missing.Count} 个 / 其余与数据包一致");
        Console.WriteLine($"  文件夹  {dir}");
        if (zip.Length > 0) Console.WriteLine($"  压缩包  {zip}   {new FileInfo(zip).Length:N0} B");
        Console.WriteLine();
        Console.WriteLine("把这个 zip 发回给作者即可（里面不含游戏本体，只有你说的那几个词缀文件）。");
        return 0;
    }

    private static (long idx, int nb, long bytes) Snap() {
        if (!System.IO.Path.GetFileName(_target).Equals("_.index.bin", StringComparison.OrdinalIgnoreCase))
            return (new FileInfo(_target).Length, 0, 0);
        var dir = System.IO.Path.GetDirectoryName(_target)!;
        var nbDir = System.IO.Path.Combine(dir, "LibGGPK3");
        var files = Directory.Exists(nbDir) ? Directory.GetFiles(nbDir, "*.bundle.bin") : Array.Empty<string>();
        return (new FileInfo(_target).Length, files.Length, files.Sum(f => new FileInfo(f).Length));
    }
}
