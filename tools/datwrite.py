# -*- coding: utf-8 -*-
""".datc64 / .datl64 字符串重写器（无 schema 依赖）。

原理：不重建文件、不重排字符串池，只做两件事
  1) 把新串追加到池尾（旧串原样保留，避免影响其他行对同一串的引用）；
  2) 把目标槽位的 u64 偏移改成新串在池中的位置。

因此行区与旧池逐字节不变，只有被改的那 8 字节指针不同——这是改动面最小的写法，
出问题时也最容易判定是「我们改错了」还是「游戏不接受追加池」。

用法（库）：
    from datwrite import load_raw, patch_bytes
    out, report = patch_bytes(raw, [(row, rel, old, new)], name="clientstrings")

用法（命令行自检）：
    python tools/datwrite.py pairs/zh/incursionrooms.datc64
"""
from __future__ import annotations

import struct
import sys

from dat64 import DatTable, find_row_size

MARK = b"\xbb" * 8


class WriteError(Exception):
    pass


def load_raw(raw: bytes, name: str = "") -> DatTable:
    if len(raw) < 12:
        raise WriteError("文件过短")
    row_count = struct.unpack_from("<I", raw, 0)[0]
    if row_count == 0:
        return DatTable(name, raw, 0, 0, 4)
    rs = find_row_size(raw, row_count)
    return DatTable(name, raw, row_count, rs, 4 + row_count * rs)


def resolve_slot(t: DatTable, row: int, rel: int, old: str | None = None) -> int:
    """定位槽位真实串起点。指针值本身可能有 ±2 偏差，故按 dat64.string_field 的同序试探。"""
    v = t.u64(row, rel)
    if v < 8:
        raise WriteError("槽位 %d/%d 偏移为 %d，不是有效字符串指针" % (row, rel, v))
    for dlt in (0, -2, 2):
        pos = t.pool_base + v + dlt
        s = t.read_at(pos)
        if s is None:
            continue
        if dlt != 0 and s == "":
            continue
        if old is not None and s != old:
            raise WriteError("槽位 %d/%d 现值为 %r，与期望的 %r 不符" % (row, rel, s, old))
        return pos
    raise WriteError("槽位 %d/%d 指向的串无法解析" % (row, rel))


def ref_count(t: DatTable, pos: int, step: int = 4) -> int:
    """有多少个 8/4 字节槽位指向同一个串起点（≥1 说明该串被共享）。"""
    target = pos - t.pool_base
    n = 0
    for i in range(t.row_count):
        base = 4 + i * t.row_size
        for rel in range(0, t.row_size - 7, step):
            if struct.unpack_from("<Q", t.raw, base + rel)[0] == target:
                n += 1
    return n


def patch_bytes(raw: bytes, edits, name: str = ""):
    """edits: 可迭代的 (row, rel, old, new)。返回 (新字节, 报告 dict)。"""
    t = load_raw(raw, name)
    buf = bytearray(raw)

    # 池尾对齐：保证新串起点前两字节是 0x00 0x00（read_at 的 is_start 判据）
    if len(buf) % 2:
        buf.append(0)
    if buf[-2:] != b"\x00\x00" and raw != b"":
        buf += b"\x00\x00"

    report = {"file": name, "row_count": t.row_count, "row_size": t.row_size,
              "pool_base": t.pool_base, "old_len": len(raw), "edits": []}

    for row, rel, old, new in edits:
        if not (0 <= row < t.row_count):
            raise WriteError("行号越界：%d（共 %d 行）" % (row, t.row_count))
        if rel + 8 > t.row_size:
            raise WriteError("槽位越界：%d（行宽 %d）" % (rel, t.row_size))

        pos = resolve_slot(t, row, rel, old)
        shared = ref_count(t, pos)

        if new == old:
            report["edits"].append({"row": row, "rel": rel, "old": old, "new": new,
                                    "action": "skip(同文)", "shared_refs": shared})
            continue

        # 共享串不能原地改，一律追加。
        # 追加前先补 2 个 0 字节：原文件末尾常有一个「空串哨兵」指向池尾（偏移 == 池长），
        # 若不补，哨兵会读出我们新写的串，等于凭空改了另一行的内容。
        start = len(buf)
        buf += b"\x00\x00"
        pos = len(buf)
        buf += new.encode("utf-16-le") + b"\x00\x00"
        while len(buf) % 2:
            buf.append(0)
        struct.pack_into("<Q", buf, 4 + row * t.row_size + rel, pos - t.pool_base)
        report["edits"].append({"row": row, "rel": rel, "old": old, "new": new,
                                "action": "append", "shared_refs": shared,
                                "new_pool_offset": pos - t.pool_base,
                                "grew": len(new) - len(old)})

    out = bytes(buf)
    report.update(verify(raw, out, edits, t))
    return out, report


def verify(raw: bytes, out: bytes, edits, t: DatTable) -> dict:
    """三重自检：结构不变 / 目标字段回读正确 / 其余字节一处未动。"""
    t2 = load_raw(out, "verify")
    if (t2.row_count, t2.row_size, t2.pool_base) != (t.row_count, t.row_size, t.pool_base):
        raise WriteError("结构改变：%r -> %r" % ((t.row_count, t.row_size, t.pool_base),
                                                (t2.row_count, t2.row_size, t2.pool_base)))

    expected = {}
    for row, rel, old, new in edits:
        expected[(row, rel)] = new
    bad = []
    for (row, rel), new in expected.items():
        got = t2.string_field(row, rel)
        if got != new:
            bad.append((row, rel, new, got))
    if bad:
        raise WriteError("回读不符 %d 处，首例 %r" % (len(bad), bad[0]))

    # 未被编辑的槽位必须一字未变（含「空串哨兵」这类指向池尾的隐式引用）
    slots = sorted(set(t.string_slots()) | {rel for (_, rel) in expected})
    drift, tolerated = [], []
    for i in range(t.row_count):
        for rel in slots:
            if (i, rel) in expected:
                continue
            a, b = t.string_field(i, rel), t2.string_field(i, rel)
            if a == b:
                continue
            # 已知无害：偏移恰好等于原池长度（池尾哨兵）。追加后该位置变成空串，
            # 语义从「越界」变「空串」，两者对渲染等价。仅此一种允许。
            if a is None and b == "" and t.u64(i, rel) == len(t.raw) - t.pool_base:
                tolerated.append((i, rel))
                continue
            drift.append((i, rel, a, b))
    if drift:
        raise WriteError("有 %d 个未编辑槽位的读数被牵连改变，首例 %r" % (len(drift), drift[0]))

    # 原文件范围内，除被改的 8 字节指针外必须逐字节相同
    allowed = set()
    for (row, rel) in expected:
        base = 4 + row * t.row_size + rel
        allowed.update(range(base, base + 8))
    changed = [i for i in range(len(raw)) if raw[i] != out[i]]
    illegal = [i for i in changed if i not in allowed]
    if illegal:
        raise WriteError("意外改动 %d 字节，首处偏移 %d" % (len(illegal), illegal[0]))

    return {"verify": "ok", "changed_bytes": len(changed),
            "old_len": len(raw), "new_len": len(out),
            "appended_bytes": len(out) - len(raw),
            "tolerated_eof_sentinel": tolerated}


if __name__ == "__main__":
    path = sys.argv[1] if len(sys.argv) > 1 else None
    if not path:
        print("usage: python tools/datwrite.py <file.datc64>")
        raise SystemExit(1)
    raw = open(path, "rb").read()
    t = load_raw(raw, path)
    slots = t.string_slots()
    print("%s：%d 行 × %d 字节，池起点 %d，文件 %d 字节，探测到 %d 个字符串槽位"
          % (path, t.row_count, t.row_size, t.pool_base, len(raw), len(slots)))
    n = 0
    for i in range(min(t.row_count, 5)):
        vals = [(rel, t.string_field(i, rel)) for rel in slots]
        vals = [(rel, v) for rel, v in vals if v]
        if vals:
            print("  行 %d: %s" % (i, "; ".join("%d=%r" % x for x in vals[:4])))
        n += 1
