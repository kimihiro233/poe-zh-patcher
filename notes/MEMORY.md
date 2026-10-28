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

## 项目位置与产物

- 项目在 `E:/projects/poe-zh/`；脚本 `tools/`，产物 `out/`。
  **git tag**：v0.1.0 分析基线 / v0.2.0 回写打通 / v0.3.0 版本绑定（= 游戏 3.29.3b）。
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
  与拖放式单文件补丁器 `dist/PoE-zh-Patcher.exe`（v0.4.1，ggpk 与 Bundles2 双形态，
  四版本任意切换，`--struct` 结构演练、`--selftest` 数据自检）。
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
