# 译文体检报告

条目总数 **322198**｜空译文 **593**｜未翻译 **198543**｜简体字渗入 **283**｜占位符不一致 **482**｜长度异常 **27**｜术语偏离 **1246**


## S2 占位符不一致

`{n}` 编号被改会显示错误数值；`#` 丢失会让数值无法填入；`\n` 丢失会挤成一行。

分类：% 百分号 314、数值占位符 165、# 数量符 3

| 原文 | 繁中 | 缺 | 多 |
|---|---|---|---|
| `Level up a Gem to 20` | 品質20%再練一次 | [] | ['%'] |
| `Have five flasks of at least Magic rarity and 20% quality equipp` | 裝備至少 5 個品質 20 以上的魔法藥劑，其中至少含有一個帶有付魔的藥劑。 | ['%'] | [] |
| `Complete Beyond Encounters` | #超越 | [] | ['#'] |
| `Break 150 Astral Chains in Mirages of Maps that are Rare, at lea` | 在稀有且至少為階級 14 的地圖蜃景中，破壞 250 條星界鎖鏈。 | ['%'] | [] |
| `Release a ferocious burst of lightning from your bow. The burst ` | 發射會穿透敵人的投射物。轉化 50% 的物理傷害至閃電傷害。箭矢發射時的巨大力量爆發出一道閃電，對前方扇形造成範圍閃電傷害。 | [] | ['%'] |
| `You are now #{0} in the arena.` | 你目前在競技場中排名第 {0} 。 | ['#'] | [] |
| `The next race event starts in {1}. To enter, go to the character` | 下一場競賽將在 {1} 後開始。欲參與者，請回到角色選擇畫面並點選「參加」。相關詳細資訊請前往論壇確認。 | ['{0}'] | [] |
| `Thanks for your feedback! Your reference number is {0}` | 感謝你的回報！ | ['{0}'] | [] |
| `number_of_additional_totems_allowed, totem_life_+%, totem_durati` | 測試 | ['%'] | [] |
| `trap_trigger_radius_+%, trap_throwing_speed_+%, mine_laying_spee` | 測試 | ['%'] | [] |
| `` | 你的物理傷害轉換為混沌傷害，且有 25% 機率擊中時造成凋零 | [] | ['%'] |
| `Point Change: <valuedefault>{{{0}}}` | 分數變動：{0} | ['valuedefault'] | [] |
| `Rating Change: <valuedefault>{{{0}}}` | 積分變動：{0} | ['valuedefault'] | [] |
| `Leo Reputation Gained: <valuedefault>{{{0}}}` | 獲得里歐友好度：{0} | ['valuedefault'] | [] |
| `Time Left: <valuedefault>{{{0}}}` | 剩餘時間：{0} | ['valuedefault'] | [] |
| `Round <valuedefault>{{{0}}} of <valuedefault>{{{1}}}` | 第 {0} 之 {1} 回合 | ['valuedefault'] | [] |
| `` | 由於不符合需求，你無法升級這顆{0}寶石。 | [] | ['{0}'] |
| `` | 按下 <<xbox_button_a>> 分配。 | [] | ['xbox_button_a'] |
| `` | 按下 <<xbox_button_a>> 取消分配。 | [] | ['xbox_button_a'] |
| `This build is intended for {0} characters.` | 這個流派是為了 | ['{0}'] | [] |
| `` | 按住<<xbox_button_a>>取消堆疊。 | [] | ['xbox_button_a'] |
| `Has {0} Skin. You can reclaim this by pressing <<xbox_button_rst` | 一般 | ['{0}', 'xbox_button_rstick', 'xbox_button_b'] | [] |
| `Use Bound Key ({0})` | 按住自訂按鍵拾取物品 | ['{0}'] | [] |
| `` | 按下<<xbox_button_a>>儲存變更 | [] | ['xbox_button_a'] |
| `` | 按下<<xbox_button_a>>儲存變更 | [] | ['xbox_button_a'] |

> 方向（increased/reduced/more/less）的检查见 `tools/direction.py`：
> 必须按「正负变体对」比较，逐条比单词会把中文替英文补漏的条目误判成错译。


## S4 术语一致性

| 术语 | 主流译法 | 正向 / 反向共现 | 其他写法 |
|---|---|---|---|
| Accuracy | 命中 | 100% / 100% | 增加(13)、傷害(11)、暴擊(10) |
| Armour | 外觀 | 54% / 100% | 身體(936)、你的(914)、身體外觀(898) |
| Attack | 攻擊 | 96% / 100% | 傷害(148)、速度(121)、增加(99) |
| Aura | 光環 | 99% / 100% | 效果(91)、光環效果(82)、的光環(75) |
| Bleed | 流血 | 77% / 100% | 造成(13)、機率(12)、輔助(8) |
| Block | 格擋 | 84% / 100% | 格擋率(29)、法術(25)、攻擊(21) |
| Brand | 烙印 | 83% / 100% | 技能(19)、使用(12)、的技能(11) |
| Chaos | 混沌 | 97% / 100% | 傷害(192)、混沌傷害(124)、沌石(81) |
| Chill | 冰緩 | 78% / 100% | 傷害(16)、敵人(16)、冰凍(15) |
| Cold | 冰冷 | 78% / 100% | 傷害(187)、冰冷傷害(123)、抗性(68) |
| Critical | 暴擊 | 94% / 100% | 暴擊率(83)、傷害(61)、增加(54) |
| Curse | 詛咒 | 76% / 100% | 光環(20)、技能(18)、敵人(17) |
| Damage | 傷害 | 97% / 100% | 造成(537)、物理(384)、增加(378) |
| Duration | 時間 | 88% / 100% | 持續(121)、持續時間(101)、傷害(71) |
| Elemental | 元素 | 95% / 100% | 傷害(114)、元素傷害(82)、的元素(52) |
| Elusive | 靈巧 | 83% / 100% | 效果(5)、技能(4)、獲得(4) |
| Energy Shield | 護盾 | 96% / 100% | 能量(195)、能量護盾(194)、生命(62) |
| Evasion | 閃避 | 96% / 100% | 閃避值(46)、護甲(21)、增加(16) |
| Fire | 火焰 | 54% / 100% | 傷害(231)、火焰傷害(145)、造成(86) |
| Fortify | 護體 | 89% / 100% |  |
| Freeze | 冰凍 | 79% / 100% | 傷害(25)、冰冷(24)、機率(19) |
| Gem | 寶石 | 94% / 100% | 技能(131)、輔助(110)、能寶石(108) |
| Ignite | 點燃 | 100% / 100% | 傷害(40)、火焰(22)、火焰傷害(19) |
| Impale | 穿刺 | 100% / 100% | 效果(11)、穿刺效果(8)、擊中(8) |
| Leech | 偷取 | 93% / 100% | 生命(39)、傷害(34)、生命偷取(26) |
| Life | 生命 | 77% / 100% | 回復(140)、傷害(127)、你的(94) |
| Lightning | 閃電 | 72% / 100% | 傷害(202)、閃電傷害(141)、造成(86) |
| Mana | 魔力 | 92% / 100% | 回復(67)、生命(59)、你的(44) |
| Melee | 近戰 | 96% / 100% | 傷害(95)、使用(76)、遠距(75) |
| Mine | 礦坑 | 54% / 100% | 地雷(79)、碧藍礦(72)、碧藍礦坑(63) |
| Minion | 召喚 | 82% / 100% | 召喚物(77)、傷害(23)、使用(17) |
| Onslaught | 猛攻 | 88% / 100% | 猛攻聯盟(8)、聯盟中(7)、使用(7) |
| Physical | 物理 | 97% / 100% | 傷害(386)、物理傷害(316)、造成(119) |
| Poison | 中毒 | 73% / 100% | 傷害(45)、造成(30)、中毒傷害(26) |
| Projectile | 投射 | 95% / 100% | 投射物(88)、傷害(39)、敵人(34) |
| Quality | 品質 | 94% / 100% | 寶石(86)、增加(43)、等級(38) |
| Quantity | 數量 | 92% / 100% | 物品(115)、物品數量(98)、至少(57) |
| Radius | 範圍 | 88% / 100% | 線範圍(10)、光線範圍(9)、超越傳送(8) |
| Rarity | 稀有 | 94% / 100% | 稀有度(118)、物品(85)、品稀有度(56) |
| Recovery | 恢復 | 72% / 100% | 生命(24)、能量護盾(15)、回復(15) |
| Regeneration | 回復 | 94% / 100% | 生命(45)、生命回復(38)、魔力(32) |
| Resistance | 抗性 | 98% / 100% | 火焰(63)、冰冷(63)、閃電(59) |
| Skill | 技能 | 57% / 100% | 天賦(303)、輔助(196)、使用(168) |
| Spell | 法術 | 87% / 100% | 傷害(105)、技能(75)、輔助(69) |
| Stun | 暈眩 | 80% / 100% | 敵人(15)、門檻(14)、暈眩門檻(13) |
| Suppression | 部隊 | 62% / 100% | 鎮壓部隊(4) |
| Totem | 圖騰 | 99% / 100% |  |
| Trap | 陷阱 | 97% / 100% | 傷害(27)、閃電(20)、投擲(18) |
| Warcry | 戰吼 | 100% / 100% | 技能(10)、攻擊(9)、敵人(8) |
| Ward | 保護 | 33% / 100% | 守衛(10)、魯的戰徽(7)、卡魯的戰(7) |

### 术语偏离示例

- **Life** 主流是「生命」，此条用「傷害」：`All Teammates leech 100% of damage they deal as life to th` → 全部友方偷取 100% 他們造成的傷害給祖靈圖騰 20 秒
- **Life** 主流是「生命」，此条用「傷害」：`Specialty: <ancestralprimary>{Revives Allies}

Tribal Pass` → 特性: <ancestralprimary>{復活友方}

部落天賦: <enchanted>{詛咒抗性}

<an
- **Life** 主流是「生命」，此条用「傷害」：`Damage on Full Life Support` → 滿血傷害輔助
- **Life** 主流是「生命」，此条用「傷害」：`You are cursed. You will take increased damage while on lo` → 你被詛咒了，你貧血時增加承受傷害，且當你擊中敵人時被虛弱。
- **Life** 主流是「生命」，此条用「傷害」：`Melee Damage from Hits is taken from your Stone Golems' Li` → 近戰傷害先從你的巨石魔像扣除
- **Life** 主流是「生命」，此条用「回復」：`Fire Damage from Hits is taken from this Aegis before your` → 被擊中時，火焰傷害會先由此屏障承受。若近期沒受到火焰傷害，聖盾會回復滿溢狀態。
- **Life** 主流是「生命」，此条用「回復」：`Cold Damage from Hits is taken from this Aegis before your` → 被擊中時，冰冷傷害會先由此屏障承受。若近期沒受到冰冷傷害，聖盾會回復滿溢狀態。
- **Life** 主流是「生命」，此条用「回復」：`Elemental Damage from Hits is taken from this Aegis before` → 被擊中時，元素傷害會先由此屏障承受。若近期沒受到元素傷害，聖盾會回復滿溢狀態。
- **Life** 主流是「生命」，此条用「回復」：`Lightning Damage from Hits is taken from this Aegis before` → 被擊中時，閃電傷害會先由此屏障承受。若近期沒受到閃電傷害，聖盾會回復滿溢狀態。
- **Life** 主流是「生命」，此条用「回復」：`Physical Damage from Hits is taken from this Aegis before ` → 被擊中時，物理傷害會先由此屏障承受。若近期沒受到物理傷害，聖盾會回復滿溢狀態。
- **Life** 主流是「生命」，此条用「回復」：`Elemental Damage from Hits is taken from this Aegis before` → 被擊中時，元素傷害會先由此屏障承受。若近期沒受到元素傷害，聖盾會回復滿溢狀態。
- **Life** 主流是「生命」，此条用「傷害」：`Cleave has Culling Strike, a larger Area of Effect and dea` → 劈砍有撲殺，有較大的範圍效果和對貧血的敵人造成更多傷害。
- **Life** 主流是「生命」，此条用「傷害」：`Your melee weapons deal increased damage against enemies o` → 增加你近戰武器對貧血敵人造成的傷害
- **Life** 主流是「生命」，此条用「你的」：`You swore, sinew and soul, to serve God and the Order of t` → 你誓死支持並且奉獻靈魂來服侍偉大的神，以及聖堂武僧的紀律。多年來你的生活都建立在信仰上，但罪孽只輕輕一碰，你堅守的信
- **Life** 主流是「生命」，此条用「回復」：`An item type that sits in the flask slots and is used to g` → 放置於藥水欄位的道具，不同的藥水在使用後能獲得不同的效果。擊殺、返回城鎮、藏身處皆可回復藥水的充能。
- **Life** 主流是「生命」，此条用「回復」：`Other magic mods can enhance the amount of life or mana a ` → 有些魔法詞綴可以加強回復的總血量或魔力、或持續時間、或可使用的次數。有的也會提供額外的功能，像是移除詛咒或異常狀態。
- **Life** 主流是「生命」，此条用「你的」：`The virtuous are rewarded - in death, if not in life.` → 你的美德將獲得獎勵 - 即使不是在生前，也會在死後。
- **Life** 主流是「生命」，此条用「你的」：`To extend your life as long as possible
is to extend all ` → 延長你的壽命
一併延長伴隨著的疫病。
- **Life** 主流是「生命」，此条用「的生」：`A life without risk is no life at all.` → 沒有風險的生活根本就不是生活。
- **Life** 主流是「生命」，此条用「你的」：`The Order was your clan in life, Agnar, Beastmaster, but t` → 野獸專家艾格納，教團過去是你的家族，但初始者喚回
他們最喜愛的兒子。他們幻視的天賦將會轉移至其它人。

## S1 简体字渗入

字形频次：“×70、”×37、雇×30、斗×17、注×16、纺×12、伙×7、裏×7、恒×6、厩×6、霉×5、绗×5、绖×5、塔×5、爾×5、䅉×5、癡×4、鳕×4、祢×4、之×4
- `Rancor` → 冤仇者　（仇→讎）
- `Rancor` → 冤仇者　（仇→讎）
- `Ghroth, Sporebreath Corruptor` → 菌息好戰锗．葛拉夫　（锗→鍺）
- `Eternal Labyrinth of Fortune` → 命運的永恒帝王迷宮　（恒→恆）
- `Eternal Labyrinth of Opportunity` → 機會的永恒帝王迷宮　（恒→恆）
- `Eternal Labyrinth of Potential` → 潛能的永恒帝王迷宮　（恒→恆）
- `Eternal Labyrinth of Fortune` → 命運的永恒帝王迷宮　（恒→恆）
- `Eternal Labyrinth of Opportunity` → 機會的永恒帝王迷宮　（恒→恆）
- `Eternal Labyrinth of Potential` → 潛能的永恒帝王迷宮　（恒→恆）
- `Shape these Maps` → 岳父藍圖　（岳→嶽）
- `Defeat Alluring Abyss Trio` → 众行不行　（众→眾）
- `践` → 践　（践→踐）

## S5 长度异常

- `Reclaim a cosmetic effect from an item (`(93) → 小技巧：
<default>{- 多個關鍵字以空白鍵分隔。僅反白包含所有關鍵字的(307)
- `Microtransactions`(17) → Art/Videos/Tutorial_StashSearch_Xbox.BK2(40)
- `Guild & Invites`(15) → Art/Videos/Tutorial_MapOpen_Xbox.bk2(36)
- `Enter Instance`(14) → Art/Videos/Tutorial_Ascend_Xbox.bk2(35)
- `<white>{Undead} Skeletons`(25) → <white>{不死}怪物會造成<craftingcold>{冰冷}傷害
不時(59)
- `<white>{Undead} Skeletons`(25) → <white>{不死}怪物會造成<craftingcold>{冰冷}傷害
不時(59)
- `Ranged <white>{Undead} Skeletons`(32) → 遠距<white>{不死}怪物會造成<craftingphysical>{物理}(82)
- `Ranged <white>{Undead} Skeletons`(32) → 遠距<white>{不死}怪物會造成<craftingphysical>{物理}(82)
- `<white>{Beasts}`(15) → <white>{不死}怪物會造成<craftingphysical>{物理}傷害(40)
- `<white>{Beasts}`(15) → <white>{不死}怪物會造成<craftingphysical>{物理}傷害(40)