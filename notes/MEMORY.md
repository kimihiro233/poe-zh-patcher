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
- **ggpk 千万别逐文件落盘（v0.4.1 修，血的教训）**：`FileRecord.Write(..., saveIndex:true)` 每写一个文件就把
  20 MB `_.index.bin` 重写进 ggpk 一次，31 个文件 31 次，反复吃/让空闲块 → 复用到
  `Bundles2/LibGGPK3/*.bundle.bin` 的位置就把它挤没了，索引记录还在 → 进游戏
  `Cannot find bundle: "Bundles2/LibGGPK3/1.bundle.bin" in GGPK`。
  现在 ggpk 也走「收齐改动 → 结尾一次 `Save()`」。写入前 `BundleHygiene.Consolidate()` 把重名/悬空的自建
  bundle 记录收成一条（`Index.Save()` 删空 bundle 是按路径删的，重名会连带删掉活的那个）。
  已经写坏的 ggpk 用 `tools/ggpkdoctor`：体检 + 备份到 `out/ggpk-backup-*/` + 归并 + 补写。
- **`ObjectDisposedException` 认对象名**：报 `LibBundle3.Bundle` = **容器已被释放还在用**
  （`Index.EnsureNotDisposed()` 转调 `baseBundle`，抛的是基准 bundle 的名字），不是数据问题。
  v0.4.1 的实例：菜单 `[t]` 结构演练 / `[c]` 采集内部 `CloseStore()` 换容器，回到菜单后仍用最初那个死句柄
  → 每个文件一行 `Cannot access a disposed object.`。现在 `IContainer.Alive` + 菜单每轮取当前活着的容器。
  **凡是「先演练/采集，再安装」的路径都要复核句柄**。
- **远端排障先跑 `--writetest[=N]`**：把 `_.index.bin` 复制到临时目录，在副本上写 N 个文件 + 落盘 + 回读，
  不碰客户端 → 一眼分清是「写入通道」问题还是「内容对不上」问题。ggpk 太大不适用（用 `--struct`）。
  `dist/PoE-zh-Patcher.exe` 要管理员权限，本机自测用 `dotnet ...PoE-zh-Patcher.dll`。
- 本机模拟环境：国服 `E:\games\WeGameApps\流放之路`（3.25.1d，只有 `data/simplified chinese/`，
  所以 5 张 dat 表缺失、词缀 sha 对不上 → 拒写是正确的；只用于 `--struct` 验通道）。
- 修正稿：`fix-direction.csv`(19) + `fix-mult.csv`(49) + `fix-wording.csv`(3)。
  待用户拍板：合并成 fix-batch-1.csv 进游戏验证链路。
  莉莉亚 2 条捷光环已拍板：改「更多」，语序用「每保留 1%…，即有/即造成 {0}% 更多…」。
- 数据备注：more→「提高」全库仅 2 条（即莉莉亚，孤例）；「提高」92% 对 increased，
  是 increased 的次要变体；more 主译「更多」(97.5%)。「提升」36 条两头混用，待后续一致性整治。
- 术语表 `out/glossary.csv`（48 条，含 Mana Reservation Efficiency=保留效用）。

## 已知问题：对照版长行会被面板软折行（2026-10-29 记录，用户决定暂不处理）

- 面板宽度约 **100 个半角字符**。对照版追加的英文单段超过这个宽度就会被**软折行**，
  折出来的那截**不带缩进**，看着像凭空多出一条词缀。
- 实例：`keystone_elemental_overload`（元素超载）英文首段 103 字符，
  在 `…with Hits / and Ailments` 处被折断，`and Ailments` 顶格。
  中文首段 30 个全角字符（≈60 半角）**不超宽**，所以只有英文侧会断。
- 规模（不是孤例）：英文单段 > 90 字符 **848 段**（stat 406 / atlas 121 / skill 85 / gem 70 /
  minion_attack 35 / minion 24 / passive 15 / leaguestone 14）；中文单段 > 30 字符 1463 段。
  一旦要处理就是全局口径，不能只改一条。
- 真要处理时的两条路，各有硬伤：
  1. **按段长自动折行**：折点会落在词中间（103 字符按 92 折会断在 `Elemental` 后），比软折行更难看；
  2. **逐条人工指定折点**（`out/fix-wrap.csv`，中英同步折，段数必须相等才能一组配一组）：
     质量最好，但只适合少量条目，848 段不可能全人工。
- 已经做掉的部分（本轮）：`dual_body()` 从「整段英文堆在整段中文后面」改成
  **一组中文配一组英文**的穿插；段数一致（860 行，2–8 组）才穿插，
  不一致（135 行）退回「整段附在末尾」但**每组都补缩进**，不会再顶格。

## 语系：客户端按 languages.dat 里的「语言名」去找数据目录（2026-10-29，v0.4.5 修正）

- 我们往 `metadata/statdescriptions` 写多少中文都**不影响客户端显示什么语言**。
  决定因素是 `data/languages.dat` 这张语言表（游戏语言列表就是它的 11 行）。
- **客户端按这一行的语言名去找 `data/<语言名>/`**（采集包里那条
  `data/traditional chinese/uitalktext.datc64` 就是证据）；语言代码（`fr` / `zh-TW`）
  只影响 `production_Config.ini` 的 `language=fr` 和国旗图标。
- 所以 PoeChinese3 的 `ApplyTraditionalChinese` 换的是**名字**，不是代码：把 French 行的
  「显示名 + 本地名」与 Traditional Chinese 行的**整对调换**（`[1]`、`[2]` 两个字段互换），
  code 原地不动。换完列表第二项写着「繁體中文」、code 仍是 `fr`，选它就加载官方繁中 + 我们的译文。
  这也是它「游戏更新后不用重打」的原因——它本来就没改译文。
- 该文件 **688–696 B / 11 行**，UTF-16LE、字符串内联、`0x0000` 结尾，**无字符串表、无偏移量**，
  所以整段搬移是安全的（长度守恒，后续字段整体平移）。
  每行字段：id、英文名、本地名、code、（可选第二 code）、几个标志位。
  行序 en / fr / de / pt-BR / ru / th / zh-TW / zh-CN / es / ko-KR / ja-JP。
- **判断是否已执行过**：两对名字谁在前 —— `French` 在 `Traditional Chinese` 之前 = 未启用，
  反之 = 已启用。任一锚点找不到就判 `Unknown`，整段跳过，绝不硬写。
- 已实现：`tools/shared/LanguageSwitch.cs`（patcher / dropper 共用）；装补丁启用、
  还原时关掉、已是目标状态不写。`--status` 报一行「语系：…」；
  `--locale-only[=original]` 只处理这一步；`--lang-test=<文件>` 跑「启用→还原」往返自检。
- **v0.4.4 及更早的实现是错的**：交换的是两个 code，且判定条件（`zh-TW` 在 `French` 之前才算未启用）
  在真实文件里永远不成立 → `Detect` 恒为 Unknown → 一直「跳过」，语系从未写入过。
  另有一个门槛：`todo.Count == 0`（译文已是最新）时直接 return，也轮不到语系/国旗。
- 另两件 PoeChinese3 会做、我们**没做**的：换字体（需随包带字体文件）。换国旗已做，见下节。
- 用户 `production_Config.ini` 里 `language=fr` 是正常的——那个 fr 正是被换过的繁中槽位。

## 国旗：语言列表那一项该显示五星红旗（2026-10-29，v0.4.5 改做法）

- 客户端按**语言代码**拼图标名：`FlagIcons/<code>`，code 就是 `languages.dat` 那行第四字段。
  语系换成繁中之后 code 仍是 `fr`，所以显示法国国旗。
- `art/uiimages1.txt` 是图集索引：6.0–6.1 MB、UTF-16LE、**纯 LF 无 CR**、约 19.5k 行，
  每行 `"图标名" "贴图路径" x1 y1 x2 y2`。11 语言 × 2 分辨率 = 22 条。
- **图集里本来就有五星红旗**：`zhCN` 那一项（756,0–830,49）。
- **不要写死坐标**：fr 的位置每个版本都不一样（3.25 = 252,0,326,49；3.29 = 840,0,914,49）。
  v0.4.4 写死 3.29 的坐标，在 3.25 上 `Detect` 立刻判「无法识别」，国旗也跟着没换。
- 正确做法：把 `fr` 与 `zhCN` 两行的**图标名对调**（客户端取 `fr` 时拿到写着 `zhCN` 的那一行）。
  不碰坐标、不碰 DDS、天然可逆；4K 那一对存在才处理，缺失不算异常。
  与 PoeChinese3 的 `ApplyNationalFlag` 同源（它换的是 `fr` ↔ `zhTW`，青天白日旗）。
- 已实现 `tools/shared/FlagIcon.cs`；`--flag-test=<文件>` 跑往返自检。
- 副作用：列表里「简体中文」那一项换完反而成法国国旗。那一项没有数据目录，
  选了只会回落英文，不影响使用。

## 写入安全：只有「全部成功」和「一个都没写」两个合法状态（2026-10-29，v0.4.7）

- Bundles2 的写入是「所有文件先写进新 bundle，最后 `Save()` 写索引」。所以中途停下 =
  索引里有一批指向新 bundle 的记录、数据却只写了一半 —— 游戏不报错，只是解出错字节。
  因此**不允许「跳过这一个、继续写下一个」**，只能整份成功或整份退回。
- 事务三步，顺序不能换：**复制索引 → 写 → Save → 关索引重开回读 → 全过才删 journal**。
  journal 用「写临时文件 + 改名」落盘，且必须在 `Save()` 之前；`phase=saved` 在
  `Save()` 返回后立刻写。崩在 `Save()` 中间就是 `writing`，回滚是对的。
- **`TrimOrphans` 必须在提交之后**。先删文件、再回滚索引 = 把「有记录、无文件」
  重新造出来 → 悬空记录 → `Buffer Underflow`。v0.4.6 把它放在 `Flush()` 里是错的。
- 启动时只看 journal 在不在，在就无条件回滚。宁可撤销一次成功安装（重跑一次即可），
  也不把没人验过的索引交给游戏。
- 没有索引还原点就一个字节都不写。老式 `Content.ggpk`（74 GB）整份复制不现实 →
  默认拒绝，只有显式 `--allow-no-rollback` 才放行。
- 备份放 `Bundles2/.poe-zh/`：同盘（复制 21 MB 几百毫秒）、Steam 校验不删、游戏不扫。
- **statdesc 没有内嵌原文字节**（成品是「原文 + 译文」现算的），所以「整份换成我们的」
  对它做不到；只能「逐行撤我们写过的行，认不出的行保留」。`.datc64` 有内嵌 `Orig`/`Patch`，
  整份换得动。

## ggpk 的备份：不需要复制 74 GB，只要记下被覆盖的那 21 MB（2026-10-29，v0.4.8）

- 真客户端实测：装 1 个文件，落盘只改写 **43 块 / 20.6 MB**，**文件长度不变** ——
  ggpk 的改写是原地覆盖，旧字节写下去就永久没了。所以「备份」= 写前记下这些块的原文。
- 实现：`BundledGGPK` 收 `Stream`，把 `FileStream` 包一层 `UndoStream`，`Write` 里先
  `Note(pos,len)`（读原文 → 追加 undo.bin → 刷盘）再真正写。**顺序反了就没有后悔药**。
- 只记「第一次碰到某段」：区间表升序、不相交、相邻合并。重复覆盖同一块只占一份。
- 追加到文件尾的字节不记、回滚**不截断**：那些字节没有记录指向它，游戏读不到；
  截断反而是「削掉一截」的风险。
- 退出前必须把 `phase` 改成 `rolledback`/`committed` 并删日志，否则下次启动会再回滚
  一遍（幂等但误报「上次没走完」）。
- `--restore-ggpk` 的快照是「那一刻」的，之后游戏更新过就不能用 → 先报时间戳+长度要确认。
- `FileRecord.Offset` 是 **bundle 内**的偏移，不是 ggpk 偏移。别拿它算文件里第几字节。

## 认「我们写过的东西」要比行，不能比整块 sha（2026-10-30，v0.4.9）

- 症状：25 个文件明明是我们自己写的，却被判「未知」，全量替换直接中止。
- 根因：`Detect` 用整块 sha 认状态，而 v0.4.3 改过 `dual_body` 的排列方式
  （英文整段附末尾 → 逐组穿插）。内容一样、只是行的顺序不同，整块 sha 就全变了。
- 判据改成**行级**：行池 = O∪T∪D∪E∪Legacy 的所有行（去标记、去首尾空白）；
  每格块内行必须全部落在池里，且至少一行来自译文类 → 判 `DualLegacy`，接管。
  有一行落不进去 → 不是我们写的 → 不接管。这一条同时挡住「别人的汉化」和「译文被改坏」。
- 判据不认拼法、只认行：以后再改多少次排列，老客户端都还认得出来。别再往
  `KnownText` 里堆「第 N 版拼法」的枚举 —— 那是把判据绑死在当前写法上。
- 标记层不做继承（DualLegacy 直接整块换目标）：我们自己的产物标记本就派生自 O/T。
  要保住别人加的颜色，那种文件会先被 `TryCustomBase` 判 `Custom` 走 merge。
- `--on-modified=skip` 现在自己就放行（`tolerate = partial || policy == Skip`），
  不用再叠 `--partial`。

## 基准：把「第一次见到的样子」当常量，结果就与改过几次无关（2026-10-30，v0.5.0）

- 问题不是「某次写错」，而是**每次都从上一次结果上算**：statdesc 96.3% 的行（717,656 里
  的 691,021 行）不参与翻译、原样继承，某次被改歪之后每次都继承，越修越歪且无声。
- 试过的方向：往发布包里塞官方原文（+5.9 MB）。被否，理由正当 —— 目标不是保原文，
  是保证「反复打补丁之后游戏还能打开」。这些目标的最优解不一样。
- 最终方案：`基准 = 该文件在该游戏版本下第一次被本程序看到的完整字节`，
  每次写入 = 基准 + 目标 flavor。输入是常量 → 结果恒定。
- 建基准时**尽量取原文**：当前是译文就从数据包还原，`sha == sha_original` 才标 Original。
  实测 47/47 全部从「中文+英文对照」精确逆推回官方原文。
- 四条例：只存一次（覆盖就不是常量了）、按游戏版本分目录、不参与备份轮转、
  sha 对不上就拒绝使用。
- 副产品：「当前内容认不出」时可以直接整份写回基准，不再逐行猜 —— 这才是真还原。
- 单文件 exe 的约束：备份跟**容器**走（`<游戏目录>/.poe-zh/`），不能跟 exe 走
  （会被覆盖、`BaseDirectory` 是临时解压目录）。
- `BackupStore.Add` 原来 `catch { }` 静默吞异常，等于没退路也照写。现在建不出目录就拒绝写入。
- .NET 的 `Encoding.UTF8` **带 BOM**，写 JSON 要用 `new UTF8Encoding(false)`，否则别的工具读不了。

## 工程纪律（v0.5.3 起的硬规矩）

- **MSBuild 的 props/targets 里，XML 注释不能出现连续两个减号。** 写了整份文件被判
  「无效」→ 静默跳过导入 → 属性没生效 → 命令成功、文件缺失、零日志。判断方法：
  `dotnet restore -v diag` 搜「由于文件无效」。凡是「属性设了但没效果」，先看这一条。
- **7 个工程只有 dropper 常编，其余随时可能是坏的。** v0.5.3 的 CI 第一次跑就发现
  `ggpkdoctor` 从 v0.5.0 起一直编不过（引用了已改名的 `BundleHygiene.Consolidate`）。
  改 `tools/shared/*` 的公开 API 之后，要么全量编一遍，要么跑 `python tools/run_tests.py --build`。
- **测试清单只在 `tools/run_tests.py` 里有一份。** 本地和 CI 跑同一条命令，判定只看
  退出码；缺语料的用例 SKIP 并说明原因，不许静默通过。
- **依赖版本靠 7 份 `packages.lock.json`，不靠记忆。** 非 NuGet 的二进制靠
  `tools/lib/deps.json` 钉 sha256（`DEPS.md` 是生成的，别手改）。
- **打包前树必须干净。** `release.py` 查 `git status --porcelain`；脏树打出来的包，
  事后没人说得清是哪个版本。发布顺序：提交代码+VERSION+bundle → 干净树上 release →
  单独提交 releases/ 并打 tag。
- **不承诺可复现构建，承诺可核验。** BUILD-INFO.json（commit/SDK/两个 sha256）+
  sbom.cdx.json（CycloneDX 1.5，serialNumber 用 uuid5 算，重打不发散）+
  MANIFEST.sha256。单文件发布做不到位级一致，别对外说「可复现」。
- **推送用 `git -c http.proxy= -c http.sslBackend=openssl push`。** schannel 会报
  `CRYPT_E_REVOCATION_OFFLINE`，`schannelCheckRevoke=false` 无效。远端分支是 `master`。

## 待拍板 / 未验证

- `oo2core.dll`（Oodle 运行时，RAD/Epic）**不可再分发**，目前仍在仓库里；
  `LibGGPK3` 是 **AGPL-3.0-or-later** 且仓库没有 LICENSE 文件 —— 两条都在
  `tools/lib/DEPS.md` 里记着，等作者决定。
- `ggpkdoctor --fix` 没在真机上验证过（手上没有已写坏的 ggpk）。
- `tools/lib` 下 `DatDefinitions.json` + 两份 `schema*.min.json` 共 3.1 MB，全仓库 0 引用。
