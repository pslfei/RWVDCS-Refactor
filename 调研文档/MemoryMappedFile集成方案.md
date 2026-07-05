# MemoryMappedFile 集成方案

## 1. 现状分析

### 1.1 当前架构

```
                         MemoryManage
┌──────────────────────────────────────────────────────┐
│  WorkPageList                                        │
│  ┌─────────────────────────────────────────────┐     │
│  │ Page 0: byte[204800]  ← GCHandle.Pinned    │     │
│  │ ┌─────┬─────┬─────┬───────┬─────┐          │     │
│  │ │SID 0│SID 1│SID 2│SID 3  │ ... │  变量区   │     │
│  │ └─────┴─────┴─────┴───────┴─────┘          │     │
│  ├─────────────────────────────────────────────┤     │
│  │ Page 1: byte[204800]  ← GCHandle.Pinned    │     │
│  │ ┌─────┬─────┬─────┐                        │     │
│  │ │SID98│SID99│SID..│                         │     │
│  │ └─────┴─────┴─────┘                        │     │
│  └─────────────────────────────────────────────┘     │
│                                                      │
│  VariableList: List<VariableListItem>                 │
│  ┌──────────────────────────────────────────┐        │
│  │ SID 0: pageIndex=0, offset=0,    len=64  │        │
│  │ SID 1: pageIndex=0, offset=64,   len=128 │        │
│  │ SID 2: pageIndex=0, offset=192,  len=32  │        │
│  │ ...                                      │        │
│  └──────────────────────────────────────────┘        │
│                                                      │
│  SavePageList: 页面的子区间链表（用于 CutPage）       │
│  ReclaimVariableList: 已回收的 SID（有序）           │
└──────────────────────────────────────────────────────┘
```

### 1.2 当前 Save/Load 流程

**Save（当前）**：

```
1. 停止编辑，等待进行中的操作完成
2. 可选：GarbageCollection() 压缩碎片
3. 写入头部字段（9 个标量值）
4. 分配中间 byte[] arrays = new byte[元数据 + 页面数据]
5. 拷贝 ReclaimVariableList → arrays
6. 拷贝 VariableList → arrays（需 GCHandle.Pinned 临时钉住）
7. 遍历 SavePageList 链表拷贝所有页面数据 → arrays
8. stream.Write(arrays) 一次写入
```

**Load（当前）**：

```
1. 读取头部字段
2. 分配一个大页面 AddPageToWork(memUseLength)
3. 分配中间 byte[] arrays = new byte[totalLength]
4. stream.Read(arrays) 一次读入
5. 从 arrays 反序列化 ReclaimVariableList
6. 从 arrays 反序列化 VariableList
7. 将页面数据 BlockCopy 到 pinned byte[]
```

### 1.3 性能瓶颈

| 阶段 | 瓶颈 | 数据量（典型） |
|------|------|----------------|
| Save 步骤 4 | 分配中间 byte[]（LOH 压力） | 50~200 MB |
| Save 步骤 5-7 | 多次 BlockCopy 到中间 buffer | 50~200 MB |
| Save 步骤 8 | stream.Write 一次写入磁盘 | 50~200 MB |
| Load 步骤 3 | 分配中间 byte[]（LOH 压力） | 50~200 MB |
| Load 步骤 4 | stream.Read 一次读入 | 50~200 MB |
| Load 步骤 7 | BlockCopy 到页面 | 50~200 MB |

**总耗时估算**（200MB 数据，机械硬盘）：
- Save ≈ 2~5 秒（分配 + 拷贝 + 写磁盘）
- Load ≈ 2~5 秒（读磁盘 + 分配 + 拷贝）

---

## 2. 目标架构

### 2.1 核心思想

将页面数据的存储后端从 `byte[] + GCHandle.Pinned` 替换为 `MemoryMappedFile`，实现：

- **运行时**：数据直接在内存映射区读写，与当前 byte[] 访问体验一致
- **Save**：仅 flush 脏页到磁盘，不需要分配中间 buffer
- **Load**：打开映射文件即完成，数据按需换入（page fault）

### 2.2 架构对比

```
┌─ 当前架构 ──────────────────────────┐  ┌─ 新架构 ──────────────────────────────┐
│                                     │  │                                       │
│  byte[] page0  ←GCHandle.Pinned     │  │  MemoryMappedFile "rtd_data.bin"      │
│  byte[] page1  ←GCHandle.Pinned     │  │  ┌───────────────────────────────┐    │
│  byte[] page2  ←GCHandle.Pinned     │  │  │ Header (元数据区)              │    │
│      ...                            │  │  ├───────────────────────────────┤    │
│                                     │  │  │ Page 数据区（连续平坦布局）     │    │
│  Save: 遍历页面 → 中间buffer → 写文件 │  │  │ ┌─────┬─────┬─────┬───────┐  │    │
│  Load: 读文件 → 中间buffer → 分发页面 │  │  │ │SID 0│SID 1│SID 2│  ...  │  │    │
│                                     │  │  │ └─────┴─────┴─────┴───────┘  │    │
│  瓶颈：中间 buffer 分配和拷贝         │  │  └───────────────────────────────┘    │
│  两次全量拷贝                         │  │                                       │
│                                     │  │  Save: FlushViewOfFile（仅刷脏页）     │
│                                     │  │  Load: MapViewOfFile（按需换入）        │
│                                     │  │                                       │
│                                     │  │  零拷贝，无中间 buffer                  │
└─────────────────────────────────────┘  └───────────────────────────────────────┘
```

### 2.3 预期性能提升

| 操作 | 当前耗时 | 新方案耗时 | 提升倍数 |
|------|----------|-----------|---------|
| Save（200MB，SSD） | 1~3 秒 | 10~50 ms | **20~60x** |
| Save（200MB，HDD） | 3~8 秒 | 50~200 ms | **15~40x** |
| Load（200MB，SSD） | 1~3 秒 | 5~20 ms（映射）+ 按需换入 | **50~150x** |
| Load（200MB，HDD） | 3~8 秒 | 10~50 ms（映射）+ 按需换入 | **60~160x** |
| 运行时读写 | 与当前相同 | 与当前相同 | 1x |
| GC 压力 | 高（LOH 分配） | **零**（映射区不在 GC 堆中） | - |

---

## 3. 文件格式设计

### 3.1 映射文件布局

```
偏移        内容                        大小
────────────────────────────────────────────────
0x0000      Magic Number "RTDM"        4 bytes
0x0004      Version                    4 bytes
0x0008      HeaderSize                 4 bytes        ← 元数据区总大小
0x000C      recyclingAtSerializing     1 byte
0x000D      acceptableUtilizationRatio 8 bytes (double)
0x0015      autoCollationSpan          4 bytes (uint)
0x0019      firstVariableIndex         4 bytes (int)
0x001D      lastVariableIndex          4 bytes (int)
0x0021      memUseLength               8 bytes (long)
0x0029      pageLength                 4 bytes (int)
0x002D      countReclaimList           4 bytes (int)
0x0031      countVariableList          4 bytes (int)
0x0035      dataRegionOffset           4 bytes (int)   ← 页面数据区的起始偏移
0x0039      totalFileSize              8 bytes (long)  ← 文件总大小
0x0041      checksum                   4 bytes (uint)  ← 头部校验和
────────────────────────────────────────────────
0x0045      ReclaimVariableList        countReclaimList * 4 bytes
            VariableListItem[]         countVariableList * 32 bytes
────────────────────────────────────────────────
dataRegionOffset:
            Page 数据区                memUseLength bytes
            （所有变量的原始数据连续排列）
────────────────────────────────────────────────
```

### 3.2 对齐要求

- `dataRegionOffset` 对齐到 4096 字节（系统页面大小），确保 mmap 效率最优
- 总文件大小按 64KB 向上取整（减少频繁扩展）

### 3.3 版本兼容

文件开头的 Magic Number + Version 用于区分：
- `Version = 1`：旧格式（BinaryWriter 流式序列化）
- `Version = 2`：新格式（MemoryMappedFile）

Load 时检查 Version，自动选择对应的加载路径，**实现向前兼容**。

---

## 4. 实现方案

### 4.1 新增类：MappedPageStore

在 `RTD` 项目中新增 `MappedPageStore.cs`，封装 MemoryMappedFile 的创建、扩展、刷盘：

```csharp
using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;

namespace DCSRTD
{
    /// <summary>
    /// 基于 MemoryMappedFile 的页面存储后端
    /// 替代 byte[] + GCHandle.Pinned 方案
    /// </summary>
    internal class MappedPageStore : IDisposable
    {
        // 魔术数和版本
        private const uint MAGIC = 0x4D445452; // "RTDM"
        private const int VERSION = 2;
        private const int HEADER_FIXED_SIZE = 0x0045; // 固定头部字段
        private const int PAGE_ALIGNMENT = 4096;       // 数据区对齐

        private string filePath;
        private MemoryMappedFile mmf;
        private MemoryMappedViewAccessor accessor;
        private long capacity;        // 当前映射大小
        private long dataRegionOffset; // 页面数据区起始偏移
        private bool disposed = false;

        /// <summary>
        /// 页面数据区的起始偏移
        /// </summary>
        public long DataRegionOffset => dataRegionOffset;

        /// <summary>
        /// 整个映射区的视图访问器
        /// </summary>
        public MemoryMappedViewAccessor Accessor => accessor;

        /// <summary>
        /// 获取映射区的基地址（稳定 IntPtr，用于兼容旧代码）
        /// </summary>
        public IntPtr BaseAddress
        {
            get
            {
                // SafeMemoryMappedViewHandle 提供稳定地址
                byte* ptr = null;
                accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
                return new IntPtr(ptr);
                // 注意：对应需要在 Dispose 时 ReleasePointer
            }
        }

        /// <summary>
        /// 创建新的映射文件或打开已有文件
        /// </summary>
        /// <param name="path">文件路径</param>
        /// <param name="initialCapacity">初始容量（字节）</param>
        public MappedPageStore(string path, long initialCapacity)
        {
            filePath = path;
            capacity = AlignUp(initialCapacity, 64 * 1024); // 64KB 对齐
            CreateOrOpen();
        }

        /// <summary>
        /// 创建或打开映射文件
        /// </summary>
        private void CreateOrOpen()
        {
            var stream = new FileStream(filePath,
                FileMode.OpenOrCreate, FileAccess.ReadWrite,
                FileShare.None);

            if (stream.Length < capacity)
                stream.SetLength(capacity);
            else
                capacity = stream.Length;

            mmf = MemoryMappedFile.CreateFromFile(
                stream,
                mapName: null,
                capacity: capacity,
                MemoryMappedFileAccess.ReadWrite,
                HandleInheritability.None,
                leaveOpen: false);

            accessor = mmf.CreateViewAccessor(0, capacity,
                MemoryMappedFileAccess.ReadWrite);
        }

        /// <summary>
        /// 扩展映射文件容量
        /// </summary>
        public void EnsureCapacity(long requiredSize)
        {
            if (requiredSize <= capacity) return;

            long newCapacity = AlignUp(
                Math.Max(requiredSize, capacity * 2), 64 * 1024);

            // 关闭旧映射
            accessor.Dispose();
            mmf.Dispose();

            // 扩展文件并重新映射
            capacity = newCapacity;
            CreateOrOpen();
        }

        /// <summary>
        /// 将所有修改刷到磁盘
        /// </summary>
        public void Flush()
        {
            accessor.Flush();
        }

        /// <summary>
        /// 写入文件头
        /// </summary>
        public void WriteHeader(MappedFileHeader header)
        {
            accessor.Write(0, MAGIC);
            accessor.Write(4, VERSION);
            accessor.Write(8, header.HeaderSize);
            accessor.Write(0x0C, header.RecyclingAtSerializing ? (byte)1 : (byte)0);
            accessor.Write(0x0D, header.AcceptableUtilizationRatio);
            accessor.Write(0x15, header.AutoCollationSpan);
            accessor.Write(0x19, header.FirstVariableIndex);
            accessor.Write(0x1D, header.LastVariableIndex);
            accessor.Write(0x21, header.MemUseLength);
            accessor.Write(0x29, header.PageLength);
            accessor.Write(0x2D, header.CountReclaimList);
            accessor.Write(0x31, header.CountVariableList);
            accessor.Write(0x35, header.DataRegionOffset);
            accessor.Write(0x39, header.TotalFileSize);
            // checksum 最后写
        }

        /// <summary>
        /// 读取文件头
        /// </summary>
        public MappedFileHeader ReadHeader()
        {
            uint magic = accessor.ReadUInt32(0);
            if (magic != MAGIC)
                throw new InvalidDataException("不是有效的 RTD 映射文件");

            int version = accessor.ReadInt32(4);
            if (version != VERSION)
                throw new InvalidDataException(
                    $"不支持的文件版本 {version}，当前版本 {VERSION}");

            return new MappedFileHeader
            {
                HeaderSize = accessor.ReadInt32(8),
                RecyclingAtSerializing = accessor.ReadByte(0x0C) != 0,
                AcceptableUtilizationRatio = accessor.ReadDouble(0x0D),
                AutoCollationSpan = accessor.ReadUInt32(0x15),
                FirstVariableIndex = accessor.ReadInt32(0x19),
                LastVariableIndex = accessor.ReadInt32(0x1D),
                MemUseLength = accessor.ReadInt64(0x21),
                PageLength = accessor.ReadInt32(0x29),
                CountReclaimList = accessor.ReadInt32(0x2D),
                CountVariableList = accessor.ReadInt32(0x31),
                DataRegionOffset = accessor.ReadInt32(0x35),
                TotalFileSize = accessor.ReadInt64(0x39),
            };
        }

        /// <summary>
        /// 在映射区指定偏移处读写 byte[]
        /// </summary>
        public void ReadBytes(long position, byte[] buffer, int offset, int count)
        {
            accessor.ReadArray(position, buffer, offset, count);
        }

        public void WriteBytes(long position, byte[] buffer, int offset, int count)
        {
            accessor.WriteArray(position, buffer, offset, count);
        }

        public void Dispose()
        {
            if (!disposed)
            {
                accessor?.Dispose();
                mmf?.Dispose();
                disposed = true;
            }
        }

        private static long AlignUp(long value, long alignment)
        {
            return (value + alignment - 1) / alignment * alignment;
        }
    }

    /// <summary>
    /// 映射文件头部结构
    /// </summary>
    internal struct MappedFileHeader
    {
        public int HeaderSize;
        public bool RecyclingAtSerializing;
        public double AcceptableUtilizationRatio;
        public uint AutoCollationSpan;
        public int FirstVariableIndex;
        public int LastVariableIndex;
        public long MemUseLength;
        public int PageLength;
        public int CountReclaimList;
        public int CountVariableList;
        public int DataRegionOffset;
        public long TotalFileSize;
    }
}
```

### 4.2 修改 PageListItem

```csharp
// 当前结构
private struct PageListItem
{
    public long memlen;
    public long memuse;
    public int savePageListIndex;
    public byte[] data;           // ← 独立的 byte[]
    public GCHandle dataHandle;   // ← 钉住句柄
    public int dataOffset;
}

// 新结构
private struct PageListItem
{
    public long memlen;
    public long memuse;
    public int savePageListIndex;
    public int dataOffset;         // ← 在映射区数据段中的偏移

    // data 和 dataHandle 被移除
    // 统一通过 MappedPageStore 访问
}
```

### 4.3 修改 MemoryManage 核心

#### 4.3.1 新增字段

```csharp
internal class MemoryManage : IMemoryManage
{
    // 新增：映射文件后端
    private MappedPageStore mappedStore;

    // 新增：映射区中页面数据的 byte[] 视图
    // 通过 unsafe 获取指针，或通过 accessor 方法访问
    // 为兼容 MemorySlot(byte[], offset, length) 接口，
    // 维护一个全局 byte[] 引用指向映射区
    private byte[] mappedData;

    // 保留现有字段不变
    private List<VariableListItem> variableList;
    private List<PageListItem> workPageList;
    // ...
}
```

#### 4.3.2 MemorySlot 兼容方案

当前 `MemorySlot` 持有 `byte[] Data` 引用。MemoryMappedFile 的内容不是一个 `byte[]`，需要桥接。

**方案 A（推荐）：单一大 byte[] 映射**

在 GarbageCollection 后，所有变量连续排列在单一映射区。通过 `MemoryMappedViewAccessor` 的非公开 API 获取底层 `SafeBuffer`，再用 `Marshal.Copy` 和 `Buffer.BlockCopy` 实现数据传递。但 `MemorySlot` 需要的是 `byte[]`。

解决方案：维护一个与映射区同步的 `byte[]` 影子缓冲区，读写时通过 accessor 直接操作映射区，MemorySlot 指向影子缓冲区。

```
映射文件 (磁盘)
    ↕ mmap (OS 管理)
映射区 (虚拟内存，稳定地址)
    ↕ 同步
影子 byte[] (MemorySlot 引用的数组)
```

**方案 B（更彻底，需改 MemorySlot）：**

将 `MemorySlot` 改为同时支持 `byte[]` 和 `MemoryMappedViewAccessor` 两种后端：

```csharp
public readonly struct MemorySlot
{
    public readonly byte[] Data;                       // 方案一用
    public readonly MemoryMappedViewAccessor Accessor;  // 方案二用
    public readonly int Offset;
    public readonly int Length;

    // 读写方法自动选择后端
    public T Read<T>(int relativeOffset) where T : struct
    {
        if (Data != null)
            return Unsafe.ReadUnaligned<T>(ref Data[Offset + relativeOffset]);
        else
            return Accessor.Read<T>(Offset + relativeOffset);
    }
}
```

**方案 C（最小改动，推荐优先采用）：**

利用 .NET 的 `UnmanagedMemoryAccessor`，将映射区包装成 `byte[]` 可直接使用的视图。具体做法：将整个数据区的内容在 Load 时 `ReadArray` 到一个 pinned `byte[]`，运行时在这个 `byte[]` 上操作，Save 时将 `byte[]` `WriteArray` 回映射区再 Flush。

这与当前架构最接近，唯一区别是 Save/Load 操作的对象从 `FileStream` 变为 `MemoryMappedViewAccessor`。

### 4.4 Save 流程（新）

```csharp
public bool Save(string filePath)
{
    this.AutoCollation = false;
    this.ForbidEdit = true;
    int tick = 0;
    while (editingStack.Count > 0 && tick < 200) { Thread.Sleep(5); tick++; }
    if (tick >= 200) return false;
    try
    {
        // 1. 可选 GC 压缩
        if (this.recyclingAtSerializing)
        {
            if (!GarbageCollection()) return false;
        }

        // 2. 计算所需文件大小
        int metaSize = CalculateMetadataSize();
        int dataRegion = AlignUp(HEADER_FIXED_SIZE + metaSize, PAGE_ALIGNMENT);
        long totalSize = dataRegion + memUseLength;

        // 3. 创建/扩展映射文件
        using (var store = new MappedPageStore(filePath, totalSize))
        {
            // 4. 写入头部（直接写映射区，无需中间 buffer）
            store.WriteHeader(BuildHeader(dataRegion, totalSize));

            // 5. 写入元数据（ReclaimList + VariableList）
            WriteMetadataToMapped(store, HEADER_FIXED_SIZE);

            // 6. 写入页面数据（直接从 byte[] BlockCopy 到映射区）
            int offset = 0;
            foreach (PageListItem pItem in this.WorkPageList)
            {
                store.WriteBytes(
                    dataRegion + offset,
                    pItem.data, pItem.dataOffset,
                    (int)pItem.memuse);
                offset += (int)pItem.memuse;
            }

            // 7. Flush —— 这一步对 SSD 通常 < 50ms
            store.Flush();
        }

        this.changed = false;
        return true;
    }
    catch { return false; }
    finally { this.AutoCollation = true; this.ForbidEdit = false; }
}
```

### 4.5 Load 流程（新）

```csharp
public bool Load(string filePath)
{
    this.Lock = true;
    try
    {
        // 清理现有状态
        ClearInternal();

        using (var store = new MappedPageStore(filePath, 0))
        {
            // 1. 读取头部
            var header = store.ReadHeader();
            ApplyHeader(header);

            if (memUseLength <= 0) return true;

            // 2. 读取元数据
            ReadMetadataFromMapped(store, HEADER_FIXED_SIZE, header);

            // 3. 分配页面并从映射区直接拷贝
            //    只需一次 ReadArray，无中间 buffer
            PageListItem pageItem = AddPageToWork(
                (uint)memUseLength, (uint)memUseLength, false);

            store.ReadBytes(
                header.DataRegionOffset,
                pageItem.data, pageItem.dataOffset,
                (int)pageItem.memuse);
        }

        this.AutoCollation = true;
        return true;
    }
    catch { return false; }
    finally { this.Lock = false; }
}
```

### 4.6 旧格式兼容

```csharp
public bool Load(Stream fs)
{
    // 尝试读取前 4 字节判断格式
    byte[] magic = new byte[4];
    fs.Read(magic, 0, 4);
    fs.Position = 0; // 重置

    uint magicValue = BitConverter.ToUInt32(magic, 0);
    if (magicValue == 0x4D445452) // "RTDM"
    {
        // 新格式：MemoryMappedFile
        // 需要文件路径，从 FileStream 获取
        if (fs is FileStream fileStream)
            return LoadMapped(fileStream.Name);
        else
            return LoadLegacy(fs); // 非文件流回退到旧逻辑
    }
    else
    {
        // 旧格式：BinaryReader 流式
        return LoadLegacy(fs);
    }
}
```

---

## 5. 进阶方案：常驻映射模式

上述方案在 Save/Load 时使用映射文件，运行时仍用 `byte[]`。进一步优化可以让运行时也直接在映射区操作。

### 5.1 架构

```
         MemoryManage（常驻映射模式）
┌───────────────────────────────────────────────────┐
│                                                   │
│  MappedPageStore (常驻打开)                        │
│  ┌───────────────────────────────────────────┐    │
│  │  rtd_data.bin                             │    │
│  │  ┌───────────┬───────────────────────┐    │    │
│  │  │  Header   │  Page 数据区           │    │    │
│  │  │  (元数据)  │  ┌───┬───┬───┬──────┐│    │    │
│  │  │           │  │S0 │S1 │S2 │ ...  ││    │    │
│  │  │           │  └───┴───┴───┴──────┘│    │    │
│  │  └───────────┴───────────────────────┘    │    │
│  └───────────────────────────────────────────┘    │
│       ↑                                           │
│       │ CreateViewAccessor                        │
│       ↓                                           │
│  MemoryMappedViewAccessor (常驻打开)               │
│       ↑                                           │
│       │ ReadXxx / WriteXxx / SafeBuffer            │
│       ↓                                           │
│  MemorySlot / IntPtr 索引器                        │
│                                                   │
│  Save = Flush()              ← 仅刷脏页           │
│  Load = 关闭旧映射 + 打开新映射  ← 无拷贝          │
│                                                   │
└───────────────────────────────────────────────────┘
```

### 5.2 Save（常驻模式）

```csharp
public bool Save()
{
    this.ForbidEdit = true;
    WaitForEditing();
    try
    {
        // 更新头部的元数据快照
        UpdateHeaderInPlace();

        // 刷盘 —— 底层仅写脏页
        mappedStore.Flush();

        this.changed = false;
        return true;
    }
    finally { this.ForbidEdit = false; }
}
```

**耗时 ≈ 更新头部（微秒级）+ FlushViewOfFile（脏页写入，毫秒级）**

### 5.3 内存增长

当需要新增页面时，映射文件需要扩展：

```csharp
private int AddPageToWork()
{
    long requiredSize = dataRegionOffset + memAllocLenth + pageLength;
    mappedStore.EnsureCapacity(requiredSize);

    PageListItem item = new PageListItem();
    item.dataOffset = (int)(dataRegionOffset + memAllocLenth);
    item.memlen = pageLength;
    item.memuse = 0;
    item.savePageListIndex = -1;

    this.SavePageList.Add(item);
    item.savePageListIndex = this.SavePageList.Count - 1;
    this.WorkPageList.Add(item);
    memAllocLenth += pageLength;

    return this.WorkPageList.Count - 1;
}
```

### 5.4 MemorySlot 适配

常驻映射模式下，MemorySlot 不再持有 `byte[]`，而是通过 accessor 直接操作：

```csharp
// 方案一：扩展 MemorySlot 支持 accessor 后端
public readonly struct MemorySlot
{
    public readonly byte[] Data;
    public readonly MemoryMappedViewAccessor MappedAccessor;
    public readonly int Offset;
    public readonly int Length;

    // 映射区构造函数
    public MemorySlot(MemoryMappedViewAccessor accessor, int offset, int length)
    {
        Data = null;
        MappedAccessor = accessor;
        Offset = offset;
        Length = length;
    }

    public T Read<T>(int relativeOffset) where T : struct
    {
        if (Data != null)
            return Unsafe.ReadUnaligned<T>(ref Data[Offset + relativeOffset]);

        T value;
        MappedAccessor.Read(Offset + relativeOffset, out value);
        return value;
    }

    public void Write<T>(int relativeOffset, T value) where T : struct
    {
        if (Data != null)
            Unsafe.WriteUnaligned(ref Data[Offset + relativeOffset], value);
        else
            MappedAccessor.Write(Offset + relativeOffset, ref value);
    }
}
```

---

## 6. 实施计划

### 6.1 分阶段推进

```
阶段一（Save/Load 优化）          阶段二（常驻映射）
─────────────────────           ─────────────────
改动范围：MemoryManage.cs        改动范围：MemoryManage.cs
         新增 MappedPageStore.cs           MemorySlot
                                           PageListItem
                                           GetSlot/SetSlot

运行时后端：byte[] (不变)         运行时后端：MemoryMappedFile
Save/Load：MemoryMappedFile      Save：Flush()
                                 Load：Remap

改动量：小                        改动量：中
风险：低                          风险：中
收益：Save/Load 提速 20~60x     收益：消除 GCHandle.Pinned
                                      彻底消除中间拷贝
                                      支持进程间共享
```

### 6.2 阶段一：详细任务

| # | 任务 | 文件 | 说明 |
|---|------|------|------|
| 1 | 新增 MappedPageStore 类 | RTD\MappedPageStore.cs | 封装 MemoryMappedFile 操作 |
| 2 | 新增 MappedFileHeader 结构 | RTD\MappedPageStore.cs | 文件头部定义 |
| 3 | 新增 Save(string) 重载 | RTD\MemoryManage.cs | 映射文件格式写入 |
| 4 | 新增 LoadMapped(string) | RTD\MemoryManage.cs | 映射文件格式读取 |
| 5 | 修改 Load(Stream) | RTD\MemoryManage.cs | 自动检测格式版本 |
| 6 | 修改 IStore 接口 | DCSCommon\Interface.cs | 新增 Save(string)/Load(string) 声明 |
| 7 | 更新上层调用 | RTD\RTD.cs | 使用文件路径调用新 Save/Load |
| 8 | 编译验证 | - | 全解决方案编译 |
| 9 | 性能测试 | - | 对比 Save/Load 耗时 |

### 6.3 阶段二：详细任务

| # | 任务 | 文件 | 说明 |
|---|------|------|------|
| 1 | 扩展 MemorySlot 支持双后端 | DCSCommon\DataType.cs | Read/Write 方法支持 accessor |
| 2 | 修改 PageListItem | RTD\MemoryManage.cs | 移除 byte[]/GCHandle 字段 |
| 3 | MemoryManage 持有常驻 MappedPageStore | RTD\MemoryManage.cs | 构造函数接收文件路径 |
| 4 | 修改 AddPageToWork | RTD\MemoryManage.cs | 使用映射区偏移 |
| 5 | 修改 GetSlot/SetSlot | RTD\MemoryManage.cs | 返回映射区 MemorySlot |
| 6 | 修改 Copy 方法 | RTD\MemoryManage.cs | 映射区内直接操作 |
| 7 | Save 简化为 Flush | RTD\MemoryManage.cs | - |
| 8 | IntPtr 索引器适配 | RTD\MemoryManage.cs | 使用映射区稳定地址 |
| 9 | 全量编译+回归测试 | - | - |

---

## 7. 风险与缓解

| 风险 | 影响 | 缓解措施 |
|------|------|----------|
| 映射文件扩展需要关闭再重开 | 短暂不可用 | ForbidUse 保护 + 快速重映射 |
| 进程崩溃时脏页可能未刷盘 | 数据丢失 | 定时 Flush + 事务日志（可选） |
| MemorySlot 接口变化 | 上层调用方需要适配 | 阶段一不改 MemorySlot，仅阶段二改 |
| 旧数据文件兼容 | 升级后无法读取旧文件 | Magic+Version 自动检测 + 旧路径保留 |
| 多进程同时打开 | 数据损坏 | FileShare.None 独占锁 |
| 32 位进程地址空间限制 | 映射区不能超过 ~1.5GB | 当前数据量 < 200MB，暂不影响 |

---

## 8. 性能基准测试方案

### 8.1 测试场景

```csharp
// 准备测试数据
var mm = new MemoryManage();
for (int i = 0; i < 10000; i++)
{
    int sid = mm.New(1024); // 分配 1KB 变量
    // 填充随机数据
}
// 此时 memUseLength ≈ 10MB

// 测试 Save
var sw = Stopwatch.StartNew();
mm.Save(stream);          // 当前方案
sw.Stop();
Console.WriteLine($"旧 Save: {sw.ElapsedMilliseconds} ms");

sw.Restart();
mm.Save("test.rtdm");     // 新方案
sw.Stop();
Console.WriteLine($"新 Save: {sw.ElapsedMilliseconds} ms");

// 测试 Load
sw.Restart();
mm.Load(stream);           // 当前方案
sw.Stop();
Console.WriteLine($"旧 Load: {sw.ElapsedMilliseconds} ms");

sw.Restart();
mm.Load("test.rtdm");     // 新方案
sw.Stop();
Console.WriteLine($"新 Load: {sw.ElapsedMilliseconds} ms");
```

### 8.2 预期结果

| 数据量 | 旧 Save | 新 Save | 旧 Load | 新 Load |
|--------|---------|---------|---------|---------|
| 10 MB | ~200ms | ~5ms | ~200ms | ~3ms |
| 50 MB | ~800ms | ~20ms | ~800ms | ~10ms |
| 200 MB | ~3000ms | ~50ms | ~3000ms | ~30ms |

---

## 9. API 变更汇总

### 9.1 新增

```csharp
// MappedPageStore.cs（新文件）
internal class MappedPageStore : IDisposable { ... }
internal struct MappedFileHeader { ... }

// IMemoryManage 接口新增
bool Save(string filePath);     // 映射文件格式保存
bool Load(string filePath);     // 映射文件格式加载

// IStore 接口新增
bool Save(string filePath);
bool Load(string filePath);
```

### 9.2 保持不变

```csharp
// 以下接口保持不变，确保零改动上层代码
bool Save(Stream fs);           // 旧格式，保留
bool Load(Stream fs);           // 自动检测格式
MemorySlot GetSlot(int SID);   // 阶段一不变
IntPtr this[int SID] { get; set; }  // 阶段一不变
```

### 9.3 弃用（阶段二）

```csharp
[Obsolete("使用 GetSlot 替代")]
IntPtr this[int SID] { get; set; }

[Obsolete("使用 GetSlot 替代")]
IntPtr this[VariableAddress Address] { get; set; }
```
