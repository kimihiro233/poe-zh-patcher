# 读写链路缺陷修复计划（B1–B9）

审计基线：v0.5.3（commit `b70820a`）。本计划只处理读写链路自身的缺陷，
不含 P0 的提权路径收敛与并发互斥 —— 那两项会改写入协议，另开一轮。

---

## 0. 总览

| 批 | 条目 | 版本 | 主题 | 人日 | 新增自检 |
|---|---|---|---|---|---|
| A | B1 B3 B6 B9 | v0.5.4 | 止血：会在真机静默毁数据 / 静默改配置 | 2.0 | `--recover-test`、`--bundle-name-test`、`--args-test`、`roundtrip_test.py` |
| B | B2 B4 B5 B7 | v0.5.5 | 完整性：备份与撤销日志的边界 | 1.5 | `--undo-test` 扩两例、`--backup-test` |
| C | B8 | v0.5.6 | 可诊断性：把「结构不符」拆成具体原因 | 0.5 | 无（改文案与分支） |

排序依据不是严重度，而是**「用户能否察觉」**：
A 批的四条都会让用户在毫无提示的情况下拿到一个错误状态；B 批只在特定时序下发生；
C 批只影响排障体验。

依赖关系：B1 与 B3 无耦合，B6 与 B9 无耦合，四条可并行；B2 依赖 B1 改造后的
`RecoverResult`（复用 `Complete` 字段）；C 批独立。

---

## 1. 三条修复原则

1. **失败必须可见**。「回滚成功 / 部分成功 / 失败」是三种状态，不能合并成 bool。
   现有 `Recover()` 把「备份不存在」当成「回滚成功」，就是这条原则的反例。
2. **删现场之前先留证据**。journal、undo 日志只允许在「已确认恢复」之后删除。
   恢复失败还删日志，等于把唯一的线索一起销毁。
3. **每条修复配一条能红的自检**。改完跑一遍不红的测试不算测试 ——
   这批缺陷全部逃过了现有 6 个自检，说明覆盖面缺口在「失败路径」而非「正常路径」。

---

## 2. Batch A / v0.5.4

### B1 回滚「假成功」并删除现场

**根因**（`tools/shared/Safety.cs`）

- `208` `RollbackFiles`：`if (j.IndexBackup.Length > 0 && File.Exists(...))` ——
  备份缺失时整块被跳过，`err` 保持 `null`，调用方读成「成功」。
- `196` `Recover`：不看 err 内容就 `File.Delete(jp)`。
- `205` `RestoredFiles = j.Written.Count`：是「打算写几个」，不是「写回几个」。
- `181` journal 损坏分支：找不到备份时同样删日志且 `Recovered=false`，
  用户什么都看不到，下次启动连「上次没走完」都不知道。
- `Program.cs:655` 把这个结论放大成「客户端可以正常启动，不用验证完整性」。

**改法**

```csharp
internal sealed class RecoverResult {
    public bool Recovered;
    public bool Complete;        // 新增：是否真的全退回去了
    public bool JournalKept;     // 新增：现场是否保留
    public string JournalPath = "";
    public string Message = "";
    public List<string> Errors = new();
    public int RestoredFiles;
}
```

```csharp
// Safety.cs:RollbackFiles —— 返回真实结果而不是「有没有异常」
private static (List<string> errs, int restored) RollbackFiles(JournalFile j) {
    var errs = new List<string>();
    var restored = 0;

    if (string.IsNullOrEmpty(j.IndexBackup)) {
        errs.Add("日志里没有索引备份路径，无法判断该退回哪一份");
    } else if (!File.Exists(j.IndexBackup)) {
        errs.Add($"索引备份不见了：{j.IndexBackup}");
    } else {
        try {
            var want = Sha256(File.ReadAllBytes(j.IndexBackup));
            File.Copy(j.IndexBackup, j.Index, overwrite: true);
            if (Sha256(File.ReadAllBytes(j.Index)) != want)
                errs.Add("索引写回后校验不一致（磁盘错误？）");
            else restored = 1;
        } catch (Exception ex) { errs.Add($"索引恢复失败：{ex.Message}"); }
    }

    var dir = Path.GetDirectoryName(j.Index)!;
    foreach (var name in j.NewBundles) {
        // 顺手堵住目录穿越：这份 JSON 落在用户可写目录里，名字不能当可信输入
        var p = Path.GetFullPath(Path.Combine(dir, name.Replace('/', Path.DirectorySeparatorChar)));
        if (!p.StartsWith(Path.GetFullPath(dir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) {
            errs.Add($"{name}：路径指向容器之外，拒绝删除");
            continue;
        }
        try { if (File.Exists(p)) { File.Delete(p); restored++; } }
        catch (Exception ex) { errs.Add($"{name}：{ex.Message}"); }
    }
    return (errs, restored);
}
```

```csharp
// Safety.cs:Recover —— 成功才删日志；失败留现场并可重试
var (errs, restored) = RollbackFiles(j);
r.RestoredFiles = restored;
r.Errors = errs;
r.Complete = errs.Count == 0;

if (r.Complete) {
    try { File.Delete(jp); } catch { }
} else {
    j.Attempts++;                       // 新增字段，缺省 0，老 journal 兼容
    j.LastErrors = errs.Take(8).ToList();
    SaveJournalAt(jp, j);
    r.JournalKept = true;
    r.JournalPath = jp;
}

r.Recovered = true;
r.Message = r.Complete
    ? $"上次写入没有正常结束（开始于 {j.Started}），已回滚到写入前的状态"
    : $"上次写入没有正常结束，但只退回了一部分（第 {j.Attempts} 次尝试）";
```

journal 损坏分支同样处理：找不到任何可用备份 → **不删** `txn.json`，
`Complete=false`，`Message="日志损坏且没有可用备份"`。

```csharp
// Program.cs:651 —— 三种状态分开说，失败时不继续写
if (rec.Recovered) {
    Console.WriteLine();
    if (rec.Complete) {
        // 现有绿字保持不变
    } else {
        Console.WriteLine(" 上次写入没有正常结束，而且没能完全退回去：");
        foreach (var e in rec.Errors.Take(8)) Console.WriteLine("   - " + e);
        Console.WriteLine($" 现场记录已保留：{rec.JournalPath}");
        Console.WriteLine(" 建议顺序：① 用 Steam/WeGame「验证游戏完整性」→ ② 重新运行本程序。");
        Console.WriteLine(" 在退干净之前，本程序不会写入任何东西（--yes 可强行继续，风险自负）。");
        if (!Args.ContainsKey("yes")) { Pause(); return 3; }
    }
}
```

**行为变化**（必须写进发布说明）：
回滚失败时退出码 3 且不进入菜单；老版本是「报成功然后继续」。

**验收**：新增 `--recover-test`，读 `fixtures/recover/cases.json`，每个场景在
临时目录里构造 `Bundles2/.poe-zh/txn.json` + 备份文件，断言四件事：
`Complete`、`journal 是否存在`、`索引字节是否等于预期`、`容器外文件是否未被触碰`。

| 用例 | 构造 | 期望 |
|---|---|---|
| C1 备份存在 | 正常 backup | Complete=true，journal 删除，索引 = 备份字节 |
| C2 备份缺失 | backup 路径指向不存在文件 | Complete=false，journal 保留，索引不动 |
| C3 日志损坏 + 有备份 | txn.json 写成 `{{{` | Complete=true，按最近备份恢复 |
| C4 日志损坏 + 无备份 | 同 C3 但删掉备份 | Complete=false，journal 保留 |
| C5 名字穿越 | NewBundles 含 `..\..\outside.bin` | Complete=false，容器外文件仍在 |

C5 顺带覆盖 P0-1 里 `RollbackFiles` 这一处；P0-1 的其余入口（`IndexBackup` 绝对路径、
ggpk `meta.Target`）不在本批范围，别在发布说明里宣布「路径问题已解决」。

**风险**：保留 journal 会让每次启动都报同一条错。缓解：`Attempts` 到 3 次后
消息改为「已尝试 3 次仍失败，请按上面的顺序手工处理」，并停止自动重试
（`if (j.Attempts >= 3) 只报错不重试`），避免每次启动都去动文件。

---

### B3 自建 bundle 名字只精确到秒

**根因**（`tools/shared/BundleHygiene.cs:94`）
`"zh" + yyMMddHHmmss`。注释自己写着「同名会 `File.Create` 把旧文件清空」。
同一秒两次写入（脚本循环；并发是 P0-2，本批不修）→ 第二份清空第一份**仍被索引引用**的
bundle，v0.4.6 的 Buffer Underflow 会原地复活。

**改法**

```csharp
// 名字：时间戳 + pid + 4 位随机，撞了就换一个
private static string NewName() {
    var stamp = DateTime.Now.ToString("yyMMddHHmmss");
    for (var i = 0; i < 8; i++) {
        var name = Prefix + "zh" + stamp
                 + "-" + Environment.ProcessId.ToString("x")
                 + "-" + Random.Shared.Next(0x10000).ToString("x4");
        if (name.Length <= 40 && !BundleFileExists(name)) return name;
    }
    return "";   // 交回调用方报错，不瞎猜
}
```

```csharp
var name = NewName();
if (name.Length == 0) { r.Error = "连续 8 次都没拿到不重复的 bundle 名"; return r; }
try { nb = (Bundle)MCreate.Invoke(index, new object?[] { name })!; }
catch (Exception ex) { r.Error = ex.Message; return r; }

// 观测点：新建的 bundle 必须是空的。不空说明撞上了一个已有物理文件，
// 内容被继承进来了 —— 这正是 v0.4.6 的现场，宁可中止也不继续写。
if (nb.Record is { Files.Count: > 0 }) {
    r.Error = $"新 bundle {name} 里已经含有 {nb.Record.Files.Count} 个文件，疑似同名覆盖";
    return r;
}
```

`BundleFileExists` 用 `MCreate` 同一个根目录拼路径（`Path.GetDirectoryName(indexFile)`），
需要确认 LibGGPK3 的 bundle 存放根目录；若拿不到就退化为只靠随机名 + 上面的观测点。

**验收**：`--bundle-name-test`
1. 生成 20000 个名字，`HashSet` 计数必须等于 20000；
2. 格式匹配 `^LibGGPK3/zh\d{12}-[0-9a-f]+-[0-9a-f]{4}$`，长度 ≤ 40；
3. 桩测试：预置一个同名空文件后调用 `NewName()`，必须返回不同的名字（注入固定时间戳）。

**风险**：名字从 16 字符变到约 28 字符，进索引 `Path` 字段。LibGGPK3 对 bundle 名
没有明确长度限制（是字符串表），但为稳妥把上限钉在 40 并在自检里断言。

---

### B6 `datwrite` 零编辑也改文件

**根因**（`tools/datwrite.py:77-81`）
池尾对齐补位写在「有没有编辑」判断之前。8/60 个奇数长度文件在零编辑时仍 +1 或 +2 字节，
导致 `patcher` 把一份干净原文的 sha 判成「已打过补丁」。

**改法**

```python
def patch_bytes(raw, edits, name=""):
    edits = list(edits)
    if not edits:
        t = load_raw(raw, name)          # 仍然解析，非法文件要照样报错
        return raw, {"file": name, "row_count": t.row_count, "row_size": t.row_size,
                     "pool_base": t.pool_base, "old_len": len(raw), "new_len": len(raw),
                     "appended_bytes": 0, "edits": []}
    ...
    # 末尾：所有编辑都是「同文」时，产物必须与原文逐字节相同
    out = bytes(buf)
    if out == raw:
        report["new_len"] = len(raw)
        report["appended_bytes"] = 0
        return raw, report
    report["new_len"] = len(out)
    return out, report
```

第二条兜底比第一条重要：`resolve_slot` 可能因为共享串而把所有编辑都变成 skip，
那时 buf 已被补位改过，入口快路径拦不到。

**验收**：新增 `tools/roundtrip_test.py`
- 对 `pairs/zh/*.datc64` 全量跑 `patch_bytes(raw, [])`，断言 `out == raw`；
- 再对每个文件取一条「新旧相同」的编辑，断言 `out == raw`；
- `pairs/` 不入库 → 缺失时 SKIP 并说明原因（照 `run_tests.py` 里 `--data-test` 的做法）。

**风险**：下游若依赖「输出一定做过对齐」，需要检查 `patch_dat.py` 里有没有
`len(out) % 2` 的假设。目前只在有编辑时调用，实际影响为零。

---

### B9 未知参数被静默吞掉

**根因**（`Program.cs`）
- `Main` 的解析循环收到任意 `--x` 都塞进 `Args`，不校验；
- `ParsePolicy` 的 `_ => Policy.Merge`；
- `GateMode` 的 `_ => ContentGate.Mode.Warn`。

后果：`--on-modified=our`（少个 s）→ 想要「整份换成我们的」，实际走「只叠文本」；
`--gate=enforce` 拼错 → 内容闸静默降级成 warn。

**改法**

```csharp
// 单一清单，usage 文本从它生成，自检也拿它比对
private static readonly string[] KnownFlags = {
    "sd-test","dat-test","data-test","lang-test","flag-test","redact-test","gate-test",
    "markup-test","undo-test","legacy-test","baseline-test","writetest","help",
    "gate","on-modified","locale-only","keep-markup","yes","restore-ggpk","restore-backup",
    "restore-baseline","backups","status","simulate-fail","allow-no-rollback","recover-test",
    "bundle-name-test","args-test",
};
```

```csharp
// 解析循环内
if (!KnownFlags.Contains(key, StringComparer.OrdinalIgnoreCase)) {
    Console.WriteLine($"未知参数：--{key}");
    var near = KnownFlags.Where(f => Distance(f, key) <= 2).OrderBy(f => Distance(f, key)).FirstOrDefault();
    if (near is not null) Console.WriteLine($"  你是想写 --{near} 吗？");
    Console.WriteLine("  用 --help 看全部参数。未知参数一律拒绝，避免「拼错了却按默认值跑」。");
    return 2;
}
```

```csharp
private static Policy? ParsePolicy(string? s) => (s ?? "").Trim().ToLowerInvariant() switch {
    "" or "merge" => Policy.Merge,
    "ours" or "mine" => Policy.Ours,
    "original" or "orig" or "restore" => Policy.Original,
    "skip" or "off" => Policy.Skip,
    _ => null,
};
// 调用点：var p = ParsePolicy(Args.GetValueOrDefault("on-modified"));
//         if (p is null) return Fail($"--on-modified 的取值不认识：…（可选 merge/ours/original/skip）");
```

`GateMode` 同样返回 `ContentGate.Mode?`，null → 报错退出 2。

**验收**：`--args-test`（不打开容器，放在 `LoadBundle()` 之前）
1. 把 usage 文本重构成 `UsageText()` 返回 string（`PrintUsage()` 只负责打印），
   正则抓 `--([a-z0-9-]+)`，断言集合 ⊆ `KnownFlags`；
2. 反向断言 `KnownFlags` 每一项都在 usage 里出现 —— 加参数忘写文档会红；
3. 断言 `--on-modified=our`、`--gate=enforce`、`--gat=warn` 三者返回 2 且不回落到默认值。

**风险**：老用户脚本里若有未登记参数会突然失败。缓解：`--args-test` 的反向断言
保证 `KnownFlags` 不会漏；发版说明里列出「未知参数现在会拒绝」。

---

## 3. Batch B / v0.5.5

### B2 备份轮转发生在写第一个字节之前

**根因**：`Safety.cs:259` `Create()` 里 `Prune(root, keep:5)`。
第 6 次安装一开始就删最老那份；若本次最后什么都没写（都已是新版），
`DropIfEmpty` 又会删掉空目录 —— 净效果是白丢一个还原点。

**改法**

```csharp
public static BackupSet? Create(string containerPath) {
    ...
    Save(s);
    return s;                       // 删掉这里的 Prune
}

/// <summary>写入结束后调用。没产生备份就只清理空壳；产生了才参与轮转。</summary>
public static void Settle(BackupSet s) {
    if (s.Items.Count == 0) { DropIfEmpty(s); return; }
    Prune(Path.GetDirectoryName(s.Dir)!, keep: 5);   // 内部已有 pin 外部内容的逻辑
}
```

`Program.cs:2626`：`BackupStore.DropIfEmpty(backupSet)` → `BackupStore.Settle(backupSet)`。
`2921` / `3044` 两条还原路径没有 `backupSet`，不受影响。

**验收**：`--backup-test`
1. 造 5 份备份 → `Settle` 第 6 次（带 1 条 item）→ 剩 5 份且最老一份被删；
2. 造 5 份备份 → `Settle` 第 6 次（0 条 item）→ 仍是 5 份，一份都不能少；
3. 第 6 份的 item 标 `Flavor="外部改过"` → 轮转必须保留它，即使它是最老的。

**风险**：失败早退路径不再轮转，备份目录可能慢慢变大。可接受 ——
备份只在「原内容不是我们生成的」时产生，本身很少。

---

### B4 ggpk 恢复只处理第一份日志

**根因**：`GgpkUndo.cs` `Recover()` 里 `break`。断电发生在「回滚过程中」会留下两份
未完成日志，只退一份。

**改法**

```csharp
var done = 0; var failed = new List<string>();
foreach (var d in Directory.GetDirectories(root).OrderByDescending(x => x, StringComparer.Ordinal)) {
    var m = ReadMeta(d);
    if (m is null || m.Phase != "writing") continue;
    if (!string.Equals(Path.GetFullPath(m.Target), Path.GetFullPath(ggpkPath), StringComparison.OrdinalIgnoreCase))
        continue;                                   // 别的客户端的日志，不许动
    var err = ApplySnapshot(m, d, out var n);
    m.Phase = err is null ? "rolledback" : "writing";
    SaveMeta(d, m);
    if (err is null) { try { Directory.Delete(d, true); } catch { } done++; r.RestoredFiles += n; }
    else failed.Add($"{Path.GetFileName(d)}：{err}");
}
r.Recovered = done + failed.Count > 0;
r.Complete = failed.Count == 0;
```

**验收**：`--undo-test` 扩两例
- 造两份 `Phase="writing"` 快照覆盖不同偏移 → 两份都必须写回；
- 其中一份 `Target` 指向另一个文件 → 那个文件必须**不被触碰**。

---

### B5 撤销日志的偏移没有上界

**根因**：`ApplySnapshot` 只查 `cnt <= 64MB`，不查 `off + cnt` 是否落在文件内。
日志被截断/写坏时会写到文件尾之外，**追加垃圾还报成功**。

**改法**

```csharp
if (off < 0 || cnt <= 0 || cnt > (64 << 20) || off > m.OrigLength - cnt) {
    errs.Add($"日志记录越界（偏移 {off}，长度 {cnt}，原文长度 {m.OrigLength}）");
    break;
}
```

用 `off > OrigLength - cnt` 而不是 `off + cnt > OrigLength`，避开 long 加法溢出。

**验收**：`--undo-test` 再扩一例 —— 手工把一条记录的 `off` 改成 `OrigLength - 1`，
`cnt` 改成 8，断言恢复失败、错误消息含「越界」、目标文件长度不变。

---

### B7 备份读回的 sha 校验是恒等式

**根因**：`Safety.cs:364` `return Sha256(b) == it.Sha ? b : b;` 两个分支一样。

**改法**：不改「照样返回」这个决定（那可能是用户唯一的副本），但必须让人知道。

```csharp
public static byte[]? Read(BackupSet s, BackupItem it, out bool shaOk) {
    var p = Path.Combine(s.Dir, it.File);
    if (!File.Exists(p)) { shaOk = false; return null; }
    var b = File.ReadAllBytes(p);
    shaOk = Sha256(b) == it.Sha;
    return b;
}
```

`Program.cs:2891` 的调用点：

```csharp
var bytes = BackupStore.Read(set, it, out var shaOk);
if (bytes is null) { ... 现有分支 ... }
if (!shaOk) {
    Console.WriteLine($"  ! {it.Path}：备份文件与记录不符（磁盘损坏或手工改过）");
    Console.WriteLine($"    记录 sha {it.Sha[..12]}，实际 {Sha(bytes)[..12]}；仍按文件当前内容写回");
    suspect++;
}
```

整轮结束后若 `suspect > 0`，打印汇总并要求 `--yes` 才继续写（默认中止）。
理由：损坏的备份写进客户端，比不写更糟，但这确实是「最后一根稻草」，
所以不直接拒绝，只要求确认。

**验收**：`--backup-test` 第 4 例 —— 篡改备份文件一个字节，断言
`shaOk=false` 且未带 `--yes` 时整体返回非 0。

---

## 4. Batch C / v0.5.6

### B8 `SplitLines` 只认「BOM + CRLF」，提示指向错误方向

**根因**：`Program.cs:1552`。无 BOM 抛「不是 UTF-16LE 带 BOM」还行；LF-only 但有 BOM
时整份文件并成一行 → 所有 `r.No` 越界 → 判 `Unknown` → 提示「结构与数据包不符」。
用户会去怀疑数据包版本，而实际是行尾格式。

**改法**（只改诊断，不改行为 —— 仍然拒绝写入）

```csharp
// Detect 返回 Unknown 之前做一次结构预检，把原因记到 SdEntry
private static string WhyUnknown(byte[] cur, SdEntry e) {
    if (cur.Length < 2 || cur[0] != 0xFF || cur[1] != 0xFE) return "文件不是 UTF-16LE 带 BOM";
    var s = Encoding.Unicode.GetString(cur, 2, cur.Length - 2);
    if (!s.Contains("\r\n") && s.Contains('\n')) return "行尾是 LF，本工具只支持 CRLF";
    var n = s.Split(new[] { "\r\n" }, StringSplitOptions.None).Length;
    if (n != e.BaseLines) return $"行数 {n} 与基线 {e.BaseLines} 不符";
    return "内容与原文、译文、对照三种形态都不匹配";
}
```

`Unknown` 的输出处（`Program.cs:1880`、`1908`）把 `WhyUnknown` 拼进提示。

**验收**：`--sd-test=<file>` 加一个开关 `--fixture=lf`，把一份词缀文件转成 LF 后跑，
断言退出码 5 且输出含「行尾是 LF」。

---

## 5. 测试基建

### 新增文件

```
fixtures/recover/cases.json        B1 的 5 个场景（声明式，可增补）
fixtures/recover/README.md         每个场景构造了什么、断言什么
tools/roundtrip_test.py            B6：patch_bytes 零编辑往返
tools/tests/bundle_name_test.cs    不单独建工程，编译进 dropper，由 --bundle-name-test 调
```

### 接入 `tools/run_tests.py`

```python
("tools/roundtrip_test.py", [], "roundtrip_test"),      # Python 侧，可 SKIP
```

```python
for name in ["gate-test", "redact-test", "markup-test", "legacy-test",
             "undo-test", "baseline-test", "recover-test",
             "bundle-name-test", "args-test", "backup-test"]:
```

`run_tests.py` 已经做到「按退出码判定、失败打完整输出」，新增项不用改框架。
`--recover-test` 必须在 `Main` 里**先于** `LoadBundle()` 执行 —— 否则自检会去打开
真的 74 GB 客户端（`--gate-test` 踩过这个坑）。

### 自检顺序

`--args-test` 放第一个执行：它验证「参数解析本身没退化」，
后面的用例都依赖参数被正确识别。

---

## 6. 排期

| 日期 | 内容 | 出口条件 |
|---|---|---|
| D1 上午 | B1 代码 + `--recover-test` 五例 | 五例全绿，且把 journal 手工改坏能复现旧行为 |
| D1 下午 | B3 + B6 + B9 | 三个新自检全绿 |
| D2 上午 | 全量 `run_tests.py --build`，发 v0.5.4 | 6+4 项全 PASS，脏树为空 |
| D2 下午 | B2 + B4 + B5 | `--backup-test` 四例、`--undo-test` 三例全绿 |
| D3 上午 | B7 + 汇总消息 | 篡改备份能被拦 |
| D3 下午 | 发 v0.5.5 | 同上 |
| D4 | B8 + usage 文案 | `--sd-test --fixture=lf` 绿 |
| D4 | 发 v0.5.6 | — |

### 提交纪律

- 一个缺陷一个 commit，前缀 `fix(readpath): B<n> <一句话>`；
  body 三段：**现象 / 根因（文件:行）/ 怎么验证的**。
- 不混提交：B1 的 `RecoverResult` 改字段会被 B4 复用，但字段新增与 B4 的
  `break` 移除分两个 commit，出问题能单独 revert。
- 每个版本发版前：`python tools/run_tests.py --build` 全绿 + `git status --porcelain` 为空
  （`release.py` 已有这道闸）。

---

## 7. 兼容性与回退

| 变更 | 兼容性 | 回退 |
|---|---|---|
| journal 新增 `Attempts`/`LastErrors` | `System.Text.Json` 默认忽略未知字段，双向兼容 | 单 commit revert |
| `RecoverResult` 新增字段 | 仅本文件与 `Program.cs` 使用 | 同上 |
| 回滚失败退出码 3 | 老脚本会中断；`--yes` 可覆盖 | 去掉 `return 3` 一行 |
| bundle 名格式变化 | 索引里只是字符串；旧的 `zh{14位}` 仍能被 `Prefix` 匹配到 | 单 commit revert |
| 未知参数拒绝（退出码 2） | 有脚本依赖「吞掉未知参数」的话会失败 | 同上 |
| `patch_bytes` 零编辑返回原文 | report 字段 `appended_bytes` 补 0，结构不变 | 同上 |
| `BackupStore.Read` 签名加 `out` | 只有 1 个调用点 | 同上 |

**不可回退的一点**：v0.5.4 之后，回滚失败不会再自动进入菜单。
这是有意的行为收紧，回退它等于回退 B1 的价值。

---

## 8. 明确不做的部分

这些都属于 P0，会改写入协议或需要新机制，塞进本计划会让每个 commit 都难以单独验证：

- **P0-1 全量路径收敛**：本批只在 `RollbackFiles` 的 `NewBundles` 上堵了穿越
  （因为正在改这个函数）。`IndexBackup` 绝对路径、`GgpkUndo.meta.Target`、
  journal 整体防篡改（HMAC/DPAPI）都留到下一轮。
- **P0-2 并发互斥**：B3 只是让「同一秒两次」不再互相清空文件，
  没有解决「两个实例同时写索引」。互斥量是独立的一项。
- **P0-4 fsync 顺序**：`SyncLog` 的 150 ms 节流与数据落盘顺序无关，
  这需要改写入协议，不是 bug fix。
- **B6 的根治**：真正的修法是「产物必须能由原文 + 编辑推导出来」，
  B6 只是让零编辑不动文件。彻底的产物校验属于内容闸 G5 的范畴。

---

## 9. 一句话总结

B1 是本批唯一「用户已经在受害但没人知道」的问题（假成功 + 删现场），
B3 是唯一「可能复现历史事故」的问题，B9 是唯一「会让人按错误配置跑完整轮」的问题。
这三条做完，读写链路的失败路径才算有了观测点；其余六条是把它做扎实。
