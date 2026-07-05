# 07 wrk 二进制格式规格

> 来源：老系统源码精读（`D:\项目\睿渥\RWVDCS`），2026-07-06。
> 用途：`.wrk` 独立解析的参考规格。注意：实际迁移工具走「进程内加载 + 桥接导出」路线
> （`LegacyRunner --load-wrk --export-state`），**不做**二进制逆向；本规格供排查与备用。
> 编码约定：`BinaryWriter`/`BinaryReader`，**小端序**；`string` = **7-bit 变长长度前缀 + UTF-8 字节**
> （.NET `ReadString`/`Write(string)` 标准格式）；`bool` = **1 字节**（0/非 0）。

## 摘要

`.wrk` **自包含**每个 RTD 的点名、类型 ID、memSID 及页内原始字节；全局类型名表仅在 **Master RTD** 段。Slave DPU 段无 TypeManage，解析时需复用 Master 的 `typeID→类型名` 映射。`Point`/`Block` 由 `PointManageItem.varClass` 区分。Block 为引用型伪对象：前 8 字节为 syncblk+TypeHandle，值类型字段从偏移 8 起可读；引用字段为页内绝对地址，需追踪嵌套对象才能完整恢复 pin/buffer 状态。

> **已实证的限制**：老系统保存 .wrk 时，块**私有字段**（`_timing`/`_prevX`/`OLD_X` 等）
> 的最新值只在托管 fc 对象里、未回写 RTD 块内存，因此 .wrk 内是初始值——
> 老系统自己加载后同样丢失。见 `docs/工况快照与wrk迁移.md` §3.3。

---

## 1. .wrk 文件总布局

```
┌─────────────────────────────────────────────────────────────┐
│ [A] 文件头：EditionString + cycTime + cycCount              │
├─────────────────────────────────────────────────────────────┤
│ [B] Master RTD 块（含 TypeManage）                          │
├─────────────────────────────────────────────────────────────┤
│ [C] int32 DPU 数量                                          │
├─────────────────────────────────────────────────────────────┤
│ [D] 重复 DPU 数量 次：                                       │
│     string dpuname                                          │
│     bool hasData                                            │
│     if hasData: Slave RTD 块（无 TypeManage）               │
└─────────────────────────────────────────────────────────────┘
```

配对 `.prj` 与 `.wrk` **同名不同扩展名**，同目录；Load 时两文件 DPU 段必须同步推进。

---

## 2. [A] .wrk 文件头

| 序 | 字段 | 类型 | 字节 | 含义 | 出处 |
|----|------|------|------|------|------|
| 1 | edition | string | 变长 | 固定 `"Format of VDCS3.0"` | `Dcs.cs:881,1687-1688,2003` |
| 2 | cycTime | double | 8 | DCS 周期（秒） | `Dcs.cs:1696,2004` |
| 3 | cycCount | int64 | 8 | DCS 周期计数 | `Dcs.cs:1698,2005` |

---

## 3. RTD 块（Master 与 Slave 共用头，Master 多 TypeManage 段）

写入顺序：`RTD.Save` / `RTD.Load`（`RTD.cs:1120-1156`, `1039-1095`）

### 3.1 RTD 头

| 序 | 字段 | 类型 | 字节 | 含义 | 出处 |
|----|------|------|------|------|------|
| 1 | name | string | 变长 | RTD 名称 | `RTD.cs:1127,1046` |
| 2 | mode | int32 | 4 | `RTDWorkMode`：0=None,1=**Master**,2=Slave | `RTD.cs:1128,1047` |
| 3 | enableWirterTrigger | bool | 1 | 写触发开关 | `RTD.cs:1129,1048` |
| 4 | allocSID | int32 | 4 | SID 分配基址 | `RTD.cs:1130,1049` |
| 5 | preAllocNum | uint32 | 4 | 预分配数量 | `RTD.cs:1131,1050` |
| 6 | metaModified | bool | 1 | 元数据是否变更 | `RTD.cs:1132,1051` |
| 7 | plugTimeMarkCount | int32 | 4 | 插件时间戳数组长度 | `RTD.cs:1133,1052` |
| 8 | plugTimeMarks[] | int64 × N | 8×N | 各插件 DLL 创建时间 Tick | `RTD.cs:1134-1135,1053-1068` |

### 3.2 TypeManage 段（**仅 mode==Master**）

Slave RTD **不写/不读**此段（`RTD.cs:1137-1144,1070-1077`）。

| 序 | 字段 | 类型 | 字节 | 含义 | 出处 |
|----|------|------|------|------|------|
| 1 | typeCount | int32 | 4 | 类型表项数 | `TypeManage.cs:1571,1477` |
| 2.. | pairs[] | 重复 typeCount 次 | | | `TypeManage.cs:1572-1575,1482-1484` |
| 2a | typeFullName | string | 变长 | 类型全名，如 `DCSType.LD` | 同上 |
| 2b | typeIndex | int32 | 4 | **PointManageItem.typeID 使用的索引** | 同上 |

**存疑**：Load 时用 `dict.Keys` 顺序重新 `ParseType`，未直接使用保存的 `typeIndex` 重排（`TypeManage.cs:1512-1534`）。独立解析器应**直接采用文件中 (name, typeIndex) 对** 建立 `typeNames[id]`，勿依赖插件反射顺序。

### 3.3 PointManage 段

Save/Load 顺序（`PointManage.cs:2412-2510,2257-2379`）：

```
MemoryManage → nameList → nameTable → aliasTable → links → bulk(itemList 等)
```

#### 3.3.1 MemoryManage 子段（见第 4 节）

#### 3.3.2 nameList

| 序 | 字段 | 类型 | 字节 | 含义 | 出处 |
|----|------|------|------|------|------|
| 1 | count | int32 | 4 | | `PointManage.cs:2424,2272` |
| 2.. | names[] | string × count | 变长 | 点名池；`nameID` 为下标 | `2425-2426,2273-2276` |

#### 3.3.3 nameTable

| 序 | 字段 | 类型 | 字节 | 含义 | 出处 |
|----|------|------|------|------|------|
| 1 | count | int32 | 4 | | `2430,2279` |
| 2.. | entries[] | × count | | | `2431-2433,2281-2283` |
| 2a | pointName | string | 变长 | 点名 | 同上 |
| 2b | sid | int32 | 4 | **itemList 数组下标**（非 memSID） | `578,2282` |

#### 3.3.4 aliasTable

| 序 | 字段 | 类型 | 字节 | 含义 | 出处 |
|----|------|------|------|------|------|
| 1 | count | int32 | 4 | | `2437,2286` |
| 2.. | entries[] | × count | | 别名→FSID | `2438-2441,2287-2290` |
| 2a | alias | string | 变长 | | |
| 2b | fsid | int64 | 8 | 外部 FSID | |

#### 3.3.5 links（信号线连接）

| 序 | 字段 | 类型 | 字节 | 含义 | 出处 |
|----|------|------|------|------|------|
| 1 | linkGroupCount | int32 | 4 | 字典项数 | `2445,2293` |
| 2.. | groups[] | × count | | key = Point/Pin 的 sid | `2446-2456,2294-2307` |
| 2a | sid | int32 | 4 | itemList 下标 | |
| 2b | linkCount | int32 | 4 | 该 sid 的 Link 数 | |
| 2c | links[] | Link × linkCount | 12×N | 见 Link 结构 | |

**Link 结构**（`DataType.cs:180-203`, Pack=1, 12 字节）：

| 偏移 | 字段 | 类型 | 字节 |
|------|------|------|------|
| 0 | sourceoffset | uint32 | 4 |
| 4 | targetsid | int32 | 4 |
| 8 | targetoffset | uint32 | 4 |

#### 3.3.6 bulk 块（reclaim + resource + itemList）

| 序 | 字段 | 类型 | 字节 | 含义 | 出处 |
|----|------|------|------|------|------|
| 1 | countReclaimIndex | int32 | 4 | | `2459,2309` |
| 2 | countReclaimName | int32 | 4 | | `2460,2310` |
| 3 | countResourceList | int32 | 4 | | `2461,2311` |
| 4 | countItemList | int32 | 4 | | `2462,2312` |
| 5 | bulkData | byte[] | 见下 | 四段连续拼接 | `2506,2323-2325` |

**bulkData 内部布局**（顺序固定）：

```
[reclaimIndexList: int32 × countReclaimIndex]      // 4×N1
[reclaimNameList:  int32 × countReclaimName]       // 4×N2
[resourceList: AllocSIDResource × countResourceList] // 8×N3
[itemList: PointManageItem × countItemList]        // 36×N4
```

**AllocSIDResource**（`DCSCommon\DataType.cs:159-178`, Pack=1, **8 字节**）：

| 偏移 | 字段 | 类型 | 字节 |
|------|------|------|------|
| 0 | baseSID | int32 | 4 |
| 4 | preAllocNum | uint32 | 4 |

**PointManageItem**（`RTD\DataType.cs:62-108`, Sequential Pack=1, **36 字节**）：

| 偏移 | 字段 | 类型 | 字节 | 含义 |
|------|------|------|------|------|
| 0 | memSID | int32 | 4 | MemoryManage.VariableList 下标 |
| 4 | typeID | int32 | 4 | TypeManage 类型索引 |
| 8 | length | uint32 | 4 | 变量字节长度 |
| 12 | refcount | int32 | 4 | 引用计数 |
| 16 | nameID | int32 | 4 | nameList 下标；-1 无效 |
| 20 | varClass | int32 | 4 | `VariableClass` 枚举 |
| 24 | varState | int32 | 4 | 0=Normal, 1=Dirty |
| 28 | protectedWord | int32 | 4 | 0=Private, 1=Public |
| 32 | definedType | int32 | 4 | 0=User, 1=Rent |

**VariableClass**（`DCSCommon\Enum.cs:331-353`）：0=Point, 1=Tag, 2=**Block**, 3=Macro, 4=Basic。

**点名恢复**：`name = nameList[item.nameID]`（`PointManage.cs:729`），或通过 `nameTable` 反查 sid。

### 3.4 SubscribeManage 段（RTD 最后）

| 序 | 字段 | 类型 | 字节 | 含义 | 出处 |
|----|------|------|------|------|------|
| 1 | pairCount | int32 | 4 | inner→outer FSID 映射数；0 表示空 | `SubscribeManage.cs:193-200,131` |
| 2.. | pairs[] | × pairCount | | | `196-197,137-139` |
| 2a | innerFSID | int64 | 8 | 内部 FSID | |
| 2b | outerFSID | int64 | 8 | 外部 FSID | |

---

## 4. MemoryManage 子段

`MemoryManage.cs:473-537`（Load），`540-628`（Save）

### 4.1 头字段

| 序 | 字段 | 类型 | 字节 | 含义 | 出处 |
|----|------|------|------|------|------|
| 1 | recyclingAtSerializing | bool | 1 | 序列化前是否 GC | `562,491` |
| 2 | acceptableUtilizationRatio | double | 8 | 页利用率阈值 | `563,492` |
| 3 | autoCollationSpan | uint32 | 4 | 自动整理间隔 | `564,493` |
| 4 | firstVariableIndex | int32 | 4 | 变量链表首索引 | `565,494` |
| 5 | lastVariableIndex | int32 | 4 | 变量链表尾索引 | `566,495` |
| 6 | memUseLength | int64 | 8 | 页堆有效字节总长 | `567,496` |
| 7 | pageLength | int32 | 4 | 页大小配置 | `568,497` |
| 8 | countReclaimList | int32 | 4 | ReclaimVariableList 长度 | `569,498` |
| 9 | countVariableList | int32 | 4 | VariableList 长度 | `570,499` |

若 `memUseLength <= 0`，Load 提前返回，**无 bulk**（`501,505-507`）。

### 4.2 bulkData

```
length = countReclaimList×4 + countVariableList×32 + memUseLength
```

| 段 | 内容 | 字节 |
|----|------|------|
| 1 | reclaimVariableList | int32 × countReclaimList |
| 2 | variableList | VariableListItem × countVariableList |
| 3 | pageHeap | memUseLength 字节（所有变量数据） |

**VariableListItem**（`MemoryManage.cs:34-51`, Explicit, **32 字节**）：

| FieldOffset | 字段 | 类型 | 字节 |
|-------------|------|------|------|
| 0 | pageIndex | int32 | 4 |
| 4 | variableLength | uint32 | 4 |
| 8 | variableOffset | int64 | 8 |
| 16 | prevVariableIndex | int32 | 4 |
| 20 | nextVariableIndex | int32 | 4 |
| 24 | state | int32 | 4（VariableState） |
| 28 | wrAbility | int32 | 4（WriteReadAbility） |

Load 后将段 3 拷入单页 `pageItem.data`（`531`）。Save 时若 `recyclingAtSerializing==false`，段 3 由 SavePageList 链拼接（`602-619`，**存疑**：独立解析器按 Load 路径读连续 memUseLength 即可）。

### 4.3 用 memSID 定位字节

```
pageHeap = bulkData 最后 memUseLength 字节
vitem = variableList[memSID]
rawBytes = pageHeap[vitem.variableOffset : vitem.variableOffset + vitem.variableLength]
```

出处：`MemoryManage.cs:650-663`, `738-753`。

---

## 5. [C][D] DPU 段（.wrk）

| 序 | 字段 | 类型 | 字节 | 含义 | 出处 |
|----|------|------|------|------|------|
| 1 | dpuCount | int32 | 4 | | `Dcs.cs:2015,1757` |
| 2.. | per DPU | × dpuCount | | **Dictionary 迭代顺序，存疑** | `2018-2043,1759-1799` |
| 2a | dpuname | string | 变长 | DPU 名 | |
| 2b | hasData | bool | 1 | false=空 DPU | `2028-2035,1763-1764` |
| 2c | slave RTD | RTD 块 | | hasData 时；**无 TypeManage** | `Dpu.cs:1667-1668,1706-1707` |

DPU 周期/计数在 **.prj** 头，不在 .wrk。

---

## 6. .prj 文件格式简表

### 6.1 主段

| 序 | 字段 | 类型 | 出处 |
|----|------|------|------|
| 1 | EditionString | string | `Dcs.cs:2016,1737` |
| 2 | dpuCount | int32 | `2017,1758` |
| 3.. | 与 .wrk 同步 per DPU | | |
| 3a | dpuname | string | |
| 3b | hasData | bool | |
| 3c | version | string | `Dpu.cs:1655,1698` |
| 3d | cycle | uint32（毫秒） | `1656,1699` |
| 3e | cycleCount | uint32 | `1657,1700` |
| 3f | controllerID | int32 | `1658,1701` |
| 3g | dbPath | string | `1659,1702` |
| 3h | prjPath | string | `1660,1703` |
| 3i | CommandCollection | 见 6.2 | `Operation.cs:916-1003` |

### 6.2 CommandCollection（Operation.cs:916-1003）

| 序 | 字段 | 类型 |
|----|------|------|
| 1 | cmdCount | int32 |
| 2.. | per cmd | |
| 2a | cmdName | string（空+空+-1 表示 null） |
| 2b | fcName | string |
| 2c | sid | int32 |
| 2d | wireCount | int32（Reference+Joint 总数） |
| 2e.. | wires × wireCount | |
| | pointName | string |
| | pinName | string |
| | reversed | bool |
| | attribute | int32（WireAttributes） |
| | type | int32（WireTypes） |
| | pointAddr | int32 sid + uint32 offset + uint16 length（**10B**） |
| | pinAddr | Reference: +uint32 parentOffset（**14B**）；Joint: 无 parentOffset（**10B**） |

### 6.3 DocVersion 尾段（可选）

`Dcs.cs:1805-1835,2045-2059`

| 序 | 字段 | 类型 |
|----|------|------|
| 1 | dpuCount | int32（须等于前面 DPU 数） |
| 2.. | per DPU | |
| 2a | dpuname | string |
| 2b | docCount | int32 |
| 2c.. | docName→version | string, string |

---

## 7. 端到端伪代码：点名 → 类型 → 字节 → 值

```python
# 1. 解析 .wrk
assert read_string() == "Format of VDCS3.0"
cyc_time = read_f64()
cyc_count = read_i64()

# 2. Master RTD
master = parse_rtd(expect_typemanage=True)
type_names = master.typemanage         # dict: typeID -> fullName

dpu_count = read_i32()
for _ in range(dpu_count):
    dpu_name = read_string()
    if not read_bool(): continue
    rtd = parse_rtd(expect_typemanage=False)   # 复用 type_names

# --- parse_rtd 内部 ---
def parse_rtd(expect_typemanage):
    read_rtd_header()
    if expect_typemanage:
        type_map = read_typemanage_pairs()   # [(name, id), ...]
    mm = read_memory_manage()                # -> pageHeap, variableList
    pm = read_pointmanage_after_mm(mm)
    read_subscribe_manage()
    return {type_map, mm, pm}

# 3. 提取所有点
def extract_points(rtd, type_names):
    results = []
    for name, sid in rtd.pm.name_table.items():
        item = rtd.pm.item_list[sid]
        if item.var_state == DIRTY: continue
        v = rtd.mm.variable_list[item.mem_sid]
        raw = rtd.mm.page_heap[v.offset : v.offset + v.length]
        results.append({
            "name": name,
            "type": type_names[item.type_id],
            "var_class": item.var_class,   # 0=Point, 2=Block
            "bytes": raw,
        })
    return results

# 4. 解码值类型 Point（无对象头）
def decode_ld(raw):   # Pack=1
    return {
        "quality":    read_i32(raw, 0),
        "istrace":    raw[4] != 0,
        "isalarm":    raw[5] != 0,
        "connected":  raw[6] != 0,
        "forcevalue": raw[7] != 0,
        "isforced":   raw[8],
        "buffer":     raw[9] != 0,
    }

# 5. 解码 Block（引用型伪对象）
def decode_block_field(raw, field_offset_in_object):
    # 对象头 8 字节；类字段 Offset = CLR字段偏移 + 8
    FIELD_DATA_START = 8
    return raw[FIELD_DATA_START + field_offset_in_object : ...]
```

---

## 8. LD / LA / LP / LP32 字段偏移表（Pack=1, bool=1B）

### LD（10 字节，`DCSType\LD.cs`）

| 偏移 | 字段 | 类型 | 字节 |
|------|------|------|------|
| 0 | quality | int32 (QualityTypes) | 4 |
| 4 | istrace | bool | 1 |
| 5 | isalarm | bool | 1 |
| 6 | isConnected | bool | 1 |
| 7 | forcevalue | bool | 1 |
| 8 | isforced | byte | 1 |
| 9 | buffer | bool | 1 |

### LA（28 字节，`DCSType\LA.cs`）

| 偏移 | 字段 | 类型 | 字节 |
|------|------|------|------|
| 0 | quality | int32 | 4 |
| 4 | istrace | bool | 1 |
| 5 | isalarm | bool | 1 |
| 6 | forcevalue | float | 4 |
| 10 | isforced | byte | 1 |
| 11 | maxreached | bool | 1 |
| 12 | minreached | bool | 1 |
| 13 | ishighalarm | bool | 1 |
| 14 | islowalarm | bool | 1 |
| 15 | isConnected | bool | 1 |
| 16 | maxvalue | float | 4 |
| 20 | minvalue | float | 4 |
| 24 | buffer | float | 4 |

### LP（12 字节）

| 偏移 | 字段 | 类型 | 字节 |
|------|------|------|------|
| 0 | quality | int32 | 4 |
| 4 | istrace | bool | 1 |
| 5 | isalarm | bool | 1 |
| 6 | isConnected | bool | 1 |
| 7 | forcevalue | uint16 | 2 |
| 9 | isforced | byte | 1 |
| 10 | buffer | uint16 | 2 |

### LP32（16 字节）

| 偏移 | 字段 | 类型 | 字节 |
|------|------|------|------|
| 0 | quality | int32 | 4 |
| 4 | istrace | bool | 1 |
| 5 | isalarm | bool | 1 |
| 6 | isConnected | bool | 1 |
| 7 | forcevalue | uint32 | 4 |
| 11 | isforced | byte | 1 |
| 12 | buffer | uint32 | 4 |

QualityTypes：0=Good,1=Bad,2=Fair,3=NotGood（`DCSCommon\Enum.cs:435-441`）。

---

## 9. Block 伪对象头与字段区

引用型（class FC/Block）在页堆中的布局（`TypeManage.cs:830-846,471-474`）：

| 偏移 | 内容 | 字节 | 说明 |
|------|------|------|------|
| 0 | syncblk | int32 | 固定写 0 |
| 4 | TypeHandle | int32 | **CLR 运行时相关**，跨进程/版本不可移植 |
| 8+ | 字段区 | | 值类型字段：`Offset = Marshal字段偏移 + 8` |

- **值类型 Point**（LD/LA/LP 等）：**无** 8 字节头，raw 即 struct 本体（`TypeManage.cs:840-841`）。
- **引用型字段**：在 `Offset` 处存 **4 字节绝对地址**（指向页堆内嵌套对象 syncblk 前 4 字节处）；嵌套对象同样 8 字节头 + 字段（`944-958,1098-1117`）。
- Load 时 `WiseNew(..., writeDefaults:false)` **不覆盖** 已保存的值类型字段，仅重建头与指针（`PointManage.cs:2362-2376`）。

**Block 状态恢复结论**：
- 值类型 pin/buffer：**可以**按 TypeManage 元数据（或硬编码 FC 布局）从字节偏移解码。
- 引用型 pin 的嵌套 struct：**可以**通过绝对地址在 pageHeap 内追踪（地址 = 页基址 + 相对偏移，Load 后页基址固定）。
- TypeHandle、syncblk：**不可**跨 CLR 版本移植；解析器应跳过前 8 字节，依赖静态字段布局表。

---

## 10. 存疑项汇总

| 项 | 说明 |
|----|------|
| TypeManage Load 顺序 | 保存的 typeIndex 未被用于重排，依赖 ParseType 重建相同 ID |
| DPU / Dictionary 顺序 | `m_dpuList` 迭代顺序未定义，但 Save/Load 同一进程一致 |
| SavePageList 链 | Save 时非 recycling 路径页数据经链拼接；Load 读连续 memUseLength |
| Block 跨版本 | TypeHandle 绑定 CLR 内部值，独立解析器不应依赖 |
| 插件类型元数据 | Block 字段 Offset 来自 `FieldHandle`+反射，独立解析器需外挂 FC 布局表 |
