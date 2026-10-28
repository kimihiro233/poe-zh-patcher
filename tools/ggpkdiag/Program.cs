// ggpk 诊断：哪些文件读不出来，索引里的自建 bundle 是否悬空
using System.Text;
using LibBundledGGPK3;

Console.OutputEncoding = Encoding.UTF8;
var ggpkPath = args[0];
using var ggpk = new BundledGGPK(ggpkPath, true);
var index = ggpk.Index;

Console.WriteLine($"文件 {index.Files.Count}  bundle 记录 {index.Bundles.Length}");

var bundles = index.Bundles.ToArray();
foreach (var b in bundles) {
    var p = b.GetType().GetProperty("Path")?.GetValue(b) as string ?? "(?)";
    if (p.Contains("LibGGPK3"))
        Console.WriteLine($"  自建 bundle: {p}");
}

Console.WriteLine("--- 读取本项目的 31 个目标文件 ---");
var targets = new List<string>();
foreach (var path in index.Files.Values.Select(f => f.Path)) {
    if (path.StartsWith("metadata/statdescriptions/") || path.StartsWith("data/traditional chinese/"))
        targets.Add(path);
}
int ok = 0, bad = 0;
foreach (var rel in targets.OrderBy(x => x)) {
    if (!index.TryGetFile(rel, out var rec)) { Console.WriteLine($"  MISS {rel}"); bad++; continue; }
    try { var d = rec.Read(); ok++; }
    catch (Exception ex) { bad++; Console.WriteLine($"  FAIL {rel}\n        {ex.GetType().Name}: {ex.Message}"); }
}
Console.WriteLine($"读取：成功 {ok} 失败 {bad}");
