// 启用繁中语系 —— 等价于 PoeChinese3 做的那一步。
//
// 为什么需要：客户端能不能「选到繁中」，不看我们往 metadata/statdescriptions 里写了多少中文，
// 而看 data/languages.dat 这张语言表。游戏里的语言列表就是这张表的 11 行。
//
// 关键在客户端怎么定位数据目录：它按这一行的**语言名**去找 `data/<语言名>/` 下的文件
// （采集包里那条路径 `data/traditional chinese/uitalktext.datc64` 就是证据），
// 而语言代码（`fr` / `zh-TW`）只影响配置值 `language=fr` 和国旗图标。
//
// 所以 PoeChinese3 换的不是代码，是**名字**：把 French 那一行的「显示名 + 本地名」
// 整对换成 Traditional Chinese 那一行的。换完之后语言列表第二项写着「繁體中文」，
// 选它就去读 `data/traditional chinese/` —— 官方繁中加上我们写进去的译文；
// 配置里仍然记着 `language=fr`，用户配置文件一个字都不用改。
//
// 文件格式（实测 3.25.1d / 3.29.3b，688–696 B / 11 行）：变长、UTF-16LE、字符串内联且以
// 0x0000 结尾，没有字符串表也没有偏移量。因此把两段「名字 + 本地名 + 各自结束符」整体对调
// 是安全的：只搬字节，不修任何偏移，整份文件长度不变。
//
// 早先的实现是错的：它交换的是 `fr` 与 `zh-TW` 两个**代码**，而且判定条件
// （`zh-TW` 出现在 `French` 之前才算未启用）在真实文件里永远不成立，
// 于是 Detect 一直返回 Unknown、TryBuild 直接「跳过」，语系从来没被写进去过。
using System.Text;

public static class LanguageSwitch {
    /// <summary>客户端语言表在 ggpk / Bundles2 里的路径（大小写不敏感）。</summary>
    public const string Path = "data/languages.dat";

    // 两行的「显示名 + 本地名」。成对搬移，不碰语言代码。
    const string UsName = "French", UsNative = "Français";
    const string CnName = "Traditional Chinese", CnNative = "繁體中文";

    public enum State {
        Applied,    // 繁中那对排在法语那对前面（法语槽位已经是繁中）
        Original,   // 法语那对在前，未启用
        Unknown,    // 结构对不上（多半是游戏更新过），不碰
    }

    /// <summary>找一段「完整字符串」的起点：前面是上一个字符串的结束符，后面紧跟自己的结束符。</summary>
    static int FindString(byte[] d, string s, int from) {
        var nb = Encoding.Unicode.GetBytes(s);
        var span = d.AsSpan();
        var i = from;
        while (i >= 0 && i + nb.Length + 2 <= d.Length) {
            var k = span[i..].IndexOf(nb);
            if (k < 0) return -1;
            i += k;
            var startOk = (i & 1) == 0 && (i == 0 || (d[i - 2] == 0 && d[i - 1] == 0));
            var endOk = d[i + nb.Length] == 0 && d[i + nb.Length + 1] == 0;
            if (startOk && endOk) return i;
            i += 2;
        }
        return -1;
    }

    /// <summary>校验 name 后面紧跟 native（中间隔一个结束符，native 自己也有结束符），返回这一段之后的偏移。</summary>
    static bool PairAfter(byte[] d, int nameStart, string name, string native, out int end) {
        end = -1;
        var nb = name.Length * 2;
        var tb = Encoding.Unicode.GetBytes(native);
        var p = nameStart + nb + 2;
        if (p + tb.Length + 2 > d.Length) return false;
        if (d[p - 2] != 0 || d[p - 1] != 0) return false;
        if (!d.AsSpan(p, tb.Length).SequenceEqual(tb)) return false;
        if (d[p + tb.Length] != 0 || d[p + tb.Length + 1] != 0) return false;
        end = p + tb.Length + 2;
        return true;
    }

    /// <summary>定位两对「名字 + 本地名」各自的字节区间。任何一边对不上就整体判 Unknown。</summary>
    static bool Locate(byte[] d, out int usStart, out int usEnd, out int cnStart, out int cnEnd) {
        usStart = usEnd = cnStart = cnEnd = -1;
        var a = FindString(d, UsName, 0);
        if (a < 0 || !PairAfter(d, a, UsName, UsNative, out usEnd)) return false;
        var b = FindString(d, CnName, 0);
        if (b < 0 || !PairAfter(d, b, CnName, CnNative, out cnEnd)) return false;
        if (a == b) return false;
        usStart = a;
        cnStart = b;
        return true;
    }

    public static State Detect(byte[] data) {
        if (!Locate(data, out var us, out _, out var cn, out _)) return State.Unknown;
        // 行序不变，只有内容对调 —— 谁在前就代表哪一态
        return us < cn ? State.Original : State.Applied;
    }

    /// <summary>两对名字对调。长度守恒（同样的字节只换位置）；结构对不上返回 null。</summary>
    public static byte[]? Swap(byte[] data) {
        if (!Locate(data, out var usStart, out var usEnd, out var cnStart, out var cnEnd)) return null;
        var firstStart = Math.Min(usStart, cnStart);
        var firstEnd = usStart < cnStart ? usEnd : cnEnd;
        var secondStart = Math.Max(usStart, cnStart);
        var secondEnd = usStart < cnStart ? cnEnd : usEnd;
        if (firstEnd > secondStart) return null;      // 区间不该重叠，重叠说明解析错了

        var res = new byte[data.Length];
        var n = 0;
        void Put(ReadOnlySpan<byte> src) { src.CopyTo(res.AsSpan(n)); n += src.Length; }

        Put(data.AsSpan(0, firstStart));
        Put(data.AsSpan(secondStart, secondEnd - secondStart));      // 后一段挪到前面
        Put(data.AsSpan(firstEnd, secondStart - firstEnd));
        Put(data.AsSpan(firstStart, firstEnd - firstStart));         // 前一段挪到后面
        Put(data.AsSpan(secondEnd));
        if (n != data.Length) return null;                           // 长度不守恒就宁可不写
        return res;
    }

    public static string Describe(State s) => s switch {
        State.Applied => "已启用繁中语系（第二项 = 繁體中文，读 data/traditional chinese）",
        State.Original => "未启用繁中语系（第二项仍是 Français）",
        _ => "无法识别（languages.dat 结构对不上）",
    };

    /// <summary>
    /// 把当前内容推到目标状态。
    /// 返回 (是否需要写, 新内容, 给人看的一句话)；不需要写时新内容为 null。
    /// </summary>
    public static bool TryBuild(byte[] current, bool wantChinese, out byte[]? next, out string message) {
        next = null;
        var st = Detect(current);
        if (st == State.Unknown) {
            message = "跳过：languages.dat 结构与预期不符（多半是游戏更新过），没有改动它";
            return false;
        }
        if ((st == State.Applied) == wantChinese) {
            message = "已是目标状态：" + Describe(st);
            return false;
        }
        var swapped = Swap(current);
        if (swapped is null) {
            message = "跳过：定位不到 French / Traditional Chinese 两行，没有改动它";
            return false;
        }
        next = swapped;
        message = wantChinese ? "启用繁中语系（语言列表第二项将变成繁體中文）" : "还原语系（第二项恢复为法语）";
        return true;
    }
}
