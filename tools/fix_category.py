# -*- coding: utf-8 -*-
"""物品类别词错配体检与修正稿生成。

覆盖三类错误（都是「英文类别词 A 被译成中文类别词 B」）：
  R1  Jewellery（飾品：戒指/項鍊/腰帶）→ 被译成「珠寶」（Jewel，插槽宝石）
  R2  Item（物品）→ 被译成「道具」
  R3  Armour（護甲）→ 被译成「防具」

输出：
  out/fix-category.csv      statdesc 侧可改条目
  out/fix-category-ui.csv   UI 表侧可改条目（pairs.csv，需回写对应 .dat）
  out/fix-category-hold.csv 存疑条目（专有名词/词本身两套并存），列出待拍板，不改
"""
import csv, re

SD = "out/statdesc.csv"
PR = "out/pairs.csv"

RE_JEWELLERY = re.compile(r"Jewellery", re.I)
RE_ITEM      = re.compile(r"\bItems?\b", re.I)
RE_ARMOUR    = re.compile(r"\bArmours?\b", re.I)

# 专有名词：箱子名 / 房间名。仍属「Jewellery 被译成珠寶」，只是同一条名字散在多表，
# 必须整批一起改，否则游戏内会出现两个名字。清单见 PROPER_NAMES。
PROPER_NAMES = re.compile(r"Perandus|Forge|Zalatl|jewellery box", re.I)

# 诗意/隐喻文本，指称不明确（可能真指宝石），不机械替换
POETIC_TABLES = {"flavourtext.datc64", "flavourtext.datcl64"}


def go_statdesc():
    rows = list(csv.DictReader(open(SD, encoding="utf-8")))
    fix, hold = [], []
    for r in rows:
        en, zh = r["en"], r["zh"]
        base = {"stat_ids": r["stat_ids"], "file": r["file"], "cond": r["cond"],
                "en": en, "zh_old": zh}

        # R1 Jewellery → 珠寶
        if RE_JEWELLERY.search(en) and "珠寶" in zh and "飾品" not in zh:
            note = "Jewellery=飾品（戒指/項鍊/腰帶），珠寶是 Jewel（插槽寶石）"
            if PROPER_NAMES.search(en):
                note = ("專有名詞，但仍屬 Jewellery→珠寶；兩表（statdesc/achievementitems）須同步改，"
                        "否則遊戲內同一寶箱出現兩個名字")
            fix.append(dict(base, zh_new=zh.replace("珠寶", "飾品"), rule="R1", note=note))

        # R2 Item → 道具
        if RE_ITEM.search(en) and "道具" in zh:
            fix.append(dict(base, zh_new=zh.replace("道具", "物品"), rule="R2",
                            note="同語料 物品 698 : 道具 26，Item 統一為「物品」"))

        # R3 Armour → 防具
        if RE_ARMOUR.search(en) and "防具" in zh:
            fix.append(dict(base, zh_new=zh.replace("防具", "護甲"), rule="R3",
                            note="同語料 護甲 227 : 防具 6；台服 Armour=護甲"))

        # 存疑：Amulet 官方自身两套词（装备名 護身符 / 泛称 項鍊）
        if re.search(r"Amulets?\b", en) and "護身符" in zh:
            hold.append(dict(base, zh_new=zh.replace("護身符", "項鍊"), rule="H1",
                             note="官方分裂：baseitemtypes 用「X 護身符」，泛称用「項鍊」(250:67)。"
                                  "本條在 statdesc 內屬泛称，按主流應改項鍊，待拍板"))

    # 去重（同一条目可能命中多条规则）
    seen, uniq = set(), []
    for x in fix:
        k = (x["stat_ids"], x["en"], x["zh_old"])
        if k in seen:
            continue
        seen.add(k); uniq.append(x)
    return uniq, hold


TBL_NOTE = {
    "currencyitems.datc64": "同表 {Jewellery} 佔位符已譯「飾品」（5 條），本條為實驗性首飾獎勵展示",
    "achievementitems.datc64": "同表 Trinket/Jewellery 類標籤多用「飾品」，本條為少數派",
    "clientstrings.datc64": "Jewellery: 已譯「飾品：」，本條同源",
    "incursionrooms.datc64": "Jewellery Forge 為神廟房間名，房間功能即產出首飾；同表 Omnitect Forge 作「全能冶煉室」",
    "uitalktext.datc64": "台詞比喻「放滿首飾的房間」，指飾品不指寶石",
}


def go_pairs():
    """UI 表侧：Jewellery→珠寶 一律改飾品，唯獨詩意/隱喻文本不動。"""
    rows = list(csv.DictReader(open(PR, encoding="utf-8")))
    # 逐条定制：机械替换后仍需人工定稿的
    OVERRIDE = {("achievementitems.datc64", "5688"): "飾品寶箱"}  # Jewellery Item Chests
    fix, hold = [], []
    for r in rows:
        en, zh = r["en"], r["zh"]
        if not (RE_JEWELLERY.search(en) and "珠寶" in zh and "飾品" not in zh):
            continue
        base = {"table": r["table"], "row": r["row"], "slot": r["slot"],
                "en": en, "zh_old": zh}
        if r["table"] in POETIC_TABLES:
            hold.append(dict(base, zh_new=zh, note="詩意/隱喻文本，指稱不確定，未動"))
            continue
        new = zh.replace("珠寶", "飾品")
        new = OVERRIDE.get((r["table"], r["row"]), new)
        fix.append(dict(base, zh_new=new,
                        note=TBL_NOTE.get(r["table"], "Jewellery=飾品；珠寶是 Jewel（插槽寶石）")))
    return fix, hold


# 反向口径：英文只有 increased/reduced（加法），中文却写成 更多/更少
REV = {
 ("{0}% increased Physical Damage per Frenzy Charge",
  "每一狂怒球造成{0}%更多物理傷害"): "每顆狂怒球增加 {0}% 物理傷害",
 ("{0}% reduced Physical Damage per Frenzy Charge",
  "每一狂怒球造成{0}%更少物理傷害"): "每顆狂怒球減少 {0}% 物理傷害",
 ("Final Map Boss deals increased Damage per stolen Divination Card",
  "每張盜竊命運卡會使最終地圖頭目造成更多傷害"): "每張盜竊命運卡使最終地圖頭目的傷害增加",
 ("Unique Boss gives {0}% increased Experience",
  "傳奇頭目給予 {0}% 更多經驗值"): "傳奇頭目給予的經驗值增加 {0}%",
 ("{0}% increased Unique Items found in Area",
  "區域掉落 {0}% 更多傳奇道具"): "區域內找到的傳奇物品增加 {0}%",
}


def go_rev():
    rows = list(csv.DictReader(open(SD, encoding="utf-8")))
    MUL = re.compile(r"\b(more|less)\b", re.I)
    ADD = re.compile(r"\b(increased|reduced)\b", re.I)
    out, miss = [], []
    for r in rows:
        if not (ADD.search(r["en"]) and not MUL.search(r["en"])):
            continue
        if not re.search("更多|更少", r["zh"]):
            continue
        key = (r["en"], r["zh"])
        new = REV.get(key)
        if new is None:
            miss.append(key); continue
        out.append({"stat_ids": r["stat_ids"], "file": r["file"], "cond": r["cond"],
                    "en": r["en"], "zh_old": r["zh"], "zh_new": new,
                    "note": "increased/reduced 是加法，不可譯「更多/更少」"})
    return out, miss


def dump(path, rows, fields):
    with open(path, "w", encoding="utf-8-sig", newline="") as f:
        w = csv.DictWriter(f, fieldnames=fields)
        w.writeheader()
        for r in rows:
            w.writerow({k: r.get(k, "") for k in fields})


if __name__ == "__main__":
    sf, shold = go_statdesc()
    uf, uhold = go_pairs()
    rev, rmiss = go_rev()

    dump("out/fix-category.csv", sf,
         ["stat_ids", "file", "cond", "en", "zh_old", "zh_new", "rule", "note"])
    dump("out/fix-category-ui.csv", uf,
         ["table", "row", "slot", "en", "zh_old", "zh_new", "note"])
    dump("out/fix-category-hold.csv", shold + uhold,
         ["stat_ids", "table", "row", "slot", "cond", "en", "zh_old", "zh_new", "rule", "note"])
    dump("out/fix-additive-rev.csv", rev,
         ["stat_ids", "file", "cond", "en", "zh_old", "zh_new", "note"])

    import collections
    c = collections.Counter(r["rule"] for r in sf)
    print("fix-category.csv     %d 条  %s" % (len(sf), dict(c)))
    print("fix-category-ui.csv  %d 条" % len(uf))
    print("fix-category-hold.csv %d 条（存疑，未改）" % (len(shold) + len(uhold)))
    print("fix-additive-rev.csv %d 条（未覆盖 %d）" % (len(rev), len(rmiss)))
    for m in rmiss:
        print("   !", m[0], "|", m[1])
