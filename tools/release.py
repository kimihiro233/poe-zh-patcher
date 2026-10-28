# -*- coding: utf-8 -*-
"""打版本快照：把「文档 + 修正稿 + 术语表」打包进 releases/<version>/ 并生成 MANIFEST.sha256。

只收文本产物。二进制原始导出（pairs/raw/raw_sd）不入档——它们可由 dumper 从 ggpk 重新导出，
且换版本后必须重导，存档只会留下过期数据。

用法：python tools/release.py [--note "说明"]
"""
import csv
import hashlib
import json
import os
import shutil
import sys
import zipfile
from datetime import datetime, timezone, timedelta

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
VERSION = open(os.path.join(ROOT, "VERSION"), encoding="utf-8").read().strip()
LOCK = os.path.join(ROOT, "game.lock.json")
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
    # 补丁清单：记录本版本往游戏里写了哪些字节
    mf = os.path.join(ROOT, "out", "patch", "manifest.json")
    if os.path.exists(mf):
        shutil.copy2(mf, os.path.join(rel_dir, "patch-manifest.json"))
        copied.append("out/patch/manifest.json")
    # 修正稿的当前状态（pending/applied/drift/gone）
    st = os.path.join(ROOT, "out", "fix-status.csv")
    if os.path.exists(st):
        shutil.copy2(st, os.path.join(rel_dir, "fix-status.csv"))
        copied.append("out/fix-status.csv")
    # 游戏版本锁：这一版译文对应哪个客户端
    lock = None
    if os.path.exists(LOCK):
        lock = json.load(open(LOCK, encoding="utf-8"))
        shutil.copy2(LOCK, os.path.join(rel_dir, "game.lock.json"))
        copied.append("game.lock.json")

    gv = (lock or {}).get("version", {})
    gclient = gv.get("client_version") or "unknown"
    gpatch = gv.get("patch_version") or "?"
    stamp = "v%s+game%s" % (VERSION, gclient)

    # RELEASE.md
    mpath = os.path.join(ROOT, "out", "patch", "manifest.json")
    patched = os.path.exists(mpath)
    lines = [
        "# v%s" % VERSION,
        "",
        "- 打包时间：%s" % now.strftime("%Y-%m-%d %H:%M:%S %z"),
        "- **适用游戏版本：%s**（patch %s）" % (gclient, gpatch),
        "- 状态：**%s**" % ("已向 ggpk 写入 .dat 补丁" if patched else "分析基线，尚未写入任何游戏文件"),
        "",
        "## 对应的游戏版本",
        "",
    ]
    if lock:
        lines += [
            "| 项 | 值 |",
            "|---|---|",
            "| 客户端版本 | `%s` |" % gclient,
            "| 补丁版本 | `%s` |" % gpatch,
            "| 版本来源 | `%s` |" % gv.get("log", "?"),
            "| 锁定时 ggpk 状态 | %s |" % ("已含本项目补丁" if lock.get("ggpk_state") == "patched" else "干净（原始）"),
            "| Content.ggpk | %s B，mtime %s |" % (format(lock["ggpk"]["size"], ","), lock["ggpk"]["mtime"]),
            "| 锁定文件数 | %d（语言 %d + 词缀 txt %d） |"
            % (lock["counts"]["total"], lock["counts"]["lang_files"], lock["counts"]["statdesc_files"]),
            "| 基线指纹 | `%s` |" % lock["fingerprint"],
            "",
            "指纹 = 上述每个文件 `path + sha256` 排序后聚合；只要有一处文本变化它就会变。",
            "游戏更新后先跑 `python tools/gameinfo.py --diff` —— 它会指出哪些文件变了。",
            "",
            "**本版译文只对 %s 负责。**换版本必须重跑：`gameinfo` → 重导 → 重分析 → `patch_dat`。" % gclient,
            "",
        ]
    else:
        lines += ["> 未找到 `game.lock.json`：这一版没有绑定游戏版本，不要直接套到客户端上。", ""]
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
        "文档 4 份 + 修正稿 %d 份 + 状态表 + 术语表 + game.lock.json + MANIFEST.sha256。"
        % sum(1 for n, _ in FIXES if os.path.exists(os.path.join(ROOT, "out", n)))
        + "二进制原始导出不入档（可由 `tools/dumper` 重导，且换版本后必须重导）。",
        "",
    ]
    if patched:
        m = json.load(open(mpath, encoding="utf-8"))
        lines += ["## 已写入游戏（.dat 侧）", "",
                  "| 表 | 改动 | 原始 → 补丁字节 | sha256(补丁) |", "|---|---|---|---|"]
        for f in m["files"]:
            lines.append("| `%s` | %d | %d → %d | `%s` |"
                         % (f["table"], len(f["edits"]), f["size_original"],
                            f["size_patched"], f["sha256_patched"][:16]))
        lines += ["",
                  "写入方式：`patcher apply`（LibBundledGGPK3），每个文件写入后从 ggpk 重读比对 sha256；",
                  "回滚：`patcher restore`。逐条改动见 `out/patch/manifest.json`。", ""]

    open(os.path.join(rel_dir, "RELEASE.md"), "w", encoding="utf-8").write("\n".join(lines))

    # MANIFEST
    files = sorted(f for f in os.listdir(rel_dir) if f != "MANIFEST.sha256")
    with open(os.path.join(rel_dir, "MANIFEST.sha256"), "w", encoding="utf-8") as f:
        for name in files:
            f.write("%s  %s\n" % (sha256(os.path.join(rel_dir, name)), name))

    # zip
    zpath = os.path.join(ROOT, "releases", "poe-zh-%s.zip" % stamp)
    with zipfile.ZipFile(zpath, "w", zipfile.ZIP_DEFLATED) as z:
        for name in files + ["MANIFEST.sha256"]:
            z.write(os.path.join(rel_dir, name), "%s/%s" % (stamp, name))

    print("归档目录：releases/v%s/（%d 个文件）  游戏版本：%s" % (VERSION, len(files) + 1, gclient))
    for name in files:
        print("   %-32s %s" % (name, sha256(os.path.join(rel_dir, name))[:16]))
    print("压缩包：%s  %.1f KB" % (zpath, os.path.getsize(zpath) / 1024))


if __name__ == "__main__":
    main()
