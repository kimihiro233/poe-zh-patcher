// 语言选择界面的国旗 —— 把「法国国旗」那一项换成五星红旗。
//
// 为什么要换：LanguageSwitch 把 languages.dat 里 French / Traditional Chinese 两行的
// 语言代码对调之后，客户端语言列表里第二项（代码 fr）加载的是繁中数据，标签写着
// 「繁體中文」，配图却还是法国国旗。看着别扭，也容易让人以为点错了。
//
// 为什么不改 DDS：游戏图集里本来就有五星红旗 —— zhCN（简体中文）那一项。
// art/uiimages1.txt 是图集索引，每个图标一行：
//
//     "Art/2DArt/UIImages/Common/FlagIcons/fr"   "<贴图路径>" 840 0 914 49
//     "Art/2DArt/UIImages/Common/FlagIcons/zhCN" "<贴图路径>" 756 0 830 49
//
// 两张图集里 zhCN 与 fr 的区域尺寸完全一致（74×49 / 101×67），所以只要把 fr 那一行
// 的坐标改成 zhCN 的坐标，画面就是五星红旗，尺寸/比例/画风全都和别的国旗对得上。
// 一个字节的贴图都不用重编码。
//
// 文件格式：UTF-16LE、CRLF、无 BOM，6 MB 左右。这里只在字节层做替换 ——
// 只把被改的那两行重新编码，其余字节原样拷贝，不整文件 decode/encode
// （整文件往返遇到落单代理项会悄悄改坏数据）。
//
// 副作用（已知并接受）：语言列表里 zhCN（简体中文）那一项本来就是五星红旗，
// 换完之后列表里会有两面五星红旗。简体中文这一项没有对应的数据目录，
// 选了也只会回落到英文，所以这个重复不影响使用。
using System.Text;

public static class FlagIcon {
    /// <summary>图集索引文件在 ggpk / Bundles2 里的路径（大小写不敏感）。</summary>
    public const string Path = "art/uiimages1.txt";

    /// <summary>
    /// 每个条目 = 一行的定位串 + 法国国旗原本的坐标（x1 y1 x2 y2）。
    /// 定位串是「图标名」后面跟着的 `" "`，所以 `FlagIcons/fr" "` 不会误命中
    /// `FlagIcons/4K/fr" "`（中间隔着 4K/）。
    /// </summary>
    static readonly (string Key, string ZhKey, int[] FrHome)[] Entries = {
        ("FlagIcons/fr\" \"",   "FlagIcons/zhCN\" \"",   new[] { 840, 0, 914, 49 }),
        ("FlagIcons/4K/fr\" \"", "FlagIcons/4K/zhCN\" \"", new[] { 0, 152, 101, 219 }),
    };

    public enum State {
        Applied,    // fr 用的是五星红旗的坐标
        Original,   // fr 用的还是法国国旗的坐标
        Unknown,    // 结构对不上（多半是游戏更新过），不碰
    }

    // ---- 字节层工具 ----

    /// <summary>是不是一个换行符（U+000A 或 U+000D）——按 UTF-16LE 落在偶数偏移上判断。</summary>
    static bool IsEol(byte[] data, int k) => data[k + 1] == 0 && (data[k] == 0x0A || data[k] == 0x0D);

    /// <summary>
    /// 找到包含 needle 的整行（返回字节区间，已去掉行首尾的 CR/LF）。
    /// 实测 art/uiimages1.txt 是 UTF-16LE + 纯 LF（19534 行、无 CR），
    /// 但这里两种换行都认，游戏更新换了写法也不至于瞎。
    /// </summary>
    static bool FindLine(byte[] data, string needle, out int start, out int end) {
        start = end = -1;
        var nb = Encoding.Unicode.GetBytes(needle);
        var span = data.AsSpan();
        var i = span.IndexOf(nb);
        // 字符边界只可能落在偶数偏移上；命中的不是偶数偏移就继续往后找
        while (i >= 0 && (i & 1) != 0) {
            var next = span[(i + 2)..].IndexOf(nb);
            i = next < 0 ? -1 : i + 2 + next;
        }
        if (i < 0) return false;

        start = 0;
        for (var k = i - 2; k >= 0; k -= 2)
            if (IsEol(data, k)) { start = k + 2; break; }

        end = data.Length;
        for (var k = i; k + 1 < data.Length; k += 2)
            if (IsEol(data, k)) { end = k; break; }

        // 行首尾可能各挂一个 CR
        while (end > start && data[end - 1] == 0 && data[end - 2] == 0x0D) end -= 2;
        return end > start;
    }

    /// <summary>把一行拆成「最后一个引号及之前」+「坐标尾巴」。</summary>
    static bool SplitTail(string line, out string head, out string tail) {
        head = tail = "";
        var q = line.LastIndexOf('"');
        if (q < 0) return false;
        head = line[..(q + 1)];
        tail = line[(q + 1)..];
        return true;
    }

    /// <summary>尾巴必须是 4 个整数，否则视为结构不符。</summary>
    static bool TryParseRect(string tail, out int[] rect) {
        rect = Array.Empty<int>();
        var parts = tail.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 4) return false;
        var r = new int[4];
        for (var i = 0; i < 4; i++)
            if (!int.TryParse(parts[i], out r[i])) return false;
        rect = r;
        return true;
    }

    /// <summary>读出某个定位串那一行当前的坐标。</summary>
    static bool ReadRect(byte[] data, string key, out int[] rect) {
        rect = Array.Empty<int>();
        if (!FindLine(data, key, out var s, out var e)) return false;
        var line = Encoding.Unicode.GetString(data, s, e - s);
        return SplitTail(line, out _, out var tail) && TryParseRect(tail, out rect);
    }

    /// <summary>把某一行的坐标尾巴换成给定的四个数（只重编这一行，其余字节原样）。</summary>
    static byte[]? SetRect(byte[] data, string key, int[] rect) {
        if (!FindLine(data, key, out var s, out var e)) return null;
        var line = Encoding.Unicode.GetString(data, s, e - s);
        if (!SplitTail(line, out var head, out var tail)) return null;
        if (!TryParseRect(tail, out _)) return null;

        var newLine = head + " " + string.Join(" ", rect);
        var newBytes = Encoding.Unicode.GetBytes(newLine);

        var res = new byte[s + newBytes.Length + (data.Length - e)];
        data.AsSpan(0, s).CopyTo(res);
        newBytes.CopyTo(res, s);
        data.AsSpan(e).CopyTo(res.AsSpan(s + newBytes.Length));
        return res;
    }

    static bool Same(int[] a, int[] b) => a.Length == b.Length && a.AsSpan().SequenceEqual(b);

    // ---- 对外 ----

    /// <summary>当前是哪一态。两条 fr 行都达标才算，任何一条对不上就是 Unknown。</summary>
    public static State Detect(byte[] data) {
        var allZh = true;
        var allFr = true;
        foreach (var (key, zhKey, frHome) in Entries) {
            if (!ReadRect(data, key, out var cur)) return State.Unknown;
            if (!ReadRect(data, zhKey, out var zh)) return State.Unknown;
            allZh &= Same(cur, zh);
            allFr &= Same(cur, frHome);
        }
        if (allZh) return State.Applied;
        if (allFr) return State.Original;
        return State.Unknown;
    }

    public static string Describe(State s) => s switch {
        State.Applied => "国旗已换成五星红旗（繁中那一项）",
        State.Original => "国旗仍是法国国旗（繁中那一项）",
        _ => "无法识别（uiimages1.txt 结构对不上）",
    };

    /// <summary>
    /// 把当前内容推到目标状态。
    /// 返回 (是否需要写, 新内容, 给人看的一句话)；不需要写时新内容为 null。
    /// 目标坐标优先从同一份文件里的 zhCN 行读，读不到才退回写死的常量。
    /// </summary>
    public static bool TryBuild(byte[] current, bool wantChinese, out byte[]? next, out string message) {
        next = null;
        var st = Detect(current);
        if (st == State.Unknown) {
            message = "跳过：uiimages1.txt 结构与预期不符（多半是游戏更新过），没有改动它";
            return false;
        }
        var already = (st == State.Applied) == wantChinese;
        if (already) {
            message = "已是目标状态：" + Describe(st);
            return false;
        }

        var buf = current;
        foreach (var (key, zhKey, frHome) in Entries) {
            int[] target;
            if (wantChinese) {
                if (!ReadRect(current, zhKey, out target)) target = new[] { 756, 0, 830, 49 };
            } else {
                target = frHome;
            }
            var step = SetRect(buf, key, target);
            if (step is null) {
                message = "跳过：定位不到 " + key.Replace("\" \"", "") + " 那一行，没有改动它";
                return false;
            }
            buf = step;
        }

        next = buf;
        message = wantChinese ? "国旗换成五星红旗（繁中那一项不再显示法国国旗）" : "国旗还原成法国国旗";
        return true;
    }
}
