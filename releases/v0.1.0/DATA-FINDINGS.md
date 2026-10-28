# 数据层逆向结论（2026-10 版客户端实测）

游戏：`C:\Program Files (x86)\Grinding Gear Games\Path of Exile\Content.ggpk`（70 GB，独立版）
索引内文件数：1,214,937　基础 .dat 表 1,918 个

---

## 一、译文到底存在哪（三个互不重叠的位置）

| 位置 | 内容 | 规模 |
|---|---|---|
| `data/traditional chinese/*.datc64` | 表格里的短文本：物品名、UI 字符串、专有名词 | 430 个表与英文版不同 |
| 同上表内的 `words.datc64` | 词缀名碎片（如 `of the Brute` = `野蠻之`） | 21,878 汉字 |
| `metadata/statdescriptions/*.txt` | **词缀 / 技能描述模板**，同一文件内嵌全部 11 种语言 | 941,999 汉字，22,089 条英文条目 |

关键结论：**「中文看着别扭」的主要来源是第三项**，不是 .dat。因为你在物品上看到的每一行词缀文字，都是
`Stats.dat` 的数值 + `stat_descriptions.txt` 里的模板拼出来的。改 .dat 改不动这些句子。

---

## 二、.dat / .datc64 二进制格式（本轮修正，与早前笔记不同）

```
偏移 0    u32  rowCount
偏移 4    ───  行区开始 ───        每行 rowSize 字节
          ───  字符串池 ───        起点 = 4 + rowCount × rowSize，前 8 字节固定 0xBB
```

- 行内的字符串字段是 **u64 相对偏移**，实际文本在 `池起点 + 偏移`
- 每个串 = `UTF-16LE 内容` + `0x0000` 终止；终止符之后常常还有 2 字节空位，但**不是固定对齐**
- 池里存在**未被引用的空闲块**（旧版本残留、`0x7b`/`0x7c` 之类的碎片），所以「顺序解析到文件尾」
  这个判据是错的，会中途断链

**rowSize 的确定方法**：扫描 `4 + rowCount × rs` 处是否出现 8 个连续 `0xBB`。实测每个文件唯一命中，
不需要 schema：

| 表 | rowSize |
|---|---|
| rarity | 48 |
| clientstrings | 52 |
| acts | 69 |
| stats | 105 |
| mods | 654 |
| baseitemtypes | 374 |
| passiveskills | 354 |

**一个坑**：指针值与真实串起点偶尔差 ±2（同一串在池里留了旧副本），解析要向两侧各试一次。

**验证**（22,089 条抽取中 21,915 条两侧都命中）：`rarity` 的 `Normal/Magic/Rare/Unique`
对应 `普通/魔法/稀有/傳奇`；`clientstrings` 的 `GameName` 对应 `Path of Exile`。

---

## 三、statdescriptions 的格式（纯文本，可直接改）

UTF-16LE + CRLF + 制表符缩进，一个 `description` 块 = 一组 stat id + 若干语言块：

```
description
	1 %_of_your_and_your_minions_damage_cannot_be_reflected
	1
		1|# "You and your Minions prevent {0:+d}% of Reflected Damage"
	lang "Traditional Chinese"
	1
		1|# "你與你的召喚物防止 {0:+d}% 反射傷害"
```

- 第一个块（无 `lang` 行）= 英文；之后每块前一行 `lang "X"`
- 语言：Japanese / Simplified Chinese / **Traditional Chinese** / French / Russian / German /
  Spanish / Korean / Portuguese / Thai
- `{0}` `{0:+d}` `#` `%` `\n` 是运行时填充位；`<tag>{文字}</tag>` 是配色标签，`{文字}` 本身要翻译

### 变体机制（这是最容易搞错的一处）

`description` 下第一个单缩进行是 `<stat_id 个数> <stat_id...>`，第二个单缩进行是**变体数量**，
随后每个 `\t\t` 行是一个变体，格式为 `<区间条件> "<文本>" <handler...>`：

```
description
	1 physical_damage_+%_per_frenzy_charge
	2
		1|#   "{0}% increased Physical Damage per Frenzy Charge"
		#|-1  "{0}% reduced Physical Damage per Frenzy Charge" negate 1
```

- 区间条件写作 `<min>|<max>`，`#` 表示该侧无界：`1|#` = 值 ≥ 1，`#|-1` = 值 ≤ -1，
  另有 `1|99`、`100|#`、`# #`（多填充位）等形态
- `negate 1` = **显示前把数字取负**。所以 `10% reduced` 在数据里是 `-10` + negate，
  显示成「10% reduced」；同一句话在英文里对应**两个变体**
- 因此「数值可能是负数」不是靠译文字面兜住的，而是靠正负两个变体分工。
  **只译一个变体 = 负值词缀显示成正向措辞**，这是本套繁中最集中的真缺陷

### 前缀**不是**稳定 key（早期笔记这里写错了）

前缀在不同变体之间会重复，例如同一块里两行都写 `#`：

```
		# "Blight Monsters in your Maps take {0}% increased Damage"
		# "Blight Monsters in your Maps take {0}% reduced Damage" negate 1
```

用 `setdefault(前缀)` 建字典会**静默丢掉第二个变体**，再把英文正值配到中文负值，
凭空造出「译反」。正确做法是**按变体序号配对**（第 N 个变体在任何语言里指同一段语义）：
实测 14,948 个块里 14,899 个中英变体条件序列完全一致，序号配对可靠。
配对前还要校验两边变体数量相同，不同即为数据缺陷（本轮仅 6 个块）。

因为它是纯文本，**改起来的代价远低于 .dat**：不需要重打包 bundle 之外的二进制操作，
diff 也可读。但改动必须保持变体数量与顺序不变，否则整块错位。

---

## 四、LibDat2 + schema 这条路（结论：放弃）

- `LibDat2.dll` 区分 32/64 位指针（`x64`、`UTF32` 字段），但构造时即完成解析，无法事后设置
- 社区 schema（`poe-tool-dev/dat-schema`）与当前客户端版本对不上：`rarity`/`clientstrings`
  宽度吻合，`stats` 差 44 字节、`baseitemtypes` 差很多，说明列已增删
- 把字符串列谎报成 `array|valuestring` 能凑过长度校验，但取值语义完全错

自写解析器（`tools/dat64.py`）已经够用，且不依赖任何 schema，随版本变化只需重跑。

---

## 五、可复现的流程

```bash
# 1. 从 ggpk 取出成对的英文 / 繁中 .dat
dumper.exe cmp  <Content.ggpk>                    # 生成 diff_pairs.csv
dumper.exe pairs <Content.ggpk> pairs diff_pairs.csv

# 2. 取词缀描述模板
dumper.exe raw <Content.ggpk> raw_sd statdescriptions

# 3. 抽取对照
python tools/extract_pairs.py pairs out/pairs.csv       # 322,198 条（UI/物品/名词）
python tools/parse_statdesc.py raw_sd out/statdesc.csv  # 22,089 条（词缀/技能描述）

# 4. 体检
python tools/analyze.py out/statdesc.csv out/report-statdesc.md
python tools/analyze.py out/pairs.csv    out/report-ui.md
python tools/glossary.py out/statdesc.csv out/glossary.csv
```

依赖：.NET 8（导出器）+ `LibGGPK3` NuGet + `oo2core.dll`；Python 3.10+（可选 `zhconv` 做繁简检测）。
