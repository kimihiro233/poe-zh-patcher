# -*- coding: utf-8 -*-
"""零编辑往返自检（B6）。

判据只有一条：**没有编辑，就不许改字节。**

以前 `patch_bytes` 的池尾对齐补位写在「有没有编辑」判断之前，于是 410 份 .datc64 里
有 160 份在一条编辑都没有时也会变长（20 份奇数长度 +1，140 份池尾不是 00 00 时 +2）。
后果不是「文件坏了」，而是更隐蔽的一种：长度与 sha 都变了，上游 patcher 会把一份
干净原文判成「已打过补丁」，之后所有状态推断都建立在错误前提上。

两条断言：
1. `patch_bytes(raw, [])` 必须逐字节等于 raw；
2. 一条「新旧相同」的编辑（会被判成 skip）也必须逐字节等于 raw ——
   这条比第 1 条重要，因为它走的是完整路径：补位已经执行过了。

语料是 pairs/zh/*.datc64（游戏数据，不入库）。缺失时 SKIP 并说明原因，
不静默通过 —— 静默跳过的测试比没有测试更糟。

用法：python tools/roundtrip_test.py [--dir=pairs/zh]
"""
import glob
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from datwrite import patch_bytes, load_raw  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def one_same_edit(raw, name):
    """找一条「新旧相同」的编辑；找不到就返回 None（这个文件没有可读字符串槽位）。"""
    t = load_raw(raw, name)
    for row in range(t.row_count):
        for rel in t.string_slots():
            try:
                cur = t.string_field(row, rel)
            except Exception:
                continue
            if cur:
                return [(row, rel, cur, cur)]
    return None


def main():
    dirs = [os.path.join(ROOT, "pairs", "zh"), os.path.join(ROOT, "raw")]
    for a in sys.argv[1:]:
        if a.startswith("--dir="):
            dirs = [a[6:]]

    # 两套语料各覆盖一条分支：pairs/zh 的池尾都是 00 00，只有奇数长度会被 +1；
    # raw/ 里有一批池尾不是 00 00 的原文，那才是 +2 那一支。少一套就有一支没被验到。
    files = []
    for d in dirs:
        files += sorted(glob.glob(os.path.join(d, "*.datc64")))
    if not files:
        print("SKIP：%s 下没有 .datc64（游戏数据不入库，本地跑前先 extract_pairs.py）"
              % " / ".join(dirs))
        return 0

    bad = []
    odd = 0
    checked_same = 0
    for f in files:
        raw = open(f, "rb").read()
        if not raw:
            continue
        name = os.path.basename(f)
        if len(raw) % 2:
            odd += 1

        try:
            out, rep = patch_bytes(raw, [], name)
        except Exception as e:
            bad.append((name, "零编辑报错：%s" % e))
            continue
        if out != raw:
            bad.append((name, "零编辑改了字节：%d -> %d" % (len(raw), len(out))))
            continue
        if rep.get("appended_bytes") != 0 or rep.get("new_len") != len(raw):
            bad.append((name, "零编辑报告不符：%r" % rep))

        same = one_same_edit(raw, name)
        if same is None:
            continue
        checked_same += 1
        try:
            out2, _ = patch_bytes(raw, same, name)
        except Exception as e:
            bad.append((name, "同文编辑报错：%s" % e))
            continue
        if out2 != raw:
            bad.append((name, "同文编辑改了字节：%d -> %d" % (len(raw), len(out2))))

    print("往返自检：%d 个文件（奇数长度 %d），同文编辑用例 %d 个"
          % (len(files), odd, checked_same))
    if bad:
        print("FAIL：%d 个不符合" % len(bad))
        for name, why in bad[:12]:
            print("  - %s：%s" % (name, why))
        return 1
    print("OK：零编辑与同文编辑都逐字节回到原文")
    return 0


if __name__ == "__main__":
    sys.exit(main())
