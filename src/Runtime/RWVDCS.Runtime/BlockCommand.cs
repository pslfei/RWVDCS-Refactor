using System.Reflection;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;

namespace RWVDCS.Runtime;

/// <summary>
/// 输入同步条目（对齐老系统 _inputPointSync：Input/IO 且 PointName 非空的管脚，字段序）。
/// </summary>
public sealed class InputBinding
{
    public required FieldAccessor Pin { get; init; }
    /// <summary>装载后的点名（未拆分：老系统输入侧整串按单点解析，含逗号则必然解析失败）。</summary>
    public required string PointName { get; init; }
    public required bool Reversed { get; init; }

    /// <summary>构建期解析的源点；null 或块槽 = 死绑定（走 pin 自身 buffer 兜底语义）。</summary>
    public PointSlotRef? Source;

    /// <summary>
    /// 一次性 pin buffer 默认值：老系统构造期把默认值写入 pin buffer，
    /// 死绑定兜底路径在首个周期会读到它（之后 buffer 被 live 值覆盖）。周期末清空。
    /// </summary>
    public object? PendingBufferValue;
}

/// <summary>
/// 输出回写条目（对齐老系统 _outputPointSync：Output 逗号拆分逐目标 / IO 单目标，构造序）。
/// </summary>
public sealed class OutputBinding
{
    public required FieldAccessor Pin { get; init; }
    public required string PointName { get; init; }
    public required bool Reversed { get; init; }

    /// <summary>构建期解析的目标点；null 或块槽 = 哑写（老系统解析失败/命中非 IValuable 即不写）。</summary>
    public PointSlotRef? Target;
}

/// <summary>强制/残留清理条目（对齐老系统 _outputPinSync 的强制面；buffer 回写在新系统无对应物）。</summary>
public sealed class PinSyncEntry
{
    public required FieldAccessor Pin { get; init; }
    public required string FieldName { get; init; }
}

/// <summary>
/// 功能块命令：老系统 DCSBase.Command 六阶段流水线的新实现。
/// 新系统中块 live 对象即管脚状态的唯一副本，六阶段折叠为四步：
/// 输入同步（阶段1+2）→ 块计算（阶段3）→ 强制处理（阶段4 的强制面）→ 输出回写（阶段5+6 净效果）。
/// </summary>
public sealed class BlockCommand : ICommand
{
    private readonly DpuRuntime _dpu;

    /// <summary>块状态在 DPU Arena 中的槽位（快照边界由 codec 整体 flush/load）。</summary>
    public int StateSid { get; }

    public BlockCommand(string name, string fcName, Function fc, DpuRuntime dpu, int stateSid)
    {
        Name = name;
        FcName = fcName;
        Fc = fc;
        _dpu = dpu;
        StateSid = stateSid;
        fc.Command = this;
    }

    public string Name { get; }
    public string FcName { get; }
    public Function Fc { get; }
    public IDpu Dpu => _dpu;

    internal InputBinding[] InputBindings = [];
    internal PinSyncEntry[] PinSync = [];
    internal OutputBinding[] OutputBindings = [];

    /// <summary>输入绑定（只读视图，供工具/对账检视）。</summary>
    public IReadOnlyList<InputBinding> Inputs => InputBindings;

    /// <summary>输出绑定（只读视图，供工具/对账检视）。</summary>
    public IReadOnlyList<OutputBinding> Outputs => OutputBindings;

    private Dictionary<string, (bool IsForced, object? ForceValue)>? _forceState;
    private Dictionary<string, object?>? _preForceValues;

    /// <summary>周期执行（对齐 Command.Execute；fc.Implement 本身吞异常，此处再兜一层与老系统一致）。</summary>
    public void Execute()
    {
        SyncInputPins();
        try
        {
            Fc.Implement(this);
        }
        catch
        {
            // 老系统 Execute 外层同样吞掉（Command.cs:1425-1432）
        }
        SyncPinForce();
        SyncOutputPoints();
        ClearPendingDefaults();
    }

    /// <summary>首次运行（对齐 Command.FirstRun：fc.FirstRun 异常向上传播，不吞）。</summary>
    public void FirstRun()
    {
        SyncInputPins();
        Fc.FirstRun(this);
        SyncPinForce();
        SyncOutputPoints();
        ClearPendingDefaults();
    }

    // -----------------------------------------------------------------
    // 阶段 1+2：源点 buffer → live 管脚（经 IValuable.Value，保留 LA 报警/强制副作用）
    // -----------------------------------------------------------------
    private void SyncInputPins()
    {
        var bindings = InputBindings;
        for (int i = 0; i < bindings.Length; i++)
        {
            var b = bindings[i];
            object? val = null;
            if (b.Source is { IsRealPoint: true } src)
                val = src.ReadBoxedBuffer();

            if (val == null)
            {
                // 死绑定兜底（Command.cs:2804-2836）：读 pin 自身 buffer 回设 live。
                // 新系统 pin buffer 与 live 合一：等价于用"待用默认值或自身当前值"重设一次
                //（LA 会按自身量程重算达限/报警，这是老系统每周期发生的真实副作用）。
                object pinObj = b.Pin.Read(Fc)!;
                if (pinObj is IValuable v)
                {
                    v.Value = b.PendingBufferValue ?? v.Value;
                    b.Pin.Write(Fc, pinObj);
                }
                continue;
            }

            if (b.Reversed)
                val = LegacySemantics.ReversePointValue(val);

            object pinObj2 = b.Pin.Read(Fc)!;
            if (pinObj2 is IValuable v2)
            {
                v2.Value = val;
                b.Pin.Write(Fc, pinObj2);
            }
        }
    }

    // -----------------------------------------------------------------
    // 阶段 4（强制面）：应用/清除管脚强制（对齐 Command.Execute 的 _outputPinSync 强制分支）
    // -----------------------------------------------------------------
    private void SyncPinForce()
    {
        var entries = PinSync;
        bool forceActive = _forceState != null && _forceState.Count > 0;

        for (int i = 0; i < entries.Length; i++)
        {
            var entry = entries[i];

            bool pinForced = false;
            object? forceValue = null;
            if (forceActive && _forceState!.TryGetValue(entry.FieldName, out var fs))
            {
                pinForced = fs.IsForced;
                forceValue = fs.ForceValue;
            }

            if (pinForced && forceValue != null)
            {
                object pinObj = entry.Pin.Read(Fc)!;
                if (pinObj is IPointOperation po)
                {
                    po.IsForced = 1;
                    po.SetMemberValue(forceValue, "forcevalue");
                    entry.Pin.Write(Fc, pinObj);
                }
            }
            else if (!pinForced)
            {
                // 残留强制清理：老系统每周期对所有 I/O 管脚做此检查（Command.cs:1487-1502）
                object pinObj = entry.Pin.Read(Fc)!;
                if (pinObj is IPointOperation po && po.IsForced != 0)
                {
                    po.IsForced = 0;
                    if (_preForceValues != null && _preForceValues.TryGetValue(entry.FieldName, out var preVal))
                    {
                        if (pinObj is IValuable v && preVal != null)
                            v.Value = preVal;
                        _preForceValues.Remove(entry.FieldName);
                    }
                    entry.Pin.Write(Fc, pinObj);
                }
            }
            // pinForced && forceValue == null：老系统两分支都不进，无动作
        }
    }

    // -----------------------------------------------------------------
    // 阶段 5+6（净效果）：live 管脚值 → 目标点 buffer（对齐 SyncOutputPinsToTargetPoints）
    // -----------------------------------------------------------------
    private void SyncOutputPoints()
    {
        var bindings = OutputBindings;
        for (int i = 0; i < bindings.Length; i++)
        {
            var b = bindings[i];

            object? pinObj;
            try
            {
                pinObj = b.Pin.Read(Fc);
            }
            catch
            {
                continue;
            }

            if (pinObj is not IValuable v)
                continue;

            object? val = v.Value;
            if (val == null)
                continue;

            if (b.Reversed)
                val = LegacySemantics.ReversePointValue(val);

            if (b.Target is { IsRealPoint: true } target)
            {
                if (_dpu.Iomap != null && _dpu.Iomap.IsOwned(target))
                {
                    if (_dpu.Iomap.TryGetOwnedValue(target, out var ownedValue) && ownedValue != null)
                        target.WriteBoxedBuffer(ownedValue);
                    continue;
                }

                target.WriteBoxedBuffer(val);
            }
        }
    }

    private void ClearPendingDefaults()
    {
        var bindings = InputBindings;
        for (int i = 0; i < bindings.Length; i++)
            bindings[i].PendingBufferValue = null;
    }

    // -----------------------------------------------------------------
    // 强制 API（对齐 Command.SetPinForce，Command.cs:529-554）
    // -----------------------------------------------------------------
    public void SetPinForce(string pinName, bool isForced, object forceValue)
    {
        _forceState ??= new Dictionary<string, (bool, object?)>(StringComparer.Ordinal);
        _preForceValues ??= new Dictionary<string, object?>(StringComparer.Ordinal);

        if (isForced && !_preForceValues.ContainsKey(pinName))
        {
            try
            {
                var pinFi = Fc.GetType().GetField(pinName,
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (pinFi != null && pinFi.GetValue(Fc) is IValuable v)
                    _preForceValues[pinName] = v.Value;
            }
            catch
            {
            }
        }

        _forceState[pinName] = (isForced, forceValue);
    }

    /// <summary>当前强制表（管脚名 → (是否强制, 强制值)；无强制时 null）。Web/接口检视用。</summary>
    public IReadOnlyDictionary<string, (bool IsForced, object? ForceValue)>? ForceStates => _forceState;

    /// <summary>把另一命令的强制状态搬过来（在线下装换代时保留强制，DeltaV 下装同语义）。</summary>
    internal void CopyForceStateFrom(BlockCommand other)
    {
        if (other._forceState is { Count: > 0 })
            _forceState = new Dictionary<string, (bool, object?)>(other._forceState, StringComparer.Ordinal);
        if (other._preForceValues is { Count: > 0 })
            _preForceValues = new Dictionary<string, object?>(other._preForceValues, StringComparer.Ordinal);
    }

    /// <summary>
    /// 直接写块字段（规格数/内部变量的在线修改入口，语义对齐老系统装配期 ApplyConstant：
    /// 类型匹配直写；值类型 Convert.ChangeType；string 用 ToString）。返回是否写成功。
    /// </summary>
    public bool SetField(string fieldName, object value)
    {
        var fi = Fc.GetType().GetField(fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (fi == null)
            return false;

        try
        {
            if (fi.FieldType.IsInstanceOfType(value))
            {
                fi.SetValue(Fc, value);
                return true;
            }
            // 管脚结构体（LA/LD/LP/LP32）：写其 Value（走报警/强制语义）
            if (fi.GetValue(Fc) is IValuable pin && typeof(IValuable).IsAssignableFrom(fi.FieldType))
            {
                pin.Value = value;
                fi.SetValue(Fc, pin);
                return true;
            }
            if (fi.FieldType.IsEnum)
            {
                fi.SetValue(Fc, value is string s
                    ? Enum.Parse(fi.FieldType, s, ignoreCase: true)
                    : Enum.ToObject(fi.FieldType, Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture)));
                return true;
            }
            if (fi.FieldType.IsValueType)
            {
                fi.SetValue(Fc, Convert.ChangeType(value, fi.FieldType, System.Globalization.CultureInfo.InvariantCulture));
                return true;
            }
            if (fi.FieldType == typeof(string))
            {
                fi.SetValue(Fc, value.ToString());
                return true;
            }
        }
        catch
        {
        }
        return false;
    }
}
