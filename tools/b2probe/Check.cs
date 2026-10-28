using LibBundle3;
using Idx = LibBundle3.Index;

internal static class Check {
    public static int Run(string idxPath) {
        idxPath = Path.GetFullPath(idxPath);
        var dir = Path.GetDirectoryName(idxPath)!;
        Console.WriteLine($"index: {idxPath}");

        using var index = new Idx(idxPath, parsePaths: false, bundleFactory: new DriveBundleFactory(dir));
        Console.WriteLine($"bundles {index.Bundles.Length:N0}   files {index.Files.Count:N0}");

        var refs = new Dictionary<string, int>();
        var bad = 0;
        var ex = new List<string>();
        foreach (var f in index.Files.Values) {
            var bp = f.BundleRecord?.Path ?? "<null>";
            refs[bp] = refs.GetValueOrDefault(bp) + 1;
            var cap = f.BundleRecord?.UncompressedSize ?? 0;
            if (f.Offset < 0 || f.Size < 0 || (long)f.Offset + f.Size > cap) {
                bad++;
                if (ex.Count < 10) ex.Add($"  x {f.Path ?? "?"}  off={f.Offset} size={f.Size} > cap={cap}  in {bp}");
            }
        }
        foreach (var e in ex) Console.WriteLine(e);
        Console.WriteLine($"越界记录：{bad}");

        Console.WriteLine("--- LibGGPK3 自建 bundle ---");
        var bundles = index.Bundles.Span.ToArray();
        foreach (var kv in refs.Where(k => k.Key.StartsWith("LibGGPK3", StringComparison.OrdinalIgnoreCase)).OrderBy(k => k.Key)) {
            var b = bundles.FirstOrDefault(x => x.Path == kv.Key);
            Console.WriteLine($"  {kv.Key,-34} files={kv.Value,6}  uncompressed={b?.UncompressedSize ?? -1,12:N0}");
        }

        Console.WriteLine("--- 抽样读 20 个随机文件 ---");
        var rnd = new Random(12345);
        var all = index.Files.Values.ToArray();
        var fails = 0;
        for (var i = 0; i < 20; i++) {
            var f = all[rnd.Next(all.Length)];
            try {
                var b = f.Read().ToArray();
                if (b.Length != f.Size) { Console.WriteLine($"  x 长度不符 {f.Path} {b.Length} != {f.Size}"); fails++; }
            } catch (Exception e) {
                Console.WriteLine($"  x 读取失败 {f.Path}: {e.Message}");
                fails++;
            }
        }
        Console.WriteLine($"抽样失败：{fails}/20");
        return bad == 0 && fails == 0 ? 0 : 7;
    }
}
