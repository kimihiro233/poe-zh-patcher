# 译文占位符漏失（按后果分级）

源：`raw_sd`；判据见 `tools/check_placeholders.py` 头注释。

| 类别 | 含义 | 条数 |
|---|---|---|
| MISS_NUM | `{n}` 整段丢失，机制必读错 | 18 |
| MISS_HASH | `#` 丢失，数值无处填 | 0 |
| MISS_PCT | `%` 丢失 | 3 |
| REWRITE | 编号在、写法不同（`{0:+d}`→`{0}`） | 42 |
| EXTRA | 译文多出占位符 | 43 |
| MISS_NL | 只丢换行，排版 | 110 |
| 合计 | | 216 |

## MISS_NUM（18 条）

### active_skill_gem_stat_descriptions.txt · block#312 · `projectiles_return projectile_return_%_chance` · 缺 {1}

- EN `0  1|99`
  - Projectiles have {1}% chance to Return to you
- ZH `0  1|99`
  - 投射物有 {0}% 機率返回你
- 判定：已修：編號錯位 {1}→{0}（投射物返回）

### atlas_stat_descriptions.txt · block#733 · `map_orb_of_chance_to_unique_item_%` · 缺 {0}

- EN `#`
  - {0}% chance for Orbs of Chance to drop as Unique Items instead in your Maps
- ZH `#`
  - 你地圖中的機會石改為掉落傳奇物品
- 判定：已修：補回 {0}%（機會石→傳奇）

### chest_stat_descriptions.txt · block#7 · `number_of_packs_to_summon number_of_monsters_to_summon summoned_monster_rarity` · 缺 {0}

- EN `2|# # 1`
  - Guarded by {0} packs of Magic Monsters
- ZH `2|# # 1`
  - 被 {1} 群魔法怪物守衛著
- 判定：已修：編號錯位 {1}→{0}（守衛群數，兩個變體）

### chest_stat_descriptions.txt · block#7 · `number_of_packs_to_summon number_of_monsters_to_summon summoned_monster_rarity` · 缺 {0}

- EN `2|# # 2`
  - Guarded by {0} packs of Rare Monsters
- ZH `2|# # 2`
  - 被 {1} 群稀有怪物守衛著
- 判定：已修：編號錯位 {1}→{0}（守衛群數，兩個變體）

### gem_stat_descriptions.txt · block#763 · `trigger_on_ward_break_%_chance` · 缺 {0}

- EN `1|99`
  - {0}% chance to Trigger this Supported Spell when your Ward Breaks
- ZH `1|99`
  - 當你的保護被破壞時觸發被輔助的技能
- 判定：已修：補回 {0}%（觸發機率）

### leaguestone_stat_descriptions.txt · block#65 · `map_leaguestone_additional_shrines` · 缺 {0}

- EN `1`
  - Next Area will contain up to {0} additional Shrine
- ZH `1`
  - 下個區域額外含有 1 座神殿
- 判定：已修：{0} 寫死成 1 座（額外神殿數）

### skill_stat_descriptions.txt · block#40 · `is_trap number_of_traps_to_throw` · 缺 {1}

- EN `1|# 2|#`
  - Throws up to {1} Traps which use this Skill when Triggered
- ZH `1|# 2|#`
  - 當觸發時使用此技能投擲 {0} 個陷阱
- 判定：已修：編號錯位 {1}→{0}（陷阱數）

### skill_stat_descriptions.txt · block#218 · `virtual_intensity_loss_ms_while_moving_interval virtual_intensity_lost_on_teleport` · 缺 {1}

- EN `# #`
  - Loses 1 Intensity every {0} seconds while moving\nLoses {1} Intensity immediately if you teleport
- ZH `# #`
  - 移動或你立即傳送時，\n每 {0} 秒失去 1 強化
- 判定：已修：第二句被併掉，補回「傳送失去 {1} 強化」

### skill_stat_descriptions.txt · block#429 · `support_arcane_surge_cast_speed_+% support_arcane_surge_mana_regeneration_rate_+%` · 缺 {1}

- EN `# #`
  - Arcane Surge grants {0}% increased Cast Speed\nArcane Surge grants {1}% increased Mana Regeneration rate
- ZH `# #`
  - 秘能波動增加 {0}% 施放速度\n秘能波動增加 {0}% 魔力恢復率
- 判定：已修：編號錯位 {0}→{1}（秘能波動魔力恢復）

### skill_stat_descriptions.txt · block#1272 · `rage_slash_damage_+%_final_per_amount_of_rage_sacrificed rage_slash_rage_sacrifice_per_damage_bonus` · 缺 {0}

- EN `1|# #`
  - Ragestorm deals {0}% more Damage per {1} Rage Sacrificed
- ZH `1|# #`
  - 每 {1} 犧牲的盛怒，使盛怒風暴造成 5% 更多傷害
- 判定：已修：{0} 寫死成 5%（盛怒之斬，兩個變體）

### skill_stat_descriptions.txt · block#1272 · `rage_slash_damage_+%_final_per_amount_of_rage_sacrificed rage_slash_rage_sacrifice_per_damage_bonus` · 缺 {0}

- EN `#|-1 #`
  - Ragestorm deals {0}% less Damage per {1} Rage Sacrificed
- ZH `#|-1 #`
  - 每 {1} 犧牲的盛怒，使盛怒風暴造成 5% 更少傷害
- 判定：已修：{0} 寫死成 5%（盛怒之斬，兩個變體）

### skill_stat_descriptions.txt · block#1364 · `spectral_throw_an_spectral_helix_active_skill_projectile_speed_+%_variation_final` · 缺 {0}

- EN `1|#`
  - Each Projectile has between {0}% more and {0}% less Projectile Speed
- ZH `1|#`
  - 每個投射物有介在 10% 更多至 10% 更少投射物速度
- 判定：已修：{0} 寫死成 10%（幻影投射物）

### stat_descriptions.txt · block#4608 · `additional_maximum_all_elemental_resistances_%_if_suppressed_spell_recently` · 缺 {0}

- EN `#`
  - {0:+d}% to all maximum Elemental Resistances if you've Suppressed Spell Damage Recently
- ZH `#`
  - 若你近期有壓抑法術傷害，
- 判定：已修：句子被逗號截斷，補回 {0}%（最大元素抗性）

### stat_descriptions.txt · block#6246 · `deepwater_chance_to_ancient_%` · 缺 {0}

- EN `1|#`
  - {0}% chance for Orbs of Chance to drop as Ancient Orbs instead
- ZH `1|#`
  - 機會石改為掉落古變石
- 判定：已修：補回「有 {0}% 機率」（deepwater 古變石）

### stat_descriptions.txt · block#6247 · `deepwater_chaos_to_divine_%` · 缺 {0}

- EN `1|#`
  - {0}% chance for Chaos Orbs to drop as Divine Orbs instead
- ZH `1|#`
  - 混沌石改為掉落神聖石
- 判定：已修：同上（deepwater 混沌→神聖）

### stat_descriptions.txt · block#6249 · `deepwater_scour_to_annulment_%` · 缺 {0}

- EN `1|#`
  - {0}% chance for Orbs of Scouring to drop as Orbs of Annulment instead
- ZH `1|#`
  - 重鑄石改為掉落無效石
- 判定：已修：同上（deepwater 第三塊）

### stat_descriptions.txt · block#6796 · `gain_%_of_two_hand_weapon_physical_damage_as_added_spell_damage` · 缺 {0}

- EN `#`
  - Spells have Added Spell Damage equal to {0}% of Physical Damage of\nyour Equipped Two handed Weapon
- ZH `#`
  - 法術擁有等同於你已裝備雙手武器{0]%物理傷害的附加法術傷害
- 判定：已修：右括號寫成 ]，fix-typo 批次改回 {0}

### stat_descriptions.txt · block#9216 · `map_vaal_vessel_drop_X_vaal_temple_maps` · 缺 {0}

- EN `#`
  - Vaal Vessel contains {0} additional Vaal Temple Maps
- ZH `#`
  - 瓦爾容器包含 1 張瓦爾密殿
- 判定：已修：{0} 寫死成 1（瓦爾密殿張數）

## MISS_PCT（3 条）

### minion_skill_stat_descriptions.txt · block#259 · `active_skill_non_reaper_minion_damage_+%_final` · 缺 %

- EN `1|#`
  - Reapers cause your Non-Reaper Minions to deal {0}% more Damage
- ZH `1|#`
  - 奪魂鐮使你非奪魂鐮召喚物造成 {0} 更多傷害
- 判定：已修：補回 %（奪魂鐮，兩個變體）

### minion_skill_stat_descriptions.txt · block#259 · `active_skill_non_reaper_minion_damage_+%_final` · 缺 %

- EN `#|-1`
  - Reapers cause your Non-Reaper Minions to deal {0}% less Damage
- ZH `#|-1`
  - 奪魂鐮使你非奪魂鐮召喚物造成 {0} 更少傷害
- 判定：已修：補回 %（奪魂鐮，兩個變體）

### passive_skill_stat_descriptions.txt · block#98 · `keystone_magebane` · 缺 %

- EN `#`
  - Dexterity provides no inherent bonus to Evasion Rating\n+1% Chance to Suppress Spell Damage per 15 Dexterity
- ZH `#`
  - 敏捷不提供固有的閃避值加成\n每 15 點敏捷 +1 法術傷害壓抑率
- 判定：已修：補回 %（法師剋星 +1%）

## 其余（REWRITE / EXTRA / MISS_NL）

| 文件 | 块 | stat_ids | 类别 | 说明 |
|---|---|---|---|---|
| active_skill_gem_stat_descriptions.txt | 172 | `corpse_erruption_base_maximum_number_of_geyers` | REWRITE | 写法不同 {0} / {0:+d} |
| active_skill_gem_stat_descriptions.txt | 230 | `base_graft_skill_tul_summon_maximum_allowed_demons` | REWRITE | 写法不同 {0} / {0:+d} |
| atlas_stat_descriptions.txt | 906 | `map_vaal_side_area_chance_%` | REWRITE | 写法不同 {0} / {0:+d} |
| gem_stat_descriptions.txt | 133 | `cast_on_damage_taken_% virtual_cast_when_damage_taken_threshold cast_on_damage_taken_threshold` | REWRITE | 写法不同 {2} {0} / {0} {2} |
| gem_stat_descriptions.txt | 175 | `support_innervate_buff_base_duration_ms` | REWRITE | 写法不同 {0} / {0:+d} |
| gem_stat_descriptions.txt | 175 | `support_innervate_buff_base_duration_ms` | REWRITE | 写法不同 {0} / {0:+d} |
| gem_stat_descriptions.txt | 262 | `base_cooldown_speed_+%` | REWRITE | 写法不同 {0} / {0:+d} |
| gem_stat_descriptions.txt | 262 | `base_cooldown_speed_+%` | REWRITE | 写法不同 {0} / {0:+d} |
| gem_stat_descriptions.txt | 470 | `minion_critical_strike_multiplier_+_per_attack_crit_recently_up_to_100` | REWRITE | 写法不同 {0} / {0:+d} |
| minion_attack_skill_stat_descriptions.txt | 141 | `support_momentum_base_buff_duration_ms support_momentum_buff_duration_ms support_momentum_max_stacks` | REWRITE | 写法不同 {0} {1} {2} {0} / {2} {1} {2} {0} |
| minion_attack_skill_stat_descriptions.txt | 150 | `support_recent_ignites_damage_per_recent_ignite_+%_final support_recent_ignites_damage_per_recent_ignite_+%_final_minimum` | REWRITE | 写法不同 {0} {0} {0} {1} / {0} {1} {0} {1} |
| skill_stat_descriptions.txt | 53 | `power_siphon_fire_at_all_targets total_number_of_projectiles_to_fire total_number_of_arrows_to_fire base_number_of_projectiles_in_spiral_nova projectile_spiral_nova_angle projectile_spiral_nova_both_directions projectiles_nova projectiles_barrage rain_of_arrows_sequences_to_fire skill_display_single_base_projectile` | REWRITE | 写法不同 {2} {8} / {8} {2} |
| skill_stat_descriptions.txt | 309 | `cast_on_damage_taken_% virtual_cast_when_damage_taken_threshold cast_on_damage_taken_threshold` | REWRITE | 写法不同 {1} {0} / {0} {1} |
| skill_stat_descriptions.txt | 469 | `active_skill_area_damage_+%_final quality_display_active_skill_area_damage_is_gem quality_display_active_skill_area_damage_quality_negated_from_gem` | REWRITE | 写法不同 {0:+d} / {0} |
| skill_stat_descriptions.txt | 469 | `active_skill_area_damage_+%_final quality_display_active_skill_area_damage_is_gem quality_display_active_skill_area_damage_quality_negated_from_gem` | REWRITE | 写法不同 {0:+d} / {0} |
| skill_stat_descriptions.txt | 587 | `base_smite_number_of_targets smite_lightning_target_range` | REWRITE | 写法不同 {1} {0} / {0} {1} |
| skill_stat_descriptions.txt | 587 | `base_smite_number_of_targets smite_lightning_target_range` | REWRITE | 写法不同 {1} {0} / {0} {1} |
| skill_stat_descriptions.txt | 587 | `base_smite_number_of_targets smite_lightning_target_range` | REWRITE | 写法不同 {1} {0} / {0} {1} |
| skill_stat_descriptions.txt | 587 | `base_smite_number_of_targets smite_lightning_target_range` | REWRITE | 写法不同 {1} {0} / {0} {1} |
| skill_stat_descriptions.txt | 641 | `call_of_steel_reload_amount call_of_steel_reload_time` | REWRITE | 写法不同 {1} {0} / {0} {1} |
| skill_stat_descriptions.txt | 693 | `charged_dash_damage_+%_final_per_stack` | REWRITE | 写法不同 {0} / {0:+d} |
| skill_stat_descriptions.txt | 718 | `corpse_erruption_maximum_number_of_geyers` | REWRITE | 写法不同 {0} / {0:+d} |
| skill_stat_descriptions.txt | 895 | `flame_surge_burning_ground_on_ignite_damage_% quality_display_alt_flame_whip_is_gem` | REWRITE | 写法不同 {0:+d} / {0} |
| skill_stat_descriptions.txt | 906 | `flamethrower_tower_trap_number_of_flamethrowers` | REWRITE | 写法不同 {0} / {0:+d} |
| skill_stat_descriptions.txt | 1087 | `lightning_tower_trap_number_of_beams lightning_tower_trap_interval_duration_ms quality_display_lightning_tower_trap_is_gem` | REWRITE | 写法不同 {1} {0} / {0} {1} |
| skill_stat_descriptions.txt | 1239 | `phys_cascade_trap_number_of_cascades phys_cascade_trap_interval_duration_ms quality_display_phys_cascade_trap_is_gem` | REWRITE | 写法不同 {1} {0} / {0} {1} |
| skill_stat_descriptions.txt | 1270 | `radiant_sentinel_minion_fire_%_of_life_to_deal_nearby_per_minute radiant_sentinel_minion_burning_effect_radius` | REWRITE | 写法不同 {1} {0} / {0} {1} |
| skill_stat_descriptions.txt | 1270 | `radiant_sentinel_minion_fire_%_of_life_to_deal_nearby_per_minute radiant_sentinel_minion_burning_effect_radius` | REWRITE | 写法不同 {1} {0} / {0} {1} |
| skill_stat_descriptions.txt | 1273 | `rage_slash_radius_+_per_amount_of_rage_sacrificed rage_slash_rage_sacrifice_per_radius_bonus` | REWRITE | 写法不同 {1} {0:+d} / {0:+d} {1} |
| skill_stat_descriptions.txt | 1273 | `rage_slash_radius_+_per_amount_of_rage_sacrificed rage_slash_rage_sacrifice_per_radius_bonus` | REWRITE | 写法不同 {1} {0:+d} / {0:+d} {1} |
| skill_stat_descriptions.txt | 1308 | `searing_bond_totems_detonation_damage_over_time_+%_final_per_active_totem` | REWRITE | 写法不同 {0} / {0:+d} |
| skill_stat_descriptions.txt | 1308 | `searing_bond_totems_detonation_damage_over_time_+%_final_per_active_totem` | REWRITE | 写法不同 {0} / {0:+d} |
| skill_stat_descriptions.txt | 1662 | `volcanic_fissure_number_of_simultaneous_crack_count` | REWRITE | 写法不同 {0} / {0:+d} |
| stat_descriptions.txt | 778 | `cast_socketed_spells_on_X_life_spent cast_socketed_spells_on_life_spent_%_chance` | REWRITE | 写法不同 {0} {1} / {1} {0} |
| stat_descriptions.txt | 1127 | `map_player_additional_physical_damage_reduction_%_in_hellscape` | REWRITE | 写法不同 {0} / {0:+d} |
| stat_descriptions.txt | 1644 | `random_skill_gem_level_+_level random_skill_gem_level_+_index` | REWRITE | 写法不同 {1} {0} / {0} {1} |
| stat_descriptions.txt | 1644 | `random_skill_gem_level_+_level random_skill_gem_level_+_index` | REWRITE | 写法不同 {1} {0} / {0} {1} |
| stat_descriptions.txt | 2723 | `deaths_oath_debuff_on_kill_duration_ms deaths_oath_debuff_on_kill_base_chaos_damage_to_deal_per_minute` | REWRITE | 写法不同 {0} {1} / {1} {0} |
| stat_descriptions.txt | 2809 | `movement_speed_bonus_when_throwing_trap_ms movement_speed_+%_on_throwing_trap` | REWRITE | 写法不同 {0} {1} / {1} {0} |
| stat_descriptions.txt | 3112 | `life_gained_on_hit_per_enemy_status_ailment` | REWRITE | 写法不同 {0:+d} / {0} |
| stat_descriptions.txt | 6680 | `fire_exposure_you_inflict_applies_extra_fire_resistance_+%` | REWRITE | 写法不同 {0} / {0:+d} |
| stat_descriptions.txt | 10298 | `spell_damage_%_suppressed_if_not_suppressed_spell_recently` | REWRITE | 写法不同 {0:+d} / {0} |
| atlas_stat_descriptions.txt | 356 | `map_contains_X_additional_strongboxes_essences_shrines_or_rogue_exiles` | EXTRA | 多 {0} |
| atlas_stat_descriptions.txt | 833 | `map_spawn_extra_exiles` | EXTRA | 多 {0} |
| chest_stat_descriptions.txt | 3 | `chest_drop_additional_unqiue_items` | EXTRA | 多 {0} |
| chest_stat_descriptions.txt | 39 | `chest_drops_extra_vaal_gems` | EXTRA | 多 {0} |
| chest_stat_descriptions.txt | 40 | `chest_drop_additional_number_of_uniques_of_drop_pool_types` | EXTRA | 多 {0} |
| chest_stat_descriptions.txt | 41 | `chest_drop_additional_unique_maps` | EXTRA | 多 {0} |
| chest_stat_descriptions.txt | 47 | `chest_number_of_additional_kaom_uniques_to_drop` | EXTRA | 多 {0} |
| chest_stat_descriptions.txt | 49 | `chest_drops_extra_vaal_fragments` | EXTRA | 多 {0} |
| minion_skill_stat_descriptions.txt | 123 | `base_spell_repeat_count` | EXTRA | 多 {0} |
| skill_stat_descriptions.txt | 50 | `spell_repeat_count` | EXTRA | 多 {0} |
| skill_stat_descriptions.txt | 947 | `frozen_legion_%_chance_to_summon_additional_statue` | EXTRA | 多 {0} |
| skill_stat_descriptions.txt | 1004 | `hinder_enemy_chaos_damage_+%` | EXTRA | 多 {0} |
| skill_stat_descriptions.txt | 1004 | `hinder_enemy_chaos_damage_+%` | EXTRA | 多 {0} |
| skill_stat_descriptions.txt | 1066 | `kinetic_shell_hit_damage_absorbed_% kinetic_shell_maximum_damage_absorbed_from_hits` | EXTRA | 多 {0} |
| skill_stat_descriptions.txt | 1556 | `tornado_hinder` | EXTRA | 多 {0} |
| stat_descriptions.txt | 156 | `map_weapon_and_shields_drop_fully_socketed_%_chance` | EXTRA | 多 {0} |
| stat_descriptions.txt | 742 | `local_display_grants_skill_touch_of_fire_level` | EXTRA | 多 {0} |
| stat_descriptions.txt | 747 | `local_display_grants_skill_vampiric_icon_level` | EXTRA | 多 {0} |
| stat_descriptions.txt | 1919 | `skill_repeat_count` | EXTRA | 多 {0} |
| stat_descriptions.txt | 1920 | `spell_repeat_count` | EXTRA | 多 {0} |
| stat_descriptions.txt | 2359 | `map_spawn_extra_exiles` | EXTRA | 多 {0} |
| stat_descriptions.txt | 2649 | `map_num_extra_invasion_bosses` | EXTRA | 多 {0} |
| stat_descriptions.txt | 3125 | `number_of_additional_clones` | EXTRA | 多 {0} |
| stat_descriptions.txt | 3191 | `local_unique_jewel_projectile_damage_+1%_per_x_dex_in_radius` | EXTRA | 多 {1} |
| stat_descriptions.txt | 6790 | `frozen_legion_%_chance_to_summon_additional_statue` | EXTRA | 多 {0} |
| stat_descriptions.txt | 7106 | `heist_contract_mechanical_unlock_count` | EXTRA | 多 {0} |
| stat_descriptions.txt | 7106 | `heist_contract_mechanical_unlock_count` | EXTRA | 多 {0} |
| stat_descriptions.txt | 7107 | `heist_contract_magical_unlock_count` | EXTRA | 多 {0} |
| stat_descriptions.txt | 7107 | `heist_contract_magical_unlock_count` | EXTRA | 多 {0} |
| stat_descriptions.txt | 8052 | `local_energy_shield_regeneration_per_minute_%_if_crit_recently` | EXTRA | 多 {0} |
| stat_descriptions.txt | 9012 | `map_ritual_number_of_free_rerolls` | EXTRA | 多 {0} |
| stat_descriptions.txt | 9060 | `map_supporter_players_random_movement_velocity_+%_final_when_hit` | EXTRA | 多 {1} |
| stat_descriptions.txt | 9446 | `minion_energy_shield_leech_from_elemental_damage_permyriad` | EXTRA | 多 {0} |
| stat_descriptions.txt | 9503 | `minions_have_%_chance_to_inflict_wither_on_hit` | EXTRA | 多 {0} |
| stat_descriptions.txt | 9879 | `profane_ground_on_crit_chance_%_if_highest_attribute_is_intelligence` | EXTRA | 多 {0} |
| stat_descriptions.txt | 9981 | `random_curse_when_hit_%_ignoring_curse_limit` | EXTRA | 多 {0} |
| stat_descriptions.txt | 10004 | `recover_X_life_on_suppressing_spell` | EXTRA | 多 {0} |
| stat_descriptions.txt | 10452 | `summon_arbalist_chance_to_freeze_%` | EXTRA | 多 {0} |
| stat_descriptions.txt | 10453 | `summon_arbalist_chance_to_ignite_%` | EXTRA | 多 {0} |
| stat_descriptions.txt | 10454 | `summon_arbalist_chance_to_shock_%` | EXTRA | 多 {0} |
| stat_descriptions.txt | 10714 | `volcanic_fissure_speed_+%` | EXTRA | 多 {0} |
| stat_descriptions.txt | 10714 | `volcanic_fissure_speed_+%` | EXTRA | 多 {0} |
| stat_descriptions.txt | 10885 | `local_unique_jewel_disconnected_passives_can_be_allocated_around_keystone_hash` | EXTRA | 多 {0} |
| active_skill_gem_stat_descriptions.txt | 353 | `vaal_lightning_arrow_fork_and_chain_modifiers_apply_to_number_of_redirects` | MISS_NL | 缺换行 |
| atlas_stat_descriptions.txt | 159 | `map_allied_mercenary_damage_+%_final_vs_mercenaries_and_rogue_exiles` | MISS_NL | 缺换行 |
| atlas_stat_descriptions.txt | 159 | `map_allied_mercenary_damage_+%_final_vs_mercenaries_and_rogue_exiles` | MISS_NL | 缺换行 |
| atlas_stat_descriptions.txt | 198 | `map_betrayal_non_interrogate_options_grant_X_intelligence` | MISS_NL | 缺换行 |
| atlas_stat_descriptions.txt | 451 | `map_double_conqueror_progress_chance_%` | MISS_NL | 缺换行 |
| atlas_stat_descriptions.txt | 886 | `map_uber_elder_and_shaper_orb_of_dominance_drop_chance_%` | MISS_NL | 缺换行 |
| atlas_stat_descriptions.txt | 894 | `map_ultimatum_trialmaster_spawn_chance_+%` | MISS_NL | 缺换行 |
| atlas_stat_descriptions.txt | 894 | `map_ultimatum_trialmaster_spawn_chance_+%` | MISS_NL | 缺换行 |
| beam_skill_stat_descriptions.txt | 2 | `active_skill_beam_splits_instead_of_chaining` | MISS_NL | 缺换行 |
| gem_stat_descriptions.txt | 178 | `support_ruthless_blow_ailment_damage_from_melee_hits_+%_final` | MISS_NL | 缺换行 |
| gem_stat_descriptions.txt | 180 | `chance_to_summon_support_ghost_on_killing_blow_%` | MISS_NL | 缺换行 |
| gem_stat_descriptions.txt | 180 | `chance_to_summon_support_ghost_on_killing_blow_%` | MISS_NL | 缺换行 |
| gem_stat_descriptions.txt | 304 | `chance_to_fork_extra_projectile_%` | MISS_NL | 缺换行 |
| gem_stat_descriptions.txt | 702 | `support_scion_onslaught_for_3_seconds_on_hitting_unique_enemy_%_chance support_scion_onslaught_on_unique_hit_duration_ms` | MISS_NL | 缺换行 |
| minion_attack_skill_stat_descriptions.txt | 102 | `dominating_blow_chance_to_summon_on_hitting_unqiue_%` | MISS_NL | 缺换行 |
| passive_skill_aura_stat_descriptions.txt | 13 | `minimum_added_physical_damage_per_impaled_on_enemy maximum_added_physical_damage_per_impaled_on_enemy` | MISS_NL | 缺换行 |
| skill_stat_descriptions.txt | 418 | `corrosive_shroud_gains_%_of_damage_from_inflicted_poisons` | MISS_NL | 缺换行 |
| skill_stat_descriptions.txt | 967 | `glacial_cascade_final_spike_damage_+%_final quality_display_glacial_cascade_is_gem` | MISS_NL | 缺换行 |
| skill_stat_descriptions.txt | 1454 | `support_executioner_buff_duration_ms` | MISS_NL | 缺换行 |
| skill_stat_descriptions.txt | 1626 | `vaal_ice_shot_modifiers_to_projectile_count_do_not_apply_to_mirages` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 141 | `map_crucible_unique_monster_drop_unique_with_weapon_tree_%_chance` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 733 | `local_display_grants_skill_mirage_chieftain` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 779 | `cast_socketed_spells_on_X_mana_spent cast_socketed_spells_on_mana_spent_%_chance` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 854 | `trigger_socketed_bow_skills_on_spell_cast_while_wielding_a_bow_%` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 1116 | `map_hellscape_gimmick_%_maximum_life_and_es_taken_as_physical_damage_per_minute_per_%_hellscape_charge_while_hellscape_can_be_activated` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 3181 | `local_unique_remove_life_and_regen_es_from_removed_life local_unique_regen_es_from_removed_life_duration_ms` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 3189 | `local_unique_jewel_energy_shield_regeneration_rate_per_minute_%_per_10_int_allocated_in_radius` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 3264 | `local_unique_jewel_glacial_hammer_item_rarity_on_shattering_enemy_+%_with_50_strength_in_radius` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 3268 | `local_unique_jewel_heavy_strike_chance_to_deal_double_damage_%_with_50_strength_in_radius` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 3296 | `local_unique_jewel_ground_slam_angle_+%_with_50_str_in_radius` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 3296 | `local_unique_jewel_ground_slam_angle_+%_with_50_str_in_radius` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 3326 | `arcane_surge_effect_on_self_+%_per_caster_abyss_jewel_up_to_+40%` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 3326 | `arcane_surge_effect_on_self_+%_per_caster_abyss_jewel_up_to_+40%` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 3394 | `local_unique_jewel_freezing_pulse_damage_+%_if_enemy_shattered_recently_with_50_int_in_radius` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 3394 | `local_unique_jewel_freezing_pulse_damage_+%_if_enemy_shattered_recently_with_50_int_in_radius` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 3499 | `auras_grant_attack_and_cast_speed_+%_to_you_and_your_allies` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 3520 | `bleeding_enemies_explode_for_%_life_as_physical_damage` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 3566 | `attack_and_cast_speed_+%_for_4_seconds_on_begin_es_recharge` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 3566 | `attack_and_cast_speed_+%_for_4_seconds_on_begin_es_recharge` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 3569 | `critical_strike_chance_+%_vs_enemies_without_elemental_status_ailments` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 3569 | `critical_strike_chance_+%_vs_enemies_without_elemental_status_ailments` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 3595 | `critical_strike_multiplier_+_vs_enemies_affected_by_elemental_status_ailment` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 3656 | `elementalist_cold_penetration_%_for_4_seconds_on_using_fire_skill` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 3657 | `elementalist_lightning_penetration_%_for_4_seconds_on_using_cold_skill` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 3658 | `elementalist_fire_penetration_%_for_4_seconds_on_using_lightning_skill` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 3659 | `elementalist_summon_elemental_golem_on_killing_enemy_with_element_%` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 3783 | `occultist_energy_shield_always_recovers_for_4_seconds_after_starting_recovery` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 3796 | `occultist_gain_%_of_non_chaos_damage_as_chaos_damage_per_curse_on_target_on_kill_for_4_seconds` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 3823 | `guardian_reserved_mana_%_given_to_you_and_nearby_allies_as_base_maximum_energy_shield` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 4092 | `gain_attack_and_cast_speed_+%_for_4_seconds_if_taken_savage_hit` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 4092 | `gain_attack_and_cast_speed_+%_for_4_seconds_if_taken_savage_hit` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 4130 | `damage_+%_of_each_type_that_you_have_an_active_golem_of` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 4280 | `physical_damage_%_to_add_as_chaos_vs_bleeding_enemies` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 4295 | `taunted_enemies_damage_+%_final_vs_non_taunt_target` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 4295 | `taunted_enemies_damage_+%_final_vs_non_taunt_target` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 4323 | `gain_defiance_when_lose_life_to_hit_once_per_x_ms` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 4359 | `projectile_attacks_chance_to_bleed_on_hit_%_if_you_have_beast_minion` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 4360 | `projectile_attacks_chance_to_maim_on_hit_%_if_you_have_beast_minion` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 4361 | `projectile_attacks_chance_to_poison_on_hit_%_if_you_have_beast_minion` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 4454 | `local_grant_skeleton_warriors_triple_damage_on_hit` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 4516 | `gain_endurance_charge_per_second_if_have_been_hit_recently_if_4_warlord_items` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 4516 | `gain_endurance_charge_per_second_if_have_been_hit_recently_if_4_warlord_items` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 4537 | `physical_damage_%_to_add_as_each_element_if_6_shaper_items` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 4641 | `additive_armour_modifiers_apply_to_energy_shield_recharge_rate_at_%_value` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 4929 | `attack_minimum_added_physical_damage_if_have_crit_recently attack_maximum_added_physical_damage_if_have_crit_recently` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 5149 | `berserker_warcry_grant_attack_speed_+%_to_you_and_nearby_allies` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 5766 | `chance_to_gain_adrenaline_for_2_seconds_on_leech_removed_by_filling_unreserved_life_%` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 5775 | `chance_to_gain_onslaught_for_4_seconds_on_leech_removed_by_filling_unreserved_life_%` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 5931 | `consecrated_ground_allies_recover_es_as_well_as_life_from_life_regeneration` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 5944 | `consecrated_ground_while_stationary_radius_if_highest_attribute_is_strength` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 6346 | `display_passives_in_large_radius_of_non_unique_jewels_grant_additional_strength` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 6416 | `elemental_damage_taken_+%_if_not_hit_recently` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 6416 | `elemental_damage_taken_+%_if_not_hit_recently` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 6600 | `every_10_seconds_physical_damage_%_to_add_as_fire_for_3_seconds` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 6649 | `fire_and_cold_hit_and_dot_damage_%_taken_as_lightning_while_affected_by_purity_of_lightning` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 6745 | `flasks_adjacent_to_active_tinctures_have_effect_+%_if_hit_with_weapon_recently` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 6745 | `flasks_adjacent_to_active_tinctures_have_effect_+%_if_hit_with_weapon_recently` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 6879 | `gain_maximum_power_charges_on_power_charge_gained_%_chance` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 6898 | `gain_%_total_phys_damage_prevented_in_the_past_10_sec_as_life_regen_per_sec` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 7022 | `ground_slam_chance_to_gain_endurance_charge_%_on_stun` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 7332 | `immune_to_curses_on_killing_cursed_enemy_for_remaining_duration_of_curse` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 7495 | `life_mana_es_recovery_rate_+%_per_different_tribe_tattoos_allocated` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 7495 | `life_mana_es_recovery_rate_+%_per_different_tribe_tattoos_allocated` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 8165 | `local_unique_jewel_blight_hinder_enemy_chaos_damage_taken_+%_with_40_int_in_radius` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 8205 | `local_unique_jewel_glacial_cascade_physical_damage_%_to_convert_to_cold_with_40_int_in_radius` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 8283 | `main_hand_critical_strike_chance_+%_per_melee_abyss_jewel_up_to_+200%` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 8283 | `main_hand_critical_strike_chance_+%_per_melee_abyss_jewel_up_to_+200%` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 8426 | `map_beyond_unique_item_quantity_+%_final_from_flesh_faction` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 8426 | `map_beyond_unique_item_quantity_+%_final_from_flesh_faction` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 8427 | `map_beyond_basic_currency_quantity_+%_final_from_pale_faction` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 8427 | `map_beyond_basic_currency_quantity_+%_final_from_pale_faction` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 8428 | `map_beyond_divination_card_quantity_+%_final_from_demon_faction` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 8428 | `map_beyond_divination_card_quantity_+%_final_from_demon_faction` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 9039 | `map_spawn_beyond_boss_when_beyond_boss_slain_%` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 9372 | `minimum_added_chaos_damage_vs_enemies_with_5+_poisons maximum_added_chaos_damage_vs_enemies_with_5+_poisons` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 9438 | `minion_damage_over_time_multiplier_+_per_minion_abyss_jewel_up_to_+30` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 9516 | `-%_of_your_elemental_damage_cannot_be_reflected_while_affected_by_purity_of_elements` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 9518 | `-%_of_your_lightning_damage_cannot_be_reflected_while_affected_by_purity_of_lightning` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 9531 | `modifiers_to_number_of_projectiles_instead_apply_to_splitting` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 9571 | `movement_speed_+%_per_nearby_enemy` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 9601 | `nearby_enemies_fire_dot_resistance_is_%_while_you_are_stationary` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 9607 | `nearby_enemies_no_fire_dot_resistance_while_you_are_stationary` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 9615 | `necromancer_mana_regeneration_rate_per_minute_for_you_and_allies_per_nearby_corpse` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 9698 | `off_hand_critical_strike_multiplier_+_per_melee_abyss_jewel_up_to_+100` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 9810 | `physical_damage_reduction_%_per_nearby_enemy` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 9840 | `poison_duration_+%_per_poison_applied_recently` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 9881 | `projecitle_chance_to_be_able_to_chain_from_terrain_%_per_ranged_abyss_jewel_up_to_20%` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 10415 | `strength_damage_bonus_grants_melee_physical_damage_+3%_per_10_strength_instead` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 10657 | `unattached_sigil_attachment_range_+%_per_second` | MISS_NL | 缺换行 |
| stat_descriptions.txt | 10806 | `you_and_nearby_allies_armour_+_if_have_impaled_recently` | MISS_NL | 缺换行 |
