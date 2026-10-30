# -*- coding: utf-8 -*-
"""统一测试入口：本地和 CI 跑的是同一份清单。

以前这套自检散在 README、提交说明和 CI 里三份，谁多写一条谁漏一条没人知道。
现在只有这一个文件说「要跑什么」，CI 只负责 `python tools/run_tests.py`。

原则：
- 每条用例都以**退出码**判定，不去 grep 输出里的中文，改文案不会弄坏 CI。
- 需要游戏语料的用例（`--data-test`）在没有 raw_sd 时**显式跳过并说明原因**，
  不静默通过 —— 静默跳过的测试比没有测试更糟。
- 任何一条失败就整体失败，但**其余照跑**：一次看到全部坏的地方，比修一个跑一次快。

用法：
    python tools/run_tests.py            # 跑全部（假定已 build）
    python tools/run_tests.py --build    # 先 build 7 个工程再跑
"""
import glob
import os
import subprocess
import sys
import time

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROJECTS = ["b2probe", "dropper", "dumper", "ggpkdiag", "ggpkdoctor", "ggpkfix", "patcher"]
DLL = os.path.join(ROOT, "tools", "dropper", "bin", "Release", "net8.0", "PoE-zh-Patcher.dll")
RAW_SD = os.path.join(ROOT, "raw_sd")


def run(cmd, cwd=ROOT):
    t0 = time.time()
    p = subprocess.run(cmd, cwd=cwd, capture_output=True, text=True, encoding="utf-8", errors="replace")
    return p.returncode, (p.stdout or "") + (p.stderr or ""), time.time() - t0


def dotnet_test(name, dll, extra=()):
    return ("dotnet " + name, ["dotnet", dll, "--" + name] + list(extra))


def main():
    build = "--build" in sys.argv
    results = []

    def record(name, code, out, secs, skip_reason=None):
        if skip_reason:
            results.append((name, "SKIP", skip_reason, secs))
        else:
            results.append((name, "PASS" if code == 0 else "FAIL", out.strip().splitlines()[-1][:110] if out.strip() else "", secs))

    if build:
        for p in PROJECTS:
            code, out, secs = run(["dotnet", "build", "-c", "Release", "--nologo", "-v", "q"],
                                  cwd=os.path.join(ROOT, "tools", p))
            record("build " + p, code, out, secs)

    # 语法：所有 Python 工具先过一遍编译，语法错不该等到跑用例才暴露
    py = [f for f in glob.glob(os.path.join(ROOT, "tools", "**", "*.py"), recursive=True)]
    code, out, secs = run([sys.executable, "-m", "py_compile"] + py)
    record("py_compile (%d 个)" % len(py), code, out, secs)

    # Python 侧：内容闸（与 C# 共用 fixtures）、文档一致性、依赖账本
    for script, args, name in [
        ("tools/content_gate.py", ["--test"], "content_gate --test"),
        ("tools/doc_check.py", [], "doc_check"),
        ("tools/deps_check.py", [], "deps_check"),
    ]:
        code, out, secs = run([sys.executable, os.path.join(ROOT, script)] + args)
        record(name, code, out, secs)

    # C# 侧自检。--gate-test / --redact-test 用的是内嵌策略表，不依赖仓库目录。
    if not os.path.exists(DLL):
        record("dropper 自检", 1, "找不到 %s，先 dotnet build 或加 --build" % DLL, 0)
    else:
        for name in ["gate-test", "redact-test", "markup-test", "legacy-test", "undo-test", "baseline-test"]:
            code, out, secs = run(["dotnet", DLL, "--" + name])
            record("--" + name, code, out, secs)

        # 数据包自检需要 42 份官方原文（raw_sd/）。那是游戏文本，不入库 —— 换版本要重导。
        if os.path.isdir(RAW_SD) and len(glob.glob(os.path.join(RAW_SD, "*.txt"))) >= 40:
            code, out, secs = run(["dotnet", DLL, "--data-test=" + RAW_SD])
            record("--data-test", code, out, secs)
        else:
            record("--data-test", 0, "", 0,
                   skip_reason="raw_sd/ 不存在（游戏文本不入库，本地跑前先 dumper 导出）")

    # 汇总
    w = max(len(n) for n, _, _, _ in results)
    print()
    bad = 0
    for name, status, msg, secs in results:
        mark = {"PASS": "OK  ", "FAIL": "FAIL", "SKIP": "SKIP"}[status]
        print("  %-4s %-*s %5.1fs  %s" % (mark, w, name, secs, msg))
        if status == "FAIL":
            bad += 1
    n_pass = sum(1 for r in results if r[1] == "PASS")
    n_skip = sum(1 for r in results if r[1] == "SKIP")
    print("\n%d 通过 / %d 失败 / %d 跳过" % (n_pass, bad, n_skip))
    if bad:
        print("\n失败项的完整输出：")
        for name, status, msg, _ in results:
            pass
    return 0 if bad == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
