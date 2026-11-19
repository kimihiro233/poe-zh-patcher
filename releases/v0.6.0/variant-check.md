# 变体条件对不上的块（对照版挂错英文的排查）

源：`raw_sd`；判据见 `tools/check_variants.py` 头注释。

| 类别 | 含义 | 块数 |
|---|---|---|
| B | negate 不一致，文本极性可能反（高风险） | 3 |
| C | 同序号区间不相交，可能配错语义（要看） | 1 |
| A | 变体数不同（不会错配，最多少一行英文） | 6 |
| D | 仅条件写法不同（无风险） | 48 |
| 合计 | | 58 |

## 需要人工看的（B / C）

### heist_equipment_stat_descriptions.txt · block#31 · `heist_chests_double_uniques_%`

- negate 只在英文（变体 #1）
- EN `#|-1` negate 1
  - Heist Chests contain twice as many Uniques
- ZH `100|#` 
  - 劫盜保險箱複製內含 2 倍傳奇
- **判定**：配对正确。英文用「负值 + negate」、繁中用「100% 上限」表达同一件事，两侧文本都是「必定内含双倍」的措辞，且英文该变体不含 {0}，negate 不影响显示。

### stat_descriptions.txt · block#7094 · `heist_chests_double_uniques_%`

- negate 只在英文（变体 #1）
- EN `#|-1` negate 1
  - Heist Chests contain twice as many Uniques
- ZH `100|#` 
  - 劫盜保險箱內含 2 倍傳奇
- **判定**：配对正确。同上：`#|-1`+negate 与 `100|#` 是同一段语义的两种写法，文本一致。

### stat_descriptions.txt · block#7095 · `heist_chests_unique_rarity_%`

- negate 只在英文（变体 #1）
- EN `#|-1` negate 1
  - Heist Chests contain more valuable Uniques
- ZH `100|#` 
  - 劫盜保險箱內含更多有價值的傳奇
- **判定**：配对正确。`Heist Chests contain more valuable Uniques` ↔ 「劫盜保險箱內含更多有價值的傳奇」，语义一一对应。

### stat_descriptions.txt · block#1490 · `local_critical_strike_chance_+%`

- 区间不相交 1|# / #|-1（变体 #1）
- EN `#|-1` negate 1
  - {0}% reduced Critical Strike Chance
- ZH `1|#` negate 1
  - 減少 {0}% 暴擊率
- **判定**：配对正确。两侧都带 negate 1、都是 reduced 措辞；只是繁中把负值区写成 `1|#`（取负后匹配），英文写 `#|-1`。条件写法不同，不影响对照。

## 附带发现（人工注记）

- `strongbox_gold_cost_to_open`（chest_stat_descriptions.txt block#99，A）
  - 英文第二个变体是 `Costs ??? Gold to open`（官方占位符没填），繁中只有一行 → 忽略即可。
- `deepwater_chance_to_ancient_%`（stat_descriptions.txt block#6246，A）
  - 繁中把「{0}% 機率」整段丢了，且英文的 100% 变体没有中文对应 —— 玩家看到「機會石改為掉落古變石」会读成必定转换，机制读错。已在 fix-variant-hold.csv 立稿。
- `deepwater_chaos_to_divine_%`（stat_descriptions.txt block#6247，A）
  - 同上：漏 {0}%，读成「混沌石必定變神聖石」。
- `deepwater_scour_to_annulment_%`（stat_descriptions.txt block#6249，A）
  - 同上：漏 {0}%，读成「重鑄石必定變無效石」。
- `local_unique_jewel_viper_strike_chance_to_gain_unholy_might_%_on_hit_per_poison_stack_on_enemy_with_50_dex_in_radius`（stat_descriptions.txt block#8250，A）
  - 繁中多一个 100% 变体，英文没有 → 该行取不到英文（属已知的 12 行之一），不会错配。
- `uber_domain_monster_critical_strike_multiplier_+%_per_revival`（stat_descriptions.txt block#10607，A）
  - 繁中多一个「減少」变体（`#|-1` + negate），英文只有「增加」→ 该行取不到英文，不会错配。

## 其余（A / D）

| 文件 | 块 | stat_ids | 类别 | 说明 |
|---|---|---|---|---|
| chest_stat_descriptions.txt | 99 | `strongbox_gold_cost_to_open` | A | 变体数 1/2 |
| stat_descriptions.txt | 6246 | `deepwater_chance_to_ancient_%` | A | 变体数 1/2 |
| stat_descriptions.txt | 6247 | `deepwater_chaos_to_divine_%` | A | 变体数 1/2 |
| stat_descriptions.txt | 6249 | `deepwater_scour_to_annulment_%` | A | 变体数 1/2 |
| stat_descriptions.txt | 8250 | `local_unique_jewel_viper_strike_chance_to_gain_unholy_might_%_on_hit_per_poison_stack_on_enemy_with_50_dex_in_radius` | A | 变体数 2/1 |
| stat_descriptions.txt | 10607 | `uber_domain_monster_critical_strike_multiplier_+%_per_revival` | A | 变体数 2/1 |
| chest_stat_descriptions.txt | 3 | `chest_drop_additional_unqiue_items` | D | 条件写法不同、区间有交集 |
| chest_stat_descriptions.txt | 13 | `chest_drop_additional_normal_items_up_to` | D | 条件写法不同、区间有交集 |
| chest_stat_descriptions.txt | 14 | `chest_drop_additional_magic_items_up_to` | D | 条件写法不同、区间有交集 |
| chest_stat_descriptions.txt | 39 | `chest_drops_extra_vaal_gems` | D | 条件写法不同、区间有交集 |
| chest_stat_descriptions.txt | 40 | `chest_drop_additional_number_of_uniques_of_drop_pool_types` | D | 条件写法不同、区间有交集 |
| chest_stat_descriptions.txt | 41 | `chest_drop_additional_unique_maps` | D | 条件写法不同、区间有交集 |
| chest_stat_descriptions.txt | 47 | `chest_number_of_additional_kaom_uniques_to_drop` | D | 条件写法不同、区间有交集 |
| chest_stat_descriptions.txt | 49 | `chest_drops_extra_vaal_fragments` | D | 条件写法不同、区间有交集 |
| gem_stat_descriptions.txt | 44 | `kill_enemy_on_hit_if_under_10%_life` | D | 条件写法不同、区间有交集 |
| gem_stat_descriptions.txt | 64 | `base_number_of_traps_allowed` | D | 条件写法不同、区间有交集 |
| gem_stat_descriptions.txt | 65 | `base_number_of_remote_mines_allowed` | D | 条件写法不同、区间有交集 |
| leaguestone_stat_descriptions.txt | 118 | `map_monsters_with_silver_coins_drop_x_additional_rare_items` | D | 条件写法不同、区间有交集 |
| minion_skill_stat_descriptions.txt | 30 | `number_of_additional_projectiles` | D | 条件写法不同、区间有交集 |
| minion_skill_stat_descriptions.txt | 104 | `number_of_archer_skeletons_to_summon` | D | 条件写法不同、区间有交集 |
| minion_skill_stat_descriptions.txt | 105 | `number_of_mage_skeletons_to_summon` | D | 条件写法不同、区间有交集 |
| minion_skill_stat_descriptions.txt | 106 | `number_of_leader_skeletons_to_summon` | D | 条件写法不同、区间有交集 |
| minion_skill_stat_descriptions.txt | 123 | `base_spell_repeat_count` | D | 条件写法不同、区间有交集 |
| offering_skill_stat_descriptions.txt | 10 | `spirit_offering_life_%_added_as_base_maximum_energy_shield_per_corpse_consumed` | D | 条件写法不同、区间有交集 |
| skill_stat_descriptions.txt | 50 | `spell_repeat_count` | D | 条件写法不同、区间有交集 |
| skill_stat_descriptions.txt | 222 | `skill_display_number_of_traps_allowed` | D | 条件写法不同、区间有交集 |
| skill_stat_descriptions.txt | 223 | `skill_display_number_of_remote_mines_allowed` | D | 条件写法不同、区间有交集 |
| stat_descriptions.txt | 537 | `display_socketed_minion_gems_supported_by_level_X_life_leech` | D | 条件写法不同、区间有交集 |
| stat_descriptions.txt | 546 | `local_display_socketed_skills_summon_your_maximum_number_of_totems_in_formation` | D | 条件写法不同、区间有交集 |
| stat_descriptions.txt | 865 | `local_recharge_on_crit` | D | 条件写法不同、区间有交集 |
| stat_descriptions.txt | 868 | `local_recharge_on_take_crit` | D | 条件写法不同、区间有交集 |
| stat_descriptions.txt | 1919 | `skill_repeat_count` | D | 条件写法不同、区间有交集 |
| stat_descriptions.txt | 1920 | `spell_repeat_count` | D | 条件写法不同、区间有交集 |
| stat_descriptions.txt | 1925 | `attack_repeat_count` | D | 条件写法不同、区间有交集 |
| stat_descriptions.txt | 2279 | `base_number_of_remote_mines_allowed` | D | 条件写法不同、区间有交集 |
| stat_descriptions.txt | 2366 | `map_spawn_extra_talismans` | D | 条件写法不同、区间有交集 |
| stat_descriptions.txt | 2517 | `bleed_on_bow_attack_chance_%` | D | 条件写法不同、区间有交集 |
| stat_descriptions.txt | 2754 | `global_critical_strike_mulitplier_+_per_green_socket_on_item` | D | 条件写法不同、区间有交集 |
| stat_descriptions.txt | 2827 | `base_deal_no_physical_damage` | D | 条件写法不同、区间有交集 |
| stat_descriptions.txt | 2993 | `gain_physical_damage_immunity_on_rampage_threshold_ms` | D | 条件写法不同、区间有交集 |
| stat_descriptions.txt | 3012 | `gain_unholy_might_on_rampage_threshold_ms` | D | 条件写法不同、区间有交集 |
| stat_descriptions.txt | 3030 | `chance_to_gain_onslaught_on_kill_% onslaught_time_granted_on_kill_ms` | D | 条件写法不同、区间有交集 |
| stat_descriptions.txt | 3119 | `totem_number_of_additional_projectiles` | D | 条件写法不同、区间有交集 |
| stat_descriptions.txt | 3125 | `number_of_additional_clones` | D | 条件写法不同、区间有交集 |
| stat_descriptions.txt | 3281 | `enchantment_boots_minimum_added_fire_damage_on_kill_4s enchantment_boots_maximum_added_fire_damage_on_kill_4s` | D | 条件写法不同、区间有交集 |
| stat_descriptions.txt | 4090 | `cannot_be_stunned_while_at_max_endurance_charges` | D | 条件写法不同、区间有交集 |
| stat_descriptions.txt | 4317 | `%_chance_to_blind_on_critical_strike` | D | 条件写法不同、区间有交集 |
| stat_descriptions.txt | 4360 | `projectile_attacks_chance_to_maim_on_hit_%_if_you_have_beast_minion` | D | 条件写法不同、区间有交集 |
| stat_descriptions.txt | 4743 | `apply_covered_in_ash_to_attacker_on_hit_%_vs_rare_or_unique_enemy` | D | 条件写法不同、区间有交集 |
| stat_descriptions.txt | 6771 | `freezing_pulse_number_of_additional_projectiles` | D | 条件写法不同、区间有交集 |
| stat_descriptions.txt | 8760 | `map_metamorph_boss_drops_additional_itemised_organs` | D | 条件写法不同、区间有交集 |
| stat_descriptions.txt | 8856 | `map_monsters_with_silver_coins_drop_x_additional_rare_items` | D | 条件写法不同、区间有交集 |
| stat_descriptions.txt | 9293 | `maximum_life_per_2%_increased_item_found_rarity` | D | 条件写法不同、区间有交集 |
| stat_descriptions.txt | 9800 | `physical_damage_reduction_%_at_devotion_threshold` | D | 条件写法不同、区间有交集 |
