"""
对已抽取的 EN/ZH 对照做质量体检，产出可直接落地的修改清单。

S1 简体字渗入      —— 繁中块里出现简体字形
S2 占位符不一致    —— {0} / # / % / \\n 数量或编号对不上（直接导致显示错值）
S3 未翻译 / 空译文
S4 术语不一致      —— 用双向共现（F1）确定每个术语的「主流译法」，再找出偏离它的条目
S5 长度异常        —— 译文远长于原文，界面易溢出
"""

from __future__ import annotations

import csv
import os
import re
import sys
import collections

TOKEN_RE = re.compile(r"\{\s*[-+]?\d[^}]*\}|#|%|\\n")
TAG_RE = re.compile(r"<([a-zA-Z][\w-]*)>")
CJK = re.compile(r"[\u4e00-\u9fff]")

# 这些字在正体里本身就合法，zhconv 仍会改写，属误报
AMBIGUOUS = set("里干台制游岩征托污凶占布只才后表松郁咸卷折志于余云采系划尸决冲准范蒙苏莉祕")

TERMS = [
    "Life", "Mana", "Energy Shield", "Armour", "Evasion", "Damage", "Attack",
    "Spell", "Minion", "Skill", "Gem", "Resistance", "Fire", "Cold", "Lightning",
    "Chaos", "Physical", "Elemental", "Critical", "Accuracy", "Stun", "Chill",
    "Freeze", "Ignite", "Bleed", "Poison", "Leech", "Regeneration", "Recovery",
    "Aura", "Curse", "Block", "Projectile", "Melee", "Duration", "Radius",
    "Rarity", "Quantity", "Quality", "Warcry", "Totem", "Trap", "Mine", "Brand",
    "Impale", "Suppression", "Ward", "Onslaught", "Fortify", "Elusive",
]


def has_cjk(s: str) -> bool:
    return bool(CJK.search(s))


def zh_diffs(s: str) -> list[str]:
    try:
        import zhconv
    except ImportError:
        return []
    conv = zhconv.convert(s, "zh-tw")
    if conv == s:
        return []
    return [f"{a}→{b}" for a, b in zip(s, conv) if a != b and a not in AMBIGUOUS]


def tok(s: str) -> collections.Counter:
    return collections.Counter(TOKEN_RE.findall(s))


def ngrams(zh: str, lo: int = 2, hi: int = 4):
    for n in range(lo, hi + 1):
        for i in range(len(zh) - n + 1):
            seg = zh[i:i + n]
            if all("\u4e00" <= c <= "\u9fff" for c in seg):
                yield seg


def build_lexicon(rows, term_res):
    """用双向共现给每个英文术语找主流中文写法。"""
    lex = {}
    for t, rx in term_res.items():
        hit = [r["zh"] for r in rows if rx.search(r["en"]) and has_cjk(r["zh"])]
        if len(hit) < 5:
            continue
        total = len(hit)
        df = collections.Counter()
        for zh in hit:
            for g in set(ngrams(zh)):
                df[g] += 1
        cands = []
        for g, c in df.items():
            if c < max(4, total * 0.08) or len(g) < 2:
                continue
            rev = sum(1 for r in rows if g in r["zh"] and rx.search(r["en"]))
            p = c / total
            rr = rev / c
            f1 = 2 * p * rr / (p + rr) if p + rr else 0
            cands.append((round(f1, 3), g, c, round(p, 2), round(rr, 2)))
        cands.sort(key=lambda x: (x[0], len(x[1]), x[2]), reverse=True)
        kept = []
        for item in cands:
            if any(item[1] != k[1] and item[1] in k[1] and k[0] >= item[0] - 0.02 for k in kept):
                continue
            kept.append(item)
            if len(kept) >= 5:
                break
        if kept:
            lex[t] = kept
    return lex


DIR_EN = {
    "more": "更多", "increased": "增加", "less": "更少", "reduced": "減少",
    "maximum": "最高", "minimum": "最低",
}
DIR_ZH = ["更多", "更少", "增加", "減少", "最高", "最低", "最大", "最小"]
# 反义组合（译反）
INVERT = {("more", "更少"), ("less", "更多"), ("increased", "減少"),
          ("reduced", "增加"), ("maximum", "最低"), ("minimum", "最高")}


def check_direction(en: str, zh: str):
    """英文里只有一个方向词、中文里也只有一个方向词，且两者反义 → 疑似译反。"""
    hits = [(w, DIR_EN[w]) for w in DIR_EN if re.search(r"\b" + w + r"\b", en, re.I)]
    if len(hits) != 1:
        return None
    found = [z for z in DIR_ZH if z in zh]
    if len(found) != 1:
        return None
    w, expect = hits[0]
    got = found[0]
    if got in ("最大", "最小"):
        return None
    if (w, got) in INVERT:
        return (w, expect, got)
    return None


def key_of(r: dict) -> str:
    return "|".join(str(r.get(k, "")) for k in ("file", "stat_ids", "variant", "table", "row") if k in r)


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else "out/statdesc.csv"
    out_md = sys.argv[2] if len(sys.argv) > 2 else "out/report-statdesc.md"
    out_csv = os.path.splitext(out_md)[0] + "-issues.csv"
    os.makedirs(os.path.dirname(out_md) or ".", exist_ok=True)

    rows = []
    with open(path, encoding="utf-8", newline="") as f:
        for r in csv.DictReader(f):
            if "en" in r:
                r["zh"] = r.get("zh") or ""
                rows.append(r)

    empty = sum(1 for r in rows if not r["zh"].strip())
    same = sum(1 for r in rows if r["zh"] and r["zh"] == r["en"])

    s1, s2, s5, s6 = [], [], [], []
    for r in rows:
        zh, en = r["zh"], r["en"]
        if not zh:
            continue
        d = zh_diffs(zh)
        if d:
            s1.append((r, d))
        te, tz = tok(en), tok(zh)
        ga, gb = collections.Counter(TAG_RE.findall(en)), collections.Counter(TAG_RE.findall(zh))
        if ga != gb:
            te = te + (ga - gb)
            tz = tz + (gb - ga)
        if te != tz:
            s2.append((r, dict(te - tz), dict(tz - te)))
        if len(en) > 12 and len(zh) > len(en) * 2.2:
            s5.append(r)
        # 方向问题由 tools/direction.py 负责（需要按变体对比较，逐条比单词会误报）
        dv = None

    term_res = {t: re.compile(r"\b" + re.escape(t) + r"\b", re.I) for t in TERMS}
    lex = build_lexicon(rows, term_res)
    s4 = []
    for t, cands in lex.items():
        canon = cands[0][1]
        for r in rows:
            if not term_res[t].search(r["en"]) or not has_cjk(r["zh"]):
                continue
            if canon in r["zh"]:
                continue
            alts = [c for _, c, *_ in cands[1:] if c in r["zh"]]
            if alts:
                s4.append((t, canon, alts[0], r))

    L = []
    L.append("# 译文体检报告\n")
    L.append(f"条目总数 **{len(rows)}**｜空译文 **{empty}**｜未翻译 **{same}**｜"
             f"简体字渗入 **{len(s1)}**｜占位符不一致 **{len(s2)}**｜长度异常 **{len(s5)}**｜"
             f"术语偏离 **{len(s4)}**\n")

    L.append("\n## S2 占位符不一致\n")
    L.append("`{n}` 编号被改会显示错误数值；`#` 丢失会让数值无法填入；`\\n` 丢失会挤成一行。\n")
    kinds = collections.Counter()
    for r, miss, extra in s2:
        keys = list(miss) + list(extra)
        if any("{" in k for k in keys):
            kinds["数值占位符"] += 1
        elif "#" in keys:
            kinds["# 数量符"] += 1
        elif "\\n" in keys:
            kinds["换行"] += 1
        else:
            kinds["% 百分号"] += 1
    L.append("分类：" + "、".join(f"{k} {v}" for k, v in kinds.most_common()) + "\n")
    L.append("| 原文 | 繁中 | 缺 | 多 |\n|---|---|---|---|")
    for r, miss, extra in s2[:25]:
        L.append(f"| `{r['en'][:64]}` | {r['zh'][:64]} | {list(miss)} | {list(extra)} |")

    L.append("\n> 方向（increased/reduced/more/less）的检查见 `tools/direction.py`：\n"
             "> 必须按「正负变体对」比较，逐条比单词会把中文替英文补漏的条目误判成错译。\n")

    L.append("\n## S4 术语一致性\n")
    L.append("| 术语 | 主流译法 | 正向 / 反向共现 | 其他写法 |\n|---|---|---|---|")
    for t, cands in sorted(lex.items()):
        canon, p, rev = cands[0][1], cands[0][3], cands[0][4]
        alts = "、".join(f"{c}({n})" for _, c, n, *_ in cands[1:4])
        L.append(f"| {t} | {canon} | {p:.0%} / {rev:.0%} | {alts} |")

    L.append("\n### 术语偏离示例\n")
    for t, canon, alt, r in s4[:20]:
        L.append(f"- **{t}** 主流是「{canon}」，此条用「{alt}」：`{r['en'][:58]}` → {r['zh'][:58]}")

    L.append("\n## S1 简体字渗入\n")
    c = collections.Counter()
    for _, d in s1:
        for x in d:
            c[x.split("→")[0]] += 1
    L.append("字形频次：" + "、".join(f"{k}×{v}" for k, v in c.most_common(20)))
    for r, d in s1[:12]:
        L.append(f"- `{r['en'][:52]}` → {r['zh'][:52]}　（{' '.join(d)}）")

    L.append("\n## S5 长度异常\n")
    for r in s5[:10]:
        L.append(f"- `{r['en'][:40]}`({len(r['en'])}) → {r['zh'][:40]}({len(r['zh'])})")

    open(out_md, "w", encoding="utf-8").write("\n".join(L))

    with open(out_csv, "w", encoding="utf-8", newline="") as f:
        w = csv.writer(f)
        w.writerow(["kind", "key", "detail", "en", "zh", "suggest"])
        for r, miss, extra in s2:
            w.writerow(["placeholder", key_of(r), f"missing={miss} extra={extra}", r["en"], r["zh"], ""])
        for t, canon, alt, r in s4:
            w.writerow(["term", key_of(r), f"{t}: 应为 {canon} 此处 {alt}", r["en"], r["zh"], ""])
        for r, d in s1:
            w.writerow(["simplified", key_of(r), " ".join(d), r["en"], r["zh"], ""])


    print("\n".join(L[:26]))
    print(f"\n报告：{out_md}\n清单：{out_csv}")


if __name__ == "__main__":
    main()
