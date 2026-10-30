# -*- coding: utf-8 -*-
"""依赖账本：tools/lib/deps.json 是唯一事实来源，DEPS.md 由它生成。

为什么要专门一个脚本，而不是手写一份 DEPS.md：

1. **文件名不变、内容会变。** `oo2core.dll` 换个版本还是叫这个名字，压出来的
   `bundle.gz` 就不同，但没有任何东西会报错。所以每个二进制都钉 sha256。
2. **手写的表会漂。** NuGet 升了版本、锁文件跟着变，DEPS.md 还写着老版本，
   没人会注意到。`--check` 直接拿 packages.lock.json 里 resolved 的版本来对。
3. **DEPS.md 是生成的。** 改 deps.json 之后忘记重新生成 → `--check` 红。
   人写「说明」、机器写「事实」，两边不会各说一套。

用法：
    python tools/deps_check.py            # 校验（CI 跑这个）
    python tools/deps_check.py --doc      # 重新生成 tools/lib/DEPS.md
    python tools/deps_check.py --fix      # 把当前文件的 sha256 写回 deps.json
"""
import hashlib
import json
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEPS = os.path.join(ROOT, "tools", "lib", "deps.json")
DOC = os.path.join(ROOT, "tools", "lib", "DEPS.md")
PROJECTS = ["b2probe", "dropper", "dumper", "ggpkdiag", "ggpkdoctor", "ggpkfix", "patcher"]


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def load():
    with open(DEPS, encoding="utf-8") as f:
        return json.load(f)


def save(d):
    with open(DEPS, "w", encoding="utf-8", newline="\n") as f:
        json.dump(d, f, ensure_ascii=False, indent=2)
        f.write("\n")


def locked_versions():
    """所有工程锁文件里 resolved 的版本，用来对 deps.json 的 nuget 段。"""
    got = {}
    for p in PROJECTS:
        lp = os.path.join(ROOT, "tools", p, "packages.lock.json")
        if not os.path.exists(lp):
            continue
        d = json.load(open(lp, encoding="utf-8"))
        for _, pkgs in d.get("dependencies", {}).items():
            for name, meta in pkgs.items():
                if "resolved" in meta:
                    got.setdefault(name, set()).add(meta["resolved"])
    return got


def render(d):
    """生成 DEPS.md。顺序固定：先二进制/数据，再 NuGet，最后是「已知问题」。"""
    L = []
    L.append("# 依赖账本")
    L.append("")
    L.append("> 本文件由 `python tools/deps_check.py --doc` 从 `tools/lib/deps.json` 生成，**不要手改**。")
    L.append("> 改依赖：先改 json，再 `--fix` 写回哈希，最后 `--doc` 重新生成，`--check` 必须绿。")
    L.append("")
    L.append("## 非 NuGet 依赖")
    L.append("")
    L.append("| 文件 | 版本 | 许可 | 可再分发 | 用途 |")
    L.append("|---|---|---|---|---|")
    for e in d["deps"]:
        L.append("| `%s` | %s | %s | %s | %s |" % (
            e["path"], e["version"], e["license"],
            "是" if e["redistributable"] else "**否**", e["purpose"]))
    L.append("")
    L.append("| 文件 | 字节 | sha256 |")
    L.append("|---|---|---|")
    for e in d["deps"]:
        size = e.get("bytes")
        h = e.get("sha256") or "（每次构建都变，不钉）"
        L.append("| `%s` | %s | `%s` |" % (
            e["path"], format(size, ",") if size else "?", h))
    L.append("")
    L.append("## NuGet 包")
    L.append("")
    L.append("| 包 | 版本 | 许可 | 用途 |")
    L.append("|---|---|---|---|")
    for e in d["nuget"]:
        L.append("| `%s` | `%s` | %s | %s |" % (e["id"], e["version"], e["license"], e["purpose"]))
    L.append("")
    L.append("版本由 7 份 `packages.lock.json` 钉住，CI 用 locked mode 还原：锁文件说 2.7.5，")
    L.append("解析出 2.8.0 就直接失败，不会「悄悄装个新的」。")
    L.append("")

    notes = [(e["id"], e["note"]) for e in d["deps"] + d["nuget"] if e.get("note")]
    if notes:
        L.append("## 逐条说明")
        L.append("")
        for i, n in notes:
            L.append("- **`%s`** —— %s" % (i, n))
        L.append("")

    bad = [e for e in d["deps"] if not e["redistributable"]]
    if bad:
        L.append("## 不可再分发的依赖")
        L.append("")
        for e in bad:
            L.append("`%s` 的版权归 %s，仓库里这份是能构建的前提，但**不能随发布包分发**。"
                     % (e["path"], e.get("license_holder", "未知")))
        L.append("")
        L.append("当前处理：留在仓库里（作者尚未拍板）。要合规只有一条路——移出仓库并 `.gitignore`，")
        L.append("构建时从使用者自己的游戏目录复制，缺文件时报「请从游戏目录复制 oo2core.dll」。")
        L.append("代价是新克隆不能一键构建，收益是仓库干净。")
        L.append("")
    return "\n".join(L)


def check(d):
    bad = 0
    seen = set()
    for e in d["deps"]:
        p = os.path.join(ROOT, e["path"])
        if e["path"] in seen:
            print("  ✗ %s 在 deps.json 里重复登记" % e["path"])
            bad += 1
        seen.add(e["path"])
        for k in ("id", "kind", "path", "version", "source", "license", "redistributable", "purpose"):
            if k not in e:
                print("  ✗ %s 缺字段 %s" % (e["path"], k))
                bad += 1
        if not os.path.exists(p):
            print("  ✗ %s 不存在" % e["path"])
            bad += 1
            continue
        size = os.path.getsize(p)
        if e.get("bytes") and e["bytes"] != size:
            print("  ✗ %s 字节数变了：%d → %d" % (e["path"], e["bytes"], size))
            bad += 1
        # 生成物每次构建都变，只校验存在与字节，不钉哈希
        if e["kind"] == "generated":
            continue
        if not e.get("sha256"):
            print("  ! %s 没有钉 sha256（跑 --fix 补上）" % e["path"])
            bad += 1
        elif e["sha256"] != sha256(p):
            print("  ✗ %s 的 sha256 对不上 —— 二进制被换过？" % e["path"])
            print("      账本：%s" % e["sha256"])
            print("      实际：%s" % sha256(p))
            bad += 1

    # NuGet：deps.json 的版本必须等于锁文件里解析出来的版本
    lv = locked_versions()
    for e in d["nuget"]:
        got = lv.get(e["id"])
        if not got:
            print("  ✗ %s 在锁文件里找不到（是不是没人引用了？）" % e["id"])
            bad += 1
        elif got != {e["version"]}:
            print("  ✗ %s 版本漂移：账本 %s，锁文件 %s" % (e["id"], e["version"], "/".join(sorted(got))))
            bad += 1
    # 反向：锁文件里有、账本里没有的包
    booked = {e["id"] for e in d["nuget"]}
    for name in sorted(set(lv) - booked):
        print("  ✗ 锁文件里有 %s，账本没登记" % name)
        bad += 1

    # 文档：DEPS.md 必须是 deps.json 的当前投影
    want = render(d)
    have = open(DOC, encoding="utf-8").read() if os.path.exists(DOC) else ""
    if want.strip() != have.strip():
        print("  ✗ tools/lib/DEPS.md 与 deps.json 不一致（跑 --doc 重新生成）")
        bad += 1

    if bad == 0:
        print("依赖账本一致：%d 份文件 + %d 个 NuGet 包 + %d 份锁文件"
              % (len(d["deps"]), len(d["nuget"]), len(PROJECTS)))
    return 0 if bad == 0 else 1


def main():
    d = load()
    if "--fix" in sys.argv:
        for e in d["deps"]:
            p = os.path.join(ROOT, e["path"])
            if not os.path.exists(p):
                continue
            e["bytes"] = os.path.getsize(p)
            if e["kind"] != "generated":
                e["sha256"] = sha256(p)
        save(d)
        print("已写回 %d 条哈希" % len(d["deps"]))
    if "--doc" in sys.argv:
        with open(DOC, "w", encoding="utf-8", newline="\n") as f:
            f.write(render(d))
        print("已生成 tools/lib/DEPS.md")
        return 0
    return check(d)


if __name__ == "__main__":
    sys.exit(main())
