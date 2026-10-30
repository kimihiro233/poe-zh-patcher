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

// DualLegacy：认出来了 —— 里面是「原文 + 译文」的某个排列，只是拼法不是当前这一版
// （v0.4.2 及之前把英文整段堆在中文后面）。它和 Custom 的区别是：Custom 是底稿被
// 别人动过、要保住对方的标记；DualLegacy 是我们自己写的，整块换成目标即可。
internal enum Flavor { Original, Translated, Dual, DualAll, Custom, DualLegacy, Unknown }

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

    /// 标记层序列（逐个 token），给内容闸判「这行多了个推导不出来的标签」用
    public static List<string> MarkupTokens(string s)
    {
        var list = new List<string>();
        foreach (var (mk, seg) in Tokens(s)) if (mk) list.Add(seg);
        return list;
    }

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
        var r = BundleHygiene.Detach(_index, System.IO.Path.Combine(System.IO.Path.GetDirectoryName(IndexPath)!, "LibGGPK3"));
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

    // 唯一一份「程序认得的参数」清单。usage 文本要与它双向对齐（--args-test 钉住）：
    // 写了不认的 → 用户照着文档敲却被拒；认了不写 → 这个开关等于不存在。
    // 以前这里没有清单，任何 `--xxx` 都被静默塞进 Args，拼错一个字母就按默认值跑完一整轮。
    private static readonly string[] KnownFlags = {
        // 动作
        "status", "selftest", "struct", "collect", "writetest", "bundles", "dump", "detach-test",
        "backups", "baseline", "restore-backup", "restore-baseline", "restore-ggpk",
        "reset-recover",
        // 写入选项
        "set", "only", "partial", "plan", "on-modified", "keep-markup", "locale-only",
        "gate", "yes", "allow-no-rollback", "out", "simulate-fail",
        // 自检
        "sd-test", "dat-test", "data-test", "lang-test", "flag-test", "redact-test", "gate-test",
        "markup-test", "undo-test", "legacy-test", "baseline-test", "recover-test",
        "bundle-name-test", "args-test", "backup-test", "fixture",
        // 其它
        "help",
    };

    private static string _target = "";      // Content.ggpk 或 Bundles2\_.index.bin
    private static bool _restoreBare = true;   // 底稿标记被整段删光时补回官方标记（--keep-markup 关闭）
    private static string _ver = "?";
    private static string _bundleBuilt = "?";
    private static string _gameClient = "?", _gamePatch = "", _gameLocked = "";
    private static List<SdEntry> _sd = new();
    private static List<DatEntry> _dat = new();

    // 仓库根目录：从当前目录往上找，直到看见 tools/pack/markup-whitelist.json。
    // 单文件 exe 的 AppContext.BaseDirectory 是 %TEMP% 解压目录，不能拿来当锚点。
    private static string RepoRoot()
    {
        var d = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (d is not null) {
            if (File.Exists(Path.Combine(d.FullName, "tools", "pack", "markup-whitelist.json")))
                return d.FullName;
            d = d.Parent;
        }
        return Directory.GetCurrentDirectory();
    }

    private static string GateRoot()
    {
        if (Args.TryGetValue("gate-test", out var v) && v.Length > 0) return v;
        return RepoRoot();
    }

    /// <summary>参数校验：名字不在清单里、或取值不认识，都返回一句能照着改的说明。</summary>
    private static string? CheckArg(string key, string val) {
        if (!KnownFlags.Contains(key, StringComparer.OrdinalIgnoreCase)) {
            var near = KnownFlags
                .Select(f => (f, d: Distance(f.ToLowerInvariant(), key.ToLowerInvariant())))
                .Where(x => x.d <= 2)
                .OrderBy(x => x.d)
                .Select(x => x.f)
                .FirstOrDefault();
            var tip = near is null ? "" : $"\n  你是想写 --{near} 吗？";
            return $"未知参数：--{key}{tip}";
        }
        if (key.Equals("on-modified", StringComparison.OrdinalIgnoreCase) && ParsePolicy(val) is null)
            return $"未知参数：--on-modified={val}\n  可选 merge / ours / original / skip";
        if (key.Equals("gate", StringComparison.OrdinalIgnoreCase) && GateModeOf(val) is null)
            return $"未知参数：--gate={val}\n  可选 off / warn / enforce";
        return null;
    }

    /// <summary>编辑距离，只用来给「你是不是想写 xxx」挑一个最近的名字。</summary>
    private static int Distance(string a, string b) {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                                   d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }

    private static int Main(string[] raw) {
        Console.OutputEncoding = Encoding.UTF8;
        try { Console.Title = "PoE 繁中改良汉化"; } catch { }

        var positional = new List<string>();
        foreach (var a in raw) {
            if (a.StartsWith("--", StringComparison.Ordinal)) {
                var body = a[2..];
                var eq = body.IndexOf('=');
                var key = eq < 0 ? body : body[..eq];
                var val = eq < 0 ? "" : body[(eq + 1)..];

                // 白名单 + 取值校验都在这里做：拼错的开关如果放过去，程序会按默认值跑完一整轮，
                // 而默认值恰好是「最保守」的那一档，用户不会收到任何提示（B9）。
                var bad = CheckArg(key, val);
                if (bad is not null) {
                    Console.WriteLine(bad);
                    Console.WriteLine("  用 --help 看全部参数。未知参数一律拒绝，避免「拼错了却按默认值跑」。");
                    return 2;
                }
                Args[key] = val;
            } else positional.Add(a);
        }

        // 自检放在最前面：它们都不需要打开容器，也不能因为参数解析退化而跑偏。
        // --args-test 尤其要第一个跑 —— 后面所有用例都依赖「参数被正确识别」这件事。
        if (Args.ContainsKey("args-test")) return ArgsTest();
        if (Args.ContainsKey("recover-test")) return RecoverTest();
        if (Args.ContainsKey("bundle-name-test")) return BundleNameTest();
        if (Args.ContainsKey("backup-test")) return BackupTest();

        LoadBundle();

        // --help 必须在这里就返回：不返回的话会落到「没有位置参数 → 自动找默认安装路径」，
        // 于是「想看帮助」变成「打开真实客户端」。文档里写了的开关都得真有用。
        if (Args.ContainsKey("help")) { PrintUsage(); return 0; }

        _restoreBare = !Args.ContainsKey("keep-markup");

        // 采集包脱敏自检：规则改一个字就是「泄漏」或「误杀」，所以每条规则都拿真实日志行钉住
        if (Args.ContainsKey("redact-test")) return Redact.SelfTest();
        // 内容闸自检：读 fixtures/gate/cases.json —— 与 tools/content_gate.py 同一份样本，
        // 两边结论必须逐条一致。只在一侧改规则，这里就会红。
        if (Args.ContainsKey("gate-test")) return ContentGate.SelfTest(GateRoot());
        // 标记层合并自检：与 tools/markup.py 的用例逐条对齐，防止两侧实现漂移
        if (Args.ContainsKey("markup-test")) return MarkupTest();
        // ggpk 逐字节还原自检：不碰真实客户端，在临时目录里造一份「同结构」的文件跑
        if (Args.ContainsKey("undo-test")) return UndoTest();
        // 旧版对照的识别自检：不依赖游戏，用数据包里的三态文本现场拼出各种历史拼法，
        // 验证「认得出多少种排列」和「什么情况下必须认不出」。
        if (Args.ContainsKey("legacy-test")) return LegacyTest();
        // 基准自检：不碰游戏，验证「只存一次」「读回一致」「版本隔离」「被改坏就拒绝使用」。
        // 这四条是「结果与改过几次无关」的前提，任何一条破了，基准就不再是常量。
        if (Args.ContainsKey("baseline-test")) return BaselineTest();
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

        // 逃生舱：只丢记录、不回滚。用于「官方已经重建索引」——此时那份日志要退的目标
        // 状态已经不存在了，继续回滚等于把官方刚下发的索引换成一个过期的副本。
        if (Args.ContainsKey("reset-recover")) {
            var msg = GgpkUndo.IsGgpk(gp) ? GgpkUndo.DiscardWriting(gp) : Txn.DiscardJournal(gp);
            if (msg is not null) { Console.WriteLine(msg); return 1; }
            Console.WriteLine("已丢弃上次未完成的写入记录（没有回滚任何字节，索引保持当前状态）。");
            Console.WriteLine(" 接下来请照常运行；如果游戏仍打不开，用 Steam/WeGame 验证完整性。");
            return 0;
        }

        // 上次写入没走完？先把客户端还原到写前状态，再谈别的。
        // 必须在打开容器之前做：回滚要覆盖 _.index.bin / Content.ggpk，句柄被占着就换不回去。
        var rec = GgpkUndo.IsGgpk(gp) ? GgpkUndo.Recover(gp) : Txn.Recover(gp);
        if (rec.Recovered) {
            Console.WriteLine();
            Console.WriteLine("==============================================================");
            // 三种状态分开说，而且「上次写入没有正常结束」只由外层讲一次 ——
            // 以前 Message 里也带这句，于是同一句话在一屏里出现三遍。
            Console.WriteLine(" 检测到上次写入没有正常结束。");
            Console.WriteLine($" {rec.Message}");
            if (rec.Complete) {
                Console.WriteLine($" 客户端可以正常启动{(rec.RestoredFiles > 0 ? $"（恢复 {rec.RestoredFiles} 处）" : "")}，直接重新运行本程序即可。");
                Console.WriteLine(" 不用先验证游戏完整性。");
            } else {
                // 「退了一部分」和「退回去了」是两件事。这里不能再说「客户端可以正常启动」——
                // 索引可能还是半截，而用户唯一能做的判断依据就是这几行字。
                foreach (var e in rec.Errors.Take(8)) Console.WriteLine("   - " + e);
                if (rec.JournalKept && rec.JournalPath.Length > 0)
                    Console.WriteLine($" 现场记录已保留：{rec.JournalPath}");
                Console.WriteLine(" 建议顺序：① 用 Steam/WeGame「验证游戏完整性」→ ② 重新运行本程序。");
                Console.WriteLine(" 在退干净之前，本程序不会写入任何东西（--yes 可强行继续，风险自负）。");
                if (rec.Hint.Length > 0) {
                    Console.WriteLine();
                    Console.WriteLine(" " + rec.Hint.Replace("\n", "\n "));
                }
                if (!Args.ContainsKey("yes")) {
                    Console.WriteLine("==============================================================");
                    Console.WriteLine();
                    Pause();
                    return 3;
                }
                Console.WriteLine(" 已指定 --yes：继续，但这次写入的起点可能不是干净的。");
            }
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
        // 基准：每次写入的「算式输入」，与改过几次无关；也可以直接把某份文件写回基准
        if (Args.ContainsKey("baseline")) return ShowBaselines(store, Args.TryGetValue("restore-baseline", out var rsb) ? rsb : "");
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

        // 诊断开关：把行尾换成 LF，复现「第三方汉化写成 LF-only」的客户端。
        // 这类文件以前会被报成「行数 1 ≠ 基线 268」，把人引向「数据包版本不对」。
        var lfFixture = Args.GetValueOrDefault("fixture") == "lf";
        if (lfFixture) {
            if (cur.Length >= 2 && cur[0] == 0xFF && cur[1] == 0xFE) {
                var s = Encoding.Unicode.GetString(cur, 2, cur.Length - 2).Replace("\r\n", "\n");
                var body = Encoding.Unicode.GetBytes(s);
                cur = new byte[body.Length + 2];
                cur[0] = 0xFF; cur[1] = 0xFE;
                body.CopyTo(cur, 2);
                Console.WriteLine("  （已把这份文件转成 LF-only，模拟第三方汉化的行尾）");
            }
        }

        var ok = TryCustomBase(e, cur, out var why, out var knownOnly);
        var nLines = LineCountSafe(cur);
        Console.WriteLine($"{name}");
        Console.WriteLine($"  行数 {(nLines < 0 ? "读不出（无 BOM）" : nLines.ToString("N0"))} / 基线 {e.BaseLines:N0}   待改行 {e.Rows.Count}");
        // 认不出时用结构层的原因覆盖 TryCustomBase 的「行数 1 ≠ 基线 N」：后者是对的，
        // 但它描述的是症状，不是原因。
        if (!ok) why = WhyUnknown(cur, e);
        Console.WriteLine($"  可叠文本判定：{(ok ? "是" : "否")}  （{why}）");

        // --fixture=lf 是自检模式：判据是「拒写 + 原因指向行尾」，不是「这份文件能合并」。
        // 这样 CI 只看退出码就够了，不必去 grep 中文输出（改文案不该弄坏 CI）。
        if (lfFixture) {
            var diagnosed = !ok && why.Contains("LF");
            Console.WriteLine(diagnosed
                ? "  OK：LF-only 被拒，而且原因指向行尾，不会把人引向「数据包版本不对」"
                : "  FAIL：LF-only 的拒写原因不对（应当指向行尾）");
            return diagnosed ? 0 : 1;
        }
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
    // 旧版对照识别自检：不碰游戏，只拿一份最小的「文件」把判定规则的边界钉住。
    // 重点不是「认得出现在这一版」，而是「以后每次改 dual_body 的排列方式，老客户端都还认得」，
    // 以及「该认不出的必须认不出」—— 认错会把别人的译文整块换成我们的。
    // 基准自检：不依赖游戏。四条性质必须同时成立，否则「结果与改过几次无关」就不成立 ——
    //   ① 存一次读一次，字节一模一样；② 同一版本同一路径只存一次，绝不覆盖；
    //   ③ 版本之间互不可见（换版本绝不能拿旧基准去算新客户端）；
    //   ④ 基准文件被改坏（sha 对不上）就拒绝使用，宁可从当前重算，也不能拿污染的常量去算。
    private static int BaselineTest() {
        var root = Path.Combine(Path.GetTempPath(), "poezh-base-" + DateTime.Now.ToString("HHmmssfff"));
        Directory.CreateDirectory(root);
        var fake = Path.Combine(root, "Content.ggpk");
        File.WriteAllBytes(fake, new byte[16]);          // 只需要存在，用来定位 .poe-zh

        var bad = 0;
        void Check(string what, bool ok, string extra = "") {
            Console.WriteLine($"  {(ok ? "✓" : "✗")} {what}{(extra.Length > 0 ? "  " + extra : "")}");
            if (!ok) bad++;
        }

        Console.WriteLine($"基准自检（临时目录 {root}）");
        var v1 = new byte[] { 1, 2, 3 };
        Check("第一次存基准", BaselineStore.Put(fake, "3.29.3b", "3.29.3.3", "0.5.0", "a/b.txt", v1, "Original"));

        var got = BaselineStore.Get(fake, "3.29.3b", "3.29.3.3", "a/b.txt", out var tag);
        Check("读回来字节一致、来源记得住",
              got is not null && got.SequenceEqual(v1) && tag == "Original", tag);

        Check("第二次存不覆盖",
              !BaselineStore.Put(fake, "3.29.3b", "3.29.3.3", "0.9.9", "a/b.txt", new byte[] { 9 }, "Custom"));
        var again = BaselineStore.Get(fake, "3.29.3b", "3.29.3.3", "a/b.txt", out var tag2);
        Check("内容仍是第一份（常量没变）",
              again is not null && again.SequenceEqual(v1) && tag2 == "Original", tag2);

        Check("换游戏版本读不到旧基准",
              BaselineStore.Get(fake, "3.30.0", "", "a/b.txt", out _) is null);

        var dir = BaselineStore.DirFor(fake, "3.29.3b", "3.29.3.3");
        var idx = BaselineStore.Load(fake, "3.29.3b", "3.29.3.3");
        Check("索引读得回来", idx is not null && idx.Files.Count == 1);
        if (idx is not null) {
            var f = Path.Combine(dir, idx.Files["a/b.txt"].File);
            var raw = File.ReadAllBytes(f);
            raw[0] ^= 0xFF;
            File.WriteAllBytes(f, raw);
            Check("基准文件被改坏 → 拒绝使用",
                  BaselineStore.Get(fake, "3.29.3b", "3.29.3.3", "a/b.txt", out _) is null);
        }

        var mf = Path.Combine(dir, "index.json");
        var head = File.ReadAllBytes(mf);
        Check("索引不带 UTF-8 BOM（别的工具能直接读）",
              !(head.Length >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF));
        Check("版本键可直接当目录名",
              BaselineStore.Key("3.29.3b", "3.29.3.3").IndexOfAny(Path.GetInvalidFileNameChars()) < 0,
              BaselineStore.Key("3.29.3b", "3.29.3.3"));

        try { Directory.Delete(root, true); } catch { }
        Console.WriteLine(bad == 0 ? "\n基准自检通过" : $"\n基准自检失败 {bad} 项");
        return bad == 0 ? 0 : 1;
    }

    private static int LegacyTest() {
        Console.WriteLine("旧版对照识别自检");
        Console.WriteLine();

        // 文本内的换行是字面「反斜杠 + n」，和真实 statdesc 一致
        const string O = "Hello\\nWorld";
        const string T = "你好\\n世界";

        byte[] Build(string body) {
            var text = "key \"x\"\r\nkey \"" + body + "\"\r\n";
            var b = Encoding.Unicode.GetBytes(text);
            var r = new byte[b.Length + 2];
            r[0] = 0xFF; r[1] = 0xFE;
            b.CopyTo(r, 2);
            return r;
        }
        SdEntry MakeEntry() {
            var e = new SdEntry { Path = "test_stat_descriptions.txt", BaseLines = 3 };
            e.Rows.Add(new Row { No = 1, O = O, T = T, E = O });
            return e;
        }

        var bad = 0;
        void Case(string what, string body, bool expect) {
            var ok = TryLegacyDual(MakeEntry(), Build(body), out var why);
            var pass = ok == expect;
            Console.WriteLine($"  {(pass ? "✓" : "✗")} {what}");
            Console.WriteLine($"      {(ok ? "认出" : "认不出")}：{why}");
            if (!pass) {
                Console.WriteLine($"      预期：{(expect ? "认出" : "认不出")}");
                bad++;
            }
        }

        // ---- 认得出来：同一份译文的任何一种排列
        Case("当前拼法（逐组穿插）", "你好\\n  Hello\\n世界\\n  World", true);
        Case("旧拼法（英文整段附末尾 + 首行缩进）", "你好\\n世界\\n  Hello\\nWorld", true);
        Case("旧拼法（英文整段附末尾、完全不缩进）", "你好\\n世界\\nHello\\nWorld", true);
        Case("英文在前", "Hello\\nWorld\\n你好\\n世界", true);
        Case("混着来（第一组穿插、第二组附后）", "你好\\n  Hello\\n世界\\nWorld", true);
        Case("只有译文、没有英文", "你好\\n世界", true);

        // ---- 认不出来：对应关系被破坏，或根本不是我们写的
        Case("英文那一行被改了一个字", "你好\\n世界\\n  Hello\\nWorlx", false);
        Case("译文那一行被改了一个字", "你好呀\\n世界\\n  Hello\\nWorld", false);
        Case("混进了别人的一句译文", "你好\\n世界\\n  Hello\\nWorld\\n  這是別人翻的", false);
        Case("整份都是官方原文（应走 Original 那条路）", "Hello\\nWorld", false);

        // ---- 一半原文、一半译文：认得出，且不把「还没翻的格」算错
        {
            var e = new SdEntry { Path = "t.txt", BaseLines = 3 };
            e.Rows.Add(new Row { No = 1, O = O, T = T, E = O });
            e.Rows.Add(new Row { No = 2, O = "Foo\\nBar", T = "甲\\n乙", E = "Foo\\nBar" });
            var text = "key \"x\"\r\nkey \"你好\\n世界\\n  Hello\\nWorld\"\r\nkey \"Foo\\nBar\"\r\n";
            var b = Encoding.Unicode.GetBytes(text);
            var cur = new byte[b.Length + 2];
            cur[0] = 0xFF; cur[1] = 0xFE;
            b.CopyTo(cur, 2);
            var ok = TryLegacyDual(e, cur, out var why);
            var pass = ok;
            Console.WriteLine($"  {(pass ? "✓" : "✗")} 一格已翻、一格还是原文");
            Console.WriteLine($"      {(ok ? "认出" : "认不出")}：{why}");
            if (!pass) bad++;
        }

        Console.WriteLine();
        Console.WriteLine(bad == 0 ? "全部通过。" : $"{bad} 项不符。");
        return bad == 0 ? 0 : 7;
    }

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

        // ---- D) 两份未完成日志：断电发生在「回滚过程中」
        // 这时目录里会同时有两份 Phase=="writing"。旧版遍历到第一份就 break，
        // 第二份的原字节永远留在日志里 —— 客户端读到的仍是写了一半的内容。
        File.WriteAllBytes(path, orig);
        var uD1 = GgpkUndo.Begin(path);
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)) {
            var st = uD1.Wrap(fs);
            st.Position = dataStart + 5000;
            var b = Rnd(512);
            st.Write(b, 0, b.Length);
            st.Flush();
        }
        uD1.SimulateCrash();
        var uD2 = GgpkUndo.Begin(path);
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)) {
            var st = uD2.Wrap(fs);
            st.Position = dataStart + 9000;
            var b = Rnd(512);
            st.Write(b, 0, b.Length);
            st.Flush();
        }
        uD2.SimulateCrash();
        var recD = GgpkUndo.Recover(path);
        Check("两份未完成日志都退回去了", recD.Recovered && recD.Complete, recD.Message);
        Check("两份日志覆盖的偏移都回到写前", SameAsOrig("多份日志恢复后"));

        // ---- E) 别的客户端的日志不许动
        var otherPath = Path.Combine(root, "Other.ggpk");
        var otherOrig = new byte[bodyLen];
        rnd.NextBytes(otherOrig);
        File.WriteAllBytes(otherPath, otherOrig);
        var uE = GgpkUndo.Begin(otherPath);
        using (var fs = new FileStream(otherPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)) {
            var st = uE.Wrap(fs);
            st.Position = dataStart + 777;
            var b = Rnd(256);
            st.Write(b, 0, b.Length);
            st.Flush();
        }
        uE.SimulateCrash();
        var otherNow = File.ReadAllBytes(otherPath);
        var recE = GgpkUndo.Recover(path);
        Check("别的客户端的日志被跳过", !recE.Recovered, recE.Message);
        Check("别的文件一个字节都没被碰", File.ReadAllBytes(otherPath).SequenceEqual(otherNow));

        // ---- F) 日志里的偏移越界：必须拒绝，且不能把文件写长
        File.WriteAllBytes(path, orig);
        var uF = GgpkUndo.Begin(path);
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)) {
            var st = uF.Wrap(fs);
            st.Position = dataStart + 20000;
            var b = Rnd(256);
            st.Write(b, 0, b.Length);
            st.Flush();
        }
        uF.SimulateCrash();
        // 把第一条记录的 off 改成「文件长度 - 1」、cnt 改成 8：越界 7 个字节
        var logPath = Path.Combine(uF.Dir, "undo.bin");
        var lb = File.ReadAllBytes(logPath);
        BitConverter.GetBytes(orig.Length - 1).CopyTo(lb, GgpkUndo.MagicLength);
        BitConverter.GetBytes(8).CopyTo(lb, GgpkUndo.MagicLength + 8);
        File.WriteAllBytes(logPath, lb);
        var lenBefore = new FileInfo(path).Length;
        var recF = GgpkUndo.Recover(path);
        Check("越界记录被拒绝", !recF.Complete && recF.Errors.Any(e => e.Contains("越界")),
              recF.Errors.FirstOrDefault() ?? recF.Message);
        Check("越界时目标文件长度不变", new FileInfo(path).Length == lenBefore,
              $"{lenBefore:N0} → {new FileInfo(path).Length:N0}");

        // ---- G) 逃生舱：丢弃记录，但不回滚任何字节
        // 用于「官方已经重建索引/文件」——此时日志要退的目标状态已经不存在，
        // 继续回滚等于把官方刚下发的文件换成一个过期的副本。
        // 这里同时把 F 留下的那份未完成日志一并丢掉（DiscardWriting 处理所有 writing）。
        File.WriteAllBytes(path, orig);
        var uG = GgpkUndo.Begin(path);
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)) {
            var st = uG.Wrap(fs);
            st.Position = dataStart + 30000;
            var b = Rnd(128);
            st.Write(b, 0, b.Length);
            st.Flush();
        }
        uG.SimulateCrash();
        var bytesBeforeDiscard = File.ReadAllBytes(path);
        var msgG = GgpkUndo.DiscardWriting(path);
        Check("逃生舱：丢弃未完成记录", msgG is null, msgG ?? "");
        Check("逃生舱：一个字节都没回滚", File.ReadAllBytes(path).SequenceEqual(bytesBeforeDiscard));
        Check("逃生舱：之后不再认为有未完成写入", !GgpkUndo.Recover(path).Recovered);

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
        Flavor.DualLegacy => "旧版对照",
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

    // ---------------------------------------------------------------- 参数表自检（B9）

    /// <summary>
    /// 三件事：文档里写的参数程序都认、程序认的参数文档都写、拼错的参数被拒（退出码 2）。
    /// 第三条要用子进程跑，因为「拒绝」发生在解析阶段 —— 在同一进程里没法观察。
    /// </summary>
    private static int ArgsTest() {
        // 子进程只用它来「过一遍解析然后立刻退出」，不能再递归跑整套自检
        if (Args.GetValueOrDefault("args-test") == "child") return 0;

        var bad = 0;
        var usage = UsageText();
        var documented = new HashSet<string>(
            System.Text.RegularExpressions.Regex.Matches(usage, @"--([a-z0-9-]+)")
                .Select(m => m.Groups[1].Value), StringComparer.OrdinalIgnoreCase);

        foreach (var f in documented.OrderBy(x => x))
            if (!KnownFlags.Contains(f, StringComparer.OrdinalIgnoreCase)) {
                Console.WriteLine($"  ! 文档写了 --{f}，但 KnownFlags 里没有"); bad++;
            }
        foreach (var f in KnownFlags.OrderBy(x => x))
            if (!documented.Contains(f)) { Console.WriteLine($"  ! --{f} 认了，但没写进 usage"); bad++; }

        // 每个都必须是 2，且不能落到默认值继续跑
        var probes = new (string arg, string why)[] {
            ("--on-modified=our", "少个 s，旧版会静默走 merge"),
            ("--gate=enforcee",   "拼错，旧版会静默降级成 warn"),
            ("--gat=warn",        "参数名拼错"),
            ("--nonexistent",     "根本不存在的参数"),
        };
        foreach (var (arg, why) in probes) {
            var code = RunChild(new[] { arg });
            var ok = code == 2;
            Console.WriteLine($"  {(ok ? "OK  " : "FAIL")} {arg,-22} 退出码 {code}（期望 2）  {why}");
            if (!ok) bad++;
        }

        // 合法取值必须能过：否则上面的「拒绝」可能只是把所有东西都拒了
        foreach (var arg in new[] { "--on-modified=ours", "--on-modified=merge", "--gate=enforce", "--gate=warn" }) {
            var code = RunChild(new[] { arg, "--args-test=child" });
            if (code != 0) { Console.WriteLine($"  ! 合法参数被误拒：{arg} → {code}"); bad++; }
        }

        // 文档里写了 --help，它就必须真的打印帮助然后退出 —— 否则会落到
        // 「没有位置参数 → 自动找默认安装路径」，变成「想看帮助却打开了客户端」。
        {
            var code = RunChild(new[] { "--help" });
            Console.WriteLine($"  {(code == 0 ? "OK  " : "FAIL")} --help               退出码 {code}（期望 0）");
            if (code != 0) bad++;
        }

        Console.WriteLine(bad == 0
            ? $"参数表自检：{documented.Count} 个参数双向一致，4 个拼写错误全被拒"
            : $"参数表自检：{bad} 处不一致");
        return bad == 0 ? 0 : 1;
    }

    /// <summary>用同一个入口再跑一次自己。dotnet 跑 dll 与单文件 exe 两种形态都要认。</summary>
    private static int RunChild(string[] extra) {
        var argv0 = Environment.GetCommandLineArgs()[0];
        var psi = new ProcessStartInfo { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        if (argv0.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) {
            psi.FileName = "dotnet";
            psi.ArgumentList.Add(argv0);
        } else psi.FileName = argv0;
        foreach (var e in extra) psi.ArgumentList.Add(e);
        try {
            using var p = Process.Start(psi)!;
            p.StandardOutput.ReadToEnd();
            p.StandardError.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode;
        } catch (Exception ex) {
            Console.WriteLine($"  ! 起不了子进程：{ex.Message}");
            return -1;
        }
    }

    // ---------------------------------------------------------------- 回滚自检（B1）

    /// <summary>
    /// 每个场景在临时目录里重建一次「上次写入没走完」的现场，然后调 Txn.Recover，
    /// 断言四件事：Complete / journal 是否保留 / 索引字节 / 容器外文件是否被触碰。
    /// 这四条正是旧版全部判错的地方 —— 旧版只看「有没有抛异常」。
    /// </summary>
    private static int RecoverTest() {
        var casesFile = Path.Combine(RepoRoot(), "fixtures", "recover", "cases.json");
        if (!File.Exists(casesFile)) { Console.WriteLine($"找不到 {casesFile}"); return 1; }

        using var doc = JsonDocument.Parse(File.ReadAllText(casesFile, Encoding.UTF8));
        var cases = doc.RootElement.GetProperty("cases");
        var bad = 0;
        var n = 0;

        foreach (var c in cases.EnumerateArray()) {
            n++;
            var id = c.GetProperty("id").GetString() ?? "?";
            var desc = c.GetProperty("desc").GetString() ?? "";
            var backup = c.GetProperty("backup").GetString() == "present";
            var journal = c.GetProperty("journal").GetString() ?? "valid";
            var newBundles = c.TryGetProperty("newBundles", out var nbEl)
                ? nbEl.EnumerateArray().Select(x => x.GetString() ?? "").ToArray()
                : Array.Empty<string>();
            var exp = c.GetProperty("expect");
            var wantComplete = exp.GetProperty("complete").GetBoolean();
            var wantKept = exp.GetProperty("journalKept").GetBoolean();
            var wantIndex = exp.GetProperty("index").GetString() ?? "unchanged";

            var tmp = Path.Combine(Path.GetTempPath(), "poezh-recover-" + Guid.NewGuid().ToString("n")[..8]);
            var bundleDir = Path.Combine(tmp, "Bundles2");
            var metaDir = Path.Combine(bundleDir, ".poe-zh");
            var index = Path.Combine(bundleDir, "_.index.bin");
            var backupFile = Path.Combine(metaDir, "index-20260101-000000.bin");
            var outside = Path.Combine(tmp, "outside.bin");
            var journalPath = Path.Combine(metaDir, "txn.json");

            var curBytes = Encoding.ASCII.GetBytes("HALF-WRITTEN-INDEX");
            var bakBytes = Encoding.ASCII.GetBytes("GOOD-INDEX-FROM-BACKUP");
            // C7：索引已经被上一次回滚写成了备份内容，只剩日志没删
            if (c.TryGetProperty("indexSameAsBackup", out var sameEl) && sameEl.GetBoolean())
                curBytes = bakBytes;
            var attempts = c.TryGetProperty("attempts", out var atEl) ? atEl.GetInt32() : 0;
            var wantMtimeSame = exp.TryGetProperty("indexMtimeUnchanged", out var mtEl) && mtEl.GetBoolean();

            Directory.CreateDirectory(metaDir);
            File.WriteAllBytes(index, curBytes);
            if (backup) File.WriteAllBytes(backupFile, bakBytes);
            if (newBundles.Length > 0) File.WriteAllBytes(outside, Encoding.ASCII.GetBytes("MUST-SURVIVE"));
            var mtimeBefore = File.GetLastWriteTimeUtc(index);

            if (journal == "corrupt") {
                File.WriteAllText(journalPath, "{{{", TextEnc.Utf8);
            } else {
                var j = new JournalFile {
                    Started = "20260101-000000",
                    Index = index,
                    // 备份不存在时也要给出路径：这正是 C2 —— 路径有、文件没了
                    IndexBackup = backupFile,
                    Phase = "writing",
                    Attempts = attempts,
                    NewBundles = newBundles.ToList(),
                };
                File.WriteAllText(journalPath,
                    JsonSerializer.Serialize(j, new JsonSerializerOptions { WriteIndented = true }), TextEnc.Utf8);
            }

            RecoverResult r;
            try { r = Txn.Recover(index); }
            catch (Exception ex) {
                Console.WriteLine($"  FAIL {id} {desc}：Recover 抛了异常 {ex.Message}");
                bad++; TryClean(tmp); continue;
            }

            var problems = new List<string>();
            if (!r.Recovered) problems.Add("Recovered=false（日志明明在）");
            if (r.Complete != wantComplete) problems.Add($"Complete={r.Complete}，期望 {wantComplete}");
            var kept = File.Exists(journalPath);
            if (kept != wantKept) problems.Add($"journal {(kept ? "仍在" : "被删")}，期望{(wantKept ? "保留" : "删除")}");

            var got = File.ReadAllBytes(index);
            var expectBytes = wantIndex == "backup" ? bakBytes : curBytes;
            if (!got.SequenceEqual(expectBytes))
                problems.Add($"索引字节不对（期望 {(wantIndex == "backup" ? "备份内容" : "保持原样")}）");

            if (newBundles.Length > 0 && !File.Exists(outside))
                problems.Add("容器外的 outside.bin 被删掉了（目录穿越没堵住）");

            // 幂等捷径的意义就在这里：索引已经对了就不该再写一遍。
            // mtime 变了说明它又复制了一次 20 MB。
            if (wantMtimeSame && File.GetLastWriteTimeUtc(index) != mtimeBefore)
                problems.Add("索引被重写了一遍（应当识别出「已经是备份内容」直接收尾）");

            if (problems.Count == 0) {
                Console.WriteLine($"  OK   {id} {desc}");
            } else {
                Console.WriteLine($"  FAIL {id} {desc}");
                foreach (var p in problems) Console.WriteLine($"         - {p}");
                bad++;
            }
            TryClean(tmp);
        }

        Console.WriteLine(bad == 0
            ? $"回滚自检：{n}/{n} 场景符合预期（含「备份缺失不报成功」「穿越不越界删除」）"
            : $"回滚自检：{n} 个场景里 {bad} 个不符合预期");
        return bad == 0 ? 0 : 1;
    }

    private static void TryClean(string dir) {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
    }

    // ---------------------------------------------------------------- bundle 名自检（B3）

    private static int BundleNameTest() {
        var bad = 0;
        var prefix = BundleHygiene.PrefixForTest;
        var ext = BundleHygiene.ExtForTest;
        var re = new System.Text.RegularExpressions.Regex(
            "^" + System.Text.RegularExpressions.Regex.Escape(prefix) + @"zh\d{12}-[0-9a-f]+-[0-9a-f]{4}$");

        // 1) 20000 个名字互不相同。同一秒 + 同一 pid 时只剩随机数能区分，
        //    所以进程内另有一道确定性去重 —— 这条断言钉的就是它。
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < 20000; i++) seen.Add(BundleHygiene.NewNameForTest());
        if (seen.Count != 20000) { Console.WriteLine($"  ! 20000 次只拿到 {seen.Count} 个不同的名字"); bad++; }

        // 2) 格式与长度
        foreach (var nm in seen.Take(500)) {
            if (!re.IsMatch(nm)) { Console.WriteLine($"  ! 名字格式不对：{nm}"); bad++; break; }
            var bareName = nm[prefix.Length..];
            if (bareName.Length + ext.Length > 40) { Console.WriteLine($"  ! 文件名过长：{bareName + ext}（{bareName.Length + ext.Length}）"); bad++; break; }
        }

        // 3) 撞上已有物理文件时必须换名
        var dir = Path.Combine(Path.GetTempPath(), "poezh-name-" + Guid.NewGuid().ToString("n")[..8]);
        Directory.CreateDirectory(dir);
        var first = BundleHygiene.NewNameForTest(dir);
        var bare = first[prefix.Length..];
        File.WriteAllBytes(Path.Combine(dir, bare + ext), Array.Empty<byte>());
        // 同目录再取一个：不能等于刚才那个已被占用的名字
        var second = BundleHygiene.NewNameForTest(dir);
        if (second == first) { Console.WriteLine($"  ! 已存在的名字被再次发出：{first}"); bad++; }
        TryClean(dir);

        Console.WriteLine(bad == 0
            ? $"bundle 名自检：20000 个名字无重复，格式 {prefix}zh<12位>-<pid>-<4位随机>，撞名会换"
            : $"bundle 名自检：{bad} 处不符合预期");
        return bad == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- 备份自检（B2 / B7）

    /// <summary>
    /// 四件事：轮转只在真有内容时发生、没内容就一份都不许少、外部内容永远 pin 住、
    /// 备份被改坏时能被发现。第三、四件事是「用户唯一的一份副本」的护栏。
    /// </summary>
    private static int BackupTest() {
        var bad = 0;

        static (string tmp, string index) NewContainer() {
            var tmp = Path.Combine(Path.GetTempPath(), "poezh-bak-" + Guid.NewGuid().ToString("n")[..8]);
            var root = Path.Combine(tmp, "Bundles2");
            Directory.CreateDirectory(root);
            var index = Path.Combine(root, "_.index.bin");
            File.WriteAllBytes(index, new byte[] { 1, 2, 3 });
            return (tmp, index);
        }
        static BackupSet Make(string index, string path, string flavor = "官方原文") {
            var s = BackupStore.Create(index)!;
            BackupStore.Add(s, path, Encoding.UTF8.GetBytes("payload-" + path), flavor);
            return s;
        }
        static int Count(string index) {
            var root = Path.Combine(Path.GetDirectoryName(index)!, ".poe-zh", "backup");
            return Directory.Exists(root) ? Directory.GetDirectories(root).Length : 0;
        }

        // 1) 第 6 次写入且真有内容 → 轮转到 5 份，最老那份被删
        {
            var (tmp, index) = NewContainer();
            var sets = new List<BackupSet>();
            for (var i = 0; i < 5; i++) sets.Add(Make(index, $"metadata/statdescriptions/f{i}.txt"));
            var oldest = sets[0].Dir;
            var six = Make(index, "metadata/statdescriptions/f5.txt");
            BackupStore.Settle(six);
            var n = Count(index);
            var ok = n == 5 && !Directory.Exists(oldest);
            Console.WriteLine($"  {(ok ? "OK  " : "FAIL")} 第 6 次写入有内容 → 剩 {n} 份，最老一份{(Directory.Exists(oldest) ? "仍在（应删）" : "已删")}");
            if (!ok) bad++;
            TryClean(tmp);
        }

        // 2) 第 6 次什么都没写 → 一份都不能少（旧版在 Create 里就轮转了，这里会掉到 4 份）
        {
            var (tmp, index) = NewContainer();
            var sets = new List<BackupSet>();
            for (var i = 0; i < 5; i++) sets.Add(Make(index, $"metadata/statdescriptions/f{i}.txt"));
            var before = Count(index);
            var six = BackupStore.Create(index)!;       // 不 Add 任何文件 = 这次什么都没覆盖
            BackupStore.Settle(six);
            var after = Count(index);
            var ok = before == 5 && after == 5 && sets.All(s => Directory.Exists(s.Dir));
            Console.WriteLine($"  {(ok ? "OK  " : "FAIL")} 第 6 次零写入 → {before} 份 → {after} 份（一份都不能少）");
            if (!ok) bad++;
            TryClean(tmp);
        }

        // 3) 最老那份里是「外部改过」的字节 → 轮转必须 pin 住它
        {
            var (tmp, index) = NewContainer();
            var foreign = Make(index, "metadata/statdescriptions/foreign.txt", "外部改过");
            for (var i = 0; i < 4; i++) Make(index, $"metadata/statdescriptions/f{i}.txt");
            var six = Make(index, "metadata/statdescriptions/f5.txt");
            BackupStore.Settle(six);
            var ok = Directory.Exists(foreign.Dir);
            Console.WriteLine($"  {(ok ? "OK  " : "FAIL")} 最老一份含外部内容 → {(ok ? "被 pin 住，没删" : "被轮转删掉了")}");
            if (!ok) bad++;
            TryClean(tmp);
        }

        // 4) 备份文件被改坏 → Read 必须报 shaOk=false，但字节仍然给出来
        {
            var (tmp, index) = NewContainer();
            var s = Make(index, "metadata/statdescriptions/tamper.txt");
            var it = s.Items[0];
            var p = Path.Combine(s.Dir, it.File);
            var b = File.ReadAllBytes(p);
            b[0] ^= 0xFF;
            File.WriteAllBytes(p, b);

            var got = BackupStore.Read(s, it, out var shaOk);
            var ok = got is not null && !shaOk;
            Console.WriteLine($"  {(ok ? "OK  " : "FAIL")} 篡改一个字节 → shaOk={shaOk}，字节{(got is null ? "丢了" : "仍返回（唯一副本不丢）")}");
            if (!ok) bad++;
            TryClean(tmp);
        }

        Console.WriteLine(bad == 0
            ? "备份自检：轮转时机、零写入不丢份、外部内容 pin、篡改可发现，四项全过"
            : $"备份自检：{bad} 项不符合预期");
        return bad == 0 ? 0 : 1;
    }

    // usage 单独抽成函数：--args-test 要用正则从这份文本里反推「文档里写了哪些参数」，
    // 再和 KnownFlags 双向比对。打印与校验读同一份字符串，才不会有第三个副本。
    private static string UsageText() {
        var sb = new StringBuilder();
        void L(string s = "") => sb.AppendLine(s);

        L($"PoE 繁中改良汉化 补丁器  v{_ver}");
        L();
        L("把下面任意一个拖到本程序上：");
        L("  · 官方独立端：游戏目录里的 Content.ggpk");
        L("  · Steam 端  ：游戏目录里的 Bundles2 目录（内含 _.index.bin）");
        L();
        L("直接双击也可以，会自动找默认安装路径。");
        L();
        L("命令行：");
        L("  PoE-zh-Patcher.exe <Content.ggpk | Bundles2>              交互菜单");
        L("  PoE-zh-Patcher.exe <...> --set=translated                 纯译文");
        L("  PoE-zh-Patcher.exe <...> --set=dual                       对照版");
        L("  PoE-zh-Patcher.exe <...> --set=dualall                    全量对照");
        L("  PoE-zh-Patcher.exe <...> --set=original                   还原官方原文");
        L("  PoE-zh-Patcher.exe <...> --status                         只看状态");
        L("  PoE-zh-Patcher.exe <...> --selftest                       数据与文件对得上吗");
        L("  PoE-zh-Patcher.exe <...> --struct                         结构演练（只验读写通道）");
        L("  PoE-zh-Patcher.exe <...> --collect [--out=目录]           采集诊断包（只读，发给作者排查；落盘前脱敏）");
        L("  PoE-zh-Patcher.exe <...> --writetest[=N]                  写入演练（在索引副本上写 N 个文件，不碰客户端）");
        L("  PoE-zh-Patcher.exe <...> --bundles[=all]                 看索引里的 bundle 记录（排查 bad allocation / Buffer Underflow）");
        L("  PoE-zh-Patcher.exe <...> --dump=<容器内路径>              把容器里的文件原样导出来看（只读）");
        L("  PoE-zh-Patcher.exe <...> --detach-test                    拆离自建 bundle 并列出记录（排障用）");
        L("  PoE-zh-Patcher.exe <...> --set=translated --partial       只处理对得上的文件，跳过对不上的");
        L("  PoE-zh-Patcher.exe <...> --set=translated --plan          只演算校验，不写文件");
        L("  PoE-zh-Patcher.exe <...> --set=translated --only=<关键词>  只改文件名含该关键词的文件（最小验证用）");
        L("  PoE-zh-Patcher.exe <...> --locale-only[=original]        只处理语系与国旗，不碰译文");
        L("  --help                                                    打印这份说明");
        L();
        L("安全相关：");
        L("  --on-modified=merge|ours|original|skip   遇到被别的汉化改过的文件怎么办（默认 merge）");
        L("                                           merge 只叠译文、ours 换成我们的、original 退回官方原文");
        L("  <...> --backups                          看有哪些还原点（整份 / 单个文件）");
        L("  <...> --restore-backup=<时间戳|latest>   把某次备份的文件整份写回去");
        L("  <Content.ggpk> --restore-ggpk[=时间戳]    把整个 ggpk 退回那次安装之前（游戏打不开时用）");
        L("  <...> --baseline                         看基准（这个版本下每份文件第一次被看到的样子）");
        L("  <...> --baseline --restore-baseline=<关键词>  把匹配的文件整份写回基准（= 硬还原）");
        L("  <...> --gate=off|warn|enforce            内容闸：写进去的每一行必须能从原文+译文推出来");
        L("                                           warn 只记（默认，灰度），enforce 直接拒写，off 关掉");
        L("  <...> --yes                              回滚没退干净时强行继续（默认中止，风险自负）");
        L("  <...> --reset-recover                    只丢弃「上次没走完」的记录，不回滚任何字节");
        L("                                           用于已验证过完整性、索引已被官方重建的情况");
        L("  <...> --allow-no-rollback                允许在「没有退路」的容器上写入（排障用，慎用）");
        L("  基准是每次写入的算式输入，与「改过几次」无关；它不随备份轮转，按游戏版本分开存。");
        L("  每次写入前都留还原点：Bundles2 复制 _.index.bin；ggpk 逐块记原字节。");
        L("  写坏了自动整份退回；进程被杀 / 断电也能在下次启动时自动退回。");
        L("  排障用：--simulate-fail=N 故意在第 N 个文件失败，验证退回流程（不写坏客户端）");
        L("  <...> --set=translated --keep-markup      保留底稿的标记层，只叠文本（外部改过的客户端）");
        L("  --sd-test=<某份词缀文件.txt>              只读演练：判定 + 合并 + 应改行数");
        L("  --sd-test=<文件> --fixture=lf            同上，但先把行尾转成 LF（复现第三方汉化的写法）");
        L();
        L("自检（都不需要打开游戏）：");
        L("  --args-test            参数表与这份文档是否一致、拼错的参数是否被拒");
        L("  --recover-test         回滚失败的五个场景（假成功、删现场、名字穿越）");
        L("  --bundle-name-test     自建 bundle 名的唯一性与格式");
        L("  --backup-test          备份轮转、外部内容 pin、备份读回校验");
        L("  --undo-test            ggpk 逐字节还原（含多份日志、越界记录）");
        L("  --baseline-test        基准只存一次、读回一致、版本隔离");
        L("  --legacy-test          旧版对照的识别");
        L("  --markup-test          标记层合并");
        L("  --gate-test[=仓库根]   内容闸（与 tools/content_gate.py 共用一份样本）");
        L("  --redact-test          采集包脱敏");
        L("  --dat-test=<文件>      外部改过的数据表演练（只读）");
        L("  --data-test=<基线目录> 数据包自检（拿官方原文重算一遍，比对发布 sha）");
        L("  --lang-test=<文件>     语系切换往返");
        L("  --flag-test=<文件>     国旗切换往返");
        return sb.ToString();
    }

    private static void PrintUsage() => Console.Write(UsageText());

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

    /// <summary>不抛异常的行数：诊断用，文件连 BOM 都没有时返回 -1。</summary>
    private static int LineCountSafe(byte[] b) {
        if (b.Length < 2 || b[0] != 0xFF || b[1] != 0xFE) return -1;
        var s = Encoding.Unicode.GetString(b, 2, b.Length - 2);
        return s.Split(new[] { "\r\n" }, StringSplitOptions.None).Length;
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

    /// <summary>
    /// 「认不出」到底是因为什么。行为不变（仍然拒写），只是把原因说准。
    ///
    /// 以前所有认不出的情况都归到「行数 N ≠ 基线 M」，而 LF-only 的文件整份会被当成一行，
    /// 于是提示是「行数 1 ≠ 基线 268」—— 用户会去怀疑数据包版本，实际是行尾格式。
    /// </summary>
    private static string WhyUnknown(byte[] cur, SdEntry e) {
        if (cur.Length < 2 || cur[0] != 0xFF || cur[1] != 0xFE)
            return "文件不是 UTF-16LE 带 BOM（本工具只支持这种编码）";
        var s = Encoding.Unicode.GetString(cur, 2, cur.Length - 2);
        if (!s.Contains("\r\n") && s.Contains('\n'))
            return "行尾是 LF，本工具只支持 CRLF（整份文件被当成了一行）";
        var n = s.Split(new[] { "\r\n" }, StringSplitOptions.None).Length;
        if (e.BaseLines > 0 && n != e.BaseLines)
            return $"行数 {n:N0} 与基线 {e.BaseLines:N0} 不符（游戏更新过？或文件被别的工具改过）";
        return "内容与原文、译文、对照三种形态都不匹配（可能是别的汉化写的）";
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

            if (curF == Flavor.DualLegacy) {
                // 认得出的旧版对照：内容就是「原文 + 译文」，直接整块换成目标文本。
                // 不做标记层继承 —— 这份内容是我们自己写的，标记本就派生自 O/T，没有额外信息；
                // 真要保住别人加的颜色，那种文件会先被 TryCustomBase 判成 Custom 走上一条路。
                var want = TextFor(r, tgtF);
                if (now == want) continue;
                lines[r.No] = raw[..j] + want + raw[k..];
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

        // 基准：这份文件在本次游戏版本下「第一次被本程序看到」的完整字节。
        // 写入时优先以它为准算，而不是以当前文件为准 —— 这样结果与「改过几次」无关。
        public byte[]? Base;
        public Flavor BaseFlavor;
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

    // ---------------------------------------------------------------- 旧版对照的接管
    //
    // v0.4.2 及之前的 dual_body 是「中文整段 + 英文整段堆在末尾，只给第一行缩进」，
    // 现在是「逐组穿插」。同一份译文，两种拼法的整块 sha 必然不同 —— 但**行还是那些行**。
    // 所以认它不能比整块，要比「每一行归到哪一版」：
    //
    //   把原文 / 译文 / 对照 / 英文原文 / 上一版文本，全部按 \n 拆成行，收成一个行池；
    //   文件里记录的每一格，块内每一行去标记、去首尾空白后都得落在池子里。
    //
    // 全部落得进去 → 这就是「原文与译文的某个排列」，是我们自己的产物，接管。
    // 有一行落不进去 → 那行不是我们写的（别人的译文，或译文本身被改过），不接管。
    //
    // 这条判据不认「拼法」，只认「行」：以后再换多少次排列方式都仍然认得出来，
    // 不会每改一次 dual_body 就有一批老客户端变成「未知」。
    private static bool TryLegacyDual(SdEntry e, byte[] cur, out string why) {
        why = "";
        List<string> lines;
        try { lines = SplitLines(cur); } catch (Exception ex) { why = ex.Message; return false; }

        var fromOrig = new HashSet<string>(StringComparer.Ordinal);   // 只出现在原文里的行
        var fromOur = new HashSet<string>(StringComparer.Ordinal);    // 只出现在译文/英文/旧版里的行
        var origLines = new HashSet<string>(StringComparer.Ordinal);
        var ourLines = new HashSet<string>(StringComparer.Ordinal);
        void Split(string? t, HashSet<string> dst) {
            if (string.IsNullOrEmpty(t)) return;
            foreach (var ln in t.Split(Nl)) {
                var k = LineKey(ln);
                if (k.Length > 0) dst.Add(k);
            }
        }
        foreach (var r in e.Rows) {
            Split(r.O, origLines);
            Split(r.T, ourLines);
            Split(r.D, ourLines);
            Split(r.E, ourLines);
            foreach (var l in r.Legacy) Split(l, ourLines);
        }
        // 一段文本既可能是原文行也可能是译文行（没翻译的专名），按「我们能写的」优先
        foreach (var k in origLines) if (!ourLines.Contains(k)) fromOrig.Add(k);
        foreach (var k in ourLines) fromOur.Add(k);
        if (fromOrig.Count + fromOur.Count == 0) { why = "数据包里没有可用的行文本"; return false; }

        var nBlocks = 0;        // 有多少格的块里带了译文
        var nBare = 0;          // 还停在原文的格
        foreach (var r in e.Rows) {
            if (r.No < 0 || r.No >= lines.Count) { why = $"第 {r.No + 1} 行超出文件范围"; return false; }
            var (j, k) = TextSpan(lines[r.No]);
            if (j < 0 || k <= j) { why = $"第 {r.No + 1} 行找不到引号文本"; return false; }
            var now = lines[r.No][j..k];
            if (LineKey(now) == LineKey(r.O)) { nBare++; continue; }   // 这一格没动，仍是原文
            var ours = false;
            foreach (var ln in now.Split(Nl)) {
                var key = LineKey(ln);
                if (key.Length == 0) continue;
                if (fromOur.Contains(key)) { ours = true; continue; }
                if (fromOrig.Contains(key)) continue;
                why = $"第 {r.No + 1} 行里有一段不是我们写的：{Clip(key)}";
                return false;
            }
            if (!ours) {
                // 整块都由原文行组成，却没有一行来自译文 —— 这是原文的重新排列，不是对照版
                why = $"第 {r.No + 1} 行只有原文，没有译文";
                return false;
            }
            nBlocks++;
        }
        if (nBlocks == 0) {
            why = nBare > 0 ? $"全部 {nBare} 格都还是原文" : "没有可判定的行";
            return false;
        }
        why = $"{nBlocks} 格是「原文 + 译文」的排列（{nBare} 格仍是原文），拼法与本版不同";
        return true;
    }

    // 行级归一的键：剥掉标记、去掉首尾空白（旧版给英文行补的缩进就去在这一步）
    private static string LineKey(string s) => Markup.Core(s).Trim();

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
                // 先看「结构没变、只有标记被外人动过」（保住对方的颜色），
                // 再看「是我们自己用旧拼法写的对照版」（接管、整块换掉）。
                if (TryCustomBase(e, cur, out why, out var knownOnly)) {
                    it.CurFlavor = Flavor.Custom;
                    it.Legacy = !knownOnly;      // 只有旧版补丁才会用到 legacy 文本
                } else if (TryLegacyDual(e, cur, out var lwhy)) {
                    it.CurFlavor = Flavor.DualLegacy;
                    why = lwhy;
                }
                // 两条接管路径都不成立：给出结构层面的具体原因，而不是「行数 1 ≠ 基线 268」
                if (it.CurFlavor == Flavor.Unknown) why = WhyUnknown(cur, e);
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

    // ---------------------------------------------------------------- 基准
    //
    // 每次写入都以「基准」为算式输入，而不是当前文件 —— 结果只取决于
    // （基准, 目标 flavor, 数据包版本），与「之前改过几次」完全无关。
    //
    // 基准 = 该文件在这个游戏版本下**第一次被本程序看到**的完整字节。建一次就不再变：
    // 常量一变，结果就又和历史有关了。认不出的内容不建基准 —— 连它是什么都不知道，
    // 拿来当常量只会把看不懂的东西固化下来。
    private static void ApplyBaseline(IContainer store, List<Item> items, bool create) {
        var hit = 0; var made = 0; var refused = 0; var restored = 0;
        foreach (var i in items) {
            if (i.Missing || i.Broken || i.Cur.Length == 0) continue;
            var b = BaselineStore.Get(store.IndexFile, _gameClient, _gamePatch, i.Path, out var tag);
            if (b is not null) {
                i.Base = b;
                i.BaseFlavor = FlavorFromTag(tag);
                hit++;
                continue;
            }
            if (!create || i.CurFlavor == Flavor.Unknown) { refused++; continue; }

            // 建基准时尽量存「原文」：当前若是我们自己改过的版本，先从数据包还原回原文。
            // 还原后 sha 必须等于数据包锁定的原文 sha 才作数 —— 这是判断「原文是否完好」的唯一真相：
            // 对得上说明没被我们记录之外的地方动过；对不上就退回存当前这份（次优，但至少是稳定常量）。
            var seed = i.Cur;
            var seedFlavor = i.CurFlavor;
            try {
                if (!i.IsSd && i.Dat is not null && i.CurFlavor != Flavor.Custom) {
                    seed = i.Dat.Orig;                  // dat 表有整份原文字节，直接就是权威原文
                    seedFlavor = Flavor.Original;
                } else if (i.IsSd && i.Sd is not null
                           && i.CurFlavor is Flavor.Translated or Flavor.Dual or Flavor.DualAll or Flavor.DualLegacy) {
                    var back = Rewrite(i.Sd, i.Cur, i.CurFlavor, Flavor.Original);
                    if (Sha(back) == i.Sd.ShaOriginal) { seed = back; seedFlavor = Flavor.Original; }
                }
            } catch { /* 还原不出来就用当前这份，不影响基准的稳定性 */ }

            if (BaselineStore.Put(store.IndexFile, _gameClient, _gamePatch, _ver, i.Path,
                                  seed, seedFlavor.ToString())) {
                made++;
                if (seedFlavor == Flavor.Original && i.CurFlavor != Flavor.Original) restored++;
            } else refused++;
        }
        if (hit + made + refused == 0) return;
        var parts = new List<string>();
        if (hit > 0) parts.Add($"已有 {hit}");
        if (made > 0) parts.Add($"新建 {made}" + (restored > 0 ? $"（其中 {restored} 份还原自原文）" : ""));
        if (refused > 0) parts.Add($"无基准 {refused}");
        Console.WriteLine($"  基准（{_gameClient}{(_gamePatch.Length > 0 ? "+" + _gamePatch : "")}）：{string.Join("  ", parts)}");
    }

    private static Flavor FlavorFromTag(string s) => Enum.TryParse<Flavor>(s, out var f) ? f : Flavor.Unknown;

    private static string ShowFlavor(Item i) => i.CurFlavor switch {
        Flavor.Unknown when i.Broken => "读不出",
        Flavor.Unknown => "未知",
        Flavor.Custom => i.Legacy ? "旧版补丁" : "外部改过",
        Flavor.DualLegacy => "旧版对照",
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
            // 基准：这个版本下每份文件「第一次被看到」的样子。它决定「还原」能有多硬 ——
            // 有原文基准时，认不出当前内容也能整份写回去。
            var bi = BaselineStore.Load(store.IndexFile, _gameClient, _gamePatch);
            if (bi is null) Console.WriteLine(" 基准      无（下次安装时自动建立）");
            else Console.WriteLine($" 基准      {bi.Files.Count} 份（原文 {bi.Files.Values.Count(f => f.Flavor == nameof(Flavor.Original))}）"
                                   + $"  {bi.TotalBytes / 1048576.0:F1} MB");
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
            Console.WriteLine("  [k] 基准          —— 这个版本下每份文件第一次被看到的样子（还原最硬的依据）");
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
            if (k is "k") {
                ShowBaselines(store, "");
                Console.WriteLine();
                var bi = BaselineStore.Load(store.IndexFile, _gameClient, _gamePatch);
                if (bi is not null && bi.Files.Count > 0) {
                    Console.Write(" 写回基准？(输入路径关键词 / 回车 = 只看)：");
                    var pat = (ReadKeyLine() ?? "").Trim();
                    if (pat.Length > 0) ShowBaselines(store, pat);
                }
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
        if (policy is null) {
            // 解析阶段已经拦过一次；走到这里说明是代码里拼出来的 Args。
            // 宁可停下，也不要挑一个默认策略继续写。
            Console.WriteLine($"--on-modified 的取值不认识：{Args.GetValueOrDefault("on-modified")}");
            Console.WriteLine("  可选 merge / ours / original / skip");
            return 2;
        }
        var pol = policy.Value;
        // .dat 表内嵌了官方原文与成品两套字节，所以 Unknown 也能整份换；
        // statdesc 没有内嵌原文字节，能做的只有「逐行把我们写过的行撤回去」——
        // 认不出的行（别的汉化写的）一个字节都不动，所以它仍然是完整可读的一份文件。
        var forced = pol switch {
            Policy.Ours => unknown.Where(i => !i.IsSd).ToList(),
            Policy.Original => unknown.ToList(),
            // 整体还原时也尽量退回原文，否则「还原」会卡在几个认不出的文件上走不下去
            _ => target == Flavor.Original ? unknown.ToList() : new List<Item>(),
        };
        var skip = missing.Concat(broken).Concat(unknown.Except(forced)).ToList();

        // 内容闸的最后一道：数据包里的文本，在真写进客户端之前过一遍。
        // 这里拦的是「内嵌数据被人换过」，前两道（patch_sd / build_bundle）拦的是
        // 「我们自己的修正稿写错了」。三道里少一道，就有一条旁路。
        var gateMode = GateMode();
        if (gateMode is null) {
            Console.WriteLine($"--gate 的取值不认识：{Args.GetValueOrDefault("gate")}");
            Console.WriteLine("  可选 off / warn / enforce");
            return 2;
        }
        var gm = gateMode.Value;
        var gateHits = GateBundle(items, target, gm);
        if (gm != ContentGate.Mode.Off) {
            var rows = items.Where(i => i.Sd is not null && !i.Missing && !i.Broken)
                            .Sum(i => i.Sd!.Rows.Count(r => TextFor(r, target) != r.O));
            Console.WriteLine($"  内容闸[{gm.ToString().ToLowerInvariant()}]：{rows} 行已过，" +
                              (gateHits.Count == 0 ? "0 命中" : $"{gateHits.Count} 处命中"));
        }
        if (gateHits.Count > 0) {
            Console.WriteLine();
            Console.WriteLine($"!! 内容闸[{gm.ToString().ToLowerInvariant()}]：{gateHits.Count} 处文本不是我们能推导出来的");
            foreach (var (it, rule, msg) in gateHits.Take(6))
                Console.WriteLine($"    {rule} {msg}");
            if (gateHits.Count > 6) Console.WriteLine($"    ... 还有 {gateHits.Count - 6} 处");
            if (gm == ContentGate.Mode.Enforce) {
                Console.WriteLine("  enforce 模式：一个都没写。数据包（bundle.gz）可能被人换过。");
                Console.WriteLine("  确认无误的话用 --gate=warn 只警告、--gate=off 直接关掉。");
                return 7;
            }
            Console.WriteLine("  warn 模式：照常写入。这是灰度期的默认值 —— 先看清命中什么，再决定要不要拦。");
        }

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

        // --on-modified=skip 的语义就是「一个都不动，只翻能对上的」，
        // 所以它自己就该放行，不必再叠一个 --partial（以前提示里写着能用、实际仍中止）。
        var tolerate = partial || pol == Policy.Skip;
        if (skip.Count > 0) {
            if (!tolerate) {
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
            if (!tolerate) {
                Console.WriteLine($"中止：游戏版本与数据包不一致（数据包 {_gameClient}）。");
                Console.WriteLine("  重启游戏让它更新一次日志，或等新版数据包。");
                return 4;
            }
            Console.WriteLine($"注意：游戏版本与数据包不一致（数据包 {_gameClient}），部分安装会跳过对不上的文件。");
            Console.WriteLine();
        }

        var plan = Args.ContainsKey("plan");
        // 放行模式（--partial 或 --on-modified=skip）下，skip 里的文件一个都不碰：
        // 跳过 = 保持它们现在能被游戏读到的样子，正是 --on-modified=skip 的承诺。
        var pool = tolerate
            ? items.Where(i => !skip.Contains(i)).ToList()
            : items;
        if (forced.Count > 0 && !partial) {
            Console.WriteLine($"  按 --on-modified={PolicyName(pol)} 处理 {forced.Count} 个对不上基线的数据表"
                              + "（原字节会先备份，随时可以还原）");
        }

        // 历史自建 bundle（历次运行留下的 LibGGPK3/N）里记的 offset / UncompressedSize
        // 已经和物理文件对不上：复用它就是 Buffer Underflow 的来源。
        // 先只做只读清点，把还挂在旧记录上的文件圈出来；确认真的要写之后才换新 bundle。
        var inherited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // 演算时也算这一项：不然 --plan 说「需要改动 0 个」，真跑却写了 47 个（都是搬 bundle），
        // 用户看到两个数字对不上只会以为程序在乱写。
        foreach (var i in items)
            if (store.IsSelfBundle(i.Path)) inherited.Add(i.Path);
        if (inherited.Count > 0) {
            var movable = items.Count(i => inherited.Contains(i.Path) && i.CurFlavor != Flavor.Unknown);
            foreach (var i in items)
                if (inherited.Contains(i.Path) && i.CurFlavor != Flavor.Unknown) i.Force = true;
            Console.WriteLine($"  {inherited.Count} 个文件还挂在历史自建 bundle 上，其中 {movable} 个可以重新落一遍");
        }

        // 基准：越早建立越可能抓到干净的那份。这一次要写，就先趁「现在这份还认得」存下来，
        // 之后所有计算都以它为准（--plan 只演算，不建）。
        ApplyBaseline(store, items, create: !plan);

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
            // 备份目录建不出来（Program Files 下没写权限、磁盘满）就等于「被覆盖的原内容会永久丢」。
            // 以前这里是静默吞异常照样写，等于把「没退路就不写」这条底线破在自己家里。
            if (backupSet is null && !AllowNoRollback()) {
                Console.WriteLine($"中止：无法在 {BackupStore.RootFor(store.IndexFile)} 建备份目录，一个字节都没写。");
                Console.WriteLine("  · 这里是游戏目录，可能需要以管理员身份运行");
                Console.WriteLine("  · 或磁盘已满、目录被安全软件锁住");
                Console.WriteLine("  · 确实接受「被覆盖的原始内容不留档」，加 --allow-no-rollback");
                return 7;
            }
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
                        // 当前内容认不出（别人的汉化、或结构被弄乱）：
                        // 有基准就整份写回，不猜任何一行；没基准才退到逐行撤。
                        if (i.Base is not null && i.BaseFlavor == Flavor.Original) {
                            targetBytes = i.Base;
                            expectSha = e.ShaOriginal;
                            Console.WriteLine($"  {label,-46} 用基准整份写回原文（当前内容认不出，不逐行猜）");
                        } else {
                            // 行数/文本都对不上，重建不出「整份原文」。能做的只有逐行把我们写过的行撤回去，
                            // 认不出的行（别的汉化写的）原样保留 —— 文件仍然完整可读，不会变成半截。
                            targetBytes = RewriteUnknownToOriginal(e, i.Cur, out var nRev, out var nKeep);
                            expectSha = null;
                            Console.WriteLine($"  {label,-46} 逐行退回原文：撤销 {nRev} 行，{nKeep} 行认不出（保持原样）");
                        }
                    } else if (i.CurFlavor == Flavor.Custom && policy != Policy.Ours) {
                        // 别人的底稿 + merge：只能基于当前文件叠文本层（别人的标记在基准里没有），
                        // 这条路径本质上与「改过几次」有关，所以它也是最需要备份兜底的一条。
                        targetBytes = Rewrite(e, i.Cur, i.CurFlavor, effTarget);
                        expectSha = TargetSha(e, effTarget, i.CurFlavor);
                    } else {
                        // 其余情况一律以基准为准：结果只取决于（基准, 目标），与之前改过多少次无关。
                        // 基准是 Original 时，TargetSha 给出数据包里锁定的 sha —— 校验就从「自证」变成真校验。
                        var srcBytes = i.Base ?? i.Cur;
                        var srcFlavor = i.Base is not null ? i.BaseFlavor : i.CurFlavor;
                        targetBytes = Rewrite(e, srcBytes, srcFlavor, effTarget,
                                              forceOurs: policy == Policy.Ours && i.CurFlavor == Flavor.Custom);
                        expectSha = TargetSha(e, effTarget, srcFlavor);
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
                // 内容没变的（只是搬 bundle 的那种重写）不用备份 —— 没覆盖掉任何东西，
                // 而每次安装都会重写全部文件，无脑备份会让 .poe-zh 白白涨几十 MB。
                if (backupSet is not null && Sha(i.Cur) != got)
                    BackupStore.Add(backupSet, i.Path, i.Cur, ShowFlavor(i));
                // 快照轮转要知道「这次盖掉的是不是别人的译文」：是的话那份快照里可能
                // 存着别处不存在的字节，不能被省磁盘的轮转顺手删掉（见 GgpkUndo.Prune）
                store.Undo?.NoteFlavor(ShowFlavor(i));

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
            if (backupSet is not null) BackupStore.Settle(backupSet);
            try { store.TrimAfterCommit(); } catch (Exception ex) {
                Console.WriteLine($"  ! 清理旧 bundle 时出错（不影响游戏）：{ex.Message}");
            }
        }

        Console.WriteLine(plan
            ? $"演算 {nDone} 个（未写入任何文件），失败 {nFail} 个，用时 {sw.Elapsed.TotalSeconds:F1} s"
            : $"完成 {nDone} 个，回读通过 {nBack} 个，失败 {nFail} 个，用时 {sw.Elapsed.TotalSeconds:F1} s");
        // 只在真有东西被覆盖时才提备份路径：内容没变的那种重写不会产生备份，
        // 空目录也已经被 DropIfEmpty 删掉了，还按老样子提示就是指向一个不存在的地方。
        if (!plan && nDone > 0 && backupSet is { Items.Count: > 0 })
            Console.WriteLine($"备份：{backupSet.Dir}（用 --restore-backup 可以整份写回去）");
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

    // 内容闸默认 warn：误杀比漏放更容易被看见。灰度一个版本、确认 0 命中后再切 enforce。
    // 取值不认识时返回 null，**不回落到 warn** —— 静默降级等于让用户以为闸开着。
    private static ContentGate.Mode? GateModeOf(string? s) => (s ?? "").Trim().ToLowerInvariant() switch {
        "" or "warn" => ContentGate.Mode.Warn,
        "off" => ContentGate.Mode.Off,
        "enforce" or "strict" => ContentGate.Mode.Enforce,
        _ => null,
    };

    private static ContentGate.Mode? GateMode() => GateModeOf(Args.GetValueOrDefault("gate"));

    // 数据包里的文本在写进客户端之前过一遍闸。
    // 查的是「要写进去的那一行」（TextFor），不是容器里现有的内容 ——
    // 容器里那行是谁写的我们管不着，写进去的是我们的东西，就得能推导出来。
    private static List<(Item item, string rule, string msg)> GateBundle(List<Item> items, Flavor target,
                                                                        ContentGate.Mode mode) {
        var hits = new List<(Item, string, string)>();
        if (mode == ContentGate.Mode.Off) return hits;
        var cfg = ContentGate.Config.Load(GateRoot());
        foreach (var i in items) {
            if (i.Sd is null || i.Missing || i.Broken) continue;
            // 白名单按 raw_sd 的扁平文件名记（metadata_statdescriptions_xxx.txt）
            var gateFile = "metadata_statdescriptions_" + Path.GetFileName(i.Sd.Path);
            foreach (var r in i.Sd.Rows) {
                var text = TextFor(r, target);
                if (text == r.O) continue;                   // 没改的行不归我们管
                foreach (var f in ContentGate.CheckLine(text, oldText: r.O, cfg: cfg, file: gateFile))
                    hits.Add((i, f.Rule, $"{i.Label}:{r.No + 1} {f.Message}"));
            }
        }
        return hits;
    }

    // 取值不认识时返回 null。老版本这里是 `_ => Policy.Merge`，于是 --on-modified=our
    // （少个 s）会静默走「只叠文本」—— 用户以为整份换成了我们的，实际没有。
    private static Policy? ParsePolicy(string? s) => (s ?? "").Trim().ToLowerInvariant() switch {
        "" or "merge" => Policy.Merge,
        "ours" or "mine" => Policy.Ours,
        "original" or "orig" or "restore" => Policy.Original,
        "skip" or "off" => Policy.Skip,
        _ => null,
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

        // 先把每份备份读进内存并核对 sha —— 必须在动容器之前做。
        // 备份被磁盘啃坏时，写回去等于把损坏的内容带进客户端；而这些又往往是这台机器上
        // 唯一的一份，所以不直接拒绝，只要求明确确认（--yes）。
        var payload = new List<(BackupItem it, byte[] bytes)>();
        var suspect = new List<string>();
        foreach (var it in set.Items) {
            var bytes = BackupStore.Read(set, it, out var shaOk);
            if (bytes is null) { Console.WriteLine($"  ✗ {it.Path}：备份文件不见了"); continue; }
            if (!shaOk) suspect.Add(it.Path);
            payload.Add((it, bytes));
        }
        if (suspect.Count > 0) {
            Console.WriteLine();
            Console.WriteLine($"  ! {suspect.Count} 份备份文件与记录不符（磁盘损坏或手工改过）：");
            foreach (var p in suspect.Take(6)) Console.WriteLine($"    - {p}");
            if (suspect.Count > 6) Console.WriteLine($"    ... 还有 {suspect.Count - 6} 份");
            Console.WriteLine("  这些字节在这台机器之外不存在第二份，但已经坏了。");
            if (!Args.ContainsKey("yes")) {
                Console.WriteLine("  中止，一个字节都没写。确认要按文件当前内容写回，加 --yes。");
                return 8;
            }
            Console.WriteLine("  已指定 --yes：继续，按文件当前内容写回。");
        }

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
            foreach (var (it, bytes) in payload) {
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

    // ---------------------------------------------------------------- 基准
    //
    // 基准不是备份。备份是「改之前存一份」，一次一份、会轮转；
    // 基准是「这个版本下每份文件第一次被看到的样子」，一个版本一份、永不覆盖。
    // 它真正的用途是当算式的常量：每次写入都以「基准 + 目标 flavor」算，不读当前文件，
    // 所以改过多少次结果都一样 —— 这是「反复打补丁也不会越打越歪」的根据。
    private static int ShowBaselines(IContainer store, string restorePat) {
        if (restorePat.Length > 0) return RestoreBaseline(store, restorePat);

        var all = BaselineStore.List(store.IndexFile);
        if (all.Count == 0) {
            Console.WriteLine($"还没有基准。下次安装时会自动建立，存在 {BaselineStore.RootFor(store.IndexFile)} 下。");
            Console.WriteLine("  （基准 = 这个版本下每份文件「第一次被看到」的完整字节，之后所有写入都以它为准）");
            return 0;
        }
        Console.WriteLine($"基准：{BaselineStore.RootFor(store.IndexFile)}");
        Console.WriteLine();
        foreach (var v in all) {
            var cur = v.ClientVersion == _gameClient && v.PatchVersion == _gamePatch;
            var key = v.ClientVersion + (v.PatchVersion.Length > 0 ? "+" + v.PatchVersion : "");
            Console.WriteLine($"  {key,-18} {v.Files.Count,3} 个文件  {v.TotalBytes / 1048576.0,7:F1} MB  建于 {v.At}"
                              + (cur ? "  ← 当前版本" : "   （旧版本，不会被使用）"));
            var byF = v.Files.Values.GroupBy(f => f.Flavor).OrderByDescending(g => g.Count());
            Console.WriteLine($"      来源：{string.Join("  ", byF.Select(g => $"{BaselineName(g.Key)} {g.Count()}"))}");
        }
        Console.WriteLine();
        Console.WriteLine("把某个文件写回基准：PoE-zh-Patcher.exe <目标> --baseline --restore-baseline=<路径关键词>");
        Console.WriteLine("整套退回原文      ：PoE-zh-Patcher.exe <目标> --baseline --restore-baseline=statdescriptions");
        return 0;
    }

    private static string BaselineName(string tag) => FlavorFromTag(tag) switch {
        Flavor.Original => "原文",
        Flavor.Translated => "纯译文",
        Flavor.Dual => "对照",
        Flavor.DualAll => "全量对照",
        Flavor.DualLegacy => "旧版对照",
        Flavor.Custom => "外部改过",
        _ => tag,
    };

    /// <summary>把文件写回基准字节。这是最硬的一种还原：基准是原文（或我们第一次见到的那份），
    /// 与之后改过多少次、当前内容认不认得出，都没有关系。</summary>
    private static int RestoreBaseline(IContainer store, string pat) {
        if (!store.Alive) {
            Console.WriteLine("容器已经释放，请重新运行本程序。");
            return 6;
        }
        var idx = BaselineStore.Load(store.IndexFile, _gameClient, _gamePatch);
        if (idx is null) {
            Console.WriteLine($"还没有 {_gameClient}{(_gamePatch.Length > 0 ? "+" + _gamePatch : "")} 版本的基准。");
            Console.WriteLine("  基准在第一次安装时自动建立；换过游戏版本就得重新建立。");
            return 1;
        }
        var hits = idx.Files.Values
            .Where(f => f.Path.Contains(pat, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.Path, StringComparer.Ordinal).ToList();
        if (hits.Count == 0) {
            Console.WriteLine($"基准里没有匹配 \"{pat}\" 的文件。用 --baseline 看有哪些。");
            return 1;
        }
        Console.WriteLine($"把 {hits.Count} 个文件写回基准（{_gameClient}{(_gamePatch.Length > 0 ? "+" + _gamePatch : "")}）：");
        foreach (var h in hits.Take(6)) Console.WriteLine($"  {h.Path,-56} {BaselineName(h.Flavor)}");
        if (hits.Count > 6) Console.WriteLine($"  ... 还有 {hits.Count - 6} 个");
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

            var dir = BaselineStore.DirFor(store.IndexFile, _gameClient, _gamePatch);
            var done = new List<(Item it, string sha)>();
            foreach (var h in hits) {
                var f = Path.Combine(dir, h.File);
                if (!File.Exists(f)) { Console.WriteLine($"  ✗ {h.Path}：基准文件不见了"); continue; }
                var bytes = File.ReadAllBytes(f);
                if (Sha(bytes) != h.Sha) {
                    Console.WriteLine($"  ✗ {h.Path}：基准文件 sha 对不上（被改过或磁盘坏了），跳过不写");
                    continue;
                }
                if (!store.Write(h.Path, bytes)) {
                    Console.WriteLine($"  ✗ {h.Path}：容器里找不到这条记录");
                    continue;
                }
                var sha = Sha(bytes);
                txn?.AddWritten(h.Path, sha);
                done.Add((new Item { Label = Path.GetFileName(h.Path), Path = h.Path }, sha));
                Console.WriteLine($"  {h.Path,-56} ← 基准（{BaselineName(h.Flavor)}）");
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
            Console.WriteLine($"已把 {done.Count} 个文件写回基准。想再装汉化，直接跑一次安装即可。");
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
        // 不写全路径：采集包是要发出去的，`C:\Users\<真名>\…` 对排查毫无用处，
        // 对收包的人却是一整套身份线索。盘符 + 层数 + 文件名已经够判断安装形态。
        sb.AppendLine($"目标        [{store.Kind}] {Redact.PathHint(store.Display)}");
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
            catch (Exception ex) { rec["dump_error"] = Redact.Text(ex.Message, log: true); }

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

        // 客户端日志前 60 行，方便看版本与语言。这一份最容易夹带身份信息：
        // 登录账号、SteamID、`C:\Users\<真名>\…` 全在日志里，所以逐行过一遍脱敏。
        var logLines = 0;
        try {
            var root = store.RootDir;
            foreach (var rel in new[] { "logs/LatestClient.txt", "logs/Client.txt" }) {
                var p = System.IO.Path.Combine(root, rel);
                if (!File.Exists(p)) continue;
                var head = File.ReadLines(p).Take(60).Select(Redact.LogLine).ToArray();
                logLines = head.Length;
                File.WriteAllLines(System.IO.Path.Combine(dir, "client-log.txt"), head, TextEnc.Utf8);
                break;
            }
        } catch { }

        var readme = new List<string> {
            "这个文件夹是 PoE 繁中汉化补丁器生成的采集包，只用于让作者针对你的客户端重做数据包。",
            "",
            "请把整个文件夹（或同级的 .zip）发回给作者。内容说明：",
            "  report.txt     —— 客户端版本、每个文件的状态、对不上的文件的逐行对照",
            "  report.json    —— 同样的内容，机器可读",
            "  files/         —— 状态对不上的那几个词缀文件原文（UTF-16 文本）",
            $"  client-log.txt —— 客户端日志开头几行，含版本号（已脱敏 {logLines} 行）",
            "",
            "已脱敏项（这些东西在写出前被替换或整行删除）：",
            "  登录账号 / 会话 / 令牌 / 邮箱   整行删除，只留行首时间戳 + [REDACTED]",
            "  SteamID64（7656 开头的 17 位）  换成 <steamid>",
            "  用户目录里的用户名              换成 <user>，后面的路径保留",
            @"  其它盘的绝对路径（仅日志）      换成 <path>",
            "  游戏容器路径                    只给「盘符 · 层数 · 文件名」，不给全路径",
            "",
            "写出后会再扫一遍：如果仍能扫到真实用户名或 SteamID，就**不生成 .zip**，",
            "只留这个文件夹给你自己看。遇到这种情况请把警告内容发回给作者。",
            "",
            "采集过程只读，没有改动游戏文件。",
        };
        File.WriteAllLines(System.IO.Path.Combine(dir, "README.txt"), readme, TextEnc.Utf8);
        File.WriteAllText(System.IO.Path.Combine(dir, "report.txt"),
            Redact.Paths(sb.ToString(), log: false), TextEnc.Utf8);
        File.WriteAllText(System.IO.Path.Combine(dir, "report.json"),
            JsonSerializer.Serialize(new {
                collected_at = DateTime.Now.ToString("s"),
                patcher = _ver, bundle_built = _bundleBuilt,
                expected_game = _gameClient, expected_patch = _gamePatch,
                game = client, patch,
                // 不给 path：整条路径里有用户名。path_hint 足够判断「什么形态的安装」
                container = new { kind = store.Kind, path_hint = Redact.PathHint(store.Display), size = store.Size },
                redacted = new[] { "account/session/token/mail 整行删除", "steamid64", "user path", "absolute path in log" },
                files,
            }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }),
            TextEnc.Utf8);

        // 落盘后复扫：宁可交一个散文件夹，也不生成一个能反查到人的 zip
        var leaks = Redact.Audit(dir);
        var zip = "";
        if (leaks.Count > 0) {
            Console.WriteLine();
            Console.WriteLine($"! 脱敏自检没通过：还有 {leaks.Count} 处可能带身份信息，已跳过打包");
            foreach (var l in leaks.Take(8)) Console.WriteLine($"    {l}");
            Console.WriteLine("  （文件夹本身仍然可用；把这几行发回给作者即可）");
        } else {
            zip = dir.TrimEnd('\\', '/') + ".zip";
            try {
                if (File.Exists(zip)) File.Delete(zip);
                ZipFile.CreateFromDirectory(dir, zip, CompressionLevel.Optimal, false);
            } catch (Exception ex) {
                Console.WriteLine($"（打包 zip 失败：{ex.Message}，直接把文件夹发回也行）");
                zip = "";
            }
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
