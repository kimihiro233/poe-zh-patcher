#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""把三套 flavor 的补丁数据压成一个可内嵌的 bundle.gz，供拖放式 exe 使用。

不内嵌完整文件（三套 flavor 合起来 120 MB），只内嵌「行级结构」：
对每个 statdescriptions 文件，记录被任一 flavor 触碰过的行
    [行号, 原文, translated文本, dual文本, 英文原文]
其余行（结构行、其它语言行）运行时从 ggpk 里的当前内容原地保留。
datc64 是二进制，直接内嵌原始 + 补丁两份字节（base64）。

输出：tools/pack/bundle.gz（JSON + gzip）
"""
import base64
import glob
import gzip
import hashlib
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
os.chdir(ROOT)

SD_DIR = "raw_sd"
PREFIX = "metadata_statdescriptions_"
DUAL_INDENT = "  "
NL = "\\n"            # 字面反斜杠+n（PoE 文本里的换行转义），不是 0x0A

OUT = "tools/pack/bundle.gz"

# 被本工具改过、但已经过时的文本（上一版补丁写进游戏里的是它）。
# 玩家升级补丁器后，容器里留着上一版的字节 —— sha 对不上新包，但内容是我们自己写
# 的，必须认出来、允许覆盖。每次重建 bundle 时自动比对上一版，把被替换掉的文本记进来。
LEGACY = "tools/pack/legacy-rows.json"

# 译文版本号直接取 VERSION，避免和发布快照、补丁清单对不上
VERSION = open(os.path.join(ROOT, "VERSION"), encoding="utf-8").read().strip()


def load(path):
    return json.load(open(path, encoding="utf-8"))


def text_span(raw):
    """找行内引号文本段的 [start, end)，与 patch_sd.py 完全一致。"""
    i = raw.find(' "')
    if i < 0:
        return -1, -1
    j = i + 2
    k = j
    n = len(raw)
    while k < n:
        c = raw[k]
        if c == "\\":
            k += 2
            continue
        if c == '"':
            break
        k += 1
    return j, k


def read_lines(path):
    b = open(path, "rb").read()
    if b[:2] != b"\xff\xfe":
        raise SystemExit(f"{path} 不是 UTF-16LE BOM 文件")
    s = b.decode("utf-16")
    if s.count("\n") != s.count("\r\n"):
        raise SystemExit(f"{path} 含真实换行 0x0A——文本内换行必须写字面 \\n，否则解析器会错位")
    return s.split("\r\n")


def tc_line_texts(lines):
    """返回 {行号: 引号内文本}，只取形如 `key "text"` 的行。"""
    out = {}
    for no, raw in enumerate(lines):
        j, k = text_span(raw)
        if j > 0 and k > j:
            out[no] = raw[j:k]
    return out


def flavor_texts(o, t, d, e):
    """一行在四种 flavor 下的完整文本（None = 这种 flavor 下与上一档相同）。"""
    base = t if t else o
    out = {base}
    if d is not None:
        out.add(d)
    if e is not None:
        out.add(base + NL + DUAL_INDENT + e)
    return out


def collect_legacy(rel, rows, prev_rows, store):
    """把「上一版写过、这一版不再出现」的文本记到 store[rel][行号] 里。

    玩家容器里留着上一版补丁的字节时，sha 认不出，但逐行文本能认出 —— 靠的就是这份清单。
    """
    old = prev_rows.get(rel) or {}
    box = store.setdefault(rel, {})
    added = 0
    for no, o, t, d, e in rows:
        prev = old.get(no)
        if not prev:
            continue
        _no, po, pt, pd, pe = (list(prev) + [None] * 5)[:5]   # prev = [行号, 原文, 译文, 对照, 英文]
        now_ok = flavor_texts(o, t, d, e)
        keep = set(box.get(str(no)) or [])
        for cand in flavor_texts(po, pt, pd, pe):
            if cand and cand not in now_ok and cand not in keep:
                keep.add(cand)
                added += 1
        if keep:
            box[str(no)] = sorted(keep)
    return added


def main():
    man_all = load("out/patch-sd-dual-all/manifest-sd-dual-all.json")
    man_t = load("out/patch-sd/manifest-sd.json")
    man_d = load("out/patch-sd-dual/manifest-sd-dual.json")
    man_dat = load("out/patch/manifest.json")

    sha_t = {f["path_in_ggpk"]: f["sha256_patched"] for f in man_t["files"]}
    sha_d = {f["path_in_ggpk"]: f["sha256_patched"] for f in man_d["files"]}

    # 上一版内嵌数据（覆盖前先读）：用来采集「旧版补丁写进游戏的文本」
    prev_rows = {}
    if os.path.exists(OUT):
        try:
            pb = json.loads(gzip.decompress(open(OUT, "rb").read()).decode("utf-8"))
            for e in pb.get("statdesc", []):
                prev_rows[e["path"]] = {r[0]: r for r in e["rows"]}
            print("比对上一版 bundle：%s（%d 个文件）" % (pb.get("version", "?"), len(prev_rows)))
        except Exception as ex:                       # 旧包损坏/格式变过都不该挡住发布
            print("上一版 bundle 读不出来，跳过 legacy 采集：", ex)
    legacy = load(LEGACY) if os.path.exists(LEGACY) else {}
    legacy_added = 0

    entries = []
    total_rows = 0
    for f in man_all["files"]:
        rel = f["path_in_ggpk"]
        src = f["src"]
        dst = f["dst"]
        orig = read_lines(src)
        allx = read_lines(dst)

        # translated / dual 的目标只有在对应 manifest 里才存在
        tgt_t = None
        if rel in sha_t:
            tgt_t = read_lines(os.path.join("out/patch-sd", os.path.basename(dst)))
        tgt_d = None
        if rel in sha_d:
            tgt_d = read_lines(os.path.join("out/patch-sd-dual", os.path.basename(dst)))

        if len(orig) != len(allx):
            raise SystemExit(f"{rel}: 行数不一致 {len(orig)} vs {len(allx)}")
        if tgt_t is not None and len(tgt_t) != len(orig):
            raise SystemExit(f"{rel}: translated 行数不一致")
        if tgt_d is not None and len(tgt_d) != len(orig):
            raise SystemExit(f"{rel}: dual 行数不一致")

        o_txt = tc_line_texts(orig)
        x_txt = tc_line_texts(allx)
        t_txt = tc_line_texts(tgt_t) if tgt_t else {}
        d_txt = tc_line_texts(tgt_d) if tgt_d else {}

        rows = []
        for no in sorted(set(o_txt) | set(x_txt) | set(t_txt) | set(d_txt)):
            o = o_txt.get(no, "")
            x = x_txt.get(no, o)
            t = t_txt.get(no, o)
            d = d_txt.get(no, t)
            # 拆出英文原文：对照行是「一组中文 + 一组两空格缩进的英文」交替穿插，
            # 英文组一定以两格缩进开头（原文里 0 个段以两空格开头），按前缀就能分开。
            e = None
            base = t if t else o
            if x != base:
                zh_segs, en_segs = [], []
                for seg in x.split(NL):
                    if seg.startswith(DUAL_INDENT):
                        en_segs.append(seg[len(DUAL_INDENT):])
                    else:
                        zh_segs.append(seg)
                if NL.join(zh_segs) != base:
                    raise SystemExit(
                        f"{rel}:{no + 1} dualall 行拆不出基线：{x[:60]!r}")
                e = NL.join(en_segs)
                if not e.strip():
                    raise SystemExit(f"{rel}:{no + 1} 英文为空")
            else:
                # 没有英文，但 dual 可能与 translated 不同
                pass
            if t == o:
                t = None
            if d == (t if t else o):
                d = None
            if e is None and t is None and d is None:
                continue
            rows.append([no, o, t, d, e])
        total_rows += len(rows)
        legacy_added += collect_legacy(rel, rows, prev_rows, legacy)
        leg = {k: v for k, v in legacy.get(rel, {}).items() if v}

        entries.append({
            "path": rel,
            "sha": {
                "original": f["sha256_original"],
                "translated": sha_t.get(rel),
                "dual": sha_d.get(rel),
                "dualall": f["sha256_patched"],
            },
            "lines": len(orig),                       # 基线行数：诊断时判断客户端文件是否整体错位
            "bytes": os.path.getsize(src),
            "rows": rows,
            # 旧版补丁留下的文本：sha 对不上时靠它认「这是我们自己写的」
            "legacy": {k: leg[k] for k in sorted(leg, key=int)} or None,
        })

    dat = []
    for f in man_dat["files"]:
        dat.append({
            "path": f["path_in_ggpk"],
            "table": f["table"],
            "sha_original": f["sha256_original"],
            "sha_patched": f["sha256_patched"],
            "orig": base64.b64encode(open(f["src"], "rb").read()).decode(),
            "patch": base64.b64encode(open(f["dst"], "rb").read()).decode(),
            # 逐行改动：客户端那份 sha 认不出、但结构没变时，靠它把改动叠到别人的底稿上
            "edits": [[e["row"], e["rel"], e["old"], e["new"]] for e in f.get("edits", [])],
        })

    lock = load("game.lock.json")
    bundle = {
        "format": 1,
        "version": VERSION,
        "built_at": __import__("datetime").datetime.now().isoformat(timespec="seconds"),
        "game": {
            "client_version": lock["version"]["client_version"],
            "patch_version": lock["version"]["patch_version"],
            "fingerprint": lock["fingerprint"],
            "locked_at": lock["locked_at"],
        },
        "statdesc": entries,
        "dat": dat,
    }

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    raw = json.dumps(bundle, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
    with gzip.open(OUT, "wb", compresslevel=9) as fh:
        fh.write(raw)
    if legacy_added:
        os.makedirs(os.path.dirname(LEGACY), exist_ok=True)
        with open(LEGACY, "w", encoding="utf-8") as fh:
            json.dump(legacy, fh, ensure_ascii=False, indent=1, sort_keys=True)
    print(f"statdesc 文件 {len(entries)}  行 {total_rows}  dat 表 {len(dat)}")
    print(f"JSON {len(raw)/1e6:.2f} MB -> gz {os.path.getsize(OUT)/1e6:.2f} MB  {OUT}")
    print(f"sha256 {hashlib.sha256(open(OUT,'rb').read()).hexdigest()[:16]}")
    print(f"旧版补丁文本 {sum(len(v) for box in legacy.values() for v in box.values())} 条"
          f"（本次新增 {legacy_added}）-> {LEGACY}")


if __name__ == "__main__":
    main()
