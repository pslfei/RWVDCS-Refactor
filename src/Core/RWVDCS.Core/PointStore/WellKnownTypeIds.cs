namespace RWVDCS.Core.PointStore;

/// <summary>
/// 内置类型 ID。工程编译器登记槽位时使用；FB 块类型的 ID 由 schema 生成器
/// 在此基数之上分配（后续里程碑）。
/// </summary>
public static class WellKnownTypeIds
{
    /// <summary>原始字节块（无类型语义）。</summary>
    public const int Raw = 0;

    public const int LD = 1;
    public const int LA = 2;
    public const int LP = 3;
    public const int LP32 = 4;

    /// <summary>FB 块类型 ID 的起始值。</summary>
    public const int BlockBase = 1000;
}
