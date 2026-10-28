"""
从成对的 EN / ZH .dat 文件里抽出「同一行同一列」的原文与译文。

不依赖 schema：字符串列靠行内 u64 偏移探测，判据是
  1) 所有 >= 8 的偏移都能解出合法串
  2) 非空值足够多，且大部分互不重复（排除被误判成偏移的整数列）
  3) 与已接受列不重叠
"""

from __future__ import annotations

import os
import sys
import csv
import json

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import dat64  # noqa: E402


def cjk_count(s: str) -> int:
    return sum(1 for c in s if "\u4e00" <= c <= "\u9fff")


def detect_slots(t: dat64.DatTable, sample: int = 600) -> list[int]:
    if t.row_count == 0:
        return []
    step = max(1, t.row_count // sample)
    idx = list(range(0, t.row_count, step))[:sample]
    cands = []
    for rel in range(0, t.row_size - 7, 4):
        # 指针高 4 字节必须恒为 0
        if any(t.raw[4 + i * t.row_size + rel + 4: 4 + i * t.row_size + rel + 8] != b"\x00\x00\x00\x00"
               for i in idx):
            continue
        vals = [t.u64(i, rel) for i in idx]
        nz = [v for v in vals if v >= 8]
        if not nz:
            continue
        texts = []
        bad = 0
        for v in nz:
            s = t.read_at(t.pool_base + v)
            if s is None:
                bad += 1
            else:
                texts.append(s)
        if bad or not texts:
            continue
        real = [s for s in texts if s]
        if len(real) < max(2, int(0.02 * len(idx))):
            continue
        distinct = len(set(real))
        if distinct / len(real) < 0.5:
            continue
        cands.append((rel, distinct / len(real), len(real)))
    # 去重叠：优先非空多、多样性高的列
    cands.sort(key=lambda x: (-x[2], -x[1]))
    accepted: list[int] = []
    for rel, _, _ in cands:
        if all(abs(rel - a) >= 8 for a in accepted):
            accepted.append(rel)
    return sorted(accepted)


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else "pairs"
    out_csv = sys.argv[2] if len(sys.argv) > 2 else "out/pairs.csv"
    os.makedirs(os.path.dirname(out_csv) or ".", exist_ok=True)

    en_dir = os.path.join(root, "en")
    zh_dir = os.path.join(root, "zh")
    files = sorted(os.listdir(en_dir))
    stats = []
    mismatch = []
    with open(out_csv, "w", encoding="utf-8", newline="") as fo:
        w = csv.writer(fo)
        w.writerow(["table", "row", "slot", "row_id", "en", "zh", "flag"])
        for fn in files:
            ep = os.path.join(en_dir, fn)
            zp = os.path.join(zh_dir, fn)
            if not os.path.exists(zp):
                continue
            try:
                te = dat64.load(ep, fn)
                tz = dat64.load(zp, fn)
            except Exception as e:
                mismatch.append((fn, f"load: {e}"))
                continue
            if te.row_count != tz.row_count or te.row_size != tz.row_size:
                mismatch.append((fn, f"rows {te.row_count}/{tz.row_count} rs {te.row_size}/{tz.row_size}"))
                continue
            slots = sorted(set(detect_slots(te)) | set(detect_slots(tz)))
            if not slots:
                continue
            n_rows = n_pairs = n_trans = n_cjk = 0
            for i in range(te.row_count):
                row_id = ""
                for rel in slots:
                    s = te.string_field(i, rel)
                    if s and row_id == "" and rel == slots[0]:
                        row_id = s
                for rel in slots:
                    en = te.string_field(i, rel)
                    zh = tz.string_field(i, rel)
                    if en is None or zh is None:
                        continue
                    if not en and not zh:
                        continue
                    n_pairs += 1
                    if en != zh:
                        n_trans += 1
                    if cjk_count(zh):
                        n_cjk += 1
                    flag = "" if en == zh else "diff"
                    if cjk_count(zh) == 0 and en == zh:
                        flag = "same"
                    w.writerow([fn, i, rel, row_id, en, zh, flag])
                n_rows += 1
            stats.append((fn, n_rows, len(slots), n_pairs, n_trans, n_cjk))

    stats.sort(key=lambda x: -x[5])
    print(f"tables processed: {len(stats)}  mismatched: {len(mismatch)}")
    print(f"{'table':46} {'rows':>7} {'cols':>5} {'pairs':>8} {'trans':>8} {'cjk':>8}")
    for s in stats[:35]:
        print(f"{s[0]:46} {s[1]:7} {s[2]:5} {s[3]:8} {s[4]:8} {s[5]:8}")
    if mismatch:
        print("\nmismatched:")
        for m in mismatch[:20]:
            print("  ", m)
    json.dump([{"table": s[0], "rows": s[1], "cols": s[2], "pairs": s[3],
                "diff": s[4], "cjk": s[5]} for s in stats],
              open(os.path.join(os.path.dirname(out_csv) or ".", "table_stats.json"), "w", encoding="utf-8"),
              ensure_ascii=False, indent=1)


if __name__ == "__main__":
    main()
