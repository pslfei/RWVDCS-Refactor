# 虚拟DCS项目重构方案 V2

副标题：保留 C# 功能码热更新、保留连续内存快照性能、把历史与工况恢复统一到一个时间轴

调研日期：2026-04-23

## 1. 这次新增约束，直接改变了重构方向

你新增的三个要求不是“优化项”，而是会直接改变架构边界的硬约束：

1. **功能码必须继续支持 C# 动态调试和热替换**
2. **运行态的点和功能块状态仍要尽量保持连续内存，以保证工况 Save/Load 极快**
3. **历史数据最好能和工况保存统一起来，能够加载任意时间点作为工况**
4. **功能码除了 C#，也可以用 Python、Web API 等方式实现，并以插件形式加载**

基于这三个约束，原来那种“把运行时尽量映射到现成外部 runtime 或时序库”的思路要收紧。

新的结论是：

**主运行时仍然应该是你们自己的 C# 内核。**

但它要演进成一个“多宿主插件运行时”：

- C# 作为主路径和硬实时实现语言
- Python 作为软实时/算法插件语言
- Web API / gRPC 作为异步服务插件接入方式

但这个内核要重构成：

- `C# 热更新友好`
- `连续内存友好`
- `历史回放友好`

而不是：

- 让 IoTDB / InfluxDB 直接承担 DPU 周期热路径
- 或者让外部 runtime 接管功能块执行

如果把时序库直接推到热路径里，你们原来最宝贵的两个优势会明显受损：

- C# 改码即看效果
- 工况快照极快

所以这次的重构建议，需要从“替换 runtime”调整为“**重建 runtime 边界**”。

---

## 2. 先说总判断

### 2.1 应该保留什么

必须保留的，不只是功能码源码，而是下面四样东西一起保留：

- **C# 作为功能码实现语言**
- **多语言插件扩展能力**
- **功能码可以运行中修改并快速生效**
- **运行态内存映像连续可快照**

### 2.2 应该替换什么

应该替换的，是现在“代码对象 = 状态对象 = 序列化对象 = 调试对象”的耦合模式。

也就是：

- 旧模式：`Function` 对象本身同时承载逻辑、状态、Pin、调试、序列化
- 新模式：**逻辑代码和运行状态彻底分离**

这一步是这次 V2 方案的核心。

### 2.3 历史与工况怎么统一

可以统一，但不能简单理解成“只存到 IoTDB / InfluxDB 一份就够了”。

更准确的说法应该是：

**历史和工况要统一为同一条“时间轴状态服务”，但底层存储必须分层。**

也就是：

- 逻辑上：历史、快照、工况恢复是一个系统
- 物理上：必须分成
  - 本地二进制 checkpoint / delta journal
  - 时序库 historian

如果强行要求“只有时序库，没有本地快照/日志”，那工况恢复性能一定会掉。

这不是实现水平问题，是数据结构问题。

### 2.4 多语言插件怎么正确纳入

多语言插件的关键不在“能不能加载”，而在：

**不同语言和宿主的功能码，必须共享同一套运行时 ABI 和同一套状态主权模型。**

也就是：

- C# 插件
- Python 插件
- Web API / gRPC 插件

都可以存在，但必须满足两个原则：

1. **运行时拥有权威状态**
2. **插件只实现逻辑，不私藏关键运行态**

如果 Python 进程或远端服务私下维护积分器、定时器、步序状态，而这些状态又不回写到运行时的连续内存里，那么：

- checkpoint 会不完整
- 工况恢复会失真
- 历史回放不再等价

所以这次 V2 方案必须升级成：

**多语言插件架构，但单一状态主权。**

---

## 3. 新的目标架构

## 3.1 总体架构

建议重构成 6 个核心子系统：

1. `FunctionCodeHost`
2. `RuntimeMemoryImage`
3. `ExecutionKernel`
4. `CheckpointAndReplay`
5. `HistorianPipeline`
6. `EngineeringCompiler`

### 3.1.1 FunctionCodeHost

负责：

- C# 功能码源码管理
- 编译
- 热更新
- 调试
- 版本化加载和卸载

### 3.1.2 RuntimeMemoryImage

负责：

- 点状态
- 块内部状态
- Pin buffer
- 强制位/质量位
- 定时器/积分器/步序等运行态

所有这些都放在**连续内存布局**里。

### 3.1.3 ExecutionKernel

负责：

- 周期调度
- 输入同步
- 功能块执行
- 输出传播
- DPU 级切换点

### 3.1.4 CheckpointAndReplay

负责：

- 工况保存
- 工况加载
- 任意时间点恢复
- 回放

### 3.1.5 HistorianPipeline

负责：

- 实时值采样
- 历史落库
- TTL / 降采样 / 归档
- 向 IoTDB / Influx 输出

### 3.1.6 EngineeringCompiler

负责：

- 从现有数据库工程模型编译出 `RuntimePlan`
- 生成连续内存布局
- 生成块执行图
- 生成历史采样计划
- 生成热更新元数据

---

## 4. 这次方案里最关键的结构变化

## 4.1 代码与状态分离

现在最需要改变的是这件事：

### 旧模式

```text
Function 对象
  ├─ 字段 = 输入/输出/常量/内部状态
  ├─ Run() = 逻辑
  ├─ 序列化 = 直接写对象
  └─ 调试 = 直接看对象字段
```

### 新模式

```text
FunctionCode（代码）
  └─ Execute(context)

FunctionState（状态）
  └─ 在连续内存中的固定布局

FunctionDescriptor（元数据）
  └─ 描述这个块的 pin / state / 版本 / 迁移规则
```

也就是：

- 代码是可热替换的
- 状态是常驻连续内存的
- 两者通过稳定 ABI 连接

这样才能同时满足：

- C# 代码可改
- 状态不丢
- 快照快
- 回放快

## 4.2 功能码 ABI 要稳定

建议定义一个新 ABI，不再让功能码直接持有运行态对象图。

例如：

```csharp
public interface IFunctionLogic<TState> where TState : unmanaged
{
    void Execute(ref TState state, in FunctionContext ctx);
}
```

或者更接近现有写法的 source-generator 方案：

```csharp
[FunctionBlock("PID")]
public partial class PIDLogic : FunctionLogic<PIDState>
{
    public override void Execute(ref PIDState s, in FunctionContext ctx)
    {
        var pv = ctx.ReadFloat(Input.PV);
        var sp = ctx.ReadFloat(Input.SP);
        var err = sp - pv;

        s.Integral += err * ctx.CycleSeconds;
        var output = s.Kp * err + s.Ki * s.Integral;

        ctx.WriteFloat(Output.OUT, output);
    }
}
```

这里的重点不是接口形式，而是：

- `state` 是连续内存里的状态块
- `ctx` 只提供受控访问
- 代码和状态可独立演进

---

## 5. 如何保留 C# 动态调试和即时生效

## 5.1 分成两种热更新机制

这部分不要只靠一种机制，应该分成两层：

### 层 1：开发期 Hot Reload

用于：

- 改方法体
- 改局部逻辑
- 快速验证控制策略

这层直接利用 .NET / Visual Studio 的 Hot Reload。

微软官方文档说明：

- Hot Reload 主要处理**方法体内部的大多数代码修改**
- .NET 6+ / Visual Studio 2022 以后支持范围更大
- 但方法签名修改、重命名、删除类型成员等仍有限制

所以：

**开发调试期间继续保留“改代码立即看效果”是完全可行的。**

### 层 2：运行时版本化热替换

用于：

- 修改功能码结构
- 新增字段/方法
- 替换整块逻辑
- 非 IDE 环境下在线切换

这层不要依赖 Hot Reload，而要用：

- Roslyn 编译
- `AssemblyLoadContext` 可回收加载
- 周期边界安全切换

微软官方文档明确：

- .NET Core / .NET 5+ 用 `AssemblyLoadContext` 支持可回收卸载
- 可在 `Unloading` 事件中清理线程和 GC 句柄
- .NET Framework 无法像这样单独卸载程序集

所以：

**如果你们要把“功能码在线改完立即切换”做成长期能力，迁移到 .NET 8 比继续停留在 .NET Framework 更合适。**

## 5.2 推荐的热更新运行方式

建议做成三档：

### A. 调试态热改

- 开发者改 `Execute()` 方法体
- IDE Hot Reload
- 当前 DPU 下一周期生效
- 不改 state schema

### B. 运行时重编译切换

- Roslyn 编译新 DLL
- 装入新的 `AssemblyLoadContext`
- 在 DPU 周期边界切换 `logic delegate`
- 老代码退出后卸载

### C. 带状态迁移的热替换

如果 state layout 变了：

- 编译新版本 logic
- 校验 state schema hash
- 执行迁移器 `Migrate(oldState, newState)`
- 再切换

这样既保留“改完即生效”，又不会把连续内存状态绑死在具体程序集对象上。

## 5.3 Roslyn 在这里怎么用

Roslyn 不只是分析器 SDK，它本质上就是开放的 C#/VB 编译器平台，适合做：

- 动态编译功能码
- 编译诊断
- 语法/语义分析
- 增量代码生成

所以建议：

- 正式功能码用 Roslyn Compilation 编译成版本化程序集
- 临时公式/调试脚本可以用 Roslyn Scripting

注意：

**不要把 Roslyn Scripting 当主运行机制。**

主运行仍然应该是编译后的程序集，不然性能和可控性都会下降。

---

## 6. 如何保留“连续内存 + 极快工况 Save/Load”

## 6.1 这部分不能让位给时序库

这里需要讲清楚：

**IoTDB / InfluxDB 适合做历史与分析，不适合替代你们的连续内存工况镜像。**

原因很简单：

- 你们当前 Save/Load 快，靠的是“整个运行态是连续内存映像”
- 时序库本质是按时间和测点组织的数据，不是进程运行态镜像

所以正确架构不是“用时序库替代工况文件”，而是：

- 工况镜像保留二进制快照语义
- 历史库保留时序查询语义
- 两者在时间轴上统一

## 6.2 新内存设计建议

建议把运行态分成 4 个连续区域：

1. `PointValueRegion`
2. `BlockStateRegion`
3. `PinBufferRegion`
4. `MetaFlagRegion`

### 1) PointValueRegion

放：

- LA / LD / LP / LP32 等点值
- 质量位
- 强制位
- 当前时间戳

### 2) BlockStateRegion

放：

- PID 积分项
- 定时器累计值
- 顺控步号
- 边沿检测历史值
- 报警确认状态
- 其他内部状态

### 3) PinBufferRegion

放：

- 运行期 pin buffer
- 中间传播值

### 4) MetaFlagRegion

放：

- dirty page bitmap
- object/version id
- checkpoint generation
- block schema hash

## 6.3 连续内存的实现建议

建议运行时使用：

- `NativeMemory` / `VirtualAlloc` / 非托管连续内存
- 或者 page-aligned `MemoryMappedFile`

但我的建议是分开：

### 运行热区

用：

- `NativeMemory` 或 page-aligned unmanaged buffer

优点：

- 周期执行最干净
- 不和磁盘 flush 直接耦合
- 可控

### 快照区

用：

- checkpoint 文件
- 页级 dirty 复制
- 可选 LZ4 / Zstd 压缩

也就是：

**运行时内存不直接等于历史库，也不直接等于磁盘文件。**

而是通过 checkpoint 服务做高效镜像。

## 6.4 工况 Save/Load 的新模式

### Save

不要每次完整遍历对象图序列化，而是：

1. 按页跟踪 dirty bitmap
2. 保存 snapshot header
3. 只拷贝变更页，或者直接拷贝整块连续区域
4. 后台异步刷盘

### Load

直接：

1. 停周期
2. 校验 schema/version
3. 将 snapshot 复制回连续内存
4. 恢复块执行上下文
5. 从周期边界继续

这仍然可以接近你们当前工况 Save/Load 的速度级别。

---

## 7. 历史与工况如何真正统一

## 7.1 统一的不是“存储引擎”，而是“时间轴模型”

建议把整个系统统一成：

### 时间轴上的三类数据

1. **Checkpoint**
2. **Delta Journal**
3. **Historian Series**

### 1) Checkpoint

表示某个时刻的完整可恢复运行态：

- 点值
- 块内部状态
- 强制状态
- 调试相关必要状态

### 2) Delta Journal

表示 checkpoint 之后每个周期或每个采样点的状态变化：

- 点变化
- 内部状态变化
- 事件

### 3) Historian Series

表示给查询、趋势、报表、分析用的时序数据：

- 过程点
- 报警
- 事件
- 选定的内部状态

## 7.2 任意时刻工况恢复的正确实现方式

如果目标是“加载任意时间点作为工况”，最正确的方式不是从时序库直接拼全量状态，而是：

### 恢复算法

1. 找到 `T` 之前最近的 checkpoint `C`
2. 加载 `C`
3. 回放 `C..T` 之间的 delta
4. 得到时刻 `T` 的完整运行态

这才是既快又准的方案。

## 7.3 为什么不能只靠历史点恢复

这是一个必须明确的技术点：

**如果只存点值历史，而不存功能块内部状态，很多时间点是无法精确恢复成工况的。**

例如这些状态都不是外部点就能推回来的：

- PID 积分累积项
- 延时块剩余时间
- Step/Seq 当前步号
- 锁存器状态
- 边沿检测历史位
- 报警 ACK/RESET 中间态

所以必须把“可回放工况”定义成：

- 外部点值
- 关键内部状态
- 强制状态
- 运行元状态

一起被记录。

### 这意味着

要提供两种恢复级别：

#### 精确恢复

需要：

- checkpoint
- internal-state delta

#### 近似恢复

只依赖外部点历史

结果：

- 可以恢复过程点
- 但内部状态会通过 warm-up 重新跑出
- 不能保证完全等价

所以如果你们要求“历史点加载后就像当时真的暂停了一样继续跑”，就必须为 replay 记录内部状态。

---

## 8. IoTDB 和 InfluxDB 在这个新目标下怎么选

## 8.1 我新的排序

在你这次新增要求下，我的建议调整为：

1. **IoTDB：主历史库**
2. **InfluxDB：可选分析/二级历史库**
3. **本地 checkpoint + delta journal：必须保留**

### 结论先说

**IoTDB 更适合作为“历史 + 回放索引 + 工业现场主 historian”。**

**InfluxDB 更适合作为“分析 / 降采样 / 对外共享 / 二级数据服务”。**

但：

**二者都不应该替代本地 checkpoint/delta 机制。**

## 8.2 为什么 IoTDB 更适合做主历史库

根据 Apache IoTDB 官方资料，当前它更贴合你们场景的点有：

- 明确面向工业 IoT / 电力 / 设备时序场景
- 强调高吞吐读写和低延迟查询
- 支持 TTL
- 有订阅机制
- 支持 TsFile 导入/自动加载
- 已有 C# 原生客户端
- 2025-04-18 发布的 V2.0.2 中，表模型已支持 C# / Go 客户端

这几项对你们尤其关键：

### 1) C# 直连友好

IoTDB 官方文档已经提供 C# Native API，支持 NuGet 包和连接池。

这意味着：

- 你们可以直接用 C# historian writer
- 不需要额外绕 JDBC
- 对现有技术栈最顺

### 2) TTL 和表级管理更贴近长期归档

IoTDB 最新文档中，TTL 可以按 table 粒度控制。

这适合你们把历史数据分成：

- 原始高频点
- 低频归档点
- 内部状态回放流
- 报警/事件流

### 3) 订阅能力适合做增量回放与数据外送

IoTDB 已经提供订阅接口，支持消费新写入数据；较新版本还支持 snapshot/live 模式。

这对下面两种事都很有用：

- 作为 historian 异步外送通道
- 作为“回放索引构建器”数据源

### 4) 工业语义更贴近

IoTDB 首页和文档都在强调工业 IoT、能源电力、高吞吐时序和边云协同，这和你们的虚拟 DCS 语境比 Influx 更接近。

## 8.3 为什么 InfluxDB 不适合作为唯一主历史/工况库

InfluxDB 3 Core 的能力本身不差，但它在你们这个目标上有几个不太舒服的点：

### 1) retention 约束不够灵活

官方文档说明：

- InfluxDB 3 Core retention 在 database 创建时设置
- Core 里 retention 之后**不能修改**
- retention 是**在查询时生效**
- 超出 retention 的数据可能暂时仍然存在于存储里

这意味着：

- 作为通用 historian 没问题
- 但要承担“回放工况基座”时，生命周期管理不够直观

### 2) 备份恢复不是内建主路径

官方文档说明：

- InfluxDB 3 Core 持久化到 object storage
- 目前没有内建 backup/restore 工具
- 恢复依赖对象存储文件复制流程

这更像分析库/服务型历史库，不像“我现在就要把某个时间点恢复成运行工况”的本地快照仓。

### 3) 处理引擎是 Python 插件，不是 C#

InfluxDB 3 Core 的 Processing Engine 是嵌入式 Python VM。

这在一般数据处理上是优点，但在你们这里反而有点割裂：

- 功能码是 C#
- 运行时是 C#
- 如果 historian 二次处理、降采样、回放编排改用 Python，技术栈就裂开了

### 4) 写入耐久路径更偏通用数据库

InfluxDB 3 Core 文档里写得很清楚：

- 数据先入内存 buffer
- 默认每 1 秒 flush 到 WAL
- 再进入 queryable buffer
- 默认每 10 分钟持久化到 Parquet

这对 historian 非常正常，但不适合作为“本地工况瞬时镜像”的等价替代。

## 8.4 对 InfluxDB 的正确定位

所以 InfluxDB 在这次方案里的最佳定位是：

- 辅助分析库
- 外部报表库
- 降采样展示库
- 云侧共享库

如果团队本身已经有 Influx 使用经验，也完全可以：

- IoTDB 做主 historian
- Influx 做降采样副本或外部接口层

## 8.5 是否能把 IoTDB 和工况彻底合并成一份

严格讲，不建议。

### 正确说法

可以做到：

- **统一调度**
- **统一时间轴**
- **统一保留策略**
- **统一回放入口**

但底层应当仍分成两层：

- `Checkpoint/Journal`：为恢复性能服务
- `IoTDB`：为历史查询服务

如果你追求“真正单份存储”，那就只能接受：

- 恢复速度下降
- 回放实现复杂化
- 连续内存优势被稀释

这不是代码写得好不好，而是存储模型本身的差异。

---

## 9. 新方案的历史/工况统一设计

## 9.1 推荐设计：二级存储、单一时间轴

### L0：运行态内存

- 连续内存
- 纳秒/微秒级访问
- 不做复杂查询

### L1：本地恢复层

- checkpoint 文件
- delta journal
- 回放索引

用途：

- 保存工况
- 快速恢复任意时间点
- 崩溃恢复

### L2：时序历史层

- IoTDB 主历史库
- Influx 可选分析副本

用途：

- 趋势查询
- 报警/事件查询
- 降采样
- 长期归档

## 9.2 历史采样策略建议

不要把所有数据都按同一周期写历史。

建议分 4 类：

### H1：过程点原始历史

例如：

- AI / AO / DI / DO / PI / 状态点

写入策略：

- 按变化写
- 或按最小周期采样写

### H2：事件/报警流

例如：

- 报警产生
- 确认
- 复归
- SOE

写入策略：

- 事件驱动

### H3：回放必要内部状态

例如：

- PID 积分项
- timer
- step
- latch
- 关键内部 flags

写入策略：

- 只对被标记为 `ReplayRequired` 的内部状态写
- 周期可低于主扫描频率

### H4：周期 checkpoint

例如每：

- 30 秒
- 1 分钟
- 5 分钟

做一次完整快照或 dirty-page 快照。

## 9.3 任意时刻恢复的实际工作流

### 场景 A：恢复最近 24h 任意时刻

走：

- 本地 checkpoint + local delta

最快

### 场景 B：恢复近 30 天任意时刻

走：

- 本地 checkpoint 索引
- delta 从 IoTDB 回拉

可接受

### 场景 C：只需要看趋势，不需要恢复运行工况

走：

- 直接查 IoTDB / Influx

---

## 10. 具体推荐的技术实现

## 10.1 运行时

### 语言

- `C# / .NET 8`

### 原则

- 不用 AOT
- 不用把功能码编成 C++
- 保留动态编译和可卸载插件

### 关键技术

- `AssemblyLoadContext`
- Roslyn Compilation
- Source Generator
- `Span<T>` / `MemoryMarshal`
- `NativeMemory`
- `System.IO.Pipelines`

## 10.2 连续内存

### 实现建议

- `NativeMemory.Alloc` 或 `VirtualAlloc`
- page size 对齐
- dirty page bitmap
- checkpoint header + page table

### 不建议

- 继续大量依赖反射和对象图遍历做 Save/Load
- 把 RTD 热路径直接改成数据库读写

## 10.3 本地恢复层

### 推荐格式

- `snapshot-<ts>.img`
- `journal-<date>.wal`
- `manifest.json`

### 可选优化

- LZ4 / Zstd
- 只压缩冷页
- 对实时恢复优先保持未压缩

## 10.4 Historian

### 主推荐

- IoTDB

### 接入方式

- C# native client
- 批量写入
- 专用 writer 线程
- 与扫描周期解耦

### 写入格式建议

#### 外部过程点表

- tags: `plant`, `dpu`, `point`
- fields: `value`, `quality`, `forced`, `source_ts`

#### 事件表

- tags: `dpu`, `event_type`, `object_id`
- fields: `state`, `message`, `severity`

#### 回放内部状态表

- tags: `dpu`, `block`, `field`
- fields: `value`

#### checkpoint 索引表

- tags: `dpu`, `checkpoint_id`
- fields: `snapshot_path`, `journal_seq`, `schema_version`

## 10.5 Influx 的建议用途

- 下游报表
- 对外开放查询接口
- 降采样
- AI / 分析场景

如果要启用 Influx 3 Core 的 downsampling，可以利用它的官方 downsampler plugin，但我不建议把核心回放链路建在这个机制上。

---

## 11. 这套方案下的性能判断

## 11.1 周期执行性能

如果按这个方案实施，热路径性能不应该比当前差很多，原因是：

- DPU 周期仍然只操作连续内存
- historian 异步化
- checkpoint 异步化
- 代码热替换只在周期边界切换

所以主扫描环路理论上仍可维持“内存级”成本。

## 11.2 工况保存性能

如果使用：

- dirty page checkpoint
- 连续内存快照

那工况保存性能仍然会明显快于：

- 对象图序列化
- 从 TSDB 现查拼快照

## 11.3 任意时刻恢复性能

恢复速度主要取决于：

- checkpoint 周期
- delta 日志大小
- 是否需要从 IoTDB 回拉 replay 区间

所以要按业务目标定策略：

- 如果要求“秒级恢复任意时刻”，checkpoint 周期就不能太长
- 如果允许“分钟级恢复”，checkpoint 周期可以放大

---

## 12. 最终推荐方案

## 12.1 我建议的主路线

### 路线名

**C# 热更新友好的连续内存运行时 + IoTDB 主历史库 + checkpoint/journal 回放层**

### 具体组成

- 运行时：`C# / .NET 8`
- 功能码：继续 C#
- 热更新：
  - 调试态用 Hot Reload
  - 运行态用 Roslyn + `AssemblyLoadContext`
- 运行态存储：连续非托管内存
- 工况：checkpoint + delta journal
- 历史：IoTDB
- 分析/报表：可选 InfluxDB

## 12.2 不建议的路线

### 不建议 1

把 IoTDB / InfluxDB 当 RTD 热路径

### 不建议 2

把“工况恢复”完全建立在时序查询之上

### 不建议 3

为了连续内存性能，把功能码重新改成 C++

因为这会直接丢掉你们现有最重要的调试体验。

---

## 13. 建议的实施顺序

## 第一阶段：先做边界重构，不改功能码语言

1. 抽出 `RuntimePlan`
2. 实现 `RuntimeMemoryImage`
3. 实现新的 checkpoint / journal
4. 先把旧 `Function` 通过适配层跑在新内核上

## 第二阶段：做功能码热替换框架

1. Roslyn 编译服务
2. `AssemblyLoadContext` 管理
3. 周期边界切换
4. state schema 校验与迁移

## 第三阶段：接入 IoTDB 主历史库

1. 过程点落库
2. 事件落库
3. 必要内部状态落库
4. checkpoint 索引落库

## 第四阶段：实现任意时间点恢复

1. 本地 checkpoint 恢复
2. local journal replay
3. IoTDB 补 replay
4. 形成统一“时间点加载工况”接口

## 第五阶段：可选接入 Influx 分析层

只在确实有需要时再做。

---

## 14. 最后给出一句更直接的建议

如果现在要定技术路线，我会这样定：

### 定案

**保留 C# 功能码，不改。**

**重写运行时内核，但保留连续内存。**

**工况与历史在逻辑上统一为“时间轴状态系统”，但底层必须是 checkpoint/journal + IoTDB 分层。**

**InfluxDB 不做主库，只做辅助分析或降采样副本。**

这条路最符合你现在提出的三个新增要求，而且不会把你们原来最值钱的能力重构掉。

---

## 15. 官方资料参考

### .NET / C# 动态代码与热更新

- Visual Studio Hot Reload 支持的代码修改：
  - https://learn.microsoft.com/en-us/visualstudio/debugger/supported-code-changes-csharp
- .NET 可卸载程序集 / `AssemblyLoadContext`：
  - https://learn.microsoft.com/zh-cn/dotnet/standard/assembly/unloadability
- Roslyn SDK：
  - https://learn.microsoft.com/zh-cn/dotnet/csharp/roslyn-sdk/
- Roslyn 官方仓库：
  - https://github.com/dotnet/roslyn

### Apache IoTDB

- 官网：
  - https://iotdb.apache.org/
- 最新发布历史：
  - https://iotdb.apache.org/UserGuide/latest/IoTDB-Introduction/Release-history_apache.html
- C# Native API：
  - https://iotdb.apache.org/UserGuide/latest/API/Programming-CSharp-Native-API.html
- TTL：
  - https://iotdb.apache.org/UserGuide/latest-Table/Basic-Concept/TTL-Delete-Data_apache.html
- 表管理：
  - https://iotdb.apache.org/UserGuide/latest-Table/Basic-Concept/Table-Management_apache.html
- 数据导入 / TsFile Auto-Loading：
  - https://iotdb.apache.org/UserGuide/latest/Tools-System/Data-Import-Tool_apache.html
- 数据订阅 API：
  - https://iotdb.apache.org/UserGuide/V1.3.x/API/Programming-Data-Subscription.html
- 系统表 / pipe plugins：
  - https://iotdb.apache.org/UserGuide/latest-Table/Reference/System-Tables_apache.html
- UDF：
  - https://iotdb.apache.org/UserGuide/latest-Table/SQL-Manual/UDF_apache.html
- Trigger：
  - https://iotdb.apache.org/UserGuide/latest/User-Manual/Trigger.html
- Flink：
  - https://iotdb.apache.org/UserGuide/latest/Ecosystem-Integration/Flink-IoTDB.html

### InfluxDB 3 Core

- retention：
  - https://docs.influxdata.com/influxdb3/core/reference/internals/data-retention/
- durability / WAL / object store：
  - https://docs.influxdata.com/influxdb3/core/reference/internals/durability/
- backup / restore：
  - https://docs.influxdata.com/influxdb3/core/admin/backup-restore/
- processing engine：
  - https://docs.influxdata.com/influxdb3/core/plugins/
- 官方 downsampler plugin：
  - https://docs.influxdata.com/influxdb3/core/plugins/library/official/downsampler/

---

## 16. 附录：用时序数据库 + 流计算构造虚拟 DCS Runtime 的可行性评估

## 16.1 这个想法为什么有吸引力

这个想法本身是成立的，而且有现实基础。

因为你们现在的虚拟 DCS 已经做了两件很关键的事：

1. 通过 I/O map，把原 DCS I/O 功能模块改接到 Apros 这类仿真系统
2. 本质上已经把“控制逻辑”和“设备本体”分离开了

一旦设备本体在仿真系统里，DCS runtime 看起来就会越来越像一个“对时间序列做变换和控制输出的数据流引擎”。

从这个角度看，下面这个设想是自然的：

- 测点持续写入 IoTDB
- 利用 IoTDB 的 Last/缓存能力维护当前值
- 利用 UDF / Trigger / Pipe / Subscription / Flink 这类能力做流处理
- 用 Redis/Valkey 做热点 current-state cache
- 把控制逻辑实现为外挂的用户自定义函数或服务

这在**监督控制、策略控制、优化控制、虚拟仿真编排**上非常有想象力。

## 16.2 官方能力上，IoTDB 确实已经具备一些关键积木

从 Apache IoTDB 官方资料看，它现在确实已经具备几块你这个想法需要的基础能力：

### 1) UDF

IoTDB 最新 table mode 文档明确支持三类 UDF：

- `UDSF`
- `UDAF`
- `UDTF`

并且支持：

- 动态注册
- 通过 URI 分发 JAR
- 无需重启装卸

### 2) Trigger

官方 Trigger 文档说明：

- 支持 `BEFORE INSERT` / `AFTER INSERT`
- 支持 `STATELESS` / `STATEFUL`
- 支持动态注册和删除

但文档也明确说明：

- 当前 Trigger 是**同步触发**
- 如果 Trigger 逻辑慢，会明显影响写入性能
- 触发顺序当前**不保证**

这对“把 Trigger 当控制扫描器”是一个很重要的限制。

### 3) Pipe / Subscription

IoTDB 已有：

- Data Subscription
- Pipe
- 历史 + 实时同步拆分
- 与 Flink 的集成

这使它很适合作为：

- 数据流源
- 历史/实时混合同步基座
- 外部流处理平台的数据入口

### 4) Last Query / FastLastQuery

官方历史文档和最新版 release history 都提到：

- Last query 有专门缓存优化
- 新版 SDK 增加了 `FastLastQuery`
- 多序列 last query 有性能优化

这说明 IoTDB 作为“当前值视图”的能力在持续增强。

## 16.3 这个方案最大的问题，不是能不能做，而是“控制语义不对等”

如果把 IoTDB + cache + stream engine 直接当成 DCS runtime，会马上遇到 5 个问题。

### 问题 1：Last Value 不等于运行时状态

DCS runtime 不只是“每个点最后一个值”。

它还包含：

- PID 积分项
- Timer 剩余时间
- Step 顺控步号
- 锁存态
- 边沿触发历史位
- 同一扫描周期内的计算顺序
- 多点同时更新的原子性

Last cache 只能替代“最新测点值”，不能替代“完整控制状态”。

### 问题 2：流计算不天然具备扫描周期的确定性

DCS/PLC runtime 的关键语义是：

- 有固定扫描周期
- 有固定执行顺序
- 周期内读取和写出边界明确
- 同一周期的状态转移可重复

而时序流计算更偏：

- 事件驱动
- watermark / 窗口
- 异步调度
- backpressure
- 至少一次 / 至多一次 / 恰好一次语义

这些语义对数据处理很好，但和控制扫描语义不是一回事。

### 问题 3：把控制逻辑塞进 UDF/Trigger，会把写入链路绑住

IoTDB 官方 Trigger 文档已经明确：

- Trigger 目前是同步 fire
- 慢 Trigger 会拖累写性能

所以如果把大量控制逻辑直接挂在 Trigger 上：

- 仿真侧写点会被控制逻辑阻塞
- 控制与存储耦合
- 故障隔离会变差

### 问题 4：Redis / Valkey 可以做 cache，但不是 RTD 语义替代

Redis/Valkey 擅长：

- 最新值缓存
- 发布订阅
- 控制面缓存
- 分布式协调

但它不天然提供：

- 扫描周期一致性
- pin/连线语义
- 功能块内部状态布局
- 工况 checkpoint/replay

所以它可以补 RTD 的一部分“读取当前值”能力，但不能单独替代 RTD。

### 问题 5：外挂 AI / Claude Code 可以生成逻辑，但不能直接充当硬实时执行器

如果你的意思是：

- 用 Claude Code / 外挂代码生成器来产出 UDF 或流处理逻辑

这是可行的。

但如果意思是：

- 让外挂 AI 在运行时直接替代确定性控制逻辑

那就不适合作为底层闭环控制。

原因是：

- 不确定性
- 可验证性弱
- 回放一致性差
- 运行延迟不可控

所以 AI/外挂代码更适合：

- 生成逻辑
- 生成规则
- 生成 UDF 模板
- 生成监督控制策略

而不适合直接做主扫描器。

## 16.4 这个方向正确的落点：不是替代底层 runtime，而是增加“上层数据流 runtime”

我更推荐把这个思路落在下面这个层次结构里：

### L0：确定性控制 runtime

仍然保留：

- 本地连续内存
- 固定扫描周期
- 功能块执行图
- checkpoint/replay

### L1：时序数据平面

由 IoTDB / cache 承担：

- 所有观测点
- 关键内部状态
- 报警事件
- 当前值视图
- 历史时间轴

### L2：流计算 / 监督控制 runtime

由下面这些承担：

- IoTDB UDF
- IoTDB Trigger
- Pipe / Subscription
- Flink / Spark
- Python worker
- 外挂的 Claude Code 生成的策略函数

### L3：回写控制面

最终把高层计算结果回写成：

- 建议设定值
- 优化修正量
- 软测量点
- 监督级命令

再通过受控入口回写给 L0 runtime。

## 16.5 这条路线最适合哪些场景

很适合：

- 虚拟 DCS
- 培训仿真系统
- 慢周期优化控制
- 机组协调优化
- 软测量
- AI 辅助决策
- 数字孪生监督控制

不适合直接承接：

- 高频 PID
- 保护逻辑
- 联锁
- 定时器密集逻辑
- 硬实时顺控

## 16.6 如果一定要尝试“TSDB 驱动 runtime”，建议限定边界

如果你们确实想做一个实验性 runtime，我建议把它限定在：

### 运行条件

- 扫描周期 >= 500ms，最好 >= 1s
- 虚拟仿真用途
- 无安全保护责任
- 允许异步/事件驱动

### 技术边界

- IoTDB 做主时序平面
- Redis/Valkey 做 current-state cache
- 流计算做监督策略
- 真正的底层块状态仍做本地 state checkpoint

### 不要越线

不要把下面这些交给外部流引擎：

- 闭环硬控制
- 扫描周期内原子状态机
- 需要确定顺序的联锁链

## 16.7 我对这条想法的最终判断

### 判断一

**它值得做，但不应该替代底层 runtime。**

### 判断二

**它非常适合作为“第二控制面”或“监督控制面”。**

### 判断三

如果你们真想做出区别于传统 DCS 的新一代虚拟控制平台，这条路线反而可能是你们最有特色的方向：

- 底层是确定性控制 runtime
- 上层是时序数据库 + 流计算 + AI 生成逻辑

也就是：

**双层 runtime**

而不是：

**单层 TSDB runtime**

## 16.8 基于这个新想法，我建议把总路线升级成两个并行方向

### 主线

`Deterministic Runtime`

特征：

- 连续内存
- C# 主路径
- checkpoint/replay
- IoTDB historian

### 创新线

`Streaming Supervisor Runtime`

特征：

- IoTDB + cache + Pipe/Subscription
- UDF / Flink / Python / AI 生成逻辑
- 只做监督层、优化层、软测量层

如果两个方向并行推进，主线保证你们现在的工程价值不丢，创新线负责打开新范式。
