// 自建 bundle（Bundles2/LibGGPK3/N.bundle.bin）是 LibBundle3 用来放「改过的文件」的容器。
// 它有两个坑，凡是反复写过的客户端都会踩：
//
// 1) Index.GetBundleToWrite 按名字在 CustomBundles 里找可复用的记录，而 CustomBundles 是
//    打开索引时从 _Bundles 里捡出所有 "LibGGPK3/" 开头的记录。于是下一次运行会**复用最老的那条**，
//    把它的物理文件从 0 开始整个重写。同一路径在 _Bundles 里可能有好几条记录（历次运行各留一条），
//    它们各自记着当时那一刻的 UncompressedSize 和文件 offset —— 物理文件被覆盖后，
//    这些布局全部作废：游戏按旧尺寸解压 chunk 就是 Buffer Underflow，按旧 offset 取数据就是别人的字节。
//
// 2) Index.Save() 清理空的自建 bundle 时用 _Bundles.RemoveAt(br.BundleIndex)，而其余记录的
//    BundleIndex 不会跟着改。只要发生一次删除，后面所有 bundle 的编号就整体错位。
//
// 所以这里不做「归并」——不同 bundle 的 offset 空间本来就不可互换，把记录挂到同一条上
// 只会让「读错内容」升级成「读崩」。正确做法是每次写入前换一条**唯一命名**的新 bundle，
// 把 CustomBundles 清成只剩它：写入全部落在新文件里，旧记录与旧物理文件原样留着，
// 仍指向它们的文件照样读得出来；要迁走的文件由上层用基线重写一遍，顺带落到新 bundle。
using System.Reflection;
using LibBundle3;
using LibBundle3.Records;
using Idx = LibBundle3.Index;

internal sealed class DetachResult {
    public int Retired;                                                        // 停用的旧自建记录条数
    public string Name = "";                                                    // 新建的 bundle 文件名
    public HashSet<string> Inherited = new(StringComparer.OrdinalIgnoreCase);    // 还挂在旧记录上的文件
    public string? Error;                                                       // 建不出来时的原因
}

internal static class BundleHygiene {
    private const string Prefix = "LibGGPK3/";
    private const string Ext = ".bundle.bin";

    private static readonly FieldInfo FBundles =
        typeof(Idx).GetField("_Bundles", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo FCustom =
        typeof(Idx).GetField("CustomBundles", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo FToWrite =
        typeof(Idx).GetField("_BundleToWrite", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo FStream =
        typeof(Idx).GetField("_BundleStreamToWrite", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo MCreate =
        typeof(Idx).GetMethod("CreateBundle", BindingFlags.Instance | BindingFlags.NonPublic)!;

    public static bool IsAlive(BundleRecord b) {
        var ok = b.TryGetBundle(out var inst, out _);
        inst?.Dispose();
        return ok;
    }

    public static List<BundleRecord> Inventory(Idx index) {
        var all = (BundleRecord[])FBundles.GetValue(index)!;
        return all.Where(b => b.Path.StartsWith(Prefix, StringComparison.Ordinal)).ToList();
    }

    /// <summary>这个文件是不是挂在自建 bundle 上（只有补丁器写过的文件会在）。</summary>
    public static bool IsSelf(FileRecord f) =>
        f.BundleRecord.Path.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>只读：列出所有还挂在自建 bundle 上的文件（它们的布局是否可信要用读出来的字节判断）。</summary>
    public static HashSet<string> Collect(Idx index) {
        var all = (BundleRecord[])FBundles.GetValue(index)!;
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in all) {
            if (!b.Path.StartsWith(Prefix, StringComparison.Ordinal)) continue;
            foreach (var f in b.Files)
                if (f.Path is not null) set.Add(f.Path);
        }
        return set;
    }

    /// <summary>
    /// 换一条全新的自建 bundle 来承接本次写入；旧记录一律停用（不再进 CustomBundles）。
    /// 容器本来就干净（没有自建记录）时什么都不做，让库自己按需创建。
    /// </summary>
    public static DetachResult Detach(Idx index) {
        var r = new DetachResult();
        var all = (BundleRecord[])FBundles.GetValue(index)!;
        var self = all.Where(b => b.Path.StartsWith(Prefix, StringComparison.Ordinal)).ToArray();
        if (self.Length == 0) return r;

        // 先逐条打开一遍：Bundle 的构造函数会把记录里的 UncompressedSize 校正成物理文件的真实值，
        // 这样「记录说 2018 B、文件其实 20 MB」这种历史错位能在写索引时被抹平。
        foreach (var b in self) {
            try { if (b.TryGetBundle(out var inst, out _)) inst!.Dispose(); } catch { /* 悬空记录，数据已丢 */ }
        }

        r.Retired = self.Length;
        foreach (var b in self)
            foreach (var f in b.Files)
                if (f.Path is not null) r.Inherited.Add(f.Path);

        // 名字必须唯一：GetBundleToWrite / CreateBundle 都是 Create 语义，同名会 File.Create 把旧文件清空。
        // 用时间戳而不是序号，是为了让「本次写入」的物理文件与历史文件永不相撞。
        var name = Prefix + "zh" + DateTime.Now.ToString("yyMMddHHmmss");
        Bundle nb;
        try { nb = (Bundle)MCreate.Invoke(index, new object?[] { name })!; }
        catch (Exception ex) { r.Error = ex.Message; return r; }

        FToWrite.SetValue(index, nb);
        FStream.SetValue(index, new MemoryStream(1 << 20));

        var custom = (List<BundleRecord>)FCustom.GetValue(index)!;
        custom.Clear();
        custom.Add(nb.Record!);

        r.Name = name + Ext;
        return r;
    }

    /// <summary>诊断用：把索引里的 bundle 记录原样列出来（自建的全列，游戏自带的只报数量）。</summary>
    public static void Dump(Idx index, bool all = false) {
        var bundles = (BundleRecord[])FBundles.GetValue(index)!;
        var custom = (List<BundleRecord>)FCustom.GetValue(index)!;
        Console.WriteLine($"  索引里共 {bundles.Length} 个 bundle 记录，其中自建 {custom.Count} 条");
        var shown = 0;
        for (var i = 0; i < bundles.Length; i++) {
            var b = bundles[i];
            var self = b.Path.StartsWith(Prefix, StringComparison.Ordinal);
            if (!self && !all) continue;
            var alive = IsAlive(b);
            var bad = b.Files.Any(f => (long)f.Offset + f.Size > b.UncompressedSize);
            Console.WriteLine($"    [{i,3}] {b.Path,-30} Index={b.BundleIndex,3}  解压后={b.UncompressedSize,12:N0}  文件={b.Files.Count,4}  {(alive ? "可读" : "悬空")}{(bad ? "  ← 有文件越界" : "")}");
            shown++;
        }
        if (shown == 0) Console.WriteLine("    （没有任何自建 bundle 记录）");
    }

    /// <summary>
    /// 落盘之后调用：删掉已经没有任何文件指向的自建 bundle 物理文件。
    /// 记录本身留在索引里（删记录要重排 BundleIndex，风险大于收益），只是文件不再占地方。
    /// </summary>
    public static int TrimOrphans(Idx index, string dir) {
        if (!Directory.Exists(dir)) return 0;
        var all = (BundleRecord[])FBundles.GetValue(index)!;
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in all)
            if (b.Path.StartsWith(Prefix, StringComparison.Ordinal) && b.Files.Count > 0)
                keep.Add(b.Path);

        var n = 0;
        long freed = 0;
        foreach (var f in Directory.EnumerateFiles(dir, "*.bundle.bin")) {
            var name = Path.GetFileName(f);
            // 索引里的 Path 就是 "LibGGPK3/" + 文件名（含 .bundle.bin），不要剥扩展名
            var key = Prefix + name;
            if (keep.Contains(key)) continue;
            try { freed += new FileInfo(f).Length; File.Delete(f); n++; } catch { }
        }
        if (n > 0) Console.WriteLine($"  清掉 {n} 个不再被引用的自建 bundle（{freed / 1048576.0:F1} MB）");
        return n;
    }

    /// <summary>
    /// 落盘前调用：本次一条都没写（或建的 bundle 是空的）就把它丢掉，
    /// 否则 Index.Save() 会 RemoveAt 掉这条空记录，把后面所有 bundle 的编号整体打乱。
    /// </summary>
    public static void DropEmpty(Idx index) {
        var custom = (List<BundleRecord>)FCustom.GetValue(index)!;
        var keep = custom.Where(b => b.Files.Count > 0).ToList();
        if (keep.Count != custom.Count) { custom.Clear(); custom.AddRange(keep); }

        if (FToWrite.GetValue(index) is not Bundle tw) return;
        if (tw.Record is { Files.Count: > 0 }) return;
        try { tw.Dispose(); } catch { }
        FToWrite.SetValue(index, null);
        FStream.SetValue(index, null);
    }
}
