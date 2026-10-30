// 采集包脱敏：把「用户机器上的身份信息」从诊断输出里摘掉再落盘。
//
// 采集包是要发回给作者的 —— 一旦发出去就收不回来。里面真正危险的不是游戏文件，
// 是客户端日志：它带登录账号、SteamID、以及 `C:\Users\<用户名>\…` 这种把真名写在脸上的路径。
// 这些东西对排查毫无用处，对收包的人却是完整的身份线索。
//
// 所以规则只有一条：**凡是本地身份信息，出包之前必须消失**；凡是有用的事实（版本号、
// 文件状态、逐行对照），一个字节都不能少。判断「有没有漏」不靠人看，靠 Audit() 在落盘后
// 复扫一遍 —— 扫到就拒绝打包，宁可让用户把文件夹拆开单发，也不生成一个带真名的 zip。
//
// 顺序不能换：先整行剔除，再替换路径。反过来的话，`C:\Users\Administrator` 会先被
// 换成 `C:\Users\<user>`，但同一行若还带账号，整行本来是该消失的 —— 那就白留了个壳。
using System.Text;
using System.Text.RegularExpressions;

internal static class Redact {
    public const string Marker = "[REDACTED]";
    public const string UserTag = "<user>";
    public const string IdTag = "<steamid>";
    public const string PathTag = "<path>";

    // 规则 1：整行剔除的触发词。刻意**不要求词尾边界** —— 日志里写的是
    // `logged in as AccountName#1234`，`\baccount\b` 在 AccountName 上不成立，会漏。
    private static readonly Regex SecretWord = new(
        @"(?i)\b(account|session|token|login|e?mail)", RegexOptions.Compiled);

    // 行首时间戳：PoE 日志形如 `2026/10/28 12:00:01 1234567 abc [INFO Client 4312] …`
    // 只留这一截，后面整段丢掉 —— 保留时间是为了让剩下几行还能排序。
    private static readonly Regex StampPrefix = new(
        @"^\s*(\d{4}[/-]\d{2}[/-]\d{2}[ T]\d{2}:\d{2}:\d{2}(?:[.,]\d{1,6})?)", RegexOptions.Compiled);

    // 规则 2：用户目录。只吃掉用户名那一段，后面的 AppData\Roaming\… 留着，
    // 那份路径本身有诊断价值（能看出是漫游目录还是安装目录）。
    private static readonly Regex UserPath = new(
        @"([A-Za-z]):\\Users\\[^\\\s""]+", RegexOptions.Compiled);

    // 规则 3：其它盘符的绝对路径。只在客户端日志上生效 —— report.txt 里的容器路径
    // 走 PathHint()，不能落全路径。`(?!Users)` 让已经脱敏的 `C:\Users\<user>` 不再被二次处理。
    private static readonly Regex OtherPath = new(
        @"([A-Za-z]):\\(?!Users)[^""\s]*", RegexOptions.Compiled);

    // 规则 4：SteamID64（7656 开头共 17 位）。
    private static readonly Regex SteamId = new(@"\b7656\d{13}\b", RegexOptions.Compiled);

    // ---------------------------------------------------------------- 规则本体

    /// <summary>规则 1 + 4：一行之内有身份信息就整行换掉，只留行首时间戳。</summary>
    public static string Line(string line) {
        if (line.Length == 0) return line;
        var s = SteamId.Replace(line, IdTag);
        if (!SecretWord.IsMatch(s)) return s;
        var m = StampPrefix.Match(line);
        return (m.Success ? m.Groups[1].Value + " " : "") + Marker;
    }

    /// <summary>规则 2 + 3：路径替换。log=true 时连其它盘符也吃掉。</summary>
    public static string Paths(string s, bool log) {
        s = UserPath.Replace(s, "$1:\\Users\\" + UserTag);
        if (log) s = OtherPath.Replace(s, "$1:\\" + PathTag);
        return s;
    }

    /// <summary>整段文本：逐行「先剔除、再换路径」，换行符原样保留。</summary>
    public static string Text(string text, bool log) {
        if (text.Length == 0) return text;
        var parts = text.Split('\n');
        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < parts.Length; i++) {
            var raw = parts[i];
            var cr = raw.EndsWith('\r');
            var body = cr ? raw[..^1] : raw;
            sb.Append(Paths(Line(body), log));
            if (cr) sb.Append('\r');
            if (i < parts.Length - 1) sb.Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>一行日志。</summary>
    public static string LogLine(string line) => Paths(Line(line), log: true);

    /// <summary>
    /// 容器路径的替代品：给出「哪个盘、几层、文件叫什么」，够作者判断安装形态，
    /// 又不含用户名、不含安装目录名。
    /// </summary>
    public static string PathHint(string absPath) {
        if (string.IsNullOrWhiteSpace(absPath)) return "(无)";
        try {
            var full = System.IO.Path.GetFullPath(absPath);
            var root = System.IO.Path.GetPathRoot(full) ?? "";
            var drive = root.Length >= 2 ? root[..2] : root;
            var rest = full.Length > root.Length ? full[root.Length..] : "";
            var depth = rest.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries).Length;
            var name = System.IO.Path.GetFileName(full);
            return $"{drive} · {depth} 层 · …\\{name}";
        } catch {
            return "(路径已隐藏)";
        }
    }

    // ---------------------------------------------------------------- 落盘后自检

    private static readonly Regex LeakUser = new(
        @"[A-Za-z]:\\Users\\(?!<user>)", RegexOptions.Compiled);
    private static readonly Regex LeakId = new(@"\b7656\d{13}\b", RegexOptions.Compiled);

    /// <summary>扫一遍输出目录里的文本文件，返回所有仍然像身份信息的位置。</summary>
    public static List<string> Audit(string dir) {
        var hits = new List<string>();
        if (!Directory.Exists(dir)) return hits;
        foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories)) {
            var ext = System.IO.Path.GetExtension(f).ToLowerInvariant();
            if (ext is not (".txt" or ".json" or ".md" or ".csv" or ".log")) continue;
            string[] lines;
            try { lines = File.ReadAllLines(f); } catch { continue; }
            for (var i = 0; i < lines.Length; i++) {
                var l = lines[i];
                if (LeakUser.IsMatch(l)) hits.Add($"{System.IO.Path.GetFileName(f)}:{i + 1}  路径仍带用户名");
                else if (LeakId.IsMatch(l)) hits.Add($"{System.IO.Path.GetFileName(f)}:{i + 1}  SteamID 未替换");
            }
        }
        return hits;
    }

    // ---------------------------------------------------------------- 自检

    /// <summary>
    /// 用真实日志行把规则钉住。断言写的是「规则的输出」，不是「没崩」——
    /// 以后有人调正则，只要改变了任何一条的输出，这里立刻红。
    /// </summary>
    public static int SelfTest() {
        var bad = 0;
        var total = 0;
        void Check(string what, string got, string want) {
            total++;
            var ok = got == want;
            Console.WriteLine($"  {(ok ? "✓" : "✗")} {what}");
            if (!ok) {
                Console.WriteLine($"      想要：{Show(want)}");
                Console.WriteLine($"      实际：{Show(got)}");
                bad++;
            }
        }
        static string Show(string s) => s.Replace("\r", "\\r").Replace("\n", "\\n");

        const string Ts = "2026/10/28 12:00:01";
        Console.WriteLine("采集包脱敏自检（整行剔除 → 路径替换）");
        Console.WriteLine();

        // 1 账号：整行消失，只留时间戳
        Check("账号行整行剔除，时间戳保留",
              LogLine($"{Ts} 1234567 abc [INFO Client 4312] User 'Administrator' logged in as AccountName#1234"),
              $"{Ts} {Marker}");

        // 2 触发词不要求词尾边界（AccountName 这种粘连写法也必须命中）
        Check("AccountName 这种粘连写法照样命中",
              LogLine($"{Ts} 1234567 abc [INFO Client 4312] AccountName#1234"),
              $"{Ts} {Marker}");

        // 3 session / token
        Check("session 行整行剔除",
              LogLine($"{Ts} 1234567 abc [ERROR Client 4312] session expired, reconnecting"),
              $"{Ts} {Marker}");

        // 4 SteamID：不是整行剔除，只换掉那串数字
        Check("SteamID64 替换成占位符（其余内容保留）",
              LogLine($"{Ts} 1234567 abc [DEBUG Client 4312] SteamID 76561198012345678 linked"),
              $"{Ts} 1234567 abc [DEBUG Client 4312] SteamID {IdTag} linked");

        // 5 用户目录：只吃掉用户名
        Check("用户目录只替换用户名，后面的路径留着",
              LogLine(@$"{Ts} 1234567 abc [INFO Client 4312] Cache at C:\Users\Administrator\AppData\Roaming\Path of Exile"),
              $"{Ts} 1234567 abc [INFO Client 4312] Cache at C:\\Users\\{UserTag}\\AppData\\Roaming\\Path of Exile");

        // 6 其它盘符（只在 log 生效）
        Check("日志里的安装路径换成盘符占位",
              LogLine(@$"{Ts} 1234567 abc [INFO Client 4312] Install D:\Games\PathOfExile\Content.ggpk"),
              $"{Ts} 1234567 abc [INFO Client 4312] Install D:\\{PathTag}");

        // 7 无时间戳的行也要整行剔除
        Check("没有时间戳的敏感行整行剔除",
              Line("token=abcd1234"),
              Marker);

        // 8 正常行一个字节都不能改
        const string plain = Ts + " 1234567 abc [INFO Client 4312] Connect time: 3.21s";
        Check("不含身份信息的行原样保留", LogLine(plain), plain);

        // 9 容器路径只给提示，不给全路径
        var hint = PathHint(@"C:\Users\Administrator\Documents\Path of Exile\Content.ggpk");
        Check("容器路径提示不含用户名",
              hint.IndexOf("Administrator", StringComparison.OrdinalIgnoreCase) >= 0 ? "泄漏" : "干净",
              "干净");

        // 10 落盘后自检：真漏了要能抓出来，脱敏过的不许误报
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "poezh-redact-" + DateTime.Now.ToString("HHmmssfff"));
        try {
            Directory.CreateDirectory(dir);
            File.WriteAllText(System.IO.Path.Combine(dir, "leak.txt"),
                @"2026/10/28 12:00:01 raw C:\Users\Administrator\AppData\Local" + Environment.NewLine, new UTF8Encoding(false));
            var hit = Audit(dir);
            Check("未脱敏的文本能被抓出来", hit.Count == 1 ? "命中" : $"漏检 {hit.Count}", "命中");

            File.WriteAllText(System.IO.Path.Combine(dir, "clean.txt"),
                @"2026/10/28 12:00:01 clean C:\Users\<user>\AppData\Local" + Environment.NewLine, new UTF8Encoding(false));
            var clean = Audit(dir).Where(x => x.StartsWith("clean", StringComparison.Ordinal)).ToList();
            Check("已脱敏的 <user> 不被误报", clean.Count == 0 ? "干净" : string.Join("；", clean), "干净");
        } finally {
            try { Directory.Delete(dir, true); } catch { }
        }

        Console.WriteLine();
        Console.WriteLine(bad == 0 ? $"脱敏自检通过（{total}/{total}）" : $"脱敏自检失败 {bad} 项（共 {total}）");
        return bad == 0 ? 0 : 1;
    }
}
