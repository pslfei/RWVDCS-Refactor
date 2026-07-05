# RTD 实时数据库架构分析与演进方案

## 一、当前系统架构特征

| 特征 | 当前实现 | 说明 |
|------|---------|------|
| 结构化数据在 flat buffer 中直接访问 | `byte[]` + `Marshal.ReadXxx` | 零反序列化 |
| 高频读写（每周期更新值） | 直接写 byte[] 偏移 | 毫秒级周期 |
| 连线值搬运 | `Buffer.BlockCopy` | 源 Pin → 目标 Pin |
| 整体 Save/Load | `BinaryReader` 直读 byte[] | 百毫秒级 |
| 复杂嵌套结构 | Syncblk + TypeHandle + 字段 + 子对象 | Function Block |
| 数据点 | LA / LD / LP 等值类型 | 大量同构数据 |

---

## 二、核心发现：FastDB — 与当前 RTD 架构高度吻合的 C++ 实时数据库

### 2.1 FastDB 简介

[FastDB](http://www.garret.ru/fastdb/FastDB.htm) 是一个 C++ 实现的**主内存嵌入式实时关系数据库**，专门为**读密集型实时应用**设计。它被用于开源 DCS/SCADA 系统 [IndigoSCADA](https://github.com/jonathanxavier/IndigoSCADA) 的核心数据引擎。

### 2.2 FastDB 与当前 RTD 的架构对比

| 特征 | 当前 RTD | FastDB | 匹配度 |
|------|---------|--------|--------|
| 内存模型 | `byte[]` pinned + `GCHandle` | 数据库文件 mmap 到虚拟内存 | ★★★★★ |
| 数据访问 | `Marshal.ReadXxx(address + offset)` | 在应用进程上下文直接访问，无上下文切换 | ★★★★★ |
| 类型管理 | `TypeManage` + `ChildTableItem` 反射 | C++ 类自动映射到数据库表，自动 schema 演化 | ★★★★★ |
| 并发控制 | `lock(this)` | 原子指令实现，几乎零开销 | ★★★★★ |
| 持久化 | `BinaryReader/Writer` 整块读写 | Shadow root page 原子更新 + 崩溃自动恢复 | ★★★★★ |
| 读优化 | 直接读 byte[] 偏移 | 假设全部数据在 RAM，查询算法据此优化 | ★★★★★ |
| 事务 | 无 | 支持事务，基于 shadow root page 协议 | FastDB 更强 |
| 崩溃恢复 | 无（byte[] 数据丢失） | 自动快速恢复，保障高可用 | FastDB 更强 |
| 共享访问 | 单进程 | 多进程共享同一 mmap 区域 | FastDB 更强 |

### 2.3 FastDB 核心特性

```
┌─────────────────────────────────────────────────────────────┐
│                    FastDB 架构                               │
├─────────────────────────────────────────────────────────────┤
│  ● 整个数据库 mmap 到每个应用进程的虚拟内存空间               │
│  ● 查询在应用进程上下文中执行，无进程切换和数据传输开销        │
│  ● 原子指令实现并发同步，几乎零锁开销                        │
│  ● Shadow root page 事务协议：原子更新 + 崩溃自动恢复        │
│  ● C++ 类 → 数据库表的自动映射，支持 schema 自动演化          │
│  ● 对象关系型：支持 SQL 子集 + 面向对象扩展                   │
│  ● 支持在线备份和复制                                        │
└─────────────────────────────────────────────────────────────┘
```

### 2.4 FastDB 对当前 RTD 痛点的解决

| 当前痛点 | FastDB 如何解决 |
|---------|----------------|
| GCHandle hack 篡改指针导致 `AccessViolationException` | mmap 直接访问，无需任何指针篡改 |
| `WiseNew` 不初始化值类型字段默认值 | C++ 类构造函数自动执行，字段默认值由编译器保证 |
| `Marshal.ReadXxx` / `WriteXxx` 类型不安全 | C++ 强类型访问，编译期检查 |
| `byte[]` 由 GC 管理，存在内存压力 | mmap 由操作系统管理，不参与 GC |
| `BinaryReader/Writer` 串行序列化 | mmap 自动持久化，Save/Load 接近零耗时 |
| 无崩溃恢复机制 | Shadow root page 原子事务 + 自动恢复 |
| 无多进程共享（HMI 不能直接读） | 多进程天然共享同一 mmap 区域 |

### 2.5 FastDB 的局限

- **无官方 C# 绑定**：需要自行构建 C++/CLI 桥接层或 P/Invoke 封装
- **不支持 Client-Server 架构**：所有应用必须在同一台机器上（适合嵌入式 DCS）
- **偏向读优化**：写密集场景性能不如读密集场景
- **项目维护节奏较慢**：最后活跃更新在 2020 年左右，但代码成熟稳定

### 2.6 参考项目：IndigoSCADA

[IndigoSCADA](https://github.com/jonathanxavier/IndigoSCADA) 是一个完整的开源 DCS/SCADA 系统，使用 FastDB 作为实时数据库引擎：

- 开发语言：ANSI C/C++98
- HMI：基于 QT
- 集成技术：FastDB、GigaBASE、ORTE
- 支持协议：OPC DA/AE/HDA、OPC UA、DNP 3.0、Modbus、MQTT
- 平台：Linux + Windows

这个项目证明了 FastDB 在 DCS 场景下的可行性。

---

## 三、其他候选方案

### 3.1 FlatSharp（FlatBuffers C# 实现）

**简介**：Google FlatBuffers 的 C# 原生实现，零拷贝二进制序列化格式。

**与当前系统的对应关系：**

```
当前系统:  byte[offset+8] → Marshal.ReadInt32 → 得到 runable
FlatSharp:  buffer.Span[vtable_offset] → 生成的 getter → 得到 runable
```

**优势：**
- 在 Microsoft、Unity3D 生产环境验证
- Schema 定义一次 → 自动生成 C# 访问器，类型安全
- 支持嵌套 table/struct，可映射 Function Block
- 基于 `Memory<T>` / `Span<T>`，安全且高性能

**局限：**
- 设计上偏向 **只读** 场景，频繁修改单字段不是它的强项

**适用评级：** 结构化访问 ★★★★★ / 高频写入 ★★★ / Save/Load ★★★★

**参考：** [GitHub](https://github.com/jamescourtney/FlatSharp) / [FlatBuffers C# 文档](https://flatbuffers.dev/languages/c_sharp/)

---

### 3.2 MemoryPack + NativeMemoryArray（Cysharp 系列）

**简介**：最接近 "NumPy for C#" 的纯 .NET 方案。

```
NumPy:            np.array(dtype=MyStruct) → 底层 C 内存 → Python 访问
NativeMemoryArray: NativeMemoryArray<T>    → 底层 NativeMemory → C# 访问
MemoryPack:        Serialize/Deserialize   → 零编码直接 memcpy → 极速 Save/Load
```

**MemoryPack 特点：**
- 零编码设计，直接拷贝 C# 内存
- 对 struct 数组序列化速度比其他方案快 **50-200 倍**（直接 memcpy）

**NativeMemoryArray 特点：**
- 使用 `NativeMemory.Alloc` 分配，不走 GC 堆，支持 >2GB

**局限：** 不处理异构嵌套结构（不同 Function Block 有不同字段布局）

**适用评级：** 结构化访问 ★★★★ / 高频写入 ★★★★★ / Save/Load ★★★★★

**参考：** [NativeMemoryArray](https://github.com/Cysharp/NativeMemoryArray) / [MemoryPack](https://github.com/Cysharp/MemoryPack)

---

### 3.3 MemoryMappedFile（.NET 内置）

**简介**：使用操作系统内核级内存映射，最务实的渐进式改进。

```csharp
var mmf = MemoryMappedFile.CreateFromFile("rtd.dat", FileMode.OpenOrCreate,
    "RTD_SharedMem", totalSize);
var accessor = mmf.CreateViewAccessor();

// 零拷贝读写结构体
accessor.Read<LA>(offset, out LA value);
accessor.Write<LA>(offset, ref value);

// Save = 什么都不用做（操作系统自动刷盘）
// Load = 重新 CreateFromFile（操作系统自动页面映射）
```

**性能关键点：**
- `ViewAccessor` 的 `Read<T>` / `Write<T>` 每次调用有 SafeHandle 锁开销
- **高吞吐场景**应使用 `AcquirePointer` 获取原始指针后用 `Span<T>` 批量操作
- 实测：通过指针直接写 256MB 数据仅需 2 秒，比 ViewAccessor 逐元素写快一倍

**优势：** Save/Load 接近零耗时 / 跨进程共享 / .NET 内置无依赖

**局限：** 不提供类型管理、字段偏移计算等高层抽象

**适用评级：** 结构化访问 ★★★ / 高频写入 ★★★★★ / Save/Load ★★★★★

**参考：** [Memory-Mapped Files and Overlaid Structs](https://blog.stephencleary.com/2023/09/memory-mapped-files-overlaid-structs.html) / [Microsoft 文档](https://learn.microsoft.com/en-us/dotnet/standard/io/memory-mapped-files)

---

### 3.4 其他 C++ 共享内存方案

| 库 | 特点 | 参考 |
|---|------|------|
| **simdb** | 无锁共享内存 KV 存储，50 万次/秒/核，单头文件 C++11 | [GitHub](https://github.com/LiveAsynchronousVisualizedArchitecture/simdb) |
| **libMdb** | C++ 映射内存数据库，STL 风格容器 API，多应用共享 | [GitHub](https://github.com/pahoughton/libmdb) |
| **Microsoft IPC** | C++ 共享内存 IPC + .NET 封装（C++/CLI），支持 Bond 序列化 | [GitHub](https://github.com/microsoft/IPC) |
| **Microsoft FASTER** | C++/C# 双版本，高性能 KV 存储，性能超越纯内存数据结构 | [官网](https://microsoft.github.io/FASTER/) |

---

## 四、综合评估矩阵

| 方案 | 结构化访问 | 高频写入 | Save/Load | 嵌套结构 | 崩溃恢复 | C# 集成 | 成熟度 |
|------|-----------|---------|-----------|---------|---------|---------|--------|
| **FastDB + C++/CLI** | ★★★★★ | ★★★★ | ★★★★★ | ★★★★★ | ★★★★★ | ★★★ | ★★★★ |
| **FlatSharp** | ★★★★★ | ★★★ | ★★★★ | ★★★★★ | ★ | ★★★★★ | ★★★★★ |
| **MemoryPack + NativeArray** | ★★★★ | ★★★★★ | ★★★★★ | ★★ | ★ | ★★★★★ | ★★★★ |
| **MemoryMappedFile** | ★★★ | ★★★★★ | ★★★★★ | ★★★ | ★★★ | ★★★★★ | ★★★★★ |
| **当前 RTD (已修复)** | ★★★ | ★★★★★ | ★★★★ | ★★★★ | ★ | ★★★★★ | ★★★ |

---

## 五、核心结论

**没有一个现成的库能完整替代当前的 RTD 系统。** 当前系统本质上是一个为 DCS 定制的实时对象数据库，同时处理：

- 异构类型（不同 FC 有不同结构）
- 对象间引用关系（Pin 连线）
- 高频字段级读写
- 整体快照式持久化

这几个需求的组合比较独特，通用库通常只覆盖其中 1-2 个。

**最接近完整替代的方案是 FastDB**（C++ 实时内存数据库），它在 IndigoSCADA 项目中已验证可用于 DCS 场景，但需要构建 C++/CLI 桥接层。

---

## 六、推荐演进路径

### 路径 A：渐进式改进（推荐，低风险）

```
Phase 1（已完成）: 修复 GCHandle hack
  → 用反射式字段搬运替代地址篡改
  → 消除 AccessViolationException

Phase 2（短期）: byte[] pinned → MemoryMappedFile
  → Save/Load 性能大幅提升（操作系统分页管理，接近零耗时）
  → 支持跨进程共享（为未来 HMI 分离做准备）

Phase 3（中期）: 值类型数据点用 NativeMemoryArray<T> 管理
  → 消除 GC 压力（原生内存，不参与垃圾回收）
  → 连线值搬运用 Span<T>.CopyTo 替代 Buffer.BlockCopy

Phase 4（长期）: Function Block 结构用 FlatSharp schema 定义
  → 类型安全，自动生成访问器
  → 消除所有 Marshal.ReadXxx / WriteXxx
```

### 路径 B：核心引擎替换（高收益，高风险）

```
Phase 1: 构建 FastDB 的 C++/CLI 桥接层
  → 将 FastDB 编译为 DLL，暴露 C# 可调用接口

Phase 2: 将 TypeManage / MemoryManage / PointManage 的核心逻辑迁移到 FastDB
  → 类型定义 → FastDB 表
  → byte[] 内存页 → FastDB mmap 区域
  → WiseNew → FastDB insert
  → ReadObjectFromAddress → FastDB query

Phase 3: 保留 C# 业务层（Command / Wire / Function）
  → 通过桥接层访问 FastDB 中的数据
  → 获得事务安全、崩溃恢复、多进程共享等能力
```

### 目标架构

```
┌──────────────────────────────────────────────────┐
│              C# 业务层                            │
│   Function / IO / Command / Wire                 │
├──────────────────────────────────────────────────┤
│          C++/CLI 桥接层 (或 P/Invoke)             │
├──────────────────────────────────────────────────┤
│            C++ Native 核心                        │
│  ┌──────────────────────────────────────────────┐│
│  │              FastDB                          ││
│  │  ● mmap 虚拟内存（零拷贝访问）                ││
│  │  ● C++ 类 → 数据库表（自动 schema）           ││
│  │  ● 原子指令并发（零锁开销）                   ││
│  │  ● Shadow root page（崩溃自动恢复）           ││
│  │  ● 多进程共享（HMI 直接读取）                 ││
│  └──────────────────────────────────────────────┘│
└──────────────────────────────────────────────────┘
```

---

## 参考链接

### 核心推荐
- [FastDB 官方主页](http://www.garret.ru/fastdb/FastDB.htm)
- [FastDB GitHub 镜像](https://github.com/gavioto/fastdb)
- [FastDB SourceForge](https://sourceforge.net/projects/fastdb/)
- [IndigoSCADA — 使用 FastDB 的开源 DCS/SCADA](https://github.com/jonathanxavier/IndigoSCADA)

### .NET 生态
- [FlatSharp — FlatBuffers for C#](https://github.com/jamescourtney/FlatSharp)
- [Cysharp/MemoryPack — 零编码极速序列化](https://github.com/Cysharp/MemoryPack)
- [Cysharp/NativeMemoryArray — 原生内存数组](https://github.com/Cysharp/NativeMemoryArray)
- [Memory-Mapped Files Overlaid Structs 模式](https://blog.stephencleary.com/2023/09/memory-mapped-files-overlaid-structs.html)

### C++ 共享内存
- [simdb — 无锁共享内存 KV](https://github.com/LiveAsynchronousVisualizedArchitecture/simdb)
- [libMdb — 映射内存数据库](https://github.com/pahoughton/libmdb)
- [Microsoft IPC](https://github.com/microsoft/IPC)
- [Microsoft FASTER](https://microsoft.github.io/FASTER/)

### 其他参考
- [Apache Arrow .NET](https://github.com/apache/arrow-dotnet)
- [NumSharp — .NET 的 NumPy](https://github.com/SciSharp/NumSharp)
- [FlatBuffers 官方 C# 文档](https://flatbuffers.dev/languages/c_sharp/)
