# 依赖账本

> 本文件由 `python tools/deps_check.py --doc` 从 `tools/lib/deps.json` 生成，**不要手改**。
> 改依赖：先改 json，再 `--fix` 写回哈希，最后 `--doc` 重新生成，`--check` 必须绿。

## 非 NuGet 依赖

| 文件 | 版本 | 许可 | 可再分发 | 用途 |
|---|---|---|---|---|
| `tools/lib/oo2core.dll` | Oodle 2 core（Kraken 解码器，版本号未内嵌） | proprietary | **否** | 解压 Bundles2 里的 Oodle 压缩块。LibBundle3 通过同名原生库加载它 |
| `tools/lib/LibDat2.dll` | 0.16.2 | AGPL-3.0-or-later | 是 | 读写 .datc64。只有 tools/dumper 用（HintPath 直接引 dll，没走 NuGet） |
| `tools/lib/DatDefinitions.json` | 无版本字段 | AGPL-3.0-or-later | 是 | LibDat2 解析 .datc64 时需要的列类型定义 |
| `tools/lib/schema.min.json` | 7 | AGPL-3.0-or-later | 是 | 研究阶段的表结构参照 |
| `tools/lib/schema-poe1.min.json` | 7 | AGPL-3.0-or-later | 是 | 研究阶段的表结构参照 |
| `tools/pack/bundle.gz` | 随 VERSION 走（构建时写入） | proprietary | 是 | 补丁器的全部写入数据，内嵌进 exe |

| 文件 | 字节 | sha256 |
|---|---|---|
| `tools/lib/oo2core.dll` | 645,400 | `4758c7901cbd68df1848b7afed48c1975ad59b509886c21b87c0e91d717446b5` |
| `tools/lib/LibDat2.dll` | 59,904 | `23e3d643d20882c11a5f21519770e7f43b6748f4a80826ea8e110e7dab74b48b` |
| `tools/lib/DatDefinitions.json` | 203,338 | `311560f229654328db584d1b4a3306fd38ad5436c10a0eb76cbc95420f1ba07a` |
| `tools/lib/schema.min.json` | 1,453,300 | `25a07746db3225d77b30b1960c3d5fe7e926499e7067829d194aaa91b4a9fc09` |
| `tools/lib/schema-poe1.min.json` | 1,453,300 | `ca95dfac297688f3363567a1db37ee676af338e2de4428e01e4346a6fed2f6d4` |
| `tools/pack/bundle.gz` | 2,323,456 | `（每次构建都变，不钉）` |

## NuGet 包

| 包 | 版本 | 许可 | 用途 |
|---|---|---|---|
| `LibGGPK3.LibBundledGGPK3` | `2.7.5` | AGPL-3.0-or-later | 把 BundledGGPK 当 Stream 用，是「原地覆盖 + 逐块记原字节」的前提 |
| `LibGGPK3.LibBundle3` | `2.7.5` | AGPL-3.0-or-later | bundle 索引与文件读写 |
| `LibGGPK3.LibGGPK3` | `2.7.5` | AGPL-3.0-or-later | ggpk 容器结构（传递依赖） |
| `aianlinb.SystemExtensions` | `1.8.0` | MIT | Span/Stream 扩展（传递依赖） |

版本由 7 份 `packages.lock.json` 钉住，CI 用 locked mode 还原：锁文件说 2.7.5，
解析出 2.8.0 就直接失败，不会「悄悄装个新的」。

## 逐条说明

- **`oo2core`** —— **不可再分发**。当前这份随仓库入库，是因为 build 与单文件 exe 都需要它。合规做法是从仓库移出、由使用者从自己的游戏目录复制；代价是新克隆不能一键构建。等作者拍板。
- **`libdat2`** —— AGPL：分发二进制时要提供对应源码。上游地址即源码，release 的 SBOM 里登记了版本与哈希。
- **`datdefinitions`** —— **当前没有任何代码引用它**（全仓库 grep 为 0）。属于 v0.1.0 的研究残留，留着是为了以后重做 .dat 时有参照。
- **`schema`** —— **当前没有任何代码引用它**。与 schema-poe1.min.json 内容不同但字节数相同，两者都不是构建输入。
- **`schema-poe1`** —— **当前没有任何代码引用它**。
- **`bundle`** —— 派生自游戏文本，属灰色地带但这是整个项目的前提。逐条改动可从 releases/<版本>/ 的清单复核。
- **`LibGGPK3.LibBundledGGPK3`** —— **AGPL 是本项目最重的一条许可约束**：exe 静态链接了它，所以分发 exe 时整个作品都受 AGPL 约束，必须提供源码。仓库目前没有 LICENSE 文件 —— 这条要作者确认。
- **`aianlinb.SystemExtensions`** —— MIT 与 AGPL 兼容，不额外增加约束。

## 不可再分发的依赖

`tools/lib/oo2core.dll` 的版权归 RAD Game Tools / Epic Games，仓库里这份是能构建的前提，但**不能随发布包分发**。

当前处理：留在仓库里（作者尚未拍板）。要合规只有一条路——移出仓库并 `.gitignore`，
构建时从使用者自己的游戏目录复制，缺文件时报「请从游戏目录复制 oo2core.dll」。
代价是新克隆不能一键构建，收益是仓库干净。
