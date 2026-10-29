// 启用繁中语系 —— 等价于 PoeChinese3 做的那一步。
//
// 为什么需要：客户端能不能「选到繁中」，不看我们往 metadata/statdescriptions 里写了多少中文，
// 而看 data/languages.dat 里那两行的语言代码：
//
//     French 行              原本 code = "fr"
//     Traditional Chinese 行  原本 code = "zh-TW"
//
// 把这两个 code 对调之后，客户端语言列表里「第二个（法国国旗）」那一项就会去加载繁中数据，
// 也就是官方繁中 + 我们的译文。官方客户端里繁中这一项本来就是存在的，只是入口指在法语上，
// 所以 PoeChinese3 才敢说「只启用语系、其余文件均未修改」，游戏更新后也不用重打。
//
// 不这么做的话：打完补丁游戏里还是英文/法文，必须再跑一次 PoeChinese3 才看得见中文。
//
// 文件格式（实测 3.29.3b，696 B / 11 行）：变长、UTF-16LE、字符串内联且以 0x0000 结尾，
// 没有字符串表也没有偏移量。因此把两段「code + 结束符」互换是安全的：
// 整份文件长度不变，中间的字段只是整体平移，不存在要修的偏移。
using System.Text;

public static class LanguageSwitch {
    /// <summary>客户端语言表在 ggpk / Bundles2 里的路径（大小写不敏感）。</summary>
    public const string Path = "data/languages.dat";

    public enum State {
        Applied,    // 繁中行 code = "fr"，已启用
        Original,   // 繁中行 code = "zh-TW"，未启用
        Unknown,    // 结构对不上（多半是游戏更新过），不碰
    }

    /// <summary>构造「整串」匹配模式：前面两个 0 是上一个字符串的结束符，末尾两个 0 是自己的。</summary>
    static byte[] WholeString(string s) {
        var b = new byte[s.Length * 2 + 4];
        Encoding.Unicode.GetBytes(s, 0, s.Length, b, 2);
        return b;
    }

    /// <summary>返回字符串正文的起始位置（跳过前面那两个结束符 0），找不到返回 -1。</summary>
    static int Find(byte[] data, string s) {
        var i = data.AsSpan().IndexOf(WholeString(s));
        return i < 0 ? -1 : i + 2;
    }

    /// <summary>只有正文 + 自己的结束符，用于真正搬字节。</summary>
    static byte[] Body(string s) {
        var b = new byte[s.Length * 2 + 2];
        Encoding.Unicode.GetBytes(s, 0, s.Length, b, 0);
        return b;
    }

    public static State Detect(byte[] data) {
        var fr = Find(data, "fr");
        var zh = Find(data, "zh-TW");
        var fname = Find(data, "French");
        if (fr < 0 || zh < 0 || fname < 0) return State.Unknown;
        // 法语行自己的 code 紧跟在 "French" 后面；繁中行的 code 在它前面。
        if (zh < fname && fr > fname) return State.Original;
        if (fr < fname && zh > fname) return State.Applied;
        return State.Unknown;
    }

    /// <summary>对调两个 code。长度守恒；结构对不上返回 null（调用方应当原样不动）。</summary>
    public static byte[]? Swap(byte[] data) {
        var a = Find(data, "fr");
        var c = Find(data, "zh-TW");
        if (a < 0 || c < 0 || a == c) return null;

        var bodyFr = Body("fr");
        var bodyZh = Body("zh-TW");
        int first = Math.Min(a, c), second = Math.Max(a, c);
        var firstBody = a < c ? bodyFr : bodyZh;
        var secondBody = a < c ? bodyZh : bodyFr;

        var res = new byte[data.Length];
        var n = 0;
        void Put(ReadOnlySpan<byte> src) { src.CopyTo(res.AsSpan(n)); n += src.Length; }

        Put(data.AsSpan(0, first));
        Put(secondBody);                                          // 后面的串挪到前面
        Put(data.AsSpan(first + firstBody.Length, second - first - firstBody.Length));
        Put(firstBody);                                           // 前面的串挪到后面
        Put(data.AsSpan(second + secondBody.Length));
        if (n != data.Length) return null;                        // 长度必须守恒，否则宁可不写
        return res;
    }

    public static string Describe(State s) => s switch {
        State.Applied => "已启用繁中语系（法语槽位 = 繁中）",
        State.Original => "未启用繁中语系（法语槽位仍是法语）",
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
        var already = (st == State.Applied) == wantChinese;
        if (already) {
            message = "已是目标状态：" + Describe(st);
            return false;
        }
        var swapped = Swap(current);
        if (swapped is null) {
            message = "跳过：定位不到 French / Traditional Chinese 的语言代码，没有改动它";
            return false;
        }
        next = swapped;
        message = wantChinese ? "启用繁中语系（法国国旗那一项将加载繁中）" : "还原语系（法国国旗恢复为法语）";
        return true;
    }
}
