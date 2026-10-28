# -*- coding: utf-8 -*-
"""Skill Gems → 主動技能寶石 的限定补全。
只处理「数值修饰族」：Level / Quality + (all|Socketed) + ... Skill Gems。
不动 Supported Skill Gems（被辅助的必为主动，且加了累赘），
也不动掉落/条件类（语义不是"只对主动生效"）。
本文件只做限定词补全，不夹带语序调整（语序类见 fix_order.py）。
"""
import csv, re

SRC, DST = "out/statdesc.csv", "out/fix-gem.csv"
PAT = re.compile(r"(Level|Quality) of (all|Socketed)\b[^.]*?\bSkill Gems?\b", re.I)
KEEP_OUT = re.compile(r"Supported", re.I)

# 同条内的漏字（与语序无关，属本次限定词工作的一部分）
PATCH = {
 "每顆插槽中的寶石，插槽主動技能寶石等級 {:+d}": "每顆插槽中的寶石，插槽中主動技能寶石等級 {:+d}",
}

# 语序类暂缓，交由 fix_order.py 单列
DEFER = {
 "{0:+d}所有光環主動技能寶石等級",
 "{0:+d} 全部圖騰主動技能寶石的等級",
 "{0:+d} 全部瓦爾主動技能寶石的等級",
 "{0:+d} 全部物理寶石的等級",
}

rows = list(csv.DictReader(open(SRC, encoding="utf-8")))
out = []
for r in rows:
    if not PAT.search(r["en"]) or KEEP_OUT.search(r["en"]):
        continue
    if "技能寶石" not in r["zh"] or "主動技能寶石" in r["zh"]:
        continue
    new = r["zh"].replace("技能寶石", "主動技能寶石")
    new = PATCH.get(new, new)
    if new == r["zh"] or new in DEFER:
        continue
    out.append({"stat_ids": r["stat_ids"], "file": r["file"], "cond": r["cond"],
                "en": r["en"], "zh_old": r["zh"], "zh_new": new})

with open(DST, "w", encoding="utf-8-sig", newline="") as f:
    w = csv.DictWriter(f, fieldnames=["stat_ids","file","cond","en","zh_old","zh_new"])
    w.writeheader(); w.writerows(out)

tmpl = {}
for r in out:
    k = re.sub(r"\{0[^}]*\}", "#", r["en"])
    tmpl.setdefault(k, r["zh_new"])
print("待改 %d 条变体，%d 个模板" % (len(out), len(tmpl)))
for k, v in tmpl.items():
    print("  %-58s -> %s" % (k.replace("\n", " / ")[:58], v.replace("\n", " / ")))
