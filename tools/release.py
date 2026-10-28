# -*- coding: utf-8 -*-
"""打版本快照：把「文档 + 修正稿 + 术语表」打包进 releases/<version>/ 并生成 MANIFEST.sha256。

只收文本产物。二进制原始导出（pairs/raw/raw_sd）不入档——它们可由 dumper 从 ggpk 重新导出，
且换版本后必须重导，存档只会留下过期数据。

用法：python tools/release.py [--note "说明"]
"""
import csv
import hashlib
import os
import shutil
import sys
import zipfile
from datetime import datetime, timezone, timedelta

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VERSION = open(os.path.join(ROOT, "VERSION"), encoding="utf-8").read().strip()
TZ = timezone(timedelta(hours=8))

DOCS = ["README.md", "REPORT.md", "DATA-FINDINGS.md", "RESEARCH-FORUM.md"]
FIXES = [
    ("fix-direction.csv", "方向错误（正/负变体用了相反措辞）"),
    ("fix-mult.csv", "乘法口径（more/less 误译 增加/減少）"),
    ("fix-additive-rev.csv", "反向口径（increased 误译 更多/更少）"),
    ("fix-wording.csv", "逐条润色与语义补全"),
    ("fix-gem.csv", "技能宝石限定词（技能寶石→主動技能寶石）"),
    ("fix-order.csv", "数值位置/语序统一"),
    ("fix-category.csv", "物品类别词（Jewellery/Item/Armour）"),
    ("fix-category-ui.csv", "物品类别词·UI 表侧（.dat 回写）"),
    ("fix-category-hold.csv", "存疑未改（待拍板）"),
]


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def count(path):
    with open(path, encoding="utf-8-sig") as f:
        return sum(1 for _ in csv.DictReader(f))


def main():
    note = ""
    if "--note" in sys.argv:
        note = sys.argv[sys.argv.index("--note") + 1]

    now = datetime.now(TZ)
    rel_dir = os.path.join(ROOT, "releases", "v" + VERSION)
    os.makedirs(rel_dir, exist_ok=True)

    copied = []
    for name in DOCS:
        src = os.path.join(ROOT, name)
        if os.path.exists(src):
            shutil.copy2(src, os.path.join(rel_dir, name))
            copied.append(name)
    for name, _ in FIXES:
        src = os.path.join(ROOT, "out", name)
        if os.path.exists(src):
            shutil.copy2(src, os.path.join(rel_dir, name))
            copied.append("out/" + name)
    shutil.copy2(os.path.join(ROOT, "out", "glossary.csv"), os.path.join(rel_dir, "glossary.csv"))
    copied.append("out/glossary.csv")

    # RELEASE.md
    lines = [
        "# v%s" % VERSION,
        "",
        "- 打包时间：%s" % now.strftime("%Y-%m-%d %H:%M:%S %z"),
        "- 游戏基线：Content.ggpk 74,730,084,326 B（2026-09-06 22:24）",
        "- 状态：**分析基线，尚未写入任何游戏文件**",
        "",
    ]
    if note:
        lines += ["> " + note, ""]
    lines += ["| 修正稿 | 条数 | 内容 |", "|---|---|---|"]
    for name, desc in FIXES:
        p = os.path.join(ROOT, "out", name)
        if os.path.exists(p):
            lines.append("| `%s` | %d | %s |" % (name, count(p), desc))
    lines += [
        "",
        "## 术语表",
        "",
        "`glossary.csv` 共 %d 条。" % count(os.path.join(ROOT, "out", "glossary.csv")),
        "",
        "## 打包内容",
        "",
        "文档 4 份 + 修正稿 %d 份 + 术语表 + MANIFEST.sha256。二进制原始导出不入档，可由 `tools/dumper` 重导。"
        % sum(1 for n, _ in FIXES if os.path.exists(os.path.join(ROOT, "out", n))),
        "",
    ]
    open(os.path.join(rel_dir, "RELEASE.md"), "w", encoding="utf-8").write("\n".join(lines))

    # MANIFEST
    files = sorted(f for f in os.listdir(rel_dir) if f != "MANIFEST.sha256")
    with open(os.path.join(rel_dir, "MANIFEST.sha256"), "w", encoding="utf-8") as f:
        for name in files:
            f.write("%s  %s\n" % (sha256(os.path.join(rel_dir, name)), name))

    # zip
    zpath = os.path.join(ROOT, "releases", "poe-zh-v%s.zip" % VERSION)
    with zipfile.ZipFile(zpath, "w", zipfile.ZIP_DEFLATED) as z:
        for name in files + ["MANIFEST.sha256"]:
            z.write(os.path.join(rel_dir, name), "v%s/%s" % (VERSION, name))

    print("归档目录：releases/v%s/（%d 个文件）" % (VERSION, len(files) + 1))
    for name in files:
        print("   %-32s %s" % (name, sha256(os.path.join(rel_dir, name))[:16]))
    print("压缩包：%s  %.1f KB" % (zpath, os.path.getsize(zpath) / 1024))


if __name__ == "__main__":
    main()
