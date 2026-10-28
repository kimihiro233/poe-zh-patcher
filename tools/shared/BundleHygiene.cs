// LibBundle3 的自建 bundle（Bundles2/LibGGPK3/N.bundle.bin）在反复写入后会留下重名记录：
// Index.GetBundleToWrite 只按 CustomBundles 里的名字查重，不看 GGPK 里实际存在的文件；
// 而 Index.Save() 删空 bundle 时是按「路径」调用 DeleteBundle 的 —— 于是同名的那一个活文件
// 会被连带删掉，里面的文件就变成「Cannot find bundle」。
// 这里把重名/悬空记录收成一条干净记录（文件偏移原样保留，不搬数据）。
using System.Reflection;
using Idx = LibBundle3.Index;
using LibBundle3.Records;

internal sealed class HygieneResult {
    public int Removed;                 // 归并掉的记录数（0 = 本来干净）
    public string? Error;               // 无法就地归并的原因
    public HashSet<string> Unreadable = new(StringComparer.OrdinalIgnoreCase);  // 数据已随悬空 bundle 丢失的文件
    public List<BundleRecord> SelfMade = new();
}

internal static class BundleHygiene {
    private const string Prefix = "LibGGPK3/";
    private const string Ext = ".bundle.bin";

    private static readonly FieldInfo FBundles =
        typeof(Idx).GetField("_Bundles", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo FCustom =
        typeof(Idx).GetField("CustomBundles", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly ConstructorInfo Ctor =
        typeof(BundleRecord).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
            new[] { typeof(string), typeof(int), typeof(Idx), typeof(int) }, null)!;

    public static bool IsAlive(BundleRecord b) {
        var ok = b.TryGetBundle(out var inst, out _);
        inst?.Dispose();
        return ok;
    }

    public static List<BundleRecord> Inventory(Idx index) {
        var all = (BundleRecord[])FBundles.GetValue(index)!;
        return all.Where(b => b.Path.StartsWith(Prefix, StringComparison.Ordinal)).ToList();
    }

    /// <summary>重名 / 悬空的自建 bundle 记录收成一个；返回处理结果（不改动文件数据）。</summary>
    public static HygieneResult Consolidate(Idx index) {
        var r = new HygieneResult();
        var all = (BundleRecord[])FBundles.GetValue(index)!;
        var self = all.Where(b => b.Path.StartsWith(Prefix, StringComparison.Ordinal)).ToArray();
        r.SelfMade = self.ToList();
        if (self.Length <= 1) return r;                       // 本来就只有一条，没什么可收

        var minIdx = self.Min(b => b.BundleIndex);
        if (self.Length != all.Length - minIdx) {
            r.Error = "自建 bundle 不在索引末尾（中间还夹着别的 bundle），不敢就地归并";
            return r;
        }

        var carrier = self.Where(IsAlive).OrderByDescending(b => b.Files.Count).FirstOrDefault();
        if (carrier is null) { r.Error = "自建 bundle 全部悬空，数据已丢失，只能整份重写"; return r; }

        // 悬空的那些记录里挂着的文件，数据已经没了：记下来交给上层重写
        foreach (var b in self)
            if (!ReferenceEquals(b, carrier) && !IsAlive(b))
                foreach (var f in b.Files)
                    if (f.Path is not null) r.Unreadable.Add(f.Path);

        var clean = (BundleRecord)Ctor.Invoke(new object?[] {
            carrier.Path[..^Ext.Length], carrier.UncompressedSize, index, minIdx
        })!;

        // 先快照：Redirect 会改动各记录的 _Files
        var files = self.SelectMany(b => b.Files).ToArray();
        foreach (var f in files)
            f.Redirect(clean, f.Offset, f.Size);

        FBundles.SetValue(index, all.Take(minIdx).Append(clean).ToArray());
        var custom = (List<BundleRecord>)FCustom.GetValue(index)!;
        custom.Clear();
        custom.Add(clean);

        r.Removed = self.Length - 1;
        return r;
    }
}
