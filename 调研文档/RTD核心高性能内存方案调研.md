# RTD 核心高性能内存方案调研

## 需求分析

RWVDCS(III) 的 RTD（实时数据库）核心需要管理大量异构数据对象，并提供高速访问、值搬运和快速持久化能力。

### 数据特征

| 类别 | 说明 | 结构复杂度 |
|------|------|------------|
| 普通数据点（AI/AO/DI/DO） | 代表现场仪表信号，包含 PV、质量码、时间戳等字段 | 低（数十字节） |
| LA（逻辑模拟量） | 中间计算变量 | 低 |
| LD（逻辑开关量） | 中间逻辑变量 | 低 |
| 功能块（Function Block） | 控制逻辑单元（PID、选择器、序列等），包含多个 Pin、内部状态、配置参数 | 高（数百至数千字节） |

### 性能需求

- **高速访问**：所有数据点和功能块需被高频读写（控制周期 100ms~1s）
- **值搬运（Wiring）**：连线关系驱动的值拷贝（Point → Pin），等价于 memcpy 语义，每周期执行数千至数万次
- **快速存储/加载**：整个工程的所有数据区（通常 50~200MB）需在数百毫秒内完成 Save/Load
- **安全可靠**：不能因内存越界或指针错误导致崩溃（当前 `AccessViolationException` 问题的根因）

### 当前实现

当前 RTD 使用 `byte[]`（pinned）+ `MemorySlot` 结构体管理内存：

```
┌─ WorkPageList ────────────────────────────────────────┐
│  Page 0: byte[204800]   ← GCHandle.Pinned            │
│  ┌──────┬──────┬──────┬───────┬──────┐                │
│  │ SID0 │ SID1 │ SID2 │ SID3  │ ...  │  ← 变量连续排列 │
│  └──────┴──────┴──────┴───────┴──────┘                │
│  Page 1: byte[204800]                                 │
│  ┌──────┬──────┬──────┐                               │
│  │ SID98│SID99 │SID100│                               │
│  └──────┴──────┴──────┘                               │
└───────────────────────────────────────────────────────┘
         ↕ MemorySlot(data, offset, length)
         ↕ Unsafe.ReadUnaligned / WriteUnaligned
```

Save/Load 通过 `BinaryWriter/BinaryReader` 将全部页面数据写入/读出流，性能瓶颈在于：
- 需要将所有页面拷贝到中间 `byte[]` 再写入流
- Load 时需要完整读取后再分发到各页面

---

## 方案一：Memory-Mapped File（最推荐）

### 核心思路

所有数据直接存放在操作系统的内存映射文件中。"保存"不需要序列化——数据已经在文件上，`FlushViewOfFile` 仅刷脏页。"加载"就是重新映射文件，按需换入。

```
┌─────────────────────────────┐
│  rtd_data.bin（磁盘文件）     │
│  ┌───┬───┬───┬─────┬───┐    │
│  │PT1│PT2│LA1│ FC1 │LD1│... │  ← 连续内存布局
│  └───┴───┴───┴─────┴───┘    │
└─────────────────────────────┘
         ↕ CreateFileMapping + MapViewOfFile
┌─────────────────────────────┐
│  进程虚拟地址空间              │
│  byte* base = MapViewOfFile │
│  读写就是普通内存操作           │
└─────────────────────────────┘
         ↕ MemorySlot / Span<byte>
┌─────────────────────────────┐
│  .NET 应用代码                │
└─────────────────────────────┘
```

### 性能特征

| 操作 | 耗时 | 说明 |
|------|------|------|
| Save（异步） | ≈ 0 | 脏页由 OS 异步写回，应用无感 |
| Save（强制 flush） | 10~100ms | 仅写入脏页，200MB 中通常只有少量脏页 |
| Load | ≈ 打开文件时间 | 数据按需换入（page fault），首次全量访问约 50~200ms（SSD） |
| 读写访问 | 与普通内存相同 | 地址稳定，无 GC 压力 |

### 技术实现

**不需要 C++ 库**。.NET Framework 4.7.2 原生支持 `System.IO.MemoryMappedFiles`：

```csharp
// 创建或打开映射文件
var mmf = MemoryMappedFile.CreateFromFile(
    "rtd_data.bin",
    FileMode.OpenOrCreate,
    "RTD_SharedMem",
    capacity: 200 * 1024 * 1024);

// 获取视图访问器
var accessor = mmf.CreateViewAccessor(0, capacity);

// 读写数据（类似当前 MemorySlot 的用法）
float pv = accessor.ReadSingle(offset);
accessor.Write(offset, newValue);

// Save = flush
accessor.Flush();
```

### 优势

- 改动最小：当前 `byte[]` 页面可以直接替换为映射视图
- Save/Load 性能提升 1~2 个数量级
- 地址稳定（映射后不会移动），兼容现有的 IntPtr 使用方
- 崩溃安全：即使进程崩溃，已 flush 的数据不丢失
- 支持进程间共享（未来可用于多 DPU 通信）

### 劣势

- 文件大小需要预分配（但可以用 sparse file 优化）
- 内存映射区不受 GC 管理，需要手动管理生命周期
- 需要处理文件增长（当前页面动态增长的模式需要调整）

---

## 方案二：LMDB（Lightning Memory-Mapped Database）

### 简介

LMDB 是 OpenLDAP 项目出品的嵌入式键值数据库，底层使用 B+ 树 + 内存映射文件。被 Caffe（深度学习框架）、NVIDIA、Monero（加密货币）等大量项目采用。

### 关键特性

- **零拷贝读取**：`mdb_get` 返回的是直接指向 mmap 内存的指针，无需反序列化
- **ACID 事务**：完整的事务支持，崩溃后自动恢复
- **无锁读**：读操作不需要锁，支持一写多读并发
- **单文件部署**：数据库就是一个文件 + 一个锁文件
- **极小的代码量**：整个库约 10000 行 C 代码

### 性能数据（官方基准）

| 操作 | 吞吐量 |
|------|--------|
| 随机读 | ~1,000,000 ops/sec |
| 顺序写 | ~100,000 ops/sec |
| 批量写（单事务） | ~500,000 ops/sec |

### .NET 绑定

NuGet 包：`LightningDB`

```csharp
using LightningDB;

// 打开数据库
var env = new LightningEnvironment("rtd_db");
env.MapSize = 200 * 1024 * 1024;
env.Open();

// 写入
using (var tx = env.BeginTransaction())
using (var db = tx.OpenDatabase())
{
    byte[] key = BitConverter.GetBytes(sid);
    byte[] value = pointData; // 序列化后的数据
    tx.Put(db, key, value);
    tx.Commit();
}

// 读取（零拷贝）
using (var tx = env.BeginTransaction(TransactionBeginFlags.ReadOnly))
using (var db = tx.OpenDatabase())
{
    var (resultCode, key, value) = tx.Get(db, keyBytes);
    // value.AsSpan() 直接指向 mmap 内存，无拷贝
}
```

### 适用场景

- 需要**事务安全性**（崩溃不丢数据，自动恢复）
- 需要**键值查询**（按名称或 SID 查找变量）
- 未来需要**进程间共享**数据

### 不适用场景

- 当前 RTD 的连续内存布局（LMDB 是 B+ 树，不是平坦 arena）
- 需要对整个内存区做 memcpy 式的值搬运
- 对写入延迟极度敏感的场景（事务提交有 fsync 开销）

---

## 方案三：Boost.Interprocess（C++ 内存管理）

### 简介

Boost.Interprocess 提供跨进程共享内存管理，支持在共享内存段或内存映射文件中使用 STL 兼容的分配器和容器。

### 示例

```cpp
#include <boost/interprocess/managed_mapped_file.hpp>
#include <boost/interprocess/allocators/allocator.hpp>

namespace bip = boost::interprocess;

// 创建/打开 200MB 的内存映射文件
bip::managed_mapped_file segment(
    bip::open_or_create, "rtd_data.bin", 200 * 1024 * 1024);

// 定义数据结构
struct DataPoint {
    float pv;
    int quality;
    int64_t timestamp;
};

// 在映射区内分配对象
auto* pt = segment.construct<DataPoint>("PT001")(1.0f, 0, 0);

// 直接读写
pt->pv = 3.14f;

// 保存 = 刷盘
segment.flush();
```

### 与 .NET 集成

需要通过 C++/CLI 或 P/Invoke 桥接：

```cpp
// C++ DLL 导出函数
extern "C" __declspec(dllexport)
void* rtd_open(const char* path, size_t size);

extern "C" __declspec(dllexport)
float rtd_read_float(void* handle, int offset);

extern "C" __declspec(dllexport)
void rtd_write_float(void* handle, int offset, float value);

extern "C" __declspec(dllexport)
void rtd_flush(void* handle);
```

```csharp
// C# P/Invoke
[DllImport("rtd_native.dll")]
static extern IntPtr rtd_open(string path, long size);

[DllImport("rtd_native.dll")]
static extern float rtd_read_float(IntPtr handle, int offset);
```

### 优势

- Boost 生态，成熟可靠
- 支持在映射内存中使用 STL 容器
- 跨进程共享

### 劣势

- 需要引入 C++ 编译工具链
- P/Invoke 桥接增加复杂度
- Boost 库体积较大

---

## 方案四：Apache Arrow（列式内存格式）

### 简介

Apache Arrow 定义了一种语言无关的列式内存格式，专为分析型工作负载优化。核心 C++ 实现，有官方 C# 绑定。

### 数据模型

```
传统行式布局（当前 RTD）：        Arrow 列式布局：
┌─────────────────────┐        ┌───────┬───────┬───────┐
│ PT1: pv=1.0, q=0    │        │  PV   │Quality│Timestamp│
│ PT2: pv=2.5, q=0    │        ├───────┼───────┼───────┤
│ PT3: pv=3.7, q=1    │        │  1.0  │   0   │  T1   │
│ ...                  │        │  2.5  │   0   │  T2   │
└─────────────────────┘        │  3.7  │   1   │  T3   │
                                └───────┴───────┴───────┘
```

### .NET 使用

NuGet 包：`Apache.Arrow`

```csharp
using Apache.Arrow;
using Apache.Arrow.Ipc;

// 构建数据
var pvBuilder = new FloatArray.Builder();
pvBuilder.Append(1.0f);
pvBuilder.Append(2.5f);

var schema = new Schema(new[] {
    new Field("pv", FloatType.Default, false)
});

var batch = new RecordBatch(schema, new[] { pvBuilder.Build() }, 2);

// 写入文件（IPC 格式，可零拷贝读回）
using var stream = File.OpenWrite("data.arrow");
using var writer = new ArrowFileWriter(stream, schema);
writer.WriteRecordBatch(batch);
```

### 适用场景

- 大量同类型数据的**批量计算**（如趋势分析、报表统计）
- 与 Python/Pandas 生态互操作
- 数据导出/交换

### 不适用场景

- RTD 核心的行式随机访问模式
- 功能块等复杂嵌套结构
- 实时值搬运（连线 memcpy）

---

## 方案五：Flatbuffers（Google 零拷贝序列化）

### 简介

Google 出品的跨平台序列化库，与 Protocol Buffers 类似但强调零拷贝——序列化后的二进制数据可以直接读取，无需解析步骤。

### Schema 定义

```flatbuffers
// rtd.fbs
namespace VDCS;

struct Vec3 {
  x: float;
  y: float;
  z: float;
}

table DataPoint {
  sid: int;
  name: string;
  pv: float;
  quality: int;
  timestamp: long;
}

table FunctionBlock {
  sid: int;
  name: string;
  type_name: string;
  inputs: [DataPoint];
  outputs: [DataPoint];
  parameters: [ubyte]; // 内部状态的原始字节
}

table RTDSnapshot {
  points: [DataPoint];
  blocks: [FunctionBlock];
  wiring: [ubyte]; // 连线表的原始字节
}

root_type RTDSnapshot;
```

### 性能特征

| 操作 | Flatbuffers | Protocol Buffers | JSON |
|------|-------------|-----------------|------|
| 序列化 | 很快 | 快 | 慢 |
| 反序列化 | **零**（直接访问） | 需要解析 | 很慢 |
| 内存占用 | 1x | 1~2x | 5~10x |

### 适用场景

- 工程文件的快速保存/加载
- 网络传输（DPU 之间的数据同步）
- 需要跨语言访问的配置数据

### 不适用场景

- 运行时的频繁原地修改（设计上偏向不可变）
- 当前 RTD 的 arena 式内存管理

---

## 方案六：自写 C++ 内存池 + P/Invoke

### 架构

类似 NumPy 的模式：C++ 层负责内存管理和高性能计算，.NET 层负责业务逻辑。

```
┌──────────────────────────────────────────────┐
│  .NET 层（C#）                                │
│  ┌────────────────────────────────────────┐  │
│  │  MemorySlot / RTD API                  │  │
│  │  功能块执行引擎 / 连线引擎              │  │
│  └──────────────┬─────────────────────────┘  │
│                 │ P/Invoke                    │
│  ┌──────────────▼─────────────────────────┐  │
│  │  rtd_native.dll（C++）                  │  │
│  │  ┌─────────────────────────────────┐   │  │
│  │  │  Arena Allocator                │   │  │
│  │  │  ┌───┬───┬───┬─────┬───┐       │   │  │
│  │  │  │PT1│PT2│LA1│ FC1 │LD1│       │   │  │
│  │  │  └───┴───┴───┴─────┴───┘       │   │  │
│  │  │  mmap 后端 / 对齐内存            │   │  │
│  │  ├─────────────────────────────────┤   │  │
│  │  │  Wire Engine（SIMD 优化）       │   │  │
│  │  │  批量 memcpy + 位反转            │   │  │
│  │  ├─────────────────────────────────┤   │  │
│  │  │  Snapshot（快照）                │   │  │
│  │  │  整块 write / mmap flush        │   │  │
│  │  └─────────────────────────────────┘   │  │
│  └────────────────────────────────────────┘  │
└──────────────────────────────────────────────┘
```

### 可复用的开源组件

| 组件 | 库 | 用途 |
|------|-----|------|
| 内存分配器 | [mimalloc](https://github.com/microsoft/mimalloc)（MIT） | 微软出品，高性能通用分配器 |
| Arena 分配 | [tlsf](https://github.com/mattconte/tlsf)（BSD） | O(1) 时间复杂度的内存分配 |
| 压缩 | [lz4](https://github.com/lz4/lz4)（BSD） | 极速压缩，适合快照压缩存储 |
| SIMD 工具 | [xsimd](https://github.com/xtensor-stack/xsimd)（BSD） | 跨平台 SIMD 抽象，加速批量值搬运 |
| 内存映射 | [mio](https://github.com/vimpunk/mio)（MIT） | 跨平台 mmap 封装 |

### P/Invoke 接口设计

```cpp
// rtd_native.h
extern "C" {
    // 生命周期
    RTD_HANDLE rtd_create(const char* file_path, size_t capacity);
    void       rtd_destroy(RTD_HANDLE h);

    // 内存分配
    int        rtd_alloc(RTD_HANDLE h, uint32_t size);  // 返回 SID
    void       rtd_free(RTD_HANDLE h, int sid);

    // 直接访问（返回映射区内的指针）
    void*      rtd_get_ptr(RTD_HANDLE h, int sid);
    int        rtd_get_size(RTD_HANDLE h, int sid);

    // 批量值搬运
    void       rtd_wire_copy(RTD_HANDLE h,
                   int dst_sid, uint32_t dst_offset,
                   int src_sid, uint32_t src_offset,
                   uint32_t length, bool reversed);

    // 快照
    bool       rtd_save(RTD_HANDLE h);   // flush mmap
    bool       rtd_load(RTD_HANDLE h);   // remap
}
```

```csharp
// C# 侧
static class RtdNative
{
    [DllImport("rtd_native.dll")]
    public static extern IntPtr rtd_create(string path, long capacity);

    [DllImport("rtd_native.dll")]
    public static extern int rtd_alloc(IntPtr h, uint size);

    [DllImport("rtd_native.dll")]
    public static extern IntPtr rtd_get_ptr(IntPtr h, int sid);

    [DllImport("rtd_native.dll")]
    public static extern void rtd_wire_copy(IntPtr h,
        int dstSid, uint dstOffset,
        int srcSid, uint srcOffset,
        uint length, bool reversed);

    [DllImport("rtd_native.dll")]
    public static extern bool rtd_save(IntPtr h);
}
```

---

## 方案对比总结

| 维度 | MemoryMappedFile | LMDB | Boost.Interprocess | Arrow | Flatbuffers | C++ 自写 |
|------|:---:|:---:|:---:|:---:|:---:|:---:|
| 改动量 | **小** | 中 | 大 | 大 | 中 | **大** |
| Save 性能 | **极快** | 快 | **极快** | 快 | 快 | **极快** |
| Load 性能 | **极快** | 快 | **极快** | 快 | **极快** | **极快** |
| 随机读写 | **快** | 快 | **快** | 一般 | 一般 | **快** |
| 值搬运(Wiring) | 快 | 一般 | 快 | 一般 | 不适用 | **极快(SIMD)** |
| 崩溃安全 | 中 | **高** | 中 | 无 | 无 | 中 |
| 事务支持 | 无 | **有** | 无 | 无 | 无 | 无 |
| .NET 集成 | **原生** | NuGet | P/Invoke | NuGet | NuGet | P/Invoke |
| 需要 C++ | **否** | 否 | 是 | 否 | 否 | **是** |
| 许可证 | - | OpenLDAP(BSD类) | BSL-1.0 | Apache-2.0 | Apache-2.0 | - |

---

## 推荐路径

### 短期（改动最小，效果显著）

**使用 .NET MemoryMappedFile 替换当前 byte[] 页面**

- 当前架构已有 `byte[] data` + `GCHandle.Pinned`，替换为 `MemoryMappedViewAccessor` 语义等价
- Save 从"遍历页面 → 拷贝到中间 buffer → stream.Write"变为 `FlushViewOfFile`
- Load 从"stream.Read → 拷贝到页面"变为重新 `MapViewOfFile`
- 预计性能提升：Save/Load 从秒级降到百毫秒级

### 中期（如果需要事务安全）

**引入 LMDB 作为持久化后端**

- RTD 运行时仍使用内存映射区（直接访问）
- 持久化操作通过 LMDB 事务保证一致性
- 崩溃后可以从最近的事务点恢复

### 长期（如果需要极致性能）

**开发 C++ native 层**

- Arena allocator + mmap 后端
- SIMD 优化的批量值搬运引擎
- 通过 P/Invoke 暴露给 .NET
- 类似 NumPy 的架构：native 负责性能关键路径，.NET 负责业务逻辑

---

## 参考资源

- [LMDB 官方文档](http://www.lmdb.tech/doc/)
- [LMDB .NET 绑定 (LightningDB)](https://github.com/CoreyKaylor/Lightning.NET)
- [Boost.Interprocess 文档](https://www.boost.org/doc/libs/release/doc/html/interprocess.html)
- [Apache Arrow C# 文档](https://arrow.apache.org/docs/csharp/)
- [Flatbuffers 官方文档](https://google.github.io/flatbuffers/)
- [mimalloc (Microsoft)](https://github.com/microsoft/mimalloc)
- [lz4 压缩库](https://github.com/lz4/lz4)
- [mio - 跨平台 mmap](https://github.com/vimpunk/mio)
- [.NET MemoryMappedFile 文档](https://docs.microsoft.com/dotnet/api/system.io.memorymappedfiles.memorymappedfile)
