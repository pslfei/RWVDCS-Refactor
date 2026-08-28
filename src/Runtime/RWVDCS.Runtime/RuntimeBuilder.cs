using System.Runtime.InteropServices;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.PointStore;
using RWVDCS.Core.Types;
using RWVDCS.Engineering;

namespace RWVDCS.Runtime;

/// <summary>构建选项。</summary>
public sealed class RuntimeBuildOptions
{
    /// <summary>Arena 后备文件目录；null = 纯内存（快照仍可显式保存）。</summary>
    public string? ArenaDirectory { get; init; }
}

/// <summary>构建报告：装配过程中的告警/错误/统计（对齐老系统 LoadDB 的日志面）。</summary>
public sealed class RuntimeBuildReport
{
    public List<string> Errors { get; } = [];
    public List<string> Warnings { get; } = [];

    /// <summary>功能码不在块目录中的块（老系统 Command ctor 抛异常后跳过），fcName → 块数。</summary>
    public Dictionary<string, int> MissingFcTypes { get; } = new(StringComparer.OrdinalIgnoreCase);

    public int PointCount { get; internal set; }
    public int IntermediatePointCount { get; internal set; }
    public int CommandCount { get; internal set; }
    public int DeadInputBindings { get; internal set; }
    public int DeadOutputBindings { get; internal set; }
    public TimeSpan Elapsed { get; internal set; }
}

/// <summary>
/// 运行时装配器：EngineeringModel + BlockCatalog → DcsRuntime。
/// 流程逐步对齐老系统 Dcs.LoadDB（Dcs.cs:1446-1636）：
/// 逐控制器建点 → pin-point 预处理（Dpu.cs:1493-1588）→ 逐块建命令（Command ctor 语义）→ 全局接线解析。
/// </summary>
public static class RuntimeBuilder
{
    public static DcsRuntime Build(EngineeringModel model, BlockCatalog catalog, RuntimeBuildOptions? options = null)
    {
        options ??= new RuntimeBuildOptions();
        var report = new RuntimeBuildReport();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // 块状态槽 TypeId：按目录内 FCName 排序分配，跨进程/跨次构建稳定（参与 SchemaHash）
        var blockTypeIds = BuildBlockTypeIds(catalog);

        var dpus = new List<DpuRuntime>();
        // 跨 DPU 名字表（老系统 rtd.Master 聚合解析的等价物；控制器注册序，首见生效）
        var globalSlots = new Dictionary<string, PointSlotRef>(StringComparer.OrdinalIgnoreCase);

        foreach (var controller in model.Controllers)
            dpus.Add(BuildDpu(controller, catalog, blockTypeIds, options, report, globalSlots));

        // 全局接线解析：所有 DPU 的点都注册完之后统一解析（老系统为运行期懒解析，结果等价）
        foreach (var dpu in dpus)
            ResolveBindings(dpu, globalSlots, report);

        sw.Stop();
        report.Elapsed = sw.Elapsed;
        return new DcsRuntime(dpus, globalSlots, report);
    }

    private static Dictionary<string, int> BuildBlockTypeIds(BlockCatalog catalog)
    {
        var names = catalog.All.Select(kv => kv.Key).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < names.Count; i++)
            map[names[i]] = WellKnownTypeIds.BlockBase + i;
        return map;
    }

    // =================================================================
    // 单控制器装配
    // =================================================================
    private static DpuRuntime BuildDpu(
        ControllerModel controller,
        BlockCatalog catalog,
        Dictionary<string, int> blockTypeIds,
        RuntimeBuildOptions options,
        RuntimeBuildReport report,
        Dictionary<string, PointSlotRef> globalSlots)
    {
        string dpuName = controller.Name;

        // ---- 老系统 pointDict：本控制器点名 → 点模型（区分大小写、后写覆盖，Operation.cs:235）
        var pointDict = new Dictionary<string, PointModel>(StringComparer.Ordinal);
        foreach (var p in controller.Points)
            pointDict[p.Name] = p;

        // ---- 老系统 blockNamesToFCNames / fcDetails（区分大小写；重复 AlgName 老系统直接抛）
        var blockByName = new Dictionary<string, BlockModel>(StringComparer.Ordinal);
        foreach (var b in controller.Blocks)
        {
            if (!blockByName.TryAdd(b.Name, b))
                throw new InvalidOperationException($"[{dpuName}] 块名重复：{b.Name}（老系统 fcDetails.Add 在此抛异常）");
        }

        // ---- pin-point 预处理（对齐 Dpu.cs:1493-1588：注册中间点 + 源块输出追加）
        bool dpuFailed = false;
        var intermediateNames = new List<string>();
        var registeredIntermediate = new HashSet<string>(StringComparer.Ordinal);
        foreach (var block in controller.Blocks)
        {
            if (block.FcName == "APSM")
                continue;

            foreach (var pin in block.Pins)
            {
                string pn = pin.PointName ?? "";
                if (pn.Length == 0 || !LegacySemantics.IsPinPointName(pn))
                    continue;

                int dotIndex = pn.LastIndexOf('.');
                if (dotIndex <= 0)
                    continue;

                if (!registeredIntermediate.Add(pn))
                    continue;
                intermediateNames.Add(pn);

                string srcBlockName = pn[..dotIndex];
                string srcPinName = pn[(dotIndex + 1)..];

                if (!blockByName.TryGetValue(srcBlockName, out var srcBlock))
                {
                    // 老系统此处抛异常导致整个 DPU 的命令装配失败（Dpu.cs:1542-1545）。
                    // 新系统：该 DPU 不建任何命令（点保留），并记录错误。
                    report.Errors.Add(
                        $"[{dpuName}] {block.Name} 的管脚 PointName {pn} 的源块 {srcBlockName} 在数据库中不存在（老系统此处抛异常）");
                    dpuFailed = true;
                    continue;
                }

                // 源块输出追加（区分大小写查找，老系统 fcDetails 内层字典语义）
                var srcPin = FindPinExact(srcBlock, srcPinName);
                if (srcPin != null)
                {
                    srcPin.PointName = string.IsNullOrEmpty(srcPin.PointName)
                        ? pn
                        : srcPin.PointName + "," + pn;
                }
                else
                {
                    report.Warnings.Add($"[{dpuName}] 源块 {srcBlockName} 中未找到 Pin={srcPinName}（老系统仅 Debug 输出）");
                }
            }
        }

        // ---- Arena 槽规划：DB 点 → 中间点 → 块状态槽
        var builder = new ArenaBuilder();
        var localSlots = new Dictionary<string, PointSlotRef>(StringComparer.OrdinalIgnoreCase);
        var plannedKinds = new List<PointKind>();   // 按 SID 序
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int dbPointSlots = 0;

        foreach (var p in controller.Points)
        {
            if (!seenNames.Add(p.Name))
            {
                // 老系统 rtd.New 重名返回 -1 → 静默跳过（Dpu.cs:1434-1440）
                report.Warnings.Add($"[{dpuName}] 点名重复，跳过：{p.Name}");
                continue;
            }
            var kind = PointLayout.KindFromDataType(p.DataType);
            builder.AddRawSlot(p.Name, PointLayout.TypeIdOf(kind), PointLayout.SizeOf(kind), BuildPointInitBytes(p, kind));
            plannedKinds.Add(kind);
            report.PointCount++;
            dbPointSlots++;
        }

        foreach (var name in intermediateNames)
        {
            if (!seenNames.Add(name))
                continue; // 老系统 rtd.New 失败被忽略（intermediateSid 未检查）
            builder.AddRawSlot(name, WellKnownTypeIds.LA, LA.Size, BuildUnconfiguredLaInitBytes());
            plannedKinds.Add(PointKind.LA);
            report.IntermediatePointCount++;
        }

        // ---- 块命令预筛与状态槽（老系统 Command ctor 内 rtd.New(blockName)）
        var commandPlans = new List<(BlockModel Block, Type FcType, int StateSid)>();
        if (!dpuFailed)
        {
            foreach (var block in controller.Blocks)
            {
                if (block.FcName == "APSM")
                    continue; // Dpu.cs:1595-1598

                if (!catalog.TryGet(block.FcName, out var fcType))
                {
                    // 老系统 FCManufactory.Types[fcname] == null → CommandException → 跳过该块
                    report.MissingFcTypes.TryGetValue(block.FcName, out int n);
                    report.MissingFcTypes[block.FcName] = n + 1;
                    continue;
                }

                if (!seenNames.Add(block.Name))
                {
                    // 老系统 rtd.New(blockName) 重名 → ctor 抛 → 跳过该块
                    report.Warnings.Add($"[{dpuName}] 块名与点名冲突，跳过块：{block.Name}");
                    continue;
                }

                var schema = BlockStateSchema.For(fcType);
                int sid = builder.AddRawSlot(block.Name, blockTypeIds[block.FcName], Math.Max(schema.ByteLength, 1));
                plannedKinds.Add(PointKind.Block);
                commandPlans.Add((block, fcType, sid));
            }
        }

        // ---- 创建 Arena 并落地名字表
        string? backingFile = options.ArenaDirectory is null
            ? null
            : Path.Combine(options.ArenaDirectory, SanitizeFileName(dpuName) + ".arena");
        if (backingFile != null)
            Directory.CreateDirectory(options.ArenaDirectory!);

        var arena = PointArena.Create(builder, backingFile);
        for (int sid = 0; sid < arena.SlotCount; sid++)
        {
            string? name = arena.GetName(sid);
            if (name == null)
                continue;
            var slotRef = new PointSlotRef(arena, sid, plannedKinds[sid]);
            localSlots[name] = slotRef;
            globalSlots.TryAdd(name, slotRef); // 跨 DPU 首见生效（控制器注册序）
        }

        var dpu = new DpuRuntime(controller.Id, dpuName, arena, localSlots) { DbPointSlotCount = dbPointSlots };

        // 基线镜像：装配初值状态（FirstRun 前）的数据区压缩副本，增量快照的参照系。
        // Brotli quality=1（速度优先）；90MB 数据区压缩 ~ 数百 ms、驻留内存缩到 ~2-5%。
        {
            using var ms = new MemoryStream();
            using (var br = new System.IO.Compression.BrotliStream(
                       ms, System.IO.Compression.CompressionLevel.Fastest, leaveOpen: true))
            {
                using var arenaAccess = arena.AcquireAccessLease();
                br.Write(arena.DataRegion);
            }
            dpu.InitialDataCompressed = ms.ToArray();
        }

        // ---- 逐块构造命令（Command ctor 语义）
        foreach (var (block, fcType, stateSid) in commandPlans)
        {
            try
            {
                var cmd = BuildCommand(block, fcType, stateSid, dpu, pointDict, report);
                dpu.Commands.Add(cmd);
                report.CommandCount++;
            }
            catch (Exception ex)
            {
                // 老系统 Dpu.cs:1607-1612：单块失败 continue
                report.Errors.Add($"[{dpuName}] 块 {block.Name}({block.FcName}) 装配失败：{ex.Message}");
            }
        }

        return dpu;
    }

    private static PinDetailModel? FindPinExact(BlockModel block, string pinName)
    {
        foreach (var p in block.Pins)
            if (string.Equals(p.PinName, pinName, StringComparison.Ordinal))
                return p;
        return null;
    }

    /// <summary>
    /// 点槽初始字节。旧字段对齐 Dpu.cs:1447-1477，并为 LA 注入工程报警限值。
    /// </summary>
    private static byte[] BuildPointInitBytes(PointModel p, PointKind kind)
    {
        switch (kind)
        {
            case PointKind.LA:
            {
                var bytes = new byte[LA.Size];
                MemoryMarshal.Write(bytes.AsSpan((int)PointLayout.LaBufferOffset), (float)p.DefaultValue);
                MemoryMarshal.Write(bytes.AsSpan((int)PointLayout.LaMaxValueOffset), p.MaxValue);
                MemoryMarshal.Write(bytes.AsSpan((int)PointLayout.LaMinValueOffset), p.MinValue);
                MemoryMarshal.Write(bytes.AsSpan((int)PointLayout.LaHighAlarmLimit3ValueOffset), p.HighAlarmLimit3Value);
                MemoryMarshal.Write(bytes.AsSpan((int)PointLayout.LaHighAlarmLimit2ValueOffset), p.HighAlarmLimit2Value);
                MemoryMarshal.Write(bytes.AsSpan((int)PointLayout.LaHighAlarmLimit1ValueOffset), p.HighAlarmLimit1Value);
                MemoryMarshal.Write(bytes.AsSpan((int)PointLayout.LaLowAlarmLimit3ValueOffset), p.LowAlarmLimit3Value);
                MemoryMarshal.Write(bytes.AsSpan((int)PointLayout.LaLowAlarmLimit2ValueOffset), p.LowAlarmLimit2Value);
                MemoryMarshal.Write(bytes.AsSpan((int)PointLayout.LaLowAlarmLimit1ValueOffset), p.LowAlarmLimit1Value);
                return bytes;
            }
            case PointKind.LD:
            {
                var bytes = new byte[LD.Size];
                bytes[PointLayout.LdBufferOffset] = (bool)p.DefaultValue ? (byte)1 : (byte)0;
                return bytes;
            }
            case PointKind.LP:
                return new byte[LP.Size];
            case PointKind.LP32:
                return new byte[LP32.Size];
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }
    }

    /// <summary>中间 LA 点没有工程报警配置，使用 NaN 让六级状态计算跳过全部限值。</summary>
    private static byte[] BuildUnconfiguredLaInitBytes()
    {
        var bytes = new byte[LA.Size];
        var value = new LA();
        MemoryMarshal.Write(bytes.AsSpan(), in value);
        return bytes;
    }

    // =================================================================
    // 单块命令构造（对齐 Command ctor：Command.cs:1729-2241）
    // internal：热更换代（BlockHotSwapper）用同一套构造语义重建命令
    // =================================================================
    internal static BlockCommand BuildCommand(
        BlockModel block,
        Type fcType,
        int stateSid,
        DpuRuntime dpu,
        Dictionary<string, PointModel> pointDict,
        RuntimeBuildReport report)
    {
        // 老系统块实例来自 rtd.New → 类型默认构造；FcName/FcCode 字段保持初始值（老系统不赋值）
        var fc = (Function)Activator.CreateInstance(fcType)!;
        var cmd = new BlockCommand(block.Name, block.FcName, fc, dpu, stateSid);

        // 大小写不敏感 details（Command.cs:1764-1776，重复 key 保留首个）
        var details = new Dictionary<string, PinDetailModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in block.Pins)
            details.TryAdd(p.PinName, p);

        var inputBindings = new List<InputBinding>();
        var outputBindings = new List<OutputBinding>();
        var pinSync = new List<PinSyncEntry>();

        foreach (var field in fcType.GetFields())
        {
            var pinAttr = (PinTypeAttribute?)field.GetCustomAttributes(typeof(PinTypeAttribute), false).FirstOrDefault();
            if (pinAttr == null)
                continue;
            var pinType = pinAttr.PinType;

            // Internal 非 FunctionCode：老系统缓存标记 SkipPin（RW 块无 FunctionCode 字段）
            if (pinType is PinTypes.Internal or PinTypes.Cascaded or PinTypes.None)
                continue;

            if (!details.TryGetValue(field.Name, out var pd))
                continue;

            var accessor = FieldAccessor.For(field);

            switch (pinType)
            {
                case PinTypes.Input:
                {
                    InputBinding? binding = null;
                    if (!string.IsNullOrEmpty(pd.PointName))
                    {
                        binding = new InputBinding
                        {
                            Pin = accessor,
                            PointName = pd.PointName,
                            Reversed = pd.Reversed,
                        };
                        inputBindings.Add(binding);
                    }

                    if (pd.HasDefaultValue)
                    {
                        object def = ResolveIoDefault(pd.DefaultValue, pointDict);
                        if (binding != null)
                            binding.PendingBufferValue = def; // 老系统写 pin buffer；死绑定兜底首周期读到
                        ApplyLiveIoDefault(fc, accessor, def); // Command.cs:1931-1940
                    }

                    pinSync.Add(new PinSyncEntry { Pin = accessor, FieldName = field.Name });
                    break;
                }

                case PinTypes.Output:
                {
                    if (!string.IsNullOrEmpty(pd.PointName))
                    {
                        if (pd.PointName.Contains(','))
                        {
                            // Command.cs:1975-2025：逗号拆分，Reverse 为跨段粘滞的可变状态
                            string[] parts = pd.PointName.Split(',');
                            bool reverse = pd.Reversed;
                            foreach (string rawPart in parts)
                            {
                                string part = rawPart;
                                if (!string.IsNullOrEmpty(part))
                                {
                                    if (part.Contains('~'))
                                    {
                                        part = part.Replace("~", "");
                                        reverse = true;
                                    }
                                    else
                                    {
                                        reverse = false;
                                    }
                                }
                                else
                                {
                                    // 空段：老系统 Wire ctor 抛异常中断整个拆分循环，后续段丢失
                                    break;
                                }

                                pd.Reversed = reverse; // 老系统直接改 pindetails.Reverse（粘滞到后续使用）
                                outputBindings.Add(new OutputBinding
                                {
                                    Pin = accessor,
                                    PointName = part,
                                    Reversed = reverse,
                                });
                            }
                        }
                        else
                        {
                            outputBindings.Add(new OutputBinding
                            {
                                Pin = accessor,
                                PointName = pd.PointName,
                                Reversed = pd.Reversed,
                            });
                        }
                    }

                    if (pd.HasDefaultValue)
                        ApplyLiveIoDefault(fc, accessor, ResolveIoDefault(pd.DefaultValue, pointDict));

                    pinSync.Add(new PinSyncEntry { Pin = accessor, FieldName = field.Name });
                    break;
                }

                case PinTypes.IO:
                {
                    InputBinding? binding = null;
                    if (!string.IsNullOrEmpty(pd.PointName))
                    {
                        binding = new InputBinding
                        {
                            Pin = accessor,
                            PointName = pd.PointName,
                            Reversed = pd.Reversed,
                        };
                        inputBindings.Add(binding);

                        // IO 输出方向：单目标，不拆分（Command.cs:2146-2163）
                        outputBindings.Add(new OutputBinding
                        {
                            Pin = accessor,
                            PointName = pd.PointName,
                            Reversed = pd.Reversed,
                        });
                    }

                    if (pd.HasDefaultValue && binding != null)
                    {
                        // 老系统只写 pin buffer、不写 live（Command.cs:2181-2182）
                        binding.PendingBufferValue = ResolveIoDefault(pd.DefaultValue, pointDict);
                    }

                    pinSync.Add(new PinSyncEntry { Pin = accessor, FieldName = field.Name });
                    break;
                }

                case PinTypes.Constant:
                {
                    if (pd.HasDefaultValue && pd.DefaultValue != null)
                        ApplyConstant(fc, field, pd.DefaultValue); // Command.cs:2206-2238
                    break;
                }
            }
        }

        cmd.InputBindings = inputBindings.ToArray();
        cmd.OutputBindings = outputBindings.ToArray();
        cmd.PinSync = pinSync.ToArray();
        return cmd;
    }

    /// <summary>
    /// I/O 管脚默认值解析：DefaultFromPoint 占位 → 本控制器 pointDict 的
    /// Convert.ToSingle(点默认值)，缺失/异常取 0f（Operation.cs:378-390）；其余原样（float）。
    /// </summary>
    private static object ResolveIoDefault(object? defaultValue, Dictionary<string, PointModel> pointDict)
    {
        if (defaultValue is DefaultFromPoint dfp)
        {
            if (pointDict.TryGetValue(dfp.PointName, out var pm))
            {
                try
                {
                    return Convert.ToSingle(pm.DefaultValue);
                }
                catch
                {
                    return 0f;
                }
            }
            return 0f;
        }
        return defaultValue ?? 0f;
    }

    /// <summary>live 管脚默认值写入（Command.cs:1931-1940：仅 IValuable 生效，异常吞掉）。</summary>
    private static void ApplyLiveIoDefault(Function fc, FieldAccessor accessor, object def)
    {
        try
        {
            object? pinObj = accessor.Read(fc);
            if (pinObj is IValuable v)
            {
                v.Value = def;
                accessor.Write(fc, pinObj);
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// 规格参数写 live 字段（Command.cs:2212-2237）：
    /// 类型匹配直写；值类型 Convert.ChangeType（失败吞掉）；string 用 ToString。
    /// </summary>
    private static void ApplyConstant(Function fc, System.Reflection.FieldInfo field, object val)
    {
        try
        {
            if (field.FieldType.IsInstanceOfType(val))
            {
                field.SetValue(fc, val);
            }
            else if (field.FieldType.IsValueType)
            {
                try
                {
                    object converted = Convert.ChangeType(val, field.FieldType);
                    field.SetValue(fc, converted);
                }
                catch
                {
                }
            }
            else if (field.FieldType == typeof(string))
            {
                field.SetValue(fc, val.ToString());
            }
        }
        catch
        {
        }
    }

    // =================================================================
    // 接线解析（老系统运行期懒解析的构建期等价物）
    // =================================================================
    private static void ResolveBindings(DpuRuntime dpu, Dictionary<string, PointSlotRef> globalSlots, RuntimeBuildReport report)
    {
        foreach (var cmd in dpu.Commands)
            ResolveCommandBindings(cmd, dpu, globalSlots, report);
    }

    /// <summary>解析单命令的接线（构建期与热更换代共用）。</summary>
    internal static void ResolveCommandBindings(
        BlockCommand cmd, DpuRuntime dpu, Dictionary<string, PointSlotRef> globalSlots, RuntimeBuildReport report)
    {
        foreach (var b in cmd.InputBindings)
        {
            if (dpu.LocalSlots.TryGetValue(b.PointName, out var slot) || globalSlots.TryGetValue(b.PointName, out slot))
                b.Source = slot;
            if (b.Source is not { IsRealPoint: true })
                report.DeadInputBindings++;
        }

        foreach (var b in cmd.OutputBindings)
        {
            if (dpu.LocalSlots.TryGetValue(b.PointName, out var slot) || globalSlots.TryGetValue(b.PointName, out slot))
                b.Target = slot;
            if (b.Target is not { IsRealPoint: true })
                report.DeadOutputBindings++;
        }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}
