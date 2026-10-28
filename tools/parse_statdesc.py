"""
解析 metadata/statdescriptions/*.txt，抽取英文与繁中的词缀/技能描述对照。

文件格式（UTF-16LE，CRLF，制表符缩进）：

    description
    \t<stat_id> <stat_id> ...
    \t<变体数量>
    \t\t<选择条件>|<符号条件> "<文本>" <handler...>
    lang "Traditional Chinese"
    \t<变体数量>
    \t\t<选择条件>|<符号条件> "<文本>" <handler...>

关于变体：一个 stat 可以有多个文本变体，游戏按该 stat 的取值挑一个显示。
典型的方向类 stat 有两个变体：

    \t\t1|#  "{0}% increased Physical Damage"          值 >= 1 用
    \t\t#|-1 "{0}% reduced Physical Damage" negate 1   值 <= -1 用，数字取反后显示

注意 `<选择条件>` 在不同语言、不同变体之间**可能重复**（例如两个变体都写 `#`），
所以它不能当作配对 key。变体顺序才是语言无关的稳定 key：第 N 个变体在
任何语言里都指同一个语义。因此本脚本按 (block, variant_index) 配对，
并单独记录变体数量不一致的块——那种情况本身就是数据缺陷。

输出列：
    file, stat_ids, variant, cond, handlers, en, zh
`variant` 是块内从 0 开始的变体序号，`cond` 是 `<选择条件>|<符号条件>` 原文，
`handlers` 是文本后面的处理指令（如 `negate 1`），它决定数字是否取反。
"""

from __future__ import annotations

import csv
import glob
import os
import sys


def split_text(raw: str):
    """把 `1|# "text" negate 1` 拆成 (条件, 文本, handler)。文本内的 \\" 不算结束。"""
    i = raw.find(' "')
    if i < 0:
        return raw, "", ""
    prefix = raw[:i]
    j = i + 2
    out = []
    n = len(raw)
    while j < n:
        c = raw[j]
        if c == "\\" and j + 1 < n:
            out.append(raw[j:j + 2])
            j += 2
            continue
        if c == '"':
            break
        out.append(c)
        j += 1
    return prefix, "".join(out), raw[j + 1:].strip()


def parse_file(path: str):
    """yield (file_name, stat_ids, blocks)；blocks 是 [(lang, [变体原始行...]), ...]。"""
    text = open(path, "rb").read().decode("utf-16", errors="replace")
    lines = text.splitlines()
    n = len(lines)
    i = 0
    file_name = os.path.basename(path)
    while i < n:
        if lines[i] == "description":
            i += 1
            stat_ids = ""
            declared = 0
            declared_variants = 0
            first = True
            while i < n and lines[i].startswith("\t") and not lines[i].startswith("\t\t"):
                s = lines[i].strip()
                if first:
                    # 首行是 "<stat_id 个数> <stat_id ...>"
                    parts = s.split(None, 1)
                    if parts and parts[0].isdigit():
                        declared = int(parts[0])
                        s = parts[1] if len(parts) > 1 else ""
                    stat_ids = s
                    first = False
                elif s.isdigit():
                    # 后续单缩进行是「变体数量」
                    declared_variants = int(s)
                i += 1
            blocks: list[tuple[str, list[str]]] = []
            cur_lang = ""
            while i < n and lines[i].startswith("\t"):
                line = lines[i]
                if line.startswith("\tlang "):
                    cur_lang = line.split('"')[1] if '"' in line else ""
                    i += 1
                    continue
                if line.startswith("\t\t"):
                    if not blocks or blocks[-1][0] != cur_lang:
                        blocks.append((cur_lang, []))
                    blocks[-1][1].append(line[2:])
                    i += 1
                    continue
                i += 1
            yield file_name, stat_ids, blocks, declared_variants
            continue
        i += 1


def variants(blocks, lang: str):
    """取某语言的变体列表 [(cond, text, handlers)]，保持文件顺序。"""
    out = []
    for lg, vs in blocks:
        if lg != lang:
            continue
        for raw in vs:
            cond, txt, hnd = split_text(raw)
            out.append((cond.strip(), txt, hnd))
    return out


def main():
    src = sys.argv[1] if len(sys.argv) > 1 else "raw_sd"
    out = sys.argv[2] if len(sys.argv) > 2 else "out/statdesc.csv"
    os.makedirs(os.path.dirname(out) or ".", exist_ok=True)
    files = sorted(glob.glob(os.path.join(src, "*.txt")))

    total = paired = cjk = 0
    no_zh = 0
    count_mismatch = 0
    offgrid: list[tuple] = []  # 变体数量不一致的块，另存复核
    loss: list[tuple] = []      # 声明变体数 != 实解变体数


    with open(out, "w", encoding="utf-8", newline="") as fo:
        w = csv.writer(fo)
        w.writerow(["file", "stat_ids", "variant", "cond", "handlers", "en", "zh"])
        for path in files:
            for fname, stat_ids, blocks, declared_variants in parse_file(path):
                en = variants(blocks, "")
                if not en:
                    continue
                if declared_variants and declared_variants != len(en):
                    loss.append((fname, stat_ids, declared_variants, len(en)))
                zh = variants(blocks, "Traditional Chinese")
                if not zh:
                    no_zh += 1  # 仍然计入总数，覆盖率才诚实
                if len(en) != len(zh):
                    count_mismatch += 1
                    offgrid.append((fname, stat_ids, len(en), len(zh),
                                    [t for _, t, _ in en][:3], [t for _, t, _ in zh][:3]))
                for idx, (cond, txt, hnd) in enumerate(en):
                    total += 1
                    z = zh[idx][1] if idx < len(zh) else ""
                    if z:
                        paired += 1
                        if any("\u4e00" <= c <= "\u9fff" for c in z):
                            cjk += 1
                    w.writerow([fname, stat_ids, idx, cond, hnd, txt, z])

    print(f"en variants {total}, paired {paired}, with cjk {cjk}")
    print(f"blocks without zh block {no_zh}, variant-count mismatch {count_mismatch}")
    print(f"blocks with declared!=parsed variants {len(loss)}")
    for row in loss[:8]:
        print("   loss:", row[0].replace("metadata_statdescriptions_",""), row[1][:40],
              "declared", row[2], "parsed", row[3])

    if offgrid:
        p = os.path.join(os.path.dirname(out) or ".", "statdesc-count-mismatch.csv")
        with open(p, "w", encoding="utf-8", newline="") as f:
            wr = csv.writer(f)
            wr.writerow(["file", "stat_ids", "en_count", "zh_count", "en_sample", "zh_sample"])
            for row in offgrid:
                wr.writerow([row[0], row[1], row[2], row[3],
                             " | ".join(row[4]), " | ".join(row[5])])
        print(f"  -> {p}")


if __name__ == "__main__":
    main()
