"""
从对照数据里反推术语表：每个英文术语的主流中文写法 + 支持度 + 竞争写法。

只输出可信度够高的条目（正向、反向共现都 >= 0.75），其余留在报告里人工判断。
"""

from __future__ import annotations

import csv
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import analyze  # noqa: E402


def main():
    src = sys.argv[1] if len(sys.argv) > 1 else "out/statdesc.csv"
    dst = sys.argv[2] if len(sys.argv) > 2 else "out/glossary.csv"
    os.makedirs(os.path.dirname(dst) or ".", exist_ok=True)

    rows = []
    with open(src, encoding="utf-8", newline="") as f:
        for r in csv.DictReader(f):
            if "en" in r:
                r["zh"] = r.get("zh") or ""
                rows.append(r)

    term_res = {t: re.compile(r"\b" + re.escape(t) + r"\b", re.I) for t in analyze.TERMS}
    lex = analyze.build_lexicon(rows, term_res)

    out = []
    for t, cands in sorted(lex.items()):
        f1, canon, cnt, p, rev = cands[0]
        if p < 0.75 or rev < 0.75 or len(canon) < 2:
            continue
        alts = "; ".join(f"{c}({n})" for _, c, n, *_ in cands[1:4] if c not in canon)
        out.append({
            "term_en": t,
            "term_zh": canon,
            "entries": cnt,
            "coverage": f"{p:.0%}",
            "precision": f"{rev:.0%}",
            "alternatives": alts,
        })

    with open(dst, "w", encoding="utf-8", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(out[0].keys()) if out else ["term_en"])
        w.writeheader()
        w.writerows(out)

    print(f"{len(out)} terms -> {dst}")
    for o in out:
        print(f"  {o['term_en']:16} {o['term_zh']:10} {o['coverage']:>5}/{o['precision']:<5} {o['alternatives'][:40]}")


if __name__ == "__main__":
    main()
