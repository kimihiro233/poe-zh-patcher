# 译文体检报告

条目总数 **22167**｜空译文 **129**｜未翻译 **4**｜简体字渗入 **10**｜占位符不一致 **201**｜长度异常 **0**｜术语偏离 **699**


## S2 占位符不一致

`{n}` 编号被改会显示错误数值；`#` 丢失会让数值无法填入；`\n` 丢失会挤成一行。

分类：换行 124、数值占位符 60、% 百分号 17

| 原文 | 繁中 | 缺 | 多 |
|---|---|---|---|
| `Maximum of {0:+d} Geysers at a time` | 同時最多 {0} 個熔炎泉 | ['{0:+d}'] | ['{0}'] |
| `{0:+d} to Maximum Hiveborn` | 最多 {0} 隻巢裔 | ['{0:+d}'] | ['{0}'] |
| `Projectiles have {1}% chance to Return to you` | 投射物有 {0}% 機率返回你 | ['{1}'] | ['{0}'] |
| `Modifiers which would cause Projectiles to Fork or\nChain instea` | 使投射物分裂或連鎖的詞綴，改為使它們額外改變方向次數 | ['\\n'] | [] |
| `Allied Mercenaries in your Maps deal {0}% more Damage against\nM` | 你地圖中的友方傭兵對傭兵和盜賊流亡者造成 {0}% 更多傷害 | ['\\n'] | [] |
| `Allied Mercenaries in your Maps deal {0}% less Damage against\nM` | 你地圖中的友方傭兵對傭兵和盜賊流亡者造成 {0}% 更少傷害 | ['\\n'] | [] |
| `Defeated Immortal Syndicate Members in your Maps grant {0} Intel` | 你地圖中被擊敗的密教成員若沒有被審問或釋放，給予目前事件藏匿處 {0} 情報 | ['\\n'] | [] |
| `Hive Monsters in your Maps have {0}% increased Toughness per Fla` | 你地圖中的巢穴遭遇根據每一摧毀的可燃洞穴\n增加{0}%堅韌 | [] | ['\\n'] |
| `Hive Monsters in your Maps have {0}% reduced Toughness per Flamm` | 你地圖中的巢穴遭遇根據每一摧毀的可燃洞穴\n減少{0}%堅韌 | [] | ['\\n'] |
| `Your Maps contain an additional Strongbox, Imprisoned Monster, S` | 你的地圖包含{0}個額外保險箱、被囚禁的怪物、神殿、或盜賊流亡者 | [] | ['{0}'] |
| `{0}% chance on completing a Map influenced by a Conqueror of the` | 完成輿圖征服者勢力地圖，有 {0}% 機率獲得 2 倍定位他們壁壘位置的進度 | ['\\n'] | [] |
| `Maps found in your Maps have {0}% chance to have an 'Area contai` | 你地圖中掉落的地圖有 {0}% 機率\n有【你的地圖含有 1 個凋落事件】附魔 | [] | ['\\n'] |
| `{0}% increased number of Rare Monsters in your Maps per Explicit` | 地圖每有 5% 品質便增加 {0}% 地圖中稀有怪物數量 | [] | ['%'] |
| `{0}% reduced number of Rare Monsters in your Maps per Explicit M` | 地圖每有 5% 品質便減少 {0}% 地圖中稀有怪物數量 | [] | ['%'] |
| `{0}% chance for Orbs of Chance to drop as Unique Items instead i` | 你地圖中的機會石改為掉落傳奇物品 | ['{0}', '%'] | [] |
| `Bismuth Ore Deposits in your Maps add an additional Modifier to ` | 在你的地圖中，鉍礦床\n會對其影響的怪物附加一個詞綴 | [] | ['\\n'] |
| `Your Maps are inhabited by an additional Rogue Exile` | 你的地圖含有額外 {0} 個盜賊流亡者 | [] | ['{0}'] |
| `The Shaper and The Elder in The Shaper's Realm have {0}% increas` | 在塑者之界中的塑界者和異界尊師增加 {0}% 機率掉落 1 顆支配之玉 | ['\\n'] | [] |
| `Ultimatum Encounters in your Maps have {0}% increased chance for` | 你地圖中的最後通牒事件，增加 {0}% 最終回合有 1 個頭目的機率 | ['\\n'] | [] |
| `Ultimatum Encounters in your Maps have {0}% reduced chance for\n` | 你地圖中的最後通牒事件，減少 {0}% 最終回合有 1 個頭目的機率 | ['\\n'] | [] |
| `Your Maps have {0:+d}% chance to contain a Vaal Side Area` | 你的地圖 +{0}% 機率含有 1 個瓦爾區域 | ['{0:+d}'] | ['{0}'] |
| `Contains an additional Unique Item` | 內含 {0} 個額外傳奇物品 | [] | ['{0}'] |
| `Guarded by {0} packs of Magic Monsters` | 被 {1} 群魔法怪物守衛著 | ['{0}'] | ['{1}'] |
| `Guarded by {0} packs of Rare Monsters` | 被 {1} 群稀有怪物守衛著 | ['{0}'] | ['{1}'] |
| `Contains an extra Vaal Gem` | 內含 {0} 個額外瓦爾寶石 | [] | ['{0}'] |

> 方向（increased/reduced/more/less）的检查见 `tools/direction.py`：
> 必须按「正负变体对」比较，逐条比单词会把中文替英文补漏的条目误判成错译。


## S4 术语一致性

| 术语 | 主流译法 | 正向 / 反向共现 | 其他写法 |
|---|---|---|---|
| Accuracy | 命中 | 100% / 100% | 命中值(107)、增加(40)、減少(35) |
| Armour | 護甲 | 87% / 100% | 增加(65)、減少(46)、閃避(45) |
| Attack | 攻擊 | 99% / 100% | 速度(466)、傷害(413)、增加(344) |
| Aura | 光環 | 100% / 100% | 效果(86)、光環效果(68)、技能(60) |
| Block | 格擋 | 99% / 100% | 傷害(156)、格擋率(131)、傷害格擋(114) |
| Brand | 烙印 | 99% / 100% | 增加(26)、減少(25)、傷害(19) |
| Chaos | 混沌 | 99% / 100% | 傷害(294)、混沌傷害(270)、的混沌(74) |
| Chill | 冰緩 | 100% / 100% | 效果(36)、敵人(34)、冰緩效果(27) |
| Cold | 冰冷 | 96% / 100% | 傷害(313)、冰冷傷害(256)、增加(78) |
| Critical | 暴擊 | 100% / 100% | 擊率(359)、暴擊率(358)、增加(204) |
| Curse | 詛咒 | 99% / 100% | 敵人(77)、技能(65)、效果(63) |
| Damage | 傷害 | 100% / 100% | 增加(1214)、減少(1053)、造成(1044) |
| Duration | 時間 | 98% / 100% | 持續時(623)、持續時間(622)、增加(290) |
| Elemental | 元素 | 99% / 100% | 傷害(255)、元素傷害(198)、抗性(141) |
| Elusive | 靈巧 | 100% / 100% | 效果(18)、暴擊(13)、獲得靈巧(11) |
| Energy Shield | 能量護盾 | 100% / 100% | 傷害(101)、最大(94)、生命(85) |
| Evasion | 閃避 | 100% / 100% | 閃避值(129)、增加(55)、護甲(42) |
| Fire | 火焰 | 80% / 100% | 傷害(392)、火焰傷害(322)、額外(117) |
| Fortify | 護體 | 100% / 100% | 擊中(21)、近戰擊中(14)、擊中護體(11) |
| Freeze | 冰凍 | 98% / 100% | 造成(50)、造成冰凍(42)、敵人(41) |
| Gem | 寶石 | 100% / 100% | 額外(21)、機率(21)、個額外(18) |
| Ignite | 點燃 | 100% / 100% | 造成(60)、傷害(53)、敵人(40) |
| Impale | 穿刺 | 100% / 100% | 擊中(31)、敵人(30)、擊中時(24) |
| Leech | 偷取 | 99% / 100% | 生命(73)、恢復(60)、最大(48) |
| Life | 生命 | 84% / 100% | 傷害(360)、最大(240)、敵人(230) |
| Lightning | 閃電 | 88% / 100% | 傷害(292)、閃電傷害(238)、增加(83) |
| Mana | 魔力 | 98% / 100% | 增加(213)、減少(190)、保留(184) |
| Melee | 近戰 | 98% / 100% | 傷害(203)、武器(194)、近戰武器(179) |
| Mine | 地雷 | 97% / 100% | 增加(45)、減少(44)、技能(43) |
| Minion | 召喚 | 98% / 100% | 召喚物(118)、增加(35)、傷害(31) |
| Onslaught | 猛攻 | 100% / 100% | 獲得(70)、獲得猛攻(40)、秒猛攻(27) |
| Physical | 物理 | 99% / 100% | 傷害(676)、物理傷害(594)、額外(121) |
| Poison | 中毒 | 99% / 100% | 造成(79)、傷害(69)、擊中(54) |
| Projectile | 投射 | 100% / 100% | 投射物(270)、速度(79)、發射(77) |
| Quality | 品質 | 100% / 100% | 寶石(33)、掉落(24)、插槽(18) |
| Quantity | 數量 | 99% / 100% | 物品(139)、掉落(119)、增加(93) |
| Radius | 範圍 | 99% / 100% | 範圍內(294)、若範圍內(121)、天賦(118) |
| Rarity | 稀有 | 96% / 100% | 稀有度(163)、物品(150)、物品稀有(110) |
| Recovery | 恢復 | 98% / 100% | 恢復率(239)、時間(169)、冷卻(167) |
| Regeneration | 恢復 | 77% / 100% | 恢復率(50)、魔力(47)、魔力恢復(36) |
| Resistance | 抗性 | 100% / 100% | 傷害(82)、火焰(79)、火焰抗性(71) |
| Skill | 技能 | 56% / 100% | 天賦(346)、附加(307)、個附加(303) |
| Spell | 法術 | 97% / 100% | 傷害(315)、法術傷害(265)、增加(76) |
| Stun | 暈眩 | 96% / 100% | 敵人(67)、時間(46)、敵人暈眩(37) |
| Totem | 圖騰 | 93% / 100% | 增加(59)、減少(57)、傷害(41) |
| Trap | 陷阱 | 100% / 100% | 增加(72)、減少(69)、傷害(54) |
| Warcry | 戰吼 | 100% / 100% | 戰吼時(18)、使用(17)、當你(16) |
| Ward | 保護 | 95% / 100% | 破損(26)、的保護(23)、當你(20) |

### 术语偏离示例

- **Life** 主流是「生命」，此条用「傷害」：`{0}% increased Damage with Hits and Ailments against Enemi` → 對滿血敵人增加 {0}% 擊中和異常狀態傷害
- **Life** 主流是「生命」，此条用「傷害」：`{0}% reduced Damage with Hits and Ailments against Enemies` → 對滿血敵人減少 {0}% 擊中和異常狀態傷害
- **Life** 主流是「生命」，此条用「傷害」：`{0}% increased Damage when on Full Life` → 當滿血時增加 {0}% 傷害
- **Life** 主流是「生命」，此条用「傷害」：`{0}% reduced Damage when on Full Life` → 當滿血時減少 {0}% 傷害
- **Life** 主流是「生命」，此条用「傷害」：`{0}% increased Damage when on Low Life` → 貧血時增加 {0}% 傷害
- **Life** 主流是「生命」，此条用「傷害」：`{0}% reduced Damage when on Low Life` → 貧血時減少 {0}% 傷害
- **Life** 主流是「生命」，此条用「傷害」：`{0}% increased Damage with Hits against Enemies that are o` → 對貧血的敵人增加 {0}% 擊中傷害
- **Life** 主流是「生命」，此条用「敵人」：`Monsters have {0}% increased Critical Strike Chance agains` → 怪物對滿血的敵人增加 {0}% 暴擊率
- **Life** 主流是「生命」，此条用「敵人」：`Monsters have {0}% reduced Critical Strike Chance against ` → 怪物對滿血的敵人減少 {0}% 暴擊率
- **Life** 主流是「生命」，此条用「傷害」：`Monsters deal {0}% increased Damage while on Low Life` → 怪物貧血時增加 {0}% 傷害
- **Life** 主流是「生命」，此条用「傷害」：`Monsters deal {0}% reduced Damage while on Low Life` → 怪物貧血時減少 {0}% 傷害
- **Life** 主流是「生命」，此条用「傷害」：`Supported Attack Skills deal {0}% more Damage while on Ful` → 滿血時，被輔助的攻擊技能造成 {0}% 更多傷害
- **Life** 主流是「生命」，此条用「傷害」：`Supported Attack Skills deal {0}% less Damage while on Ful` → 滿血時，被輔助的攻擊技能造成 {0}% 更少傷害
- **Life** 主流是「生命」，此条用「傷害」：`Supported Skills deal {0}% increased Damage with Hits and ` → 被輔助的技能對滿血的敵人增加 {0}% 擊中和異常狀態傷害
- **Life** 主流是「生命」，此条用「傷害」：`Supported Skills deal {0}% reduced Damage with Hits and Ai` → 被輔助的技能對滿血的敵人減少 {0}% 擊中和異常狀態傷害
- **Life** 主流是「生命」，此条用「傷害」：`Supported Skills deal {0}% increased Damage when on Full L` → 滿血時，被輔助的技能增加 {0}% 傷害
- **Life** 主流是「生命」，此条用「傷害」：`Supported Skills deal {0}% reduced Damage when on Full Lif` → 滿血時，被輔助的技能減少 {0}% 傷害
- **Life** 主流是「生命」，此条用「傷害」：`Supported Skills Deal {0}% increased Damage while on Low L` → 貧血時，被輔助的技能增加 {0}% 傷害
- **Life** 主流是「生命」，此条用「傷害」：`Supported Skills Deal {0}% reduced Damage while on Low Lif` → 貧血時，被輔助的技能減少 {0}% 傷害
- **Life** 主流是「生命」，此条用「傷害」：`Supported Skills deal {0}% increased Damage with Hits agai` → 被輔助的技能對貧血的敵人增加 {0}% 擊中傷害

## S1 简体字渗入

字形频次：减×6、内×4
- `Adds {0} to {1} Chaos Damage if you've taken a Criti` → 近期內你若有受到暴擊，短時間内附加 {0} 至 {1} 混沌傷害　（内→內）
- `Enemies you Shock have {0}% reduced Cast Speed` → 被你感電的敵人减少 {0}% 施放速度　（减→減）
- `Enemies you Shock have {0}% reduced Movement Speed` → 被你感電的敵人减少 {0}% 移動速度　（减→減）
- `{0}% increased Burning Damage if you've Ignited an E` → 近期你若有點燃敵人，短時間内增加 {0}% 燃燒傷害　（内→內）
- `{0}% reduced Burning Damage if you've Ignited an Ene` → 近期你若有點燃敵人，短時間内減少 {0}% 燃燒傷害　（内→內）
- `{0}% reduced Critical Strike Chance with arrows that` → 分裂的箭矢减少 {0}% 暴擊率　（减→減）
- `{0}% reduced Area of Effect for Attacks` → 减少攻擊技能 {0}% 範圍效果　（减→減）
- `You have Phasing if you've Killed Recently` → 若你近期有擊殺，短時間内獲得迷蹤效果　（内→內）
- `Players and Monsters have {0}% reduced Critical Stri` → 怪物和玩家减少 {0}% 暴擊率　（减→減）
- `{0}% reduced Movement speed while on Burning, Chille` → 在燃燒、冰緩、感電地面上時减少 {0}% 移動速度　（减→減）

## S5 长度异常
