# -*- coding: utf-8 -*-
"""把修正稿（UI 表侧）应用成可写回 ggpk 的 .dat 文件。

输入：out/fix-category-ui.csv（table,row,slot,en,zh_old,zh_new）
      基线：pairs/zh/<table>（从 ggpk 原文导出，未改过）
输出：out/patch/<table> + out/patch/manifest.json

每改一个文件都跑 datwrite 的三重自检；任一文件失败即整体失败，不产出半套补丁。
"""
import csv
import hashlib
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from datwrite import WriteError, load_raw, patch_bytes  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, "out", "fix-category-ui.csv")
BASE = os.path.join(ROOT, "pairs", "zh")
DST = os.path.join(ROOT, "out", "patch")
LANG = "traditional chinese"


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def norm(s):
    """CSV 里换行被规整成 \\n，.dat 里实际是 \\r\\n；比较时统一再还原。"""
    return s.replace("\r\n", "\n")


def adapt(actual, new):
    """按实际串的换行风格写回：原文用 \\r\\n 就把新串的 \\n 也换成 \\r\\n。"""
    if "\r\n" in actual:
        return new.replace("\r\n", "\n").replace("\n", "\r\n")
    return new


def main():
    rows = list(csv.DictReader(open(SRC, encoding="utf-8-sig")))
    if not rows:
        print("fix-category-ui.csv 为空"); return 1

    by_table = {}
    for r in rows:
        by_table.setdefault(r["table"], []).append(r)

    os.makedirs(DST, exist_ok=True)
    files = []
    failed = 0
    for table, items in sorted(by_table.items()):
        src = os.path.join(BASE, table)
        if not os.path.exists(src):
            print("  ! 缺基线文件 %s" % src); failed += 1; continue
        raw = open(src, "rb").read()
        t = load_raw(raw, table)
        edits = []
        ok = True
        for i in items:
            row, rel = int(i["row"]), int(i["slot"])
            actual = t.string_field(row, rel)
            if actual is None:
                print("  x %-24s 行 %d 槽位 %d 读不出字符串" % (table, row, rel)); ok = False; break
            if norm(actual) != norm(i["zh_old"]):
                print("  x %-24s 行 %d 槽位 %d 现值与清单不符\n      实际 %r\n      清单 %r"
                      % (table, row, rel, actual, i["zh_old"])); ok = False; break
            edits.append((row, rel, actual, adapt(actual, i["zh_new"])))
        if not ok:
            failed += 1; continue
        try:
            out, rep = patch_bytes(raw, edits, table)
        except WriteError as e:
            print("  x %-26s %s" % (table, e)); failed += 1; continue
        dst = os.path.join(DST, table)
        open(dst, "wb").write(out)
        changed = [e for e in rep["edits"] if e["action"] == "append"]
        print("  OK %-26s %d 处改动，%d → %d 字节（+%d）"
              % (table, len(changed), rep["old_len"], rep["new_len"], rep["appended_bytes"]))
        files.append({
            "table": table,
            "path_in_ggpk": "data/%s/%s" % (LANG, table),
            "src": os.path.relpath(src, ROOT).replace("\\", "/"),
            "dst": os.path.relpath(dst, ROOT).replace("\\", "/"),
            "sha256_original": sha256(src),
            "sha256_patched": sha256(dst),
            "size_original": rep["old_len"],
            "size_patched": rep["new_len"],
            "verify": rep["verify"],
            "edits": rep["edits"],
        })

    if failed:
        print("失败 %d 个文件，未生成 manifest" % failed)
        return 1

    version = open(os.path.join(ROOT, "VERSION"), encoding="utf-8").read().strip()
    manifest = {
        "version": version,
        "lang": LANG,
        "source": os.path.relpath(SRC, ROOT).replace("\\", "/"),
        "total_edits": sum(len(f["edits"]) for f in files),
        "files": files,
    }
    mp = os.path.join(DST, "manifest.json")
    json.dump(manifest, open(mp, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
    print("\nmanifest: %s（%d 个文件 / %d 处改动）"
          % (os.path.relpath(mp, ROOT), len(files), manifest["total_edits"]))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
