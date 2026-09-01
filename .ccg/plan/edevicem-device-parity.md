# 实施计划：EDEVICEM 对照 DEVICE 的功能完善方案

## 任务类型

- [x] 后端功能码
- [x] HMI/XTP 交互
- [x] 说明文档
- [ ] 通用前端

## 分析范围

EDEVICEM：

- `D:\项目\睿渥\算法块说明书_md\EDEVICEM.md`
- `src/Blocks/RWVDCS.Blocks/RW/FC_EDEVICEM.cs`
- `src/Blocks/RWVDCS.Blocks/RW/FC_EDEVICEM_RUN.cs`
- `D:\项目\睿渥\template\style\Popup_TPRI\电气开关.xtp`

参考基准 DEVICE：

- `D:\项目\睿渥\算法块说明书_md\DEVICE.md`
- `src/Blocks/RWVDCS.Blocks/RW/FC_DEVICE.cs`
- `src/Blocks/RWVDCS.Blocks/RW/FC_DEVICE_RUN.cs`

工程契约核对：

- `D:\项目\睿渥\VDCS(RuiWo)--260615.mdb`
- `Cld_FCBlock.FunctionName='EDEVICEM'`
- `Cld_FCInput`、`Cld_FCOutput`、`Cld_FCParameter`

## 结论摘要

当前 EDEVICEM 不是只需要细节优化，而是存在三类结构性问题：

1. **HMI 管脚契约断裂**：XTP 和实际 MDB 写/配置 `CON/COF/CTA/CTM/CAK/CFB/CRS/CDB`，但
   `FC_EDEVICEM.cs` 没有这些 Input 字段，只定义了无法由画面路径写到的 `hmiCmd*` Internal bool。
2. **32 位状态打包类型错误**：EDEVICEM 用 `LA/float TAG` 承载 Bit0～Bit31，float 只有约 24 位整数精度，
   Bit31 与低位组合时会丢位；DEVICE 已使用 `LP32 TAG`。
3. **状态机不完整**：持续自动请求会重复触发已到位方向，ResetM=2 可能使输出失去计时清零路径，
   Loc 的 EnLoc 参数没有在全部路径生效，派生状态在超时/反馈变化后滞后一周期，画面按钮禁用表达式与
   EDEVICEM 位定义不匹配。

推荐采用“EDEVICEM 专用双向电气开关状态机”方案：吸收 DEVICE 已验证的 HMI 上升沿、命令激活、反馈完成、
超时、派生状态后置计算和 PACK 组装模式，但保留 EDEVICEM 自身无 Stop、无保护开/关、无 OutM、无 TripM 的
业务边界。

## 已确认的工程数据库契约

实际 MDB 中 EDEVICEM 的输入管脚为：

```text
Enable, EnOn, EnOff, ToM, ReqA, Aon, AOff,
FBOn, FBOff, Loc, FBat, FDev, POpe, FSpr,
CON, COF, CTA, CTM, CAK, CFB, CRS, CDB
```

输出管脚：

```text
On, Off, MA, NoCon, FBFl, Trip, OpFl, Forbid,
OpFlOn, OpFlOff, TAG
```

参数：

```text
ResetM, SetT, FLB, Tover,
EnLoc, EnFBat, EnFDev, EnFSpr,
MP, QualityT
```

当前 C# 定义缺失：

```text
CON, COF, CTA, CTM, CAK, CFB, CRS, CDB, QualityT
```

当前源码中的 `// ...保持原有定义不变` 只是注释，实际字段并不存在，RuntimeBuilder 不会为这些 MDB 管脚建立
有效输入绑定。

## XTP 实际交互契约

`电气开关.xtp` 使用 GB18030 编码。实际按钮写入：

| 按钮 | 写入路径 | 类型 |
| --- | --- | --- |
| 合闸 | `DPU.Device@CON.Value` | 约 500 ms 脉冲 |
| 分闸 | `DPU.Device@COF.Value` | 约 500 ms 脉冲 |
| 挂牌 | `DPU.Device@CFB.Value` | 约 500 ms 脉冲，功能码内翻转 |
| 复位 | `DPU.Device@CRS.Value` + `DPU.Device@CAK.Value` | 两路约 500 ms 脉冲 |

主要显示位：

```text
Bit10 已合
Bit11 已分
Bit14 控制电源失去
Bit15 设备故障
Bit20 调试
Bit21 反馈异常
Bit22 跳闸
Bit23 操作失败
Bit26 手动挂牌/禁操
Bit29 正在合闸
Bit30 正在分闸
```

当前按钮禁用表达式：

```text
合闸：(BIT30) || (BIT1) || (BIT26)
分闸：(BIT29) || (BIT0) || (BIT26)
```

而 EDEVICEM 文档中 Bit0=ToM、Bit1=ReqA；这两个表达式更像按 DEVICE 的 Bit0/Bit1 保护指令映射遗留，
与 EDEVICEM 的允许、综合禁操、弹簧储能和目标反馈状态不一致。

## 不应从 DEVICE 复制的能力

以下为 DEVICE 特有能力，EDEVICEM 文档和实际 MDB 均无对应管脚/参数，不应为了“代码一致”强行引入：

- POn / POff 保护开关指令；
- EnStp / AStp / FBStp / Stp；
- 中停和 `middleStopActive`；
- OpFlStp；
- Totp / TRBL 独立输出；
- OutM 长信号模式；
- OutPri 三方向优先级；
- StopR；
- TripM 多模式跳闸判据。

EDEVICEM 应保持“合闸/分闸两方向、定长控制脉冲、断路器专用状态”的定位。

## 方案对比

### 方案 A：只把 hmiCmd 改名

把 `hmiCmdOn` 等重命名为 CON/COF 等，但继续使用 Internal bool 和周期末自清零。

缺点：bool 不实现 IValuable，现有 InputBinding 无法像 LD 管脚一样从工程点同步；500 ms 外部脉冲在多个扫描周期内
会被反复重新写入；无法正确实现 Toggle 上升沿。

结论：不采用。

### 方案 B：直接复制 DEVICE

优点：快速获得成熟状态机。

缺点：引入 EDEVICEM 不存在的保护、中停、Stop 和模式参数，改变 MDB 契约和 HMI 位定义。

结论：不采用。

### 方案 C：按 DEVICE 模式重构 EDEVICEM 专用状态机（推荐）

只复用状态机组织方式和已验证规则：真实 HMI Input、上升沿、命令 helper、反馈完成、超时、派生状态后置、
PACK 互斥显示；保留 EDEVICEM 自身管脚和断路器语义。

结论：采用。

## 修改方案一：修复功能码字段契约（最高优先级）

### 1. 增加实际 HMI Input 管脚

在 `FC_EDEVICEM.cs` 增加八个 `LD` Input：

```csharp
CON // 合闸脉冲
COF // 分闸脉冲
CTA // 投自动脉冲
CTM // 切手动脉冲
CAK // 故障确认脉冲
CFB // 禁操翻转脉冲
CRS // 复位脉冲
CDB // 调试翻转脉冲
```

字段名和大小写以实际 MDB/XTP 为准。删除 `hmiCmdOn/...` 八个无工程绑定的 Internal 字段。

### 2. 增加 HMI 历史沿状态

增加：

```text
oldCON, oldCOF, oldCTA, oldCTM,
oldCAK, oldCFB, oldCRS, oldCDB
```

每周期计算：

```csharp
edgeCON = CON && !oldCON;
...
```

周期末仅更新 old 值，不修改输入管脚自身。这样 XTP 的 500 ms 脉冲无论跨越几个扫描周期，都只消费一次。

### 3. 增加 QualityT 参数

实际 MDB 和说明文档均有 `QualityT`，当前 C# 类型缺失，需定义：

```csharp
[PinType(PinTypes.Constant)]
public uint QualityT = 0;
```

### 4. TAG 改为 LP32

将：

```csharp
public LA TAG
```

改为：

```csharp
public LP32 TAG = new();
```

并使用：

```csharp
TAG.Value = packStatus;
```

禁止 `uint → float`，否则 Bit24 以上与低位组合时可能丢失状态位。

### 5. 删除无效占位注释

删除“CONPAGE/CON 等保持原定义不变”的占位注释。实际 MDB 没有 PAGE/源字符串参数，只需实现真实存在的 Input。

## 修改方案二：重构 HMI 指令处理

### 1. Toggle 指令

```text
edgeCFB：manualForbid 翻转
edgeCDB：debugMode 翻转
```

必须按上升沿翻转，不能对高电平每周期翻转。

### 2. Ack 与 Reset

```text
edgeCAK：清除 Trip、OpFlOn、OpFlOff
edgeCRS：执行 Ack 的清除，并取消 On/Off 活动、清空全部定时器、释放 On/Off 输出
```

XTP 当前复位按钮同时发 CRS 和 CAK，因此处理必须幂等。

EDEVICEM 说明书只说 Forbid 禁止输出，没有说禁止确认；XTP 的复位按钮也没有按 Bit26 禁用。建议保留
“挂牌时仍允许 Ack/Reset”，不要照搬 DEVICE 中 `!manualForbid` 的限制，除非现场另行确认。

### 3. 模式切换

优先级：

```text
ToM=true       → 手动
否则 ReqA=true → 自动
否则 edgeCTM   → 手动
否则 edgeCTA   → 自动
```

Loc 是否阻止模式切换应使用 `locForbid = EnLoc && Loc`，而不是无条件使用原始 Loc；否则 EnLoc=false 仍无法操作，
与参数说明冲突。

### 4. MP 语义

```text
MP=0：手动合/分命令不切手动，但命令有效
MP=1：手动合/分命令切手动且有效
MP=2：手动合/分命令不切手动且无效
```

仅对 `edgeCON/edgeCOF` 生效。

## 修改方案三：统一安全闭锁和命令仲裁

### 1. 有效禁操条件

```csharp
locForbid  = EnLoc  && Loc;
fbatForbid = EnFBat && FBat;
fdevForbid = EnFDev && FDev;
forbid = manualForbid || locForbid || fbatForbid || fdevForbid;
```

所有模式切换、手动命令和自动命令统一使用 `forbid/NoCon`，不再额外无条件判断原始 Loc。

### 2. 禁操出现时的活动命令

按照文档“禁止一切输出”：

- Forbid 变为 true 时立即释放 On/Off 物理输出；
- 保留已发起行程的 `onCmdActive/offCmdActive` 监视，继续等待反馈或 Tover，防止随后反馈变化被误判 Trip；
- 禁止启动任何新操作；
- CRS 可显式取消活动监视和定时器。

这一规则比直接清除 cmdActive 更安全，因为控制脉冲虽然停止，机械设备可能已经开始动作。

### 3. 弹簧未储能

`EnFSpr && FSpr` 只闭锁合闸，不影响分闸；PACK Bit6 保留原始状态。

### 4. 目标反馈已满足时不重复动作

启动前增加：

```text
合闸：FBOn=false 才允许 StartOn
分闸：FBOff=false 才允许 StartOff
```

避免持续 AOn/AOff 或重复点击在设备已到位时反复输出控制脉冲。

故障 ACK 后，如果自动请求仍保持且目标反馈未到位，应允许重新自动操作，符合说明书的自动续动语义。

### 5. 同时合/分请求

EDEVICEM 没有 OutPri。建议采用电气安全侧固定优先级：

```text
分闸优先于合闸
```

当 AOn/AOff 或 CON/COF 同周期同时有效时，只接受分闸。已有行程活动时不反向重入，需等反馈、超时、Forbid
或 CRS 结束当前行程。

## 修改方案四：拆分“输出脉冲”和“行程监视”

当前 `onCmdActive/offCmdActive` 同时承担输出计时和反馈监视。反馈提前到位且 ResetM=2 时，cmdActive 被清除，
对应输出可能失去后续 SetT 清零路径。

推荐拆分：

```text
onPulseActive / offPulseActive：控制输出脉冲长度 SetT
onCmdActive / offCmdActive：行程反馈和 Tover 监视
```

规则：

1. StartOn/StartOff 将 pulseActive 和 cmdActive 同时置位；
2. 脉冲计时独立于反馈监视，达到 SetT 后无条件释放 On/Off；
3. 反馈到位立即结束 cmdActive；
4. ResetM=0/1 时反馈可提前释放输出；
5. ResetM=2 时反馈不提前释放，但输出仍必须在 SetT 到期后释放，避免控制线圈永久得电；
6. Tover 超时置 OpFl；ResetM=0 可释放输出，其他模式仍受 SetT 的硬脉宽上限；
7. `effectiveTover = max(0, Tover, SetT)`；
8. SetT 小于一个扫描周期时，仍保证触发周期有一个可见输出周期。

On 与 Off 永远互斥，任何 helper 启动一个方向时必须释放另一个方向的 pulse/状态。

## 修改方案五：反馈、Trip 和派生状态

### 1. 反馈完成

```text
FBOn=true  → 完成合闸行程、清除 OpFlOn
FBOff=true → 完成分闸行程、清除 OpFlOff
```

目标反馈已满足时不创建新的行程状态。

### 2. FBFl

保留当前定义：

```text
FBOn && FBOff → FBFl=true
```

说明书的 NoCon 明确未包含 FBFl，因此默认只报警显示，不自动加入闭锁；如果现场要求反馈矛盾禁操，应作为单独
需求确认，不能静默改变。

### 3. Trip

保留 EDEVICEM 断路器特有判据：

```text
没有授权分闸行程
且不处于有效就地禁操
且 FBOn 由 1 变 0
→ Trip=true
```

使用 `offCmdActive` 作为“已授权分闸”的监视窗口。Forbid 只释放输出但不立刻清除 offCmdActive，可避免已发脉冲后的
正常反馈变化被误判为跳闸。

### 4. 派生状态后置计算

把以下计算移到本周期反馈、超时、Ack/Reset 和新命令处理之后：

```text
OpFl = OpFlOn || OpFlOff
NoCon = FLB ? Forbid : (OpFl || Trip || Forbid)
PACK Bit21～Bit31
```

避免超时刚发生时 OpFl/NoCon/PACK 滞后一周期。

## 修改方案六：PACK/TAG 和画面状态互斥

严格按 EDEVICEM 文档组装 Bit0～Bit31，最终写入 LP32 TAG。

行程期间抑制旧端点状态：

```text
onCmdActive 或 offCmdActive 时，不输出 Bit10/Bit11
```

反馈到位、cmdActive 结束的当前周期，再显示新的 Bit10/Bit11。这样“正在合/分”不会与旧“已分/已合”同时显示，
与 DEVICE 已验证的画面行为一致。

Bit28～30 使用最终行程状态：

```text
Bit28 = onCmdActive || offCmdActive
Bit29 = onCmdActive
Bit30 = offCmdActive
```

Bit31 按 EDEVICEM 文档保持：

```text
FDev || OpFl
```

不擅自复制 DEVICE 的 TRBL 定义。

## 修改方案七：修正 XTP 按钮表达式

保持按钮写入路径不变，源码补齐真实管脚后即可生效。

推荐禁用条件：

### 合闸按钮

```text
!BIT2        // EnOn=false
|| BIT24     // NoCon/综合禁操
|| BIT6      // 弹簧未储能（EnFSpr 生效后的最终禁合最好增加独立位；若无独立位则结合功能码拒绝）
|| BIT29     // 正在合
|| BIT30     // 正在分
|| BIT10     // 已合
```

### 分闸按钮

```text
!BIT3        // EnOff=false
|| BIT24     // NoCon/综合禁操
|| BIT29     // 正在合
|| BIT30     // 正在分
|| BIT11     // 已分
```

当前 Bit0/Bit1 不应继续用于合/分按钮禁用，因为在 EDEVICEM 中它们是 ToM/ReqA，不是 DEVICE 的保护开/关。

若 Bit6 只表示原始 FSpr，而 EnFSpr=false 时不应禁按钮，需要新增“最终合闸可用”表达式或在 XTP 中同时读取参数；
最低限度由功能码层保证不会误合闸。

复位按钮当前使用 Bit22/Bit23 控制可用性，并同时发送 CRS/CAK，可保留。

编辑 XTP 时必须保持原文件 GB18030 编码，禁止保存成 UTF-8 导致中文和模板解析异常。

## 修改方案八：品质传递

当前实际 MDB 有 QualityT，但类型和 Run 均未实现。

建议按项目通用约定：

```text
0 NoTransfer：输出品质均为 Good
1 OrTransfer：任一参与控制的输入非 Good，相关输出为 Bad
2 AndTransfer：全部参与控制的输入非 Good 时，相关输出为 Bad
```

参与输入至少包括：

```text
Enable, EnOn, EnOff, ToM, ReqA, AOn, AOff,
FBOn, FBOff, Loc, FBat, FDev, POpe, FSpr
```

HMI 命令输入是否参与品质聚合建议不参与，避免一次性命令点品质影响全部状态；其自身写入失败由 HMI 通讯处理。

品质应同步到 On、Off、MA、NoCon、FBFl、Trip、OpFl、Forbid、OpFlOn、OpFlOff 和 TAG。

该项建议在核心状态机稳定后实施，优先级低于 HMI 管脚、TAG 类型和安全状态机。

## 修改方案九：说明文档修订

更新 `EDEVICEM.md`：

1. 补充 CON/COF/CTA/CTM/CAK/CFB/CRS/CDB 为 Input；
2. TAG 改为 LP32 Output，说明为 32 位 HMI 状态打包点；
3. 删除/澄清 PAGE 和源端字符串等当前 MDB 不存在的旧配置；
4. ResetM 文档修正重复的“0”为 0/1/2；
5. 明确控制输出始终受 SetT 硬上限保护；
6. 明确分闸/合闸同时请求时的固定优先级；
7. 明确 Forbid 对活动输出和行程监视的处理；
8. 明确反馈已到位不重复发相同方向脉冲；
9. 明确 HMI Toggle 指令按上升沿消费；
10. 明确 PACK 行程位与端点反馈位互斥显示；
11. 补充 QualityT 语义。

## 部署与工况兼容风险（实施前必须处理）

新增 8 个 LD Input、修改 TAG 类型和增加内部沿/脉冲状态会改变 EDEVICEM 的 BlockStateSchema。

影响：

- 运行中 hotload 很可能因新状态槽大于旧 Arena 槽而拒绝；
- 即使总长度人为做成相同，字段偏移变化也会让 v1 Arena 按新布局误读旧字节；
- 已保存 Condition 使用 v1 全量 Arena 和 SchemaHash，部署后可能无法加载；
- 旧 Snapshot/工况中的 TAG 类型与新 LP32 不同。

禁止只通过填充字段“凑相同 ByteLength”来绕过 SchemaHash，这不能保证字段语义正确。

推荐部署路径：

1. 记录当前旧版 EDEVICEM BlockStateSchema 和程序集；
2. 备份 `rwvdcs-data/conditions` 与 `snapshots`；
3. 在旧版可执行环境中依次加载有效工况，并导出带字段 SchemaCatalog 的 SnapshotV2 或逻辑字段快照；
4. 新版构建 Runtime 后按字段名转换公共状态；
5. 新 HMI Input 初始化为 false；
6. TAG 不迁移旧 LA 原始字节，由新版首周期重新组装；
7. 保留 MA、Trip、OpFl、Forbid、manualForbid、cmdActive 和计时器等公共状态；
8. 验证每个工况后重新保存为新版工况；
9. 全部验证成功后再正式替换 Host；
10. 本次改动按完整重启部署，不承诺直接 hotload。

如果业务允许放弃旧工况，可选择备份后重新保存，但必须由用户明确确认，不能在代码修改中隐式使旧工况失效。

## 实施步骤

### 阶段 0：兼容准备

1. 统计 EDEVICEM 实例数量和所在 DPU；
2. 记录旧 BlockStateSchema ByteLength/字段清单；
3. 选择旧工况迁移或明确放弃策略；
4. 备份工况、快照和当前工程 MDB。

### 阶段 1：字段契约

1. 增加八个 LD HMI Input；
2. 增加 QualityT；
3. TAG 改 LP32；
4. 删除 hmiCmd*，增加 old command edge 状态；
5. 增加 pulseActive 等必要状态；
6. 写元数据测试，与实际 MDB 管脚集合对账。

### 阶段 2：状态机

1. 实现 HMI 上升沿；
2. 实现 Ack/Reset/Toggle；
3. 实现有效禁操；
4. 实现手自动与 MP；
5. 实现分闸固定优先；
6. 拆分脉冲与行程监视；
7. 实现反馈完成、超时、Trip；
8. 后置计算 OpFl/NoCon；
9. 组装 LP32 TAG。

### 阶段 3：画面

1. 保持 CON/COF/CFB/CRS/CAK 写入路径；
2. 修正合/分按钮 DisableExp；
3. 核对显示位和颜色；
4. 保持 GB18030；
5. 用真实 500 ms 指令联调。

### 阶段 4：质量和文档

1. 实现 QualityT；
2. 更新 EDEVICEM.md；
3. 补充部署/迁移说明。

### 阶段 5：验证和迁移

1. 跑功能码单元测试；
2. 跑 Core/Runtime 全量测试；
3. 构建 Host；
4. 对生产 MDB 只读装配检查；
5. 迁移并逐个加载旧工况；
6. 使用 XTP 做合闸、分闸、挂牌、复位联调。

## 关键文件

| 文件 | 操作 | 说明 |
| --- | --- | --- |
| `src/Blocks/RWVDCS.Blocks/RW/FC_EDEVICEM.cs` | 修改 | 管脚契约、LP32 TAG、QualityT、内部状态 |
| `src/Blocks/RWVDCS.Blocks/RW/FC_EDEVICEM_RUN.cs` | 重构 | HMI 沿、状态机、反馈/超时/Trip、PACK、品质 |
| `D:\项目\睿渥\template\style\Popup_TPRI\电气开关.xtp` | 修改 | 合/分按钮禁用表达式，保持 GB18030 |
| `D:\项目\睿渥\算法块说明书_md\EDEVICEM.md` | 修改 | 修正文档契约和行为 |
| `src/Tests/RWVDCS.Core.Tests/EdevicemControlTests.cs` | 新增 | 状态机和位图测试 |
| `src/Tests/RWVDCS.Runtime.Tests/EngineeringMetadataTests.cs` | 修改 | EDEVICEM MDB 管脚与字段对账 |
| `src/Runtime/RWVDCS.Runtime/SnapshotV2.cs` 或迁移工具 | 视策略修改 | 旧工况字段级迁移 |

## 回归测试矩阵

### HMI 输入

- CON/COF 500 ms 高电平只触发一次；
- CFB/CDB 持续高多个周期只翻转一次；
- CRS+CAK 同周期幂等；
- CTA/CTM 与 ToM/ReqA 优先级；
- MP=0/1/2。

### 操作状态机

- 合闸/分闸正常反馈；
- 反馈提前到达；
- SetT 到期输出释放；
- Tover 超时 OpFlOn/Off；
- ResetM=0/1/2；
- SetT=0、小于 Cycle、Tover<SetT；
- 已合不重复合、已分不重复分；
- AOn/AOff 持续电平；
- AOn/AOff 同时为 true 时分闸优先；
- 行程中重复同方向命令不重置 Tover；
- 行程中反方向命令不反向重入。

### 禁操和异常

- manualForbid；
- EnLoc=true/false；
- EnFBat=true/false；
- EnFDev=true/false；
- EnFSpr 只禁合；
- Forbid 中断物理输出但保留行程监视；
- FBOn+FBOff 同时为 true；
- 意外 FBOn 下降触发 Trip；
- 授权分闸不触发 Trip；
- Ack、Reset 后自动请求恢复。

### PACK/XTP

- Bit0～Bit31 逐位测试；
- Bit31 与多个低位同时存在仍精确；
- 行程位与端点反馈位互斥；
- 合/分按钮禁用条件；
- 挂牌状态和按钮；
- 复位按钮只在 Trip/OpFl 时可用；
- XTP GB18030 往返不损坏中文。

### 品质

- QualityT=0/1/2；
- 单个输入 Bad；
- 全部输入 Bad；
- 输出和 TAG 品质一致。

### 工况兼容

- 旧字段状态迁移；
- 新命令输入默认 false；
- TAG 首周期重建；
- MA/Trip/OpFl/manualForbid/计时状态保留；
- 新版条件保存后再次加载完全一致。

## 风险与缓解

| 风险 | 缓解措施 |
| --- | --- |
| 旧 XTP 命令当前不生效 | 优先补齐真实 Input 管脚和上升沿 |
| 32 位 PACK 浮点丢位 | TAG 改 LP32，并做组合位测试 |
| 持续自动请求重复发脉冲 | 目标反馈守卫 + cmdActive 重入保护 |
| ResetM=2 输出永久保持 | 输出脉冲计时与行程监视拆分，SetT 硬上限 |
| 禁操时物理输出仍保持 | Forbid 立即释放 On/Off，保留行程监视 |
| EnLoc=false 仍被 Loc 阻断 | 所有控制统一使用 locForbid |
| 派生状态滞后一周期 | OpFl/NoCon/PACK 后置计算 |
| XTP 位映射沿用 DEVICE | 按 EDEVICEM Bit 表重写 DisableExp |
| 改字段导致旧工况失效 | 部署前做字段级迁移或明确放弃，不直接 hotload |
| XTP 中文乱码 | 使用 XmlDocument/GB18030 原编码保存 |
| 误复制 DEVICE 特有能力 | 以实际 MDB 与 EDEVICEM 文档为契约边界 |

## 验收标准

- [ ] 实际 MDB 的全部 EDEVICEM Input/Output/Parameter 都有匹配 C# 字段。
- [ ] 电气开关 XTP 的 CON/COF/CFB/CRS/CAK 能真实驱动功能码。
- [ ] 500 ms 脉冲跨多个周期只消费一次。
- [ ] TAG 为 LP32，Bit0～Bit31 无精度丢失。
- [ ] On/Off 永远互斥且受 SetT 硬脉宽上限。
- [ ] 反馈已到位不重复发同方向脉冲。
- [ ] ResetM=2 不会造成永久控制输出。
- [ ] Forbid 立即释放物理输出并阻止新操作。
- [ ] EnLoc/EnFBat/EnFDev/EnFSpr 参数实际生效。
- [ ] 合分同时请求时采用分闸优先。
- [ ] OpFl/NoCon/TAG 在故障发生当前周期同步更新。
- [ ] 行程中 Bit29/30 与 Bit10/11 不重叠显示旧端点。
- [ ] QualityT 参数有明确行为并通过测试。
- [ ] XTP 按钮禁用条件与最终功能码状态一致。
- [ ] 旧工况迁移或放弃策略在部署前得到确认并完成验证。
- [ ] Core、Runtime 测试和 Host build 全部通过。

## 验证命令

```text
dotnet test src/Tests/RWVDCS.Core.Tests/RWVDCS.Core.Tests.csproj
dotnet test src/Tests/RWVDCS.Runtime.Tests/RWVDCS.Runtime.Tests.csproj
dotnet build src/Host/RWVDCS.Host/RWVDCS.Host.csproj
```

现场联调按以下顺序：

```text
未操作空闲
→ 合闸正常反馈
→ 分闸正常反馈
→ 合闸无反馈超时
→ 分闸无反馈超时
→ 挂牌/解牌
→ Loc/FBat/FDev
→ FSpr 禁合
→ Trip
→ Ack/Reset
→ 自动持续请求
→ 工况保存/加载
```
