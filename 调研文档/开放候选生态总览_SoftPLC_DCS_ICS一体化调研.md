# 开放候选生态总览

副标题：Soft PLC、虚拟 DCS、智慧监盘、智能预警、ICS 智能扩展的一体化开源候选调研

调研日期：2026-04-23

## 1. 这次调研的目标

当前讨论的重构方案还不应该过早收敛。

因为你们不是只在重构一个“虚拟 DCS runtime”，而是在同时考虑一套更大的平台：

- 虚拟 DCS
- Soft PLC 能力
- 智慧监盘
- 智能预警
- ICS 系统扩展
- 历史统计分析
- 机器学习预测
- 仿真系统联动

而且你已经明确说了：**这些能力希望尽量用同一套代码和框架。**

所以这一轮更合理的目标不是“确定最终方案”，而是把开源候选生态按层梳理清楚：

1. 哪些项目适合做底层控制 runtime
2. 哪些项目适合做 Soft PLC
3. 哪些项目适合做 SCADA/HMI/监盘
4. 哪些项目适合做规则、预警、流计算、ML
5. 哪些项目适合做统一连接层和历史层

最终输出应该是一张“开放架构地图”，而不是一个单点产品名单。

---

## 2. 应该先把目标平台拆成哪几层

如果真的希望“虚拟 DCS + 监盘 + 预警 + 分析 + AI”共用同一套框架，建议先按 5 层理解：

### L0：确定性控制层

负责：

- 周期扫描
- 联锁
- 顺控
- PID
- 定时器
- 功能块内部状态
- 连续内存工况

关键词：

- 确定性
- 连续内存
- 精确回放
- 周期边界

### L1：工业连接与数据平面

负责：

- OPC UA
- Modbus
- S7
- MQTT
- 仿真系统 I/O map
- 当前值视图
- 历史数据入口

关键词：

- 协议接入
- 数据统一建模
- 当前值缓存
- 历史写入

### L2：历史与资产层

负责：

- 时序历史
- 事件/报警
- checkpoint 索引
- 设备/测点资产模型

关键词：

- 时间轴
- 历史查询
- 告警上下文
- 资产图谱

### L3：监督控制与智能层

负责：

- 规则引擎
- 预警
- 流计算
- 软测量
- 优化
- 机器学习预测
- AI 生成逻辑

关键词：

- 数据流
- 推理
- 预警
- 分析
- 在线优化

### L4：操作与可视化层

负责：

- 监盘
- Dashboard
- HMI
- 报表
- 告警管理
- 运维工具

关键词：

- 统一视图
- 操作闭环
- 告警处置

---

## 3. Soft PLC / 控制 Runtime 开源候选

## 3.1 Beremiz

### 项目定位

Beremiz 官方仓库和官网给出的定位很明确：

- 开源机器自动化 IDE
- 符合 IEC 61131
- 依赖开放标准，避免 vendor lock-in
- 包含 IDE、CLI、runtime
- 包含 Python reference runtime 和 C runtime
- 可连接现有 supervision、database、fieldbus

### 优点

- 非常符合“Soft PLC”的基本概念
- 不是单纯编译器，而是完整工程环境
- 有 C runtime，便于目标平台下放
- 有 Python runtime，便于扩展和实验
- 已经考虑到 HMI / 数据库 / 总线集成

### 对你们的价值

Beremiz 不一定适合作为你们整个虚拟 DCS 的最终 runtime，但它很值得参考，尤其在这些方面：

- IEC 61131 项目组织方式
- 代码生成和 runtime 分离
- IDE + CLI 并存
- 小目标设备与通用机两种 runtime
- Soft PLC 和上层集成并存

### 局限

- 更偏 PLC 自动化，不是原生 DCS 图式 runtime
- 你们已有大量 C# 功能块资产，不可能直接平移
- 工况连续内存、精确回放、DPU 概念仍需自定义

### 结论

**Beremiz 是最值得认真参考的 Soft PLC 开源样板之一。**

它适合做：

- Soft PLC 参考实现
- IEC 61131 导入/兼容方向
- 运行时分层参考

不太适合直接一把替换你们全部 DCS runtime。

官方来源：

- https://github.com/beremiz/beremiz
- https://beremiz.org/

---

## 3.2 OpenPLC

### 项目定位

OpenPLC 长期以来都是最知名的开源 Soft PLC 项目之一。

其官方 Editor 仓库 README 直接说明：

- OpenPLC Editor 包含修改过的 Beremiz 和 matiec

也就是说，它本身就处在：

- IEC 61131 IDE
- 编译器
- Runtime
- 硬件适配

这一条链路上。

### 优点

- Soft PLC 概念非常明确
- 地址模型和 I/O 映射直观
- 对教学、实验、协议演示非常友好
- 社区知名度高

### 局限

最关键的问题不是技术，而是维护状态。

OpenPLC v3 官方仓库 README 已经明确写明：

- **IMPORTANT UPDATE**
- **As of April 4, 2026, OpenPLC V3 is officially End-of-Life and archived**

这意味着：

- 它仍然值得研究
- 但不适合再作为长期主平台押注

### 结论

**OpenPLC 更适合作为 Soft PLC 参考项目，而不是你们未来主平台的基石。**

可以借鉴：

- I/O 地址模型
- Web 管理体验
- Soft PLC packaging

不建议作为长期核心依赖。

官方来源：

- https://github.com/thiagoralves/OpenPLC_v3
- https://github.com/thiagoralves/OpenPLC_Editor

---

## 3.3 Eclipse 4diac / FORTE

### 项目定位

严格说它不属于传统 Soft PLC，而更接近开放式分布式控制 runtime。

官方站点表明：

- 4diac IDE：建模和工程工具
- FORTE：IEC 61499 runtime

### 优点

- 天然支持 Function Block 思维
- Device / Resource / Application 模型贴近分布式控制
- 在线部署/重配置能力比传统 PLC 思维更自然
- 对你们“DPU + 功能块 + 连线 + 在线下装”的抽象更接近

### 局限

- IEC 61499 而不是 IEC 61131-3
- 对现有 C# 资产和数据库工程模型的迁移成本较高

### 结论

**它不是 Soft PLC 的直接答案，但它是虚拟 DCS runtime 映射里最有价值的开源项目之一。**

官方来源：

- https://eclipse.dev/4diac/
- https://eclipse.dev/4diac/4diac_ide/
- https://eclipse.dev/4diac/4diac_forte/

---

## 3.4 matiec

### 项目定位

matiec 官方 README 的定位非常清楚：

- 开源 IEC 61131-3 编译器
- 主要支持 IL / ST / SFC 的文本表示
- `iec2c` 可以生成 ANSI C 代码

### 优点

- 它不是完整平台，但它是非常重要的“基础组件”
- 如果你们将来需要：
  - 导入 IEC 61131-3 程序
  - 给 Soft PLC / 兼容层提供编译能力
  - 做 ST 到自定义 runtime 的中间转换

那么它很有价值

### 局限

- 它是编译器，不是完整 runtime
- 不解决工况、调试、历史、智能扩展

### 结论

**matiec 更适合作为组件，而不是平台。**

官方来源：

- https://github.com/beremiz/matiec

---

## 3.5 其他值得提到的候选

### LDmicro

优点：

- 小巧
- 简单
- Ladder 风格明显

局限：

- 更适合小型场景
- 不适合承接虚拟 DCS + ICS 智能扩展的大平台目标

### IronPLC

属于更偏前沿/探索性的实现方向。

可关注，但暂不建议作为主候选。

### 结论

这些项目更适合作为概念参考，不建议进入主决策表第一梯队。

---

## 4. 工业连接与数据平面候选

## 4.1 Apache PLC4X

### 项目定位

Apache PLC4X 官方定位很明确：

- 面向工业协议的统一访问框架
- 提供 Java、Go 等语言 API

### 为什么它重要

你们现在已经有 OPC UA、仿真 I/O map、各种外部系统对接。

如果以后平台做大，协议层不应该继续分散在：

- DCS runtime
- SCADA
- 智能分析
- 外部工具

而是应当逐步统一。

PLC4X 的价值不在“替代 runtime”，而在：

- 做统一连接层
- 为监盘、预警、分析共用协议访问

### 结论

**PLC4X 是非常值得纳入中长期平台版图的连接层候选。**

官方来源：

- https://plc4x.apache.org/

---

## 5. 监盘 / SCADA / HMI 候选

## 5.1 FUXA

### 项目定位

FUXA 官方文档明确写明：

- Web-based
- 可快速构建 SCADA / HMI / Dashboard / IIoT
- 支持 OPC UA、S7、Modbus、BACnet、MQTT、EtherNet/IP、WebAPI
- Backend 基于 Node.js

### 优点

- 非常适合做现代 Web 监盘
- 协议支持丰富
- 免 runtime license
- 适合作为“上层可视化与轻操作层”

### 对你们的价值

如果你们未来不想把全部前端/监盘都继续压在 WinForms/老 UI 上，FUXA 这种项目很值得关注。

### 局限

- 它不是控制 runtime
- 不解决确定性控制、工况快照、DPU 扫描

### 结论

**FUXA 很适合做智慧监盘、可视化、上层 SCADA/HMI。**

官方来源：

- https://frangoteam.github.io/FUXA/
- https://frangoteam.github.io/FUXA/

---

## 5.2 Grafana

### 项目定位

Grafana 官方 Alerting 文档显示：

- 可对多个数据源创建查询和表达式
- 统一管理规则、通知、告警状态历史

### 优点

- 多数据源能力强
- 告警管理成熟
- 运维生态完善

### 对你们的价值

Grafana 不适合做控制 HMI 主界面，但很适合做：

- 历史趋势
- 统计分析看板
- 预警面板
- 统一告警中心

### 结论

**Grafana 是 ICS 智能扩展和监测运维层的高价值候选。**

官方来源：

- https://grafana.com/docs/grafana/latest/alerting/

---

## 6. 规则、预警、智能扩展候选

## 6.1 ThingsBoard

### 项目定位

ThingsBoard 官方 Rule Engine Overview 写得很清楚：

- Rule Engine 是它的核心数据处理机制
- 基于 message、rule node、rule chain
- 支持脚本逻辑、HTTP、Kafka、MQTT 等外部集成
- 还能处理报警、通知、RPC 等

### 优点

- 非常适合作为：
  - 智能预警引擎
  - 规则链平台
  - 事件驱动自动化平台
- 支持脚本扩展
- 支持自定义 rule node

### 对你们的价值

如果你们要做“DCS 之上的 ICS 智能扩展”，ThingsBoard 这类规则引擎平台是非常值得看的一类方案。

它特别适合：

- 告警治理
- 数据清洗
- 事件联动
- 规则编排
- 边缘侧自动化

### 局限

- 不是确定性控制 runtime
- 更像“监督规则平面”

### 结论

**ThingsBoard 适合做 L3：监督控制、预警、规则编排层。**

官方来源：

- https://thingsboard.io/docs/user-guide/rule-engine-2-0/overview/
- https://thingsboard.io/docs/user-guide/alarm-rules
- https://thingsboard.io/docs/user-guide/contribution/rule-node-development/

---

## 6.2 Apache StreamPipes

### 项目定位

StreamPipes 官方首页和文档明确强调：

- Industrial IoT toolbox
- 自助式 IIoT 数据处理
- 支持工业协议接入
- 可运行扩展处理器和 sink
- 有 Java / Python / TypeScript 扩展能力
- 可做在线 ML

### 优点

- 很贴近“ICS 智能扩展”的语义
- 不是单纯 dashboard，而是完整的 IIoT 数据流平台
- 支持实时流处理和历史数据交互
- 可运行时安装扩展

### 对你们的价值

它很适合放在：

- 智慧监盘数据流平台
- 智能预警
- 轻量在线 ML
- 数据处理编排

### 局限

- 仍然不是底层确定性控制 runtime

### 结论

**如果你们真的要做“DCS 之上的智能系统”，StreamPipes 是非常值得深入看的开源项目。**

官方来源：

- https://streampipes.apache.org/
- https://streampipes.apache.org/docs/user-guide-introduction/

---

## 7. 历史层候选

## 7.1 Apache IoTDB

### 项目定位

仍然是最适合作为主历史库的候选之一。

### 为什么它在这张图里很关键

因为它不只适合存历史，还开始具备：

- UDF
- Trigger
- Subscription
- C# client
- 表模型

这使它成为“历史层”和“流式监督层”的关键中枢。

### 结论

**IoTDB 很适合做整个平台的时间轴核心。**

官方来源：

- https://iotdb.apache.org/

## 7.2 InfluxDB

更适合作为：

- 分析侧
- 降采样侧
- 外部共享侧

不建议单独作为整个控制/工况平台的唯一数据底座。

---

## 8. 把这些项目放到同一张架构图里

## 8.1 最值得考虑的开放式组合

### 组合 A：自研确定性 runtime + 开源上层生态

这是目前最稳的组合。

#### L0 确定性控制层

- 自研新 runtime
- 保留 C# 功能码主路径
- 保留连续内存
- 保留工况 checkpoint/replay

#### L1 连接与数据平面

- OPC UA / 现有仿真 I/O map
- 中长期可引入 Apache PLC4X

#### L2 历史与时间轴

- IoTDB 主历史库
- checkpoint 索引

#### L3 智能扩展层

- ThingsBoard 或 StreamPipes
- Python worker
- AI / 预测 / 规则

#### L4 监盘与可视化

- FUXA
- Grafana
- 自研 UI

### 组合 B：Beremiz/Soft PLC 参考内核 + 上层生态

适合做：

- IEC 61131 兼容路线
- Soft PLC 实验平台

但不太适合直接变成你们完整虚拟 DCS 的最终主线。

### 组合 C：4diac/FORTE + 上层生态

适合做：

- 面向 IEC 61499 的长期平台路线
- 分布式功能块体系

更偏战略路线。

---

## 9. 这些候选对“同一套代码和框架”的启发

如果你们希望真正共用一套框架，那就不要理解成：

- 所有功能都跑在同一个进程
- 所有功能都用同一个项目实现

更合理的理解应该是：

**共用同一套平台骨架、资产模型、插件模型、时间轴模型。**

也就是说，共用的是：

### 1) 统一资产模型

- 点
- 设备
- 功能块
- DPU
- 仿真对象
- 告警对象

### 2) 统一时间轴模型

- 当前值
- 历史值
- 事件
- checkpoint
- replay

### 3) 统一插件模型

- 硬实时块
- 软实时算法块
- 规则块
- 预警块
- 分析块

### 4) 统一接入层

- 仿真系统
- OPC UA
- PLC / DCS 协议
- WebAPI / gRPC

### 5) 统一运维与 UI

- 监盘
- 告警
- 可视化
- 历史分析

所以，“同一套代码和框架”最合理的目标不是 one engine，而是 one platform。

---

## 10. 现阶段的建议排序

## 10.1 第一梯队

这些项目最值得真正纳入方案比选：

- 自研确定性 runtime
- Apache IoTDB
- Beremiz
- Eclipse 4diac / FORTE
- Apache PLC4X
- Apache StreamPipes
- ThingsBoard
- FUXA
- Grafana

## 10.2 第二梯队

这些项目更适合作为参考而非主干：

- OpenPLC
- LDmicro
- IronPLC

其中 OpenPLC 最大问题是维护生命周期已经发生明显变化。

---

## 11. 最重要的结论

### 结论一

**不要再试图找一个“万能开源项目”同时承接虚拟 DCS、Soft PLC、监盘、预警、分析、AI。**

现实中更可行的是：

- 底层控制自己掌握
- 上层能力尽量接入开源生态

### 结论二

**Soft PLC 这条线里，Beremiz 的参考价值明显高于 OpenPLC。**

OpenPLC 仍值得研究，但更适合作为参考样本。

### 结论三

**对于你们这种“DCS + ICS 智能扩展”并行演进的目标，最有希望的不是单一 runtime，而是分层平台。**

### 结论四

如果只说一个最值得继续深入的开放式组合，那就是：

**自研确定性 C# runtime + IoTDB + StreamPipes/ThingsBoard + FUXA/Grafana + 中长期考虑 PLC4X。**

这条路线最符合你们现在的复杂目标，也最容易保住现有资产。

---

## 12. 官方参考链接

### Soft PLC / 控制

- Beremiz
  - https://github.com/beremiz/beremiz
  - https://beremiz.org/
- matiec
  - https://github.com/beremiz/matiec
- OpenPLC v3
  - https://github.com/thiagoralves/OpenPLC_v3
- OpenPLC Editor
  - https://github.com/thiagoralves/OpenPLC_Editor
- Eclipse 4diac
  - https://eclipse.dev/4diac/
  - https://eclipse.dev/4diac/4diac_forte/
  - https://eclipse.dev/4diac/4diac_ide/

### 连接层

- Apache PLC4X
  - https://plc4x.apache.org/

### 监盘 / HMI / SCADA

- FUXA
  - https://frangoteam.github.io/FUXA/
- Grafana Alerting
  - https://grafana.com/docs/grafana/latest/alerting/

### 智能扩展 / 规则 / 流处理

- ThingsBoard Rule Engine
  - https://thingsboard.io/docs/user-guide/rule-engine-2-0/overview/
  - https://thingsboard.io/docs/user-guide/alarm-rules
- Apache StreamPipes
  - https://streampipes.apache.org/
  - https://streampipes.apache.org/docs/user-guide-introduction/

### 历史 / 时序

- Apache IoTDB
  - https://iotdb.apache.org/
- InfluxDB
  - https://docs.influxdata.com/influxdb3/core/
