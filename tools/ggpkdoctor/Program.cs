// ggpk 体检 / 修复。自建 bundle 重名会让文件变成「找不到 bundle」，见 tools/shared/BundleHygiene.cs
using System.Security.Cryptography;
using System.Text;
using LibBundledGGPK3;

Console.OutputEncoding = Encoding.UTF8;
if (args.Length < 1) {
    Console.WriteLine("usage: ggpkdoctor <Content.ggpk> [补丁目录] [--fix]");
    Console.WriteLine("       默认只体检；--fix 才写入。补丁目录默认 out/patch-sd-dual-all");
    return 1;
}
var ggpkPath = args[0];
var srcDir = args.Skip(1).FirstOrDefault(a => !a.StartsWith("--")) ?? "out/patch-sd-dual-all";
var doFix = args.Contains("--fix");

var expected = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);   // 容器路径 → 应有字节
if (Directory.Exists(srcDir))
    foreach (var f in Directory.GetFiles(srcDir, "*.txt")) {
        var name = Path.GetFileName(f);
        const string pfx = "metadata_statdescriptions_";
        if (!name.StartsWith(pfx, StringComparison.OrdinalIgnoreCase)) continue;
        expected["metadata/statdescriptions/" + name[pfx.Length..]] = File.ReadAllBytes(f);
    }

using var ggpk = new BundledGGPK(ggpkPath, true);
var index = ggpk.Index;

var self = BundleHygiene.Inventory(index);
Console.WriteLine($"自建 bundle 记录 {self.Count} 个，文件 {index.Files.Count} 个");
foreach (var b in self)
    Console.WriteLine($"  #{b.BundleIndex,-6} {b.Path,-24} files={b.Files.Count,-4} {(BundleHygiene.IsAlive(b) ? "存在" : "悬空")}");

// ---- 体检 ----
var broken = new List<(string path, byte[] bytes)>();
var mismatch = 0;
foreach (var (rel, want) in expected.OrderBy(kv => kv.Key)) {
    if (!index.TryGetFile(rel, out var rec)) { Console.WriteLine($"  MISS {rel}"); continue; }
    byte[] got;
    try { got = rec.Read().ToArray(); }
    catch (Exception ex) {
        Console.WriteLine($"  BAD  {rel}  {ex.Message}");
        broken.Add((rel, want));
        continue;
    }
    if (Sha(got) != Sha(want)) { mismatch++; Console.WriteLine($"  DIFF {rel}  {got.Length:N0} vs {want.Length:N0} B"); }
}
Console.WriteLine($"体检：读不出 {broken.Count} 个，内容不符 {mismatch} 个");

if (!doFix) {
    Console.WriteLine();
    Console.WriteLine(broken.Count > 0 || self.Count > 1
        ? "需要修复：加 --fix 重跑（先把自建 bundle 归并成一个，再补写丢失的文件）"
        : "状态正常，无需修复。");
    return 0;
}

// ---- 留底：索引与原载体 bundle 的原始字节 ----
var backupDir = Path.GetFullPath(Path.Combine("out", "ggpk-backup-" + DateTime.Now.ToString("MMdd-HHmmss")));
Directory.CreateDirectory(backupDir);
if (index.TryGetFile("Bundles2/_.index.bin", out var idxRec))
    File.WriteAllBytes(Path.Combine(backupDir, "_.index.bin"), idxRec.Read().ToArray());
var carrier = self.Where(BundleHygiene.IsAlive).OrderByDescending(b => b.Files.Count).FirstOrDefault();
if (carrier is not null && carrier.TryGetBundle(out var cb, out _))
    using (cb) File.WriteAllBytes(Path.Combine(backupDir, carrier.Path.Replace('/', '_')), cb.ReadWithoutCache().ToArray());
Console.WriteLine($"留底：{backupDir}");

// ---- 归并 ----
var hy = BundleHygiene.Consolidate(index);
if (hy.Error is not null) { Console.WriteLine("无法修复：" + hy.Error); return 2; }
Console.WriteLine($"自建 bundle 记录 {self.Count} → {self.Count - hy.Removed}（丢失数据的文件 {hy.Unreadable.Count} 个）");

// ---- 补写丢失的文件 ----
var todo = hy.Unreadable.Select(p => (Path: p, Bytes: expected.TryGetValue(p, out var b) ? b : null))
                        .Where(x => x.Bytes is not null)
                        .ToList();
foreach (var (rel, bytes) in todo) {
    if (!index.TryGetFile(rel, out var rec)) { Console.WriteLine($"  ✗ 仍找不到 {rel}"); continue; }
    var t0 = Environment.TickCount64;
    rec.Write(bytes!, saveIndex: false);
    Console.WriteLine($"  补写 {rel}  {bytes!.Length:N0} B  ({Environment.TickCount64 - t0} ms)");
}
if (todo.Count == 0) Console.WriteLine("  没有需要补写的文件");

var tSave = Environment.TickCount64;
index.Save();
Console.WriteLine($"落盘索引 {Environment.TickCount64 - tSave} ms");
return 0;

static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b));
