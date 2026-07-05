# 新工况快照格式与 .wrk 迁移工具

> 状态：已实现并通过对账验证（2026-07-06）。
> 相关代码：`src/Runtime/RWVDCS.Runtime/DcsRuntime.cs`（快照）、
> `src/Legacy/RWVDCS.LegacyRunner`（桥接导出）、
> `src/Runtime/RWVDCS.Runtime/LegacyStateImporter.cs`（导入）、
> `scripts/migrate-wrk.ps1`（一键迁移）。

---

## 1. 新工况快照格式（RWVDCS.Next 原生）

一个工况 = 一个目录：

```
snap-xxx/
  manifest.json     清单：格式版本、保存时间、每 DPU 条目
                    （ControllerId / Name / File / CycleSeconds / CycleCount /
                      SchemaHash / PointCount / CommandCount）
  DPU1001.arena     每 DPU 一个 Arena 连续内存镜像（点 + 块状态一体）
  DPU1002.arena
  ...
```

设计要点：

- **镜像即快照**。运行时全部可变状态（点子字段 + 块内部字段）都活在每 DPU 的
  连续内存 Arena 里；保存 = `FlushBlockStates()` 后按段 memcpy 落盘，
  加载 = 就地覆写 Arena 内存。没有对象图序列化，没有反射。
- **SchemaHash 守卫**。工程结构（点表/块表/布局）变了则拒绝加载，避免错位覆写。
- **每 DPU 独立文件**。天然支持只迁移/回滚单个 DPU；也方便后续按 DPU 做
  Checkpoint + Journal 的历史时间轴（方案 §4.5）。

实测（VDCS(RuiWo) 全工程：50 DPU / 123,692 点 + 165,569 中间点 / 196,779 块命令）：

| 指标 | 老 .wrk（BinaryFormatter） | 新快照（Arena 镜像） |
| --- | --- | --- |
| 体积 | 139 MB .wrk + 19 MB .prj | ~90 MB（50 个 .arena，未压缩） |
| 保存 | 数十秒 | **~250 ms** |
| 加载 | ~50 s（含反序列化+重连） | **~15 ms**（就地覆写） |
| 往返一致性 | 有损（见 §3.3） | **按位一致**（save→load→dump SHA256 相同） |

> 压缩（Zstd）与周期性 Checkpoint/Journal 属于方案 §4.5 历史站范畴，后续里程碑接入；
> 当前格式已预留 manifest 版本号。

## 2. .wrk 迁移工具链

```
老 .wrk ──(1) LegacyRunner --load-wrk --export-state──▶ bridge.tsv
             （x86 进程内加载老 DCS，原生 LoadFile 语义）
bridge.tsv ──(2) Host --import-legacy --save──▶ 新快照目录
             （按名寻址应用到 Arena，另存新格式）
```

一键脚本（含可选 c0 对账验证）：

```powershell
scripts/migrate-wrk.ps1 `
    -Mdb "D:\...\VDCS(RuiWo)--260615.mdb" `
    -Wrk "D:\...\xxx.wrk" `
    -LegacyDir "D:\...\legacy-run" `      # stage-legacy.ps1 生成的老系统运行目录
    -RepoRoot "D:\...\RWVDCS重构" `
    -OutDir "D:\...\snap-out" `
    -Verify                               # 可选：迁移后老/新 c0 全点对账
```

### 2.1 桥接文件格式（bridge.tsv）

名字寻址的全量状态导出（UTF-8 无 BOM，TSV），不逆向 .wrk 二进制：

```
V	1                                     格式版本
D	DPU名	cycle秒	cycleCount            每 DPU 一行
P	DPU名	点名	LA|LD|LP|LP32	k=v;k=v;...   点的全部子字段
B	DPU名	块名	FC名	字段名	规格           块的全部状态字段
     规格 = PIN:k=v;...（LA/LD/LP/LP32 管脚，含 quality/isforced/buffer 等全子字段）
          | VAL:标量   | ARR:v1,v2,... | STR:URI转义 | NUL:
```

全工程桥接文件约 319 万行 / 352 MB（中间产物，迁移完可删；脚本默认放临时目录）。

## 3. 对账结论

### 3.1 迁移本身（c0）：按位一致

- 点：**289,261 / 289,261 一致，0 差异**（5,426 个跨 DPU 副本为老系统冗余存储，按规则排除）。
- 块字段：2,895,167 个全部应用（跳过 0，缺块 0）。
- 新快照往返（import → save → load → dump）：与导入后即刻 dump **SHA256 相同**。

### 3.2 迁移后续跑（+10 周期）：289,199 / 289,261 一致

与"老系统 LoadFile 同一 .wrk 后续跑 10 周期"逐点对比：

| 类别 | 数量 | 结论 |
| --- | --- | --- |
| 一致 | 289,199 | — |
| 非确定点（RAND/DATE 及下游） | 14 | 排除（两次老系统运行本身就不一致） |
| 逻辑位翻转 | 44（LA 44 + LD 4 为同一批点的两种视图） | **老系统 LoadFile 后连接性 bug 所致，新系统行为正确**（§3.4） |

### 3.3 .wrk 格式的固有状态丢失（迁移如实保留）

老系统保存 .wrk 时，块**私有字段**（如 `TIMER._timing/_prevX`、`COUNT.OLD_X`）
的最新值并不在 RTD 块内存里（只在托管 fc 对象里，仅初始化时写入过内存），
因此 .wrk 里存的是初始值。老系统自己加载 .wrk 后同样拿到丢失后的状态。

后果（新老完全一致，已用逐周期 trace 证实）：加载后第 1 周期，
TIMER 因 `_prevX=0、X=1` 误判上升沿而**重触发**（脉冲重新计时）、
COUNT 多计一次数等。这是 .wrk 格式的固有语义，迁移工具如实保留
（桥接导出的就是"老系统加载 .wrk 后"的真实状态）。
新格式快照不受此影响——块私有字段本来就在 Arena 里，往返无损。

### 3.4 44 处逻辑差异的归因：老系统 LoadFile 后连接性 bug

证据链（以 `1010$211$TIMER12`（TIMER，MODE=1 脉冲）为例）：

1. **两边块内部演化逐周期一致**：加载后 c1 重触发（§3.3），TRun 每周期 +0.2，
   c10 时 `TRun=2.0000002 ≥ TIME=2`，live 引脚 `OUT` 翻 0——老/新 trace 完全相同。
2. **老系统的点没跟上 live 引脚**：老系统续跑 c10~c12 dump 里
   `1010$211$TIMER12.OUT` 点值恒为 1，而 live OUT 已是 0——**点与引脚内部不一致**。
3. 原因：老系统 LoadFile 后，`RebuildPinSyncTables` 只能从反序列化出的 Wire
   对象重建同步表；凡是靠 PinDetails 直连（无 Wire 对象）的块
   （检视确认 TIMER12 无任何 joint/reference wire），其输入/输出点绑定**永久丢失**——
   输出点冻结在加载值，输入引脚不再从源点刷新。
4. 新系统装配自 mdb，连接完整：OUT 翻 0 正常传播到点，下游
   NOT/ALM/BXOF/HDI/DEVICE 链正常反应。44 处翻转全部位于这些下游链上
   （DPU1010/1011 的 ALM×16、NOT×16、TIMER12/BXOF89，DPU1016/1020/1068 的
   HDI/DEVICE/BTOL 及硬点 10HCB15AT007MD、10LAB35AA001VC、J0LBG80AA001*）。

结论：**不做 bug-for-bug 复刻**。新系统语义（加载状态 → 正常执行）是正确目标；
老系统这 44 处是其 LoadFile 连接性缺陷的表现（其 dump 甚至自相矛盾）。
该结论与"新系统 fresh 运行 c0/c10/c100 与老系统对账仅剩 x87/SSE 浮点噪声"相容。

## 4. 已知边界

- 迁移要求新老两侧使用**同一工程 mdb**（点表/块表一致；SchemaHash 校验兜底）。
- 桥接导出跳过引用型非状态字段（ICommand 等），与新系统 `BlockStateSchema` 的
  状态定义一一对应；对账中块字段跳过数为 0。
- 老系统 .wrk 中的跨 DPU 副本点（非属主 DPU 的远程点占位）不迁移，
  新系统按属主唯一存储（对账工具已按此分类）。
