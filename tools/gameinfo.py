# -*- coding: utf-8 -*-
"""锁定「这份译文对应哪个游戏版本」。

为什么需要：
  补丁是按某一版客户端的文本做的。游戏一更新，英文原文可能变、行号可能移、
  甚至整张表换格式，同一份 diff 套上去就是错的。光靠版本号不够（热修不改版本号），
  所以同时锁两层：
    1. 版本号        —— 给人看（3.29.3b / patch 3.29.3.3）
    2. 内容指纹      —— 给机器判（1916 个语言文件 + 45 个词缀 txt 的 sha256）

用法：
    python tools/gameinfo.py            生成 / 刷新 game.lock.json
    python tools/gameinfo.py --check    只比对，不写盘；有变化返回 1
    python tools/gameinfo.py --diff     比对并列出每个变动的文件

路径优先级：--ggpk 参数 > POE_DIR 环境变量 > 默认安装路径。
"""
import argparse
import hashlib
import json
import os
import re
import subprocess
import sys
from datetime import datetime

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LOCK = os.path.join(ROOT, "game.lock.json")
DUMPER = os.path.join(ROOT, "tools", "dumper", "bin", "Debug", "net8.0", "dumper.exe")
DEFAULT_DIR = r"C:\Program Files (x86)\Grinding Gear Games\Path of Exile"
LANG = "traditional chinese"

# 会被补丁直接改写的文件（其余只是分析输入）
TARGET_PREFIX = "data/%s/" % LANG


def find_game_dir(arg=None):
    for cand in [arg, os.environ.get("POE_DIR"), DEFAULT_DIR]:
        if cand and os.path.exists(os.path.join(cand, "Content.ggpk")):
            return os.path.abspath(cand)
    sys.exit("找不到 Content.ggpk；用 --ggpk 指定游戏目录，或设 POE_DIR 环境变量")


def parse_version(game_dir):
    """从 logs/LatestClient.txt 取最近一次启动的客户端版本与补丁版本。

    GGG 的 exe 没有版本资源，日志里这两行是唯一可靠的版本来源：
        [HTTP2] User agent: PoE release/tags/3.29.3b Windows x64
        Web root: https://patch.poecdn.com/3.29.3.3/
    """
    log = os.path.join(game_dir, "logs", "LatestClient.txt")
    if not os.path.exists(log):
        log = os.path.join(game_dir, "logs", "Client.txt")
    if not os.path.exists(log):
        return {}, "logs 缺失"

    client = patch = None
    with open(log, "r", encoding="utf-8", errors="replace") as f:
        for line in f:
            m = re.search(r"User agent: PoE release/tags/([\w.\-]+)", line)
            if m:
                client = m.group(1)
            m = re.search(r"Web root: https://patch\.poecdn\.com/([\w.\-]+)/", line)
            if m:
                patch = m.group(1)
    info = {"log": os.path.relpath(log, game_dir).replace("\\", "/"),
            "log_mtime": datetime.fromtimestamp(os.path.getmtime(log)).isoformat(timespec="seconds")}
    if client:
        info["client_version"] = client
    if patch:
        info["patch_version"] = patch
    return info, None


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def hash_ggpk(ggpk, filt):
    """调 dumper 批量算 ggpk 内文件的 sha256，返回 {path: {size, sha256}}。"""
    if not os.path.exists(DUMPER):
        sys.exit("找不到 %s，先构建：cd tools/dumper && dotnet build" % DUMPER)
    out = subprocess.run([DUMPER, "hash", ggpk, filt], capture_output=True)
    text = out.stdout.decode("utf-8", errors="replace")
    files = {}
    for line in text.splitlines():
        line = line.strip()
        if not line or line.startswith("hash matches"):
            continue
        if line.startswith("MISS"):
            continue
        parts = line.split("\t")
        if len(parts) != 3:
            continue
        path, size, sha = parts
        files[path] = {"size": int(size), "sha256": sha}
    if not files:
        print(text[:800])
        sys.exit("dumper hash 没有返回任何文件（filter=%s）" % filt)
    return files


def fingerprint(files):
    """对 (path, sha256) 排序后取聚合哈希；任一条目变化都会改变它。"""
    h = hashlib.sha256()
    for path in sorted(files):
        h.update(path.encode("utf-8"))
        h.update(b"\0")
        h.update(files[path]["sha256"].encode("ascii"))
        h.update(b"\n")
    return h.hexdigest()


def collect(game_dir):
    ggpk = os.path.join(game_dir, "Content.ggpk")
    ver, warn = parse_version(game_dir)
    if warn:
        print("  ! %s" % warn)

    inputs = {}
    inputs.update(hash_ggpk(ggpk, "data/%s/" % LANG))
    n_lang = len(inputs)
    inputs.update(hash_ggpk(ggpk, "metadata/statdescriptions/"))
    n_sd = len(inputs) - n_lang

    exe = {}
    for name in ("PathOfExile.exe", "PathOfExile_x64.exe", "Client.exe"):
        p = os.path.join(game_dir, name)
        if os.path.exists(p):
            st = os.stat(p)
            exe[name] = {"size": st.st_size, "sha256": sha256_file(p),
                         "mtime": datetime.fromtimestamp(st.st_mtime).isoformat(timespec="seconds")}

    targets = sorted(p for p in inputs if p.startswith(TARGET_PREFIX) and
                     os.path.splitext(p)[1] in (".datc64", ".dat"))
    st = os.stat(ggpk)
    return {
        "schema": 1,
        "locked_at": datetime.now().isoformat(timespec="seconds"),
        "game_dir": game_dir,
        "version": ver,
        "ggpk": {"size": st.st_size,
                 "mtime": datetime.fromtimestamp(st.st_mtime).isoformat(timespec="seconds")},
        "exe": exe,
        "counts": {"lang_files": n_lang, "statdesc_files": n_sd, "total": len(inputs)},
        "fingerprint": fingerprint(inputs),
        "files": inputs,
    }


def patched_shas():
    """本项目补丁写入后的 sha256 集合：用来把「我们改的」和「游戏改的」分开。"""
    mp = os.path.join(ROOT, "out", "patch", "manifest.json")
    if not os.path.exists(mp):
        return {}
    m = json.load(open(mp, encoding="utf-8"))
    return {f["path_in_ggpk"]: f["sha256_patched"] for f in m.get("files", [])}


def compare(old, new, patched=None):
    """返回 (状态, 明细, 本项补丁条数)。

    状态：same / version_changed / content_changed / new
    我们自己打进去的补丁也会让文件 sha 变，那不是「游戏更新」，
    比对时必须剔除，否则每次 check 都误报。
    """
    patched = patched or {}
    if not old:
        return "new", [], 0
    rows, n_patch = [], 0
    of, nf = old.get("files", {}), new["files"]
    for p in sorted(set(of) | set(nf)):
        a, b = of.get(p), nf.get(p)
        if a is None:
            rows.append(("+", p, "-", b["sha256"][:12]))
        elif b is None:
            rows.append(("-", p, a["sha256"][:12], "-"))
        elif a["sha256"] != b["sha256"]:
            if patched.get(p) == b["sha256"]:
                n_patch += 1
            else:
                rows.append(("~", p, a["sha256"][:12], b["sha256"][:12]))
    ov, nv = old.get("version", {}), new.get("version", {})
    vchange = ov.get("client_version") != nv.get("client_version") or \
              ov.get("patch_version") != nv.get("patch_version")
    if not rows and not vchange:
        return "same", [], n_patch
    if rows:
        return "content_changed", rows, n_patch
    return "version_changed", [], n_patch


def patched_tables(new_files):
    """当前 ggpk 里哪些文件已是「我们打的补丁」状态。

    基线锁必须在干净的游戏上取。如果 ggpk 还留着上一版补丁，锁进去的 sha256
    会混进我们自己的改动，之后就没法用它判断「游戏是否更新过」。
    """
    mp = os.path.join(ROOT, "out", "patch", "manifest.json")
    if not os.path.exists(mp):
        return []
    m = json.load(open(mp, encoding="utf-8"))
    hit = []
    for f in m.get("files", []):
        cur = new_files.get(f["path_in_ggpk"])
        if not cur:
            continue
        if cur["sha256"] == f["sha256_patched"] and f["sha256_patched"] != f["sha256_original"]:
            hit.append(f["table"])
    return hit


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--ggpk", help="游戏目录（含 Content.ggpk）")
    ap.add_argument("--check", action="store_true", help="只比对，不写盘")
    ap.add_argument("--diff", action="store_true", help="列出变动文件（最多 40 条）")
    ap.add_argument("--allow-patched", action="store_true", help="允许在已打补丁的 ggpk 上锁基线（不推荐）")
    a = ap.parse_args()

    game_dir = find_game_dir(a.ggpk)
    print("游戏目录：%s" % game_dir)
    new = collect(game_dir)
    v = new["version"]
    print("客户端版本：%s   补丁版本：%s   （来源 %s）"
          % (v.get("client_version", "?"), v.get("patch_version", "?"), v.get("log", "?")))
    old_peek = None
    if os.path.exists(LOCK):
        try:
            old_peek = json.load(open(LOCK, encoding="utf-8"))
        except Exception:
            old_peek = None
    print("语言文件 %d 个 + 词缀 txt %d 个" % (new["counts"]["lang_files"], new["counts"]["statdesc_files"]))
    print("当前指纹：%s" % new["fingerprint"][:16])
    if old_peek:
        print("锁定指纹：%s%s"
              % (old_peek.get("fingerprint", "")[:16],
                 "  （与当前一致）" if old_peek.get("fingerprint") == new["fingerprint"] else "  （与当前不同）"))
    print("Content.ggpk %s B" % format(new["ggpk"]["size"], ","))

    dirty = patched_tables(new["files"])
    new["ggpk_state"] = "patched" if dirty else "clean"
    if dirty:
        print("当前 ggpk 里已有本项目的补丁：%s" % ", ".join(dirty))
        if not a.allow_patched and not a.check:
            print("  拒绝锁基线：请在干净的游戏上取（先 patcher restore），")
            print("  否则指纹会混进我们自己的改动，失去「游戏是否更新过」的判断力。")
            print("  确实要强锁：--allow-patched")
            return 6

    old = None
    if os.path.exists(LOCK):
        old = json.load(open(LOCK, encoding="utf-8"))

    state, rows, n_patch = compare(old, new, patched_shas())
    label = {"new": "首次锁定", "same": "与上次一致", "version_changed": "版本号变了，内容未变",
             "content_changed": "游戏内容有变化"}[state]
    extra = "，另有 %d 个文件是本项目补丁（已剔除）" % n_patch if n_patch else ""
    print("对比上次：%s（%d 个游戏侧变动%s）" % (label, len(rows), extra))

    if rows and (a.diff or a.check):
        for op, p, x, y in rows[:40]:
            print("  %s %-64s %s -> %s" % (op, p, x, y))
        if len(rows) > 40:
            print("  ... 另有 %d 条" % (len(rows) - 40))

    if a.check:
        return 1 if state in ("version_changed", "content_changed") else 0

    if state != "same":
        if old:
            # 覆盖前留一份，便于回答「上一版是哪个游戏版本」
            hist = os.path.join(ROOT, "out", "game-lock-history")
            os.makedirs(hist, exist_ok=True)
            tag = (old.get("version", {}).get("client_version") or "unknown").replace("/", "_")
            dst = os.path.join(hist, "game.lock.%s.%s.json"
                               % (tag, old.get("locked_at", "0").replace(":", "")))
            json.dump(old, open(dst, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
            print("  旧锁已留档：%s" % os.path.relpath(dst, ROOT).replace("\\", "/"))
        json.dump(new, open(LOCK, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
        print("已写入 %s" % os.path.relpath(LOCK, ROOT).replace("\\", "/"))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
