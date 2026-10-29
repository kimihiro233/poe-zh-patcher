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
import glob
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


def patch_index():
    """本项目所有补丁写出来的 sha256：path_in_ggpk -> {original, patched, label}。

    两条链路各有一份 manifest（`out/patch` 管 .dat，`out/patch-sd*` 管词缀 txt）。
    早先只读前一份，后果有两个，都不轻：
      1. `--check` 把装好的词缀补丁当成「游戏更新过」——实测一次误报 42 个文件；
      2. 锁基线那道闸拦不住脏客户端，补丁的 sha 会被静默锁进基线，指纹从此失去意义。
    """
    idx = {}
    paths = [os.path.join(ROOT, "out", "patch", "manifest.json")]
    paths += sorted(glob.glob(os.path.join(ROOT, "out", "patch-sd*", "manifest*.json")))
    for mp in paths:
        if not os.path.exists(mp):
            continue
        try:
            m = json.load(open(mp, encoding="utf-8"))
        except Exception:
            continue
        for f in m.get("files", []):
            p = f.get("path_in_ggpk")
            if not p:
                continue
            e = idx.setdefault(p, {"original": None, "patched": set(), "label": None})
            if e["original"] is None and f.get("sha256_original"):
                e["original"] = f["sha256_original"]
            for sha in [f.get("sha256_patched")] + list(f.get("sha256_variants") or []):
                if sha:
                    e["patched"].add(sha)
            if e["label"] is None:
                e["label"] = f.get("table") or os.path.basename(p)
    # 持久账本：manifest 只记「上一版产物」的形态，改两轮译文就会把更早的 sha 挤掉。
    # 客户端里装着的往往是某个中间构建，manifest 认不出来就当成游戏侧变动。
    # 账本由 patch_sd.py 维护，只增不减，且入库（out/ 会被清理）。
    lp = os.path.join(ROOT, "tools", "sd-sha-ledger.json")
    if os.path.exists(lp):
        try:
            ledger = json.load(open(lp, encoding="utf-8"))
        except Exception:
            ledger = {}
        for p, shas in ledger.items():
            e = idx.setdefault(p, {"original": None, "patched": set(), "label": None})
            for sha in shas or []:
                if sha:
                    e["patched"].add(sha)
    return idx


def normalize_files(files, idx, old_files=None):
    """把「本项目补丁写进去的 sha」换回原文 sha，得到能与基线对齐的指纹。

    只有这条记录确实指向基线时才替换（manifest 里的 original == 锁里的 sha）：
    否则「游戏换了版本、我们恰好也打过补丁」这种真变动会被悄悄吃掉。
    返回 (归一化后的 files, 被替换的文件数)。
    """
    out, n = {}, 0
    for p, v in files.items():
        e = idx.get(p)
        if e and v["sha256"] in e["patched"] and e["original"]:
            if old_files is None or (old_files.get(p) or {}).get("sha256") == e["original"]:
                out[p] = {"size": v["size"], "sha256": e["original"]}
                n += 1
                continue
        out[p] = v
    return out, n


def compare(old, new):
    """返回 (状态, 明细)。状态：same / version_changed / content_changed / new

    调用前已按 normalize_files() 把本项目补丁的 sha 归一化回原文，
    所以这里剩下的差异都是游戏侧的，不再需要「剔除补丁」的特判。
    """
    if not old:
        return "new", []
    rows = []
    of, nf = old.get("files", {}), new["files"]
    for p in sorted(set(of) | set(nf)):
        a, b = of.get(p), nf.get(p)
        if a is None:
            rows.append(("+", p, "-", b["sha256"][:12]))
        elif b is None:
            rows.append(("-", p, a["sha256"][:12], "-"))
        elif a["sha256"] != b["sha256"]:
            rows.append(("~", p, a["sha256"][:12], b["sha256"][:12]))
    ov, nv = old.get("version", {}), new.get("version", {})
    vchange = ov.get("client_version") != nv.get("client_version") or \
              ov.get("patch_version") != nv.get("patch_version")
    if not rows and not vchange:
        return "same", []
    if rows:
        return "content_changed", rows
    return "version_changed", []


def patched_files(files, idx):
    """当前 ggpk 里哪些文件已是「我们打的补丁」状态（.dat 与词缀 txt 都算）。

    基线锁必须在干净的游戏上取。如果 ggpk 还留着上一版补丁，锁进去的 sha256
    会混进我们自己的改动，之后就没法用它判断「游戏是否更新过」。
    """
    hit = []
    for p, e in idx.items():
        cur = files.get(p)
        if not cur or not e["original"] or not e["patched"]:
            continue
        if cur["sha256"] in e["patched"] and cur["sha256"] != e["original"]:
            hit.append(e["label"])
    return sorted(set(hit))


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

    old = None
    if os.path.exists(LOCK):
        try:
            old = json.load(open(LOCK, encoding="utf-8"))
        except Exception:
            old = None

    # 先归一化：本项目补丁写进去的字节换回原文 sha，指纹才和基线可比
    idx = patch_index()
    norm, n_norm = normalize_files(new["files"], idx, (old or {}).get("files"))
    new_cmp = dict(new, files=norm, fingerprint=fingerprint(norm))

    print("语言文件 %d 个 + 词缀 txt %d 个" % (new["counts"]["lang_files"], new["counts"]["statdesc_files"]))
    print("当前指纹：%s%s"
          % (new_cmp["fingerprint"][:16],
             "  （已剔除本项目补丁 %d 个文件）" % n_norm if n_norm else ""))
    if old:
        print("锁定指纹：%s%s"
              % (old.get("fingerprint", "")[:16],
                 "  （与当前一致）" if old.get("fingerprint") == new_cmp["fingerprint"] else "  （与当前不同）"))
    print("Content.ggpk %s B" % format(new["ggpk"]["size"], ","))

    dirty = patched_files(new["files"], idx)
    new_cmp["ggpk_state"] = "patched" if dirty else "clean"
    if dirty:
        head = "、".join(dirty[:6])
        more = "…等 %d 个" % len(dirty) if len(dirty) > 6 else ""
        print("当前 ggpk 里已有本项目的补丁：%s%s" % (head, more))
        if not a.allow_patched and not a.check:
            print("  拒绝锁基线：请在干净的游戏上取（先 patcher restore），")
            print("  否则指纹会混进我们自己的改动，失去「游戏是否更新过」的判断力。")
            print("  确实要强锁：--allow-patched")
            return 6

    state, rows = compare(old, new_cmp)
    label = {"new": "首次锁定", "same": "与上次一致", "version_changed": "版本号变了，内容未变",
             "content_changed": "游戏内容有变化"}[state]
    print("对比上次：%s（%d 个游戏侧变动）" % (label, len(rows)))

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
        json.dump(new_cmp, open(LOCK, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
        print("已写入 %s" % os.path.relpath(LOCK, ROOT).replace("\\", "/"))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
