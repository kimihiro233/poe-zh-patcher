// ggpk 诊断：哪些文件读不出来，索引里的自建 bundle 是否悬空 / 重名
using System.Text;
using LibBundledGGPK3;

Console.OutputEncoding = Encoding.UTF8;
var ggpkPath = args.Length > 1 ? args[1] : args[0];
var mode = args.Length > 1 ? args[0] : "";
using var ggpk = new BundledGGPK(ggpkPath, true);
var index = ggpk.Index;

Console.WriteLine($"文件 {index.Files.Count}  bundle 记录 {index.Bundles.Length}");

var bundles = index.Bundles.ToArray();

// ---- 自建 bundle 清单：LibBundle3 用它们存放改动过的文件 ----
var custom = bundles.Where(b => b.Path.Contains("LibGGPK3")).ToArray();
Console.WriteLine($"自建 bundle 记录 {custom.Length} 个：");
foreach (var b in custom) {
    var alive = b.TryGetBundle(out var inst, out var ex);
    inst?.Dispose();
    Console.WriteLine($"  #{b.BundleIndex,-6} {b.Path,-28} files={b.Files.Count,-4} "
                      + (alive ? "存在" : $"悬空 {ex?.GetType().Name}: {ex?.Message}"));
    foreach (var f in b.Files.Take(8))
        Console.WriteLine($"         - {f.Path}  {f.Size:N0} B");
    if (b.Files.Count > 8) Console.WriteLine($"         ... 共 {b.Files.Count} 个");
}
var dup = custom.GroupBy(b => b.Path).Where(g => g.Count() > 1).ToArray();
if (dup.Length > 0) {
    Console.WriteLine();
    Console.WriteLine("!! 重名的自建 bundle（库删空 bundle 时会连带删掉同名的活文件）：");
    foreach (var g in dup)
        Console.WriteLine($"   {g.Key}  x{g.Count()}  → BundleIndex {string.Join(", ", g.Select(b => b.BundleIndex))}  "
                          + $"files={string.Join("/", g.Select(b => b.Files.Count))}");
}

if (mode == "bundles") return 0;

Console.WriteLine("--- 读取本项目的 31 个目标文件 ---");
var targets = new List<string>();
foreach (var f in index.Files.Values) {
    var p = f.Path;
    if (p is null) continue;
    if (p.StartsWith("metadata/statdescriptions/") || p.StartsWith("data/traditional chinese/"))
        targets.Add(p);
}
int ok = 0, bad = 0;
foreach (var rel in targets.OrderBy(x => x)) {
    if (!index.TryGetFile(rel, out var rec)) { Console.WriteLine($"  MISS {rel}"); bad++; continue; }
    try { _ = rec.Read(); ok++; }
    catch (Exception ex) { bad++; Console.WriteLine($"  FAIL {rel}\n        {ex.GetType().Name}: {ex.Message}"); }
}
Console.WriteLine($"读取：成功 {ok} 失败 {bad}");
return 0;
