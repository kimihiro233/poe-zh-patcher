"""
PoE .dat / .datc64 读取器（无 schema 依赖）。

格式（据 Content.ggpk 实测反推）：

    u32   rowCount
    row   [rowCount]            每行 rowSize 字节，从偏移 4 开始
    pool  字符串池               起点 = 4 + rowCount * rowSize，前 8 字节固定为 0xBB

行内字符串字段是 u64 相对偏移，实际文本位于 pool + 偏移。
每个串 = UTF-16LE 内容 + 0x0000 终止（终止后常有 2 字节空位，但池中存在
未被引用的空闲块，因此不靠顺序解析校验，只靠行内偏移按需读取）。

rowSize 的确定：扫描 4 + rowCount*rowSize 处 8 个连续 0xBB。实测每个文件
唯一命中。
"""

from __future__ import annotations

import struct


class DatError(Exception):
    pass


POOL_MARK = b"\xbb" * 8


class DatTable:
    def __init__(self, name: str, raw: bytes, row_count: int, row_size: int, pool_base: int):
        self.name = name
        self.raw = raw
        self.row_count = row_count
        self.row_size = row_size
        self.pool_base = pool_base
        self._cache: dict[int, str | None] = {}

    # ---------- 基础读取 ----------
    def u64(self, i: int, rel: int) -> int:
        return struct.unpack_from("<Q", self.raw, 4 + i * self.row_size + rel)[0]

    def u32(self, i: int, rel: int) -> int:
        return struct.unpack_from("<I", self.raw, 4 + i * self.row_size + rel)[0]

    # ---------- 字符串 ----------
    def is_start(self, pos: int) -> bool:
        """合法串起点：池首，或紧跟在 4 个 0 字节之后（上一条的终止符+界隔）。"""
        if pos == self.pool_base + 8:
            return True
        if pos < 4:
            return False
        return self.raw[pos - 2:pos] == b"\x00\x00"

    def read_at(self, pos: int) -> str | None:
        raw = self.raw
        n = len(raw)
        if pos < 0 or pos >= n - 1:
            return None
        if pos % 2 or not self.is_start(pos):
            return None
        end = pos
        while end + 1 < n and not (raw[end] == 0 and raw[end + 1] == 0):
            end += 2
        if end + 1 >= n:
            return None
        try:
            s = raw[pos:end].decode("utf-16-le")
        except UnicodeDecodeError:
            return None
        if any(ord(c) < 32 and c not in "\t\n\r" for c in s):
            return None
        return s

    def string_field(self, i: int, rel: int) -> str | None:
        """返回 None 表示该槽位不是字符串；返回 '' 表示空串。

        指针值与真实串起点偶尔有 ±2 偏差（同一串在池里存在旧副本），
        因此精确解析失败时向两侧各试一次。
        """
        v = self.u64(i, rel)
        if v < 8:
            return ""
        for dlt in (0, -2, 2):
            key = self.pool_base + v + dlt
            if key in self._cache:
                s = self._cache[key]
            else:
                s = self.read_at(key)
                self._cache[key] = s
            if s is not None:
                if dlt != 0 and s == "":
                    continue
                return s
        return None

    # ---------- 列探测 ----------
    def string_slots(self, sample: int = 400, threshold: float = 0.9) -> list[int]:
        """找出行内哪些 4 字节对齐槽位是 u64 字符串偏移。"""
        if self.row_count == 0:
            return []
        step = max(1, self.row_count // sample)
        idx = list(range(0, self.row_count, step))[:sample]
        out = []
        for rel in range(0, self.row_size - 7, 4):
            good = 0
            for i in idx:
                v = self.u64(i, rel)
                if v < 8:
                    good += 1
                elif self.read_at(self.pool_base + v) is not None:
                    good += 1
            if idx and good / len(idx) >= threshold:
                out.append(rel)
        return out


def find_row_size(raw: bytes, row_count: int, limit: int = 2048) -> int:
    n = len(raw)
    hits = []
    for rs in range(1, limit):
        base = 4 + row_count * rs
        if base + 8 > n:
            break
        if raw[base:base + 8] == POOL_MARK:
            hits.append((rs, base))
    if not hits:
        raise DatError(f"未找到字符串池标记（{n} 字节, {row_count} 行）")
    # 多个候选时取行宽最大的（行宽过小会把行数据误判为池起点）
    rs, _ = max(hits, key=lambda x: x[0])
    return rs


def load(path: str, name: str | None = None) -> DatTable:
    with open(path, "rb") as f:
        raw = f.read()
    if len(raw) < 12:
        raise DatError(f"{path}: 文件过短")
    row_count = struct.unpack_from("<I", raw, 0)[0]
    if row_count == 0:
        return DatTable(name or path, raw, 0, 0, 4)
    rs = find_row_size(raw, row_count)
    return DatTable(name or path, raw, row_count, rs, 4 + row_count * rs)
