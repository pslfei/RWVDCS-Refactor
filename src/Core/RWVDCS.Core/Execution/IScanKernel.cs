using RWVDCS.Core.PointStore;

namespace RWVDCS.Core.Execution;

/// <summary>
/// 扫描内核契约（PoC 版）：热加载的 FB 代码实现本接口，对 Arena 就地读写。
/// 内核必须无状态——所有状态住在 Arena 里，这是热更换代不丢状态的前提（方案 §4.3）。
/// </summary>
/// <remarks>
/// M2 里程碑会引入源生成器产出的强类型状态视图（ref struct View）替代直接传 Arena；
/// 本接口保持稳定，作为宿主与内核之间的最低公分母。
/// </remarks>
public interface IScanKernel
{
    /// <summary>执行一个扫描周期。</summary>
    void Scan(PointArena arena);
}
