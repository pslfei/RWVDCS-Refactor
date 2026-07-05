using System.Runtime.InteropServices;
using RWVDCS.Core.PointStore;
using RWVDCS.Core.Types;

namespace RWVDCS.Runtime;

/// <summary>点槽类别（决定 buffer 子字段的类型与偏移）。</summary>
public enum PointKind : byte
{
    LA = 0,
    LD = 1,
    LP = 2,
    LP32 = 3,

    /// <summary>块状态槽（按点名解析到块时产生"哑绑定"，对齐老系统 rtd[名字] 命中块 SID 的行为）。</summary>
    Block = 100,
}

/// <summary>
/// 点类型的布局常量与 buffer 子字段的装箱读写。
/// 老系统同步路径读写的是点的 buffer 子字段（LA→float / LD→bool / LP→ushort / LP32→uint），
/// 这里的装箱类型与转换规则对齐 PointManage.TryGetVariableFast / WriteValueFastNoLock。
/// </summary>
public static class PointLayout
{
    public static readonly uint LaBufferOffset = (uint)Marshal.OffsetOf<LA>("buffer");
    public static readonly uint LdBufferOffset = (uint)Marshal.OffsetOf<LD>("buffer");
    public static readonly uint LpBufferOffset = (uint)Marshal.OffsetOf<LP>("buffer");
    public static readonly uint Lp32BufferOffset = (uint)Marshal.OffsetOf<LP32>("buffer");

    public static readonly uint LaMaxValueOffset = (uint)Marshal.OffsetOf<LA>("maxvalue");
    public static readonly uint LaMinValueOffset = (uint)Marshal.OffsetOf<LA>("minvalue");

    public static PointKind KindFromDataType(string dataType) => dataType switch
    {
        "LA" => PointKind.LA,
        "LD" => PointKind.LD,
        "LP" => PointKind.LP,
        "LP32" => PointKind.LP32,
        _ => throw new ArgumentOutOfRangeException(nameof(dataType), dataType, "未知点类型"),
    };

    public static int SizeOf(PointKind kind) => kind switch
    {
        PointKind.LA => LA.Size,
        PointKind.LD => LD.Size,
        PointKind.LP => LP.Size,
        PointKind.LP32 => LP32.Size,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static int TypeIdOf(PointKind kind) => kind switch
    {
        PointKind.LA => WellKnownTypeIds.LA,
        PointKind.LD => WellKnownTypeIds.LD,
        PointKind.LP => WellKnownTypeIds.LP,
        PointKind.LP32 => WellKnownTypeIds.LP32,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}

/// <summary>
/// 已解析的点槽引用：绑定在构建期一次性解析，运行期零查找。
/// </summary>
public readonly struct PointSlotRef(PointArena arena, int sid, PointKind kind)
{
    public PointArena Arena { get; } = arena;
    public int Sid { get; } = sid;
    public PointKind Kind { get; } = kind;

    /// <summary>是否为可读写 buffer 的真点（块槽只是名字占位，读写均为哑操作）。</summary>
    public bool IsRealPoint => Kind != PointKind.Block;

    /// <summary>
    /// 读点 buffer 子字段（装箱），对齐老系统 TryGetVariableFast 的装箱类型：
    /// LA→float、LD→bool、LP→ushort、LP32→uint。块槽返回 null（老系统 rtd[sid] 取到 Function 非 IValuable → null）。
    /// </summary>
    public object? ReadBoxedBuffer() => Kind switch
    {
        PointKind.LA => Arena.ReadField<float>(Sid, PointLayout.LaBufferOffset),
        PointKind.LD => Arena.ReadField<byte>(Sid, PointLayout.LdBufferOffset) != 0,
        PointKind.LP => Arena.ReadField<ushort>(Sid, PointLayout.LpBufferOffset),
        PointKind.LP32 => Arena.ReadField<uint>(Sid, PointLayout.Lp32BufferOffset),
        _ => null,
    };

    /// <summary>
    /// 写点 buffer 子字段。类型转换对齐老系统 WriteValueFastNoLock（Convert.ToXxx），
    /// 转换失败静默忽略（老系统 catch 后放弃写入）。
    /// </summary>
    public void WriteBoxedBuffer(object value)
    {
        try
        {
            switch (Kind)
            {
                case PointKind.LA:
                    Arena.WriteField(Sid, PointLayout.LaBufferOffset,
                        value is float f ? f : Convert.ToSingle(value));
                    break;
                case PointKind.LD:
                    Arena.WriteField(Sid, PointLayout.LdBufferOffset,
                        (byte)((value is bool b ? b : Convert.ToBoolean(value)) ? 1 : 0));
                    break;
                case PointKind.LP:
                    Arena.WriteField(Sid, PointLayout.LpBufferOffset,
                        value is ushort us ? us : Convert.ToUInt16(value));
                    break;
                case PointKind.LP32:
                    Arena.WriteField(Sid, PointLayout.Lp32BufferOffset,
                        value is uint u ? u : Convert.ToUInt32(value));
                    break;
            }
        }
        catch
        {
            // 类型转换失败（溢出/无效转换）：老系统同样放弃本次写入
        }
    }
}
