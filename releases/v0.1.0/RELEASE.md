# v0.1.0

- 打包时间：2026-10-28 13:01:44 +0800
- 游戏基线：Content.ggpk 74,730,084,326 B（2026-09-06 22:24）
- 状态：**分析基线，尚未写入任何游戏文件**

> 分析基线：22,167 条英中词缀对照、322,198 条 UI 文本、8 批修正稿共 168 条待改；此版本未改动任何游戏文件。

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

文档 4 份 + 修正稿 9 份 + 术语表 + MANIFEST.sha256。二进制原始导出不入档，可由 `tools/dumper` 重导。
