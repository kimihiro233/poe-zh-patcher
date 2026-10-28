# poe-zh · 繁中改良汉化

不重建整套汉化，只修官方繁中里**读着别扭、容易误读、机制算错**的地方，
并且这套东西能在每个新赛季活下来。

核心原则：**不动原文，只维护一份按 key 索引的覆盖清单。**

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
│  ├─ release.py          打版本快照到 releases/v<ver>/
│  ├─ extract_pairs.py    成对抽取 EN/ZH 的 .dat 文本
│  ├─ parse_statdesc.py   解析词缀/技能描述模板（纯文本）
│  ├─ analyze.py          体检：占位符/方向词/术语/简体字/长度
│  ├─ glossary.py         从对照数据反推术语表
│  ├─ dumper/             C# 导出器（LibGGPK3 + oo2core）
│  ├─ patcher/            C# 写回器（status / apply / restore，只认 ggpk）
│  ├─ dropper/            C# 拖放式补丁器（ggpk 与 Bundles2 双形态，打包成 dist/*.exe）
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

发布包命名带游戏版本：`poe-zh-v0.3.0+game3.29.3b.zip`。

| 译文版本 | 游戏版本 | 状态 |
|---|---|---|
| v0.1.0 | 3.29.3b（回溯推定，当时未记录） | 分析基线，未动游戏文件 |
| v0.2.0 | 3.29.3b（回溯推定，当时未记录） | 回写链路打通，写入 5 表 / 10 处 |
| v0.3.0 | **3.29.3b / patch 3.29.3.3（已锁）** | 版本绑定，指纹 `1962f349…` |

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
 [1] 纯译文        —— 日常游玩，160 处修正
 [2] 对照版        —— 修正过的 160 行下方显示英文原文
 [3] 全量对照      —— 所有词缀下方显示英文原文
 [4] 还原官方原文  —— 撤销全部修改
 [t] 结构演练      —— 只验读写通道，不改内容（换客户端/换平台时先跑这个）
```

两种形态的差别只在落盘方式：ggpk 每次写文件顺带存索引；`Bundles2` 是把改动
写进新的 `LibGGPK3/N.bundle.bin`，最后统一存一次 `_.index.bin`。

和手工链路的差别：

- **四个版本随意来回切**。运行时从容器当前内容按行改写目标行，不做整文件
  替换，所以「对照版 → 纯译文」不再需要先 restore；甚至上次切换到一半的
  混合状态也能逐文件识别并修好。
- 每个文件写入前用内嵌的 sha256 校验生成结果，写入后回读再校验一次。
- 状态未知的文件（多半是游戏更新了）会让整体中止，一个都不写。
- 写入前备份到 `%LOCALAPPDATA%\PoE-zh-汉化\backup\<时间戳>\`。
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
| `client-log.txt` | 客户端日志开头 60 行（含版本号） |

报告里每个对不上的文件会给三样东西：

1. **行数差**（本文件 vs 数据包基线），判断是否整体增删了行；
2. **逐行核对**（原文 / 纯译文 / 对照 / 全量对照 / 其它 各多少行）；
3. **错位探测**：对不上的行去附近 ±40 行找同一段文本，给出最常见偏移。
   例如「1187 行能在附近找到，最常见偏移 +4 行（1159 行）」——说明客户端只是
   插入了 4 行，按偏移重映射就能套用。

作者拿到 zip 后可以：按 `files/` 重做该版本基线；或确认偏移后做一次重映射。

## 写回游戏（已打通）


两条独立链路，各自一份 manifest：`out/patch/manifest.json` 管 `.datc64`，
`out/patch-sd/manifest-sd.json` 管 `metadata/statdescriptions/*.txt`（整文件替换）。
同一个 `patcher` 吃两种 manifest，状态判定与回滚逻辑一样。

```bash
GGPK="C:/Program Files (x86)/Grinding Gear Games/Path of Exile/Content.ggpk"
PT=./tools/patcher/bin/Debug/net8.0/patcher.exe

# 1a) 表格文本补丁（读 out/fix-category-ui.csv + pairs/zh 基线）
python tools/patch_dat.py

# 1b) statdescriptions 补丁（读 7 份修正稿 + raw_sd 基线）
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
TC 段里连续命中）→ 变体序号 → 条件消歧 → 原文全文相等。任何一层对不上就记
miss，绝不猜。批次按顺序链式应用（后一批看到的是前一批改过的文本），
所以 `fix-category` 这类基于中间态再修的稿子不会和 `fix-mult` 打架；
两批真冲突的（目前只有 1 条）登记在 `SUPPRESSED`，附裁决理由。

写回器只认三种状态：**原始**（sha 等于基线）、**已打补丁**（sha 等于补丁）、**未知**。
未知状态一律中止，不会覆盖来路不明的内容。每次写入前后都重新读出比对 sha256，
并留一份 `out/backup/<文件名>.before-apply`。

## 三个可切换的版本

`statdescriptions` 一个槽位只有一份文本，游戏没有「按键切换语言」的入口
（Alt 面板由客户端拼装：物品给词缀名+tier，天赋给内部名+涂油，注入不进去）。
所以「对照原文」只能写进文本，靠两套补丁来回切。

| 产物 | 内容 | 用途 |
|---|---|---|
| `out/patch-sd` | 纯译文（160 行改动） | 正常游玩 |
| `out/patch-sd-dual` | 纯译文 + 改动处下方附英文 | 核对这批改动 |
| `out/patch-sd-dual-all` | 纯译文 + 全部 22037 行附英文 | 通读核对（天赋/物品/技能都能见原文） |

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

对照版的英文追加在引号内，格式是 `中文\n  English`（两个空格缩进），
不新增行、不改 cond/handler，所以解析结构与原文件逐块一致。

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
