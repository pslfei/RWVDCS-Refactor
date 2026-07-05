# RWVDCS RTD 实时数据库模块 — 技术解读报告

> 分析范围：`D:\项目\睿渥\RWVDCS\RTD\` 及 `DCSCommon`、`DCSType` 相关定义。只读分析，路径与行号均基于当前源码。

---

## 1. RTD 内存模型

### 1.1 总体架构

RTD 采用**三层寻址**：

| 层级 | 含义 | 数据结构 |
|------|------|----------|
| **memSID** | MemoryManage 中物理内存块 ID | `VariableListItem` |
| **SID** | PointManage 中逻辑点 ID | `PointManageItem` + `nameTable` |
| **FSID** | 全局变量域 ID | `(globalSID << 32) \| offset` |

### 1.2 页面（Page）结构

默认页大小 **200 KB**（`pageLength = 1024 * 200`）：

```111:105:D:\项目\睿渥\RWVDCS\RTD\MemoryManage.cs
        private int pageLength = 1024 * 200;
        // ...
        private struct PageListItem
        {
            public long memlen;
            public long memuse;
            public int savePageListIndex;
            public byte[] data;
            public GCHandle dataHandle;
            public int dataOffset;
```

- **WorkPageList**：工作页，变量顺序追加分配（`NewVariable` 在最后一页 `memuse` 处扩展）。
- **SavePageList**：同一 `byte[]` 的逻辑切片链表，用于删除时 `CutPage`、GC 时按序紧凑拷贝。
- 每页通过 `GCHandle.Alloc(..., Pinned)` 钉住，`Address` 属性提供稳定 `IntPtr`（兼容旧代码）。

### 1.3 VariableListItem（memSID 元数据）

```34:51:D:\项目\睿渥\RWVDCS\RTD\MemoryManage.cs
        [StructLayout(LayoutKind.Explicit)]
        private struct VariableListItem
        {
            [FieldOffset(0)]  public int pageIndex;
            [FieldOffset(4)]  public uint variableLength;
            [FieldOffset(8)]  public long variableOffset;
            [FieldOffset(16)] public int prevVariableIndex;
            [FieldOffset(20)] public int nextVariableIndex;
            [FieldOffset(24)] public VariableState state;
            [FieldOffset(28)] public WriteReadAbility wrAbility;
```

- 变量在页内通过 **双向链表**（`firstVariableIndex` / `lastVariableIndex`）串联。
- 删除后 SID 进入 `reclaimVariableList`，`state = Dirty`。
- **读写能力**（NWR/R/W/WR）在 VariableListItem 层 enforcement。

### 1.4 MemorySlot（零拷贝视图）

```1660:1692:D:\项目\睿渥\RWVDCS\DCSCommon\DataType.cs
    public readonly struct MemorySlot
    {
        public readonly byte[] Data;
        public readonly int Offset;
        public readonly int Length;
        // Read<T>/Write<T> 使用 Unsafe.ReadUnaligned/WriteUnaligned
```

`GetSlot(SID)` 返回指向页面 `byte[]` 子区间的 **零拷贝** 视图：

```650:664:D:\项目\睿渥\RWVDCS\RTD\MemoryManage.cs
        public MemorySlot GetSlot(int SID)
        {
            // ...
            return new MemorySlot(pitem.data, pitem.dataOffset + (int)vitem.variableOffset, (int)vitem.variableLength);
```

### 1.5 SID / FSID 分配与寻址

**Master 分配全局 SID 段**（`AllocResource`）：

```962:966:D:\项目\睿渥\RWVDCS\RTD\RTD.cs
        private AllocSIDResource AllocResource(object sender, EventArgs e)
        {
            AllocSIDResource res = new AllocSIDResource(this.allocSID, this.preAllocNum);
            this.allocSID += (int)this.preAllocNum;
            return res;
```

**FSID 编码**（`PointManage.Translate`）：

```3332:3358:D:\项目\睿渥\RWVDCS\RTD\PointManage.cs
        public void Translate(int SID, uint Offset, ref long FSID)
        {
            // resourceList 中 baseSID + 局部偏移 → globalSID
            FSID = sid;
            FSID <<= 32;
            FSID += Offset;
        }
```

**FSID 解码**：

```3173:3205:D:\项目\睿渥\RWVDCS\RTD\PointManage.cs
        public void Translate(long FSID, ref int SID, ref uint Offset)
        {
            int sid = (int)(FSID >> 32);
            uint offset = (uint)(FSID & 0xffffffff);
            // 映射 globalSID → 本地 SID
        }
```

- `offset == uint.MaxValue` 表示**整点**（非成员域）。
- 每个 Slave 持有一段 `AllocSIDResource`（`baseSID` + `preAllocNum`）。

### 1.6 变量布局

1. **值类型 Point**（LD/LA/LP 等）：整块 `byte[]` 按 CLR `Pack=1` 布局存储。
2. **引用类型 Block/FC**：`WiseNew` 在 `byte[]` 中伪造 CLR 对象头（syncblk + TypeHandle + 引用指针链），详见第 2 节。
3. **ConnectPointToPin** 将 Pin 字段写成 Point 的**绝对 IntPtr 地址**（内存别名，非拷贝）。

### 1.7 内存 GC（GarbageCollection）

利用率低于阈值或 Save 时触发 `GarbageCollection()`：将所有有效字节紧凑到单块 `byte[]`，更新 `variableOffset`，**先** 触发 `MemoryRebuilt` 事件清 fast-cache，**再** `Unpin` 旧页（见 §10）。

---

## 2. TypeManage：类型解析与伪造对象

### 2.1 初始化流程

```111:149:D:\项目\睿渥\RWVDCS\RTD\TypeManage.cs
        private bool Init()
        {
            InitBaseType();
            foreach (Type t in PointManufactory.Types)
                ParseType(t, 0);
            foreach (Type t in FCManufactory.Types)
                ParseType(t, 0);
```

### 2.2 长度计算（ComputeLength）

- **基本类型**：`Marshal.SizeOf`（bool 特殊为 1 字节）。
- **引用类型/类**：读 `Marshal.ReadInt32(T.TypeHandle.Value, 4)` 得 MethodTable 基大小，再递归累加引用成员。
- **数组/字符串**：手工模拟 CLR 布局（syncblk + TypeHandle + length + 元素区）。

### 2.3 字段偏移（ParseType + ChildTableItem）

```443:474:D:\项目\睿渥\RWVDCS\RTD\TypeManage.cs
            foreach (FieldInfo info in T.GetFields(...))
            {
                offset = (ushort)Marshal.ReadInt16(info.FieldHandle.Value, 8);
                // PinTypeAttribute → citem.PinType
                // PinAttribute → citem.IsPinner
                if (T.IsValueType)
                    citem.Offset = (uint)offset;
                else
                    citem.Offset = (uint)offset + 8; // +8: syncblk + TypeHandle
```

- **PinTypeAttribute**：标记管脚类型（Input/Output/IO 等）。
- **PinAttribute**：标记 Pinner 字段（连接时不分配子对象，写 0）。
- **ReferenceOffset**：引用类型成员在对象体内的绝对偏移（跳过 syncblk 后指向 TypeHandle 区）。

### 2.4 WiseNew / WiseCopy / WiseClone（伪造对象 hack）

**仍在使用**，是 RTD 核心机制：

| 方法 | 作用 | 实现要点 |
|------|------|----------|
| `WiseNew` | 在 byte[] 中构造假 CLR 对象 | 写 syncblk(0)、TypeHandle、引用指针链；`writeDefaults=false` 时保留已加载字节 |
| `WiseCopy` | 托管对象间字段拷贝 | `MemberwiseClone` 风格递归；值类型/数组 `CopyTo` |
| `WiseClone` | 浅克隆 | 反射 `MemberwiseClone` |

IntPtr 版 `WiseNew`（830-964 行）与 MemorySlot 版（972-1123 行）并存；后者用 `Unsafe.WriteUnaligned` 写 byte[]。

### 2.5 unsafe / Marshal 现状

- RTD 目录内 **已无 `unsafe` 块**（注释说明曾去除 `Marshal.AllocHGlobal`）。
- **大量使用**：`Marshal.ReadInt32/WriteInt32`、`Marshal.Copy`、`Marshal.ReadInt16`（读 FieldHandle 偏移）、`Unsafe.WriteUnaligned`、`GCHandle.Pinned`。
- **bool 布局错配**：CLR 中 bool=1 字节，`Marshal.SizeOf` 对含 bool 的结构=4 字节/字段 → 整体 `PtrToStructure/StructureToPtr` 会越界；已通过 `WriteStructFieldByField` + `PinCalculationCache` 规避。

---

## 3. PointManage：点注册、连接、读写、强制

### 3.1 点注册（New）

```526:639:D:\项目\睿渥\RWVDCS\RTD\PointManage.cs
        public int New(string Name, string TypeName, VariableClass Class, DefinedType DefinedType)
        {
            lock (objLock) {
                if (nameTable.TryGetValue(Name, out index)) return index;
                int memsid = memoryManage.New(item.Length);
                typeManage.WiseNew(typeid, memoryManage[memsid]);
                typeManage.WiseCopy(...); // 默认值
                nameTable[Name] = sid;
            }
        }
```

- **nameTable**：`Dictionary<string,int>`（OrdinalIgnoreCase）→ 名称到 SID。
- **nameList**：SID → 名称字符串（支持 reclaim）。
- **预分配**：`preAllocNum` 耗尽时通过 `SIDConsume` 回调 Master 的 `AllocResource`。
- **DefinedType.Rent**：跨控制器租用的代理点。

### 3.2 ConnectPointToPin

```2950:3048:D:\项目\睿渥\RWVDCS\RTD\PointManage.cs
        public bool ConnectPointToPin(VariableAddress Point, VariableAddress Pin)
        {
            // 取 Point/Pin 的 IntPtr
            // 将 ptr1 写入 Pin 字段（引用类型 +4 跳过 syncblk）
            Unsafe.WriteUnaligned(ref pinSlot.Data[pinSlot.Offset], ptr1.ToInt32() [+4]);
            // 双向 Link 记录到 links 字典
        }
```

- **物理效果**：Pin 与 Point **共享同一块 byte[]**（指针别名）。
- **links**：`Dictionary<int, List<Link>>`，记录 sourceoffset ↔ targetsid/targetoffset。

### 3.3 快速读写路径

| 路径 | 锁 | 场景 |
|------|-----|------|
| `TryGetBufferValueFast` / `TrySetBufferValueFast` | **无锁**（ConcurrentDictionary 缓存 IntPtr） | HTTP 读、DPU Pin 写 |
| `TryEqualsBuffered` | 无锁 | TriggerRefresh 差值检测，跳过 Clone 装箱 |
| `SetBatch` | 一次 `lock(this)` | IOMAP/HMI 批量写 |
| `writeCache` | 在 `lock(this)` 内 | FSID → WriteCacheParams，避免重复反射 |
| `cloneCache` | ConcurrentDictionary | Clone(SID,offset) 热路径 |
| 索引器 `[User, FSID]` | `lock(this)` | 通用读写 |

**BufferAccessRevision**：`ClearBufferFastCache` 后自增，防止 GC 搬移后使用悬空指针。

### 3.4 强制（Force）逻辑

RTD 层 **不单独实现 force**；强制语义在 **DCSType 结构体**内：

- `isforced != 0` 时，`Value`/`ForceValue` setter 将 `buffer = forcevalue`。
- 写 `isforced` 也会立即把 buffer 设为 forcevalue（见 LD 116-124 行、LA 96-104 行）。
- 经 `ConnectPointToPin` 别名后，对 Pin.buffer 的写入同样作用于 Point.buffer。
- IOMAP 独占：`TriggerRefresh` 中对 `IomapOwnership.IsOwned` 的点做周期末回盖（SubscribeManage 357-381 行）。

---

## 4. Master / Slave / Clone 同步机制

### 4.1 三种模式

```437:437:D:\项目\睿渥\RWVDCS\RTD\RTD.cs
        public RTDWorkMode Mode  // None, Master, Slave, Clone
```

| 模式 | 角色 | 数据所在 |
|------|------|----------|
| **Master** | 全局协调、分配 SID、持有 TypeManage | 订阅缓存 + 路由到 Slave |
| **Slave** | 实际 DPU 实时库 | pointManage + memoryManage |
| **Clone** | Master 为每个 Slave 创建的**镜像 RTD** | 无独立内存，转发到真实 Slave |

### 4.2 注册关系（AddRTD）

```2184:2207:D:\项目\睿渥\RWVDCS\RTD\RTD.cs
        public bool AddRTD(IRTD rtd)
        {
            rtd.SIDConsume = this.SIDConsume;
            IRTD temp = (IRTD)rtd.Clone();
            temp.Mode = RTDWorkMode.Clone;
            temp.Master = this;
            temp.Communications[rtd.Name] = rtd;
            this.communications[rtd.Name] = rtd;
            rtd.Communications[temp.Name] = temp;
            this.rtds[rtd.Name] = temp;
        }
```

### 4.3 TriggerRefresh（Slave 周期末）

```2131:2139:D:\项目\睿渥\RWVDCS\RTD\RTD.cs
        public void TriggerRefresh()
        {
            if (this.mode == RTDWorkMode.Slave)
                this.subscribeManage.TriggerRefresh();
        }
```

流程（SubscribeManage 295-406 行）：
1. 遍历 `subscribeList`（SID → offset → outerFSID）。
2. IOMAP 占用点：跳过 Outer 传播或回盖 HMI 值。
3. `TryEqualsBuffered` 未变则 skip。
4. 否则 `Clone` → `subscribeManage[RTD.Name, outerFSID, Outer] = obj`。
5. Outer setter 向 Master/Clone/通信 RTD 广播。

### 4.4 RefreshNotifier（Master 侧通知）

```2160:2176:D:\项目\睿渥\RWVDCS\RTD\RTD.cs
        public void RefreshNotifier()
        {
            lock (objLock) {
                if (informList.Count > 0)
                    InformRefreshHandler(new InformRefreshArgs(datas));
                informList.Clear();
            }
        }
```

Master 写 Inner 时累积 `informList`，周期末批量 `RefreshData` → 再次走订阅 Outer 路径。

### 4.5 ExchangeData / AcceptData（增量同步）

```2213:2262:D:\项目\睿渥\RWVDCS\RTD\RTD.cs
        public void ExchangeData()  // Slave/Clone → 序列化 Changed 的 PointManage/SubscribeManage
        public void AcceptData(...) // 对端 Load(stream) 全量替换
```

- 标记字：`SaveMarkWord.PointManage / SubscribeManage`。
- **注意**：`AcceptData` 直接调用 `Load(s)`，是**嵌套 Load** 而非增量 merge。

### 4.6 数据拷贝方向（SubscribeManage 索引器）

- **Inner**：外部 → RTD buffer（HMI/IOMAP/跨控制器）。
- **Outer**：RTD buffer → 订阅缓存 → 广播到其他 RTD。
- **Clone**：Inner/Outer 均转发到 `communications` 中的真实 Slave 或 `Master`。

---

## 5. SubscribeManage 订阅发布机制

### 5.1 数据结构

```49:77:D:\项目\睿渥\RWVDCS\RTD\SubscribeManage.cs
        Dictionary<int, Dictionary<uint, long>> subscribeList;  // sid → offset → fsid
        Dictionary<long, object> outerDataList;   // 对外通知缓存
        Dictionary<long, object> innerDataList;   // 对外写入缓存
        Dictionary<long, long> inner2outer / outer2inner;  // 跨控制器 FSID 映射
```

### 5.2 订阅粒度

- **整点**：`offset == uint.MaxValue`，Lease/Subscribe 可展开所有成员 offset。
- **成员域**：`(SID, offset)` 单字段 FSID。
- **跨控制器**：Master 向 Slave `Lease`，Slave 侧 `AppendTransform` 建立 inner/outer FSID 映射。

### 5.3 通知方式

- **差分通知**：Outer 方向比较 `GetHashCode()`（数组逐元素 float 比较）。
- **TriggerRefresh**：每 DPU 周期全量扫描 subscribeList（有 TryEqualsBuffered 优化）。
- **RefreshData**：按 `InformData[]` 增量推送。

### 5.4 性能特征

- **热点优化**：`_cachedSids` / `_cachedOffsets` 避免每周期分配 List。
- **瓶颈**：`Clone` 装箱 + `GetHashCode` 比较（非 Equals）；subscribeList 全扫描 O(订阅数)。
- **IOMAP 写**：默认 `EnableIomapWriteQueue=true`，入队后在 DPU `DrainIomapPendingWrites` 安全点写入。

---

## 6. 快照 Save / Load 完整流程

### 6.1 调用链

```
RTD.Save(stream)
  ├─ [Master] TypeManage.Save   — 类型名→index 表
  ├─ PointManage.Save
  │    └─ MemoryManage.Save      — 核心内存 blob
  └─ SubscribeManage.Save       — inner2outer 键值对

RTD.Load(stream) — 逆序，Load 后 Start() + WiseNew(..., false)
```

### 6.2 RTD 头（1126-1135 行）

| 字段 | 类型 |
|------|------|
| name | string |
| mode | int |
| enableWirterTrigger | bool |
| allocSID | int |
| preAllocNum | uint |
| metaModified | bool |
| plugTimeMarks[] | long[] |

### 6.3 MemoryManage blob（561-622 行）

```
bool recyclingAtSerializing
double acceptableUtilizationRatio
uint autoCollationSpan
int first/lastVariableIndex
long memUseLength
int pageLength
int reclaimCount, variableCount
byte[] = reclaimList + VariableListItem[] + 所有页面原始字节
```

Load 时单页分配 `memUseLength` 大小并 BlockCopy（473-534 行）。

### 6.4 PointManage blob（2423-2506 行）

- nameList、nameTable、aliasTable、links（Link 三元组）
- reclaimIndex/Name、resourceList、itemList（二进制 struct 数组）
- Load 后：**WiseNew(..., writeDefaults: false)** 仅修复对象头，不覆盖数据（2362-2377 行）

### 6.5 SubscribeManage blob（177-201 行）

- `count` + `(innerFSID, outerFSID)*`

### 6.6 性能瓶颈

1. **Save 前 ForbidEdit + 等待 editingCount**（可能 Spin 200×5ms）。
2. **可选 GarbageCollection**（`recyclingAtSerializing`）全内存紧凑 + 指针失效 → 必须清 cache。
3. **PointManage.Save 末尾 `GC.Collect()`**（2518 行）— 全量 GC 停顿。
4. **单块 byte[] 分配** `reclaim + variables + pages`，大工况内存峰值翻倍。
5. **TypeManage.Load** 按快照顺序重 ParseType，插件元数据变化会导致 index 漂移风险（1504-1505 行注释）。

---

## 7. RTD 对外公开 API 清单

`RTD` 类实现 `IRTD`（`DCSCommon\Interface.cs:328`），对外主要入口如下：

### 7.1 生命周期

```csharp
bool Start(); bool Stop(); bool Restart(); bool Reset();
bool Clear(); bool Load(Stream fs); bool Save(Stream fs);
void Dispose();
bool AddRTD(IRTD rtd);
```

### 7.2 变量 CRUD 与寻址（继承 IDataBase）

```csharp
int New(string Name, string TypeName, VariableClass Class);
bool Delete(long FSID); bool Delete(int SID);
long GetFSID(int SID, uint Offset);
void Translate(long FSID, ref int SID, ref uint Offset);
void Translate(int SID, uint Offset, ref long FSID);
VariableAddress GetVariableAddress(params string[] Names);
IntPtr GetVariableAddress(long FSID);
MemorySlot GetVariableSlot(long FSID);
string GetVariableName(long FSID); string GetVariableTypeName(long FSID);
uint GetVariableLength(long FSID);
bool CopyTo(long TargetFSID, long SourceFSID, bool IsReversed);
bool SetWriteReadAbility(long FSID, WriteReadAbility ability);
WriteReadAbility GetWriteReadAbility(long FSID);
long AppendaAliasSystem(string[][] AliasArray);
```

### 7.3 读写（索引器 + 快速路径）

```csharp
object this[string User, long FSID] { get; set; }
object this[int SID] { get; set; }
object this[VariableAddress Address] { get; set; }
object this[int SID, uint Offset] { get; set; }
int this[string Name] { get; }
long this[params string[] names] { get; }
object GetVariable(long FSID);

bool TryGetVariableFast(long FSID, out object value);
bool TrySetBufferValueFast(long FSID, object value);
bool TryEqualsBufferedFast(long FSID, object oldBoxed);
bool TryGetBufferAccess(long FSID, out IntPtr ptr, out Type fieldType, out int length, out IRTD ownerRtd);
object ReadBufferAt(IntPtr ptr, Type fieldType, int length);
bool WriteBufferAt(IntPtr ptr, Type fieldType, int length, object value);
int BufferAccessRevision { get; }
void ClearBufferFastCache();
void SetMasterLocalBatch(string User, long[] FSIDs, object[] Values);
void SetSlaveLocalBatch(string User, long[] FSIDs, object[] Values);
```

### 7.4 订阅与租约

```csharp
long Subscribe(params string[] names);
bool UnSubscribe(long FSID, bool IsForced);
long ReversedSubscribe(params string[] names);
bool UnReversedSubscribe(long FSID);
LeaseParams Lease(string user, params string[] names);
void UnLease(long fsid);
void TriggerRefresh();
void DrainIomapPendingWrites();
void RefreshNotifier();
void ExchangeData();
void AcceptData(string RtdName, byte[] buffer);
```

### 7.5 连接与元数据

```csharp
bool ConnectPointToPin(VariableAddress Point, VariableAddress Pin);
bool DisconnectPointToPin(VariableAddress Pin);
long[] GetLinkedVariable(long FSID);
BlockDetails[] GetBlockDetails(...); PointDetails[] GetPointDetails(...);
ChildTableItem GetMemberDetails(params string[] names);
```

### 7.6 属性

```csharp
string Name; RTDWorkMode Mode; IRTD Master;
ITypeManage TypeManage;
Dictionary<string, IRTD> Communications / Rtds;
bool EnableWirterTrigger; bool Runable; uint CycleCount;
```

---

## 8. LD / LA / LP / LP32 内存布局

均 `[StructLayout(Sequential, Pack=1)]`，TypeManage 按 **CLR 实际布局**（`FieldHandle+8`）计算 offset 与 `Length`。

### 8.1 LD（Digital 点）— 约 **10 字节**

| 偏移 | 字段 | 类型 | 说明 |
|------|------|------|------|
| 0 | quality | QualityTypes (4) | 信号质量 |
| 4 | istrace | bool (1) | 跟踪 |
| 5 | isalarm | bool (1) | 报警 |
| 6 | isConnected | bool (1) | 连线状态 |
| 7 | forcevalue | bool (1) | 强制值 |
| 8 | isforced | byte (1) | 强制标志 |
| 9 | buffer | bool (1) | 过程值 |

来源：`D:\项目\睿渥\RWVDCS\DCSType\LD.cs:26-130`

### 8.2 LA（Analog 点）— 约 **28 字节**

| 偏移 | 字段 | 类型 |
|------|------|------|
| 0 | quality | QualityTypes (4) |
| 4 | istrace | bool (1) |
| 5 | isalarm | bool (1) |
| 6 | forcevalue | float (4) |
| 10 | isforced | byte (1) |
| 11 | maxreached | bool (1) |
| 12 | minreached | bool (1) |
| 13 | ishighalarm | bool (1) |
| 14 | islowalarm | bool (1) |
| 15 | isConnected | bool (1) |
| 16 | maxvalue | float (4) |
| 20 | minvalue | float (4) |
| 24 | buffer | float (4) |

来源：`D:\项目\睿渥\RWVDCS\DCSType\LA.cs:26-217`

### 8.3 LP（16-bit Packed Digital）— 约 **12 字节**

| 偏移 | 字段 | 类型 |
|------|------|------|
| 0 | quality | QualityTypes (4) |
| 4 | istrace / isalarm / isConnected | bool×3 |
| 7 | forcevalue | ushort (2) |
| 9 | isforced | byte (1) |
| 10 | buffer | ushort (2) |

`this[int i]` 支持按位读写 0-15 位。来源：`LP.cs`

### 8.4 LP32 — 约 **16 字节**

与 LP 相同结构，但 `forcevalue`/`buffer` 为 **uint (4)**，支持 0-32 位。来源：`LP32.cs`

### 8.5 Marshal.SizeOf 与 CLR 差异

- `Marshal.SizeOf(LD/LA/LP/LP32)` **大于** `citem.Length`（bool 在 Marshal 中按 4 字节）。
- 代码中 `IsStructLayoutMismatch` 检测此差异，强制走 `PinCalculationCache` 子字段读写（PointManage 3663-3671 行）。

### 8.6 LALDLP

`LALDLP<T>` 为 **抽象类**（非 RTD 内联存储类型），提供与 LA/LD/LP 的类型转换运算符；实际 Point 类型仍是 LD/LA/LP/LP32 结构体。

---

## 9. 线程与锁模型

### 9.1 锁一览

| 组件 | 锁 | 保护对象 |
|------|-----|----------|
| MemoryManage | `lock(this)` | GetSlot/SetSlot/索引器 |
| MemoryManage | `lock(editLock)` | New/Delete |
| MemoryManage | `lock(objLock)` | memLock |
| MemoryManage | `Interlocked` + `ForbidUse/ForbidEdit` | 读写与 GC 互斥 |
| PointManage | `lock(this)` / `lock(objLock)` | 索引器、SetBatch、TryBuildAndReadFast 冷路径 |
| SubscribeManage | `lock(subscribeList)` | 订阅表 + inner/outerDataList 写 |
| SubscribeManage | `lock(objLock)` | inner2outer 读属性 |
| TypeManageItem | `objLock`（per-type） | Clone 读 |
| RTD | `lock(objLock)` | informList、RefreshNotifier |

### 9.2 无锁路径

- `_bufferFastCache` / `cloneCache`（ConcurrentDictionary）
- `TryGetBufferValueFast` / `TrySetBufferValueFast`（注释：x86 对齐读写原子性，复杂 struct 可能撕裂）
- MemoryManage.Copy/New 的部分路径仅用 `Interlocked` 计数 + spin wait

### 9.3 潜在竞态

1. **无锁读 vs DPU 写**：HTTP fast-path 与周期写共享 pinned byte[]，可能读到撕裂值（已接受）。
2. **MemoryRebuilt 与无锁写**：若未清 cache，悬空 IntPtr → EEE（已有 MemoryRebuilt + BufferAccessRevision 缓解）。
3. **outerDataList 的 GetHashCode 比较**：非语义相等，可能漏通知或误通知。
4. **ConnectPointToPin 无锁**：连接时与其他写并发未显式同步。
5. **SetWriteReadAbility 未写回 List**（见 §10.1）：WR 能力设置可能无效。
6. **Master informList** 与 Slave TriggerRefresh 并行，依赖周期边界。

---

## 10. 崩溃 / 内存安全隐患（代码证据）

### 10.1 SetWriteReadAbility 结构体未写回（逻辑 bug）

```177:189:D:\项目\睿渥\RWVDCS\RTD\MemoryManage.cs
                VariableListItem vitem = this.VariableList[SID];
                vitem.wrAbility = ability;
                return true;  // 未执行 VariableList[SID] = vitem
```

读写能力修改**不会持久**到 variableList。

### 10.2 Collation 等待条件疑似反了

```631:636:D:\项目\睿渥\RWVDCS\RTD\MemoryManage.cs
            while (editingCount == 0 && tick < 200) { Thread.Sleep(5); tick++; }
```

与 Save 中 `while (editingCount > 0 ...)` 相反；可能在仍有编辑操作时启动 GC。

### 10.3 ConnectPointToPin 链接表键错误

```3035:3039:D:\项目\睿渥\RWVDCS\RTD\PointManage.cs
                    else if (list == null)
                    {
                        list = new List<Link>();
                        list.Add(link2);
                        links[Point.Sid] = list;  // 应为 links[Pin.Sid]
                    }
```

Pin 侧连接表可能挂到错误 SID，导致 Disconnect/GetLinkedVariable 行为异常。

### 10.4 Link / PointManageItem 相等性仅用 GetHashCode

```216:221:D:\项目\睿渥\RWVDCS\RTD\DataType.cs
        public static bool operator ==(Link link1, Link link2)
        {
            if (link1.GetHashCode() == link2.GetHashCode()) return true;
```

`List.Contains(link1)` 可能误判，连接重复或遗漏。

### 10.5 悬空指针 + 无锁写（历史 EEE 根因，部分已修）

MemoryManage 909-914 行注释 + PointManage `ClearBufferFastCache`：GC 搬移 byte[] 后 `_bufferFastCache`/`cloneCache` 中 IntPtr 悬空 → `ExecutionEngineException`。

SubscribeManage 375-378 行：**已禁用** TriggerRefresh 中无锁 `TrySetBufferValueFast`，改走带锁索引器。

### 10.6 32 位绝对地址假设

WiseNew / ConnectPointToPin 广泛使用 `ToInt32()` 写指针（如 2983-2985 行）。在 **64 位进程**中 pinned 数组地址可能超出 Int32，导致截断错误（项目目标 x86 则风险较低）。

### 10.7 AcceptData 嵌套 Load 风险

```2257:2261:D:\项目\睿渥\RWVDCS\RTD\RTD.cs
                MemoryStream s = new MemoryStream(buffer);
                this.Load(s);  // 重新解析 RTD 头 + 子模块，非增量
```

ExchangeData 发出的 buffer 若不含完整 RTD 头，Load 会失败或 corrupt；且 Load 内 `Start()` 可能重复注册。

### 10.8 Save 末尾强制 GC

PointManage.Save `finally { GC.Collect(); }`（2518 行）可能造成生产环境长时间 STW。

### 10.9 TypeManage 依赖 CLR 内部布局

`Marshal.ReadInt32(T.TypeHandle.Value, 4)`、`Marshal.ReadInt16(info.FieldHandle.Value, 8)`（292、450 行）—— .NET 版本/架构变化可能导致类型长度与偏移错误。

### 10.10 inner/outer 字典并发（已部分修复）

SubscribeManage 注释（1637-1641、1553-1555 行）记录：OWIN 高并发无锁写 `innerDataList` 曾导致 Dictionary 内部损坏 → EEE；已统一到 `lock(subscribeList)`。

---

## 重构建议摘要（供后续方案参考）

1. **寻址层**：考虑用 `(memSID, offset)` 或 `MemorySlot` 替代 Int32 绝对地址；FSID 编码保留兼容层。
2. **类型系统**：逐步脱离 MethodTable/FieldHandle hack，改为显式 schema（代码生成或 Source Generator）。
3. **伪造对象**：Block 类型可评估 `struct + blittable` 或独立 Pin 堆，减少 WiseNew 复杂度。
4. **同步**：ExchangeData/AcceptData 改为增量 delta；统一 Master/Slave 通知为单通道事件总线。
5. **并发**：fast-path 与 GC 用 epoch/revision 统一失效；消除 GetHashCode 差分比较。
6. **快照**：去掉 Save 时 `GC.Collect()`；流式/chunked 序列化降低峰值内存。
7. **必修 bug**：SetWriteReadAbility 写回、ConnectPointToPin links 键、Collation 等待条件、Link 相等性。

---

如需针对某一子模块（例如仅 MemoryManage 重构或仅 Subscribe 协议）出详细重构设计，可以指定范围继续展开。

[REDACTED]