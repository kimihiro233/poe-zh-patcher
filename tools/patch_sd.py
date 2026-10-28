"""把修正稿应用到 metadata/statdescriptions/*.txt，产出整文件替换补丁。

为什么需要「整文件替换」：statdescriptions 是文本文档（UTF-16LE + CRLF），
不是定长的 .datc64，没有槽位指针可改，只能整文件替换进 ggpk。

定位策略（从严到宽，任何一步对不上就记 miss，绝不猜）：
  1. 组内序列匹配：一条修正稿的「正/负」多行必须在同一 stat 块的 TC 段里
     连续命中（同一个 stat 可能有 4 个变体，正/负未必是第 0/1 个）
  2. 变体序号：修正稿显式给了变体号时优先用，但必须文本相符才采纳
  3. 条件消歧：同文本多命中时用 cond（如 `#|-1`）挑

批次按 BATCHES 顺序链式应用：后面的批次看到的是前面批次改过的文本，
所以 fix-category 这类「基于中间态再修」的稿子不会和 fix-mult 打架。

用法：
    python tools/patch_sd.py            # dry-run，出 out/patch-sd-report.csv
    python tools/patch_sd.py --write    # 写 out/patch-sd/*.txt + manifest

外壳（shell）模式：
    python tools/patch_sd.py --shell <已被改过的文件目录> [--write]

    正常模式以 raw_sd（官方原版）为底稿整文件重建，会把别人的颜色体系一起冲掉。
    shell 模式改以**目标机器上现有的文件**为底稿，只把修正稿的「文本差量」叠上去。
    标记层按三分法：对方留着标记（换色/换标签）就原样沿用；对方把标记连 {{ }} 一起
    删光（原版有、这里寸草不生）判为覆盖伤，按原版补回；双方一致就原样。
    加 --keep-markup 关掉补回，变成纯「只改文本」。判定见 tools/markup.py，
    文本层对不上就记 miss，绝不猜。
"""

from __future__ import annotations

import csv
import glob
import hashlib
import json
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import markup


SD_DIR = "raw_sd"
DEFAULT_LANG = "Traditional Chinese"     # 修正稿不给 lang 列时按繁中处理
PREFIX = "metadata_statdescriptions_"   # raw_sd 里的扁平文件名前缀，去掉后才是 ggpk 内文件名
# 译文版本号直接取 VERSION，避免和发布快照、内嵌数据包对不上
VERSION = open(os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "VERSION"),
               encoding="utf-8").read().strip()
OUT_DIR = "out/patch-sd"
MANIFEST = "out/patch-sd/manifest-sd.json"
DUAL_DIR = "out/patch-sd-dual"
DUAL_MANIFEST = "out/patch-sd-dual/manifest-sd-dual.json"
DUAL_ALL_DIR = "out/patch-sd-dual-all"
DUAL_ALL_MANIFEST = "out/patch-sd-dual-all/manifest-sd-dual-all.json"
REPORT = "out/patch-sd-report.csv"

# 对照版：在改过的行下方追加英文原文，缩进两格区分从属关系。
# 游戏没有「按键切换文本」的入口（Alt 面板由客户端拼装，只放词缀名/tier、
# 天赋内部名/涂油），所以对照只能落在文本里，靠两套补丁来回切。
DUAL_INDENT = "  "
# 文本内的换行必须写字面 \\n（反斜杠+n 两个字符）。用真实 0x0A 会让解析器把
# 后面的内容当成新行，繁中段被跳过、回退到默认英文段（表现为 "Allocates <中文名> 无法使用的值"）。
NL = "\\n"

BATCHES = [
    "fix-direction",
    "fix-mult",
    "fix-gem",
    "fix-order",
    "fix-wording",
    "fix-additive-rev",
    "fix-category",
    "fix-typo",          # 原件自带笔误（占位符打错）。放最后：它不该和译文措辞批次抢同一行
]

# 批次之间对同一行给出相反目标时的裁决：key=(batch, line) -> 理由。
# 这里只登记已经在别处单独论证过的条目，不做通用「后者覆盖」。
SUPPRESSED = {
    ("fix-category", 35):
        "与 fix-additive-rev#5 指向同一行；原文 increased 属加法，"
        "方向稿改为「區域內找到的傳奇物品增加 {0}%」为准，本条「更多」口径作废",
}


# ---------------------------------------------------------------- 文件解析

def split_text(raw: str):
    """`1|# "text" negate 1` -> (cond, text, handlers)，处理 \\" 转义。"""
    i = raw.find(' "')
    if i < 0:
        return raw.strip(), "", ""
    prefix = raw[:i]
    j = i + 2
    out = []
    n = len(raw)
    while j < n:
        c = raw[j]
        if c == "\\" and j + 1 < n:
            out.append(raw[j:j + 2])
            j += 2
            continue
        if c == '"':
            break
        out.append(c)
        j += 1
    return prefix.strip(), "".join(out), raw[j + 1:].strip()


def parse_blocks(path: str):
    text = open(path, "rb").read().decode("utf-16")
    lines = text.split("\r\n")
    n = len(lines)
    blocks = []
    i = 0
    while i < n:
        if lines[i] != "description":
            i += 1
            continue
        i += 1
        stat_ids = ""
        first = True
        while i < n and lines[i].startswith("\t") and not lines[i].startswith("\t\t"):
            s = lines[i].strip()
            if first:
                parts = s.split(None, 1)
                if parts and parts[0].isdigit():
                    s = parts[1] if len(parts) > 1 else ""
                stat_ids = s
                first = False
            i += 1
        langs = []
        cur = ""
        while i < n and lines[i].startswith("\t"):
            line = lines[i]
            if line.startswith("\tlang "):
                cur = line.split('"')[1] if '"' in line else ""
                i += 1
                continue
            if line.startswith("\t\t"):
                if not langs or langs[-1][0] != cur:
                    langs.append((cur, []))
                cond, txt, hnd = split_text(line[2:])
                langs[-1][1].append((i, cond, txt, hnd))
                i += 1
                continue
            i += 1
        blocks.append((stat_ids, langs))
    return lines, blocks


class SdFile:
    def __init__(self, path: str):
        self.path = path
        self.name = os.path.basename(path)
        self.lines, self.blocks = parse_blocks(path)
        self.index: dict[str, list] = {}
        for bi, (ids, _langs) in enumerate(self.blocks):
            for sid in ids.split():
                self.index.setdefault(sid, []).append(bi)
        self.orig: dict[int, str] = {}
        self.tc: dict[int, list] = {}      # block_idx -> [(no, cond)]
        for bi, (_ids, langs) in enumerate(self.blocks):
            for lang, vs in langs:
                for no, cond, txt, _h in vs:
                    self.orig[no] = txt
                if lang == "Traditional Chinese":
                    self.tc[bi] = [(no, cond) for no, cond, _t, _h in vs]
        # 按语言索引变体：lang -> [(行号, cond)]。空串是英文（默认段）。
        # 修正稿只写繁中时用 DEFAULT_LANG；个别条目显式指定语言（修官方笔误时）。
        self.by_lang: dict[int, dict[str, list]] = {}
        for bi, (_ids, langs) in enumerate(self.blocks):
            self.by_lang[bi] = {}
            for lang, vs in langs:
                self.by_lang[bi].setdefault(lang, []).extend(
                    (no, cond) for no, cond, _t, _h in vs)
        self.cur: dict[int, str] = {}
        self.edits: dict[int, tuple[str, str]] = {}
        # 行号 -> 同变体序号的英文原文，供「对照版」补丁使用
        self.line2en: dict[int, str] = {}
        for _ids, langs in self.blocks:
            en_vs = next((vs for lang, vs in langs if lang == ""), [])
            tc_vs = next((vs for lang, vs in langs if lang == "Traditional Chinese"), [])
            for i, (no, _c, _t, _h) in enumerate(tc_vs):
                if i >= len(en_vs):
                    continue
                # handler 带 hash 的（passive_hash / *_gem_hash 等），{0} 运行时会被替换成
                # 天赋、宝石这类「名字」，而客户端只有中文名。英文模板拼上去就变成
                # 「Allocates 減世之語」这种中英混排，所以这类行不加英文。
                if "hash" in (_h or ""):
                    continue
                self.line2en[no] = en_vs[i][2]

    def text_of(self, no: int) -> str:
        return self.cur[no] if no in self.cur else self.orig.get(no, "")

    def vers(self, bi: int, lang: str | None = None) -> list:
        """某个 stat 块在指定语言下的变体 [(行号, cond)]。lang 省略或空 = 繁中。"""
        key = lang or DEFAULT_LANG
        return (self.by_lang.get(bi) or {}).get(key, [])

    def accepted(self, no: int) -> set:
        """该行可接受的「旧文本」：原文与当前文本都算，便于链式批次与重复稿。"""
        s = {self.text_of(no)}
        if no in self.orig:
            s.add(self.orig[no])
        return s

    def accepted_core(self, no: int) -> set:
        """同上，但只比文本层——底稿可能被第三方改过标记。"""
        return {markup.core(t) for t in self.accepted(no)}


# ---------------------------------------------------------------- 修正稿读取

def load_groups():
    """把各修正稿统一成组：[{gid, batch, line, file, stat_ids, cond, parts:[{old,new}]}]"""
    groups = []
    gid = 0
    for batch in BATCHES:
        p = f"out/{batch}.csv"
        if not os.path.exists(p):
            continue
        for k, r in enumerate(csv.DictReader(open(p, encoding="utf-8-sig")), 1):
            if (batch, k) in SUPPRESSED:
                continue
            g = lambda n: (r.get(n) or "").strip()
            old = r.get("zh_old") or r.get("zh_before") or ""
            new = r.get("zh_new") or r.get("zh_after") or ""
            if not old or not new:
                continue
            o_lines, n_lines = old.split("\n"), new.split("\n")
            if len(o_lines) != len(n_lines):
                o_lines, n_lines = [old], [new]
            parts = []
            for o, nn in zip(o_lines, n_lines):
                strip_pfx = (lambda s: s[2:] if s[:1] in ("正", "负") and s[1:2] in (" ", "\u3000") else s)
                parts.append({"old": strip_pfx(o).strip(), "new": strip_pfx(nn).strip()})
            gid += 1
            groups.append({"gid": gid, "batch": batch, "line": k,
                           "file": g("file"), "stat_ids": g("stat_ids"),
                           "cond": g("cond"), "parts": parts,
                           "lang": g("lang") or DEFAULT_LANG})
    return groups


# ---------------------------------------------------------------- 定位

def id_match(pattern: str, sid: str) -> bool:
    if pattern == sid:
        return True
    if "X" in pattern:
        rx = "^" + re.escape(pattern).replace("X", "[A-Za-z0-9_]+") + "$"
        return re.match(rx, sid) is not None
    return False


def locate(files: dict[str, SdFile], g: dict, loose: bool = False):
    """返回 ([(sf, line_no), ...], None) 或 (None, 原因)。长度等于 parts。

    loose=True 时忽略标记层，只比文本层（底稿被第三方改过颜色/标签时用）。
    """
    pats = g["stat_ids"].split()
    cands = []
    seen = set()
    for sf in files.values():
        if g["file"] and sf.name != g["file"]:
            continue
        for sid in sf.index:
            if any(id_match(p, sid) for p in pats):
                for bi in sf.index[sid]:
                    key = (id(sf), bi)
                    if key not in seen:      # 同一块常被多个 stat id 索引，去重
                        seen.add(key)
                        cands.append((sf, bi))
    if not cands:
        return None, "找不到 stat 块"

    want = [p["old"] for p in g["parts"]]
    want_core = [markup.core(w) for w in want]
    hits = []
    lang = g.get("lang") or DEFAULT_LANG
    for sf, bi in cands:
        tcv = sf.vers(bi, lang)
        for start in range(len(tcv) - len(want) + 1):
            window = tcv[start:start + len(want)]
            ok = True
            for k, ((no, cond), w) in enumerate(zip(window, want)):
                if loose:
                    if want_core[k] not in sf.accepted_core(no):
                        ok = False
                        break
                elif w not in sf.accepted(no):
                    ok = False
                    break
                if g["cond"] and cond and cond != g["cond"]:
                    ok = False
                    break
            if ok:
                hits.append((sf, [no for no, _c in window]))

    if len(hits) == 1:
        return hits[0], None
    if not hits:
        return None, "原文不符（可能已改/已漂移）"
    return None, f"命中 {len(hits)} 处，无法唯一定位"


# ---------------------------------------------------------------- 主流程

def text_span(raw: str):
    """返回行内引号文本的 [start, end)，处理 \\" 转义。"""
    i = raw.find(' "')
    if i < 0:
        raise ValueError(f"该行没有文本段：{raw[:60]!r}")
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


def build_bytes(sf: SdFile, dual: bool = False, all_lines: bool = False):
    """按编辑表生成整文件字节。

    dual=True 时追加英文原文：默认只给改过的行加（精确定位改动）；
    all_lines=True 则给该文件所有繁中行都加（整包对照，用于通读核对）。
    """
    lines = list(sf.lines)
    real = 0
    touched = []
    for no, (old, new) in sf.edits.items():
        if old == new:          # 「正/负」组里常有一行本来就不用改，不算编辑
            continue
        real += 1
        raw = lines[no]
        j, k = text_span(raw)
        if raw[j:k] != old:
            raise SystemExit(f"{sf.name}:{no + 1} 回读不符：{raw[j:k]!r} != {old!r}")
        lines[no] = raw[:j] + new + raw[k:]
        touched.append(no)

    if dual:
        targets = sorted(sf.line2en) if all_lines else touched
        for no in targets:
            en = sf.line2en.get(no, "")
            if not en:
                continue
            raw = lines[no]
            j, k = text_span(raw)
            body = raw[j:k]
            if body.endswith(NL + DUAL_INDENT + en):
                continue
            lines[no] = raw[:j] + body + NL + DUAL_INDENT + en + raw[k:]

    return b"\xff\xfe" + "\r\n".join(lines).encode("utf-16-le"), real


def merge_line(sf: SdFile, no: int, old: str, new: str, restore_bare: bool = True):
    """把一条修正叠到「该行当前文本」上，只动文本层。

    标记层三分法（见 tools/markup.py）：
      keep     对方留着标记（换色/换标签）→ 原样沿用
      restore  对方整段删光 → 判为覆盖伤，还原原版标记
      same     双方一致 → 正常替换

    返回 (before, after, kind)。任何会悄悄改坏标记层的情况直接中止。
    """
    before = sf.text_of(no)
    try:
        after = markup.merge(before, old, new, restore_bare=restore_bare)
    except ValueError as e:
        raise SystemExit(f"{sf.name}:{no + 1} {e}")
    if markup.core(after) != markup.core(new):
        raise SystemExit(f"{sf.name}:{no + 1} 合并后文本层不等于修正稿目标：{after!r} vs {new!r}")

    mk_before, mk_old, mk_after = (markup.markup_seq(before),
                                   markup.markup_seq(old),
                                   markup.markup_seq(after))
    if not mk_before and mk_old:
        if not restore_bare:
            kind = "bare"          # 对方删光了，但 --keep-markup 要求不补
        else:
            kind = "restore"
            if mk_after != mk_old:
                raise SystemExit(f"{sf.name}:{no + 1} 还原标记失败：{mk_after} != {mk_old}")
    elif mk_after == mk_before:
        kind = "keep" if before != old else "same"
    elif mk_before == mk_old and mk_after == markup.markup_seq(new):
        # 底稿标记与原文逐段相同 = 没被任何人动过。这种行允许按修正稿一起修标记层，
        # 典型就是官方笔误：`<enchanted>{{…감소` 漏了 `}}`。底稿被别人换过样式的行不走这里。
        kind = "markup"
    else:
        raise SystemExit(f"{sf.name}:{no + 1} 合并动了标记层：{mk_before} -> {mk_after}")
    return before, after, kind


def main():
    write = "--write" in sys.argv
    dual = "--dual" in sys.argv
    all_lines = "--all" in sys.argv
    restore_bare = "--keep-markup" not in sys.argv
    shell_dir = None
    if "--shell" in sys.argv:
        i = sys.argv.index("--shell")
        if i + 1 >= len(sys.argv):
            raise SystemExit("--shell 后面要跟目录")
        shell_dir = os.path.abspath(sys.argv[i + 1])
        if not os.path.isdir(shell_dir):
            raise SystemExit(f"不是目录：{shell_dir}")
    label = os.path.basename(shell_dir.rstrip("\\/")) if shell_dir else ""
    if dual and all_lines:
        out_dir, manifest = DUAL_ALL_DIR, DUAL_ALL_MANIFEST
    elif dual:
        out_dir, manifest = DUAL_DIR, DUAL_MANIFEST
    elif shell_dir:
        out_dir = f"out/patch-sd-{label}"
        manifest = os.path.join(out_dir, f"manifest-sd-{label}.json")
    else:
        out_dir, manifest = OUT_DIR, MANIFEST
    files = {}
    shell_map = {}                    # 文件名 -> 外壳文件路径（用于登记 sha 与还原源）
    for path in sorted(glob.glob(os.path.join(SD_DIR, "*.txt"))):
        name = os.path.basename(path)
        use = path
        if shell_dir:
            cand = os.path.join(shell_dir, name[len(PREFIX):])
            if os.path.exists(cand):
                use = cand
                shell_map[name] = cand
        sf = SdFile(use)
        sf.name = name                # 外壳目录里没有前缀，统一按 raw_sd 的名字索引
        files[name] = sf

    groups = load_groups()
    report = []
    ok = done = 0
    kept = restored = bare = 0   # 沿用底稿标记 / 还原原版标记 / 删光了但没补回 的条数
    for g in groups:
        pos, why = locate(files, g)
        if pos is None:               # 底稿被改过标记：退回「只比文本层」再定位一次
            pos, why2 = locate(files, g, loose=True)
            if pos is not None:
                why = None
        tag = f"{g['batch']}#{g['line']}"
        if pos is None:
            report.append([tag, g["stat_ids"], g["parts"][0]["old"][:56], "✗ " + why])
            continue
        sf, nos = pos
        msgs = []
        skipped = False
        for no, part in zip(nos, g["parts"]):
            if no in sf.edits and markup.core(sf.text_of(no)) == markup.core(part["new"]):
                done += 1
                msgs.append("已达成")
                continue
            if no in sf.edits and markup.core(sf.text_of(no)) != markup.core(part["old"]):
                report.append([tag, g["stat_ids"], part["old"][:56],
                               f"✗ 该行已被前序批次改为「{sf.text_of(no)[:24]}」，与本条冲突"])
                skipped = True
                break
            before, after, kind = merge_line(sf, no, part["old"], part["new"], restore_bare)
            sf.cur[no] = after
            sf.edits[no] = (before, after)
            ok += 1
            if kind == "keep":
                kept += 1
                msgs.append(f"{sf.name[len(PREFIX):]}:{no + 1}(沿用底稿标记)")
            elif kind == "restore":
                restored += 1
                msgs.append(f"{sf.name[len(PREFIX):]}:{no + 1}(还原原版标记)")
            elif kind == "bare":
                bare += 1
                msgs.append(f"{sf.name[len(PREFIX):]}:{no + 1}(底稿无标记，未补回)")
            else:
                msgs.append(f"{sf.name[len(PREFIX):]}:{no + 1}")
        if skipped:
            continue
        report.append([tag, g["stat_ids"], g["parts"][0]["old"][:56],
                       "✓ " + ("；".join(msgs) if msgs else "无变化")])

    with open(REPORT, "w", encoding="utf-8-sig", newline="") as f:
        w = csv.writer(f)
        w.writerow(["group", "stat_ids", "old", "result"])
        w.writerows(report)

    bad = [r for r in report if not r[3].startswith("✓")]
    print(f"修正组 {len(groups)}：写入 {ok} 行，已达成 {done} 行，失败 {len(bad)} 组")
    if kept:
        print(f"  其中 {kept} 行底稿有标记但与原版不同 → 沿用底稿的（不碰别人的颜色体系）")
    if restored:
        print(f"  其中 {restored} 行底稿标记被整段删光 → 判为覆盖伤，还原原版标记")
    if bare:
        print(f"  其中 {bare} 行底稿标记被整段删光 → 按 --keep-markup 未补回（只改文本）")
    for r in bad[:20]:
        print("  ", r[0], r[1][:36], "|", r[3])
    print(f"-> {REPORT}")

    if not write:
        return

    os.makedirs(out_dir, exist_ok=True)
    base_dir = os.path.join(out_dir, "bases")
    entries = []
    for name, sf in sorted(files.items()):
        if not sf.edits and not (dual and all_lines):
            continue
        base_b, real = build_bytes(sf, dual=False)
        dst_b = base_b
        if dual:
            dst_b, _ = build_bytes(sf, dual=True, all_lines=all_lines)
        src_path = sf.path
        if name in shell_map:
            # 外壳文件也留一份：patcher 的 restore 要拿它还原，不能依赖临时解包目录
            os.makedirs(base_dir, exist_ok=True)
            keep = os.path.join(base_dir, name)
            if not os.path.exists(keep) or open(keep, "rb").read() != open(sf.path, "rb").read():
                open(keep, "wb").write(open(sf.path, "rb").read())
            src_path = keep
        src_b = open(src_path, "rb").read()
        if src_b == dst_b:
            continue
        dst = os.path.join(out_dir, name)
        open(dst, "wb").write(dst_b)
        entry = {
            "kind": "file_replace",
            "path_in_ggpk": "metadata/statdescriptions/" + name[len(PREFIX):],
            "src": src_path.replace("\\", "/"),
            "dst": dst.replace("\\", "/"),
            "sha256_original": hashlib.sha256(src_b).hexdigest(),
            "sha256_patched": hashlib.sha256(dst_b).hexdigest(),
            "size_original": len(src_b),
            "size_patched": len(dst_b),
            "edits": real,
        }
        if name in shell_map:
            entry["base"] = label
        if dual and base_b != dst_b:
            # 纯译文版也是「已知形态」：从对照版切回纯译文不该被判成未知内容
            entry["sha256_variants"] = [hashlib.sha256(base_b).hexdigest()]
        entries.append(entry)

    lock = json.load(open("game.lock.json", encoding="utf-8"))
    game = dict(lock["version"])
    game["fingerprint"] = lock["fingerprint"]
    game["locked_at"] = lock["locked_at"]
    game["ggpk_size"] = lock["ggpk"]["size"]
    game["source"] = "game.lock.json"
    mf = {"version": VERSION, "kind": "statdescriptions",
          "flavor": "dual-all" if (dual and all_lines) else "dual" if dual else "translated",
          "game": game, "fingerprint": lock["fingerprint"],
          "total_edits": sum(e["edits"] for e in entries), "files": entries}
    if shell_dir:
        # 这一包只对「与该外壳逐字节相同」的客户端成立；不要拿去分发给原版客户端
        mf["base"] = {"label": label, "dir": shell_dir.replace("\\", "/"),
                      "policy": "text-only-merge",
                      "note": "以目标机现有文件为底稿，只叠文本层；标记层（颜色/标签）保持底稿原样"}
    json.dump(mf, open(manifest, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
    print(f"-> {out_dir}/ ({len(entries)} 文件, {mf['total_edits']} 处)")
    print(f"-> {manifest}")


if __name__ == "__main__":
    main()
