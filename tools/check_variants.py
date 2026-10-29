# -*- coding: utf-8 -*-
"""排查「英文与繁中的变体条件对不上」的块 —— 对照版会不会把英文挂错行。

背景：对照版是按**变体序号**给繁中行追加英文的（第 N 个变体配第 N 个）。
DATA-FINDINGS 记的是 14,899 / 14,948 个块两侧条件序列完全一致，剩下这些不一致。
序号配对在这批块里还成不成立？逐块判定，分四类：

  A 变体数不同    一侧多出变体 —— 不会错配，最多少一行英文
  B negate 不一致 同序号两侧一个取负、一个不取 —— 文本极性可能反，**高风险**
  C 区间不相交    同序号两侧条件区间没有交集 —— 该变体可能配到了另一段语义，**要看**
  D 仅写法差异    区间有交集（`1` vs `#`、`2|#` vs `#`）—— 无风险

判据只看条件与 handler，不猜文本语义；B/C 两类要人工读一眼文本才能定论，
所以脚本负责把候选压到个位数，报告里再逐条写结论。

用法：
    python tools/check_variants.py                     # 默认 raw_sd -> out/variant-check.{csv,md}
    python tools/check_variants.py raw_sd out/x.csv
"""
from __future__ import annotations

import csv
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from patch_sd import SdFile  # noqa: E402

KIND_ORDER = {"B": 0, "C": 1, "A": 2, "D": 3}

# 已经人工判过的 B / C 条目：写在这里，重跑时不会重新变成「待判」。
# 判据是**文本语义**，脚本算不出来，只能人读一眼再登记。
VERDICTS = {
    ("heist_equipment_stat_descriptions.txt", 31):
        "配对正确。英文用「负值 + negate」、繁中用「100% 上限」表达同一件事，"
        "两侧文本都是「必定内含双倍」的措辞，且英文该变体不含 {0}，negate 不影响显示。",
    ("stat_descriptions.txt", 7094):
        "配对正确。同上：`#|-1`+negate 与 `100|#` 是同一段语义的两种写法，文本一致。",
    ("stat_descriptions.txt", 7095):
        "配对正确。`Heist Chests contain more valuable Uniques` ↔ "
        "「劫盜保險箱內含更多有價值的傳奇」，语义一一对应。",
    ("stat_descriptions.txt", 1490):
        "配对正确。两侧都带 negate 1、都是 reduced 措辞；只是繁中把负值区写成 `1|#`"
        "（取负后匹配），英文写 `#|-1`。条件写法不同，不影响对照。",
}

# 人工注记：不属 B/C，但值得写进报告（多为繁中自身的译文缺陷）。
NOTES = {
    "deepwater_chance_to_ancient_%":
        "繁中把「{0}% 機率」整段丢了，且英文的 100% 变体没有中文对应 —— "
        "玩家看到「機會石改為掉落古變石」会读成必定转换，机制读错。已在 fix-variant-hold.csv 立稿。",
    "deepwater_chaos_to_divine_%": "同上：漏 {0}%，读成「混沌石必定變神聖石」。",
    "deepwater_scour_to_annulment_%": "同上：漏 {0}%，读成「重鑄石必定變無效石」。",
    "local_unique_jewel_viper_strike_chance_to_gain_unholy_might_%_on_hit_per_poison_stack_on_enemy_with_50_dex_in_radius":
        "繁中多一个 100% 变体，英文没有 → 该行取不到英文（属已知的 12 行之一），不会错配。",
    "uber_domain_monster_critical_strike_multiplier_+%_per_revival":
        "繁中多一个「減少」变体（`#|-1` + negate），英文只有「增加」→ 该行取不到英文，不会错配。",
    "strongbox_gold_cost_to_open":
        "英文第二个变体是 `Costs ??? Gold to open`（官方占位符没填），繁中只有一行 → 忽略即可。",
}


def parse_tok(tok: str):
    """`1|#` -> (1, None)；`#|-1` -> (None, -1)；`100` -> (100, 100)。"""
    lo, hi = (tok.split("|", 1) + [tok])[:2] if "|" in tok else (tok, tok)
    return (None if lo == "#" else int(lo), None if hi == "#" else int(hi))


def parse_cond(cond: str):
    return [parse_tok(t) for t in cond.split()] if cond.strip() else []


def overlap(a, b) -> bool:
    lo = max([x for x in (a[0], b[0]) if x is not None], default=None)
    hi = min([x for x in (a[1], b[1]) if x is not None], default=None)
    if lo is None or hi is None:
        return True
    return lo <= hi


def has_negate(h: str) -> bool:
    return "negate" in (h or "")


def judge(en_vs, tc_vs):
    """返回 (类别, 首个问题变体序号, 说明)。"""
    if len(en_vs) != len(tc_vs):
        return "A", min(len(en_vs), len(tc_vs)), "变体数 %d/%d" % (len(tc_vs), len(en_vs))
    for i, (e, t) in enumerate(zip(en_vs, tc_vs)):
        if has_negate(e[3]) != has_negate(t[3]):
            return "B", i, "negate 只在%s" % ("英文" if has_negate(e[3]) else "繁中")
        ce, ct = parse_cond(e[1]), parse_cond(t[1])
        if len(ce) == len(ct) and not all(overlap(a, b) for a, b in zip(ce, ct)):
            return "C", i, "区间不相交 %s / %s" % (t[1], e[1])
    return "D", -1, "条件写法不同、区间有交集"


def scan(root: str):
    rows = []
    for fn in sorted(os.listdir(root)):
        if not fn.endswith(".txt"):
            continue
        sf = SdFile(os.path.join(root, fn))
        short = fn.replace("metadata_statdescriptions_", "")
        for bi, (ids, langs) in enumerate(sf.blocks):
            en = next((vs for lang, vs in langs if lang == ""), [])
            tc = next((vs for lang, vs in langs if lang == "Traditional Chinese"), [])
            if not en or not tc:
                continue
            if [v[1] for v in en] == [v[1] for v in tc] and len(en) == len(tc):
                continue
            kind, idx, why = judge(en, tc)
            e = en[idx] if 0 <= idx < len(en) else None
            t = tc[idx] if 0 <= idx < len(tc) else None
            rows.append({
                "file": short, "block": bi, "stat_ids": ids, "kind": kind,
                "variant": idx, "why": why,
                "en_cond": e[1] if e else "", "zh_cond": t[1] if t else "",
                "en_handler": e[3] if e else "", "zh_handler": t[3] if t else "",
                "en": e[2] if e else "", "zh": t[2] if t else "",
            })
    rows.sort(key=lambda r: (KIND_ORDER[r["kind"]], r["file"], r["block"]))
    return rows


def write_csv(rows, path):
    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    with open(path, "w", encoding="utf-8-sig", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0].keys()) if rows else ["file"])
        w.writeheader()
        w.writerows(rows)


def write_md(rows, path, root):
    cnt = {}
    for r in rows:
        cnt[r["kind"]] = cnt.get(r["kind"], 0) + 1
    lines = [
        "# 变体条件对不上的块（对照版挂错英文的排查）",
        "",
        "源：`%s`；判据见 `tools/check_variants.py` 头注释。" % root,
        "",
        "| 类别 | 含义 | 块数 |",
        "|---|---|---|",
        "| B | negate 不一致，文本极性可能反（高风险） | %d |" % cnt.get("B", 0),
        "| C | 同序号区间不相交，可能配错语义（要看） | %d |" % cnt.get("C", 0),
        "| A | 变体数不同（不会错配，最多少一行英文） | %d |" % cnt.get("A", 0),
        "| D | 仅条件写法不同（无风险） | %d |" % cnt.get("D", 0),
        "| 合计 | | %d |" % len(rows),
        "",
        "## 需要人工看的（B / C）",
        "",
    ]
    for r in rows:
        if r["kind"] not in ("B", "C"):
            continue
        verdict = VERDICTS.get((r["file"], r["block"]))
        lines += [
            "### %s · block#%d · `%s`" % (r["file"], r["block"], r["stat_ids"]),
            "",
            "- %s（变体 #%d）" % (r["why"], r["variant"]),
            "- EN `%s` %s" % (r["en_cond"], r["en_handler"]),
            "  - %s" % r["en"],
            "- ZH `%s` %s" % (r["zh_cond"], r["zh_handler"]),
            "  - %s" % r["zh"],
            "- **判定**：%s" % (verdict or "待判"),
            "",
        ]
    notes = [(r, NOTES[r["stat_ids"]]) for r in rows if r["stat_ids"] in NOTES]
    if notes:
        lines += ["## 附带发现（人工注记）", ""]
        for r, n in notes:
            lines += ["- `%s`（%s block#%d，%s）" % (r["stat_ids"], r["file"], r["block"], r["kind"]),
                      "  - %s" % n]
        lines += [""]
    lines += ["## 其余（A / D）", "", "| 文件 | 块 | stat_ids | 类别 | 说明 |", "|---|---|---|---|---|"]
    for r in rows:
        if r["kind"] in ("B", "C"):
            continue
        lines.append("| %s | %d | `%s` | %s | %s |"
                     % (r["file"], r["block"], r["stat_ids"], r["kind"], r["why"]))
    open(path, "w", encoding="utf-8", newline="\n").write("\n".join(lines) + "\n")


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else "raw_sd"
    csv_path = sys.argv[2] if len(sys.argv) > 2 else "out/variant-check.csv"
    md_path = os.path.splitext(csv_path)[0] + ".md"
    rows = scan(root)
    write_csv(rows, csv_path)
    write_md(rows, md_path, root)
    cnt = {}
    for r in rows:
        cnt[r["kind"]] = cnt.get(r["kind"], 0) + 1
    todo = [r for r in rows if r["kind"] in ("B", "C") and (r["file"], r["block"]) not in VERDICTS]
    print("条件序列不一致的块：%d（B %d / C %d / A %d / D %d），待判 %d"
          % (len(rows), cnt.get("B", 0), cnt.get("C", 0), cnt.get("A", 0), cnt.get("D", 0), len(todo)))
    print("-> %s\n-> %s" % (csv_path, md_path))
    return 1 if todo else 0


if __name__ == "__main__":
    raise SystemExit(main())
