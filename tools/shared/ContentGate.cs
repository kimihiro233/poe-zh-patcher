// 内容闸（C# 侧）：与 tools/content_gate.py 同构，共用 fixtures/gate/cases.json、
// tools/pack/markup-whitelist.json 与 tools/pack/gate-exceptions.json。
//
// 两份实现必须逐条一致 —— 只在一侧加规则，等于给另一侧开了个后门。
// --gate-test 读同一份样本，任何一条结论不同就报失败。
//
// 五组规则：
//   G1 字符集    零宽 / RTL 覆写 / 控制字符 / 孤立代理项
//   G2 标记      标签白名单 + 语法；`{{` `}}` 配对；文本层不许有落单花括号
//   G3 结构      占位符守恒；行数与基线一致
//   G4 体量      单行 ≤ 2000；整文件 ≤ 基线 ×1.25；总量 ≤ 基线 ×1.2
//   G5 可推导    文本层等于目标文本；标签不超出「原文 ∪ 目标 ∪ 底稿」
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PoeZhPatcher;

internal static class ContentGate
{
    public const int MaxLine = 2000;
    public const double FileRatio = 1.25;
    public const double TotalRatio = 1.20;

    // 与 markup.py 的 TAG 一致
    private static readonly Regex Tag = new(@"<[^<>]{1,40}>", RegexOptions.Compiled);
    private static readonly Regex NameTag = new(@"^<([a-z][a-z0-9_]*)>$", RegexOptions.Compiled);
    private static readonly Regex Rgb = new(@"^<rgb\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})\s*\)>$", RegexOptions.Compiled);
    private static readonly Regex Placeholder = new(@"\{[^{}]*\}", RegexOptions.Compiled);

    // 与 check_placeholders.py 的 TOKEN_RE 必须一字不差
    private static readonly Regex TokenRe = new(@"\{\s*[-+]?\d[^}]*\}|#|%|\\n", RegexOptions.Compiled);

    public readonly record struct Finding(string Rule, string Message);

    public enum Mode { Off, Warn, Enforce }

    public sealed class Config
    {
        public HashSet<string> Named = new(StringComparer.Ordinal);
        public bool AllowRgb = true;
        // G3 白名单：占位符集合故意不同的行，按完整文本匹配
        public HashSet<(string file, string oldText, string newText)> Exceptions = new();

        public static Config Load(string root)
        {
            var cfg = new Config();
            var wlJson = ReadJson(root, "tools/pack/markup-whitelist.json", "markup-whitelist.json");
            if (wlJson is not null) {
                using var doc = JsonDocument.Parse(wlJson);
                foreach (var e in doc.RootElement.GetProperty("named").EnumerateArray())
                    cfg.Named.Add(e.GetString() ?? "");
                if (doc.RootElement.TryGetProperty("allow_rgb", out var ar))
                    cfg.AllowRgb = ar.GetBoolean();
            }
            var exJson = ReadJson(root, "tools/pack/gate-exceptions.json", "gate-exceptions.json");
            if (exJson is not null) {
                using var doc = JsonDocument.Parse(exJson);
                foreach (var c in doc.RootElement.GetProperty("case").EnumerateArray())
                    cfg.Exceptions.Add((Str(c, "file"), Str(c, "old"), Str(c, "new")));
            }
            return cfg;
        }

        // 先读磁盘（开发时改完就生效），读不到再退回内嵌副本（玩家机器上只有 exe）。
        private static string? ReadJson(string root, string rel, string resource)
        {
            var p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(p)) return File.ReadAllText(p, Encoding.UTF8);
            using var s = typeof(Config).Assembly.GetManifestResourceStream(resource);
            if (s is null) return null;
            using var r = new StreamReader(s, Encoding.UTF8);
            return r.ReadToEnd();
        }

        private static string Str(JsonElement e, string k) =>
            e.TryGetProperty(k, out var v) ? v.GetString() ?? "" : "";
    }

    // ---------------------------------------------------------------- 五个规则

    public static List<Finding> G1(string s)
    {
        var outv = new List<Finding>();
        for (var i = 0; i < s.Length && outv.Count < 3; i++) {
            var c = s[i];
            var cp = (int)c;
            string? why = null;
            if (cp <= 0x08 || (cp >= 0x0B && cp <= 0x1F)) why = "控制字符";
            else if (cp >= 0x7F && cp <= 0x9F) why = "C1 控制字符";
            else if (cp >= 0x200B && cp <= 0x200F) why = "零宽字符";
            else if (cp >= 0x202A && cp <= 0x202E) why = "双向覆写（RTL）";
            else if (cp >= 0x2066 && cp <= 0x2069) why = "双向隔离（RTL）";
            else if (cp == 0xFEFF) why = "BOM（文本中间不合法）";
            else if (char.IsSurrogate(c)) {
                // 成对代理项是正常的非 BMP 字符；落单的才算坏
                var paired = char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]);
                if (!paired && !(char.IsLowSurrogate(c) && i > 0 && char.IsHighSurrogate(s[i - 1])))
                    why = "孤立代理项";
                else if (paired) i++;      // 低代理项不重复检查
            }
            if (why is not null)
                outv.Add(new("G1", $"第 {i + 1} 字符 U+{cp:X4} 是{why}"));
        }
        return outv;
    }

    public static List<Finding> G2(string s, Config cfg)
    {
        var outv = new List<Finding>();
        foreach (Match m in Tag.Matches(s)) {
            var t = m.Value;
            var rgb = Rgb.Match(t);
            if (rgb.Success) {
                if (!cfg.AllowRgb) { outv.Add(new("G2", $"不允许颜色标签：{t}")); continue; }
                for (var g = 1; g <= 3; g++)
                    if (int.Parse(rgb.Groups[g].Value) > 255) {
                        outv.Add(new("G2", $"颜色分量越界：{t}"));
                        break;
                    }
                continue;
            }
            var nm = NameTag.Match(t);
            if (!nm.Success) outv.Add(new("G2", $"标签语法不认识：{t}"));
            else if (!cfg.Named.Contains(nm.Groups[1].Value)) outv.Add(new("G2", $"标签不在白名单：{t}"));
        }
        if (s.Count(ch => ch == '<') != Tag.Matches(s).Count)
            outv.Add(new("G2", "存在不配对或超长的 `<`"));
        var seq = Markup.MarkupTokens(s);
        var a = seq.Count(x => x == "{{");
        var b = seq.Count(x => x == "}}");
        if (a != b) outv.Add(new("G2", $"`{{{{` 与 `}}}}` 不配对（{a} / {b}）"));
        else {
            var rest = Placeholder.Replace(Markup.Core(s), "");
            if (rest.Contains('{') || rest.Contains('}'))
                outv.Add(new("G2", $"文本层里有落单的花括号：{Clip(rest.Trim(), 40)}"));
        }
        return outv;
    }

    public static List<Finding> G3(string s, string? oldText, string? want, Config cfg,
                                   string? file = null)
    {
        if (file is not null && oldText is not null
            && (cfg.Exceptions.Contains((file, oldText, s))
                || cfg.Exceptions.Contains((file, oldText, s.Split(@"\n")[0]))))
            return new List<Finding>();      // 已判过的编号错位修正，见 gate-exceptions.json

        var mode = want is not null ? "equal" : (oldText is not null ? "subset" : null);
        if (mode is null) return new List<Finding>();
        var refText = mode == "equal" ? want! : oldText!;
        var wantT = Tokens(refText);
        var got = Tokens(s);
        var oldT = (mode == "equal" && oldText is not null) ? Tokens(oldText) : new List<string>();
        if (mode == "equal" && SameMulti(wantT, got) && oldT.All(got.Contains)) return new List<Finding>();

        var miss = wantT.Where(x => !got.Contains(x)).Concat(oldT.Where(x => !got.Contains(x))).ToList();
        var extra = mode == "subset" ? new List<string>() : got.Where(x => !wantT.Contains(x)).ToList();
        if (miss.Count == 0 && extra.Count == 0) return new List<Finding>();
        var bits = new List<string>();
        if (miss.Count > 0) bits.Add("缺 " + string.Join(" ", miss.Distinct().OrderBy(x => x, StringComparer.Ordinal)));
        if (extra.Count > 0) bits.Add("多 " + string.Join(" ", extra.Distinct().OrderBy(x => x, StringComparer.Ordinal)));
        return new List<Finding> {
            new("G3", "占位符与" + (mode == "equal" ? "目标文本" : "原文") + "不一致：" + string.Join("，", bits))
        };
    }

    public static List<Finding> G4(string s)
    {
        if (s.Length > MaxLine)
            return new List<Finding> { new("G4", $"单行 {s.Length} 字符，超过 {MaxLine}") };
        return new List<Finding>();
    }

    public static List<Finding> G5(string s, string? oldText, string? want, string? shell)
    {
        if (want is null) return new List<Finding>();
        var outv = new List<Finding>();
        var core = Markup.Core(s);
        if (core != Markup.Core(want)) {
            outv.Add(new("G5", $"文本层不是目标文本：{Clip(core, 40)}"));
            return outv;
        }
        var allowed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var src in new[] { oldText, want, shell })
            if (src is not null) foreach (var t in Markup.MarkupTokens(src)) allowed.Add(t);
        var extra = Markup.MarkupTokens(s).Where(t => !allowed.Contains(t)).Take(3).ToList();
        if (extra.Count > 0) outv.Add(new("G5", "出现推导不出来的标记：" + string.Join(" ", extra)));
        return outv;
    }

    public static List<Finding> CheckLine(string s, string? oldText = null, string? want = null,
                                          string? shell = null, Config? cfg = null,
                                          string? file = null)
    {
        cfg ??= new Config();
        var outv = new List<Finding>();
        outv.AddRange(G1(s));
        outv.AddRange(G2(s, cfg));
        outv.AddRange(G3(s, oldText, want, cfg, file));
        outv.AddRange(G4(s));
        outv.AddRange(G5(s, oldText, want, shell));
        return outv;
    }

    public static List<Finding> CheckFile(IReadOnlyList<string> lines, IReadOnlyList<string>? baseLines,
                                          long? baseBytes, Config? cfg = null)
    {
        cfg ??= new Config();
        var outv = new List<Finding>();
        if (baseLines is not null && lines.Count != baseLines.Count)
            outv.Add(new("G3", $"行数 {lines.Count}，基线 {baseLines.Count}"));
        var bytes = Encoding.Unicode.GetByteCount(string.Join("\r\n", lines));
        if (baseBytes is > 0 && bytes > baseBytes * FileRatio)
            outv.Add(new("G4", $"整文件 {bytes} B，超过基线的 {FileRatio} 倍（{baseBytes} B）"));
        for (var i = 0; i < lines.Count; i++) {
            var oldText = baseLines is not null && i < baseLines.Count ? baseLines[i] : null;
            foreach (var f in CheckLine(lines[i], oldText, null, null, cfg))
                outv.Add(new(f.Rule, $"第 {i + 1} 行 {f.Message}"));
        }
        return outv;
    }

    // ---------------------------------------------------------------- 工具

    private static List<string> Tokens(string s)
    {
        var list = new List<string>();
        foreach (Match m in TokenRe.Matches(s)) list.Add(m.Value);
        list.Sort(StringComparer.Ordinal);
        return list;
    }

    private static bool SameMulti(List<string> a, List<string> b) => a.SequenceEqual(b);

    private static string Clip(string s, int n) => s.Length <= n ? s : s[..n] + "…";

    // ---------------------------------------------------------------- 自检

    public static int SelfTest(string root)
    {
        var cfg = Config.Load(root);
        var path = Path.Combine(root, "fixtures", "gate", "cases.json");
        if (!File.Exists(path)) {
            Console.WriteLine($"找不到样本：{path}");
            return 1;
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
        var bad = 0;
        var total = 0;
        foreach (var c in doc.RootElement.GetProperty("case").EnumerateArray()) {
            total++;
            var name = c.GetProperty("name").GetString() ?? "?";
            var expect = c.GetProperty("expect").GetString() ?? "accept";
            var wantRule = c.TryGetProperty("rule", out var rr) ? rr.GetString() ?? "" : "";
            List<Finding> got;
            if (c.TryGetProperty("lines", out var ls)) {
                var lines = ls.EnumerateArray().Select(x => x.GetString() ?? "").ToList();
                List<string>? baseLines = null;
                if (c.TryGetProperty("base_lines", out var bl))
                    baseLines = bl.EnumerateArray().Select(x => x.GetString() ?? "").ToList();
                long? baseBytes = c.TryGetProperty("base_bytes", out var bb) ? bb.GetInt64() : null;
                got = CheckFile(lines, baseLines, baseBytes, cfg);
            } else {
                got = CheckLine(c.GetProperty("actual").GetString() ?? "",
                                Opt(c, "old"), Opt(c, "want"), Opt(c, "shell"), cfg);
            }
            var rules = got.Select(f => f.Rule).ToList();
            var ok = expect == "accept" ? got.Count == 0 : rules.Contains(wantRule);
            if (!ok) {
                bad++;
                Console.WriteLine($"  ✗ {name}   期望 {expect}/{wantRule}，实际 {(rules.Count == 0 ? "通过" : string.Join(",", rules))}");
                foreach (var f in got) Console.WriteLine($"      {f.Rule} {f.Message}");
            } else {
                Console.WriteLine($"  ✓ {name}");
            }
        }
        Console.WriteLine(bad == 0 ? $"内容闸自检通过（{total}/{total}）" : $"内容闸自检失败 {bad} 项（共 {total}）");
        return bad == 0 ? 0 : 1;

        static string? Opt(JsonElement e, string k) =>
            e.TryGetProperty(k, out var v) ? v.GetString() : null;
    }
}
