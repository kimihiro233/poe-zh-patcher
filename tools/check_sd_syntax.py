"""
statdescriptions 语法体检 + 「客户端被改过」指纹。

体检单个目录（默认 raw_sd）时只做语法层判断；给 --canon 时再与基线逐 (stat块, 语言, 变体)
比对，反推对方文件被怎样改写。

判定项：

  E1 括号不平衡      `{{` / `}}` 不配对（渲染会吞字或露出源码）
  E2 尖括号不平衡    `<` 与 `>` 数量不等
  E3 非法标签        不在规范词表内、也不是合法的 `<rgb(r,g,b)>`
  E4 非法占位符      `{0}` / `{0:+d}` / `{}` 以外的花括号残留（如 `{0]`）
  W1 markup 空声明   行尾有 `markup` 关键字，正文已无任何标记
  W2 裸 rgb          直接写 `<rgb(…)`——原版 45 个词缀文件里出现 0 次，出现即非原版产出
  D1 定位键冲突      同一文件内 (stat_ids, lang) 出现多次

比对项（--canon）：命名标签丢失 / 标签换 rgb / 无标记处新增颜色。

用法：
    python tools/check_sd_syntax.py raw_sd
    python tools/check_sd_syntax.py out/collect-20261028/files --canon raw_sd
    python tools/check_sd_syntax.py out/collect-20261028/files --canon raw_sd out/report.md
"""

from __future__ import annotations

import glob
import os
import re
import sys
import collections

import parse_statdesc as ps

NAMETAG = re.compile(r"<([a-z][a-z0-9_]*)>")
RGBC = re.compile(r"<rgb\(\s*\d{1,3}\s*,\s*\d{1,3}\s*,\s*\d{1,3}\s*\)>")
ANYTAG = re.compile(r"<[^<>]{1,40}>")
PLACEHOLDER = re.compile(r"\{[^{}]*\}")
BRACED = re.compile(r"\{\{(?:[^{}]|\{[^{}]*\})*\}\}")


def norm(fname: str) -> str:
    """raw_sd/ 里是 metadata_statdescriptions_<x>.txt，采集包里是 <x>.txt，统一成后者。"""
    return fname.replace("metadata_statdescriptions_", "")


def index(src: str):
    """→ {(文件, stat_ids, 语言, 变体序): (文本, 条件, handler, 有无 markup 关键字)}"""
    out = {}
    dups = []
    for path in sorted(glob.glob(os.path.join(src, "*.txt"))):
        fname = norm(os.path.basename(path))
        seen = collections.Counter()
        for _, ids, blocks, _dv in ps.parse_file(path):
            for lang, raw_lines in blocks:
                seen[(ids, lang)] += 1
                if seen[(ids, lang)] == 2:
                    dups.append((fname, ids, lang))
                for vi, raw in enumerate(raw_lines):
                    cond, txt, hnd = ps.split_text(raw)
                    out[(fname, ids, lang, vi)] = (
                        txt, cond.strip(), hnd, "markup" in hnd.split())
    return out, dups


def lint(txt: str, has_mk: bool, canon_tags: set):
    issues = []
    rest = BRACED.sub("", txt)
    if "{{" in rest or "}}" in rest:
        issues.append("E1 括号不平衡")
    if txt.count("<") != txt.count(">"):
        issues.append("E2 尖括号不平衡")
    for t in ANYTAG.findall(txt):
        if RGBC.fullmatch(t):
            issues.append("W2 裸 rgb")
        elif NAMETAG.fullmatch(t):
            if canon_tags and NAMETAG.fullmatch(t).group(1) not in canon_tags:
                issues.append("E3 未知标签 " + t)
        else:
            issues.append("E3 标签格式错 " + t)
    for m in PLACEHOLDER.finditer(rest):
        if not re.fullmatch(r"\{\d*(:[^{}]{0,6})?\}", m.group(0)):
            issues.append("E4 非法占位符 " + m.group(0))
    leftover = PLACEHOLDER.sub("", rest)
    if "{" in leftover or "}" in leftover:
        issues.append("E4 非法占位符 孤立括号")
    if has_mk and not (ANYTAG.search(txt) or "{{" in txt):
        issues.append("W1 markup 空声明")
    return issues


def tag_vocab(idx) -> set:
    out = set()
    for txt, *_ in idx.values():
        out.update(NAMETAG.findall(txt))
    return out


def compare(canon, target):
    """逐键比对，返回 [(类别, 文件, stat_ids, 语言, 变体, 基线, 对方)]"""
    rows = []
    for k in sorted(set(canon) & set(target), key=lambda x: (x[0], x[1], x[3])):
        a = canon[k][0]
        b = target[k][0]
        if a == b:
            continue
        ta = ANYTAG.findall(a)
        tb = ANYTAG.findall(b)
        if ta and not tb:
            kind = "命名标签被整段删除"
        elif ta and tb and ta != tb:
            kind = "标签被改写"
        elif not ta and tb:
            kind = "无标记处新增颜色"
        else:
            kind = "标记被改写"
        rows.append((kind, k[0], k[1], k[2], k[3], a, b))
    return rows


def main():
    src = sys.argv[1] if len(sys.argv) > 1 else "raw_sd"
    canon_dir = None
    if "--canon" in sys.argv:
        canon_dir = sys.argv[sys.argv.index("--canon") + 1]
    out_md = sys.argv[-1] if sys.argv[-1].endswith(".md") else "/dev/null"

    target, dups_t = index(src)
    canon, _ = index(canon_dir) if canon_dir else ({}, [])
    canon_tags = tag_vocab(canon if canon else target)

    print(f"目录 {src}：{len(target)} 个 (块,语言,变体) 条目，"
          f"规范标签 {len(canon_tags)} 个，定位键冲突 {len(dups_t)} 处")
    for f, ids, lang in dups_t[:5]:
        print(f"   D1 {f} {ids[:36]} [{lang or '默认'}]")

    counts = collections.Counter()
    samples = collections.defaultdict(list)
    for k, (txt, cond, hnd, has_mk) in target.items():
        for it in lint(txt, has_mk, canon_tags):
            key = it.split()[0]
            counts[key] += 1
            if len(samples[key]) < 5:
                samples[key].append((k, it, txt))

    print("\n语法体检：")
    for e in ("E1", "E2", "E3", "E4", "W1", "W2"):
        print(f"   {e}  {counts.get(e, 0):6}")
    for e in ("E1", "E2", "E3", "E4", "W1"):
        for k, it, txt in samples.get(e, []):
            print(f"      [{it}] {k[0]} {k[1][:32]} [{k[2] or '默认'}] {txt[:64]}")

    if canon:
        rows = compare(canon, target)
        kinds = collections.Counter(r[0] for r in rows)
        print("\n与基线比对：")
        for k, v in kinds.most_common():
            print(f"   {k:16} {v:6} 条（{v // max(1, len(set(r[3] for r in rows)))} 处逻辑文本）")
        per_lang = collections.Counter(r[3] for r in rows)
        if per_lang:
            spread = max(per_lang.values()) - min(per_lang.values())
            print(f"   语言分布（{len(per_lang)} 种，最多与最少差 {spread} 条）："
                  + "，".join(f"{k or '默认'} {v}" for k, v in per_lang.most_common()))
            if spread <= 1:
                print("   → 各语言同步变化，说明是整份文件被程序批量重写，不是人工只改繁中。")
        if out_md != "/dev/null":
            with open(out_md, "w", encoding="utf-8") as f:
                f.write("# statdescriptions 体检\n\n")
                f.write(f"目标：`{src}`" + (f"　基线：`{canon_dir}`" if canon_dir else "") + "\n\n")
                f.write("## 语法\n\n| 项 | 条数 |\n|---|---:|\n")
                for e in ("E1", "E2", "E3", "E4", "W1", "W2"):
                    f.write(f"| {e} | {counts.get(e, 0)} |\n")
                if canon:
                    f.write("\n## 与基线的标记差异\n\n| 类别 | 条数 |\n|---|---:|\n")
                    for k, v in kinds.most_common():
                        f.write(f"| {k} | {v} |\n")
                    f.write("\n## 明细\n\n| 类别 | 文件 | stat_ids | 语言 | 变体 | 基线 | 对方 |\n")
                    f.write("|---|---|---|---|---:|---|---|\n")
                    for kind, f2, ids, lang, vi, a, b in rows:
                        f.write(f"| {kind} | {f2} | {ids[:48]} | {lang or '默认'} | {vi} "
                                f"| `{a[:70]}` | `{b[:70]}` |\n")
            print(f"\n→ {out_md}")


if __name__ == "__main__":
    main()
