using RWVDCS.Core.Blocks;
using RWVDCS.Core.PointStore;

namespace RWVDCS.Runtime;

/// <summary>
/// 单控制器运行时（老系统 DCS.Dpu 的新实现，仅保留扫描/存取职责，线程调度由 DcsRuntime 负责）。
/// </summary>
public sealed class DpuRuntime : IDpu, IDisposable
{
    private uint _cycleMilliseconds = 200; // 老系统默认 200ms（Dpu.cs:286）

    public DpuRuntime(int controllerId, string name, PointArena arena, IReadOnlyDictionary<string, PointSlotRef> localSlots)
    {
        ControllerId = controllerId;
        Name = name;
        Arena = arena;
        LocalSlots = localSlots;
        CycleCount = 1; // 老系统初值 1（针对延时块判断条件，Dpu.cs:287）
    }

    public int ControllerId { get; }

    public string Name { get; }

    /// <summary>本 DPU 的点仓（DB 点 + 中间 pin-point + 块状态槽）。</summary>
    public PointArena Arena { get; }

    /// <summary>本 DPU 名字表：点名/块名 → 槽引用（大小写不敏感，与 Arena 名字表同源）。</summary>
    public IReadOnlyDictionary<string, PointSlotRef> LocalSlots { get; }

    /// <summary>命令表（Cld_FCBlock 装载序 = 老系统执行序）。</summary>
    public List<BlockCommand> Commands { get; } = [];

    /// <summary>DB 点槽数量（SID [0, N) 为工程库点；其后是中间点与块槽）。历史站按此圈定记录范围。</summary>
    public int DbPointSlotCount { get; internal set; }

    /// <summary>扫描周期（秒）。老系统 get=cycle/1000，set 时小于 0.01 忽略（Dpu.cs:204-215）。</summary>
    public float Cycle
    {
        get => (float)_cycleMilliseconds / 1000;
        set
        {
            if (value < 0.01)
                return;
            _cycleMilliseconds = (uint)(value * 1000);
        }
    }

    public uint CycleCount { get; set; }

    /// <summary>首次运行：顺序 FirstRun 所有命令（块 FirstRun 异常向上传播，与老系统一致）。</summary>
    public void FirstRun()
    {
        var commands = Commands;
        for (int i = 0; i < commands.Count; i++)
            commands[i].FirstRun();
    }

    /// <summary>单步：顺序执行所有命令一个周期，随后周期数 +1（对齐 Dpu.Implement 的 Step 分支）。</summary>
    public void Step()
    {
        var commands = Commands;
        for (int i = 0; i < commands.Count; i++)
            commands[i].Execute();
        CycleCount++;
    }

    public BlockCommand? FindCommand(string blockName)
    {
        foreach (var cmd in Commands)
            if (string.Equals(cmd.Name, blockName, StringComparison.OrdinalIgnoreCase))
                return cmd;
        return null;
    }

    public void Dispose() => Arena.Dispose();
}
