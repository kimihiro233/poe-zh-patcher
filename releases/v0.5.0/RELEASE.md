# v0.5.0

- 打包时间：2026-10-30 11:39:31 +0800
- **适用游戏版本：3.29.3b**（patch 3.29.3.3）
- 状态：**已向 ggpk 写入 词缀文本 42 文件 / 181 处 + .dat 5 表 / 10 处 + 语系 1 文件 + 国旗 1 文件**

## 对应的游戏版本

| 项 | 值 |
|---|---|
| 客户端版本 | `3.29.3b` |
| 补丁版本 | `3.29.3.3` |
| 版本来源 | `logs/LatestClient.txt` |
| 锁定时 ggpk 状态 | 干净（原始） |
| Content.ggpk | 74,730,084,326 B，mtime 2026-10-28T13:10:58 |
| 锁定文件数 | 1961（语言 1916 + 词缀 txt 45） |
| 基线指纹 | `1962f349c65322aedc9f826cf4974ffe8543bd72efceae51f251483e9d8fa155` |

指纹 = 上述每个文件 `path + sha256` 排序后聚合；只要有一处文本变化它就会变。
游戏更新后先跑 `python tools/gameinfo.py --diff` —— 它会指出哪些文件变了。

**本版译文只对 3.29.3b 负责。**换版本必须重跑：`gameinfo` → 重导 → 重分析 → `patch_dat`。

> 基准：每次写入以「该版本下第一次看到的那份字节」为算式输入，结果与改过几次无关，反复打补丁不会累积漂移；--baseline / --restore-baseline 可整份退回原文；备份或基准目录建不出来时拒绝写入

| 修正稿 | 条数 | 内容 |
|---|---|---|
| `fix-direction.csv` | 19 | 方向错误（正/负变体用了相反措辞） |
| `fix-mult.csv` | 49 | 乘法口径（more/less 误译 增加/減少） |
| `fix-additive-rev.csv` | 5 | 反向口径（increased 误译 更多/更少） |
| `fix-wording.csv` | 5 | 逐条润色与语义补全 |
| `fix-gem.csv` | 34 | 技能宝石限定词（技能寶石→主動技能寶石） |
| `fix-order.csv` | 10 | 数值位置/语序统一 |
| `fix-category.csv` | 41 | 物品类别词（Jewellery/Item/Armour） |
| `fix-category-ui.csv` | 10 | 物品类别词·UI 表侧（.dat 回写） |
| `fix-category-hold.csv` | 5 | 存疑未改（待拍板） |
| `fix-typo.csv` | 1 | 原件笔误（繁中占位符 {0] → {0}） |
| `fix-placeholder.csv` | 20 | 占位符漏失（{n} / % 丢了，补回；含一条繁体错字） |

## 术语表

`glossary.csv` 共 51 条。

## 体检报告

- `placeholder-check.md` —— 占位符漏失全库排查（216 条按后果分级）
- `variant-check.md` —— 中英变体条件不一致排查（58 处）

## 打包内容

文档 4 份 + 修正稿 11 份 + 状态表 + 术语表 + 体检报告 2 份 + 补丁清单 + game.lock.json + MANIFEST.sha256 + `PoE-zh-Patcher.exe`（zip 根目录，不进归档目录）。二进制原始导出不入档（可由 `tools/dumper` 重导，且换版本后必须重导）。

## 已写入游戏（metadata/statdescriptions 侧）

| 版本 | 文件 | 改动行 | 说明 |
|---|---|---|---|
| 纯译文 | 14 | 181 | `active_skill_gem_stat_descriptions.txt, atlas_stat_descriptions.txt, chest_stat_…` |
| 对照版 | 14 | 181 | `active_skill_gem_stat_descriptions.txt, atlas_stat_descriptions.txt, chest_stat_…` |
| 全量对照 | 42 | 181 | `active_skill_gem_stat_descriptions.txt, advanced_mod_stat_descriptions.txt, atla…` |

写回方式是**逐行改引号内文本**，结构行、其它语言段、`.dat` 之外的一切原样保留；
标记层（`<enchanted>` / `<rgb(…)>{{ }}`）按三分法处理：对方的换色/换标签沿用，
被整段删光的按官方补回，与官方一致的就原样。逐条改动见对应 `statdesc-manifest-*.json`。

## 已写入游戏（.dat 侧）

| 表 | 改动 | 原始 → 补丁字节 | sha256(补丁) |
|---|---|---|---|
| `achievementitems.datc64` | 5 | 1371030 → 1371102 | `6a9490a77793f926` |
| `clientstrings.datc64` | 1 | 1279106 → 1279206 | `d80be3062d44cfc8` |
| `currencyitems.datc64` | 2 | 518020 → 518224 | `d5a6d6983ba260cd` |
| `incursionrooms.datc64` | 1 | 64720 → 64734 | `cdccc4cc27fd281b` |
| `uitalktext.datc64` | 1 | 23852 → 23916 | `f43dfea570d780e4` |

写入方式：`patcher apply`（LibBundledGGPK3），每个文件写入后从 ggpk 重读比对 sha256；
回滚：`patcher restore`。逐条改动见 `out/patch/manifest.json`。

## 已写入游戏（客户端配置侧）

这两步不是译文，但**不做就看不到中文 / 图标对不上**，所以由补丁器一并处理，
已经是对应状态就跳过，`[4] 还原` 时一并改回。

| 文件 | 改什么 | 为什么 |
|---|---|---|
| `data/languages.dat` | `French` 与 `Traditional Chinese` 两行的语言代码对调（`fr` ↔ `zh-TW`） | 客户端语言列表第二项（法国国旗）改为加载繁中数据。不这么做，打完补丁游戏里还是英文，必须再跑一次 PoeChinese3 |
| `art/uiimages1.txt` | `FlagIcons/fr` 与 `FlagIcons/4K/fr` 两行的图集坐标改指 `zhCN` 区域 | 上一步之后那一项的标签是「繁體中文」，配图却还是法国国旗。图集里本来就有五星红旗，且两处矩形尺寸一致（74×49 / 101×67），只改坐标、不动任何 DDS |

两份文件都是**字节级最小替换**：`languages.dat` 交换两段等长字符串（696 B 长度守恒），
`uiimages1.txt` 只重编那两行（6 MB 里其余字节原样拷贝）。结构对不上（多半是游戏更新过）
一律跳过并提示，不硬写。实现见 `tools/shared/LanguageSwitch.cs` 与 `tools/shared/FlagIcon.cs`。

已知副作用：语言列表里「简体中文」那一项本来就是五星红旗，换完会有两面；
那一项没有对应的数据目录（客户端只有 `data/traditional chinese/`），选了只会回落到英文。
