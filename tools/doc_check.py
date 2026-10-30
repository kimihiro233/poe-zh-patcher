#!/usr/bin/env python3
"""README 与实现的一致性检查。

文档漂移不会报错，只会让用户按错的东西操作 —— 比如 README 说备份留 5 份，
代码里改成 3 份，谁都不会发现，直到某天用户发现第二份备份不见了。

所以这里不「读文档猜意图」，只做一件事：把 README 里能被产物算出来的数字，
逐条拿去和产物比。对不上就退出码 1，接进 CI 之后漂移当场变红。

    python tools/doc_check.py            # 检查
    python tools/doc_check.py -v         # 连每个命中位置一起打印

来源只有三类，都是仓库里真实存在的文件：
    tools/pack/bundle.gz   —— 当前数据包（文件数、行数、游戏版本）
    game.lock.json         —— 实测客户端的字节数
    tools/shared/*.cs      —— 轮转份数这类硬编码常量
"""
from __future__ import annotations

import gzip
import json
import re
import sys
import pathlib

ROOT = pathlib.Path(__file__).resolve().parent.parent
README = ROOT / "README.md"
CLAIMS = ROOT / "docs" / "claims.json"
BUNDLE = ROOT / "tools" / "pack" / "bundle.gz"
LOCK = ROOT / "game.lock.json"
SAFETY = ROOT / "tools" / "shared" / "Safety.cs"
UNDO = ROOT / "tools" / "shared" / "GgpkUndo.cs"
DROPPER = ROOT / "tools" / "dropper" / "Program.cs"


def load_bundle() -> dict:
    with gzip.open(BUNDLE, "rb") as f:
        return json.load(f)


def build_sources() -> dict:
    """把所有断言可能引用的值算出来。少一个就在这里加，不要在 claims.json 里写死数字。"""
    b = load_bundle()
    sd = b.get("statdesc", [])
    dat = b.get("dat", [])
    lock = json.loads(LOCK.read_text(encoding="utf-8"))
    safety = SAFETY.read_text(encoding="utf-8")
    undo = UNDO.read_text(encoding="utf-8")

    m_backup = re.search(r"Prune\(root, keep:\s*(\d+)\)", safety)
    m_snap = re.search(r"Prune\(keep:\s*(\d+)\)", undo)

    src = {
        "bundle.statdesc.count": len(sd),
        "bundle.statdesc.rows": sum(len(x.get("rows", [])) for x in sd),
        "bundle.dat.count": len(dat),
        "bundle.files": len(sd) + len(dat),
        "bundle.game.client_version": b.get("game", {}).get("client_version", ""),
        "bundle.game.patch_version": b.get("game", {}).get("patch_version", ""),
        "lock.ggpk.size": lock.get("ggpk", {}).get("size", 0),
    }
    if m_backup:
        src["code.backup_keep"] = int(m_backup.group(1))
    if m_snap:
        src["code.snapshot_keep"] = int(m_snap.group(1))

    # 供应链：工程数、锁文件数、依赖账本条目数。三者在 README 里是 7 / 7 / 6，
    # 加一个工程或换一个依赖就应当红。
    src["code.projects"] = len(list((ROOT / "tools").glob("*/*.csproj")))
    src["supply.locks"] = len(list((ROOT / "tools").glob("*/packages.lock.json")))
    deps_json = ROOT / "tools" / "lib" / "deps.json"
    if deps_json.exists():
        src["supply.deps"] = len(json.loads(deps_json.read_text(encoding="utf-8"))["deps"])

    # 内容闸：白名单标签数 / 对抗样本条数 / 白名单条目数
    whitelist = ROOT / "tools" / "pack" / "markup-whitelist.json"
    fixtures = ROOT / "fixtures" / "gate" / "cases.json"
    exceptions = ROOT / "tools" / "pack" / "gate-exceptions.json"
    if whitelist.exists():
        src["gate.whitelist.named"] = len(json.loads(whitelist.read_text(encoding="utf-8"))["named"])
    if fixtures.exists():
        src["gate.fixtures.count"] = len(json.loads(fixtures.read_text(encoding="utf-8"))["case"])
    if exceptions.exists():
        src["gate.exceptions.count"] = len(json.loads(exceptions.read_text(encoding="utf-8"))["case"])
    return src


def as_number(text: str) -> int | None:
    t = text.replace(",", "").replace("，", "").strip()
    return int(t) if t.isdigit() else None


def check_claims(text: str, src: dict, verbose: bool) -> tuple[int, int]:
    spec = json.loads(CLAIMS.read_text(encoding="utf-8"))
    ok = bad = 0

    for c in spec.get("claims", []):
        field = c["source"]
        if field not in src:
            print(f"  ✗ {c['id']}：来源 {field} 没有实现（claims.json 写了个不存在的字段）")
            bad += 1
            continue

        want = src[field]
        hits = list(re.finditer(c["regex"], text))
        if not hits:
            print(f"  ✗ {c['id']}：README 里找不到「{c['regex']}」—— 这句话被改写或删掉了")
            bad += 1
            continue

        local_bad = 0
        for h in hits:
            got = as_number(h.group(1))
            line_no = text.count("\n", 0, h.start()) + 1
            if got is None or str(got) != str(want):
                local_bad += 1
                print(f"  ✗ {c['id']}：第 {line_no} 行写「{h.group(0)}」，产物是 {want}")
            elif verbose:
                print(f"  ✓ {c['id']}：第 {line_no} 行 {got} == {field} {want}")

        if local_bad == 0:
            ok += 1
            if not verbose:
                print(f"  ✓ {c['id']} {want}（{len(hits)} 处）")
        else:
            bad += 1

    for u in spec.get("unverified", []):
        hits = list(re.finditer(u["regex"], text))
        if hits:
            print(f"  ~ {u['id']}：README 里有 {len(hits)} 处「{hits[0].group(0)}」，但没有可核的来源")

    return ok, bad


def check_flags(text: str) -> tuple[int, int]:
    """README 命令行里出现的开关，必须真的是本程序认得的。

    只查含 `PoE-zh-Patcher.exe` 的行 —— 仓库里还有一堆 python 工具，
    它们各有自己的 `--write` / `--apply`，混进来只会制造假警报。
    """
    usage = DROPPER.read_text(encoding="utf-8")
    m = re.search(r"private static void PrintUsage\(\)\s*\{(.+?)\n    \}", usage, re.S)
    if not m:
        print("  ✗ 找不到 PrintUsage()，无法校验开关")
        return 0, 1

    known = set(re.findall(r"--([a-z][a-z0-9-]*)", m.group(1)))
    mentioned: dict[str, int] = {}
    for i, line in enumerate(text.splitlines(), 1):
        if "PoE-zh-Patcher.exe" not in line:
            continue
        if "--" not in line:
            continue
        for f in re.findall(r"--([a-z][a-z0-9-]*)", line):
            mentioned.setdefault(f, i)

    bad = 0
    for f, line_no in sorted(mentioned.items()):
        if f not in known:
            print(f"  ✗ README 第 {line_no} 行写了 --{f}，但程序没有这个开关")
            bad += 1
    if bad == 0:
        print(f"  ✓ README 里 {len(mentioned)} 个开关都是程序认得的")
    return (1 if bad == 0 else 0), bad


def main() -> int:
    verbose = "-v" in sys.argv or "--verbose" in sys.argv
    text = README.read_text(encoding="utf-8")
    src = build_sources()

    print("doc_check：README 与实现的一致性")
    print()
    ok1, bad1 = check_claims(text, src, verbose)
    ok2, bad2 = check_flags(text)

    ok, bad = ok1 + ok2, bad1 + bad2
    print()
    print(f"汇总：{ok} 项通过，{bad} 项失败" if bad else f"汇总：{ok} 项通过，0 项失败")
    if bad:
        print("改 README，或者改实现 —— 但别让两边各说一套。")
    return 0 if bad == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
