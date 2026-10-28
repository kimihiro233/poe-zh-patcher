# PoE 繁中汉化项目 · 长期备忘

> **本目录是项目记忆的正本**（随仓库走、进 git）。
> `%USERPROFILE%\WorkBuddy\2026-10-27-16-36-08\.workbuddy\memory` 是指向这里的 directory junction，
> 两个路径读写的是同一份文件；换机器/换工作区时先重建这个联接。
> 日志 `YYYY-MM-DD.md` 只追加，长期结论沉淀进本文件。

## 用户约定与偏好

- **简繁混用提醒**：用户自述简繁字体混用、有时记错用字。约定：用户提供的所有定稿文案，
  入库写入 `.txt` 前必须先跑 zhconv 简体检测，抓到的逐字标出请用户确认，不得直接写入。
- 术语判定以语料统计为准（如「保留效用」165:0 胜「保留效能」），不依赖用户或译者的记忆。
- 用户会深究具体词条的场景语义（如 increased/reduced 的方向、保留效用的反直觉逻辑），
  这类质疑必须先查数据再回应，数据不支持用户前提时直接摆证据。

## 已定死的规则

- **乘法/加法口径**：按 stat 的 `_final` 标记分，不按属性分。
  `_final` ⇒ more/less ⇒ 「更多/更少」；无 `_final` ⇒ increased/reduced ⇒ 「增加/減少」；
  非数值（more likely / no more than / more than）⇒ 更有可能/不超過/超過。
- **保留体系**：「保留效用」(Reservation Efficiency) 与「保留量」(Reservation) 是两个平行 stat，
  中英各自字面忠实，方向零错位（269 条核对）。「效用增加 ≡ 保留减少」的相反感来自除法公式，
  属机制反直觉，**不是翻译错误，永不修正**。
- **難度**是繁中一级术语（25 处 increased Difficulty 全译「增加難度」），不换「難纏/棘手」。
- **Skill Gem 语义**：GGG 显示文本的 "Skill Gems" **不含辅助宝石**（stat id 里写 active、
  wiki 分栏统计佐证、同物品 Gems vs Skill Gems 对照）。官方中文从不写破（主動技能寶石 0 次）。
  「全部技能寶石」两条（等级/品质，群星之灰）已改「全部主動技能寶石」入 fix-wording.csv；
  同族其余 35 个模板（火焰/插槽等）是否统一加「主動」待拍板。
- 「for each」不可译「根據」；递进关系靠「每…就…」句式立起来。
- **物品类别词**：`Jewellery` = **飾品**（戒指/項鍊/腰帶），`Jewel` = **珠寶**（插槽寶石），
  `Trinket` = 飾品（官方與 Jewellery 撞車，不修）。**「首飾」是簡中用詞，繁中語料 0 處**——
  用户若說「首飾」，那是簡中記憶，不要照抄進繁中文件。鐵證：`{Jewellery}` 佔位符譯「飾品」、
  `itemclasses` 內 Jewellery 無此類而 Jewel→珠寶、`chests.dat` 首飾箱一律「飾品」（珠寶僅用於寶石箱）、
  同句 `Jewellery…drop as Jewels` → 「飾品…會改為珠寶」。
  Amulet：裝備名用「護身符」（星盤護身符/簡純護身符），泛稱用「項鍊」(250:67)，兩套並存不強改。

## 异地客户端：先判「是不是原版」（2026-10-28 定论，含一次自我修正）

- **要先证伪「原版」再谈适配。** 首份采集包（Steam 亚太/腾讯渠道，3.29.3b / Build 321328，
  与本机同 build）里的 statdesc **不是原版产出**：正文一字未动，标记被整体重写。
  我第一版把它误判成「两套官方数据包」，是错的 —— 依据是 12 个文件 sha 不同就下了结论，
  没验证标签体系。教训：**sha 不同先查标记，别先猜渠道。**
- 三个「非原版」指纹（`tools/check_sd_syntax.py` 的 W1/W2 已能自动报）：
  1. **裸 `<rgb(r,g,b)>`** —— 原版 45 个词缀文件里出现 **0 次**，标签体系只有 17 个命名标签；
     对方 1,122 处。这条单独就足以定性。
  2. **`markup` 关键字空声明** —— necropolis 814 行留着关键字、正文标记全无（原版 814:814 严丝合缝）。
  3. **11 语言同步改写**（各 319 处，韩文 318）—— 人手不会去改泰文俄文，必是程序批量。
- 重写规则：`<nemesismod>`→`<rgb(209,46,46)>`（5 处）；`<enchanted>` 143 处、
  `<crafting*>` 74 处、`<necropolisdownside>`/`<normal>` 7 处 **连 `{{}}` 一起整段删**；
  另有 **97 处原本零标记的行被加上颜色**（红=致命 64、紫=收益 20、蓝=掉落翻倍 8、金/绿=反射 5）——
  这是人工设计的「危险词缀高亮」，不是格式转换。
- **改坏了吗？语法没坏**：E1 括号 1:1、E4 占位符 6:6，与原版完全一致；
  那 1+6 处是**原版自带 GGG 笔误**（韩文缺 `}}`、日文/繁中 `{0]`、法文 `0}%` 缺 `{`），别算到对方头上。
  坏在契约：命名标签变死色（丢上下文取色）、necropolis 74 处「按词缀类型上色」被抹平（可读性退化）。
- **因此不能把对方文件登记成基线**：整文件覆盖会推翻对方 1,122 处高亮、恢复 2,386 处标签。
- **写回口径 = 标记层三分法**（2026-10-28 晚定稿，唯一口径，别再另立）：
  引号内文本拆「标记层（`<enchanted>` `<rgb(…)>{{ }}`）+ 文本层（其余，含 `{0}`）」，译文只管文本层。
  ① 对方留着标记但换了（换色/换标签/新增高亮）→ **沿用对方，一字节不动**（不替别人改风格）
  ② 标记连 `{{ }}` 一起被删光 → **按原版补回**（连结构性 `{{ }}` 都没了 = 机械替换的覆盖伤，不是风格）
  ③ 与官方一致 → 正常替换。`--keep-markup` 关掉 ②，退化成纯只改文本。
  实测本包：② 触发 217 行（expedition 143 + necropolis 74），正好等于「整段删光」统计数，① 那部分 0 误伤。
  ④ 底稿标记序列与原文**逐段相同**（= 这行没人动过）→ `old→new` 连标记层一起套用。
     这条是修原件笔误（补漏掉的 `}}`）的唯一通道，不违反「不替别人改标记」——被换过样式的行不走这里。
  实现：`tools/markup.py`（`merge()`，`python tools/markup.py` 跑 11 条自检）；C# 侧 `Markup` 类同逻辑，
  `PoE-zh-Patcher.exe --markup-test` 对齐，`--sd-test=<词缀文件>` 只读演练。
- **修正范围只到繁中/简中**（2026-10-28 用户定，别扩散）：日/法/韩/俄等语言段即便有原件笔误也不动
  ——那些段在繁中客户端不渲染，改了没收益却扩大影响面。`patch_sd.py` 按语言段索引
  （`SdFile.vers(块, lang)`，修正稿可用 `lang` 列显式指定，默认 `Traditional Chinese`）。
  全库 E 类语法缺陷 7 条记录 / 6 个逻辑行：繁中 1、简中 0、日 2、法 2、韩 1。
- **判「外部改过」≠ 判「未知」**：拖放版新增 `Flavor.Custom` —— 行数与基线相同 + 每个要动的行文本层
  要么原文要么已是译文，就只叠文本；行数变/文本变仍判未知整体拒写。命令行侧对应
  `python tools/patch_sd.py --shell <目录> --write`（产出以对方 sha 为 `sha256_original` 的独立补丁）。
- **`.dat` 表同样走 `Flavor.Custom`（2026-10-28 晚补）**：判定看行数/行宽/池起点一致 + 每个待改槽位
  现存文本 ∈ {原文, 译文}；写入只做「新串追加池尾 + 改 8 字节指针」，行区与别的行一字节不动。
  bundle 里每张表带 `edits`（`[[row, rel, old, new], …]`），C# `DatMerge` 与 Python
  `datwrite.patch_bytes` 同算法，`--dat-test=<表文件>` 只读演练。第二条采集包（233523）就靠它复位：
  `clientstrings` 被第三方改过 2 行（MSAA抗锯齿、腾讯冻结文案），与我们要改的 7910 行不重叠 → 合并后
  对方 2 行原样保留，只 3 个字节变化。**教训：能按行判定的就不该整份拒写**，statdesc 如此，dat 也如此。
- 更正一条错判：bundle 里 26 个目标文件都**有译行**（necropolis 243、primordial 268…），
  不存在「7 个我们不写却触发拒写」；之前拒写纯粹因为 26 个文件 sha 全对不上。
- 原件笔误收进 `out/fix-typo.csv`（目前 1 条，繁中 `{0]`→`{0}`，玩家可见）。
  它让 `stat_descriptions` 的 edits 从 114 变 115，其余 9 个文件 sha 不变。
- `clientstrings` 是另一条线索：15 格差异里 `市集`/防沉迷/腾讯游戏安全中心 = **渠道**；
  12 格含简体（守护艾利丝/溃烂疮口/左键点击）= **简体污染**。两件事别混着归因。
- 通用教训（与渠道无关，长期有效）：
  - **整文件 sha256 不能当文件身份**，同版本同 build 也会对不上。身份 = 行数 + 语言段数 + 逐行去标记文本哈希。
  - 采集包 `report.txt` 只核「数据包要改的行」，严重低估差异（atlas 报 4 行、实际 44 行 = 4 处 × 11 语言）。
  - `patch` 版本读不到时用 `[ENGINE] Build Revision` 兜底（client-log.txt 里有），比版本号更能定性。
  - 定位键用 (stat 块, 语言, 变体序号)；`map` 2 处、`mercenary` 1 处 stat_ids 重复，需「文件内第 n 次」兜底。

## 项目位置与产物

- 项目在 `E:/projects/poe-zh/`；脚本 `tools/`，产物 `out/`。
  **git tag**：v0.1.0 分析基线 / v0.2.0 回写打通 / v0.3.0 版本绑定 / 
  v0.4.0 词缀文本入补丁（= 游戏 3.29.3b，26 文件 161 处）。
- **游戏版本绑定（v0.3.0）**：版本号只能从 `logs/LatestClient.txt` 取（`release/tags/3.29.3b`、
  `Web root: .../3.29.3.3/`），exe 无版本资源。`game.lock.json` 锁 1916 语言文件 + 45 词缀 txt
  的 sha256 指纹（3.29.3b = 1962f349c65322ae），**必须在干净 ggpk 上锁**。
  三道闸：patcher 版本不符 exit 4（--force 可越）/ 全量预检遇未知 exit 5 一个都不写 /
  patch_dat 校验 pairs/zh 的 sha 与 lock 一致。`check_fixes.py` 复议 178 条修正稿：
  pending/applied/drift/gone，换赛季必跑。
- 游戏 ggpk：`C:/Program Files (x86)/Grinding Gear Games/Path of Exile/Content.ggpk`（74,730,084,326 B）。
- **回写链路（v0.2.0 已打通并实测）**：
  `python tools/patch_dat.py` → `out/patch/*.dat` + `manifest.json`；`tools/patcher` 的
  `status / apply / restore`（LibBundledGGPK3，`FileRecord.Write`，写进空闲块，ggpk 体积不变）。
  `.datc64` 重写策略：**新串追加池尾、只改目标槽位的 8 字节 u64 指针**，不重建文件、不依赖 schema。
  三个坑：空串哨兵（偏移==池长，追加前补 2 个 0x00）、`\r\n` vs `\n`、共享串（一律追加不原地改）。
  自检三条：结构不变 / 目标字段回读 / 除指针外逐字节相同。
- 已打通：`.txt` 直链（`patch_sd.py` → `out/patch-sd*/` + `manifest-sd*.json`，kind=file_replace）
  与拖放式单文件补丁器 `dist/PoE-zh-Patcher.exe`（ggpk 与 Bundles2 双形态，
  四版本任意切换，`--struct` 结构演练、`--selftest` 数据自检、`--plan` 只算不写）。
- **版本号只有一处真相**：`VERSION`。`patch_sd.py` / `build_bundle.py` / `release.py` 全读它，
  别再在任何地方写死（v0.3.0 时代写死过 0.3.0 与 0.4.2 两处，发布时三方对不上）。
- **发版**：`python tools/release.py --note "..."` → `releases/v<ver>/`（文本 + 三套补丁清单）
  与 `releases/poe-zh-v<ver>+game<client>.zip`（额外含 `PoE-zh-Patcher.exe`，`--no-exe` 可去掉）。
  `dist/` 与 `releases/*.zip` 都不进 git。`MANIFEST.sha256` 必须写 LF（CRLF 会让 `sha256sum -c` 失效）。
- **升级不能把自己卡死（v0.4.0 修）**：译文一变，容器里上一版的字节 sha 就和新包不符 → 曾被判「未知」整体拒写。
  现在 `build_bundle.py` 重建时自动比对上一版、把被替换的文本写进 `tools/pack/legacy-rows.json`
  并内嵌到每行的 `legacy`，补丁器认它 → 判「旧版补丁」（`Flavor.Custom`+Legacy）就地重写。
  改译文只改 `out/fix-*.csv` 再重建，不要再手工维护 legacy 表（除非旧包已被覆盖，v0.4.0 那 1 条就是手补的）。
- **两种安装形态**：官方独立端 = `Content.ggpk`（单文件，`BundledGGPK`）；
  Steam/WeGame = `Bundles2/_.index.bin` + `*.bundle.bin`（`LibBundle3.Index` + `DriveBundleFactory`）。
  抽象在 dropper 的 `IContainer`；`_.index.bin` **只能一个写句柄**，必须 `CloseStore()/Reopen()`。
  Bundles2 写完要 `index.Save()` 才落盘（ggpk 是随写随存）。完整性体检 `tools/b2probe --check`。
- 本机模拟环境：国服 `E:\games\WeGameApps\流放之路`（3.25.1d，只有 `data/simplified chinese/`，
  所以 5 张 dat 表缺失、词缀 sha 对不上 → 拒写是正确的；只用于 `--struct` 验通道）。
- 修正稿：`fix-direction.csv`(19) + `fix-mult.csv`(49) + `fix-wording.csv`(3)。
  待用户拍板：合并成 fix-batch-1.csv 进游戏验证链路。
  莉莉亚 2 条捷光环已拍板：改「更多」，语序用「每保留 1%…，即有/即造成 {0}% 更多…」。
- 数据备注：more→「提高」全库仅 2 条（即莉莉亚，孤例）；「提高」92% 对 increased，
  是 increased 的次要变体；more 主译「更多」(97.5%)。「提升」36 条两头混用，待后续一致性整治。
- 术语表 `out/glossary.csv`（48 条，含 Mana Reservation Efficiency=保留效用）。
