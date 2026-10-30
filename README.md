# poe-zh · 繁中改良汉化

不重建整套汉化，只修官方繁中里**读着别扭、容易误读、机制算错**的地方，
并且这套东西能在每个新赛季活下来。

核心原则：**不动原文，只维护一份按 key 索引的覆盖清单。**

源码仓库：<https://github.com/kimihiro233/poe-zh-patcher>。
仓库**按字节存取**（`.gitattributes` 里 `* -text`），克隆后别开换行转换——否则补丁载荷变形、
`MANIFEST.sha256` 校验必然失败。`releases/*.zip`、`dist/`、`raw*/`、`pairs/` 等体积大或可重新生成的内容不入库。

---

## 先读这三个

- `REPORT.md` —— 本轮结论：问题分类、真实案例、修正方案、可行性判断
- `DATA-FINDINGS.md` —— 逆向细节：`.datc64` 布局、statdescriptions 格式、可复现流程
- `notes/MEMORY.md` —— 项目记忆：用户约定、已定死的规则、踩过的坑（改任何文案前先看）

---

## 目录

```
├─ VERSION                译文版本号（打 tag 的依据）
├─ game.lock.json         绑定的游戏版本 + 1961 个文件的 sha256 基线指纹
├─ REPORT.md              体检报告与修正方案（给人看）
├─ DATA-FINDINGS.md       二进制格式与流程（给下个赛季的自己看）
├─ notes/                 项目记忆：MEMORY.md（长期规则）+ YYYY-MM-DD.md（每日日志，只追加）
├─ tools/
│  ├─ gameinfo.py         锁游戏版本：客户端版本号 + 语言文件/词缀文本指纹
│  ├─ check_fixes.py      升级体检：旧修正稿在新语料里还成不成立
│  ├─ dat64.py            .dat/.datc64 解析器，无 schema 依赖
│  ├─ datwrite.py         .datc64 字符串重写器（追加式，三重自检）
│  ├─ patch_dat.py        修正稿 → out/patch/*.dat + manifest.json
│  ├─ patch_sd.py         修正稿 → out/patch-sd/*.txt + manifest-sd.json（整文件替换）
│  ├─ release.py          打版本快照到 releases/v<ver>/（zip 里额外带补丁器 exe）
│  ├─ extract_pairs.py    成对抽取 EN/ZH 的 .dat 文本
│  ├─ parse_statdesc.py   解析词缀/技能描述模板（纯文本）
│  ├─ check_sd_syntax.py  词缀文件语法体检 + 「客户端被改过」指纹（--canon 比对基线）
│  ├─ check_variants.py   中英变体条件对不上的块：对照版会不会把英文挂错行
│  ├─ check_placeholders.py 译文占位符漏失按后果分级（{n} 丢了 / % 丢了 / 只丢换行）
│  ├─ sd-sha-ledger.json  历代产物 sha 的持久账本（只增不减，--check 归一化靠它）
│  ├─ markup.py           标记层/文本层分离与合并（三分法，写回时的唯一口径）
│  ├─ analyze.py          体检：占位符/方向词/术语/简体字/长度
│  ├─ glossary.py         从对照数据反推术语表
│  ├─ dumper/             C# 导出器（LibGGPK3 + oo2core）
│  ├─ patcher/            C# 写回器（status / apply / restore，只认 ggpk）
│  ├─ dropper/            C# 拖放式补丁器（ggpk 与 Bundles2 双形态，打包成 dist/*.exe）
│  ├─ shared/             两个 C# 补丁器共用：bundle 归并、语系切换（languages.dat）、国旗（uiimages1.txt）
│  ├─ build_bundle.py     patch_sd 产物 → 内嵌数据包 tools/pack/bundle.gz
│  ├─ b2probe/            Steam/WeGame 索引探针与完整性体检（--check）
│  └─ ggpkdiag/ ggpkfix/  ggpk 索引诊断与悬空记录修复
├─ out/                   全部产物（out/patch、out/patch-sd 为待写入的补丁）
├─ releases/              版本快照（v0.1.0、v0.2.0…）
└─ pairs/ raw_sd/ raw/    原始导出（可随时重建，不必备份）
```

## 游戏版本 ↔ 译文版本

一套译文只对**做过它的那一版客户端**负责。游戏一更新，英文原文可能改、行号可能移、
表结构可能换，同一份 diff 套上去就写错行。所以每一版都锁两层：

| 层 | 内容 | 用途 |
|---|---|---|
| 版本号 | 客户端 `3.29.3b` / patch `3.29.3.3`（从 `logs/LatestClient.txt` 取） | 给人看，一眼对上 |
| 内容指纹 | 1916 个繁中语言文件 + 45 个词缀 txt 的 sha256 聚合 | 给机器判，热修不改版本号也能抓到 |

```bash
python tools/gameinfo.py          # 锁当前游戏 → game.lock.json
python tools/gameinfo.py --check  # 只比对：版本/内容变了就返回 1（开局体检用）
python tools/gameinfo.py --diff   # 列出到底哪些文件变了
```

`game.lock.json` 必须在**干净的游戏**上取（没有本项目补丁）。若检测到 ggpk 还留着旧补丁，
`gameinfo.py` 会拒绝锁基线——否则指纹里混进我们自己的改动，就再也判断不了游戏是否更新过。

`--check` 比对时会先把**本项目补丁造成的差异剔除**，所以打完补丁再体检不会误报「游戏更新了」：
它看的是 manifest 里记录的 `sha256_patched`，命中就记作「我方改动」而不是「游戏侧变动」。

补丁生成时，`game` 段被写进 `out/patch/manifest.json`；`patcher apply` 会拿它和当前客户端比对，
**不一致直接中止（退出码 4）**，要强上得显式加 `--force`。加上原有的三态门禁
（原始/已打补丁/未知），版本错配和内容错配各有一道闸。

发布包命名带游戏版本：`poe-zh-v0.4.0+game3.29.3b.zip`。

| 译文版本 | 游戏版本 | 状态 |
|---|---|---|
| v0.1.0 | 3.29.3b（回溯推定，当时未记录） | 分析基线，未动游戏文件 |
| v0.2.0 | 3.29.3b（回溯推定，当时未记录） | 回写链路打通，写入 5 表 / 10 处 |
| v0.3.0 | 3.29.3b / patch 3.29.3.3（已锁） | 版本绑定，指纹 `1962f349…` |
| v0.4.0 | **3.29.3b / patch 3.29.3.3** | 词缀文本入补丁（26 文件 / 161 处）、标记层三分法、繁中 `{0]` 笔误、补丁器支持升级与异地客户端 |
| v0.4.2 | **3.29.3b / patch 3.29.3.3** | 修 3.29 新语法：技能类 19 个文件（aura/buff/curse/minion/skill…）以前整份读成 0 块、从没进过补丁，全量对照补到 42 文件 / 26635 行 |
| v0.4.3 | **3.29.3b / patch 3.29.3.3** | 占位符漏失补回（`{n}`/`%` 丢了，三套产物各 181 处）、对照版按 `\n` 分组穿插、补丁工具自带语系启用（不再需要 PoeChinese3）、国旗换五星红旗、产物 sha 持久账本 |
| v0.4.4 | **3.29.3b / patch 3.29.3.3** | 补丁器补齐 v0.4.3 的穿插规则：25 个文件在全新客户端上被判「生成内容 sha 不符」整体拒写（978 行受影响）；新增 `--data-test` 数据包自检 |
| v0.4.5 | **3.29.3b / patch 3.29.3.3** | 语系切换从没生效过：换成官方做法（对调语言名而非代码）、修好「无需改动」时跳过语系的门槛、国旗改成对调图标名不再写死坐标；新增 `--locale-only` / `--lang-test` / `--flag-test` |
| v0.4.6 | **3.29.3b / patch 3.29.3.3** | 反复打补丁会让客户端 `Buffer Underflow`：自建 bundle 记录被复用后物理文件被覆盖、offset 与 UncompressedSize 全部作废。改成每次写入都换一条唯一命名的新 bundle，不再碰历史记录；写完关掉索引重开逐个回读；新增 `--bundles` 体检 |
| v0.4.7 | **3.29.3b / patch 3.29.3.3** | 安全底线：写入前备份 `_.index.bin` 并落 journal，任何一步不过就整份回滚；断电/强杀下次启动自愈；被覆盖的原字节全部留档（`--backups` / `--restore-backup`）；新增 `--on-modified=merge\|ours\|original\|skip` 处理「别人改过的文件」 |
| v0.4.8 | **3.29.3b / patch 3.29.3.3** | 老式 `Content.ggpk`（74 GB）也能整份退回：写入前逐块记下被覆盖的原字节（实测 43 块 / 20.6 MB），失败自动回滚、断电下次启动自愈、`--restore-ggpk` 手动退回；不再需要「验证游戏完整性」或重下 74 GB |
| v0.4.9 | **3.29.3b / patch 3.29.3.3** | 旧版补丁器写的对照版**能认出来并接管**：判定从「整块 sha 相同」改成「每一行归到哪一版」，以后再改对照拼法也不会有一批老客户端变成「未知」；修复 `--on-modified=skip` 提示说能用、实际仍中止 |
| v0.5.0 | **3.29.3b / patch 3.29.3.3** | **基准**：每次写入以「该版本下第一次看到的那份字节」为算式输入，结果与改过几次无关，反复打补丁不会累积漂移；`--baseline` / `--restore-baseline` 可整份退回原文；备份/基准目录建不出来时**拒绝写入**（以前静默吞异常照样写） |

## 游戏更新后（换赛季）

```bash
GGPK="C:/Program Files (x86)/Grinding Gear Games/Path of Exile/Content.ggpk"

# 0) 先把上一版补丁撤掉，回到干净客户端
./tools/patcher/bin/Debug/net8.0/patcher.exe restore "$GGPK" out/patch/manifest.json

# 1) 体检：版本变了哪些文件变了，一目了然
python tools/gameinfo.py --diff

# 2) 重新锁基线（旧锁自动留档到 out/game-lock-history/）
python tools/gameinfo.py

# 3) 重导 → 重解析 → 重跑体检（修正稿会显示哪些 key 已经失效）
cd tools/dumper && dotnet build && ./bin/Debug/net8.0/dumper.exe pairs "$GGPK" ../../pairs diff_pairs.csv
cd ../.. && python tools/extract_pairs.py pairs out/pairs.csv
python tools/parse_statdesc.py raw_sd out/statdesc.csv

# 4) 升级体检：哪些修正稿还能照用、哪些英文原文改了要重做
python tools/check_fixes.py
#    pending 还没打 / applied 已生效 / drift 原文变了要人工重判 / gone 条目没了

# 5) 重生成补丁 → 写入
python tools/patch_dat.py
./tools/patcher/bin/Debug/net8.0/patcher.exe apply "$GGPK" out/patch/manifest.json
```

`patch_dat.py` 会逐表把 `pairs/zh` 的 sha256 和 `game.lock.json` 里记的值对齐，
对不上就拒绝生成——所以「拿着旧补丁打新版本」在工具层面是走不通的，不用靠自觉。


## 一次完整的重跑

```bash
# 1) 导出（.NET 8，需要 oo2core.dll 与 Content.ggpk 路径）
cd tools/dumper && dotnet build
./bin/Debug/net8.0/dumper.exe cmp   "<...>/Content.ggpk"
./bin/Debug/net8.0/dumper.exe pairs "<...>/Content.ggpk" ../../pairs diff_pairs.csv
./bin/Debug/net8.0/dumper.exe raw   "<...>/Content.ggpk" ../../raw_sd statdescriptions

# 2) 抽取
cd ../..
python tools/extract_pairs.py pairs out/pairs.csv
python tools/parse_statdesc.py raw_sd out/statdesc.csv

# 3) 体检
python tools/analyze.py  out/statdesc.csv out/report-statdesc.md
python tools/analyze.py  out/pairs.csv    out/report-ui.md
python tools/glossary.py out/statdesc.csv out/glossary.csv
```

## 拖放式补丁器（给玩家用）

`dist/PoE-zh-Patcher.exe` 是单文件版（约 38 MB，内嵌数据包，不需要装 .NET）。
拖 **两种** 目标都行，程序自己认：

| 安装形态 | 拖什么 | 内部结构 |
|---|---|---|
| 官方独立端 | `Content.ggpk` | 单个 ggpk 文件 |
| Steam / WeGame 国服 | `Bundles2` 目录（或其中的 `_.index.bin`） | `_.index.bin` + 一堆 `*.bundle.bin` |

也可以直接双击，会自动找默认安装路径（含 Steam 库和 WeGame 目录）。

```
 [1] 纯译文        —— 日常游玩，161 处修正
 [2] 对照版        —— 修正过的 161 行下方显示英文原文
 [3] 全量对照      —— 所有词缀下方显示英文原文
 [4] 还原官方原文  —— 撤销全部修改（连语系一起还原成英文）
 [t] 结构演练      —— 只验读写通道，不改内容（换客户端/换平台时先跑这个）
 [c] 采集诊断包    —— 只读，把这份客户端的原文打包给作者
 [b] 备份与还原    —— 看安装前的原始内容，整份写回去（ggpk 是整份退回）
```

两种形态的差别只在落盘方式：ggpk 每次写文件顺带存索引；`Bundles2` 是把改动
写进新的 `LibGGPK3/N.bundle.bin`，最后统一存一次 `_.index.bin`。

## 安全底线：任何一步失败都退回写前状态

两条硬规则，写在代码里，不是写在说明里：

1. **客户端任何时刻都必须是一份游戏读得出来的完整文件。** 不允许出现「一半新、一半旧」
   或者「索引写了一半」这种中间状态。原始内容永远留着一份。
2. **遇到被别人改过的文件，只有三条路**：把译文叠上去、换成我们的版本、退回官方原文。
   三条都做不到就不动它 —— 保持它现在能被游戏读到的样子。

### 第一层：写第一个字节之前先拿到退路

两种客户端，两种退路，规则是同一条：**拿不到退路就一个字节都不写**。

`Bundles2`（Steam / WeGame / 带 Bundles2 目录的客户端）的索引只有 21 MB，整份复制：

```
写第一个字节前   →  复制 _.index.bin（21 MB）到 .poe-zh/index-<时间>.bin，落 txn.json
写文件 / 存索引  →  关闭索引、重新打开、逐个回读校验
校验全过         →  提交：删 txn.json、清掉没被引用的旧 bundle、备份留最近 5 份
任何一项不过     →  索引换回备份 + 删掉本次新建的 bundle → 客户端回到写入前
```

`Content.ggpk`（官方独立端）是 74 GB 的单文件，整份复制不现实。但它也不需要：

```
写第一个字节前   →  打开 .poe-zh/ggpk/<时间>/undo.bin
每次写文件/存索引 →  先把「这段即将被覆盖的原字节」追加进日志并刷盘，再真正写
校验全过         →  提交：日志改标 committed，留最近 3 份当后悔药
任何一项不过     →  把日志里的原字节逐块写回去，每块写完立刻读回来比对
```

为什么这样够用：实测这份 74,730,084,326 B 的客户端，装 1 个文件、整份索引落盘，
真正被改写的只有 **43 块 / 20.6 MB**，而且**文件长度一个字节都没变** —— 全是原地
覆盖。也就是说被覆盖掉的旧字节在这台机器之外没有第二个副本，只要写之前记下来，
21 MB 就能换回 74 GB。往文件尾部追加的数据本来没覆盖任何东西，回滚后留着也不影响
游戏读取（没有记录指向它），所以不截断文件，少一次「截断到一半」的风险。

进程被强杀或者断电呢？`txn.json` / `undo.bin` 就是「上次没走完」的证据：下次启动
（在打开容器**之前**）先看它们，有就无条件回滚，然后再照常干活。回滚只依赖
「备份文件 + 日志里的字节」这两样确定的东西，不推断任何中间状态，所以断电也能恢复。

> 宁可撤销一次本来成功的安装（重跑一次即可），也不能把一个没人验过的索引交给游戏。

### 第二层：被覆盖的内容留一份原字节

`.poe-zh/backup/<时间>/` 里存的是**覆盖前那一刻的字节**，manifest 记着
「容器内路径 → 文件名 + sha256 + 覆盖前状态（官方原文 / 全量对照 / 外部改过）」。
别的汉化改过的那种文件，这份字节在整台机器之外没有第二个副本，所以必须落盘。

```
PoE-zh-Patcher.exe <目标> --backups                    看有哪些还原点（整份 / 单个文件）
PoE-zh-Patcher.exe <目标> --restore-backup=latest      把某次备份的文件整份写回去（同样走事务）
PoE-zh-Patcher.exe <Content.ggpk> --restore-ggpk       把整个 ggpk 退回那次安装之前
```

菜单里按 `[b]` 也能做，整份和单个文件都在同一个列表里。文件级备份保留最近 5 份，
ggpk 整份还原点保留最近 3 份（每份约等于一次落盘改写的字节数，实测 20 MB 上下）。

**轮转有一个例外**：如果某一份备份里存着「被外部汉化改过」的内容，它不参与轮转。
那些字节在这台机器之外不存在第二份，删掉就是永久没了 —— 而轮转的初衷只是省磁盘。
每轮删除前会打印删的是哪一份、多大、为什么删，不留「备份自己消失」这种悬案。

`--restore-ggpk` 的适用前提是「装完之后没再更新过客户端」：还原只把当时被覆盖的
那几块写回去，如果中间游戏又更新过，写回去的就是两个版本的混合物。所以它会先报
时间戳和长度、要一次确认；加 `--yes` 可跳过确认（脚本用）。

### 第三层：遇到别人的内容，四选一

`--on-modified=` 决定「与数据包对不上」的文件怎么处置，默认 `merge`：

| 取值 | 行为 | 适用 |
|---|---|---|
| `merge` | 只把译文行叠上去，别人的标记/颜色/其它行一个字节不动；结构对不上就跳过 | 默认 |
| `ours` | 整份换成我们验过的字节（`.dat` 有内嵌成品）；原字节先备份 | 被别的汉化改坏了 |
| `original` | 退回官方原文：`.dat` 用内嵌原文整份换；statdesc 逐行撤掉我们写过的行，认不出的行保留 | 想彻底退出汉化 |
| `skip` | 不动，保持现在能被游戏读到的样子 | 只想翻能对上的 |

statdesc 没有内嵌原文字节（成品是「原文 + 译文」现算的），所以 `ours` 对它做不到 ——
这种情况会明确跳过并说明，不会写一份四不像。

`skip` 自己就会放行，不需要再叠 `--partial`；被跳过的那几个原样保留，游戏照常能读。

### 「旧版补丁器写的」不算「别人的内容」

对照版的拼法改过（早先是「英文整段堆在中文后面、只给第一行缩进」，现在是「逐组穿插」）。
如果拿整块 sha 去认，改一次拼法就会让一批老客户端全变成「未知」—— 明明是我们自己写的，
却整份拒写。

所以判定不看整块，看**行**：把原文、译文、对照、英文原文、上一版文本全部按 `\n` 拆成行收成一个池子，
文件里记录的每一格，块内每一行去标记、去首尾空白后都得落在池子里。

```
全部落得进去   → 这是我们自己某版本写的「原文 + 译文」排列，接管，整块换成目标
有一行落不进去 → 那行不是我们写的（别人的译文，或译文本身被改过），不接管
```

这条判据不认拼法、只认行，所以以后再改多少次排列方式，老客户端都还认得出来。
实测把 25 个早先写的文件一次接管成新格式，47/47 回读通过，客户端字节数一个都没变。

### 基准：让结果只取决于「目标」，不取决于「改过几次」

前三层解决的是一次写入失败。真正的问题是**反复写**：statdesc 里 96% 的行不参与翻译、
原样继承，一旦某次被改动，之后每一次都继承，越修越歪，而且完全没有提示。

所以引入「基准」—— 不是备份，是**算式里的常量**：

```
基准 = 该文件在这个游戏版本下「第一次被本程序看到」的完整字节
每次写入 = 基准 + 目标 flavor  →  产物
```

于是结果只取决于（基准, 目标 flavor, 数据包版本）三者：

| 性质 | 含义 |
|---|---|
| 幂等 | 同一个基准算多少次都是同一份字节 |
| 与历史无关 | 第 2 次和第 200 次的产物完全相同，不可能累积漂移 |
| 还原有确定目标 | 把基准原样写回去就是还原，不需要「原文」这个概念 |

**基准尽量存原文。** 第一次看到时如果客户端已经是我们的译文，会先从数据包还原回原文，
还原后 sha 必须等于数据包锁定的原文 sha 才作数 —— 这是判断「原文是否完好」的唯一真相。
对不上说明有我们没记录的地方被动过，那就退回存当前这份（次优，但至少是稳定常量）。

真客户端上实测（74,730,084,326 B，当时是「全量对照 42 / 已打补丁 5」）：

```
建基准      47 份，全部还原成原文（sha 校验通过才会标 Original），52.8 MB
→ 单语      47/47 回读通过，产物 sha 与发布产物逐字节一致（硬校验，非自证）
→ 全量对照  47/47 回读通过
→ 写回基准  42 份 statdesc 整份回到官方原文，状态变成「官方原文 42 / 已打补丁 5」
→ 再装回    47/47 通过，状态精确回到「全量对照 42 / 已打补丁 5」
```

基准按 `client_version + patch_version` 分目录，**永不覆盖、不参与备份轮转**。
补丁器升级不影响它（键是游戏版本，不是补丁器版本）；游戏一更新就换目录，
旧基准留在原处不会被误用。

```
PoE-zh-Patcher.exe <目标> --baseline                              看有哪些基准
PoE-zh-Patcher.exe <目标> --baseline --restore-baseline=<关键词>   把匹配的文件写回基准
PoE-zh-Patcher.exe <目标> --baseline --restore-baseline=statdescriptions   整套退回原文
```

菜单里按 `[k]`。所有路径都在 `<游戏目录>\.poe-zh\baseline\`，
与 exe 放哪、叫什么、什么版本都无关 —— 补丁器是单文件、会被覆盖，备份不能跟它走。

体积：基准约 50 MB（42 份 statdesc + 5 个 dat），一个游戏版本一份，之后不再增长。
备份只在**内容真的被改变**时才产生（内容没变的「搬 bundle」重写不备份），
每份约 50 MB、留 5 份；ggpk 快照约 21 MB、留 3 份。
最坏合计约 350 MB —— 都在 `<游戏目录>\.poe-zh\` 下，删掉不影响游戏本身。

### 拒绝写入的五种情形

| 情形 | 处理 |
|---|---|
| 拿不到还原点（索引复制失败 / 磁盘满 / 库不接受接管 ggpk 写入通道） | 中止，0 字节写入（`--allow-no-rollback` 才放行） |
| **备份或基准目录建不出来（游戏目录没写权限 / 磁盘满）** | **中止，0 字节写入**（以前这里是静默吞异常照样写） |
| 生成内容 sha 与发布产物不符 | 这一个跳过；最终没通过就整份回滚 |
| 游戏版本与数据包不一致 | 中止（`--partial` 则只处理对得上的） |
| 目标文件在客户端里缺失 / 索引里指向已删的 bundle | 不动，提示验证游戏完整性 |

**关于游戏是否在运行**：`PoE-zh-Patcher.exe`（旧版补丁器）启动时会枚举进程，发现
`PathOfExile*` 就直接拒绝；本程序（dropper）**没有做进程检测**，靠的是以
`FileShare.Read` 独占写权限打开容器 —— 游戏正在运行时打开很可能直接失败，
报错落在「打开不了容器」那一步，不会写坏任何东西。做法上先退出游戏再打补丁。

### 实测

在真客户端（WeGame 3.25.1d 的 `Bundles2`）上跑的三组：

| 场景 | 结果 |
|---|---|
| 正常安装 24 个文件 | 关闭索引重开回读 24/24，提交，journal 已删 |
| `--simulate-fail=5`：故意让第 5 个文件失败 | 前 4 个已写 → 整份退回 → `_.index.bin` 的 sha256 与写入前**逐字节一致**，新建 bundle 已删，`--status` 正常 |
| 手工造 `txn.json` + 破坏索引前 256 字节 | 下次启动自动回滚，索引 sha 恢复成原值，提示「直接重跑即可，不用验证完整性」 |

`--simulate-fail` 是留给排障的：它能在不写坏客户端的前提下，把退回流程完整走一遍。

同一台机器上还有一份官方独立端的 `Content.ggpk`（74,730,084,326 B），在它上面跑的四组：

| 场景 | 结果 |
|---|---|
| `--set=translated --only=atlas_relic --partial`（写 1 个文件） | 日志 43 块 / 20.6 MB，回读 1/1 通过，文件长度不变 |
| `--restore-ggpk=latest` | 43 块原字节写回、逐块读回比对一致；`--status` 回到 `全量对照 17 / 未知 25 / 已打补丁 5`，与写入前完全相同 |
| `--simulate-fail=2`（写 2 个文件后故意失败） | 索引已落盘 → 整份退回 → 状态回到写入前，日志目录自动清空，下次启动不再误报「上次没走完」 |
| `--struct`（47 个文件原样写回） | 47/47 回读一致，文件长度 `74,730,084,326 → 74,730,084,326 B` |
| `--set=dualall`（接管 25 个旧版对照） | `未知 25` → `旧版对照 25`，一次写成 `全量对照 42 / 已打补丁 5`，重开后回读 47/47；长度仍是 `74,730,084,326 B`；再跑一次 `--plan` 显示需要改动 0 个 |

`--undo-test` 是不依赖游戏的回归自检：在临时目录造一份「同结构」的文件，把
「原地覆盖 / 多块重叠 / 写到末尾之外」三种写入混在一起，验证「进程被杀后自愈」
「提交后按快照退回」「只读会话不留空日志」三条路。

`--legacy-test` 也是不依赖游戏的：用数据包里的三态文本现场拼出 11 种情况，验证
「6 种排列都认得出」和「译文被改一个字 / 混进别人的译文 / 整份都是原文，必须认不出」。

`--baseline-test` 验证基准的四条性质（缺一条，「结果与改过几次无关」就不成立）：
存一次读一次字节一致、同版本同路径**绝不覆盖**、换游戏版本读不到旧基准、
**基准文件被改坏（sha 对不上）就拒绝使用**——宁可从当前重算，也不拿污染的常量去算。

`--redact-test` 钉的是采集包脱敏的规则：11 条真实日志行断言，包括账号行要连时间戳
一起保留、`AccountName` 这种词尾粘连写法也要命中、已脱敏的 `<user>` 不许被二次处理。

```bash
python tools/doc_check.py     # README 里的数字与产物对得上吗
```

`doc_check` 把 README 里能被产物算出来的数字（42 份 statdesc / 26635 行 / 47 个目标文件 /
`74,730,084,326 B` / 备份留 5 份 / 快照留 3 份）逐条绑到 `bundle.gz`、`game.lock.json`
和 `Safety.cs` 里的常量上，顺便检查 README 命令行里出现的每个开关是不是程序真认得。
对不上就返回 1 —— 文档漂移从此会当场变红，而不是等用户按错的说明操作。

### 语系：为什么以前打完补丁还要跑一次 PoeChinese3

客户端能不能看到中文，**不取决于我们往 `metadata/statdescriptions` 里写了多少中文**，
而取决于 `data/languages.dat` —— 游戏语言列表就是这张表的 11 行。

客户端按这一行的**语言名**去找 `data/<语言名>/` 下的文件（采集包里那条
`data/traditional chinese/uitalktext.datc64` 就是证据）；语言代码（`fr` / `zh-TW`）
只是配置值 `language=fr` 和国旗图标用的。所以 PoeChinese3 换的不是代码，是名字：

```
French 行              原本：French / Français        code = fr
Traditional Chinese 行  原本：Traditional Chinese / 繁體中文   code = zh-TW
        ↓ 把两行的「显示名 + 本地名」整对调
French 行              Traditional Chinese / 繁體中文   code 仍是 fr
Traditional Chinese 行  French / Français              code 仍是 zh-TW
```

换完之后语言列表第二项写着「繁體中文」，选它就去读 `data/traditional chinese/`
—— 官方繁中加上我们写进去的译文；配置里仍然记着 `language=fr`，用户配置一个字都不用改。
官方客户端本来就有繁中这一项，只是入口指在法语上，所以 PoeChinese3 才敢说
「只启用语系、其余文件均未修改」，游戏更新后也不用重打。

现在装补丁时**自动做这一步**（`[1]`/`[2]`/`[3]` 启用，`[4]` 还原时关掉），
已经是对应状态就跳过，不重复写；连「译文早就是最新、无需改动」的情况也会补做
（旧版补丁器打过的客户端就是这种状态）。结构对不上（多半是游戏更新过）就整段跳过并提示，绝不硬写。

实现见 `tools/shared/LanguageSwitch.cs`，patcher 与 dropper 共用；`--status` 会报一行「语系：…」。
只想单独处理这一步，用 `--locale-only`（还原用 `--locale-only=original`）。

> v0.4.4 及更早的实现是错的：交换的是 `fr` 与 `zh-TW` 两个**代码**，而且判定条件在真实文件里
> 永远不成立，`Detect` 一直返回「无法识别」直接跳过 —— 语系从来没被写进去过，所以游戏里一直是英文/法文。

### 国旗：语言列表里那一项不该是法国国旗

语系切到繁中之后，语言列表第二项的标签写着「繁體中文」，配图却还是法国国旗——因为
客户端是按**语言代码**去拼图标名的（`FlagIcons/<code>`，这里的 code 仍然是 `fr`）。

游戏图集里本来就有五星红旗：`zhCN`（简体中文）那一项。`art/uiimages1.txt` 是图集索引，
每个图标一行，末尾四个数是贴图上的矩形：

```
"…/FlagIcons/fr"   "…/FlagIcons/1.dds"    252 0 326 49     ← 法国国旗（3.25 版坐标）
"…/FlagIcons/zhCN" "…/FlagIcons/1.dds"    756 0 830 49     ← 五星红旗
```

fr 的坐标每个版本都不一样（3.25 是 252，3.29 是 840），所以**不能写死坐标**。
做法是把 `fr` 与 `zhCN` 两行的**图标名对调**：客户端去取 `fr` 时拿到的是写着 `zhCN`
的那一行（五星红旗），一个贴图字节都不用碰，也不依赖任何坐标，再换一次就回到原样。
这与 PoeChinese3 的做法一致（它换的是 `fr` ↔ `zhTW`，青天白日旗）。

实现见 `tools/shared/FlagIcon.cs`（同样 patcher 与 dropper 共用）：只在字节层替换那两行，
其余 600 万字节原样拷贝；`--status` 报一行「国旗：…」，装补丁时启用、`[4]` 还原时改回法国国旗，
已是目标状态就跳过。已知副作用：列表里「简体中文」那一项本来就是五星红旗，换完会有两面——
那一项没有对应的数据目录，选了只会回落到英文，不影响使用。

和手工链路的差别：

- **四个版本随意来回切**。运行时从容器当前内容按行改写目标行，不做整文件
  替换，所以「对照版 → 纯译文」不再需要先 restore；甚至上次切换到一半的
  混合状态也能逐文件识别并修好。
- 每个文件写入前用内嵌的 sha256 校验生成结果，写入后回读再校验一次。
- 状态未知的文件（多半是游戏更新了）会让整体中止，一个都不写。
- 写入前把「被覆盖的原字节」存到容器旁边的 `.poe-zh/backup/<时间戳>/`，整份还原点在 `.poe-zh/ggpk/<时间戳>/`。
- 需要 .datc64 的版本重做时：`python tools/build_bundle.py` 重新打包内嵌数据，
  再 `dotnet publish`（见下）。

```bash
# 命令行等价（脚本/无人值守用）
./dist/PoE-zh-Patcher.exe "$GGPK" --set=translated|dual|dualall|original
./dist/PoE-zh-Patcher.exe "$GGPK" --status
./dist/PoE-zh-Patcher.exe "$GGPK" --selftest   # 校验数据与机器文件对得上
./dist/PoE-zh-Patcher.exe "$Bundles2" --struct  # 结构演练：原样写回再读回校验
./dist/PoE-zh-Patcher.exe "$Bundles2" --collect # 采集诊断包（只读），异地客户端适配用
# $Bundles2 也可以直接换成 "…\Path of Exile"，程序会自己找 Bundles2/_.index.bin

# 重新打包：数据 -> 单文件 exe
python tools/build_bundle.py
cd tools/dropper && dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o ../../dist
```

### 平台边界（重要）

数据包是按**繁中客户端 3.29.3b** 的文件内容做的：每个目标文件都内嵌了「原文 /
纯译文 / 对照版 / 全量对照」四份 sha256。所以：

- Steam 繁中客户端：文件路径与内容都一致，四个版本随便切。
- **WeGame 国服（简中）**：`data/` 下只有 `data/simplified chinese/`，没有
  `data/traditional chinese/`，5 张数据表直接判缺失；词缀文本则因为版本或语言
  不同，sha 对不上，判「未知」。补丁器会整体拒绝写入 —— 这是正确行为。
  想验通道就在这种客户端上跑 `--struct`。

### 换平台前的自检清单

```bash
# 1) 结构演练（会临时改 index，跑完自己还原内容）
./dist/PoE-zh-Patcher.exe "$Bundles2" --struct
# 2) 索引完整性：越界记录应为 0，抽样读 20 个文件应全过
./tools/b2probe/bin/Release/net8.0/b2probe.exe --check "$Bundles2/_.index.bin"
```

`--struct` 会备份并复用 `Bundles2/LibGGPK3/` 下的自建 bundle；如果不放心，
先把 `_.index.bin`（约 20 MB）复制一份，出问题直接盖回去即可。

### 异地客户端适配：`--collect`

数据包只认自己做基线的那一份客户端。别人机器上的客户端版本/语言不同，sha 对不上时，
让他跑一次采集（**只读，不写任何游戏文件**）：

```bash
PoE-zh-Patcher.exe "<他的 Bundles2 或 Content.ggpk>" --collect [--out=目录]
# 或把目录拖到 exe 上，菜单里按 c
```

输出（默认在 exe 旁边，`poe-zh-collect-<时间戳>/` + 同名 `.zip`）：

| 文件 | 内容 |
|---|---|
| `report.txt` | 客户端版本、31 个目标文件的状态、对不上的文件的逐行对照 |
| `report.json` | 同上，机器可读 |
| `files/` | 状态对不上的那几个词缀文件原文（UTF-16），作者据此重做基线 |
| `client-log.txt` | 客户端日志开头 60 行（含版本号），逐行脱敏 |

报告里每个对不上的文件会给三样东西：

1. **行数差**（本文件 vs 数据包基线），判断是否整体增删了行；
2. **逐行核对**（原文 / 纯译文 / 对照 / 全量对照 / 其它 各多少行）；
3. **错位探测**：对不上的行去附近 ±40 行找同一段文本，给出最常见偏移。
   例如「1187 行能在附近找到，最常见偏移 +4 行（1159 行）」——说明客户端只是
   插入了 4 行，按偏移重映射就能套用。

作者拿到 zip 后可以：按 `files/` 重做该版本基线；或确认偏移后做一次重映射。

#### 采集包在发出前就脱敏了

采集包是发给作者的，发出去就收不回来。里面的真正风险不是游戏文件，是客户端日志 ——
它带登录账号、SteamID、以及把真名写在脸上的 `C:\Users\<用户名>\…`。这些东西对排查
毫无用处，对收包的人却是完整的身份线索，所以在**落盘之前**就摘掉，不靠人记得检查：

| 内容 | 处理 |
|---|---|
| 登录账号 / 会话 / 令牌 / 邮箱 | 整行删除，只留行首时间戳 + `[REDACTED]` |
| SteamID64（`7656` 开头的 17 位） | 换成 `<steamid>` |
| 用户目录里的用户名 | 换成 `<user>`，后面的路径保留（`AppData\Local` 这类有诊断价值） |
| 其它盘的绝对路径（仅日志） | 换成 `<path>` |
| 容器路径（`report.txt` / `report.json`） | 只给「盘符 · 层数 · 文件名」，不给全路径 |

顺序是先整行剔除再替换路径：反过来的话，一行里既带账号又带路径时，路径先被换成
`<user>`，整行反而「看起来干净」地留下了。

写出后会**再扫一遍**输出目录：还能扫到真实用户名或 SteamID，就**不生成 `.zip`**，
只留文件夹并打印命中位置。规则本身有回归自检 —— `--redact-test`，11 条真实日志行断言，
连「`AccountName` 这种词尾粘连写法也必须命中」都钉住了。

诊断价值不减：版本号、文件状态、逐行对照、日志里的引擎与加载行都在。少的只是「你是谁、装在哪」。

**先判「是不是原版」，再谈适配。** 拿到的文件可能已被第三方汉化/高亮补丁改过，
这时它不是基线而是别人的产物，照它的样子重建会把别人的改动继承下来。判定用：

```bash
python tools/check_sd_syntax.py out/collect-*/files --canon raw_sd out/syntax-check.md
```

三个「非原版」指纹：

| 指纹 | 含义 |
|---|---|
| **W2 裸 `<rgb(…)`** | 原版 45 个词缀文件里出现 **0 次**（只用 17 个命名标签），出现即非原版产出 |
| **W1 `markup` 空声明** | 行尾留着 `markup` 关键字，正文标记已被删光 |
| **11 语言同步变化** | 各语言处数相同 → 整份文件被程序批量重写，不是人工只改繁中 |

反过来，**括号/标签/占位符错误数与原版相同**（E1、E3、E4 逐项相等）说明语法没被改坏；
若对方错误数多于基线，才需要逐条修。

### 标记层三分法：只叠文本，不替别人改风格

引号内文本 = **标记层**（`<enchanted>` `<rgb(…)>{{ }}`）+ **文本层**（其余字符，含 `{0}`）。
译文只对文本层负责，写回时按三种情况处理：

| 底稿的情况 | 处理 | 理由 |
|---|---|---|
| 留着标记但换了（`<nemesismod>`→`<rgb(209,46,46)>`、新增高亮） | **沿用底稿，一个字节不动** | 那是别人设计的高亮体系，不是我们该管的事 |
| 标记连 `{{ }}` 一起被删光（原版有、这里寸草不生） | **按官方原样补回** | 连结构性的 `{{ }}` 都一起没，是机械替换的覆盖伤，不是风格选择 |
| 与官方一致 | 正常替换 | — |

`--keep-markup` 关掉第二行，退化成纯「只改文本」。判定与合并实现在
`tools/markup.py`（`merge()` 三分法 + `selfcheck()`），C# 侧同名实现在
`tools/dropper/Program.cs` 的 `Markup` 类，两边用例逐条对齐（`--markup-test`）。

两种落地方式：

```bash
# A. 命令行：以目标机的文件为底稿重建补丁（只叠文本，产出独立 manifest）
python tools/patch_sd.py --shell out/collect-XXXX/files --write
#    -> out/patch-sd-<目录名>/，sha256_original 记的是**对方那份**，patcher 直接可写

# B. 拖放版：运行时按行合并，不需要预先重建（sha 对不上但结构没变就自动走这条路）
PoE-zh-Patcher.exe "<游戏>" --set=translated          # 判成「外部改过」时只叠文本
PoE-zh-Patcher.exe "<游戏>" --set=translated --keep-markup
PoE-zh-Patcher.exe --sd-test=某份词缀文件.txt          # 只读演练：判定 + 合并 + 应改行数
```

判成「外部改过」（`Flavor.Custom`）的条件很硬：**行数与基线相同**，且每个要动的行
文本层要么是原文、要么已是译文。游戏真更新过（行数变了 / 文本变了）仍然判「未知」整体拒写。

**升级补丁器不会把自己卡死**：新版本的译文一旦变化，容器里上一版写进去的字节
sha 就对不上新数据包了。`tools/build_bundle.py` 每次重建都会比对上一版、把
「被替换掉的文本」记进 `tools/pack/legacy-rows.json` 并内嵌到数据包里，
于是这种文件被判成**旧版补丁**而不是「未知」—— 逐行认得出来，就地重写成新版。
第一次启用时这条规则是靠人补的（v0.3.0 → v0.4.0 那一行 `{0]`），之后全自动。


### 原件笔误：只修繁中/简中

原件（GGG 官方繁中）本身带少量语法缺陷，会**原样显示**给玩家。全库扫一遍
（`python tools/check_sd_syntax.py raw_sd`）共 7 条记录 / 6 个逻辑行：

| 语言 | 文件 | 缺陷 | 处理 |
|---|---|---|---|
| **繁中** | `stat_descriptions` | `{0]%物理傷害` —— `}` 打成 `]` | **`fix-typo.csv` 收录** |
| 简中 | — | 无 | — |
| 日 | `stat_descriptions` | `{0]から` | 不动 |
| 日 | `stat_descriptions` | `回避力0}%減少` 缺 `{` | 不动 |
| 法 | `stat_descriptions` | `0}% de Réduction…` 缺 `{`（2 处） | 不动 |
| 韩 | `expedition_relic` | `<enchanted>{{…감소` 缺 `}}`（E1 与 E4 各计一次） | 不动 |

**约定：只修繁中/简中段，其它语言段即便有错也不动。** 理由是那些段落在繁中客户端
根本不渲染，改了没有可见收益，却扩大了补丁的影响面（多改一段就多一份和官方更新
撞车的概率）。哪天要扩到全语言，`fix-typo.csv` 加 `lang` 列即可 —— 定位与写回本来就
按语言段索引（`SdFile.vers(块, lang)`）。

修标记类的笔误（比如补 `}}`）不走「只叠文本」那条路：`merge()` 会先看底稿的标记
序列与原文是否逐段相同 —— 相同说明这行没被任何人动过，`old→new` 的差量连同标记层
一起套用；被人换过样式的行仍然只动文本，绝不替别人补标记。


### 数据表（`.dat`）也有同样的通道

`.datc64` 没有「行文本」可重写：行区是定长二进制，文本在字符串池里。所以合并换成
另一套动作 —— **只把新串追加到池尾、把目标槽位的 8 字节指针改过去**，行区与旧池
逐字节不动。

能不能合看两条：行数 / 行宽 / 池起点与包内原文一致，且每个要动的槽位，现存文本
要么是原文、要么已经是我们的译文。缺一条就退回「未知」，仍然整体拒写。

写之前三道自检：结构不变、目标槽位回读正确、原文件范围内**只允许那 8 字节指针变化**
（多改一个字节就抛错）。`python tools/datwrite.py <表>` 是同一套算法的 Python 版，
两条实现互相校对。

例：某客户端 `clientstrings.datc64` 被第三方改过 2 行（`MSAA抗锯齿`、账号冻结文案），
与我们要改的行（7910 珠寶→飾品）不重叠 —— 合并后对方那 2 行原样保留，我们只落自己
那 1 处，1,279,402 → 1,279,502 B，只有 3 个字节变化。


## 写回游戏（已打通）


两条独立链路，各自一份 manifest：`out/patch/manifest.json` 管 `.datc64`，
`out/patch-sd/manifest-sd.json` 管 `metadata/statdescriptions/*.txt`（整文件替换）。
同一个 `patcher` 吃两种 manifest，状态判定与回滚逻辑一样。

```bash
GGPK="C:/Program Files (x86)/Grinding Gear Games/Path of Exile/Content.ggpk"
PT=./tools/patcher/bin/Debug/net8.0/patcher.exe

# 1a) 表格文本补丁（读 out/fix-category-ui.csv + pairs/zh 基线）
python tools/patch_dat.py

# 1b) statdescriptions 补丁（读 8 份修正稿 + raw_sd 基线）
python tools/patch_sd.py            # 只定位，出 out/patch-sd-report.csv
python tools/patch_sd.py --write    # 落盘 out/patch-sd/*.txt + manifest-sd.json

# 2) 看当前状态（只读）
$PT status  "$GGPK" out/patch/manifest.json
$PT status  "$GGPK" out/patch-sd/manifest-sd.json

# 3) 写入 / 回滚
$PT apply   "$GGPK" out/patch-sd/manifest-sd.json
$PT restore "$GGPK" out/patch-sd/manifest-sd.json

# 只处理某个文件
$PT apply "$GGPK" out/patch-sd/manifest-sd.json --only=gem_stat_descriptions.txt
```

`patch_sd.py` 定位靠 4 层：组内序列匹配（「正/负」两行必须在同一 stat 块的
对应语言段里连续命中）→ 变体序号 → 条件消歧 → 原文全文相等。任何一层对不上就记
miss，绝不猜。批次按顺序链式应用（后一批看到的是前一批改过的文本），
所以 `fix-category` 这类基于中间态再修的稿子不会和 `fix-mult` 打架；
两批真冲突的（目前只有 1 条）登记在 `SUPPRESSED`，附裁决理由。

**修正范围只到繁中/简中**：`fix-typo` 之外的所有批次默认 `lang=Traditional Chinese`；
修正稿可用 `lang` 列显式指定 `Simplified Chinese`。日/法/韩/俄等语言段即便有
原文笔误也不动（见下一节）。`fix-typo` 收的是原件自带的语法缺陷，目前 1 条。

写回器认五种状态：**原始**（sha 等于基线）、**已打补丁**（sha 等于补丁）、
**旧版补丁**（sha 对不上，但逐行文本命中上一版记录 → 就地重写）、
**外部改过**（行数一致 + 每个待改行的文本层要么原文要么已是译文 → 只叠文本层，
标记沿用对方的）、**未知**。未知状态一律中止，不会覆盖来路不明的内容。
每次写入前后都重新读出比对 sha256，并留一份 `out/backup/<文件名>.before-apply`。

## 三个可切换的版本

`statdescriptions` 一个槽位只有一份文本，游戏没有「按键切换语言」的入口
（Alt 面板由客户端拼装：物品给词缀名+tier，天赋给内部名+涂油，注入不进去）。
所以「对照原文」只能写进文本，靠两套补丁来回切。

| 产物 | 内容 | 用途 |
|---|---|---|
| `out/patch-sd` | 纯译文（161 行改动） | 正常游玩 |
| `out/patch-sd-dual` | 纯译文 + 改动处下方附英文 | 核对这批改动 |
| `out/patch-sd-dual-all` | 纯译文 + 全部 26635 行附英文（42 个文件） | 通读核对（天赋/物品/技能都能见原文） |

```bash
python tools/patch_sd.py --write              # 纯译文
python tools/patch_sd.py --write --dual       # 只给改动处加原文
python tools/patch_sd.py --write --dual --all # 全量加原文

# 切到对照版（纯译文 → 对照版会被识别为「其它版本」，可直接覆盖）
$PT apply "$GGPK" out/patch-sd-dual-all/manifest-sd-dual-all.json

# 切回纯译文：必须先 restore 再 apply，因为对照版覆盖的文件比纯译文版多
# （拖放版 PoE-zh-Patcher.exe 没有这个限制，它按行改写、支持任意方向切换）
$PT restore "$GGPK" out/patch-sd-dual-all/manifest-sd-dual-all.json
$PT apply   "$GGPK" out/patch-sd/manifest-sd.json
```

对照版的英文追加在引号内，**一组中文配一组英文**：`中文1\n  English1\n中文2\n  English2`。
中英两边的 `\n` 段数一致时逐组穿插（实测 860 行），不一致时（135 行）退回
`整段中文\n  整段英文`，但每一组英文都带两格缩进。两种情况都不新增行、不改 cond/handler，
所以解析结构与原文件逐块一致，`build_bundle.py` 也能按「两空格前缀」把中英再拆开。

写回器除了「原始 / 已打补丁 / 未知」，多认一个**其它版本**：manifest 里的
`sha256_variants` 记录同一文件的另一种已知形态（纯译文版），命中即视为已知、
允许覆盖，不会因为「内容对不上」而误判成游戏更新。

## 关键约定

`key` 不能用行号（每个赛季都会变），用业务主键：

```
statdescriptions/<文件名> : <stat_ids> : <变体条件>      词缀/技能描述
<表名>.datc64 : <行 ID> : <列偏移>                       表格文本
```

覆盖层每条再锁一个原文指纹（`en_hash`），原文一变校验器立刻报警 —— 这是这套东西
不会悄悄烂掉的原因。

## 尚未做

- 覆盖层与校验器本体（`data/overlay.csv` + `validate.py`）：等 L1/L2 的修法定下来再落。
- `fix-category-hold.csv` 5 条（Amulet 是「護身符」还是「項鍊」）待拍板，暂不进补丁。
