"""
方向检查：increased / reduced / more / less 与繁中译法的一致性。

为什么必须按「变体对」检查，而不是逐条比单词
--------------------------------------------
PoE 里 `10% increased Damage` 与 `10% reduced Damage` 是**同一个 stat**，
取值分别是 +10 / -10。游戏不靠译文表达方向，而是按取值落在哪个区间挑变体：

    description
        1 damage_+%
        2
            1|#   "{0}% increased Damage"              值 >= 1
            #|-1  "{0}% reduced Damage" negate 1       值 <= -1，显示前取反

`1|#` / `#|-1` / `1|99` / `100|#` 都是区间条件（`#` 表示该侧无界），
`negate 1` 表示显示前把数字取负。所以「数值可能是负数」这件事，
在数据层是靠**两个变体分工**解决的，不是靠某一句话的措辞。

由此得出的检查方式：
1. 把同一块里的变体按「非区间条件」（如 `!0`）分组，组内找一对正区间 / 负区间变体；
2. 英文这对措辞方向必须相反，中文这对也必须相反，且各自与英文同侧；
3. 中文两变体同向（最常见）或互换（较少）都是缺陷——负值那一侧会显示错方向。

为什么还要单独判「源数据缺陷」
------------------------------
英文源数据本身有漏：例如 `damage_+%` 的正负两个变体都写 "increased"，
负变体只加了 `negate 1`，于是英文客户端对 -20 显示「20% increased」。
繁中、法、俄、泰、西、简中都在负变体改成「減少」。这属**修正源数据**，
不能算错译，更不能被批量脚本改回去。

分类
----
    S1_neg_variant_wrong    负区间变体用了正向措辞（最常见，机制会算错）
    S1p_pos_variant_wrong   正区间变体用了负向措辞（少见）
    S2_pair_swapped         一对变体整体互换
    S3_src_defect_zh_fixed  英文两变体同文、中文区分正确 → 中文更准，不是错
    M1_additive_as_mult     increased/reduced 译成「更多/更少」（加法写成乘法）
    M2_mult_as_additive     more/less 译成「增加/減少」（乘法写成加法）
    F_sign_format           符号格式化：`{0:+d}` 丢成 `{0}`，或负变体仍带 `+{0}`
"""

from __future__ import annotations

import csv
import glob
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import parse_statdesc as p  # noqa: E402

# ---------- 区间与条件 ----------

POS, NEG, OTHER = "pos", "neg", "other"


def split_cond(cond: str):
    """`1|# !0` → ('1|#', '!0')。区间条件在前，其余条件（如 !0）作为分组键。"""
    parts = (cond or "").split()
    if not parts:
        return "", ""
    return parts[0], " ".join(parts[1:])


def range_class(cond: str) -> str:
    rng, _ = split_cond(cond)
    if "|" not in rng:
        return OTHER
    a, b = rng.split("|", 1)[0].strip(), rng.split("|", 1)[-1].strip()
    a, b = a.split()[0] if a else a, b.split()[0] if b else b

    def n(x):
        try:
            return int(x)
        except ValueError:
            return None

    lo, hi = n(a), n(b)
    if lo is not None and lo >= 1 and hi is None:
        return POS
    if lo is None and hi is not None and hi <= -1:
        return NEG
    return OTHER


# ---------- 方向词 ----------

EN_RE = {
    "INC": re.compile(r"\bincreased\b", re.I),
    "RED": re.compile(r"\breduced\b", re.I),
    "MORE": re.compile(r"\bmore\b", re.I),
    "LESS": re.compile(r"\bless\b", re.I),
}
# 不要放「增益」(= buff)，否则 "減少 {0}% 增益效果" 会被误判成「增加」
ZH = {
    "INC": ("增加", "提高", "提升", "增多", "上升"),
    "RED": ("減少", "降低", "下降", "減低", "減弱"),
    "MORE": ("更多",),
    "LESS": ("更少",),
}
POL = {"INC": 1, "RED": -1, "MORE": 1, "LESS": -1}


def en_dir(text: str):
    hit = [t for t, rx in EN_RE.items() if rx.search(text)]
    return hit[0] if len(hit) == 1 else None


def zh_dir(text: str):
    hit = {t for t, ws in ZH.items() if any(w in text for w in ws)}
    return hit.pop() if len(hit) == 1 else None


# ---------- 检查 ----------

def load_blocks(src_glob: str):
    for path in sorted(glob.glob(src_glob)):
        for fname, sids, blocks, _ in p.parse_file(path):
            en = p.variants(blocks, "")
            zh = p.variants(blocks, "Traditional Chinese")
            if en:
                yield fname, sids, en, zh


def check_pairs(en, zh):
    """按块比较正负变体对，返回 [(kind, detail)]。"""
    out = []
    if not zh:
        return out
    if len(zh) < len(en):
        zh = list(zh) + [("", "", "")] * (len(en) - len(zh))

    groups: dict[str, list[int]] = {}
    for i, (cond, _, _) in enumerate(en):
        _, rest = split_cond(cond)
        groups.setdefault(rest, []).append(i)

    for rest, idxs in groups.items():
        pos = [i for i in idxs if range_class(en[i][0]) == POS]
        neg = [i for i in idxs if range_class(en[i][0]) == NEG]
        if not pos or not neg:
            continue
        pi, ni = pos[0], neg[0]
        if ni >= len(zh) or pi >= len(zh):
            continue
        ed_p, ed_n = en_dir(en[pi][1]), en_dir(en[ni][1])
        zd_p, zd_n = zh_dir(zh[pi][1]), zh_dir(zh[ni][1])
        if not (ed_p and ed_n and zd_p and zd_n):
            continue

        ep, en_ = en[pi][1], en[ni][1]
        zp, zn = zh[pi][1], zh[ni][1]
        ctx = (f"正[{en[pi][0]}] {ep}\n负[{en[ni][0]}] {en_}",
               f"正 {zp}\n负 {zn}")

        # 英文两变体方向相同 → 源数据缺陷
        if POL[ed_p] == POL[ed_n]:
            if POL[zd_p] != POL[zd_n]:
                out.append(("S3_src_defect_zh_fixed",
                            f"英文正负变体同向({ed_p}/{ed_n})，中文已区分为 {zd_p}/{zd_n}", ctx))
            continue

        ok_p = POL[zd_p] == POL[ed_p]
        ok_n = POL[zd_n] == POL[ed_n]
        if ok_p and ok_n:
            continue
        if not ok_p and not ok_n:
            swapped = POL[zd_p] == -POL[ed_p] and POL[zd_n] == -POL[ed_n]
            kind = "S2_pair_swapped" if swapped else "S1_neg_variant_wrong"
            out.append((kind, f"正变体应 {ed_p} 实为 {zd_p}；负变体应 {ed_n} 实为 {zd_n}", ctx))
        elif not ok_n:
            out.append(("S1_neg_variant_wrong", f"负变体应 {ed_n} 实为 {zd_n}", ctx))
        else:
            out.append(("S1p_pos_variant_wrong", f"正变体应 {ed_p} 实为 {zd_p}", ctx))
    return out


def check_family(cond, txt, zh_txt):
    """加法词 / 乘法词混用（与符号无关，逐条查）。"""
    ed, zd = en_dir(txt), zh_dir(zh_txt)
    if not ed or not zd or ed == zd:
        return None
    if ed in ("INC", "RED") and zd in ("MORE", "LESS"):
        return "M1_additive_as_mult", f"{ed} 应译加法词，此处用 {zd}"
    if ed in ("MORE", "LESS") and zd in ("INC", "RED"):
        return "M2_mult_as_additive", f"{ed} 应译「更多/更少」，此处用 {zd}"
    return None


def check_sign_format(cond, en_txt, zh_txt):
    """符号格式化：负变体不该再带 `+`；`{0:+d}` 不该退化成 `{0}`。"""
    if "{0:+d}" in en_txt and "{0:+d}" not in zh_txt:
        return "F_sign_format", "英文 {0:+d} 在中文丢成 {0}，正负号消失"
    if (range_class(cond) == NEG
            and re.search(r"\+\s*\{0\}", zh_txt)
            and not re.search(r"\+\s*\{0\}", en_txt)):
        return "F_sign_format", "负变体中文仍带 + 号，会显示 +-N"
    return None


def main():
    src = sys.argv[1] if len(sys.argv) > 1 else "raw_sd/*.txt"
    out = sys.argv[2] if len(sys.argv) > 2 else "out/direction-check.csv"
    rows, counts = [], {}

    for fname, sids, en, zh in load_blocks(src):
        for idx, (cond, txt, hnd) in enumerate(en):
            z = zh[idx][1] if idx < len(zh) else ""
            if not z or not any("\u4e00" <= c <= "\u9fff" for c in z):
                continue
            hits = []
            fam = check_family(cond, txt, z)
            if fam:
                hits.append(fam)
            fmt = check_sign_format(cond, txt, z)
            if fmt:
                hits.append(fmt)
            if hits:
                rows.append([fname, sids, idx, cond, hnd, txt, z, hits])

        for kind, detail, ctx in check_pairs(en, zh):
            rows.append([fname, sids, -1, "", "", ctx[0], ctx[1], [(kind, detail)]])

    with open(out, "w", encoding="utf-8", newline="") as f:
        w = csv.writer(f)
        w.writerow(["kind", "file", "stat_ids", "variant", "cond", "handlers",
                    "en", "zh", "detail"])
        for fname, sids, idx, cond, hnd, txt, z, hits in rows:
            for kind, detail in hits:
                counts[kind] = counts.get(kind, 0) + 1
                w.writerow([kind, fname, sids, idx, cond, hnd, txt, z, detail])

    print("问题统计：")
    for k in sorted(counts):
        print(f"  {k:26} {counts[k]:5}")
    print(f"-> {out}")

    by: dict[str, list] = {}
    for r in rows:
        for kind, _ in r[7]:
            by.setdefault(kind, []).append(r)
    for k in sorted(by):
        print(f"\n== {k} 样例 ==")
        for r in by[k][:3]:
            print(f"  [{r[3]}|{r[4][:12]}] {r[5][:70]}")
            if r[6]:
                print(f"      -> {r[6][:70]}")


if __name__ == "__main__":
    main()
