namespace RWVDCS.Core.Blocks;

/// <summary>
/// 功能块运行期可见的命令上下文。
/// 老系统 ICommand 面很大，但 106 个 RW 块的 Run/FirstRun 实际只用到
/// cmd.Name / cmd.Dpu.Cycle / cmd.Dpu.CycleCount，故新契约只保留这一最小面。
/// </summary>
public interface ICommand
{
    /// <summary>命令名（即功能块实例名，如 "1001$1$DCON5"）。</summary>
    string Name { get; }

    /// <summary>所属 DPU。</summary>
    IDpu Dpu { get; }
}

/// <summary>功能块可见的 DPU 上下文（最小面）。</summary>
public interface IDpu
{
    /// <summary>扫描周期（秒）。老系统 Dpu.Cycle 对块暴露的就是秒。</summary>
    float Cycle { get; }

    /// <summary>周期计数。</summary>
    uint CycleCount { get; }

    /// <summary>DPU 名（如 "DPU1001"）。</summary>
    string Name { get; }
}
