# -*- coding: utf-8 -*-
"""内容闸：即将写进客户端的那一行，只许是「我们能推导出来的那一行」。

补丁器的可信边界不在网络，在**数据包**。bundle.gz 一旦被别人塞进私货，
客户端就变成任意文本的展示器（零宽字符改界面顺序、自定义标签骗过解析器、
超长行撑爆 UI）。这道闸不判断译文好坏，只回答一个问题：

    这一行文本，能不能用「原文 + 译文」两行按 markup.merge 推出来？

推不出来的，一律不是我们写的。

五组规则（编号与 docs/claims.json、ContentGate.cs 一一对应）：

  G1 字符集    零宽 / RTL 覆写 / 控制字符 / 孤立代理项
  G2 标记      标签必须在白名单内且语法合法；`{{` `}}` 必须配对
  G3 结构      占位符集合与原文一致；行数与基线一致
  G4 体量      单行 ≤ 2000 字符；整文件 ≤ 基线 ×1.25；总量 ≤ 基线 ×1.2
  G5 可推导    文本层必须等于目标文本；标签不得超出「原文 ∪ 目标」

两种模式：warn 只记录，enforce 拒绝。灰度期用 warn，确认 45 个原版文件
与三套 flavor 全 0 命中后再切 enforce。

用法：
    python tools/content_gate.py --test          # 跑 fixtures/gate/cases.json
    python tools/content_gate.py --whitelist     # 从 raw_sd 重算标签白名单
    python tools/content_gate.py --scan out/patch-sd-dual-all   # 扫一个产物目录
"""

from __future__ import annotations

import json
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
WHITELIST = os.path.join(ROOT, "tools", "pack", "markup-whitelist.json")
FIXTURES = os.path.join(ROOT, "fixtures", "gate", "cases.json")

MAX_LINE = 2000
FILE_RATIO = 1.25
TOTAL_RATIO = 1.20

# 与 markup.py 的 TAG 一致：标签只可能是 <...>，且不会嵌套
TAG = re.compile(r"<[^<>]{1,40}>")
NAMETAG = re.compile(r"<([a-z][a-z0-9_]*)>")
RGB = re.compile(r"<rgb\(\s*(\d{1,3})\s*,\s*(\d{1,3})\s*,\s*(\d{1,3})\s*\)>")

# 与 check_placeholders.py 的 TOKEN_RE **必须一字不差**：那边算「漏了哪些占位符」，
# 这边算「多/少了哪些」，两边正则一旦漂移，同一个文件会一边通过一边报警。
TOKEN_RE = re.compile(r"\{\s*[-+]?\d[^}]*\}|#|%|\\n")

# 不可见/可反转字符：正常译文里一个都不该有
BAD_CODEPOINTS = [
    (0x0000, 0x0008), (0x000B, 0x001F), (0x007F, 0x009F),
    (0x200B, 0x200F), (0x202A, 0x202E), (0x2066, 0x2069),
]
BAD_NAMES = {
    (0x0000, 0x0008): "控制字符",
    (0x000B, 0x001F): "控制字符",
    (0x007F, 0x009F): "C1 控制字符",
    (0x200B, 0x200F): "零宽字符",
    (0x202A, 0x202E): "双向覆写（RTL）",
    (0x2066, 0x2069): "双向隔离（RTL）",
}


def load_whitelist(path=WHITELIST) -> dict:
    d = json.load(open(path, encoding="utf-8"))
    return {"named": set(d["named"]), "allow_rgb": bool(d.get("allow_rgb", True))}


# ---------------------------------------------------------------- 五个规则

def g1_charset(s: str):
    """不可见字符、控制字符、孤立代理项。"""
    out = []
    for i, ch in enumerate(s):
        cp = ord(ch)
        for lo, hi in BAD_CODEPOINTS:
            if lo <= cp <= hi:
                out.append(("G1", f"第 {i + 1} 字符 U+{cp:04X} 是{BAD_NAMES[(lo, hi)]}"))
                break
        if 0xD800 <= cp <= 0xDFFF:
            out.append(("G1", f"第 {i + 1} 字符是孤立代理项 U+{cp:04X}"))
        elif cp == 0xFEFF:
            out.append(("G1", f"第 {i + 1} 字符是 BOM（文本中间不合法）"))
        if len(out) >= 3:      # 一行里报三条足够定位，不刷屏
            break
    return out


def g2_markup(s: str, wl: dict):
    """标签白名单 + `{{ }}` 配对。"""
    out = []
    for t in TAG.findall(s):
        m = RGB.fullmatch(t)
        if m:
            if not wl["allow_rgb"]:
                out.append(("G2", f"不允许颜色标签：{t}"))
                continue
            for v in m.groups():
                if int(v) > 255:
                    out.append(("G2", f"颜色分量越界：{t}"))
                    break
            continue
        m = NAMETAG.fullmatch(t)
        if not m:
            out.append(("G2", f"标签语法不认识：{t}"))
        elif m.group(1) not in wl["named"]:
            out.append(("G2", f"标签不在白名单：{t}"))
    if s.count("<") != len(TAG.findall(s)):
        out.append(("G2", "存在不配对或超长的 `<`"))
    # 只数 `{{` 的个数不够，而且 `{0}` 这种单花括号占位符本来就该在文本层里。
    # 判据是「去掉标识为标记的 `{{ }}` 与 `{…}` 占位符之后，文本层不许再有花括号」。
    import markup
    seq = markup.markup_seq(s)
    a, b = seq.count("{{"), seq.count("}}")
    if a != b:
        out.append(("G2", f"`{{{{` 与 `}}}}` 不配对（{a} / {b}）"))
    else:
        rest = re.sub(r"\{[^{}]*\}", "", markup.core(s))
        if "{" in rest or "}" in rest:
            out.append(("G2", f"文本层里有落单的花括号：{rest.strip()[:40]!r}"))
    return out


def g3_structure(s: str, old: str | None, want: str | None, file: str | None = None,
                 exc: set | None = None):
    """占位符守恒。

    给了 want（运行时：bundle 说的目标文本）就要求**逐项相等**，多一个少一个都算。
    没给 want（构建期扫产物：只拿得到官方原文）就退化成**子集**检查 —— 原文里的
    占位符一个都不许丢；产物多出来的（比如双语 flavor 在中间插的 `\\n`）放行。

    白名单（gate-exceptions.json）按「文件 + 原文 + 目标」完整文本匹配，
    只覆盖 fix-placeholder / fix-typo 那几条已判过的编号错位修正。

    反向不查：`want` 自己把占位符写错，靠 check_placeholders.py 在构建期挡，
    那道检查手上有 en/zh 两份原文，比这里准。
    """
    if file and old and exc:
        if (file, old, s) in exc or (file, old, s.split("\\n")[0]) in exc:
            return []
    if want is not None:
        ref, mode = want, "equal"
    elif old is not None:
        ref, mode = old, "subset"
    else:
        return []
    want_t = sorted(TOKEN_RE.findall(ref))
    got = sorted(TOKEN_RE.findall(s))
    # 原文的占位符一个都不许丢 —— 即使 want 自己也漏了，这一步也要拦住
    old_t = [x for x in TOKEN_RE.findall(old)] if (want is not None and old is not None) else []
    if mode == "equal" and want_t == got and all(x in got for x in old_t):
        return []
    miss = [x for x in want_t if x not in got] + [x for x in old_t if x not in got]
    extra = [] if mode == "subset" else [x for x in got if x not in want_t]
    if not miss and not extra:
        return []
    bits = []
    if miss:
        bits.append("缺 " + " ".join(sorted(set(miss))))
    if extra:
        bits.append("多 " + " ".join(sorted(set(extra))))
    return [("G3", "占位符与" + ("目标文本" if mode == "equal" else "原文") +
             "不一致：" + "，".join(bits))]


def g4_size(s: str):
    if len(s) > MAX_LINE:
        return [("G4", f"单行 {len(s)} 字符，超过 {MAX_LINE}")]
    return []


def g5_derivable(s: str, old: str | None, want: str | None, shell: str | None):
    """文本层必须是目标文本；标签不得超出「原文 ∪ 目标 ∪ 底稿」的并集。"""
    if want is None:
        return []
    out = []
    # 延迟导入：content_gate 不该在 import 期就拖起 markup 的 diff 实现
    import markup
    if markup.core(s) != markup.core(want):
        out.append(("G5", f"文本层不是目标文本：{markup.core(s)[:40]!r}"))
        return out
    allowed = set()
    for src in (old, want, shell):
        if src:
            allowed.update(markup.markup_seq(src))
    extra = [t for t in markup.markup_seq(s) if t not in allowed]
    if extra:
        out.append(("G5", "出现推导不出来的标记：" + " ".join(extra[:3])))
    return out


def check_line(s: str, *, old: str | None = None, want: str | None = None,
               shell: str | None = None, wl: dict | None = None,
               file: str | None = None, exc: set | None = None):
    wl = wl or load_whitelist()
    out = []
    out += g1_charset(s)
    out += g2_markup(s, wl)
    out += g3_structure(s, old, want, file, exc)
    out += g4_size(s)
    out += g5_derivable(s, old, want, shell)
    return out


def check_file(lines, *, base_lines=None, base_bytes=None, total_bytes=None,
               total_base_bytes=None, wl=None):
    """文件级：行数守恒、整文件体量、总量体量，外加逐行 check_line。

    base_lines 给了就要求行数一致 —— 增删行会改掉 stat desc 的编号，
    客户端按编号取值，多一行就是整表错位。
    """
    wl = wl or load_whitelist()
    out = []
    if base_lines is not None and len(lines) != len(base_lines):
        out.append(("G3", f"行数 {len(lines)}，基线 {len(base_lines)}"))
    body = "\r\n".join(lines)
    n = len(body.encode("utf-16-le"))
    if base_bytes and n > base_bytes * FILE_RATIO:
        out.append(("G4", f"整文件 {n} B，超过基线的 {FILE_RATIO} 倍（{base_bytes} B）"))
    if total_base_bytes and total_bytes and total_bytes > total_base_bytes * TOTAL_RATIO:
        out.append(("G4", f"总量 {total_bytes} B，超过基线的 {TOTAL_RATIO} 倍"))
    for i, s in enumerate(lines):
        old = base_lines[i] if base_lines is not None and i < len(base_lines) else None
        for rule, msg in check_line(s, old=old, wl=wl):
            out.append((rule, f"第 {i + 1} 行 {msg}"))
    return out


# ---------------------------------------------------------------- 白名单

def build_whitelist(sd_dir: str) -> dict:
    """从官方原文里统计标签词表。原文里出现过的，就是客户端认得的。"""
    named = set()
    for name in sorted(os.listdir(sd_dir)):
        if not name.endswith(".txt"):
            continue
        try:
            s = open(os.path.join(sd_dir, name), "rb").read().decode("utf-16")
        except UnicodeDecodeError:
            continue
        for t in TAG.findall(s):
            m = NAMETAG.fullmatch(t)
            if m:
                named.add(m.group(1))
    return {
        "_comment": "由 tools/content_gate.py --whitelist 从 raw_sd 生成，勿手改。"
                    "原文里出现过的标签，才是客户端认得的。",
        "named": sorted(named),
        "allow_rgb": True,
    }


# ---------------------------------------------------------------- 自检

def run_fixtures(path=FIXTURES) -> int:
    cases = json.load(open(path, encoding="utf-8"))["case"]
    wl = load_whitelist()
    bad = 0
    for c in cases:
        if "lines" in c:
            got = check_file(c["lines"], base_lines=c.get("base_lines"),
                             base_bytes=c.get("base_bytes"),
                             total_bytes=c.get("total_bytes"),
                             total_base_bytes=c.get("total_base_bytes"), wl=wl)
        else:
            got = check_line(c["actual"], old=c.get("old"), want=c.get("want"),
                             shell=c.get("shell"), wl=wl)
        rules = [r for r, _ in got]
        ok = (not got) if c["expect"] == "accept" else (c["rule"] in rules)
        if not ok:
            bad += 1
        print(f"  {'✓' if ok else '✗'} {c['name']}"
              + ("" if ok else f"   期望 {c['expect']}/{c.get('rule', '?')}，实际 {rules or '通过'}"))
        if not ok:
            for r, m in got:
                print(f"      {r} {m}")
    print(f"内容闸自检：{len(cases) - bad}/{len(cases)}")
    return 0 if bad == 0 else 1


def text_span(raw: str):
    """行内引号文本段的 [start, end)。与 patch_sd.py / dropper 的实现一致：
    第一个 ` "` 之后到下一个未转义的 `"` 之间。找不到返回 (-1, -1)。"""
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


EXCEPTIONS = os.path.join(ROOT, "tools", "pack", "gate-exceptions.json")


def load_exceptions(path=EXCEPTIONS) -> set:
    """G3 白名单：(文件, 原文, 目标) 三元组。占位符集合故意不同的行，只认完整文本匹配。"""
    try:
        d = json.load(open(path, encoding="utf-8"))
    except FileNotFoundError:
        return set()
    return {(c["file"], c["old"], c["new"]) for c in d["case"]}


def check_pair(src_path: str, dst_path: str, name: str, wl: dict, exc: set):
    """写入前 vs 写入后，只查**被改过的行**。

    整份文件逐行查是不对的：官方原文里本来就有 `{0]` 这种笔误、也有我们不碰的行，
    拿闸去卡它们只会把自己卡死。闸的作用域是「将要写进客户端的那一行」。
    """
    out = []
    cur = open(dst_path, "rb").read().decode("utf-16").split("\r\n")
    base = open(src_path, "rb").read().decode("utf-16").split("\r\n")
    if len(cur) != len(base):
        return [("G3", f"行数 {len(cur)} / 基线 {len(base)} —— 整表错位")]
    for i, raw in enumerate(cur):
        if raw == base[i]:
            continue
        j, k = text_span(raw)
        if j < 0 or k <= j:
            continue
        bj, bk = text_span(base[i])
        old = base[i][bj:bk] if bj >= 0 and bk > bj else None
        text = raw[j:k]
        for rule, msg in check_line(text, old=old, wl=wl, file=name, exc=exc):
            out.append((rule, f"第 {i + 1} 行 {msg}"))
    return out


def mode_of(argv) -> str:
    """--gate=off|warn|enforce，默认 warn（灰度）。缺省就是 warn 是故意的：
    误杀比漏放更容易被看见，先让它在产品里跑一个版本、把命中记下来，再谈拦截。"""
    for a in argv:
        if a.startswith("--gate="):
            v = a.split("=", 1)[1].strip().lower()
            if v in ("off", "warn", "enforce"):
                return v
            raise SystemExit(f"--gate= 只支持 off / warn / enforce，收到 {v}")
    return "warn"


def gate_entries(entries, report_path: str, mode: str = "warn"):
    """对清单里的每一对 (src, dst) 过闸，命中写 report_path。

    entries 的元素要有 src / dst / path_in_ggpk 三个字段（patch_sd 与 build_bundle
    的清单都是这个形状）。返回 (命中数, 明细行)。
    """
    wl, exc = load_whitelist(), load_exceptions()
    hits, rows = 0, []
    for e in entries:
        name = os.path.basename(e["dst"])
        src = e["src"] if os.path.isabs(e["src"]) else os.path.join(ROOT, e["src"])
        dst = e["dst"] if os.path.isabs(e["dst"]) else os.path.join(ROOT, e["dst"])
        if not (os.path.exists(src) and os.path.exists(dst)):
            continue
        got = check_pair(src, dst, name, wl, exc)
        if not got:
            continue
        hits += len(got)
        for rule, msg in got:
            rows.append((e.get("path_in_ggpk", name), rule, msg))
    if rows:
        with open(report_path, "w", encoding="utf-8", newline="\n") as f:
            f.write("# 内容闸命中\n\n")
            f.write(f"模式 `{mode}`，命中 {hits} 处。每一条都得先判「是不是误杀」——\n"
                    "G2 会误杀对方自造标签，G5 的标记层边界只能放在 merge() 的三分法里。\n\n")
            f.write("| 文件 | 规则 | 说明 |\n|---|---|---|\n")
            for p, r, m in rows[:200]:
                f.write(f"| `{p}` | {r} | {m.replace('|', '/')} |\n")
    return hits, rows


def scan_dir(d: str) -> int:
    """扫一个产物目录（out/patch-sd*），只查**被改过的行**。

    整份文件逐行查是不对的：官方原文里本来就有 `{0]` 这种笔误、也有我们不碰的行，
    拿闸去卡它们只会把自己卡死。闸的作用域是「将要写进客户端的那一行」，
    所以基线里没变的行一律跳过。
    """
    wl = load_whitelist()
    exc = load_exceptions()
    hits = 0
    files = sorted(f for f in os.listdir(d) if f.endswith(".txt"))
    for name in files:
        bp = os.path.join(ROOT, "raw_sd", name)
        if not os.path.exists(bp):
            print(f"  {name}: 基线里没有同名文件，跳过")
            continue
        got = check_pair(bp, os.path.join(d, name), name, wl, exc)
        if got:
            hits += len(got)
            print(f"  {name}: {len(got)} 处")
            for r, m in got[:5]:
                print(f"    {r} {m}")
    print(f"扫描 {len(files)} 个文件，命中 {hits} 处")
    return 0 if hits == 0 else 1


if __name__ == "__main__":
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    a = sys.argv[1:]
    if "--test" in a:
        sys.exit(run_fixtures())
    if "--whitelist" in a:
        wl = build_whitelist(os.path.join(ROOT, "raw_sd"))
        with open(WHITELIST, "w", encoding="utf-8", newline="\n") as f:
            json.dump(wl, f, ensure_ascii=False, indent=2)
            f.write("\n")
        print(f"{WHITELIST}: {len(wl['named'])} 个命名标签")
        sys.exit(0)
    if "--scan" in a:
        sys.exit(scan_dir(a[a.index("--scan") + 1]))
    print(__doc__)
