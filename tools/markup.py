"""词缀文本的「标记层 / 文本层」分离与合并。

背景：statdesc 的引号内文本混着两类东西——

  标记层  `<enchanted>` `<rgb(209,46,46)>` `<size:23>` `{{` `}}`
  文本层  其余所有字符（含 `{0}` 这类占位符）

我们只对文本层负责。任何人（GGG 官方、别的汉化、玩家自己）改过标记层，
都不该被我们的修正稿冲掉——所以写回时把修正稿的「文本差量」叠到目标文件的
标记骨架上，而不是拿我们的整段文本去覆盖。

本模块只做这件事，两个函数对外：
    core(s)                      去掉标记后的纯文本
    merge(shell, old, new)       把 old→new 的文本改动叠到 shell 上，保留 shell 的标记
"""

from __future__ import annotations

import re

# 标签只可能是这两类（见 tools/check_sd_syntax.py 的词表校验）：
#   命名标签 <enchanted> / <craftingfire> / <size:23> / <normal> ...
#   颜色标签 <rgb(209,46,46)>
TAG = re.compile(r"<[^<>]{1,40}>")


def tokens(s: str):
    """切成 [(is_markup, 片段)]。标记层包含标签与 `{{` `}}`；文本层是其余字符。"""
    out = []
    i = 0
    n = len(s)
    while i < n:
        if s.startswith("{{", i) or s.startswith("}}", i):
            out.append((True, s[i:i + 2]))
            i += 2
            continue
        if s[i] == "<":
            m = TAG.match(s, i)
            if m:
                out.append((True, m.group(0)))
                i = m.end()
                continue
        j = i
        while j < n and s[j] != "<" and not s.startswith("{{", j) and not s.startswith("}}", j):
            j += 1
        out.append((False, s[i:j]))
        i = j
    return out


def core(s: str) -> str:
    """纯文本层：去掉标签与 {{ }}。"""
    return "".join(seg for is_mk, seg in tokens(s) if not is_mk)


def markup_seq(s: str):
    """标记层序列，用于判断两个文本的标记是否相同。"""
    return [seg for is_mk, seg in tokens(s) if is_mk]


def merge(shell: str, old: str, new: str, restore_bare: bool = True) -> str:
    """把 old→new 的文本改动叠到 shell 上。

    shell 是目标文件里那一行现有的文本（可能来自官方、别的汉化、或玩家自改），
    old  是修正稿认定的原文，new 是修正稿的目标文本。
    要求 core(shell) == core(old)，否则说明这一行已经被改得不是我们认识的样子，
    交给调用方拒绝（绝不猜）。

    标记层按三分法处理：
      1 对方留着标记（换了颜色/标签）  → 沿用对方的，一个字节都不动
      2 对方整段删光（原版有、现在一个标记都没有） → 判为机械替换的破坏，还原原版标记
      3 双方一致                        → 原样

    返回：合并后的文本。
    """
    cn, co, ct = core(shell), core(old), core(new)
    if cn != co:
        raise ValueError(
            "目标行文本与修正稿原文不符，拒绝合并：\n"
            f"    shell 文本层：{cn[:80]!r}\n"
            f"    修正稿原文：{co[:80]!r}")
    if restore_bare and not markup_seq(shell) and markup_seq(old):
        # 对方把标记连 {{ }} 一起删了。原版有、这里寸草不生 —— 是覆盖伤，不是风格，
        # 按原版标记重建（new 本身就带着 old 的标记）。
        return new
    if markup_seq(shell) == markup_seq(old):
        # 底稿的标记与原文逐段相同 = 没被任何人动过，那 old→new 的差量（含标记层改动，
        # 比如修官方笔误时补回漏掉的 `}}`）可以整套套用。
        # 依据：标记序列 + 文本层都相同 → 两个字符串必然完全相同。
        return new
    if co == ct:
        return shell                       # 纯标记差异（对方换了样式），无需动文本
    if cn == ct:
        return shell

    ops = _opcodes(co, ct)

    # shell 里哪些位置属于文本层
    is_lit = []
    for is_mk, seg in tokens(shell):
        is_lit.extend([not is_mk] * len(seg))

    out = []
    i = ci = 0                              # i: shell 光标；ci: 文本层光标
    n = len(shell)

    def flush_markup():
        """把光标前的标记原样搬过去（不动它们）。"""
        nonlocal i
        while i < n and not is_lit[i]:
            out.append(shell[i])
            i += 1

    for tag, i1, i2, j1, j2 in ops:
        while ci < i1:                      # 相同段：逐字照抄
            flush_markup()
            out.append(shell[i])
            i += 1
            ci += 1
        if tag == "equal":
            continue
        if tag == "insert":
            # 插在当前位置，且**不**抢走后面的标记：文本追加应落在
            # `秒}}` 的 `}}` 之前，而不是被挤到 `{{…}}` 外面去。
            # 只有整段文本开头插入时，才先把前导标记放过去（保证插进 `{{…}}` 里面）。
            if i1 == 0:
                flush_markup()
            out.append(ct[j1:j2])
            continue
        # replace / delete：丢掉 shell 的这段文本层，写入新文本；其间遇到的标记保留
        out.append(ct[j1:j2])
        while ci < i2:
            flush_markup()
            i += 1
            ci += 1
    while i < n:
        out.append(shell[i])
        i += 1
    return "".join(out)


def _opcodes(a: str, b: str):
    """字符级 diff。用 difflib 之外的轻量实现，便于逐行移植到 C#。

    先剥掉公共前后缀（绝大多数修正就是中段替换），剩下的短段用 LCS。
    """
    n, m = len(a), len(b)
    p = 0
    while p < n and p < m and a[p] == b[p]:
        p += 1
    s = 0
    while s < n - p and s < m - p and a[n - 1 - s] == b[m - 1 - s]:
        s += 1
    mid_a, mid_b = a[p:n - s], b[p:m - s]
    ops = []
    if p:
        ops.append(("equal", 0, p, 0, p))
    if not mid_a and not mid_b:
        pass
    elif not mid_a:
        ops.append(("insert", p, p, p, p + len(mid_b)))
    elif not mid_b:
        ops.append(("delete", p, p + len(mid_a), p, p))
    else:
        ops.extend(_lcs_ops(mid_a, mid_b, p, p))
    if s:
        ops.append(("equal", n - s, n, m - s, m))
    return ops


def _lcs_ops(a: str, b: str, off_a: int, off_b: int):
    """经典 LCS 回溯，产出 (tag, i1, i2, j1, j2)。段落短，平方复杂度可接受。"""
    n, m = len(a), len(b)
    dp = [[0] * (m + 1) for _ in range(n + 1)]
    for i in range(n - 1, -1, -1):
        for j in range(m - 1, -1, -1):
            dp[i][j] = dp[i + 1][j + 1] + 1 if a[i] == b[j] else max(dp[i + 1][j], dp[i][j + 1])
    ops = []
    i = j = 0
    while i < n and j < m:
        if a[i] == b[j]:
            i += 1
            j += 1
            continue
        if dp[i + 1][j] >= dp[i][j + 1]:
            k = i
            while i < n and (j >= m or dp[i + 1][j] >= dp[i][j + 1]):
                i += 1
            ops.append(("delete", off_a + k, off_a + i, off_b + j, off_b + j))
        else:
            k = j
            while j < m and (i >= n or dp[i][j + 1] > dp[i + 1][j]):
                j += 1
            ops.append(("insert", off_a + i, off_a + i, off_b + k, off_b + j))
    if i < n:
        ops.append(("delete", off_a + i, off_a + n, off_b + j, off_b + j))
    if j < m:
        ops.append(("insert", off_a + i, off_a + i, off_b + j, off_b + m))
    return _normalize(ops, off_a, off_b, a, b)


def _normalize(ops, off_a, off_b, a, b):
    """把相邻的 delete+insert 合并成 replace，顺序整理成单调递增。"""
    ops.sort(key=lambda o: (o[1], o[3]))
    out = []
    for op in ops:
        if out:
            pt, pi1, pi2, pj1, pj2 = out[-1]
            if op[1] == pi2 and op[3] == pj2 and (pt == "delete" or op[0] == "insert"):
                tag = "replace" if (pt in ("delete", "replace") or op[0] in ("insert",)) else pt
                out[-1] = ("replace", pi1, op[2], pj1, op[4])
                continue
        out.append(op)
    return out


def selfcheck():
    """几条硬性自检，改代码后先跑这个。"""
    cases = [
        # (shell, old, new, 期望)
        ("<enchanted>{{稀有珠寶}}</enchanted>", "<enchanted>{{稀有珠寶}}</enchanted>",
         "<enchanted>{{稀有飾品}}</enchanted>", "<enchanted>{{稀有飾品}}</enchanted>"),
        # 对方整段删光标记 → 判为覆盖伤，按原版还原标记
        ("發掘保險箱含有 1 個額外稀有珠寶", "<enchanted>{{發掘保險箱含有 1 個額外稀有珠寶}}",
         "<enchanted>{{發掘保險箱含有 1 個額外稀有飾品}}",
         "<enchanted>{{發掘保險箱含有 1 個額外稀有飾品}}"),
        # 双方都没有标记 → 只改文本
        ("稀有珠寶", "稀有珠寶", "稀有飾品", "稀有飾品"),
        # 对方把命名标签换成固定颜色，我们保留它的颜色
        ("<rgb(209,46,46)>{{增加 20% 伤害}}", "<nemesismod>{{增加 20% 伤害}}",
         "<nemesismod>{{增加 25% 伤害}}", "<rgb(209,46,46)>{{增加 25% 伤害}}"),
        # 对方删了外层标签但保留了 {{ }} → 算「留着标记」，只改文本，不补标签
        ("{{發掘保險箱含有 1 個額外稀有珠寶}}", "<enchanted>{{發掘保險箱含有 1 個額外稀有珠寶}}",
         "<enchanted>{{發掘保險箱含有 1 個額外稀有飾品}}", "{{發掘保險箱含有 1 個額外稀有飾品}}"),
        # 插入型改动
        ("<enchanted>{{有 5% 機率}}</enchanted>", "<enchanted>{{有 5% 機率}}</enchanted>",
         "<enchanted>{{有 5% 的機率}}</enchanted>", "<enchanted>{{有 5% 的機率}}</enchanted>"),
        # 纯标记差异，文本没变
        ("<rgb(1,2,3)>{{相同}}", "<enchanted>{{相同}}", "<enchanted>{{相同}}", "<rgb(1,2,3)>{{相同}}"),
        # 多段替换 + 占位符
        ("<rgb(200,200,200)>{{每秒造成 {0} 點傷害，持續 {1} 秒}}",
         "<normal>{{每秒造成 {0} 點傷害，持續 {1} 秒}}",
         "<normal>{{每秒造成 {0} 點傷害，持續 {1} 秒，可疊加}}",
         "<rgb(200,200,200)>{{每秒造成 {0} 點傷害，持續 {1} 秒，可疊加}}"),
        # 修官方笔误：`{0]` → `{0}`（标记层没动，纯文本）
        ("法術擁有等同於你已裝備雙手武器{0]%物理傷害的附加法術傷害",
         "法術擁有等同於你已裝備雙手武器{0]%物理傷害的附加法術傷害",
         "法術擁有等同於你已裝備雙手武器{0}%物理傷害的附加法術傷害",
         "法術擁有等同於你已裝備雙手武器{0}%物理傷害的附加法術傷害"),
        # 修官方笔误：漏掉的 `}}` 要补回来（底稿标记与原文一致 = 没被动过，可整套套用）
        ("<enchanted>{{몬스터가 떨어뜨리는 아이템의 수량 {0}% 감소",
         "<enchanted>{{몬스터가 떨어뜨리는 아이템의 수량 {0}% 감소",
         "<enchanted>{{몬스터가 떨어뜨리는 아이템의 수량 {0}% 감소}}",
         "<enchanted>{{몬스터가 떨어뜨리는 아이템의 수량 {0}% 감소}}"),
        # 同一行的漏 `}}`，但底稿被别人换了颜色 → 不补 `}}`，只保留对方的样式
        ("<rgb(1,2,3)>{{몬스터가 떨어뜨리는 아이템의 수량 {0}% 감소",
         "<enchanted>{{몬스터가 떨어뜨리는 아이템의 수량 {0}% 감소",
         "<enchanted>{{몬스터가 떨어뜨리는 아이템의 수량 {0}% 감소}}",
         "<rgb(1,2,3)>{{몬스터가 떨어뜨리는 아이템의 수량 {0}% 감소"),
    ]
    ok = True
    for shell, old, new, want in cases:
        got = merge(shell, old, new)
        flag = "ok " if got == want else "FAIL"
        if got != want:
            ok = False
        print(f"  [{flag}] {shell[:40]!r}  ->  {got[:40]!r}")
        if got != want:
            print(f"         want {want!r}")
    print("selfcheck", "passed" if ok else "FAILED")
    return ok


if __name__ == "__main__":
    selfcheck()
