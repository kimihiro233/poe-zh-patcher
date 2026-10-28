# -*- coding: utf-8 -*-
"""数值位置统一：把「{0:+d} ... 的等級」改为「... 等級 {0:+d}」。

背景：英文模板是 "{0:+d} to Level of ..."，中文若照抄语序，就成了
「+1 全部圖騰技能寶石的等級」——读得懂，但全库 81 条技能宝石等级条目里
71 条（88%）是「... 等級 +1」，且官方「所有X的等級 {0:+d}」也是数值在尾。
本批属一致性整治（P3），非必修；与 fix-gem 重叠的条目已合并主动技能限定。
"""
import csv, re

SRC = "out/statdesc.csv"
DST = "out/fix-order.csv"

rows = list(csv.DictReader(open(SRC, encoding="utf-8")))

# 显式映射：只收「数值跑在句首 + 的等級/等級」这一类，逐条人工定稿
FIX = {
 "{0:+d}所有光環技能寶石等級": "所有光環主動技能寶石等級 {0:+d}",
 "{0:+d} 全部圖騰技能寶石的等級": "全部圖騰主動技能寶石等級 {0:+d}",
 "{0:+d} 全部瓦爾技能寶石的等級": "全部瓦爾主動技能寶石等級 {0:+d}",
 "{0:+d} 全部暴擊輔助寶石的等級": "全部暴擊輔助寶石等級 {0:+d}",
 "{0:+d} 插槽中全部物理寶石的等級": "插槽中物理寶石等級 {0:+d}",
 "{0:+d} 被輔助混沌技能寶石的等級": "被輔助混沌技能寶石等級 {0:+d}",
 "{0:+d} 被輔助元素技能寶石的等級": "被輔助元素技能寶石等級 {0:+d}",
 "{0:+d} 被輔助召喚物技能寶石的等級": "被輔助召喚物技能寶石等級 {0:+d}",
 "{0:+d} 被輔助物理技能寶石的等級": "被輔助物理技能寶石等級 {0:+d}",
 "{0:+d} 被輔助打擊技能寶石的等級": "被輔助打擊技能寶石等級 {0:+d}",
}

PH = re.compile(r"\{\d*[^}]*\}|#|%")

out, bad = [], []
for r in rows:
    new = FIX.get(r["zh"])
    if new is None:
        continue
    if sorted(PH.findall(r["zh"])) != sorted(PH.findall(new)):
        bad.append((r["stat_ids"], r["zh"], new)); continue
    out.append({"stat_ids": r["stat_ids"], "file": r["file"], "cond": r["cond"],
                "en": r["en"], "zh_old": r["zh"], "zh_new": new,
                "note": "与 fix-gem 重叠，两批同时生效时以本条为准" if "主動" in new else ""})

with open(DST, "w", encoding="utf-8-sig", newline="") as f:
    w = csv.DictWriter(f, fieldnames=["stat_ids","file","cond","en","zh_old","zh_new","note"])
    w.writeheader(); w.writerows(out)

print("写入 %s：%d 条（占位符不符 %d 条）" % (DST, len(out), len(bad)))
for b in bad:
    print("   x", b[0], b[1], "->", b[2])
for r in out:
    print("  %-44s -> %s" % (r["zh_old"], r["zh_new"]))
