# v0.3.0

- 打包时间：2026-10-28 13:13:47 +0800
- **适用游戏版本：3.29.3b**（patch 3.29.3.3）
- 状态：**已向 ggpk 写入 .dat 补丁**

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

> 游戏版本绑定：每一版译文锁定客户端版本 + 1961 个文件的内容指纹；patcher 版本不符拒绝写入（退出码 4）；新增 check_fixes.py 做升级体检。

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

## 术语表

`glossary.csv` 共 51 条。

## 打包内容

文档 4 份 + 修正稿 9 份 + 状态表 + 术语表 + game.lock.json + MANIFEST.sha256。二进制原始导出不入档（可由 `tools/dumper` 重导，且换版本后必须重导）。

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
