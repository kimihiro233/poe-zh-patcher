// ggpk 修复：对记录悬空的文件直接重写原始字节（不读旧内容）。
using System.Text;
using LibBundledGGPK3;

Console.OutputEncoding = Encoding.UTF8;
var ggpkPath = args[0];
var root = args[1];   // 项目根，用于取 pairs/zh 与 raw_sd

var pairs = new List<(string rel, string src)>();
foreach (var f in Directory.GetFiles(Path.Combine(root, "pairs", "zh"), "*.datc64")) {
    var name = Path.GetFileName(f);
    pairs.Add(($"data/traditional chinese/{name}", f));
}
foreach (var name in new[] { "tincture", "village" }) {
    var f = Directory.GetFiles(Path.Combine(root, "raw_sd"), $"*{name}_stat_descriptions.txt").First();
    pairs.Add(($"metadata/statdescriptions/{name}_stat_descriptions.txt", f));
}

using var ggpk = new BundledGGPK(ggpkPath, true);
var index = ggpk.Index;
int done = 0, fail = 0;
foreach (var (rel, src) in pairs) {
    if (!index.TryGetFile(rel, out var rec)) {
        // 只处理确实悬空的
        if (rel.Contains("tincture") || rel.Contains("village")) { Console.WriteLine($"MISS {rel}"); fail++; }
        continue;
    }
    bool readable;
    try { rec.Read(); readable = true; } catch { readable = false; }
    if (readable) continue;      // 只修坏掉的
    var bytes = File.ReadAllBytes(src);
    try {
        rec.Write(bytes, true);
        Console.WriteLine($"  重写 {rel}  {bytes.Length:N0} B");
        done++;
    } catch (Exception ex) {
        Console.WriteLine($"  失败 {rel}  {ex.GetType().Name}: {ex.Message}");
        fail++;
    }
}
Console.WriteLine($"重写 {done} 个，失败 {fail} 个");
