using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using RWVDCS.Core.Types;

namespace RWVDCS.Runtime;

/// <summary>
/// 点结构子字段的按名读写（PointInfo 检视/修改的底层）。
/// 直接对 Arena 槽位做字段级读写：读无副作用；写 buffer 走裸写（老系统
/// Remoting SetValue 语义），写 isforced/forcevalue 后由点自身语义在后续周期生效。
/// </summary>
public static class PointFieldAccess
{
    public sealed record PointField(string Name, string Type, object Value);

    // kind → (子字段名 → (偏移, 类型))，声明序保留
    private static readonly Dictionary<PointKind, List<(string Name, uint Offset, Type Type)>> Layouts = BuildLayouts();

    private static Dictionary<PointKind, List<(string, uint, Type)>> BuildLayouts()
    {
        var map = new Dictionary<PointKind, List<(string, uint, Type)>>();
        foreach (var (kind, type) in new[]
                 {
                     (PointKind.LA, typeof(LA)), (PointKind.LD, typeof(LD)),
                     (PointKind.LP, typeof(LP)), (PointKind.LP32, typeof(LP32)),
                 })
        {
            var list = new List<(string, uint, Type)>();
            foreach (var fi in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
                list.Add((fi.Name, (uint)Marshal.OffsetOf(type, fi.Name), fi.FieldType));
            map[kind] = list;
        }
        return map;
    }

    /// <summary>读全部子字段（枚举转 int 名，byte 布尔字段原样 0/1）。</summary>
    public static List<PointField> ReadAll(PointSlotRef slot)
    {
        var result = new List<PointField>();
        if (!slot.IsRealPoint)
            return result;

        foreach (var (name, offset, type) in Layouts[slot.Kind])
        {
            Type t = type.IsEnum ? Enum.GetUnderlyingType(type) : type;
            object value = Type.GetTypeCode(t) switch
            {
                TypeCode.Single => slot.Arena.ReadField<float>(slot.Sid, offset),
                TypeCode.Byte => slot.Arena.ReadField<byte>(slot.Sid, offset),
                TypeCode.UInt16 => slot.Arena.ReadField<ushort>(slot.Sid, offset),
                TypeCode.UInt32 => slot.Arena.ReadField<uint>(slot.Sid, offset),
                TypeCode.Int32 => slot.Arena.ReadField<int>(slot.Sid, offset),
                _ => "?",
            };
            result.Add(new PointField(name, type.IsEnum ? type.Name : t.Name, value));
        }
        return result;
    }

    /// <summary>按名直接读取单个子字段，避免高频订阅读取时构造完整字段列表。</summary>
    public static bool TryRead(PointSlotRef slot, string fieldName, out object? value, out Type? fieldType)
    {
        value = null;
        fieldType = null;
        if (!slot.IsRealPoint || !Layouts.TryGetValue(slot.Kind, out var fields))
            return false;

        foreach (var (name, offset, type) in fields)
        {
            if (!string.Equals(name, fieldName, StringComparison.OrdinalIgnoreCase))
                continue;

            fieldType = type;
            Type t = type.IsEnum ? Enum.GetUnderlyingType(type) : type;
            value = Type.GetTypeCode(t) switch
            {
                TypeCode.Single => slot.Arena.ReadField<float>(slot.Sid, offset),
                TypeCode.Byte => slot.Arena.ReadField<byte>(slot.Sid, offset),
                TypeCode.UInt16 => slot.Arena.ReadField<ushort>(slot.Sid, offset),
                TypeCode.UInt32 => slot.Arena.ReadField<uint>(slot.Sid, offset),
                TypeCode.Int32 => slot.Arena.ReadField<int>(slot.Sid, offset),
                _ => null,
            };
            return value != null;
        }
        return false;
    }

    /// <summary>按名直接写入类型化子字段；供兼容二进制通道使用，避免值转文本。</summary>
    public static bool WriteObject(PointSlotRef slot, string fieldName, object? value)
    {
        if (!slot.IsRealPoint || value == null || !Layouts.TryGetValue(slot.Kind, out var fields))
            return false;

        foreach (var (name, offset, type) in fields)
        {
            if (!string.Equals(name, fieldName, StringComparison.OrdinalIgnoreCase))
                continue;

            Type t = type.IsEnum ? Enum.GetUnderlyingType(type) : type;
            try
            {
                switch (Type.GetTypeCode(t))
                {
                    case TypeCode.Single:
                        slot.Arena.WriteField(slot.Sid, offset, Convert.ToSingle(value, CultureInfo.InvariantCulture));
                        return true;
                    case TypeCode.Byte:
                        byte b = value is bool flag ? (byte)(flag ? 1 : 0)
                            : Convert.ToByte(value, CultureInfo.InvariantCulture);
                        slot.Arena.WriteField(slot.Sid, offset, b);
                        return true;
                    case TypeCode.UInt16:
                        slot.Arena.WriteField(slot.Sid, offset, Convert.ToUInt16(value, CultureInfo.InvariantCulture));
                        return true;
                    case TypeCode.UInt32:
                        slot.Arena.WriteField(slot.Sid, offset, Convert.ToUInt32(value, CultureInfo.InvariantCulture));
                        return true;
                    case TypeCode.Int32:
                        slot.Arena.WriteField(slot.Sid, offset, Convert.ToInt32(value, CultureInfo.InvariantCulture));
                        return true;
                }
            }
            catch
            {
                return false;
            }
        }
        return false;
    }

    /// <summary>按名写单个子字段（文本值解析；未知字段/解析失败返回 false）。</summary>
    public static bool Write(PointSlotRef slot, string fieldName, string value)
    {
        if (!slot.IsRealPoint)
            return false;

        foreach (var (name, offset, type) in Layouts[slot.Kind])
        {
            if (!string.Equals(name, fieldName, StringComparison.OrdinalIgnoreCase))
                continue;

            Type t = type.IsEnum ? Enum.GetUnderlyingType(type) : type;
            try
            {
                switch (Type.GetTypeCode(t))
                {
                    case TypeCode.Single:
                        slot.Arena.WriteField(slot.Sid, offset, float.Parse(value, CultureInfo.InvariantCulture));
                        return true;
                    case TypeCode.Byte:
                        slot.Arena.WriteField(slot.Sid, offset, ParseByteish(value));
                        return true;
                    case TypeCode.UInt16:
                        slot.Arena.WriteField(slot.Sid, offset, ushort.Parse(value, CultureInfo.InvariantCulture));
                        return true;
                    case TypeCode.UInt32:
                        slot.Arena.WriteField(slot.Sid, offset, uint.Parse(value, CultureInfo.InvariantCulture));
                        return true;
                    case TypeCode.Int32:
                        slot.Arena.WriteField(slot.Sid, offset, int.Parse(value, CultureInfo.InvariantCulture));
                        return true;
                }
            }
            catch
            {
                return false;
            }
        }
        return false;
    }

    /// <summary>
    /// 点级强制：置 isforced + forcevalue，并按点语义立即覆盖 buffer（LA.IsForced setter 行为）。
    /// 解除强制只清 isforced（buffer 保持当前值，由上游继续驱动）。
    /// </summary>
    public static bool SetForce(PointSlotRef slot, bool forced, string? forceValue)
    {
        if (!slot.IsRealPoint)
            return false;

        try
        {
            switch (slot.Kind)
            {
                case PointKind.LA:
                {
                    ref var la = ref slot.Arena.GetRef<LA>(slot.Sid);
                    if (forced && forceValue != null)
                        la.ForceValue = float.Parse(forceValue, CultureInfo.InvariantCulture);
                    la.IsForced = (byte)(forced ? 1 : 0);
                    return true;
                }
                case PointKind.LD:
                {
                    ref var ld = ref slot.Arena.GetRef<LD>(slot.Sid);
                    if (forced && forceValue != null)
                        ld.ForceValue = forceValue is "1" or "true" or "True";
                    ld.IsForced = (byte)(forced ? 1 : 0);
                    return true;
                }
                case PointKind.LP:
                {
                    ref var lp = ref slot.Arena.GetRef<LP>(slot.Sid);
                    if (forced && forceValue != null)
                        lp.ForceValue = ushort.Parse(forceValue, CultureInfo.InvariantCulture);
                    lp.IsForced = (byte)(forced ? 1 : 0);
                    return true;
                }
                case PointKind.LP32:
                {
                    ref var lp32 = ref slot.Arena.GetRef<LP32>(slot.Sid);
                    if (forced && forceValue != null)
                        lp32.ForceValue = uint.Parse(forceValue, CultureInfo.InvariantCulture);
                    lp32.IsForced = (byte)(forced ? 1 : 0);
                    return true;
                }
            }
        }
        catch
        {
        }
        return false;
    }

    private static byte ParseByteish(string value) => value is "true" or "True" ? (byte)1
        : value is "false" or "False" ? (byte)0
        : byte.Parse(value, CultureInfo.InvariantCulture);
}
