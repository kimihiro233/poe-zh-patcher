// 语言选择界面的国旗 —— 让「繁體中文」那一项配五星红旗，而不是法国国旗。
//
// 为什么要换：LanguageSwitch 把 languages.dat 里 French / Traditional Chinese 两行的
// 语言名对调之后，列表第二项写着「繁體中文」，语言代码却还是 `fr`，客户端就按代码去
// `FlagIcons/fr` 取图标 —— 结果是法国国旗。看着别扭，也容易让人以为点错了。
//
// 为什么不改 DDS、也不改坐标：这张表里本来就有五星红旗（`zhCN` 那一项）。
// 把 `FlagIcons/fr` 与 `FlagIcons/zhCN` 两行的**图标名对调**，客户端去取 `fr` 时
// 拿到的是写着 zhCN 的那一行（也就是五星红旗），一个贴图字节都不用碰。
// 这与 PoeChinese3 的做法一致（它换的是 fr ↔ zhTW，青天白日旗），好处是：
//   - 不依赖任何写死的矩形坐标 —— 早先的实现写死「fr 原本是 840,0,914,49」，
//     换个游戏版本 fr 变成 252,0,326,49，Detect 立刻判「无法识别」直接跳过；
//   - 天然可逆：再换一次就回到原样，不需要记住原来的坐标。
//
// 文件格式：UTF-16LE、纯 LF、无 BOM，6 MB 左右。这里只在字节层替换那两行，
// 其余字节原样拷贝，不整文件 decode/encode（整文件往返遇到落单代理项会悄悄改坏数据）。
// 名字长度不同（fr 2 字符 / zhCN 4 字符），所以换完文件长度会变，但这张表没有偏移表，
// 变长是安全的。
//
// 副作用（已知并接受）：对调之后 `zhCN`（简体中文）那一项反而显示法国国旗。
// 简体中文这一项没有对应的数据目录，选了只会回落到英文，不影响使用。
using System.Text;

public static class FlagIcon {
    /// <summary>图集索引文件在 ggpk / Bundles2 里的路径（大小写不敏感）。</summary>
    public const string Path = "art/uiimages1.txt";

    /// <summary>
    /// 要互换的两行。定位串是「图标名」加上紧跟的 `" "`，所以 `FlagIcons/fr" "`
    /// 不会误命中 `FlagIcons/4K/fr" "`（中间隔着 4K/）。
    /// 4K 那一对在部分版本里不存在，缺了就只处理普通图集，不当作结构异常。
    /// </summary>
    static readonly (string Fr, string Zh)[] Pairs = {
        ("FlagIcons/fr\" \"",    "FlagIcons/zhCN\" \""),
        ("FlagIcons/4K/fr\" \"", "FlagIcons/4K/zhCN\" \""),
    };

    public enum State {
        Applied,    // fr 那一行现在叫 zhCN（取到五星红旗）
        Original,   // fr 那一行还叫 fr（法国国旗）
        Unknown,    // 结构对不上（多半是游戏更新过），不碰
    }

    // ---- 字节层工具 ----

    /// <summary>是不是一个换行符（U+000A 或 U+000D）——按 UTF-16LE 落在偶数偏移上判断。</summary>
    static bool IsEol(byte[] data, int k) => data[k + 1] == 0 && (data[k] == 0x0A || data[k] == 0x0D);

    /// <summary>找到包含 needle 的整行（返回字节区间，已去掉行首尾的 CR/LF）。</summary>
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

        while (end > start && data[end - 1] == 0 && data[end - 2] == 0x0D) end -= 2;
        return end > start;
    }

    /// <summary>把一行的图标名最后一段在 fr / zhCN 之间对调（行形如 `"路径/fr" "贴图" x y w h`）。</summary>
    static bool Toggle(string line, out string result) {
        result = line;
        if (line.Length < 2 || line[0] != '"') return false;
        var q = line.IndexOf('"', 1);
        if (q < 0) return false;
        var name = line[1..q];
        var slash = name.LastIndexOf('/');
        if (slash < 0 || slash + 1 >= name.Length) return false;
        var code = name[(slash + 1)..];
        var next = code == "fr" ? "zhCN" : code == "zhCN" ? "fr" : null;
        if (next is null) return false;
        result = "\"" + name[..(slash + 1)] + next + "\"" + line[(q + 1)..];
        return true;
    }

    /// <summary>某一对当前是「原样」还是「已对调」；两行缺一就返回 false。</summary>
    static bool StateOf(byte[] data, (string Fr, string Zh) p, out bool applied) {
        applied = false;
        if (!FindLine(data, p.Fr, out var a, out _)) return false;
        if (!FindLine(data, p.Zh, out var b, out _)) return false;
        applied = a > b;      // 对调之后，写着 fr 的那一行跑到 zhCN 原来的位置（后面）
        return true;
    }

    /// <summary>
    /// 对调某一对图标名。两行缺一返回 null。
    /// 按「每一行自己现在是 fr 还是 zhCN」翻转，所以不依赖两行的先后 ——
    /// 否则在已启用的文件上再跑一次会翻错方向，还原就不回原样了。
    /// </summary>
    static byte[]? SwapPair(byte[] data, (string Fr, string Zh) p) {
        if (!FindLine(data, p.Fr, out var aS, out var aE)) return null;
        if (!FindLine(data, p.Zh, out var bS, out var bE)) return null;
        if (aS > bS) { (aS, aE, bS, bE) = (bS, bE, aS, aE); }

        var lineA = Encoding.Unicode.GetString(data, aS, aE - aS);
        var lineB = Encoding.Unicode.GetString(data, bS, bE - bS);
        if (!Toggle(lineA, out var newA)) return null;
        if (!Toggle(lineB, out var newB)) return null;

        var aBytes = Encoding.Unicode.GetBytes(newA);
        var bBytes = Encoding.Unicode.GetBytes(newB);
        var res = new byte[aS + aBytes.Length + (bS - aE) + bBytes.Length + (data.Length - bE)];
        data.AsSpan(0, aS).CopyTo(res);
        aBytes.CopyTo(res, aS);
        data.AsSpan(aE, bS - aE).CopyTo(res.AsSpan(aS + aBytes.Length));
        bBytes.CopyTo(res, aS + aBytes.Length + (bS - aE));
        data.AsSpan(bE).CopyTo(res.AsSpan(aS + aBytes.Length + (bS - aE) + bBytes.Length));
        return res;
    }

    // ---- 对外 ----

    /// <summary>当前是哪一态。以普通图集那一对为准（4K 是附加项）。</summary>
    public static State Detect(byte[] data) {
        if (!StateOf(data, Pairs[0], out var applied)) return State.Unknown;
        return applied ? State.Applied : State.Original;
    }

    public static string Describe(State s) => s switch {
        State.Applied => "国旗已换成五星红旗（繁體中文那一项）",
        State.Original => "国旗仍是法国国旗（繁體中文那一项）",
        _ => "无法识别（uiimages1.txt 里找不到 FlagIcons/fr 与 FlagIcons/zhCN 两行）",
    };

    /// <summary>
    /// 把当前内容推到目标状态。
    /// 返回 (是否需要写, 新内容, 给人看的一句话)；不需要写时新内容为 null。
    /// </summary>
    public static bool TryBuild(byte[] current, bool wantChinese, out byte[]? next, out string message) {
        next = null;
        var st = Detect(current);
        if (st == State.Unknown) {
            message = "跳过：uiimages1.txt 里找不到国旗那两行（多半是游戏更新过），没有改动它";
            return false;
        }
        if ((st == State.Applied) == wantChinese) {
            message = "已是目标状态：" + Describe(st);
            return false;
        }

        var buf = current;
        var touched = 0;
        foreach (var p in Pairs) {
            if (!StateOf(buf, p, out var applied)) continue;      // 这一对不存在，跳过
            if (applied == wantChinese) continue;                 // 已经是目标态
            var step = SwapPair(buf, p);
            if (step is null) {
                message = "跳过：对调国旗那两行失败，没有改动它";
                return false;
            }
            buf = step;
            touched++;
        }
        if (touched == 0) {
            message = "已是目标状态：" + Describe(st);
            return false;
        }
        next = buf;
        message = wantChinese ? "国旗换成五星红旗（繁體中文那一项）" : "国旗还原成法国国旗";
        return true;
    }
}
