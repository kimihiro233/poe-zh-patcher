# -*- coding: utf-8 -*-
"""乘法口径（more/less）误译为加减（增加/减少）的修正稿生成器。
判据：stat_id 含 _final 且英文含 more/less（GGG 的乘法标记），
      但中文用了 增加/減少/提高/提升/降低 而非 更多/更少。
"""
import csv, re

SRC = "out/statdesc.csv"
DST = "out/fix-mult.csv"

FIX = {
 ("map_unique_monster_tribute_multiplier_final_+%", "Unique Monsters slain at Ritual Altars in your Maps grant {0}% more Tribute"):
   "你地圖中的傳奇怪物在祭祀神壇被擊殺時，獲得 {0}% 更多貢禮",
 ("map_unique_monster_tribute_multiplier_final_+%", "Unique Monsters slain at Ritual Altars in your Maps grant {0}% less Tribute"):
   "你地圖中的傳奇怪物在祭祀神壇被擊殺時，獲得 {0}% 更少貢禮",
 ("support_inspiration_mana_cost_+%_final", "Supported Skills have {0}% more Mana Cost"):
   "被輔助的技能有 {0}% 更多魔力消耗",
 ("support_inspiration_mana_cost_+%_final", "Supported Skills have {0}% less Mana Cost"):
   "被輔助的技能有 {0}% 更少魔力消耗",
 ("skill_used_by_sacred_wisp_damage_+%_final", "Supported Skills deal {0}% more Damage when used by Sacred Wisps"):
   "被輔助的技能被聖潔妖精使用時，造成 {0}% 更多傷害",
 ("skill_used_by_sacred_wisp_damage_+%_final", "Supported Skills deal {0}% less Damage when used by Sacred Wisps"):
   "被輔助的技能被聖潔妖精使用時，造成 {0}% 更少傷害",
 ("map_leaguestone_beyond_monster_item_quantity_and_rarity_+%_final", "Monsters from Beyond in next Area will have {0}% more Quantity and Rarity of Dropped Items"):
   "下個區域的超越怪物有 {0}% 更多物品掉落數量和物品稀有度",
 ("map_leaguestone_beyond_monster_item_quantity_and_rarity_+%_final", "Monsters from Beyond in next Area will have {0}% less Quantity and Rarity of Dropped Items"):
   "下個區域的超越怪物有 {0}% 更少物品掉落數量和物品稀有度",
 ("map_leaguestone_beyond_monster_item_quantity_and_rarity_+%_final", "Monsters from Beyond have {0}% more Quantity and Rarity of Dropped Items"):
   "超越怪物有 {0}% 更多物品掉落數量和物品稀有度",
 ("map_leaguestone_beyond_monster_item_quantity_and_rarity_+%_final", "Monsters from Beyond have {0}% less Quantity and Rarity of Dropped Items"):
   "超越怪物有 {0}% 更少物品掉落數量和物品稀有度",
 ("map_leaguestone_imprisoned_monsters_item_rarity_+%_final", "{0}% more Rarity of Items Dropped by Imprisoned Monsters in next Area"):
   "下個區域受精髓禁錮的怪物有 {0}% 更多掉落物品稀有度",
 ("map_leaguestone_imprisoned_monsters_item_rarity_+%_final", "{0}% more Rarity of Items Dropped by Imprisoned Monsters"):
   "受精髓禁錮的怪物有 {0}% 更多掉落物品稀有度",
 ("map_currency_drop_chance_+%_final_from_uber_mod", "{}% more Currency found in Area"):
   "區域內可找到 {}% 更多通貨",
 ("map_currency_drop_chance_+%_final_from_uber_mod", "{}% less Currency found in Area"):
   "區域內可找到 {}% 更少通貨",
 ("map_divination_card_drop_chance_+%_final_from_quality", "{}% less Divination Cards found in Area"):
   "區域內可找到 {}% 更少命運卡",
 ("map_map_drop_chance_+%_final_from_faridun_wish", "{0}% more Maps found in Area"):
   "區域內可找到 {0}% 更多地圖",
 ("map_map_drop_chance_+%_final_from_faridun_wish", "{0}% less Maps found in Area"):
   "區域內可找到 {0}% 更少地圖",
 ("map_map_item_drop_chance_+%_final_from_uber_mod", "{}% more Maps found in Area"):
   "區域內可找到 {}% 更多地圖",
 ("map_map_item_drop_chance_+%_final_from_uber_mod", "{}% less Maps found in Area"):
   "區域內可找到 {}% 更少地圖",
 ("map_scarab_drop_chance_+%_final_from_faridun_wish", "{0}% more Scarabs found in Area"):
   "區域內可找到 {0}% 更多聖甲蟲",
 ("map_scarab_drop_chance_+%_final_from_faridun_wish", "{0}% less Scarabs found in Area"):
   "區域內可找到 {0}% 更少聖甲蟲",
 ("map_scarab_drop_chance_+%_final_from_uber_mod", "{}% more Scarabs found in Area"):
   "區域內可找到 {}% 更多聖甲蟲",
 ("map_scarab_drop_chance_+%_final_from_uber_mod", "{}% less Scarabs found in Area"):
   "區域內可找到 {}% 更少聖甲蟲",
 ("gladiator_accuracy_rating_+%_final_while_wielding_sword", "{0}% more Accuracy Rating while wielding a Sword"):
   "持劍時有 {0}% 更多命中值",
 ("gladiator_accuracy_rating_+%_final_while_wielding_sword", "{0}% less Accuracy Rating while wielding a Sword"):
   "持劍時有 {0}% 更少命中值",
 ("ailment_bearer_elemental_damage_+%_final", "{0}% more Elemental Damage while Unbound"):
   "無縛狀態下，{0}% 更多元素傷害",
 ("ailment_bearer_elemental_damage_+%_final", "{0}% less Elemental Damage while Unbound"):
   "無縛狀態下，{0}% 更少元素傷害",
 ("alternate_ascendancy_herald_buff_effect_+%_final_per_1%_mana_reserved",
  "Herald Skills have {0}% more Buff Effect for every 1% of Maximum Mana they Reserve"):
   "捷光環技能每保留 1% 最大魔力，即有 {0}% 更多增益效果",
 ("alternate_ascendancy_herald_minion_and_herald_damage_+%_final_per_1%_life_reserved",
  "Herald Skills and Minions from Herald Skills deal {0}% more Damage for every 1% of Maximum Life those Skills Reserve"):
   "捷光環技能與由其召喚的召喚物每保留 1% 最大生命，即造成 {0}% 更多傷害",
 ("gladiator_area_of_effect_+%_final_while_wielding_mace", "{0}% more Area of Effect while wielding a Mace or Sceptre"):
   "持錘或權杖時，{0}% 更多範圍效果",
 ("gladiator_area_of_effect_+%_final_while_wielding_mace", "{0}% less Area of Effect while wielding a Mace or Sceptre"):
   "持錘或權杖時，{0}% 更少範圍效果",
 ("damage_+%_final_to_grant_packmate_on_death", "{0}% more Damage granted to Pack members on death"):
   "死亡時賦予怪物群成員 {0}% 更多傷害",
 ("damage_+%_final_to_grant_packmate_on_death", "{0}% less Damage granted to Pack members on death"):
   "死亡時賦予怪物群成員 {0}% 更少傷害",
 ("deadeye_accuracy_rating_+%_final_per_frenzy_charge", "{0}% less Accuracy Rating per Frenzy Charge"):
   "每顆狂怒球有 {0}% 更少命中值",
 ("gladiator_critical_strike_chance_+%_final_while_wielding_dagger", "{0}% more Critical Strike Chance while wielding a Dagger"):
   "持匕首時，{0}% 更多暴擊率",
 ("gladiator_critical_strike_chance_+%_final_while_wielding_dagger", "{0}% less Critical Strike Chance while wielding a Dagger"):
   "持匕首時，{0}% 更少暴擊率",
 ("map_monster_item_rarity_+permyriad_final_per_empowered_soul",
  "Monsters have {0}% more Rarity of items dropped for each Empowered Soul, up to 500%"):
   "怪物每擁有 1 個強化靈魂，就有 {0}% 更多物品掉落稀有度，最多 500%",
 ("monster_damage_+%_final_per_alive_packmate", "{0}% more Damage for each alive Monster in Pack"):
   "怪物群中每隻存活的怪物使其造成 {0}% 更多傷害",
 ("monster_damage_+%_final_per_alive_packmate", "{0}% less Damage for each alive Monster in Pack"):
   "怪物群中每隻存活的怪物使其造成 {0}% 更少傷害",
 ("necromancer_minion_damage_+%_final", "Minions deal {0}% more Damage"):
   "召喚物造成 {0}% 更多傷害",
 ("necromancer_minion_damage_+%_final", "Minions deal {0}% less Damage"):
   "召喚物造成 {0}% 更少傷害",
 ("strongest_packmate_multiplier_+%_final", "{0}% more Damage for the Strongest Pack member"):
   "最強的怪物群成員造成 {0}% 更多傷害",
 ("strongest_packmate_multiplier_+%_final", "{0}% less Damage for the Strongest Pack member"):
   "最強的怪物群成員造成 {0}% 更少傷害",
 ("tincture_bleeding_damage_+%_final_vs_non_bleeding_enemies",
  "Bleeding inflicted with Melee Weapons on non-Bleeding Enemies deals {0}% more Damage"):
   "以近戰武器施加於未流血敵人的流血，造成 {0}% 更多傷害",
 ("tincture_bleeding_damage_+%_final_vs_non_bleeding_enemies",
  "Bleeding inflicted with Melee Weapons on non-Bleeding Enemies deals {0}% less Damage"):
   "以近戰武器施加於未流血敵人的流血，造成 {0}% 更少傷害",
}

MUL = re.compile(r"\b(more|less)\b", re.I)
ADD = re.compile(r"(增加|減少|降低|提高|提升|增長)")
KEEP = re.compile(r"(更多|更少|較多|較少)")
PH = re.compile(r"\{\d*\}|#|%")

def main():
    rows = list(csv.DictReader(open(SRC, encoding="utf-8")))
    out, miss, bad = [], [], []
    for r in rows:
        if "_final" not in r["stat_ids"] or not MUL.search(r["en"]):
            continue
        if not ADD.search(r["zh"]) or KEEP.search(r["zh"]):
            continue
        key = (r["stat_ids"].split()[0], r["en"])
        new = FIX.get(key)
        if new is None:
            miss.append(key); continue
        # 自检：占位符集合必须完全一致
        if sorted(PH.findall(r["en"])) != sorted(PH.findall(new)):
            bad.append((key[0], r["en"], new)); continue
        out.append({"stat_ids": r["stat_ids"], "file": r["file"], "cond": r["cond"],
                    "en": r["en"], "zh_old": r["zh"], "zh_new": new})
    with open(DST, "w", encoding="utf-8-sig", newline="") as f:
        w = csv.DictWriter(f, fieldnames=["stat_ids","file","cond","en","zh_old","zh_new"])
        w.writeheader(); w.writerows(out)
    print("写入 %s：%d 条" % (DST, len(out)))
    print("未覆盖（需人工）:", len(miss))
    for m in miss: print("   !", m[0], "|", m[1])
    print("占位符不符（已剔除）:", len(bad))
    for b in bad: print("   x", b[0], "|", b[2])

if __name__ == "__main__":
    main()
