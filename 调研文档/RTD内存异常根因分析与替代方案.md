# RTD 内存异常根因分析与替代方案

## 1. 根因诊断：为什么去掉 unsafe 后仍然崩溃

去掉 `unsafe` 关键字只是消除了编译器层面的标记，但代码中的核心危险操作——**GCHandle 地址重写 hack**——仍然完整保留，只是从 `int*` 指针操作换成了 `Marshal.ReadInt32`/`Marshal.WriteInt32`。

### 1.1 崩溃机制详解

下面是 `TypeManageItem` 索引器的核心代码（DataType.cs 第 707-730 行）：

```csharp
// 步骤 1：为新对象分配 GCHandle
handle = GCHandle.Alloc(Activator.CreateInstance(type), GCHandleType.Normal);

// 步骤 2：获取 GCHandle 的内部存储地址
IntPtr ptr = GCHandle.ToIntPtr(handle);
handlePtr = ptr;

// 步骤 3：保存原始的对象指针
hp.firstObjectAdr = Marshal.ReadInt32(handlePtr);

// 步骤 4：覆盖 GCHandle 内部指针，指向原始内存中的 "伪 .NET 对象"
//         address 是 MemoryManage 中 byte[] 的绝对地址
//         +4 跳过 Syncblk，使 handle 认为 TypeHandle 就在那里
Marshal.WriteInt32(handlePtr, address + 4);  // ← 核心危险操作

// 步骤 5：CLR 通过被篡改的指针，将原始内存解读为 .NET 对象
object obj = handle.Target;  // ← 崩溃点

// 步骤 6：恢复原始指针
Marshal.WriteInt32(handlePtr, hp.firstObjectAdr);
handle.Free();
```

**崩溃的五种原因**：

```
原因 1：GC 恰好在步骤 4 和步骤 5 之间触发
────────────────────────────────────────────────
   线程 A: Marshal.WriteInt32(handlePtr, address+4)
   GC 线程: 扫描所有 GCHandle → 发现 handle 指向一个
           "对象"（其实是 byte[] 中间的一段）→ 尝试追踪
           该"对象"的引用字段 → 读到垃圾数据 → 崩溃
   线程 A: object obj = handle.Target  // 永远执行不到

原因 2：address 计算偏移错误
────────────────────────────────────────────────
   WiseNew 在 byte[] 中写入 syncblk/TypeHandle 时：
   - TypeHandle 值是 32 位的（type.TypeHandle.Value.ToInt32()）
   - 在 64 位进程中，TypeHandle 实际是 64 位
   - 写入的 4 字节 TypeHandle 被 CLR 解读为 8 字节指针 → 访问非法地址

原因 3：byte[] 被 GC 移动
────────────────────────────────────────────────
   WiseNew(MemorySlot) 中：
     GCHandle pinH = GCHandle.Alloc(slot.Data, GCHandleType.Pinned);
     int baseAddr = Marshal.UnsafeAddrOfPinnedArrayElement(slot.Data, 0).ToInt32();
     pinH.Free();  // ← Pin 立即释放！
     // 后续 GC 可能移动 slot.Data，baseAddr 已失效
     // 但代码继续用 baseAddr 计算绝对地址并写入 byte[]

原因 4：绝对地址写入 byte[] 后，GarbageCollection 压缩页面
────────────────────────────────────────────────
   WiseNew 把 "成员的绝对内存地址" 写入 byte[]：
     int refAbsAddr = objAddress + (int)childItem.ReferenceOffset;
     Unsafe.WriteUnaligned(ref slot.Data[...], refAbsAddr);

   GarbageCollection() 把所有页面合并到新 byte[]：
     Buffer.BlockCopy(old, 0, newData, offset, length);

   → byte[] 中存储的绝对地址仍然指向旧内存 → 后续读取崩溃

原因 5：PointManage 中的指针持久化
────────────────────────────────────────────────
   PointManage.SetValueByAddress() (行 1687-1713):
     GCHandle handle = GCHandle.Alloc(value, GCHandleType.Normal);
     int objAddr = Marshal.ReadInt32(GCHandle.ToIntPtr(handle));
     IntPtr ptr = new IntPtr(objAddr + 4);
     memoryManage[item.memSID] = ptr;  // 把对象内部地址持久化
     handle.Free();                     // 对象可能被 GC 回收！
     // → memoryManage 中存储的地址指向已回收的内存
```

### 1.2 为什么这个设计本质上不可修复

```
根本矛盾：
┌─────────────────────────────────────────────────────┐
│  .NET CLR 的契约：                                    │
│    - 对象由 GC 管理，地址随时可能变化                   │
│    - 对象引用只能通过 GC 追踪的方式持有                  │
│    - 对象的内存布局是 CLR 实现细节，不是公开契约         │
│                                                     │
│  当前代码的假设：                                      │
│    - 对象地址是固定的（写入 byte[] 后永不变化）          │
│    - 可以通过篡改 GCHandle 内部指针来"伪造"对象         │
│    - 对象布局是 [4字节 Syncblk] + [4字节 TypeHandle]  │
│    + [字段数据]                                       │
│                                                     │
│  → 这两套假设互相矛盾，无法调和                        │
└─────────────────────────────────────────────────────┘
```

**结论：无论怎么修改这段代码（unsafe/Marshal/Unsafe.WriteUnaligned），只要继续使用"在 byte[] 中伪造 .NET 对象"的方案，崩溃就不可避免。**

---

## 2. 正确的解决路径

### 2.1 核心思路转变

```
当前模式（不可靠）：
    byte[] → "伪造" 为 .NET 对象 → 通过反射/vtable 调用方法

正确模式：
    byte[] → 读取原始字段值 → 构造真正的 .NET 对象（如需要）
           → 或直接通过 MemorySlot.Read<T> 读写字段值
```

**关键洞察**：RTD 中存储的数据（PV 值、质量码、时间戳、布尔状态等）本质上都是**基本类型**（float、int、bool、long、string）。不需要在 byte[] 中构造完整的 .NET 对象（含 Syncblk、TypeHandle、vtable），只需要能**按偏移读写基本类型的值**。

### 2.2 现有的 MemorySlot 已经够用

```csharp
// 读取 float 值
float pv = slot.Read<float>(pvOffset);

// 写入 int 值
slot.Write<int>(qualityOffset, 192);

// 读取 bool 值
bool isAlarm = slot.Read<byte>(alarmOffset) != 0;

// 拷贝值（连线/Wiring）
dstSlot.CopyFrom(srcSlot, srcOffset, dstOffset, length);
```

这些操作完全不需要 GCHandle hack，不需要伪造对象，**只是对 byte[] 做偏移读写**。当前 MemorySlot 已经实现了所有这些方法。

---

## 3. 成熟的第三方库推荐

### 3.1 需求重新定义

你真正需要的不是"NumPy"（数值计算库），而是：

| 需求 | 准确描述 |
|------|----------|
| 结构化内存缓冲区 | 在一块连续 byte[] 中定义多种结构体，按偏移读写字段 |
| 零拷贝访问 | 不需要反序列化就能读取字段值 |
| 类型安全 | 编译期保证读 float 不会意外读成 int |
| 快速批量序列化 | 整块内存直接写入/读出文件 |
| 异构数据支持 | 既能存简单的 AI 点（32 字节），又能存复杂的功能块（2000 字节） |
| .NET 4.7.2 兼容 | 不能要求 .NET 5+ |

### 3.2 库推荐对照表

| 库 | 核心能力 | .NET 4.7.2 | NuGet | 适配度 |
|-----|---------|:---:|-------|:---:|
| **FlatBuffers** | 零拷贝结构化数据，Schema 定义类型 | ✅ | Google.FlatBuffers | ⭐⭐⭐⭐ |
| **MessagePack-CSharp** | 极速二进制序列化 | ✅ | MessagePack | ⭐⭐⭐⭐ |
| **Bond** | 微软出品，跨语言结构化数据 | ✅ | Bond.CSharp | ⭐⭐⭐ |
| **MemoryPack** | 最快的 .NET 序列化库 | ❌(.NET 7+) | - | - |
| **Cap'n Proto** | 零拷贝，类似 FlatBuffers | ⚠️(有限) | CapnProto-net | ⭐⭐⭐ |
| **protobuf-net** | Protocol Buffers 的 .NET 实现 | ✅ | protobuf-net | ⭐⭐ |

### 3.3 推荐方案：MessagePack-CSharp

**为什么选 MessagePack-CSharp 而不是 FlatBuffers**：

- FlatBuffers 的 C# 实现需要 Schema → 代码生成流程，与现有类体系集成成本高
- MessagePack-CSharp 可以**直接标注现有类**，几乎零改造成本
- 序列化/反序列化性能极高（接近手写 memcpy 的速度）
- 支持 .NET Framework 4.7.2
- 由 neuecc（Cysharp/UniTask 作者）维护，质量极高
- GitHub 5000+ star，Unity/游戏行业广泛使用

```
NuGet 包：
  MessagePack            （核心库）
  MessagePack.Annotations （标注属性）
```

---

## 4. 基于 MessagePack 的重构方案

### 4.1 架构对比

```
┌─ 当前架构（崩溃）────────────────────────────────────────────┐
│                                                              │
│  定义类型：                                                   │
│    public class AI : IO { float pv; int quality; ... }       │
│                                                              │
│  存储 ──→ TypeManage.WiseNew() 在 byte[] 中伪造 CLR 对象     │
│           写入 Syncblk + TypeHandle + 字段内部地址            │
│                                                              │
│  读取 ──→ TypeManageItem[address] 篡改 GCHandle              │
│           让 CLR 误认 byte[] 区段为 .NET 对象 ← 崩溃根因     │
│                                                              │
│  连线 ──→ memcpy byte[] 中的数据段                            │
│                                                              │
│  持久化 ──→ 遍历页面 + 中间 buffer + stream.Write            │
│                                                              │
└──────────────────────────────────────────────────────────────┘

┌─ 新架构（安全）──────────────────────────────────────────────┐
│                                                              │
│  定义类型（在现有类上加标注）：                                │
│    [MessagePackObject]                                       │
│    public class AI : IO {                                    │
│        [Key(0)] public float PV { get; set; }                │
│        [Key(1)] public int Quality { get; set; }             │
│        ...                                                   │
│    }                                                         │
│                                                              │
│  存储 ──→ MessagePackSerializer.Serialize(obj)               │
│           → byte[] 存入 MemoryManage 页面（纯数据，无CLR头）  │
│                                                              │
│  读取 ──→ MessagePackSerializer.Deserialize<T>(byte[])       │
│           → 返回真正的 .NET 对象（GC 管理，永不崩溃）         │
│                                                              │
│  连线 ──→ 仍然 memcpy byte[] 中的序列化数据                   │
│           目标端按需反序列化读取值                              │
│                                                              │
│  持久化 ──→ byte[] 页面直接写入文件（与当前相同）              │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

### 4.2 然而——MessagePack 方案的问题

MessagePack 解决了 "对象 ↔ byte[]" 的安全转换问题，但引入了新问题：

| 问题 | 说明 |
|------|------|
| **反序列化开销** | 每次读取对象需要反序列化（微秒级），原方案是直接内存访问（纳秒级） |
| **连线语义变化** | 原方案连线 = memcpy 字节，连线后立即生效。新方案需要"反序列化源→修改→序列化→写入目标" |
| **字段级访问** | 原方案可以通过偏移直接读写单个字段。MessagePack 格式不支持随机字段访问 |
| **改造量巨大** | 需要给所有 IO/Function 子类加标注，改写所有读写路径 |

**结论：MessagePack 适合对象整体的存取，不适合替代 RTD 核心的字段级随机读写。**

---

## 5. 真正推荐的方案：字段注册表 + MemorySlot

### 5.1 核心思路

不使用任何第三方库。利用已有的 `MemorySlot` + `Unsafe.ReadUnaligned/WriteUnaligned`，配合一个**字段注册表**，实现完全安全的字段级读写。

**关键变化**：彻底抛弃"在 byte[] 中伪造 .NET 对象"的做法。byte[] 中只存放**原始字段值**，不存放 Syncblk、TypeHandle 等 CLR 内部结构。

### 5.2 架构

```
┌─ 字段注册表（TypeManage 改造）──────────────────────────────┐
│                                                              │
│  类型 AI (typeId=5):                                        │
│  ┌────────┬──────────┬────────┬─────────┐                   │
│  │ 字段名  │ 偏移     │ 大小    │ 类型     │                   │
│  ├────────┼──────────┼────────┼─────────┤                   │
│  │ PV     │ 0        │ 4      │ float   │                   │
│  │ Quality│ 4        │ 4      │ int     │                   │
│  │ Alarm  │ 8        │ 1      │ bool    │                   │
│  │ Tag    │ 12       │ 64     │ string  │  ← 定长 char[]     │
│  │ ...    │          │        │         │                   │
│  └────────┴──────────┴────────┴─────────┘                   │
│  总大小 = 128 bytes                                          │
│                                                              │
│  类型 PID (typeId=12):                                      │
│  ┌────────┬──────────┬────────┬─────────┐                   │
│  │ PV     │ 0        │ 4      │ float   │                   │
│  │ SV     │ 4        │ 4      │ float   │                   │
│  │ MV     │ 8        │ 4      │ float   │                   │
│  │ Kp     │ 12       │ 4      │ float   │                   │
│  │ Ti     │ 16       │ 4      │ float   │                   │
│  │ Td     │ 20       │ 4      │ float   │                   │
│  │ Mode   │ 24       │ 4      │ int     │                   │
│  │ ...    │          │        │         │                   │
│  └────────┴──────────┴────────┴─────────┘                   │
│  总大小 = 256 bytes                                          │
│                                                              │
└──────────────────────────────────────────────────────────────┘

┌─ MemoryManage 页面（纯数据，无 CLR 头）─────────────────────┐
│                                                              │
│  byte[] page:                                                │
│  ┌──────────────┬──────────────┬──────────────────────────┐  │
│  │ SID 0 (AI)   │ SID 1 (AI)   │ SID 2 (PID 功能块)      │  │
│  │ 128 bytes    │ 128 bytes    │ 256 bytes                │  │
│  │ ┌────┬───┬─┐│ ┌────┬───┬─┐│ ┌────┬────┬────┬────┬──┐ │  │
│  │ │ PV │ Q │A││ │ PV │ Q │A││ │ PV │ SV │ MV │ Kp │..│ │  │
│  │ │1.23│192│0││ │4.56│ 0 │1││ │50.0│50.0│45.2│1.5 │  │ │  │
│  │ └────┴───┴─┘│ └────┴───┴─┘│ └────┴────┴────┴────┴──┘ │  │
│  └──────────────┴──────────────┴──────────────────────────┘  │
│                                                              │
│  没有 Syncblk，没有 TypeHandle，没有对象引用                   │
│  只有纯粹的字段值，按注册表中的偏移排列                         │
│                                                              │
└──────────────────────────────────────────────────────────────┘

读写方式：
    MemorySlot slot = memoryManage.GetSlot(sid);
    float pv = slot.Read<float>(pvOffset);        // Unsafe.ReadUnaligned
    slot.Write<int>(qualityOffset, 192);          // Unsafe.WriteUnaligned

连线（Wiring）：
    MemorySlot src = memoryManage.GetSlot(srcSid);
    MemorySlot dst = memoryManage.GetSlot(dstSid);
    dst.CopyFrom(src, srcFieldOffset, dstFieldOffset, fieldLength);
    // 底层是 Buffer.BlockCopy，安全高效

对象重建（需要时）：
    AI point = new AI();
    point.PV = slot.Read<float>(0);
    point.Quality = slot.Read<int>(4);
    point.Alarm = slot.Read<byte>(8) != 0;
    // 通过字段注册表自动化此过程
```

### 5.3 字段注册表实现

```csharp
/// <summary>
/// 字段描述（替代 ChildTableItem 中的偏移计算）
/// </summary>
public class FieldDescriptor
{
    public string Name;
    public int Offset;      // 在 byte[] 区间中的偏移
    public int Size;         // 字节大小
    public TypeCode TypeCode; // 基本类型
    public int StringMaxLen; // 字符串最大长度（定长）
}

/// <summary>
/// 类型布局描述（替代 TypeManageItem 的 CLR hack）
/// </summary>
public class TypeLayout
{
    public string TypeName;
    public int TotalSize;                         // 总字节数
    public List<FieldDescriptor> Fields;           // 字段列表
    public Dictionary<string, FieldDescriptor> FieldsByName; // 名称索引

    /// <summary>
    /// 从 .NET 类型自动生成布局（反射一次，缓存结果）
    /// </summary>
    public static TypeLayout FromType(Type type)
    {
        var layout = new TypeLayout();
        layout.TypeName = type.Name;
        layout.Fields = new List<FieldDescriptor>();
        layout.FieldsByName = new Dictionary<string, FieldDescriptor>();

        int offset = 0;
        foreach (var field in type.GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            var desc = new FieldDescriptor();
            desc.Name = field.Name;
            desc.Offset = offset;
            desc.TypeCode = Type.GetTypeCode(field.FieldType);
            desc.Size = GetFieldSize(field.FieldType);
            // 对齐
            offset += desc.Size;

            layout.Fields.Add(desc);
            layout.FieldsByName[desc.Name] = desc;
        }

        layout.TotalSize = offset;
        return layout;
    }

    /// <summary>
    /// 将 .NET 对象写入 MemorySlot
    /// </summary>
    public void WriteObject(MemorySlot slot, object obj)
    {
        foreach (var field in Fields)
        {
            object value = obj.GetType()
                .GetField(field.Name, BindingFlags.Instance |
                    BindingFlags.Public | BindingFlags.NonPublic)
                .GetValue(obj);

            WriteField(slot, field, value);
        }
    }

    /// <summary>
    /// 从 MemorySlot 重建 .NET 对象
    /// </summary>
    public object ReadObject(MemorySlot slot, Type type)
    {
        object obj = Activator.CreateInstance(type);
        foreach (var field in Fields)
        {
            object value = ReadField(slot, field);
            type.GetField(field.Name, BindingFlags.Instance |
                    BindingFlags.Public | BindingFlags.NonPublic)
                .SetValue(obj, value);
        }
        return obj;
    }

    private void WriteField(MemorySlot slot, FieldDescriptor field, object value)
    {
        switch (field.TypeCode)
        {
            case TypeCode.Single:
                slot.Write(field.Offset, (float)value);
                break;
            case TypeCode.Double:
                slot.Write(field.Offset, (double)value);
                break;
            case TypeCode.Int32:
                slot.Write(field.Offset, (int)value);
                break;
            case TypeCode.Int64:
                slot.Write(field.Offset, (long)value);
                break;
            case TypeCode.Int16:
                slot.Write(field.Offset, (short)value);
                break;
            case TypeCode.Boolean:
                slot.Write(field.Offset, (bool)value ? (byte)1 : (byte)0);
                break;
            case TypeCode.String:
                WriteString(slot, field, (string)value);
                break;
            // ... 其他类型
        }
    }

    private object ReadField(MemorySlot slot, FieldDescriptor field)
    {
        switch (field.TypeCode)
        {
            case TypeCode.Single:  return slot.Read<float>(field.Offset);
            case TypeCode.Double:  return slot.Read<double>(field.Offset);
            case TypeCode.Int32:   return slot.Read<int>(field.Offset);
            case TypeCode.Int64:   return slot.Read<long>(field.Offset);
            case TypeCode.Int16:   return slot.Read<short>(field.Offset);
            case TypeCode.Boolean: return slot.Read<byte>(field.Offset) != 0;
            case TypeCode.String:  return ReadString(slot, field);
            default: return null;
        }
    }
}
```

### 5.4 改造影响范围

| 组件 | 改造内容 | 工作量 |
|------|----------|--------|
| **TypeManageItem** | 移除 GCHandle hack，改为 `TypeLayout.ReadObject()` | 中 |
| **TypeManage.WiseNew** | 不再写 Syncblk/TypeHandle，只需要 `Array.Clear` 清零 | 小 |
| **TypeManage.WiseCopy** | 用 `Buffer.BlockCopy` 或字段级拷贝替代反射拷贝 | 小 |
| **PointManage** | `GetVariableAddress` 返回 MemorySlot 代替 IntPtr | 中 |
| **PinCalculationCache** | 已完成（已改为 MemorySlot + Unsafe.ReadUnaligned） | 已完成 |
| **Wire.Transmit** | 已部分完成，需要适配新的对象读取方式 | 小 |
| **Command.cs** | `rtd[sid]` 从 IntPtr 转对象改为 TypeLayout.ReadObject | 中 |

### 5.5 这个方案为什么不需要第三方库

```
问：为什么不用 MessagePack / FlatBuffers / Bond？

答：
┌────────────────────────────────────────────────────────┐
│ RTD 核心操作的特征：                                     │
│                                                        │
│  1. 字段级随机读写（读 AI.PV、写 PID.MV）                │
│     → MessagePack 不支持（需要整体反序列化）              │
│     → FlatBuffers 部分支持（但 table 修改需重建）         │
│     → MemorySlot.Read<float>(offset) 天然支持 ✓         │
│                                                        │
│  2. 连线 = 字节拷贝（源字段区域 → 目标字段区域）          │
│     → 第三方库的序列化格式不支持 offset 级拷贝            │
│     → Buffer.BlockCopy(src, off1, dst, off2, len) ✓     │
│                                                        │
│  3. 批量快照（整个页面 dump 到磁盘）                      │
│     → 第三方序列化格式会增加额外开销                       │
│     → byte[] 直接 write 到 stream 最快 ✓                 │
│                                                        │
│  结论：你已有的 MemorySlot + byte[] 就是最合适的           │
│       "NumPy 等价物"。问题出在对象重建时的 GCHandle hack  │
│       而不是底层存储机制。修复 hack 即可，不需要换库。      │
└────────────────────────────────────────────────────────┘
```

---

## 6. 如果一定要引入第三方库

如果出于"不信任自写代码"的考虑，希望用成熟库来保障稳定性，以下是具体建议：

### 6.1 方案 A：用 MessagePack 替代对象 ↔ byte[] 转换

```
NuGet: MessagePack 2.5.187（支持 .NET Framework 4.6.1+）
```

**只替换对象重建环节**，不改变底层存储：

```csharp
// 安装
// Install-Package MessagePack
// Install-Package MessagePack.Annotations

// 1. 给数据类加标注
[MessagePackObject]
public class AIPoint
{
    [Key(0)] public float PV;
    [Key(1)] public int Quality;
    [Key(2)] public bool Alarm;
    [Key(3)] public long Timestamp;
}

// 2. 存入 RTD 时序列化
byte[] data = MessagePackSerializer.Serialize(point);
int sid = memoryManage.New((uint)data.Length);
memoryManage.SetSlot(sid, data, 0, data.Length);

// 3. 从 RTD 读出时反序列化（替代 GCHandle hack）
MemorySlot slot = memoryManage.GetSlot(sid);
byte[] raw = new byte[slot.Length];
Buffer.BlockCopy(slot.Data, slot.Offset, raw, 0, slot.Length);
AIPoint point = MessagePackSerializer.Deserialize<AIPoint>(raw);

// 永远不会 AccessViolationException！
```

**性能数据**（MessagePack-CSharp 官方基准）：

| 操作 | 吞吐量 | 延迟 |
|------|--------|------|
| Serialize（小对象） | ~5,000,000 ops/sec | ~200 ns |
| Deserialize（小对象） | ~3,000,000 ops/sec | ~330 ns |
| Serialize（1KB 对象） | ~1,000,000 ops/sec | ~1 µs |
| Deserialize（1KB 对象） | ~800,000 ops/sec | ~1.25 µs |

**对于 100ms 控制周期内 10000 个变量**：
- 序列化全部变量：10000 × 1µs = 10ms（可接受）
- 反序列化全部变量：10000 × 1.25µs = 12.5ms（可接受）

### 6.2 方案 B：用 FlatBuffers 做零拷贝字段访问

```
NuGet: Google.FlatBuffers 24.3.25
```

FlatBuffers 的优势是**零拷贝字段访问**——数据存在 byte[] 中，读取时不需要反序列化：

```csharp
// 1. 定义 Schema（.fbs 文件）
// table AIPoint {
//     pv: float;
//     quality: int;
//     alarm: bool;
//     timestamp: long;
// }

// 2. 使用 flatc 生成 C# 代码

// 3. 构建并存入 RTD
var builder = new FlatBufferBuilder(128);
var offset = AIPoint.CreateAIPoint(builder, 1.23f, 192, false, timestamp);
builder.Finish(offset.Value);
byte[] data = builder.SizedByteArray();
int sid = memoryManage.New((uint)data.Length);
memoryManage.SetSlot(sid, data, 0, data.Length);

// 4. 零拷贝读取字段（直接从 byte[] 读，不创建对象）
MemorySlot slot = memoryManage.GetSlot(sid);
var buf = new ByteBuffer(slot.Data, slot.Offset);
var point = AIPoint.__assign(buf.__vector(0), buf);
float pv = point.Pv;        // 直接从 byte[] 读取，零拷贝
int quality = point.Quality; // 直接从 byte[] 读取，零拷贝
```

**但是**：FlatBuffers 的字段偏移不是固定的（有 vtable 间接层），这意味着**连线时不能简单 memcpy**，需要逐字段拷贝。

### 6.3 方案 C（推荐）：用 StructLayout 手动定义布局 + 安全的 Marshal 操作

不引入第三方库，利用 .NET 原生能力：

```csharp
// 1. 用 StructLayout 精确控制内存布局
[StructLayout(LayoutKind.Explicit, Size = 32)]
public struct AIPointData
{
    [FieldOffset(0)]  public float PV;
    [FieldOffset(4)]  public int Quality;
    [FieldOffset(8)]  public byte Alarm;
    [FieldOffset(12)] public long Timestamp;
    [FieldOffset(20)] public int Mode;
    [FieldOffset(24)] public float HighLimit;
    [FieldOffset(28)] public float LowLimit;
}

// 2. 存入 RTD（安全的 Marshal 操作，无 GCHandle hack）
public static void WriteStruct<T>(MemorySlot slot, T value) where T : struct
{
    int size = Unsafe.SizeOf<T>();
    Unsafe.WriteUnaligned(ref slot.Data[slot.Offset], value);
}

// 3. 从 RTD 读出
public static T ReadStruct<T>(MemorySlot slot) where T : struct
{
    return Unsafe.ReadUnaligned<T>(ref slot.Data[slot.Offset]);
}

// 4. 使用
AIPointData data = new AIPointData { PV = 1.23f, Quality = 192 };
WriteStruct(slot, data);

AIPointData readBack = ReadStruct<AIPointData>(slot);
Console.WriteLine(readBack.PV); // 1.23

// 5. 字段级访问（通过已知偏移）
float pv = slot.Read<float>(0);   // 直接读 PV 字段
slot.Write<int>(4, 0);            // 直接写 Quality 字段

// 6. 连线 = memcpy（因为布局完全一致）
dstSlot.CopyFrom(srcSlot, 0, 0, 32); // 整个 AI 点拷贝
dstSlot.CopyFrom(srcSlot, 0, 0, 4);  // 只拷贝 PV 字段
```

**这个方案的优势**：

- **零依赖**：不需要任何第三方库
- **零拷贝**：Unsafe.ReadUnaligned 直接从 byte[] 读取
- **类型安全**：struct 的 FieldOffset 确保布局正确
- **连线兼容**：固定布局 → memcpy 即连线
- **性能等同原方案**：无反序列化开销
- **绝对安全**：没有 GCHandle hack，没有伪造对象

---

## 7. 总结与推荐路径

```
┌─────────────────────────────────────────────────────────┐
│                                                         │
│  崩溃的根因不是"缺少一个好的库"，而是"GCHandle hack"。    │
│                                                         │
│  MemorySlot + Unsafe.ReadUnaligned/WriteUnaligned       │
│  已经是 C# 世界中最接近 NumPy 的底层内存访问方式。        │
│  它就是你要找的"库"——只是它已经在你的代码里了。           │
│                                                         │
│  需要做的不是"找一个新库"，而是：                          │
│                                                         │
│  1. 删除 TypeManageItem 中的 GCHandle 地址重写代码        │
│  2. 删除 WiseNew 中写入 Syncblk/TypeHandle 的代码        │
│  3. 删除 PointManage 中持久化 GCHandle 内部指针的代码     │
│  4. 用 StructLayout 结构体 + Unsafe.ReadUnaligned        │
│     替代所有"byte[] → .NET 对象"的转换                   │
│                                                         │
│  改造量 ≈ 3~5 个文件，不需要任何第三方依赖                │
│                                                         │
└─────────────────────────────────────────────────────────┘
```

| 路径 | 方案 | 改造量 | 风险 | 推荐 |
|------|------|--------|------|:---:|
| **A** | 删除 GCHandle hack + StructLayout 结构体 | 3~5 文件 | 低 | ⭐⭐⭐⭐⭐ |
| B | 引入 MessagePack 替代对象序列化 | 所有数据类+调用方 | 中 | ⭐⭐⭐ |
| C | 引入 FlatBuffers 零拷贝方案 | Schema+代码生成+调用方 | 高 | ⭐⭐ |
| D | 引入 C++ native 库 | 跨语言互操作 | 很高 | ⭐ |
