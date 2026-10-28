// ggpk 写回器：把 out/patch/*.dat 替换进 Content.ggpk。
//
//   patcher status  <ggpk> <manifest>          只读：报告每个文件当前是「原始/已打补丁/未知」
//   patcher apply   <ggpk> <manifest>          写入补丁（已打过的跳过；状态未知的中止）
//   patcher restore <ggpk> <manifest>          用 pairs/zh 原始字节还原
//
// 每个文件写入后都从 ggpk 重新读出比对 sha256，不一致立即报错中止。
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LibBundledGGPK3;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length < 3) {
    Console.WriteLine("usage: patcher <status|apply|restore> <Content.ggpk> <manifest.json> [--only=a,b]");
    return 1;
}
var mode = args[0].ToLowerInvariant();
var ggpkPath = args[1];
var manifestPath = Path.GetFullPath(args[2]);
var onlyArg = args.FirstOrDefault(a => a.StartsWith("--only=", StringComparison.Ordinal));
var only = onlyArg is null ? null : onlyArg["--only=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

if (!File.Exists(ggpkPath)) { Console.WriteLine($"找不到 ggpk：{ggpkPath}"); return 1; }
if (!File.Exists(manifestPath)) { Console.WriteLine($"找不到 manifest：{manifestPath}"); return 1; }

var running = Process.GetProcesses().Where(p => {
    try { return p.ProcessName.Contains("PathOfExile", StringComparison.OrdinalIgnoreCase) && p.Id != Environment.ProcessId; }
    catch { return false; }
}).ToList();
if (running.Count > 0 && mode != "status") {
    Console.WriteLine($"游戏正在运行（PID {string.Join(",", running.Select(p => p.Id))}），请先退出游戏");
    return 2;
}

var root = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(manifestPath)!))!;  // <root>/out/patch/manifest.json -> <root>
using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
var files = doc.RootElement.GetProperty("files").EnumerateArray().ToList();
if (only is not null) files = files.Where(f => only.Contains(f.GetProperty("table").GetString()!)).ToList();

static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
static byte[] ReadAll(string p) => File.ReadAllBytes(p);

var backupDir = Path.Combine(root, "out", "backup");
Directory.CreateDirectory(backupDir);
Console.WriteLine($"ggpk : {ggpkPath}  ({new FileInfo(ggpkPath).Length:N0} B)");
Console.WriteLine($"mode : {mode}   文件: {files.Count}");

using var ggpk = new BundledGGPK(ggpkPath, true);
var index = ggpk.Index;
Console.WriteLine($"index: {index.Files.Count} 个文件\n");

int nOrig = 0, nPatch = 0, nUnknown = 0, nDone = 0, nFail = 0;
foreach (var f in files) {
    var table = f.GetProperty("table").GetString()!;
    var rel = f.GetProperty("path_in_ggpk").GetString()!;
    var shaOrig = f.GetProperty("sha256_original").GetString()!;
    var shaNew = f.GetProperty("sha256_patched").GetString()!;
    var dst = Path.GetFullPath(Path.Combine(root, f.GetProperty("dst").GetString()!));

    if (!index.TryGetFile(rel, out var rec)) { Console.WriteLine($"  MISS {rel}"); nFail++; continue; }
    var cur = rec.Read().ToArray();
    var shaCur = Sha(cur);

    var state = shaCur == shaOrig ? "原始" : shaCur == shaNew ? "已打补丁" : "未知";
    if (state == "原始") nOrig++; else if (state == "已打补丁") nPatch++; else nUnknown++;
    Console.WriteLine($"  {table,-26} {state,-10} {cur.Length,10:N0} B  {rel}");

    if (mode == "status") continue;

    if (mode == "restore") {
        if (state == "原始") { Console.WriteLine("     = 已是原始内容，跳过"); continue; }
        var orig = Path.GetFullPath(Path.Combine(root, "pairs", "zh", table));
        if (!File.Exists(orig)) { Console.WriteLine($"     ! 找不到原始文件 {orig}"); nFail++; continue; }
        var bytes = ReadAll(orig);
        if (Sha(bytes) != shaOrig) { Console.WriteLine("     ! 原始文件 sha 与 manifest 不符，拒绝还原"); nFail++; continue; }
        File.WriteAllBytes(Path.Combine(backupDir, table + ".before-restore"), cur);
        rec.Write(bytes, true);
        var back = rec.Read().ToArray();
        if (Sha(back) != shaOrig) { Console.WriteLine("     x 还原后回读比对失败"); nFail++; continue; }
        Console.WriteLine($"     <- 已还原 {back.Length:N0} B");
        nDone++;
        continue;
    }

    if (state == "已打补丁") { Console.WriteLine("     = 已是补丁内容，跳过"); continue; }
    if (state == "未知") { Console.WriteLine("     ! 内容既非原始也非补丁，中止（先 restore 或人工核对）"); nFail++; continue; }
    if (!File.Exists(dst)) { Console.WriteLine($"     ! 缺补丁文件 {dst}"); nFail++; continue; }
    var patch = ReadAll(dst);
    if (Sha(patch) != shaNew) { Console.WriteLine("     ! 补丁文件 sha 与 manifest 不符，拒绝写入"); nFail++; continue; }

    File.WriteAllBytes(Path.Combine(backupDir, table + ".before-apply"), cur);
    var sw = Stopwatch.StartNew();
    rec.Write(patch, true);
    var after = rec.Read().ToArray();
    if (Sha(after) != shaNew) { Console.WriteLine("     x 写入后回读不符"); nFail++; continue; }
    Console.WriteLine($"     -> 已写入 {after.Length:N0} B（{sw.ElapsedMilliseconds} ms）");
    nDone++;
}

Console.WriteLine();
Console.WriteLine($"统计：原始 {nOrig} / 已打补丁 {nPatch} / 未知 {nUnknown} / 本次完成 {nDone} / 失败 {nFail}");
Console.WriteLine($"备份：{backupDir}");
return nFail == 0 ? 0 : 3;
