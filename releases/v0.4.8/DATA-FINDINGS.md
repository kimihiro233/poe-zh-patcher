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

**对照版的拼装规则是两侧共用的硬约定**：`tools/patch_sd.py` 的 `dual_body()` 生成产物，
补丁器 `DualBody()` 从数据包里的 (原文/译文/英文) 重放同一份字节。规则是
「中文段数 == 英文段数 → 逐组穿插；不等 → 整段附末尾但每组都缩进两格」。
任何一侧改了而另一侧没改，会表现为补丁器报 `生成内容 sha 不符（拒绝写入）`——
因为它是拿重放结果和发布产物比 sha。改动这条规则必须跑 `--data-test=<raw_sd>`，42/42 通过才算数。

### 占位符漏失的分布（`check_placeholders.py`，216 条）

繁中译文在 `{n}` / `#` / `%` 上的漏失不是零星的，能按后果分成三档，数量差两个量级：

| 类别 | 含义 | 条数 | 处理 |
|---|---|---|---|
| `{n}` 整段丢失 | 句子里没有数字，机制必读错 | 18 | 全部立稿修正 |
| `%` 丢失 | 「增加 20」这种读法 | 3 | 全部立稿修正 |
| `#` 丢失 | 数值无处填入 | 0 | — |
| 写法不同 | `{0:+d}`→`{0}`、`{1} {0}` 语序互换 | 42 | **不动**，中文语序差异 |
| 多出占位符 | 英文 `an additional X`，繁中写「{0} 個額外 X」 | 43 | **不动**，值就是 1，更明确 |
| 只丢换行 | 影响排版 | 110 | 暂不动 |

两条经验：

- **编号错位比丢失更隐蔽**。块里有多个 stat_id 时，繁中常把 `{1}` 写成 `{0}`，
  数值照样显示、语法照样通顺，只是取到了相邻那个 stat 的值。这类只能靠
  逐个 stat_id 对齐才能发现（本轮 6 条）。
- **占位符括号会写错**。`stat_descriptions.txt` 有一处 `{0]%`（右括号写成 `]`），
  这种形态连 `{n}` 都算不上，`TOKEN_RE` 抓不到，要靠 `check_sd_syntax.py` 的
  「孤立括号」规则（E4）才能发现。日语/法语/韩语原文也有同类错写。

### 3.29 起两套语法并存（v0.4.2 修的坑）

技能那一批文件（aura / banner_aura / beam / brand / buff / curse / debuff / minion 四件套 /
offering / secondary_debuff / single_minion_spell / skill / vaal_side_area / variable_duration
等 19 个）改成了新写法：**块名直接跟在 `description` 后面**。

```
include "Metadata/StatDescriptions/skill_stat_descriptions.txt"
has_identifiers

description physical_damage_incr
	1 physical_damage_+%
	2
		1|# "You and nearby allies gain {0}% increased Physical Damage"
		#|-1 "You and nearby allies gain {0}% reduced Physical Damage" negate 1
	lang "Traditional Chinese"
	2
		1|# "你與附近的友方獲得 {0}% 物理傷害"
		#|-1 "你與附近的友方減少 {0}% 物理傷害" negate 1
```

- 老写法 `description` 独占一行，新写法 `description <块名>`；其后 `\t1 <stat_id>`、
  变体数、`\t\t` 变体、`lang "X"` 全部一致。
- 判据写成 `lines[i] != "description"` 时，整份新文件会被读成 **0 个块**，于是既不进修正稿
  也不进对照包。v0.4.1 及以前全量对照只有 26 个文件、技能只能看到中文，根因就在这里。
  单是 `skill_stat_descriptions.txt` 就有 75,316 行 / 1,731 块 / 3,056 繁中行被漏掉。
- 三个例外不必处理：`deepwater_stat_descriptions.txt`、`vaal_side_area_stat_descriptions.txt`
  只有一行 `include` 转发到 map；`skillpopup_stat_filters.txt` 是 `group { … }` 过滤规则，
  不是描述文件。
- `include` 是层级引用（skill_stat 又 include 了 active_skill_gem），但每个文件自身仍带完整
  多语言段，按文件独立处理即可，不需要展开 include。

---

## 四、LibDat2 + schema 这条路（结论：放弃）

- `LibDat2.dll` 区分 32/64 位指针（`x64`、`UTF32` 字段），但构造时即完成解析，无法事后设置
- 社区 schema（`poe-tool-dev/dat-schema`）与当前客户端版本对不上：`rarity`/`clientstrings`
  宽度吻合，`stats` 差 44 字节、`baseitemtypes` 差很多，说明列已增删
- 把字符串列谎报成 `array|valuestring` 能凑过长度校验，但取值语义完全错

自写解析器（`tools/dat64.py`）已经够用，且不依赖任何 schema，随版本变化只需重跑。

---

## 五、写回：.datc64 的字符串替换（v0.2.0 实测）

结论先说：**不需要重建 .dat，也不需要 schema**。改一个字符串只要两步。

### 结构决定的写法

```
[0:4]        u32 行数
[4 : P]      行区（rowSize × 行数），字符串字段是 u64 相对偏移
[P : EOF]    字符串池，UTF-16LE + 0x0000 终止
```

池里没有长度表、没有索引，字符串之间靠终止符分隔。所以：

1. 新串**追加到文件尾**（池尾），旧串留在原地——其他行如果引用同一个串，不受影响；
2. 把目标槽位的 u64 改成 `新串位置 - P`，其余 8 字节以外一个字节都不碰。

单个文件实测：1.37 MB 的表改 5 处只增长 72 字节，耗时 433 ms（含 Oodle 重压缩与写盘）。

### 三个必须处理的细节

| 坑 | 现象 | 处理 |
|---|---|---|
| 空串哨兵 | 有的槽位偏移恰好等于池长度（指向池外）。追加后那里长出数据，哨兵会读出新串 | 每次追加前先补 2 个 `0x00`，哨兵读到空串 |
| 换行风格 | CSV 里是 `\n`，`.dat` 里是 `\r\n` | 比对时归一化，写回时按原串风格还原 |
| 共享串 | 多个槽位指向同一串 | 不做原地改（会牵连其他行），一律追加 |

### 自检（三条，任一不过就丢弃产物）

1. 行数 / 行宽 / 池基址前后一致；
2. 目标字段回读 == 新串；
3. 原文件范围内**除被改的 8 字节指针外逐字节相同**。

第 3 条是关键：它把「顺手改坏别处」变成可检测的硬错误。写完从 ggpk 另起进程重导，
再比 sha256，才算出货。

### 写回 ggpk

`LibBundledGGPK3` 的 `FileRecord.Write(bytes, saveIndex: true)` 直接可用，它会把新 bundle
写进 ggpk 空闲块并换掉索引——所以 `Content.ggpk` 体积没变（74,730,084,326 B 前后一致）。
写 5 个文件合计约 2 秒，回滚同样 2 秒。索引打开一次 1.7 秒（121 万文件）。

## 六、可复现的流程

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

---

## 七、写回的安全模型（v0.4.7）

写回不是「把字节塞进去」那么简单。真实会遇到的失败模式有四种，都要有确定性的应对：

| 失败模式 | 后果 | 应对 |
|---|---|---|
| 进程被杀 / 断电 | `_.index.bin` 写了一半 → 游戏直接读不了 | 写前复制索引 + 落 journal；下次启动无条件回滚 |
| 磁盘满 / 权限 | `Save()` 抛异常，索引可能已截断 | 同上，异常路径同样走回滚 |
| 生成内容与发布产物不符 | 写进去的是没人验过的混合文件 | 逐个比 sha，不通过就整份退回 |
| 目标文件被别人改过 | 覆盖后对方的成果永久消失 | 覆盖前落盘原字节 + 四种处置策略 |

### 为什么是「整份回滚」而不是「跳过这一个」

`Bundles2` 的写入是「所有文件先写进新 bundle，最后一次 `Save()` 写索引」。所以
**中途停下 = 索引里有一批指向新 bundle 的记录，但那批文件只写了一半**。
对游戏来说这和没写一样糟，甚至更糟（它不会报错，只会解出错误的字节）。

因此只有两个合法状态：**全部写完并校验通过**，或者 **一个都没写**。

### 顺序上的两个坑

1. **`TrimOrphans`（删没被引用的 bundle 文件）必须在提交之后。**
   如果先删、再回滚索引，那些刚被删掉的文件在恢复后的索引里又变成「有记录、无文件」→
   悬空记录 → 游戏报 `Buffer Underflow`。v0.4.6 把它放在 `Flush()` 里，是错的，已拆出来。

2. **journal 必须在 `Save()` 之前落盘，且用「写临时文件再改名」。**
   journal 自己半截就等于没有 journal。同理，`Phase = "saved"` 也在 `Save()` 返回后
   立刻写入 —— 崩溃点落在 `Save()` 中间时，phase 还是 `writing`，回滚是对的。

### 为什么备份目录放在 `Bundles2/.poe-zh/`

- 和 `_.index.bin` 同一块盘：复制 21 MB 只要几百毫秒，且不占额外分区；
- Steam「验证文件完整性」只校验清单里的文件，不会删掉这个目录；
- 游戏只读 `_.index.bin` 和 `LibGGPK3/`，多一个点开头目录不影响加载。

### 事务与 bundle 生命周期的关系

`Detach()` 换的新 bundle 名字（`LibGGPK3/zhYYMMDDHHmmss`）必须登记进 journal，
否则回滚之后那个文件没人删，会变成孤儿；反过来，回滚时删它也一定安全 ——
因为索引已经换回**不含这条记录**的备份了。

### 为什么 statdesc 做不到「整份换成我们的版本」

成品是 `官方原文 + 译文` 现算出来的（见第三节），数据包里只存了译文和改动行的原文，
**没有整份原文字节**。所以：
- 认得出结构（`Custom`）→ 能合、能逐行换回原文；
- 认不出结构（`Unknown`）→ 只能逐行撤我们写过的行，认不出的行原样保留。

`.datc64` 不一样：`DatEntry` 内嵌了 `Orig` 与 `Patch` 两套完整字节，所以整份换得动。

## 八、74 GB 的 `Content.ggpk` 怎么备份（v0.4.8）

`Bundles2` 只有一个 21 MB 的 `_.index.bin`，复制一份当还原点就行。官方独立端不是：
`Content.ggpk` 是 **74,730,084,326 字节**的单文件，复制不现实，重装要重下 74 GB。
v0.4.7 之前只能加 `--allow-no-rollback` 硬写，写坏了没有退路 —— 这条边界在 v0.4.8 关掉。

### 先测「到底改了多少字节」

拿真客户端跑一次单文件安装（`--set=translated --only=atlas_relic --partial`），
用代理流统计被覆盖的区间：

```
索引落盘             520 ms
被覆盖的区间         43 块 / 20.6 MB
文件长度             74,730,084,326 → 74,730,084,326 B（Δ 0）
```

三条结论，直接决定了备份方案：

1. **改写几乎是纯原地覆盖**。长度不变说明没有「先删后追加」，旧字节写下去就没了。
   这正是必须备份的原因，也是备份能很小的原因 —— 要救的只是这 20.6 MB。
2. **`Index.Save()` 是整段重写**。装 1 个文件也要重写 21 MB 索引区，跟装 47 个文件
   差不多。所以「索引区整段抄一份」和「逐块记账」代价一样，选了后者（不需要知道
   ggpk 的内部布局，也不会因为布局变化而失效）。
3. **写到文件尾部的数据不用管**。追加的字节原先不存在，回滚后留在文件末尾
   没有任何记录指向它，游戏读不到。所以回滚**不截断**文件 —— 少一次「截断到一半」
   把文件弄断的风险，代价只是几 MB 垃圾。

### 机制：流这一层记账，先记后写

`BundledGGPK` 有一个接受 `Stream` 的构造函数，于是把 `FileStream` 包一层
`UndoStream`，所有写入都先经过它：

```csharp
public override void Write(byte[] buffer, int offset, int count) {
    var pos = _s.Position;
    _u.Note(pos, count);      // 把这段「原来是什么」追加进 undo.bin 并刷盘
    _s.Position = pos;
    _s.Write(buffer, offset, count);
}
```

`Note` 只记「第一次碰到某段时的原文」：区间表升序、不相交、相邻即合并，所以
同一块被反复覆盖（`rec.Write` 改同一个文件的记录头、`Save` 回头再写同一段）也只占
一份空间。实测 5 次写入（原地、两块重叠、重叠再改、追加）→ 3 条记录、8,292 B。

### 兜底的三道

| 断在哪 | 谁来收拾 |
|---|---|
| 写文件写到一半，进程被杀 | 下次启动 `Recover()` 看到 `phase=writing`，逐块写回原字节 |
| 索引落盘后回读不过 | 同一进程内 `Rollback()`：写回原字节 → 每块读回来比对 → 删日志 |
| 追加后断电 | 日志用 150 ms 节流 `Flush(true)`；追加的字节本来就没覆盖东西，丢了也无害 |

回滚只依赖「日志里的字节」这一样东西，不做任何推断，所以断电重启后也确定能走通。
退出前必须把 `phase` 改成 `rolledback`/`committed` 并删日志 —— 否则下次启动会再回滚
一遍（幂等，但会误报「上次写入没走完」）。

### 边界

- **还原点是「那一刻」的**。`--restore-ggpk` 把当时被覆盖的块写回去；如果中间游戏
  又更新过，写回去就是两个版本的混合物。所以先报时间戳和长度、要一次确认。
- **库若拒绝自建流**（未来版本把构造函数参数换成 `FileStream`），容器会退回原来的
  开法并明确告知「这次没有 ggpk 还原点」，安装随之被拒绝 —— 不会静默降级成没有退路。
- **`SetLength` 直接抛异常**。截断会丢掉日志覆盖不到的字节；ggpk 的写入路径本来
  也不该走到这里，走到就是库变了，宁可停下也不能把客户端削掉一截。
