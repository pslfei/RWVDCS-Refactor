using System.Reflection;
using System.Runtime.Loader;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using RWVDCS.Engineering;

namespace RWVDCS.Runtime;

/// <summary>热更换代结果。</summary>
public sealed class HotSwapReport
{
    public bool Success { get; internal set; }
    public int Generation { get; internal set; }
    public List<string> SwappedFcNames { get; } = [];
    public int CommandsSwapped { get; internal set; }
    public int FieldsTransferred { get; internal set; }
    public List<string> Messages { get; } = [];

    /// <summary>被替换下去的上一代 ALC 弱引用（诊断真卸载用；无换代时为 null）。</summary>
    public WeakReference? RetiredAlc { get; internal set; }
}

/// <summary>
/// 功能块热更换代：把 Roslyn 编译出的新块程序集装入可回收 ALC，
/// 对受影响的功能码逐命令"重建 + 状态按名转移 + 原位替换"。
/// 这是老系统"现调现改 function code"体验在新架构下的运行时承载
/// （老系统靠 csc 编译 + 重启 DPU 装载；新系统运行中原子换代、状态不丢）。
/// </summary>
/// <remarks>
/// 必须在周期边界调用（宿主经 <see cref="ScanScheduler.RunAtCycleBoundary"/> 串行化）。
/// 约束：新块类型的状态槽字节数不得超过装配期分配的 Arena 槽长（新增大字段需完整重建运行时）。
/// 注意：热更改变了字段布局时，此前保存的工况快照中该块的状态区将不再对位，热更后应重存工况。
/// </remarks>
public sealed class BlockHotSwapper
{
    private sealed class CollectibleContext(string name) : AssemblyLoadContext(name, isCollectible: true);

    private readonly DcsRuntime _runtime;
    private readonly Dictionary<string, Dictionary<string, BlockModel>> _blocksByDpu;   // dpuName → blockName → model
    private readonly Dictionary<string, Dictionary<string, PointModel>> _pointsByDpu;   // dpuName → pointDict（Ordinal，对齐装配期）
    private readonly List<(int Generation, WeakReference Context)> _retired = [];
    private CollectibleContext? _current;
    private int _generation;

    public BlockHotSwapper(DcsRuntime runtime, EngineeringModel model)
    {
        _runtime = runtime;
        _blocksByDpu = new Dictionary<string, Dictionary<string, BlockModel>>(StringComparer.OrdinalIgnoreCase);
        _pointsByDpu = new Dictionary<string, Dictionary<string, PointModel>>(StringComparer.OrdinalIgnoreCase);

        foreach (var controller in model.Controllers)
        {
            var blocks = new Dictionary<string, BlockModel>(StringComparer.Ordinal);
            foreach (var b in controller.Blocks)
                blocks.TryAdd(b.Name, b);
            _blocksByDpu[controller.Name] = blocks;

            var points = new Dictionary<string, PointModel>(StringComparer.Ordinal);
            foreach (var p in controller.Points)
                points[p.Name] = p;
            _pointsByDpu[controller.Name] = points;
        }
    }

    public int Generation => _generation;

    /// <summary>历代已退役 ALC 的弱引用（诊断泄漏用）。</summary>
    public IReadOnlyList<(int Generation, WeakReference Context)> RetiredContexts => _retired;

    /// <summary>同步热更重建所使用的工程管脚默认值，避免后续热更恢复旧值。</summary>
    public bool TrySetPinDefault(
        string dpuName,
        string blockName,
        string pinName,
        object? value,
        bool hasDefaultValue,
        out object? oldValue,
        out bool oldHasDefaultValue)
    {
        oldValue = null;
        oldHasDefaultValue = false;
        if (!_blocksByDpu.TryGetValue(dpuName, out var blocks))
            return false;
        BlockModel? block = blocks.Values.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, blockName, StringComparison.OrdinalIgnoreCase));
        PinDetailModel? pin = block?.FindPin(pinName);
        if (pin == null)
            return false;

        oldValue = pin.DefaultValue;
        oldHasDefaultValue = pin.HasDefaultValue;
        pin.DefaultValue = value is Array array ? array.Clone() : value;
        pin.HasDefaultValue = hasDefaultValue;
        return true;
    }

    /// <summary>
    /// 装载新一代块程序集并换代。程序集中所有带 FCName 特性的块类型都会参与替换；
    /// 运行时中不存在对应功能码的类型仅告警。
    /// </summary>
    public HotSwapReport Apply(byte[] assemblyImage, byte[]? pdbImage)
    {
        var report = new HotSwapReport();
        int gen = _generation + 1;
        var context = new CollectibleContext($"blocks-gen{gen}");

        Assembly assembly;
        using (var pe = new MemoryStream(assemblyImage))
        using (var pdb = pdbImage is null ? null : new MemoryStream(pdbImage))
        {
            assembly = context.LoadFromStream(pe, pdb);
        }

        // 收集程序集中的功能码类型
        var newTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract || !typeof(Function).IsAssignableFrom(type))
                continue;
            var attr = type.GetCustomAttribute<FCNameAttribute>(inherit: false);
            if (attr == null)
                continue;
            newTypes[attr.FCName] = type;
        }

        if (newTypes.Count == 0)
        {
            context.Unload();
            report.Messages.Add("程序集中没有任何带 [FCName] 的功能块类型，热更中止。");
            return report;
        }

        // 逐 DPU 逐命令换代
        var buildReport = new RuntimeBuildReport();
        foreach (var dpu in _runtime.Dpus)
        {
            if (!_blocksByDpu.TryGetValue(dpu.Name, out var blockModels) ||
                !_pointsByDpu.TryGetValue(dpu.Name, out var pointDict))
                continue;

            var commands = dpu.Commands;
            for (int i = 0; i < commands.Count; i++)
            {
                var oldCmd = commands[i];
                if (!newTypes.TryGetValue(oldCmd.FcName, out var newType))
                    continue;
                if (newType == oldCmd.Fc.GetType())
                    continue;

                if (!blockModels.TryGetValue(oldCmd.Name, out var blockModel))
                {
                    report.Messages.Add($"[{dpu.Name}] {oldCmd.Name}：工程模型中找不到块定义，跳过。");
                    continue;
                }

                // 新状态布局必须放得进装配期分配的槽（快照边界 Flush 的硬约束）
                var newSchema = BlockStateSchema.For(newType);
                int slotLen = dpu.Arena.GetByteLength(oldCmd.StateSid);
                if (newSchema.ByteLength > slotLen)
                {
                    report.Messages.Add(
                        $"[{dpu.Name}] {oldCmd.Name}({oldCmd.FcName})：新状态 {newSchema.ByteLength}B 超过槽长 {slotLen}B，" +
                        "跳过（新增/扩大字段需重启装配）。");
                    continue;
                }

                BlockCommand newCmd;
                try
                {
                    newCmd = RuntimeBuilder.BuildCommand(blockModel, newType, oldCmd.StateSid, dpu, pointDict, buildReport);
                }
                catch (Exception ex)
                {
                    report.Messages.Add($"[{dpu.Name}] {oldCmd.Name}({oldCmd.FcName})：重建命令失败：{ex.Message}");
                    continue;
                }

                RuntimeBuilder.ResolveCommandBindings(newCmd, dpu, _runtime.GlobalSlots, buildReport);

                // 状态按名转移（管脚 + 私有内部状态），随后清掉装配期默认值残留——
                // 换代是"继续跑"，不是"重新初始化"
                report.FieldsTransferred += TransferState(oldCmd.Fc, newCmd.Fc);
                foreach (var b in newCmd.InputBindings)
                    b.PendingBufferValue = null;

                commands[i] = newCmd;
                report.CommandsSwapped++;
            }
        }

        foreach (var fc in newTypes.Keys)
            report.SwappedFcNames.Add(fc);

        if (report.CommandsSwapped == 0)
        {
            context.Unload();
            report.Messages.Add("没有任何命令被替换（功能码在当前工程中未使用？），新代已丢弃。");
            return report;
        }

        // 换代成功：退役上一代
        if (_current != null)
        {
            var weak = new WeakReference(_current);
            _retired.Add((_generation, weak));
            report.RetiredAlc = weak;
            _current.Unload();
        }
        _current = context;
        _generation = gen;
        report.Generation = gen;
        report.Success = true;
        return report;
    }

    /// <summary>
    /// 按字段名转移块状态（旧实例 → 新实例）。同名同型直拷；数组按元素拷（截断到新容量）；
    /// 值类型尝试 Convert 转换；其余跳过。返回成功转移的字段数。
    /// <paramref name="skipConstants"/>：在线下装时置 true——规格数（Constant 管脚）
    /// 取新工程装配值，不从旧状态转移（热更换代则全转移：工程没变，保留在线改过的参数）。
    /// </summary>
    internal static int TransferState(Function from, Function to, bool skipConstants = false)
    {
        var src = BlockStateSchema.For(from.GetType());
        var dst = BlockStateSchema.For(to.GetType());
        int copied = 0;

        foreach (var d in dst.Fields)
        {
            if (skipConstants && d.PinType == PinTypes.Constant)
                continue;
            if (!src.TryGetField(d.Name, out var s))
                continue;

            object? val;
            try
            {
                val = s.Field.GetValue(from);
            }
            catch
            {
                continue;
            }

            var st = s.Field.FieldType;
            var dt = d.Field.FieldType;

            try
            {
                if (st == dt)
                {
                    if (val is Array srcArr)
                    {
                        // 元素类型是共享 Core/BCL 类型，数组实例不钉住旧 ALC；但仍拷进新实例自己的数组
                        if (d.Field.GetValue(to) is Array dstArr)
                            Array.Copy(srcArr, dstArr, Math.Min(srcArr.Length, dstArr.Length));
                        else
                            d.Field.SetValue(to, srcArr.Clone());
                    }
                    else
                    {
                        d.Field.SetValue(to, val);
                    }
                    copied++;
                }
                else if (dt.IsValueType && val != null)
                {
                    d.Field.SetValue(to, Convert.ChangeType(val, dt));
                    copied++;
                }
                else if (dt == typeof(string))
                {
                    d.Field.SetValue(to, val?.ToString());
                    copied++;
                }
            }
            catch
            {
                // 单字段转移失败不阻断换代（类型不兼容的新字段保持其构造默认值）
            }
        }

        return copied;
    }
}
