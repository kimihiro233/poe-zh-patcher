using System.Reflection;
using System.Text;
using LibBundledGGPK3;
using LibDat2;
using LibBundle3.Records;

Console.OutputEncoding = Encoding.UTF8;

static string DatName(string path) {
    var n = Path.GetFileName(path);
    if (n.EndsWith(".datcl64", StringComparison.OrdinalIgnoreCase)) return n[..^4];
    if (n.EndsWith(".datc64", StringComparison.OrdinalIgnoreCase)) return n[..^3];
    return n;
}
static bool IsDat(string path) {
    var n = path.ToLowerInvariant();
    return n.EndsWith(".dat") || n.EndsWith(".datc64") || n.EndsWith(".datcl64");
}

if (args.Length > 0 && args[0] == "reload") {
    try {
        Console.WriteLine("calling ReloadDefinitions() ...");
        DatContainer.ReloadDefinitions();
        Console.WriteLine("ReloadDefinitions OK");
    } catch (Exception e) { Console.WriteLine($"ReloadDefinitions FAILED: {e.GetType().Name}: {e.Message}"); }
    try {
        Console.WriteLine("calling DownloadSchemaMin() ...");
        DatContainer.DownloadSchemaMin();
        Console.WriteLine("DownloadSchemaMin OK");
    } catch (Exception e) { Console.WriteLine($"DownloadSchemaMin FAILED: {e.GetType().Name}: {e.Message}"); }
    return;
}

if (args.Length > 0 && args[0] == "raw") {
    var gpath = args[1];
    var odir = args[2];
    var filt = args[3];
    Directory.CreateDirectory(odir);
    using var g2 = new BundledGGPK(gpath, true);
    var hits = g2.Index.Files.Values.Select(f => f.Path)
        .Where(p => !string.IsNullOrEmpty(p) && filt.Split(',').Any(f => p.Contains(f.Trim(), StringComparison.OrdinalIgnoreCase)))
        .OrderBy(p => p, StringComparer.Ordinal).ToList();
    Console.WriteLine($"raw matches: {hits.Count}");
    foreach (var p in hits) {
        if (!g2.Index.TryGetFile(p, out var rec)) continue;
        var data = rec.Read().ToArray();
        var name = p.Replace('/', '_');
        File.WriteAllBytes(Path.Combine(odir, name), data);
        Console.WriteLine($"  {p} -> {name} ({data.Length:N0} B)");
    }
    return;
}

if (args.Length > 0 && args[0] == "cmp") {
    var gpath = args[1];
    var lang = args.Length > 2 ? args[2] : "traditional chinese";
    using var g3 = new BundledGGPK(gpath, true);
    var paths = g3.Index.Files.Values.Select(f => f.Path)
        .Where(p => p is not null && p.StartsWith("data/", StringComparison.Ordinal)
                    && p.IndexOf('/', 5) < 0 && IsDat(p))
        .OrderBy(p => p, StringComparer.Ordinal).ToList();
    Console.WriteLine($"base dat files: {paths.Count}");
    int same = 0, diff = 0, miss = 0;
    var rows = new List<(string name, int baseLen, int langLen, int cjk, int baseCjk)>();
    foreach (var p in paths) {
        var lp = "data/" + lang + "/" + p[5..];
        if (!g3.Index.TryGetFile(p, out var rb) || !g3.Index.TryGetFile(lp, out var rl)) { miss++; continue; }
        var b = rb.Read().ToArray();
        var l = rl.Read().ToArray();
        if (b.AsSpan().SequenceEqual(l)) { same++; continue; }
        diff++;
        rows.Add((p[5..], b.Length, l.Length, CountCjk(l), CountCjk(b)));
    }
    Console.WriteLine($"identical: {same}  different: {diff}  missing: {miss}");
    foreach (var r in rows.OrderByDescending(r => r.langLen - r.baseLen).Take(40))
        Console.WriteLine($"  {r.name,-52} base={r.baseLen,9:N0} lang={r.langLen,9:N0} cjk(lang)={r.cjk,6:N0} cjk(base)={r.baseCjk,5:N0}");
    var listPath = Path.Combine(Environment.CurrentDirectory, "diff_pairs.csv");
    var sb = new System.Text.StringBuilder();
    sb.AppendLine("path,baseBytes,langBytes,langCjk");
    foreach (var r in rows.OrderByDescending(r => r.cjk).ThenBy(r => r.name))
        sb.AppendLine($"{r.name},{r.baseLen},{r.langLen},{r.cjk}");
    File.WriteAllText(listPath, sb.ToString(), new System.Text.UTF8Encoding(false));
    Console.WriteLine($"list written: {listPath}");
    return;
}

if (args.Length > 0 && args[0] == "pairs") {
    var gpath = args[1];
    var outRoot = args[2];
    var listFile = args[3];
    var lang = args.Length > 4 ? args[4] : "traditional chinese";
    Directory.CreateDirectory(Path.Combine(outRoot, "en"));
    Directory.CreateDirectory(Path.Combine(outRoot, "zh"));
    using var g4 = new BundledGGPK(gpath, true);
    var lines = File.ReadAllLines(listFile).Skip(1).Select(l => l.Split(',')[0].Trim())
        .Where(l => l.Length > 0).ToList();
    int pok = 0, pfail = 0;
    foreach (var rel in lines) {
        var p = "data/" + rel;
        var lp = "data/" + lang + "/" + rel;
        foreach (var (src, dstDir) in new[] { (p, "en"), (lp, "zh") }) {
            try {
                if (!g4.Index.TryGetFile(src, out var rec)) { pfail++; Console.WriteLine($"  MISS {src}"); continue; }
                File.WriteAllBytes(Path.Combine(outRoot, dstDir, rel), rec.Read().ToArray());
                pok++;
            } catch (Exception e) { pfail++; Console.WriteLine($"  FAIL {src}: {e.Message}"); }
        }
    }
    Console.WriteLine($"pairs done: {pok} ok, {pfail} failed");
    return;
}

static int CountCjk(byte[] d) {
    int n = 0;
    for (int i = 0; i + 1 < d.Length; i += 2) {
        var v = (char)(d[i] | (d[i + 1] << 8));
        if (v >= 0x4E00 && v <= 0x9FFF) n++;
    }
    return n;
}

if (args.Length > 0 && args[0] == "fields") {
    var da = Assembly.Load("LibDat2");
    var t = da.GetType("LibDat2.DatContainer")!;
    foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
        Console.WriteLine($"{(f.IsStatic ? "static " : "")}{f.FieldType.Name} {f.Name}");
    return;
}

if (args.Length > 0 && args[0] == "ls") {
    var filt2 = args.Length > 1 ? args[1] : "";
    using var g5 = new BundledGGPK(args.Length > 2 ? args[2] : @"C:\Program Files (x86)\Grinding Gear Games\Path of Exile\Content.ggpk", true);
    var hit = g5.Index.Files.Values.Select(f => f.Path)
        .Where(p => p is not null && p.Contains(filt2, StringComparison.OrdinalIgnoreCase))
        .OrderBy(p => p, StringComparer.Ordinal).Take(60).ToList();
    Console.WriteLine($"matches: {hit.Count}");
    foreach (var p in hit) {
        g5.Index.TryGetFile(p, out var rec);
        Console.WriteLine($"  {p}  [{rec.Size:N0} B]");
    }
    return;
}

if (args.Length > 0 && args[0] == "api") {
    var want = args.Length > 1 ? args[1] : "";
    foreach (var an in new[] { "LibBundle3", "LibBundledGGPK3", "LibGGPK3" }) {
        System.Reflection.Assembly asm;
        try { asm = Assembly.Load(an); } catch { continue; }
        foreach (var t in asm.GetExportedTypes().Where(t => t.Name.Contains(want, StringComparison.OrdinalIgnoreCase)).OrderBy(t => t.FullName)) {
            Console.WriteLine($"\n== {t.FullName} : {t.BaseType?.Name}");
            foreach (var c in t.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
                Console.WriteLine($"   ctor({string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))})");
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                Console.WriteLine($"   prop {p.PropertyType.Name} {p.Name}");
            foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Where(m => !m.IsSpecialName))
                Console.WriteLine($"   {m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))})");
        }
    }
    return;
}

if (args.Length > 0 && args[0] == "inspect") {
    var asm = Assembly.Load("LibBundle3");
    foreach (var t in asm.GetExportedTypes().Where(t => t.Name.Contains("Node") || t.Name.Contains("Record") || t.Name.Contains("Tree")).OrderBy(t => t.FullName)) {
        Console.WriteLine($"\n-- {t.FullName} : {t.BaseType?.Name}");
        foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            Console.WriteLine($"   prop {p.PropertyType.Name} {p.Name}");
        foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Where(m => !m.IsSpecialName))
            Console.WriteLine($"   {m.ReturnType.Name} {m.Name}({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))})");
    }
    return;
}

if (args.Length < 2) {
    Console.WriteLine("usage: dumper <Content.ggpk> <outDir> [name1,name2,...]");
    Console.WriteLine("       dumper inspect");
    return;
}
var ggpkPath = args[0];
var outDir = args[1];
var only = args.Length > 2 && args[2].Length > 0
    ? args[2].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    : null;

Directory.CreateDirectory(outDir);

var schemaArg = args.FirstOrDefault(a => a.StartsWith("--schema=", StringComparison.Ordinal));
if (schemaArg is not null) {
    var sp = schemaArg["--schema=".Length..];
    try { DatContainer.ReloadDefinitions(sp); Console.WriteLine("schema loaded from " + sp); }
    catch (Exception e) { Console.WriteLine($"schema load FAILED: {e.GetType().Name}: {e.Message}"); }
}

if (args.Contains("--reload")) {
    try { DatContainer.ReloadDefinitions(); Console.WriteLine("schema: reloaded"); }
    catch (Exception e) { Console.WriteLine($"schema: reload failed ({e.Message})"); }
}

Console.WriteLine($"opening {ggpkPath} ...");
using var ggpk = new BundledGGPK(ggpkPath, true);
var index = ggpk.Index;
Console.WriteLine($"files in index: {index.Files.Count}");

var allPaths = index.Files.Values.Select(f => f.Path).Where(p => !string.IsNullOrEmpty(p)).ToList();

if (only is null) {
    var groups = allPaths.Where(p => p.StartsWith("data/", StringComparison.Ordinal))
        .Select(p => string.Join('/', p.Split('/').Take(2)))
        .GroupBy(g => g).OrderByDescending(g => g.Count()).Take(40);
    Console.WriteLine("data/ subdirs:");
    foreach (var g in groups) Console.WriteLine($"   {g.Key,-42} {g.Count()}");
}

var sw = System.Diagnostics.Stopwatch.StartNew();
var targets = allPaths
    .Where(IsDat)
    .Where(p => only is null || only.Any(o => DatName(p).Equals(o, StringComparison.OrdinalIgnoreCase)))
    .OrderBy(p => p, StringComparer.Ordinal)
    .ToList();
Console.WriteLine($"dat files matched: {targets.Count}");

int ok = 0, bad = 0;
foreach (var path in targets) {
    var name = DatName(path);
    try {
        if (!index.TryGetFile(path, out var rec)) { Console.WriteLine($"  MISS {path}"); bad++; continue; }
        var bytes = rec.Read().ToArray();
        var dat = new DatContainer(bytes, name, false);
        var csv = dat.ToCsv();
        var sub = path.StartsWith("data/") ? path[5..].Replace('/', '_') : name;
        var dst = Path.Combine(outDir, sub + ".csv");
        File.WriteAllText(dst, csv, new UTF8Encoding(false));
        Console.WriteLine($"  OK   {path,-55} {bytes.Length,9:N0} B -> {csv.Length,10:N0} chars");
        ok++;
    }
    catch (Exception e) {
        Console.WriteLine($"  FAIL {path}: {e.GetType().Name}: {e.Message}");
        bad++;
    }
}
Console.WriteLine($"done: {ok} ok, {bad} failed, {sw.Elapsed.TotalSeconds:N1}s");
