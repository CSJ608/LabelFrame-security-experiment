# Lealone 替换评估

评估日期：2026-10-26。结论：**不推荐为当前本地打印队列替换 SQLite**，继续已有分表与离线反向迁移方案。不是宣称 Lealone 性能差或 SQLite 在所有场景更快；本轮为官方文档/源码适配评估，没有安装、运行或进行两库实机 A-B 测试，也未验证宣传所指的具体历史榜单。

## 核查版本与来源

- 主仓库：`lealone/Lealone`，核查 HEAD `b6c7cc654109005cbc0ae6bde062b4a77dd91c70`。当前 README 以通用智能体为定位，构建要求 JDK 21+，示例是 8.0.0-SNAPSHOT；不能把历史数据库介绍的体积/环境要求直接当成当前发行规格。[README](https://github.com/lealone/Lealone/blob/b6c7cc654109005cbc0ae6bde062b4a77dd91c70/README.md)
- 基准仓库：`lealone-plugins/lealone-bench`，main `8d33f0899f9836fe844263171c5eb2431ea27d1e`。[基准入口](https://github.com/lealone-plugins/lealone-bench/tree/8d33f0899f9836fe844263171c5eb2431ea27d1e)
- 历史数据库介绍列出全链路异步、AOSE/AOTE、行列混合存储及兼容 MySQL/PostgreSQL 的插件；这些是有价值的 OLTP 架构方向。[数据库介绍](https://github.com/lealone/Lealone-Docs/blob/fc3ed25e2bf7de01cd564d52dc38128b99428830/应用文档/before-ai/README-DB.md)

## 为什么“远超 SQLite”不足以决定本项目换库

核查的单行更新用例为 `pk int primary key, f1 int`，10000 行中随机更新整数；公共基类默认 16 线程、自动提交，SQLite 连接设置 WAL + synchronous=NORMAL。这不是 LabelFrame 的一台打印机串行处理、100～200KB 指令载荷、每张领取/完成持久化、恢复/幂等验证负载。[用例](https://github.com/lealone-plugins/lealone-bench/blob/8d33f0899f9836fe844263171c5eb2431ea27d1e/src/main/java/com/lealone/plugins/bench/cs/write/singleRowUpdate/SingleRowUpdateBTest.java)、[参数与 SQLite 连接](https://github.com/lealone-plugins/lealone-bench/blob/8d33f0899f9836fe844263171c5eb2431ea27d1e/src/main/java/com/lealone/plugins/bench/cs/ClientServerBTest.java)

当前日志同步源码默认选择 periodic；其代码允许在 fsync 前完成响应，instant 才等待同步。`log_sync_period` 当前只是兼容性的睡眠参数，新日志会唤醒线程，**不能把名字误读为固定隔若干秒才刷盘**。基准资源中的 lealone-test.sql 也指定 periodic；另一个启动器引用仓库树中未见的 lealone-bench.yaml，因此不能认定每个历史性能结果采用同一配置，更不能断言宣传是造假。[日志实现](https://github.com/lealone/Lealone/blob/b6c7cc654109005cbc0ae6bde062b4a77dd91c70/lealone-aote/src/main/java/com/lealone/transaction/aote/log/LogSyncService.java)、[测试配置](https://github.com/lealone-plugins/lealone-bench/blob/8d33f0899f9836fe844263171c5eb2431ea27d1e/src/main/resources/lealone-test.sql)、[另一启动器](https://github.com/lealone-plugins/lealone-bench/blob/8d33f0899f9836fe844263171c5eb2431ea27d1e/src/main/java/com/lealone/plugins/bench/start/LealoneBenchTestServer.java)

若未来认真进入选型，必须固定版本、核对两库实际持久性配置，使用 instant/相当保证、相同事务边界与真实队列负载，测单写者 p50/p95/p99、重启恢复与故障注入，而非直接沿用吞吐倍数。当前未做此测试，所以既不承诺 Lealone 更快，也不判定它更慢。

## 项目适配成本

| 维度 | 对 LabelFrame 的影响 |
|---|---|
| 接入 | 所查原生客户端是 Java/JDBC；未确认可直接替换 Microsoft.Data.Sqlite 的官方 ADO.NET 嵌入驱动。兼容数据库协议插件是一条可能路径，但仍需另验 .NET 驱动、参数、BLOB、事务/取消、异常与恢复契约，不能把协议宣传当成已经兼容。 |
| 部署 | 推断采用 Java 服务/桥接会引入 JVM、生命周期、连接故障、打包与运维面；与当前库随进程运行不同。不能只把小 jar 大小当作完整部署成本。 |
| 多宿主 | Core 作业存储被 Windows/Linux 宿主以及 Android 宿主复用；Java 桌面运行要求不等于 Android 集成已经可用。只替换服务器也不能消除本地 Worker 的既有热点。此处只评估架构，不修改 Android 应用。 |
| 迁移 | 换库仍须迁移历史作业、幂等键、状态、指令及逆向导出，并重新验证整条作业状态机；不会自然免除本轮正在处理的迁移/降级问题。 |
| 许可 | 当前 LICENSE 区分 H2 来源部分与 SSPL v1 来源部分，不是直接等同于本项目 MIT 的宽松依赖。若拟引入需单独核查分发/服务使用义务，不在本评估作法律兼容结论。[许可证原文](https://github.com/lealone/Lealone/blob/b6c7cc654109005cbc0ae6bde062b4a77dd91c70/LICENSE.md) |

当前源码与历史文档方向有变化、示例采用 snapshot，增加了钉版和支持范围核查的工作；不以星数或猜测维护人数作为可靠性定论。

## 决定与后续

我们已有隔离 A-B 证据说明，在不更换 SQLite、不改变逐张事务的条件下，状态/载荷分离能显著减少状态更新耗时；相比跨运行时换库，当前方向的改动面更小、证据更直接。详见 [分表方案](job-storage-split-proposal.md)。磁盘提交仍有噪声，不能由此保证固定整批收益。

用户授权：“如果评估结果不是推荐使用，可以接受降级时先停机运行反向迁移工具，再安装旧版。”本评估不推荐替换，因此该条件已满足；继续实现 SQLite 分表、原子正向迁移和离线反向迁移工具，不再等待同一降级决策。仍仅本地测试/提交，禁止真实库操作、安装部署或远端变更。
