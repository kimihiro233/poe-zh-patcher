// 只读探针：看 Steam/WeGame 那种 Bundles2 目录（_.index.bin + *.bundle.bin）里有什么
using System.Security.Cryptography;
using LibBundle3;
using Idx = LibBundle3.Index;

Console.OutputEncoding = System.Text.Encoding.UTF8;
if (args.Length > 0 && args[0] == "--check") return Check.Run(args.Length > 1 ? args[1] : @"E://games//WeGameApps//流放之路//Bundles2//_.index.bin");
var dir = args.Length > 0 ? args[0] : @"E:\games\WeGameApps\流放之路\Bundles2";
var idxPath = Directory.Exists(dir) ? Path.Combine(dir, "_.index.bin") : dir;
dir = Path.GetDirectoryName(Path.GetFullPath(idxPath))!;
Console.WriteLine($"index: {idxPath}");
Console.WriteLine($"dir  : {dir}");

using var index = new Idx(idxPath, parsePaths: false, bundleFactory: new DriveBundleFactory(dir));
Console.WriteLine($"bundles: {index.Bundles.Length}   files: {index.Files.Count}");

Console.WriteLine("--- bundles ---");
foreach (var b in index.Bundles.Span)
    Console.WriteLine($"  {b.Path,-28} {b.UncompressedSize,12:N0}   files={b.Files.Count}");

Console.WriteLine("--- 试读目标路径 ---");
string[] probes = args.Length > 1 ? args[1..] : new[] {
    "metadata/statdescriptions/stat_descriptions.txt",
    "metadata/statdescriptions/gem_stat_descriptions.txt",
    "metadata/statdescriptions/active_skill_gem_stat_descriptions.txt",
    "metadata/statdescriptions/support_gem_stat_descriptions.txt",
    "metadata/statdescriptions/passive_skill_stat_descriptions.txt",
    "metadata/statdescriptions/advanced_mod_stat_descriptions.txt",
    "metadata/statdescriptions/map_stat_descriptions.txt",
    "Data/achievementitems.datc64",
    "Data/clientstrings.datc64",
    "Data/currencyitems.datc64",
    "Data/incursionrooms.datc64",
    "Data/uitalktext.datc64",
};
foreach (var p in probes) {
    if (!index.TryGetFile(p, out var f)) { Console.WriteLine($"  {p,-70} 缺失"); continue; }
    try {
        var bytes = f.Read().ToArray();
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        Console.WriteLine($"  {p,-70} {bytes.Length,10:N0} B  {sha[..16]}  bundle={f.BundleRecord.Path}");
    } catch (Exception ex) {
        Console.WriteLine($"  {p,-70} 读取失败 {ex.Message}");
    }
}
return 0;
