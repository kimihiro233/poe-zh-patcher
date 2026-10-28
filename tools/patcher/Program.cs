// ggpk 写回器：把 out/patch/*.dat 替换进 Content.ggpk。
//
//   patcher status  <ggpk> <manifest>          只读：每个文件当前是「原始/已打补丁/未知」+ 版本对账
//   patcher apply   <ggpk> <manifest>          写入补丁（先全量预检，任一文件异常则整体不动）
//   patcher restore <ggpk> <manifest>          用 pairs/zh 原始字节还原
//
// 版本门禁：manifest 记录生成补丁时的游戏版本（3.29.3b / patch 3.29.3.3）。
// 当前游戏版本与它不一致时 apply 直接中止——因为同一份 diff 套到别的版本上是错的。
// 确实要强上时加 --force（会打印醒目警告，并把强制记录写进日志）。
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LibBundledGGPK3;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length < 3) {
    Console.WriteLine("usage: patcher <status|apply|restore> <Content.ggpk> <manifest.json> [--only=a,b] [--force]");
    return 1;
}
var mode = args[0].ToLowerInvariant();
var ggpkPath = args[1];
var manifestPath = Path.GetFullPath(args[2]);
var onlyArg = args.FirstOrDefault(a => a.StartsWith("--only=", StringComparison.Ordinal));
var only = onlyArg is null ? null : onlyArg["--only=".Length..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
var force = args.Contains("--force");

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
var rootEl = doc.RootElement;
var files = rootEl.GetProperty("files").EnumerateArray().ToList();
if (only is not null) files = files.Where(f => only.Contains(f.GetProperty("table").GetString()!)).ToList();

static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();
static byte[] ReadAll(string p) => File.ReadAllBytes(p);

// 从客户端日志取版本：GGG 的 exe 不带版本资源，日志里这两行是唯一可靠来源
static (string? client, string? patch) DetectVersion(string ggpkPath) {
    var dir = Path.GetDirectoryName(ggpkPath)!;
    foreach (var rel in new[] { "logs/LatestClient.txt", "logs/Client.txt" }) {
        var p = Path.Combine(dir, rel);
        if (!File.Exists(p)) continue;
        string? c = null, pa = null;
        try {
            foreach (var line in File.ReadLines(p)) {
                var m = Regex.Match(line, @"User agent: PoE release/tags/([\w.\-]+)");
                if (m.Success) c = m.Groups[1].Value;
                m = Regex.Match(line, @"Web root: https://patch\.poecdn\.com/([\w.\-]+)/");
                if (m.Success) pa = m.Groups[1].Value;
            }
        } catch { /* 日志被游戏占用时读不到就算了 */ }
        return (c, pa);
    }
    return (null, null);
}

var backupDir = Path.Combine(root, "out", "backup");
Directory.CreateDirectory(backupDir);
Console.WriteLine($"ggpk : {ggpkPath}  ({new FileInfo(ggpkPath).Length:N0} B)");
Console.WriteLine($"mode : {mode}   文件: {files.Count}");

// ---- 版本对账 ----
string? mClient = null, mPatch = null, mFp = null, mLockedAt = null;
if (rootEl.TryGetProperty("game", out var g)) {
    if (g.TryGetProperty("client_version", out var e1)) mClient = e1.GetString();
    if (g.TryGetProperty("patch_version", out var e2)) mPatch = e2.GetString();
    if (g.TryGetProperty("fingerprint", out var e3)) mFp = e3.GetString();
    if (g.TryGetProperty("locked_at", out var e4)) mLockedAt = e4.GetString();
}
var (curClient, curPatch) = DetectVersion(ggpkPath);
Console.WriteLine($"补丁适用 : {mClient ?? "(未记录)"}  patch {mPatch ?? "?"}  锁定于 {mLockedAt ?? "?"}  fp {(mFp is null ? "-" : mFp[..12])}");
Console.WriteLine($"当前游戏 : {curClient ?? "(未知)"}  patch {curPatch ?? "?"}");

bool versionMismatch = false;
if (mClient is null) {
    Console.WriteLine("  ! manifest 没有记录游戏版本（v0.2.x 之前的旧补丁）——无法确认适用性");
    versionMismatch = true;
} else if (curClient is not null && curClient != mClient) {
    Console.WriteLine($"  ! 客户端版本不符：补丁为 {mClient}，当前 {curClient}");
    versionMismatch = true;
} else if (curPatch is not null && mPatch is not null && curPatch != mPatch) {
    Console.WriteLine($"  ! 补丁版本不符：补丁为 {mPatch}，当前 {curPatch}");
    versionMismatch = true;
}

if (versionMismatch && mode == "apply") {
    if (!force) {
        Console.WriteLine();
        Console.WriteLine("中止：游戏版本与补丁不匹配。同一个位置的文本可能已经变了，硬套会写错行。");
        Console.WriteLine("  · 要按当前版本重做补丁：python tools/gameinfo.py  → 重跑分析 → python tools/patch_dat.py");
        Console.WriteLine("  · 确认没问题仍要写入：加 --force");
        return 4;
    }
    Console.WriteLine("  !! --force：忽略版本不符，继续写入（后果自负）");
}

using var ggpk = new BundledGGPK(ggpkPath, true);
var index = ggpk.Index;
Console.WriteLine($"index: {index.Files.Count} 个文件\n");

// ---- 第一遍：只读判定，全量预检 ----
var plan = new List<(JsonElement f, string table, string rel, string shaOrig, string shaNew, string dst, byte[] cur, string state)>();
foreach (var f in files) {
    var table = f.GetProperty("table").GetString()!;
    var rel = f.GetProperty("path_in_ggpk").GetString()!;
    var shaOrig = f.GetProperty("sha256_original").GetString()!;
    var shaNew = f.GetProperty("sha256_patched").GetString()!;
    var dst = Path.GetFullPath(Path.Combine(root, f.GetProperty("dst").GetString()!));
    if (!index.TryGetFile(rel, out var rec)) {
        plan.Add((f, table, rel, shaOrig, shaNew, dst, Array.Empty<byte>(), "缺失"));
        continue;
    }
    var cur = rec.Read().ToArray();
    var shaCur = Sha(cur);
    var state = shaCur == shaOrig ? "原始" : shaCur == shaNew ? "已打补丁" : "未知";
    plan.Add((f, table, rel, shaOrig, shaNew, dst, cur, state));
}

int nOrig = 0, nPatch = 0, nUnknown = 0, nDone = 0, nFail = 0;
foreach (var p in plan) {
    if (p.state == "原始") nOrig++; else if (p.state == "已打补丁") nPatch++; else nUnknown++;
    Console.WriteLine($"  {p.table,-26} {p.state,-10} {p.cur.Length,10:N0} B  {p.rel}");
}

// 除 status 外，只要有文件状态异常就整体不动（保证「要么全改，要么不改」）
if (mode != "status" && mode != "restore" && nUnknown > 0) {
    Console.WriteLine();
    Console.WriteLine($"中止：{nUnknown} 个文件既非原始也非补丁内容（多半是游戏更新过）。一个都没写。");
    Console.WriteLine("  先 python tools/gameinfo.py --diff 看哪些文件变了，再决定重做还是 restore。");
    return 5;
}

if (mode != "status") Console.WriteLine();
foreach (var p in plan) {
    if (mode == "status") continue;
    var rec = index.TryGetFile(p.rel, out var r) ? r : null;
    if (rec is null) { Console.WriteLine($"  MISS {p.rel}"); nFail++; continue; }

    if (mode == "restore") {
        if (p.state == "原始") { Console.WriteLine($"  {p.table,-26} = 已是原始内容，跳过"); continue; }
        var orig = Path.GetFullPath(Path.Combine(root, "pairs", "zh", p.table));
        if (!File.Exists(orig)) { Console.WriteLine($"  {p.table,-26} ! 找不到原始文件 {orig}"); nFail++; continue; }
        var bytes = ReadAll(orig);
        if (Sha(bytes) != p.shaOrig) { Console.WriteLine($"  {p.table,-26} ! 原始文件 sha 与 manifest 不符，拒绝还原"); nFail++; continue; }
        File.WriteAllBytes(Path.Combine(backupDir, p.table + ".before-restore"), p.cur);
        rec.Write(bytes, true);
        var back = rec.Read().ToArray();
        if (Sha(back) != p.shaOrig) { Console.WriteLine($"  {p.table,-26} x 还原后回读比对失败"); nFail++; continue; }
        Console.WriteLine($"  {p.table,-26} <- 已还原 {back.Length:N0} B");
        nDone++;
        continue;
    }

    if (p.state == "已打补丁") { Console.WriteLine($"  {p.table,-26} = 已是补丁内容，跳过"); continue; }
    if (!File.Exists(p.dst)) { Console.WriteLine($"  {p.table,-26} ! 缺补丁文件 {p.dst}"); nFail++; continue; }
    var patch = ReadAll(p.dst);
    if (Sha(patch) != p.shaNew) { Console.WriteLine($"  {p.table,-26} ! 补丁文件 sha 与 manifest 不符，拒绝写入"); nFail++; continue; }

    File.WriteAllBytes(Path.Combine(backupDir, p.table + ".before-apply"), p.cur);
    var sw = Stopwatch.StartNew();
    rec.Write(patch, true);
    var after = rec.Read().ToArray();
    if (Sha(after) != p.shaNew) { Console.WriteLine($"  {p.table,-26} x 写入后回读不符"); nFail++; continue; }
    Console.WriteLine($"  {p.table,-26} -> 已写入 {after.Length:N0} B（{sw.ElapsedMilliseconds} ms）");
    nDone++;
}

Console.WriteLine();
Console.WriteLine($"统计：原始 {nOrig} / 已打补丁 {nPatch} / 未知 {nUnknown} / 本次完成 {nDone} / 失败 {nFail}");
Console.WriteLine($"备份：{backupDir}");
return nFail == 0 ? 0 : 3;
