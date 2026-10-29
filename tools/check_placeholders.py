# -*- coding: utf-8 -*-
"""查译文里的占位符漏失 —— 「机率整段丢了」那一类还有多少。

来由：deepwater 三个块英文是 `{0}% chance for ... instead`，繁中写成「機會石改為掉落古變石」，
数值和百分号一起没了，玩家把概率读成必然。`analyze.py` 的 S2 能报出来（201 条），
但把「换行丢了」和「数值丢了」混在一张表里，分不出轻重。本脚本只做一件事：
按**后果**给每条定级，让需要人看的那一档缩到个位数。

分级（按严重度取最高的一条）：
  MISS_NUM   `{n}` 整段丢失        —— 句子里没有数字，机制必读错
  MISS_HASH  `#` 丢失              —— 数值无处填入
  MISS_PCT   `%` 丢失              —— 「增加 20」这种读法
  REWRITE    编号在、写法不同      —— `{0:+d}` → `{0}`，显示差一个正号
  EXTRA      译文多出占位符        —— 可能凭空多一个数
  MISS_NL    `\\n` 丢失            —— 只影响排版

用法：
    python tools/check_placeholders.py            # raw_sd -> out/placeholder-check.{csv,md}
"""
from __future__ import annotations

import csv
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from patch_sd import SdFile  # noqa: E402

TOKEN_RE = re.compile(r"\{\s*[-+]?\d[^}]*\}|#|%|\\n")
NUM_RE = re.compile(r"\{\s*([-+]?\d)[^}]*\}")
ORDER = ["MISS_NUM", "MISS_HASH", "MISS_PCT", "REWRITE", "EXTRA", "MISS_NL"]
RANK = {k: i for i, k in enumerate(ORDER)}

# 已判过的条目：(文件, 块号) -> 结论。写在这里，重跑不会重新变成「待判」。
# 本脚本扫的是 raw_sd（官方原文），所以「已修」的意思是：缺陷仍在原文里，
# 但已被某个修正批次覆盖，产物里已经是对的。要复核产物本身，把 root 指到 out/patch-sd*。
VERDICTS = {
    # fix-placeholder：机率/数值整段丢失
    ("stat_descriptions.txt", 6246): "已修：補回「有 {0}% 機率」（deepwater 古變石）",
    ("stat_descriptions.txt", 6247): "已修：同上（deepwater 混沌→神聖）",
    ("stat_descriptions.txt", 6249): "已修：同上（deepwater 第三塊）",
    ("stat_descriptions.txt", 4608): "已修：句子被逗號截斷，補回 {0}%（最大元素抗性）",
    ("stat_descriptions.txt", 9216): "已修：{0} 寫死成 1（瓦爾密殿張數）",
    ("atlas_stat_descriptions.txt", 733): "已修：補回 {0}%（機會石→傳奇）",
    ("active_skill_gem_stat_descriptions.txt", 312): "已修：編號錯位 {1}→{0}（投射物返回）",
    ("skill_stat_descriptions.txt", 40): "已修：編號錯位 {1}→{0}（陷阱數）",
    ("skill_stat_descriptions.txt", 218): "已修：第二句被併掉，補回「傳送失去 {1} 強化」",
    ("skill_stat_descriptions.txt", 429): "已修：編號錯位 {0}→{1}（秘能波動魔力恢復）",
    ("skill_stat_descriptions.txt", 1272): "已修：{0} 寫死成 5%（盛怒之斬，兩個變體）",
    ("skill_stat_descriptions.txt", 1364): "已修：{0} 寫死成 10%（幻影投射物）",
    ("chest_stat_descriptions.txt", 7): "已修：編號錯位 {1}→{0}（守衛群數，兩個變體）",
    ("minion_skill_stat_descriptions.txt", 259): "已修：補回 %（奪魂鐮，兩個變體）",
    ("passive_skill_stat_descriptions.txt", 98): "已修：補回 %（法師剋星 +1%）",
    ("gem_stat_descriptions.txt", 763): "已修：補回 {0}%（觸發機率）",
    ("leaguestone_stat_descriptions.txt", 65): "已修：{0} 寫死成 1 座（額外神殿數）",
    # fix-typo：占位符括号写错，比丢数值更彻底——游戏里会原样显示 {0]
    ("stat_descriptions.txt", 6796): "已修：右括號寫成 ]，fix-typo 批次改回 {0}",
}


def tokens(s: str):
    return TOKEN_RE.findall(s or "")


def nums(toks):
    return {NUM_RE.sub(r"{\1}", t) for t in toks if t.startswith("{")}


def classify(en: str, zh: str):
    """返回 (类别, 说明)；无问题返回 (None, "")。"""
    te, tz = tokens(en), tokens(zh)
    ne, nz = nums(te), nums(tz)
    miss, extra = sorted(ne - nz), sorted(nz - ne)
    if miss:
        return "MISS_NUM", "缺 %s" % " ".join(miss)
    if "#" in te and "#" not in tz:
        return "MISS_HASH", "缺 #"
    if "%" in te and "%" not in tz:
        return "MISS_PCT", "缺 %"
    if extra:
        return "EXTRA", "多 %s" % " ".join(extra)
    if "\\n" in te and "\\n" not in tz:
        return "MISS_NL", "缺换行"
    if [t for t in te if t.startswith("{")] != [t for t in tz if t.startswith("{")]:
        return "REWRITE", "写法不同 %s / %s" % (
            " ".join(t for t in tz if t.startswith("{")),
            " ".join(t for t in te if t.startswith("{")))
    return None, ""


def scan(root: str):
    rows = []
    for fn in sorted(os.listdir(root)):
        if not fn.endswith(".txt"):
            continue
        sf = SdFile(os.path.join(root, fn))
        short = fn.replace("metadata_statdescriptions_", "")
        for bi, (ids, langs) in enumerate(sf.blocks):
            en = next((vs for lang, vs in langs if lang == ""), [])
            tc = next((vs for lang, vs in langs if lang == "Traditional Chinese"), [])
            for i, (no, cond, txt, _h) in enumerate(tc):
                if i >= len(en):
                    continue
                kind, why = classify(en[i][2], txt)
                if not kind:
                    continue
                rows.append({"file": short, "block": bi, "stat_ids": ids, "variant": i,
                             "kind": kind, "why": why, "cond": cond,
                             "en": en[i][2], "zh": txt,
                             "verdict": VERDICTS.get((short, bi), "")})
    rows.sort(key=lambda r: (RANK[r["kind"]], r["file"], r["block"]))
    return rows


def write_csv(rows, path):
    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    with open(path, "w", encoding="utf-8-sig", newline="") as f:
        w = csv.DictWriter(f, fieldnames=["file", "block", "stat_ids", "variant", "kind",
                                          "why", "cond", "en", "zh", "verdict"])
        w.writeheader()
        w.writerows(rows)


def write_md(rows, path, root):
    cnt = {}
    for r in rows:
        cnt[r["kind"]] = cnt.get(r["kind"], 0) + 1
    L = ["# 译文占位符漏失（按后果分级）", "",
         "源：`%s`；判据见 `tools/check_placeholders.py` 头注释。" % root, "",
         "| 类别 | 含义 | 条数 |", "|---|---|---|",
         "| MISS_NUM | `{n}` 整段丢失，机制必读错 | %d |" % cnt.get("MISS_NUM", 0),
         "| MISS_HASH | `#` 丢失，数值无处填 | %d |" % cnt.get("MISS_HASH", 0),
         "| MISS_PCT | `%%` 丢失 | %d |" % cnt.get("MISS_PCT", 0),
         "| REWRITE | 编号在、写法不同（`{0:+d}`→`{0}`） | %d |" % cnt.get("REWRITE", 0),
         "| EXTRA | 译文多出占位符 | %d |" % cnt.get("EXTRA", 0),
         "| MISS_NL | 只丢换行，排版 | %d |" % cnt.get("MISS_NL", 0),
         "| 合计 | | %d |" % len(rows), ""]
    for kind in ("MISS_NUM", "MISS_HASH", "MISS_PCT"):
        sub = [r for r in rows if r["kind"] == kind]
        if not sub:
            continue
        L += ["## %s（%d 条）" % (kind, len(sub)), ""]
        for r in sub:
            L += ["### %s · block#%d · `%s` · %s" % (r["file"], r["block"], r["stat_ids"], r["why"]),
                  "", "- EN `%s`" % r["cond"], "  - %s" % r["en"],
                  "- ZH `%s`" % r["cond"], "  - %s" % r["zh"],
                  "- 判定：%s" % (r["verdict"] or "待判"), ""]
    L += ["## 其余（REWRITE / EXTRA / MISS_NL）", "",
          "| 文件 | 块 | stat_ids | 类别 | 说明 |", "|---|---|---|---|---|"]
    for r in rows:
        if r["kind"] in ("MISS_NUM", "MISS_HASH", "MISS_PCT"):
            continue
        L.append("| %s | %d | `%s` | %s | %s |" % (r["file"], r["block"], r["stat_ids"], r["kind"], r["why"]))
    open(path, "w", encoding="utf-8", newline="\n").write("\n".join(L) + "\n")


def main():
    root = sys.argv[1] if len(sys.argv) > 1 else "raw_sd"
    csv_path = sys.argv[2] if len(sys.argv) > 2 else "out/placeholder-check.csv"
    rows = scan(root)
    write_csv(rows, csv_path)
    write_md(rows, os.path.splitext(csv_path)[0] + ".md", root)
    cnt = {}
    for r in rows:
        cnt[r["kind"]] = cnt.get(r["kind"], 0) + 1
    todo = [r for r in rows if r["kind"] in ("MISS_NUM", "MISS_HASH", "MISS_PCT") and not r["verdict"]]
    print("占位符不一致：%d（%s），待判 %d"
          % (len(rows), " / ".join("%s %d" % (k, cnt[k]) for k in ORDER if k in cnt), len(todo)))
    print("-> %s" % csv_path)
    return 1 if todo else 0


if __name__ == "__main__":
    raise SystemExit(main())
