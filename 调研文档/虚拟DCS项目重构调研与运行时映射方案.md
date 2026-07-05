# 虚拟DCS项目重构调研与运行时映射方案

调研日期：2026-04-23

## 1. 结论先行

这个项目当前**不是功能不够**，而是“工程模型、运行时内核、实时数据、工况文件、在线下装、调试接口、UI/通信”长期堆叠在一起，导致代码已经进入“**能力完整，但边界失控**”的阶段。

从代码和可替代 runtime 的匹配度看，我的建议是：

1. **首选路线**：保留现有业务语义和功能块资产，基于 **C# / .NET 8** 做“分层重构 + 新运行时内核替换”。
2. **战略备选路线**：如果希望把运行时尽量映射到现成开源控制 runtime，优先评估 **Eclipse 4diac IDE + FORTE**，它与现有“功能块实例 + 连线 + 设备/资源 + 在线部署/监控”的模型最接近。
3. **不建议作为主路线**：直接迁移到 **OpenPLC**。它更像 PLC 扫描周期运行时，不像你们当前这种“多 DPU、图式功能块、跨块/跨控制器连线、在线差量下装”的虚拟 DCS 运行时；而且 OpenPLC v3 已在 **2026-04-04** 官方归档并停止维护。

一句话概括：

**短中期最现实的方案是“保工程模型，换运行时内核”；长期标准化平台路线才考虑 4diac 映射。**

---

## 2. 本次调研基于哪些代码事实

### 2.1 方案规模

当前解决方案是一个较老的 .NET Framework 多工程方案，核心项目包括：

- `DCS/`：DCS 顶层流程、工况加载/保存、DPU 初始化、在线下装
- `DCSBase/`：`Command`、`Wire`、插件、动态功能块构建
- `DCSCommon/`：`Function`、`Point`、`Pin`、接口和数据类型抽象
- `RTD/`：实时数据、类型系统、订阅刷新、主从 RTD
- `Function/`：功能块库
- `TDK.Core.DAL/`、`ProjectManager/`：工程/组态数据库模型
- `Simulator/`：WinForms 主程序和对外接口

### 2.2 关键大类现状

本次抽查到的核心类规模如下：

| 文件 | 行数 | 主要职责 |
| --- | ---: | --- |
| `DCS/Dcs.cs` | 4173 | 工程初始化、工况读写、状态机、下装、批量读写、性能补丁 |
| `DCS/Dpu.cs` | 2022 | DPU 周期执行、初始化、序列化/反序列化、DPU 下装 |
| `DCSBase/Command.cs` | 2462 | 功能块实例、Pin 同步、连线执行、反射/缓存优化 |
| `RTD/RTD.cs` | 3717 | 主从/克隆 RTD、订阅、通信聚合、序列化、快路径 |
| `RTD/PointManage.cs` | 3759 | SID/FSID、内存槽、Point/Pin 连接、快读快写、订阅刷新 |
| `RTD/TypeManage.cs` | 1474 | 类型解析、偏移计算、反射注册 |
| `DCS/Operation.cs` | 988 | 数据库工程模型导入、工况/指令序列化 |

### 2.3 当前已具备的能力

从代码看，项目已经完整支持了用户描述的核心能力：

- **点/功能块实例管理**
  - `DCS/Operation.cs`：`InitPointByDatabase()`、`InitFCByDatabase()`
  - `DCS/Dpu.cs`：`InitOperationStart(InitOpType.InitPoint/InitCommand)`
- **读写与调试**
  - `RTD/RTD.cs`、`RTD/SubscribeManage.cs`
  - `DCS/Dcs.cs` 中存在大量点值快路径缓存与批量读写优化
- **连线逻辑**
  - `DCSBase/Wire.cs`：`Transmit()`
  - `RTD/PointManage.cs`：`ConnectPointToPin()`、`DisconnectPointToPin()`
  - `DCSBase/Command.cs`：`Execute()`、`ReconnectWires()`
- **工况读写**
  - `DCS/Dcs.cs`：`LoadFile()`、`SaveDsc()`
  - `DCS/Dpu.cs`：`SerializeOperationStart()`
- **DPU 初始化和在线下装**
  - `DCS/Dcs.cs`：`LoadDB()`、`LoadDpuByDB()`、`DownLoad()`
  - `DCS/Dpu.cs`：`DownLoad()`

### 2.4 现有工程模型

数据库模型比较清晰，已经天然分成“工程/组态层”和“运行时层”：

- `Cfg_VarSystem`：点定义，包含 `Name`、`DataType`、`DefaultValue`、`ForceValue`、量程、报警相关信息
- `Cld_FCBlock`：功能块实例，包含 `AlgName`、`FunctionName`、`Sequence`、图纸/控制器归属
- `Cld_FCInput` / `Cld_FCOutput`：Pin 到 Point/Pin 的连线关系
- `Cld_FCParameter`：块参数

这意味着：**工程模型是可保留资产，真正该换的是运行时承载方式。**

### 2.5 已暴露的技术债

代码里已经有不少“为了性能和兼容性后补的修复/补丁”，说明系统本身在逼近维护上限：

- 热路径逻辑和业务逻辑混在一个类里
- 大量反射、`Activator.CreateInstance()`、动态插件装载
- `BinaryFormatter` 仍存在于 `DCS/Operation.cs`
- 工程和工况采用私有二进制格式 `.wrk/.prj`
- 旧式集合广泛存在：`Hashtable`、`ArrayList`
- 方案仍锁定在 **.NET Framework 4.7.2 / x86**
- 数据访问层仍依赖老版本 NHibernate / MySQL 驱动
- 未发现有效自动化测试

---

## 3. 为什么现在这套代码“不够精简清晰和稳定”

### 3.1 根因不是“类太大”，而是“边界错位”

当前很多类同时承担了三种责任：

1. **领域责任**：例如 DPU、Point、Function Block、Wire
2. **基础设施责任**：序列化、数据库读取、插件装载、日志、缓存
3. **性能责任**：快路径、FSID 缓存、批量分桶、反射委托缓存

这会带来典型后果：

- 改一个业务语义，容易破坏序列化或快路径
- 修一个性能问题，容易把连线或工况兼容逻辑卷进去
- 很难为单一职责写测试
- 很难替换底层存储或运行机制

### 3.2 运行时热路径依赖太多“结构外知识”

例如当前执行链路里，块执行依赖：

- 反射字段
- Pin 偏移
- TypeManage 的类型表
- RTD 的 SID/FSID 翻译
- Wire 的连接状态
- 主从 RTD 路由

这类系统可以跑得很快，但前提是“内核小而稳定”。现在的问题是这些机制散落在多个超大类里，而且还和保存/加载、首次运行、在线下装等流程强耦合，所以稳定性会越来越难保证。

### 3.3 当前 RTD 更像“自研内存对象系统”，不是简单实时数据库

这点很重要。现在的 RTD 不只是值表，它还承载了：

- 类型系统
- 对象布局
- Point 和 Pin 的物理连接
- 主从 RTD 聚合
- 工况序列化
- 订阅刷新
- 强制/调试读写

所以不能简单用 Redis、Valkey、IoTDB 直接替换 RTD 热路径。正确做法是：

- **热路径**仍在进程内运行时内核里
- 外部数据库只承担“持久化 / 历史 / 控制面 / 配置分发”

---

## 4. 我建议的目标架构

## 4.1 分成六层

### A. 工程模型层

负责导入和维护当前数据库中的工程定义：

- 点定义
- 块定义
- 块实例
- Pin
- 连线
- 周期/任务分组
- 控制器/DPU 拓扑

这一层只关心“**工程长什么样**”，不关心“**每周期怎么跑**”。

### B. 编译/规划层

把工程模型编译成一个不可变的运行计划 `RuntimePlan`：

- `PointDefinition[]`
- `BlockDefinition[]`
- `BlockInstance[]`
- `Connection[]`
- `TaskGroup[]`
- `DeploymentUnit[]`

这一层相当于把现在散落在 `InitOperation`、`TypeManage`、`Command` 里的初始化知识，收敛成一次性的编译结果。

### C. 运行时内核层

这是新的 DCS/DPU runtime，建议拆成：

- `Scheduler`
- `ExecutionGraph`
- `BlockRunner`
- `PointStore`
- `ForceService`
- `DebugWatchService`
- `DownloadPlanner`

这一层只负责：

- 周期调度
- 输入同步
- 块执行
- 输出传播
- 工况快照/恢复
- 在线差量下装

### D. 持久化层

这里不应该再直接参与周期执行，只承担：

- Retentive/工况快照
- 下装版本对比缓存
- 工况导入导出
- 崩溃恢复

### E. 协议与网关层

负责：

- OPC UA
- Modbus/TCP
- HTTP/gRPC
- NATS 事件总线

### F. 历史与分析层

负责：

- 历史点
- SOE
- 报警与事件
- 趋势/报表/回放

---

## 5. 语言和技术栈建议

## 5.1 推荐主语言：C# / .NET 8

虽然理论上可以选 Go、Python、C++，但结合你们现状，我认为**主重构语言首选还是 C#**，原因很直接：

- 现有核心资产都在 C#：工程模型、RTD 语义、功能块库
- 已有约 **460 个功能块源码文件**，约 **235 个 `FCName` 定义**
- 运行时语义里大量使用了现有 `Function`/`Point`/`Pin` 模型
- OPC UA、Windows、现有工具链迁移成本最低
- .NET 8 足够现代，Span、MemoryMarshal、NativeMemory、Channels、Source Generator 都能用

### 不建议

- **Python 做运行时内核**：不适合周期执行热路径
- **Go 直接做控制内核**：适合控制面服务，不适合承接你们现有 C# 功能块资产
- **完全重写成 C++**：技术上可行，但一次性迁移风险最大

### 更合理的语言分工

- **C#/.NET 8**：运行时内核、工程编译器、OPC UA 服务、兼容层
- **Go**：部署控制面、网关、运维工具、配置中心、批量任务
- **Python**：离线分析、校核、仿真脚本、AI/规则工具链

---

## 6. 开源组件怎么选

## 6.1 热路径状态存储

### 结论

**不要把网络型数据库直接放进 DPU 周期热路径。**

热路径应该是：

- 进程内 `PointStore`
- 结构化连续内存
- 预编译访问器
- 无反射或极少反射

### 推荐做法

- 周期内只访问进程内状态
- 工况/retentive 快照异步落盘
- 在线调试走只读镜像或订阅总线

### 持久化备选

#### 方案 A：RocksDB

适合：

- 大量写入
- 本地嵌入式 KV
- 快照/checkpoint
- Windows/Linux 都要兼顾

适合放的位置：

- Retentive state
- 工况增量快照
- 版本/部署差量缓存

#### 方案 B：Valkey

适合：

- 控制面缓存
- 分布式锁
- 发布订阅
- 会话/调试信息

不适合：

- DPU 周期内热路径
- 本地 deterministic 扫描环

### 我的建议

- **热路径**：自研 `PointStore`
- **retentive/snapshot**：优先 RocksDB
- **控制面缓存/订阅**：Valkey 可选

## 6.2 事件总线

### 推荐：NATS JetStream

适合你们的点：

- 轻量
- watch/history 语义很适合调试/监控/部署事件
- 支持 KV / Object Store / 原子更新

建议用途：

- 在线下装任务编排
- 调试 watch / force 事件流
- DPU 状态广播
- 工程版本变更通知

## 6.3 历史库 / 时序库

### 推荐：Apache IoTDB

原因：

- 本身就是工业 IoT 时序数据库
- 支持边云协同
- 高吞吐写入和时间对齐查询
- 对工业场景语义更贴近

建议用途：

- 历史点
- SOE/报警归档
- 回放
- 趋势查询

### 备选

- QuestDB：偏 SQL/高吞吐分析
- TimescaleDB：若团队更熟 PostgreSQL

---

## 7. 推荐的重构路线

## 7.1 路线 A：保留工程模型，重写运行时内核

这是我最推荐的路线。

### 第 1 阶段：冻结现有语义

先不要急着改架构，先把旧系统的“行为契约”固化：

- 选 5 到 10 个典型工程做 golden case
- 固化以下输出：
  - 工程初始化后点值/块值
  - 单周期执行后的关键点
  - 工况保存/加载一致性
  - 在线下装前后行为
  - 强制/解除强制行为
  - Watch/调试行为

如果不先做这步，后面所有“重构”都只能凭感觉对齐。

### 第 2 阶段：抽出工程编译器

把当前数据库加载逻辑从运行时里切出来，形成独立模块：

- `LegacyProjectImporter`
- `RuntimePlanCompiler`

输出统一中间模型，例如：

```text
Controller -> TaskGroup -> BlockInstance -> Pin -> Connection -> PointBinding
```

这样可以先保留数据库不动，但把运行时初始化从 NHibernate/老 DAL 解耦出来。

### 第 3 阶段：实现新 PointStore

目标：

- 不再依赖 `RTD/PointManage/TypeManage` 的混合机制
- 明确区分：
  - 点当前值
  - 质量位
  - 强制状态
  - 调试镜像
  - retentive 值

这里建议：

- 基础值类型用 blittable struct
- 编译期生成访问器
- 只在边界层保留对象包装

### 第 4 阶段：实现新执行内核

执行过程建议固定成 4 步：

1. `Read inputs`
2. `Run blocks`
3. `Propagate outputs`
4. `Publish changed points`

连线不要再走“运行时临时反射 + 偏移查找 + 即时连接”，而是提前编译成：

- 点到块输入绑定表
- 块输出到块输入连接表
- 块输出到点绑定表

### 第 5 阶段：兼容现有功能块

不要一开始就重写 235 个功能块。

建议做一个兼容执行适配器：

- 先让旧 `Function` 在新 runtime 下跑起来
- 再逐步把关键功能块迁移成新接口

这样收益最大，风险最小。

### 第 6 阶段：重做工况与在线下装

建议把现在的 `.wrk/.prj` 私有二进制格式升级为：

- `manifest.json`
- `runtime-snapshot.bin`
- `retentive.bin`
- `versions.json`

在线下装则改成：

- 工程 diff
- 计划 diff
- 控制器级 change set
- 可回滚部署

也就是把“下装”从“重新加载 + 修修补补”变成“显式变更集发布”。

---

## 8. 运行时映射路线：优先评估 Eclipse 4diac

如果希望借助现成开源 runtime，而不是自己长期维护整个控制内核，那么最值得评估的是 **Eclipse 4diac**。

### 8.1 为什么是 4diac，不是 OpenPLC

因为 4diac 的核心模型天然就是：

- Function Block
- Application
- Device
- Resource
- Connection
- Deployment
- Monitoring/Debugging

这和你们现有虚拟 DCS 的抽象高度相似。

### 8.2 功能映射关系

| 你们当前概念 | 4diac 映射 |
| --- | --- |
| DCS 工程 | 4diac Application |
| DPU / 控制器 | Device 或 Resource |
| 功能块实例 | FB Instance |
| Pin | FB Data Port / Event Port |
| 连线 | Data Connection / Event Connection |
| 点 | I/O FB、SIFB、Adapter FB，或点镜像数据 FB |
| 工况初始化 | Deployment 时的初始值 / 参数注入 |
| 在线下装 | Selective download / online reconfiguration |
| 调试 watch / force | Monitoring and Debugging |
| OPC/外设接入 | SIFB / OPC UA / protocol adapter |

### 8.3 4diac 能直接替你们做什么

- 功能块建模
- 设备/资源部署
- 在线监控与调试
- 部分在线重配置
- IEC 61499 风格运行时

### 8.4 4diac 无法直接替你们做的部分

这些仍然需要自己补：

- 当前数据库工程模型到 4diac 模型的编译器
- 工况 `.wrk/.prj` 兼容导入
- 现有功能块库自动迁移/包装
- 历史库、报警库、SOE
- 你们现有“点-块-块-点”混合绑定语义兼容
- 版本差异比较和下装策略
- 与现有 UI/调试接口的兼容

### 8.5 什么时候适合选 4diac

适合：

- 想对齐 IEC 61499
- 想减少自维护运行时内核
- 愿意接受工程模型迁移成本
- 接受新工具链和研发方式

不适合：

- 短期必须兼容全部历史工程和私有格式
- 需要快速迭代，不允许工具链迁移学习成本

### 8.6 对 4diac 的实际建议

不要 big-bang 替换。

更合理的方式是：

1. 先做一个 `LegacyProject -> RuntimePlan`
2. 再做一个 `RuntimePlan -> 4diac Model` 的 PoC
3. 选 1 个典型 DPU 试迁
4. 只用 4diac 跑新增控制域或新功能块域

也就是说，把 4diac 当成**战略平台候选**，不是立即整体切换。

---

## 9. OpenPLC 和 Beremiz 怎么看

## 9.1 OpenPLC

### 优点

- IEC 61131-3 生态 familiar
- 地址模型明确，I/O 映射简单
- 小控制器或 PLC 风格场景上手快

### 问题

- 更接近 PLC 扫描周期，不接近你们当前的虚拟 DCS 图式 runtime
- 对“多 DPU + 跨块图连接 + 在线差量下装 + 工况镜像”支持不自然
- 现成工程模型映射成本高
- **OpenPLC v3 已 EOL/归档**

### 结论

可以作为“小型 PLC 子系统”参考，不建议作为整个虚拟 DCS runtime 主路线。

## 9.2 Beremiz

### 优点

- 仍在活跃维护
- IEC 61131-3 工具链更完整
- 有 Python/C runtime
- 2026 年仍在改进 OPC UA

### 问题

- 仍然更偏 PLC/自动化 IDE + runtime
- 和你们当前“图式块实例 + 自定义点模型 + 在线工况/下装语义”的一一映射不如 4diac 自然

### 结论

比 OpenPLC 更值得关注，但优先级依然低于 4diac。

---

## 10. 三条方案的对比

| 方案 | 对现有功能贴合度 | 迁移风险 | 标准化程度 | 短期落地性 | 长期收益 |
| --- | --- | --- | --- | --- | --- |
| **A. .NET 8 自主重构运行时** | 最高 | 中 | 中 | **最高** | **最高** |
| **B. 4diac 映射** | 高 | 中高 | **最高** | 中 | 高 |
| **C. OpenPLC/Beremiz** | 中低 | 中高 | 高 | 低 | 中 |

我的排序：

1. **A：主路线**
2. **B：战略备选**
3. **C：局部参考，不做主线**

---

## 11. 建议的目标系统形态

建议最终形成下面这样的仓库结构：

```text
vdcs/
  project-model/          # 导入现有数据库工程，输出标准模型
  runtime-plan/           # 编译后的不可变执行计划
  runtime-kernel/         # DPU/调度/执行/传播
  state-store/            # in-memory store + retentive adapter
  deployment/             # 在线下装、diff、版本管理
  gateways/
    opcua/
    modbus/
    http/
    nats/
  historian/              # IoTDB adapter
  compat/
    legacy-function-host/ # 旧 Function 兼容层
    wrk-prj-importer/     # 老工况格式导入
```

---

## 12. 最小可执行实施建议

如果只做一个 3 个月内能见效果的版本，我建议这样排：

### 里程碑 1

- 固化 5 个典型工程样本
- 补齐初始化/工况/连线/下装契约测试
- 产出 `RuntimePlan` 中间模型

### 里程碑 2

- 新建 `PointStore`
- 新建 `Scheduler + ExecutionGraph`
- 先兼容 20 个高频核心功能块

### 里程碑 3

- 做新旧 runtime 并跑比对
- 打通工况导入/导出
- 打通在线差量下装

### 里程碑 4

- 引入 NATS JetStream 作为控制面事件总线
- 引入 IoTDB 作为历史库
- 将 OPC UA/HTTP 调试接口从 WinForms 主进程剥离

---

## 13. 最终建议

### 建议一

**不要从“换数据库”开始。**

真正要先换的是：

- 运行时边界
- 初始化模型
- 连线执行模型
- 工况与下装机制

数据库只是配套选择，不是根问题。

### 建议二

**第一阶段不要追求完全标准化，先把旧语义稳定搬出来。**

也就是：

- 先做新 runtime
- 再谈是否切到 4diac/IEC 61499

### 建议三

如果只能选一个最终主方案：

**选“C# / .NET 8 + 新运行时内核 + NATS + IoTDB + 嵌入式持久化快照”**。

如果想同时保留一个中长期平台化方向：

**把 4diac 作为第二路线做 PoC，但不要直接替换现网语义。**

---

## 14. 外部调研参考

### 现成 runtime / 自动化平台

- Eclipse 4diac: https://eclipse.dev/4diac
- 4diac IDE: https://eclipse.dev/4diac/4diac_ide/
- 4diac FORTE: https://eclipse.dev/4diac/4diac_forte/
- OpenPLC v3: https://github.com/thiagoralves/OpenPLC_v3
- OpenPLC Runtime Overview: https://old.autonomylogic.com/docs/2-1-openplc-runtime-overview/
- OpenPLC Addressing: https://old.autonomylogic.com/docs/2-3-input-output-and-memory-addressing/
- Beremiz: https://beremiz.org/
- Beremiz GitHub: https://github.com/beremiz/beremiz

### 开源基础组件

- NATS JetStream: https://docs.nats.io/nats-concepts/jetstream
- NATS KV: https://docs.nats.io/nats-concepts/jetstream/key-value-store
- Valkey: https://valkey.io/topics/introduction/
- Apache IoTDB: https://iotdb.apache.org/
- RocksDB: https://github.com/facebook/rocksdb

---

## 15. 后续可以继续展开的专题

这份文档是项目级重构建议。后续如果继续推进，我建议再拆 4 份专项文档：

1. `RuntimePlan` 中间模型定义
2. 新 `PointStore` 内存布局设计
3. 在线下装 diff 模型设计
4. 4diac 映射 PoC 设计

如果只允许先做一件事，就先做第 1 件：**把工程模型和运行时模型彻底拆开。**
