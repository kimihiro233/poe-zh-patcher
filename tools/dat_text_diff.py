# -*- coding: utf-8 -*-
"""对比 ggpk 里的语言表与本地基线，找出「新增/删除的文本」。

只回答一个问题：这次游戏更新往数据表里加了哪些要翻译的字。
用在术语修正之外——statdesc 那 45 份文件走 patch_sd 的校验，这里管的是
clientstrings / mods / monstervarieties 这类 .dat 里的字符串。

为什么不用「逐行逐槽位比对」：池（字符串区）一旦重组，行内指针整体位移，
dat64.string_field 会在池中间读到 `'힚('` 这种 2 字符垃圾，几百行假差异。
改成对文本池做集合差集，结果才干净。

用法：
  python tools/dat_text_diff.py --ggpk "C:/.../Path of Exile" \
      --tables clientstrings,mods,monstervarieties [--base pairs] [--report out/text-diff.md]

先跑 `python tools/gameinfo.py --check --diff` 拿到变动表名，再把它喂给这里。
"""
from __future__ import annotations

import argparse
import collections
import os
import shutil
import subprocess
import sys
import tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, os.path.join(ROOT, "tools"))
import dat64  # noqa: E402

DUMPER = os.path.join(ROOT, "tools", "dumper", "bin", "Debug", "net8.0", "dumper.exe")
LANGS = ("en", "zh")


def pool_strings(t: dat64.DatTable) -> list[str]:
    """按 UTF-16LE 0x0000 切出池里的串。

    两个过滤缺一不可：datc64 的池里混着非字符串数据（引用列的伴生区），
    不加「合法起点」会切出 `'ð'`，不加长度下限会把占位残片当成文本。
    """
    raw, base = t.raw, t.pool_base
    out, pos, n = [], base + 8, len(t.raw)
    while pos + 1 < n:
        if pos != base + 8 and raw[pos - 2:pos] != b"\x00\x00":
            pos += 2
            continue
        end = pos
        while end + 1 < n and not (raw[end] == 0 and raw[end + 1] == 0):
            end += 2
        if end + 1 >= n:
            break
        if end > pos:
            try:
                s = raw[pos:end].decode("utf-16-le")
            except UnicodeDecodeError:
                s = None
            if s and len(s) >= 4 and not any(ord(c) < 32 and c not in "\t\n\r" for c in s):
                out.append(s)
        pos = end + 2
    return out


def textlike(s: str) -> bool:
    return sum(c.isalnum() or c in " #%{}[]()+-.,:;'/" for c in s) >= len(s) * 0.8


def export(ggpk: str, tables: list[str], dest: str) -> None:
    if not os.path.exists(DUMPER):
        raise SystemExit("缺 %s；先 cd tools/dumper && dotnet build" % os.path.relpath(DUMPER, ROOT))
    lst = os.path.join(dest, "list.csv")
    with open(lst, "w", encoding="utf-8", newline="\n") as f:
        f.write("file\n")
        for t in tables:
            # dumper pairs 按「表名」成对导出 en/zh，后缀靠它自己认
            f.write(t + ("" if t.endswith((".datc64", ".datcl64")) else ".datc64") + "\n")
    r = subprocess.run([DUMPER, "pairs", ggpk, dest, lst],
                       capture_output=True, text=True, encoding="utf-8", errors="replace")
    print((r.stdout or "").strip().splitlines()[-1] if r.stdout else "")
    if r.returncode != 0:
        raise SystemExit("导出失败：%s" % (r.stderr or "")[-300:])


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--ggpk", required=True, help="游戏目录或 Content.ggpk")
    ap.add_argument("--base", default="pairs", help="基线目录（含 en/ zh/），默认 pairs")
    ap.add_argument("--tables", required=True, help="逗号分隔的表名，如 clientstrings,mods")
    ap.add_argument("--report", help="把结论写成 md")
    ap.add_argument("--lang", default="traditional chinese", help="ggpk 内的语言目录名")
    a = ap.parse_args()

    ggpk = a.ggpk
    if os.path.isdir(ggpk):
        ggpk = os.path.join(ggpk, "Content.ggpk")
    tables = [t.strip() for t in a.tables.split(",") if t.strip()]
    base = a.base if os.path.isabs(a.base) else os.path.join(ROOT, a.base)

    tmp = tempfile.mkdtemp(prefix="datdiff-")
    lines: list[str] = []
    try:
        export(ggpk, tables, tmp)
        for t in tables:
            name = t if t.endswith((".datc64", ".datcl64")) else t + ".datc64"
            # datcl64 与 datc64 文本相同，只看一份，避免把同一处变化报两遍
            if name.endswith(".datcl64"):
                continue
            for lang in LANGS:
                old_p = os.path.join(base, lang, name)
                new_p = os.path.join(tmp, lang, name)
                if not (os.path.exists(old_p) and os.path.exists(new_p)):
                    lines.append(f"### {name} [{lang}]  跳过：缺基线或新导出")
                    continue
                a_t, b_t = dat64.load(old_p), dat64.load(new_p)
                ca = collections.Counter(pool_strings(a_t))
                cb = collections.Counter(pool_strings(b_t))
                added = [s for s in sorted(set((cb - ca).elements())) if textlike(s)]
                removed = [s for s in sorted(set((ca - cb).elements())) if textlike(s)]
                h = (f"### {name} [{lang}]  行 {a_t.row_count} → {b_t.row_count}"
                     f"  |  新增 {len(added)}  删除 {len(removed)}")
                lines.append(h)
                lines.append("")
                for s in added:
                    lines.append(f"- `+` {s}")
                for s in removed:
                    lines.append(f"- `-` {s}")
                if not added and not removed:
                    lines.append("（文本层无变化；表若有变动，只在数值/引用列）")
                lines.append("")
    finally:
        shutil.rmtree(tmp, ignore_errors=True)

    text = "\n".join(lines)
    print(text)
    if a.report:
        out = a.report if os.path.isabs(a.report) else os.path.join(ROOT, a.report)
        os.makedirs(os.path.dirname(out) or ".", exist_ok=True)
        with open(out, "w", encoding="utf-8", newline="\n") as f:
            f.write("# 数据表文本变化\n\n")
            f.write(f"对比基线 `{os.path.relpath(base, ROOT)}`\n\n")
            f.write(text + "\n")
        print("\n报告：%s" % os.path.relpath(out, ROOT))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
