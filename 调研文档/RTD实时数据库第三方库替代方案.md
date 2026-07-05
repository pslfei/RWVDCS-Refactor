# RTD 实时数据库第三方库替代方案

## 一、需求摘要

寻找一款 **C++ 实现的开源库**，具备以下能力：

| 需求 | 说明 |
|------|------|
| 存储异构数据 | 既有简单 LA/LD 数据点，也有复杂的 Function Block |
| 高速字段级访问 | 周期性读写单个字段值 |
| 连线值搬运 | 功能块间基于 Pin 连线关系的值传递 |
| 快速 Save/Load | 几百毫秒级存储/加载整个工程数据区 |
| C# 可调用 | 类似 NumPy（C 底层 + Python 封装）的模式 |
| 安全可靠 | 崩溃恢复、数据完整性保障 |

---

## 二、核心推荐：libmdbx + MemoryPack

经过对 FastDB、ObjectBox、LMDB、libmdbx、FlatSharp、NativeMemoryArray 等方案的全面调研，**推荐 libmdbx + MemoryPack 组合** 作为最佳方案。

### 2.1 为什么是 libmdbx？

[libmdbx](https://github.com/erthink/libmdbx) 是 LMDB（Lightning Memory-Mapped Database）的深度改进版，C++ 实现，被半数以太坊客户端使用（Erigon、Akula、Silkworm、Reth），Apache 2.0 许可。

```
你的 RTD:    byte[] pinned → GCHandle → Marshal.ReadXxx → BinaryWriter Save
libmdbx:     mmap 文件 → 零拷贝直读 → 原子事务提交 → 操作系统自动持久化
```

**与你的 RTD 系统的能力对比：**

| 能力 | 当前 RTD | libmdbx | 优势 |
|------|---------|---------|------|
| 内存模型 | byte[] pinned（GC 管理） | mmap（操作系统管理） | 无 GC 压力 |
| 数据访问 | Marshal.ReadXxx（手工偏移） | 零拷贝直接返回内存指针 | 无内存拷贝 |
| 并发 | `lock(this)`（互斥锁） | 写者单 mutex + 读者无锁 | 读写不互相阻塞 |
| 持久化 | BinaryReader/Writer 串行 | mmap 自动持久化 | 接近零耗时 |
| 崩溃恢复 | 无（数据丢失） | Copy-on-Write 原子更新 | 崩溃后自动恢复 |
| 事务 | 无 | 完整 ACID 事务 | 数据一致性保障 |
| 多进程 | 不支持 | 多进程共享同一数据库 | HMI 可直接读取 |
| Save/Load | ~百毫秒（序列化） | ~0（mmap 即持久化） | 数量级提升 |

**性能数据（来自官方和社区测试）：**
- 单线程读取：**190 万+次/秒**（结构化对象）
- 多线程读取：**600 万+次/秒**（4/8 核）
- 单线程写入：**50 万次/秒**
- 多线程写入：**170 万次/秒**
- 比 LMDB 快 **10-30%**

### 2.2 为什么搭配 MemoryPack？

libmdbx 是 KV 存储（Key=字节, Value=字节），需要序列化层将结构化对象转为字节。[MemoryPack](https://github.com/Cysharp/MemoryPack) 是目前 .NET 生态最快的序列化库：

- struct 数组序列化比其他方案快 **50-200 倍**（直接 memcpy）
- 对普通对象比其他方案快 **10 倍**
- 零编码设计：内存布局 ≈ 序列化格式
- POCO 友好：不需要特殊基类或 Schema 文件

```csharp
// 序列化 Function Block
byte[] bytes = MemoryPackSerializer.Serialize(functionBlock);

// 反序列化
var fb = MemoryPackSerializer.Deserialize<MyFunctionBlock>(bytes);
```

**对于 LA/LD 等 blittable 值类型，甚至不需要序列化**，可以直接作为原始字节存取。

### 2.3 C# 绑定：mdbx.NET

[mdbx.NET](https://github.com/wangjia184/mdbx.NET) 是 libmdbx 的官方级 .NET 封装：

- **NuGet 包**：`mdbx.NET`，直接 `Install-Package` 即可使用
- 内置原生二进制：Windows（x86/x64/ARM）、Linux、macOS
- 完整 API：环境管理、事务、游标、多数据库

```csharp
// 打开数据库
var env = new MdbxEnvironment();
env.SetMaxDatabases(10)
   .SetMapSize(1024L * 1024 * 1024)  // 1GB
   .Open("rtd_data", EnvFlags.NoSubDir | EnvFlags.WriteMap, 0664);

// 写入一个 Function Block
using (var tran = env.BeginTransaction())
{
    var db = tran.OpenDatabase("blocks", DatabaseOption.Create | DatabaseOption.IntegerKey);
    byte[] data = MemoryPackSerializer.Serialize(myFC);
    db.Put(sid, data);
    tran.Commit();  // 原子提交，崩溃安全
}

// 读取
using (var tran = env.BeginTransaction(TransactionOption.ReadOnly))  // 无锁读
{
    var db = tran.OpenDatabase("blocks");
    byte[] data = db.Get<int, byte[]>(sid);
    var fc = MemoryPackSerializer.Deserialize<MyFunctionBlock>(data);
}

// Save = 什么都不用做（mmap 自动持久化）
// Load = 重新打开 env（mmap 自动映射）
```

### 2.4 架构映射

```
┌─────────────────────────────────────────────────────────────┐
│                    C# 业务层                                 │
│         Function / IO / Command / Wire                      │
├─────────────────────────────────────────────────────────────┤
│                    数据访问层                                 │
│  ┌─────────────────────┐  ┌───────────────────────────────┐ │
│  │  RtdStore (新封装)   │  │  WireEngine (值搬运引擎)      │ │
│  │  Get<T>(sid)        │  │  Transfer(srcSid, dstSid)     │ │
│  │  Put<T>(sid, obj)   │  │  基于 mdbx 事务的批量搬运     │ │
│  │  BulkWrite(batch)   │  │                               │ │
│  └─────────────────────┘  └───────────────────────────────┘ │
├─────────────────────────────────────────────────────────────┤
│                    序列化层                                   │
│  ┌─────────────────────┐  ┌───────────────────────────────┐ │
│  │  MemoryPack          │  │  直接字节（LA/LD blittable） │ │
│  │  (FC 等复杂对象)     │  │  MemoryMarshal.AsBytes       │ │
│  └─────────────────────┘  └───────────────────────────────┘ │
├─────────────────────────────────────────────────────────────┤
│                    存储引擎                                   │
│  ┌─────────────────────────────────────────────────────────┐ │
│  │                    libmdbx (C++)                         │ │
│  │  ● mmap 零拷贝访问    ● ACID 原子事务                   │ │
│  │  ● 读者完全无锁       ● Copy-on-Write 崩溃恢复          │ │
│  │  ● 多进程共享         ● B+Tree O(log N) 查找            │ │
│  └─────────────────────────────────────────────────────────┘ │
│                  ↕ mdbx.NET (P/Invoke)                       │
└─────────────────────────────────────────────────────────────┘
```

### 2.5 当前 RTD 各组件的迁移映射

| 当前组件 | 功能 | 迁移到 | 方式 |
|---------|------|--------|------|
| **MemoryManage** | byte[] 页面分配/管理 | libmdbx mmap | 不再需要手工管理内存页 |
| **TypeManage** | 类型注册/Schema | MemoryPack `[MemoryPackable]` | 编译期自动生成序列化代码 |
| **TypeManageItem** | 字段偏移读写 | MemoryPack 反序列化 | 类型安全，无 Marshal |
| **PointManage** | SID→对象的读写 | mdbx.NET Get/Put | KV 存取，事务安全 |
| **WiseNew** | byte[] 中创建对象布局 | `new MyFC()` + `Put` | 正常 C# 构造 + 存入 DB |
| **WiseCopy** | 对象间字段拷贝 | 正常 C# 赋值 | 无需反射 |
| **ConnectPointToPin** | Pin 连线绝对地址 | DB 中存引用 SID | 逻辑引用替代物理指针 |
| **Wire.Transmit** | Buffer.BlockCopy 搬运 | mdbx Get→修改→Put | 事务内原子搬运 |
| **Save** | BinaryWriter 串行写 | 无操作（mmap 自动） | 接近零耗时 |
| **Load** | BinaryReader 串行读 | env.Open（mmap 映射） | 接近零耗时 |
| **GCHandle hack** | 伪造 .NET 对象 | 彻底消除 | 正常对象，正常访问 |

---

## 三、其他候选方案对比

### 3.1 为什么不选 FastDB？

[FastDB](http://www.garret.ru/fastdb/FastDB.htm) 架构上与 RTD 最相似（mmap + 结构化对象 + 实时），且被 [IndigoSCADA](https://github.com/jonathanxavier/IndigoSCADA) 验证过。但：

| 因素 | FastDB | libmdbx |
|------|--------|---------|
| C# 绑定 | **无**，需自建 C++/CLI | **有** mdbx.NET NuGet |
| 活跃维护 | 2020 年后不活跃 | 2025 年仍在活跃开发 |
| 许可证 | 自定义许可 | Apache 2.0 |
| 生产验证 | IndigoSCADA（小众） | 以太坊半数客户端（大规模） |
| 崩溃恢复 | 有 | 更强（Copy-on-Write） |

### 3.2 为什么不选 ObjectBox？

[ObjectBox](https://objectbox.io/) 是最接近"对象数据库"的现代方案，支持 FlatBuffers 结构化对象 + mmap 零拷贝。但：

- **没有 C# / .NET 绑定**（官方 issue #5 从 2019 年至今未解决）
- 需要完全自建 P/Invoke 封装层

### 3.3 为什么不选 FlatSharp 单独使用？

[FlatSharp](https://github.com/jamescourtney/FlatSharp) 提供了极好的结构化访问，但：

- 不是数据库，没有持久化、事务、崩溃恢复
- 偏向只读场景，高频写入不是强项
- 可以作为序列化层配合 libmdbx 使用（替代 MemoryPack）

### 3.4 完整对比矩阵

| 方案 | C# 绑定 | 结构化数据 | 高频写入 | Save/Load | 崩溃恢复 | 活跃度 | 生产验证 |
|------|---------|-----------|---------|-----------|---------|--------|---------|
| **libmdbx + MemoryPack** | ★★★★★ | ★★★★ | ★★★★★ | ★★★★★ | ★★★★★ | ★★★★★ | ★★★★★ |
| FastDB + C++/CLI | ★★ | ★★★★★ | ★★★★ | ★★★★★ | ★★★★ | ★★ | ★★★ |
| ObjectBox + P/Invoke | ★ | ★★★★★ | ★★★★★ | ★★★★★ | ★★★★ | ★★★★★ | ★★★★ |
| FlatSharp 单独 | ★★★★★ | ★★★★★ | ★★★ | ★★ | ★ | ★★★★★ | ★★★★★ |
| MemoryMappedFile 自建 | ★★★★★ | ★★★ | ★★★★★ | ★★★★★ | ★★★ | — | — |
| 当前 RTD（已修复） | ★★★★★ | ★★★ | ★★★★★ | ★★★★ | ★ | — | ★★★ |

---

## 四、迁移路径

### Phase 1：引入 libmdbx 作为持久层（低风险）

```
保持现有 RTD 运行逻辑不变
在 Save/Load 环节用 libmdbx 替代 BinaryReader/Writer
  → Save: 遍历 PointManage → 序列化每个对象 → mdbx.Put
  → Load: mdbx.Get → 反序列化 → 写入 byte[]
收益：崩溃恢复、数据安全、Save/Load 性能提升
```

### Phase 2：数据点迁移到 libmdbx（中等风险）

```
LA/LD 等值类型数据点从 byte[] 迁移到 libmdbx
  → Key = SID (int)
  → Value = 原始字节（blittable struct，零序列化开销）
连线值搬运改为：mdbx.Get(srcSid) → mdbx.Put(dstSid)
收益：消除 MemoryManage 的手工内存页管理
```

### Phase 3：Function Block 迁移到 libmdbx（较高风险）

```
Function Block 从 byte[] + Marshal 迁移到 libmdbx + MemoryPack
  → Key = SID (int)
  → Value = MemoryPack 序列化的字节
  → 字段访问 = 反序列化为 C# 对象 → 正常属性访问
彻底消除：TypeManageItem 索引器、WiseNew、GCHandle hack
收益：类型安全、无 Marshal、正常 C# 对象
```

### Phase 4：架构优化（长期）

```
多进程架构：HMI 进程通过 libmdbx 共享内存直接读取实时数据
热备冗余：主/备 DCS 通过 libmdbx 的 mmap 文件同步
性能监控：利用 mdbx 的统计 API 监控数据库性能
```

---

## 五、25 万 FC 规模适配性分析

### 5.1 规模参数

| 参数 | 值 |
|------|-----|
| FC 数量 | ~250,000 |
| 单 FC 平均大小 | ~1 KB（含 Pin 字段、字符串等） |
| 总数据量 | ~250 MB |
| 运行周期 | 200-500 ms |
| 每周期操作 | 读取所有 FC → 执行 Run() → Wire 值搬运 → 写回变更字段 |

### 5.2 纯 libmdbx + MemoryPack 方案的瓶颈

如果将 libmdbx 用作**每周期的热路径数据存取引擎**，每次字段访问都经过 B+Tree 查找 + 序列化/反序列化：

| 操作 | 单次耗时 | 25 万次总耗时 | 是否可接受 |
|------|---------|-------------|-----------|
| mdbx.Get（B+Tree 查找） | ~0.5 μs | ~125 ms | 勉强 |
| MemoryPack.Deserialize（中等对象） | ~0.5-2 μs | ~125-500 ms | **瓶颈** |
| MemoryPack.Serialize（写回） | ~0.5-2 μs | ~125-500 ms | **瓶颈** |
| mdbx.Put（事务写入） | ~2 μs | ~500 ms | **超标** |
| **单周期总计** | | **~875-1625 ms** | **不可接受** |

对比当前 byte[] 直接内存访问：

| 操作 | 单次耗时 | 25 万次总耗时 |
|------|---------|-------------|
| Marshal.ReadInt32（直接偏移） | ~5-10 ns | ~1.25-2.5 ms |
| Buffer.BlockCopy（值搬运） | ~10-50 ns | ~2.5-12.5 ms |
| **单周期总计** | | **~5-15 ms** |

**差距约 100 倍。** 核心原因：每次访问都做序列化/反序列化，25 万次累积后开销巨大。

### 5.3 结论

**纯 libmdbx + MemoryPack 方案不适合作为 25 万 FC 的热路径（per-cycle）数据存取引擎。**

但 libmdbx 仍然非常适合作为 **持久化层**（Save/Load + 崩溃恢复 + 多进程共享）。

### 5.4 修正方案：热冷分层架构

```
┌─────────────────────────────────────────────────────┐
│ 热路径（每周期执行，μs 级）                          │
│ → MemoryMappedFile + Unsafe/Span<T> 直接内存访问    │
│ → 零序列化、零 B+Tree 查找、零 GC                   │
├─────────────────────────────────────────────────────┤
│ 冷路径（Save/Load/崩溃恢复，秒级）                   │
│ → libmdbx ACID 事务持久化                           │
│ → 定期快照同步 + 崩溃自动恢复                        │
└─────────────────────────────────────────────────────┘
```

#### 热路径：MemoryMappedFile + Unsafe

```csharp
// 25万 FC，每个平均 1KB → 总计 ~250MB
var mmf = MemoryMappedFile.CreateFromFile("rtd.dat",
    FileMode.OpenOrCreate, "RTD", 512L * 1024 * 1024);

// 获取原始指针 — 一次获取，全周期复用
byte* basePtr;
accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref basePtr);

// 读取 FC 的某个 int 字段 — 与当前 Marshal.ReadInt32 等效
// ~5ns/次，25万次 ≈ 1.25ms
int value = Unsafe.ReadUnaligned<int>(ref basePtr[fcOffset + fieldOffset]);

// 值搬运 — 与当前 Buffer.BlockCopy 等效
// ~10ns/次
Unsafe.CopyBlock(ref basePtr[dstOffset], ref basePtr[srcOffset], length);

// Save = 什么都不用做（mmap 自动持久化到 rtd.dat）
// Load = 重新 CreateFromFile（操作系统自动映射）
```

**性能与当前 byte[] 方案相当，但额外获得：**
- mmap 自动持久化（Save 接近零耗时）
- 跨进程共享（HMI 进程可直接读取实时数据）
- 无 GC 压力（操作系统管理内存，不参与垃圾回收）
- 无需 GCHandle.Alloc/Pin（mmap 天然固定在物理内存）

#### 冷路径：libmdbx 做崩溃恢复和备份

```csharp
// 定期（如每 5-10 秒）将 mmap 快照同步到 libmdbx
// 用于崩溃恢复和历史版本管理
void PeriodicBackup()
{
    using var tran = mdbxEnv.BeginTransaction();
    var db = tran.OpenDatabase("snapshot");
    // 250MB 整块写入 ≈ 100-200ms
    db.Put(snapshotKey, mmfBytes);
    tran.Commit();  // ACID 原子提交，崩溃安全
}

// 崩溃恢复：从 libmdbx 最后一个完整快照恢复 mmap
void RecoverFromCrash()
{
    using var tran = mdbxEnv.BeginTransaction(TransactionOption.ReadOnly);
    var db = tran.OpenDatabase("snapshot");
    byte[] snapshot = db.Get<int, byte[]>(snapshotKey);
    // 写回 mmap
    Marshal.Copy(snapshot, 0, mmfBasePtr, snapshot.Length);
}
```

### 5.5 各规模性能估算

| FC 数量 | 数据量 | 热路径单周期 | Save（mmap） | Load（mmap） | 崩溃恢复（libmdbx） |
|--------|--------|-------------|-------------|-------------|-------------------|
| 1 万 | ~10 MB | ~0.5 ms | ~0 ms | ~5 ms | ~20 ms |
| 5 万 | ~50 MB | ~2.5 ms | ~0 ms | ~10 ms | ~60 ms |
| **25 万** | **~250 MB** | **~12 ms** | **~0 ms** | **~50 ms** | **~250 ms** |
| 50 万 | ~500 MB | ~25 ms | ~0 ms | ~100 ms | ~500 ms |

**25 万 FC 规模下：**
- 热路径单周期 ~12ms，远小于 200ms 周期 → **完全满足**
- Save 接近零耗时 → **比当前 BinaryWriter 方案提升数量级**
- Load ~50ms → **满足几百毫秒级要求**
- 崩溃恢复 ~250ms → **满足高可用要求**

### 5.6 完整分层架构图

```
┌──────────────────────────────────────────────────────────┐
│                   C# 业务层                               │
│          Function / IO / Command / Wire                  │
├──────────────────────────────────────────────────────────┤
│   热路径：MemoryMappedFile + Unsafe/Span<T>              │
│   ┌────────────────────────────────────────────────────┐ │
│   │  AcquirePointer → basePtr（一次获取，全周期复用）   │ │
│   │                                                    │ │
│   │  字段读写: Unsafe.ReadUnaligned<T>     ~5 ns/次    │ │
│   │  值搬运:   Unsafe.CopyBlock            ~10 ns/次   │ │
│   │  对象构造: ReadObjectFromAddress        (已有实现)  │ │
│   │                                                    │ │
│   │  25万 FC 单周期总计:                   ~12 ms      │ │
│   └────────────────────────────────────────────────────┘ │
│                   ↕ mmap 自动持久化到磁盘文件             │
│   冷路径：libmdbx（ACID 事务引擎）                       │
│   ┌────────────────────────────────────────────────────┐ │
│   │  定期快照同步 mmap → libmdbx           每 5-10 秒  │ │
│   │  崩溃恢复: libmdbx → 重建 mmap         ~250 ms    │ │
│   │  多进程: HMI 直接 mmap 共享读取                    │ │
│   │  备份: libmdbx 事务级快照              可随时回滚  │ │
│   └────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────┘
```

### 5.7 与当前系统的迁移兼容性

| 当前组件 | 迁移方式 | 改动量 |
|---------|---------|--------|
| **MemoryManage**（byte[] 页面） | 替换为 MemoryMappedFile | MemoryManage 内部重构 |
| **TypeManageItem 索引器** | basePtr 替代 pinned byte[] 指针 | 仅修改地址来源 |
| **ReadObjectFromAddress** | 保持不变（地址仍然是绝对地址） | 无改动 |
| **WriteObjectToAddress** | 保持不变 | 无改动 |
| **WiseNew** | 保持不变（写入 mmap 而非 byte[]） | 无改动 |
| **Buffer.BlockCopy** | 改为 Unsafe.CopyBlock | 一行替换 |
| **BinaryWriter Save** | 删除（mmap 自动持久化） | 简化代码 |
| **BinaryReader Load** | 删除（mmap 自动映射） | 简化代码 |

**关键点：由于 mmap 的地址空间与 pinned byte[] 等效（都是固定的绝对地址），已有的 `ReadObjectFromAddress` / `WriteObjectToAddress` / `WiseNew` 等核心逻辑完全不需要修改，只需把底层存储从 byte[] 换成 mmap 即可。**

---

## 六、快速验证方案（POC）

### POC 1：MemoryMappedFile 热路径验证

```csharp
// 验证 25 万次结构体读写的周期耗时
var mmf = MemoryMappedFile.CreateFromFile("poc_rtd.dat",
    FileMode.OpenOrCreate, "POC_RTD", 512L * 1024 * 1024);
var accessor = mmf.CreateViewAccessor();

byte* basePtr = null;
accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref basePtr);

// 模拟 25 万 FC 的单周期读写
var sw = Stopwatch.StartNew();
for (int i = 0; i < 250_000; i++)
{
    int offset = i * 1024; // 每个 FC 占 1KB
    // 读取 runable (bool)
    bool runable = *(basePtr + offset + 8) != 0;
    // 读取 counter (int)
    int counter = Unsafe.ReadUnaligned<int>(ref basePtr[offset + 12]);
    // 写回 counter + 1
    Unsafe.WriteUnaligned(ref basePtr[offset + 12], counter + 1);
}
sw.Stop();
Console.WriteLine($"25万 FC 单周期读写: {sw.ElapsedMilliseconds}ms");
// 预期: < 15ms

accessor.SafeMemoryMappedViewHandle.ReleasePointer();
```

### POC 2：libmdbx 冷路径验证

```csharp
// 验证 250MB 快照的写入和恢复速度
var env = new MdbxEnvironment();
env.SetMaxDatabases(4).SetMapSize(1024L * 1024 * 1024)
   .Open("poc_mdbx", EnvFlags.WriteMap | EnvFlags.MapAsync, 0664);

byte[] snapshot = new byte[250 * 1024 * 1024]; // 模拟 250MB 快照
new Random().NextBytes(snapshot);

// 快照写入
var sw = Stopwatch.StartNew();
using (var tran = env.BeginTransaction())
{
    var db = tran.OpenDatabase("snapshot", DatabaseOption.Create);
    db.Put(1, snapshot);
    tran.Commit();
}
Console.WriteLine($"250MB 快照写入: {sw.ElapsedMilliseconds}ms");
// 预期: 100-300ms

// 快照读取（崩溃恢复）
sw.Restart();
using (var tran = env.BeginTransaction(TransactionOption.ReadOnly))
{
    var db = tran.OpenDatabase("snapshot");
    byte[] recovered = db.Get<int, byte[]>(1);
}
Console.WriteLine($"250MB 快照恢复: {sw.ElapsedMilliseconds}ms");
// 预期: 50-200ms

env.Close();
```

---

## 七、参考链接

### 核心推荐
- [libmdbx — 超越 LMDB 的嵌入式事务数据库](https://github.com/erthink/libmdbx)
- [mdbx.NET — libmdbx 的 .NET 封装（NuGet）](https://github.com/wangjia184/mdbx.NET)
- [mdbx.NET NuGet 包](https://www.nuget.org/packages/mdbx.NET)
- [MemoryPack — 零编码极速序列化](https://github.com/Cysharp/MemoryPack)

### 对比参考
- [FastDB 官方主页](http://www.garret.ru/fastdb/FastDB.htm)
- [FastDB GitHub 镜像](https://github.com/gavioto/fastdb)
- [IndigoSCADA — 使用 FastDB 的开源 DCS](https://github.com/jonathanxavier/IndigoSCADA)
- [ObjectBox C/C++ 数据库](https://github.com/objectbox/objectbox-c)
- [FlatSharp — FlatBuffers for C#](https://github.com/jamescourtney/FlatSharp)
- [NativeMemoryArray — 原生内存数组](https://github.com/Cysharp/NativeMemoryArray)
- [LMDB vs FastDB 讨论](https://lists.openldap.org/hyperkitty/list/openldap-technical@openldap.org/thread/TXNZKPPELL5ZAVZT3YBEPQ6FRNHLM76N/)
- [LMDB 微基准测试](http://www.lmdb.tech/bench/microbench/)
- [Memory-Mapped Files Overlaid Structs 模式](https://blog.stephencleary.com/2023/09/memory-mapped-files-overlaid-structs.html)
