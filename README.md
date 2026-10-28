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
│  ├─ markup.py           标记层/文本层分离与合并（三分法，写回时的唯一口径）
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

发布包命名带游戏版本：`poe-zh-v0.4.0+game3.29.3b.zip`。

| 译文版本 | 游戏版本 | 状态 |
|---|---|---|
| v0.1.0 | 3.29.3b（回溯推定，当时未记录） | 分析基线，未动游戏文件 |
| v0.2.0 | 3.29.3b（回溯推定，当时未记录） | 回写链路打通，写入 5 表 / 10 处 |
| v0.3.0 | 3.29.3b / patch 3.29.3.3（已锁） | 版本绑定，指纹 `1962f349…` |
| v0.4.0 | **3.29.3b / patch 3.29.3.3** | 词缀文本入补丁（26 文件 / 161 处）、标记层三分法、繁中 `{0]` 笔误、补丁器支持升级与异地客户端 |

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
