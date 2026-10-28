# poe-zh · 繁中改良汉化

不重建整套汉化，只修官方繁中里**读着别扭、容易误读、机制算错**的地方，
并且这套东西能在每个新赛季活下来。

核心原则：**不动原文，只维护一份按 key 索引的覆盖清单。**

---

## 先读这两个

- `REPORT.md` —— 本轮结论：问题分类、真实案例、修正方案、可行性判断
- `DATA-FINDINGS.md` —— 逆向细节：`.datc64` 布局、statdescriptions 格式、可复现流程

---

## 目录

```
├─ REPORT.md              体检报告与修正方案（给人看）
├─ DATA-FINDINGS.md       二进制格式与流程（给下个赛季的自己看）
├─ tools/
│  ├─ dat64.py            .dat/.datc64 解析器，无 schema 依赖
│  ├─ extract_pairs.py    成对抽取 EN/ZH 的 .dat 文本
│  ├─ parse_statdesc.py   解析词缀/技能描述模板（纯文本）
│  ├─ analyze.py          体检：占位符/方向词/术语/简体字/长度
│  ├─ glossary.py         从对照数据反推术语表
│  └─ dumper/             C# 导出器（LibGGPK3 + oo2core）
├─ out/                   全部产物
└─ pairs/ raw_sd/ raw/    原始导出（可随时重建，不必备份）
```

## 一次完整的重跑

```bash
# 1) 导出（.NET 8，需要 oo2core.dll 与 Content.ggpk 路径）
cd tools/dumper && dotnet build
./bin/Debug/net8.0/dumper.exe cmp   "<...>/Content.ggpk"
./bin/Debug/net8.0/dumper.exe pairs "<...>/Content.ggpk" ../../pairs diff_pairs.csv
./bin/Debug/net8.0/dumper.exe raw   "<...>/Content.ggpk" ../../raw_sd statdescriptions

# 2) 抽取
cd ../..
python tools/extract_pairs.py pairs out/pairs.csv
python tools/parse_statdesc.py raw_sd out/statdesc.csv

# 3) 体检
python tools/analyze.py  out/statdesc.csv out/report-statdesc.md
python tools/analyze.py  out/pairs.csv    out/report-ui.md
python tools/glossary.py out/statdesc.csv out/glossary.csv
```

## 关键约定

`key` 不能用行号（每个赛季都会变），用业务主键：

```
statdescriptions/<文件名> : <stat_ids> : <变体条件>      词缀/技能描述
<表名>.datc64 : <行 ID> : <列偏移>                       表格文本
```

覆盖层每条再锁一个原文指纹（`en_hash`），原文一变校验器立刻报警 —— 这是这套东西
不会悄悄烂掉的原因。

## 尚未做

- 写回 `.dat`（需要重打包 bundle）。`metadata/statdescriptions/*.txt` 是纯文本，
  优先级更高、成本更低，建议先走这条。
- 覆盖层与校验器本体（`data/overlay.csv` + `validate.py`）：等 L1/L2 的修法定下来再落。
