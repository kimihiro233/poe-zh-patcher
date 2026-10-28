# -*- coding: utf-8 -*-
"""升级体检：拿着旧版本的修正稿，检查它们在新版本的游戏文本里还成不成立。

游戏一更新，修正稿最怕三件事：
  1. 要改的那句英文改写了  → 译文得重做，不能照搬
  2. 那个 stat / 那个表行没了 → 条目作废
  3. 位置还在、文本没变     → 照旧可用

用法：
    python tools/check_fixes.py            # 对照当前 out/statdesc.csv 与 out/pairs.csv
    python tools/check_fixes.py --only=fix-mult
输出：
    控制台汇总 + out/fix-status.csv（逐条状态）

状态：
    pending  当前文本仍是 zh_old   → 还没打（或没打上）
    applied  当前文本已是 zh_new   → 已生效
    drift    两边都不是            → 游戏更新改过原文，必须人工重判
    gone     定位不到              → stat/变体/表行已不存在
    unknown  无法用文本判定        → 需人工（如 fix-direction 这种带上下文的）
    hold     本来就没定稿          → 不计入待办
"""
import argparse
import csv
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, "out")

AFFIX = ["fix-direction", "fix-mult", "fix-additive-rev", "fix-wording",
         "fix-gem", "fix-order", "fix-category", "fix-category-hold"]
UI = ["fix-category-ui"]
HOLD = {"fix-category-hold"}


def norm(s):
    return (s or "").replace("\r\n", "\n").strip()


def load(name):
    p = os.path.join(OUT, name + ".csv")
    if not os.path.exists(p):
        return []
    return list(csv.DictReader(open(p, encoding="utf-8-sig")))


def build_affix_index(rows):
    """stat_ids -> [rows]；另建 en -> [rows] 兜底（stat_ids 顺序可能被 GGG 调过）。"""
    by_ids, by_en = {}, {}
    for r in rows:
        by_ids.setdefault(r["stat_ids"], []).append(r)
        by_en.setdefault(norm(r["en"]), []).append(r)
    return by_ids, by_en


def check_affix(fix, idx, by_en):
    f = fix.get("file", "")
    ids = fix.get("stat_ids", "")
    en = norm(fix.get("en", ""))
    old = norm(fix.get("zh_old", "") or fix.get("zh_before", ""))
    new = norm(fix.get("zh_new", "") or fix.get("zh_after", ""))
    note = ""

    cands = idx.get(ids, [])
    if cands and f:
        cands = [r for r in cands if r["file"] == f] or cands
    if not cands and en:
        cands = [r for r in by_en.get(en, []) if not f or r["file"] == f]
        if cands:
            note = "stat_ids 变了，按英文原文命中"
    if not cands:
        return "gone", "", note or "stat_ids 在当前语料中不存在"

    if en:
        same = [r for r in cands if norm(r["en"]) == en]
        if not same:
            cur = " ⏎ ".join(norm(r["en"]) for r in cands[:2])
            return "drift", cur, "英文原文已改写"
        cands = same

    zhs = [norm(r["zh"]) for r in cands]
    if new and all(z == new for z in zhs):
        return "applied", " ⏎ ".join(zhs), note
    if old and all(z == old for z in zhs):
        return "pending", " ⏎ ".join(zhs), note
    if new and any(z == new for z in zhs):
        return "applied", " ⏎ ".join(zhs), note + "（部分变体已是新值）"
    return "drift", " ⏎ ".join(zhs), note + "当前值与旧/新都对不上"


def split_sides(text):
    """fix-direction 的 zh_before/zh_after 是「正 …\n负 …」两行拼接，拆回逐个变体文本。"""
    out = []
    for line in norm(text).split("\n"):
        line = line.strip()
        if not line:
            continue
        out.append(line.split(" ", 1)[1] if line[:2] in ("正 ", "负 ") else line)
    return out


def check_direction(fix, idx):
    ids = fix.get("stat_ids", "")
    cands = idx.get(ids, [])
    if not cands:
        return "gone", "", "stat_ids 在当前语料中不存在"
    cur = {norm(r["zh"]) for r in cands}
    after, before = split_sides(fix.get("zh_after", "")), split_sides(fix.get("zh_before", ""))
    if after and all(x in cur for x in after):
        return "applied", " ⏎ ".join(sorted(cur)), ""
    if before and all(x in cur for x in before):
        return "pending", " ⏎ ".join(sorted(cur)), ""
    return "drift", " ⏎ ".join(sorted(cur)), "变体文本已变，需重新判定方向"


def check_ui(fix, ui_index):
    key = (fix["table"], fix["row"], fix["slot"])
    cur = ui_index.get(key)
    if cur is None:
        return "gone", "", "表/行/槽位已不存在"
    z = norm(cur)
    if z == norm(fix["zh_new"]):
        return "applied", z, ""
    if z == norm(fix["zh_old"]):
        return "pending", z, ""
    return "drift", z, "该槽位文本已被改动（游戏更新或别的补丁）"


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", help="只查某一批，逗号分隔")
    a = ap.parse_args()
    want = set(a.only.split(",")) if a.only else None

    sd = load("statdesc")
    if not sd:
        sys.exit("缺 out/statdesc.csv，先跑 python tools/parse_statdesc.py raw_sd out/statdesc.csv")
    idx, by_en = build_affix_index(sd)

    pairs = load("pairs")
    if not pairs:
        sys.exit("缺 out/pairs.csv，先跑 python tools/extract_pairs.py pairs out/pairs.csv")
    ui_index = {(r["table"], r["row"], r["slot"]): r["zh"] for r in pairs}

    rows_out = []
    summary = []
    for name in AFFIX + UI:
        if want and name not in want:
            continue
        items = load(name)
        if not items:
            continue
        cnt = {"pending": 0, "applied": 0, "drift": 0, "gone": 0, "unknown": 0, "hold": 0}
        for i, fix in enumerate(items, 1):
            if name in HOLD:
                state, cur, note = "hold", "", "待拍板，本来就未定稿"
            elif name in UI:
                state, cur, note = check_ui(fix, ui_index)
            elif name == "fix-direction":
                state, cur, note = check_direction(fix, idx)
            else:
                state, cur, note = check_affix(fix, idx, by_en)
            cnt[state] += 1
            rows_out.append({
                "batch": name, "line": i, "state": state,
                "stat_ids": fix.get("stat_ids", ""),
                "table": fix.get("table", ""), "row": fix.get("row", ""),
                "slot": fix.get("slot", ""),
                "en": fix.get("en", ""), "zh_old": fix.get("zh_old", "") or fix.get("zh_before", ""),
                "zh_new": fix.get("zh_new", "") or fix.get("zh_after", ""),
                "current": cur, "note": note,
            })
        summary.append((name, len(items), cnt))

    dst = os.path.join(OUT, "fix-status.csv")
    with open(dst, "w", encoding="utf-8-sig", newline="") as f:
        w = csv.DictWriter(f, fieldnames=["batch", "line", "state", "stat_ids", "table",
                                          "row", "slot", "en", "zh_old", "zh_new",
                                          "current", "note"])
        w.writeheader()
        w.writerows(rows_out)

    print("%-20s %5s %8s %8s %7s %6s %6s" % ("批次", "条数", "pending", "applied", "drift", "gone", "hold"))
    print("-" * 66)
    tot = {"pending": 0, "applied": 0, "drift": 0, "gone": 0, "unknown": 0, "hold": 0}
    for name, n, cnt in summary:
        print("%-20s %5d %8d %8d %7d %6d %6d"
              % (name, n, cnt["pending"], cnt["applied"], cnt["drift"], cnt["gone"], cnt["hold"]))
        for k in tot:
            tot[k] += cnt[k]
    print("-" * 66)
    print("%-20s %5d %8d %8d %7d %6d %6d"
          % ("合计", sum(n for _, n, _ in summary), tot["pending"], tot["applied"],
             tot["drift"], tot["gone"], tot["hold"]))

    bad = [r for r in rows_out if r["state"] in ("drift", "gone")]
    if bad:
        print("\n需要重做的 %d 条：" % len(bad))
        for r in bad[:20]:
            tag = r["stat_ids"] or "%s/%s/%s" % (r["table"], r["row"], r["slot"])
            print("  [%s] %s" % (r["state"], tag))
            if r["note"]:
                print("        %s" % r["note"])
            if r["current"]:
                print("        现在: %s" % r["current"][:70])
        if len(bad) > 20:
            print("  ... 另有 %d 条，见 out/fix-status.csv" % (len(bad) - 20))

    print("\n明细：%s" % os.path.relpath(dst, ROOT).replace("\\", "/"))
    print("注意：UI 侧（fix-category-ui）的状态读的是 out/pairs.csv，也就是**最近一次导出**的内容。")
    print("      刚往 ggpk 打过补丁的话，先重导 pairs 再跑本脚本，否则会显示成 pending。")
    print("      词缀侧读 out/statdesc.csv，同理：换版本先重跑 dumper raw + parse_statdesc。")
    return 0 if not bad else 1


if __name__ == "__main__":
    raise SystemExit(main())
