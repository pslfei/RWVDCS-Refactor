using System.Runtime.InteropServices;

namespace RWVDCS.Core.Types;

/// <summary>
/// 数字量点。语义与老系统 DCSType.LD 逐字段对齐；布局固定 10 字节。
/// </summary>
/// <remarks>
/// 与老系统的差异仅有一处（有意为之）：bool 字段改为 byte 存储 + bool 属性包装，
/// 使类型满足 unmanaged 约束、布局显式可控（老系统靠 CLR 内部布局恰好也是 1 字节）。
/// 行为语义（含"Value 直写不检查强制、索引器写检查强制"这一不对称）忠实保留。
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct LD : IValuable, IPointOperation
{
    // 布局与老系统 CLR 布局一致：quality(4) istrace(1) isalarm(1) isConnected(1) forcevalue(1) isforced(1) buffer(1) = 10B
    private QualityTypes quality;
    private byte istrace;
    private byte isalarm;
    private byte isConnected;
    private byte forcevalue;
    private byte isforced;
    private byte buffer;

    public const int Size = 10;

    public LD(QualityTypes quality, bool isTrace, bool alarm, bool forceValue, byte isForced, bool value)
    {
        this.quality = quality;
        istrace = ToByte(isTrace);
        isalarm = ToByte(alarm);
        forcevalue = ToByte(forceValue);
        isforced = isForced;
        buffer = ToByte(value);
        isConnected = 0;
    }

    private static byte ToByte(bool b) => b ? (byte)1 : (byte)0;

    public QualityTypes Quality
    {
        readonly get => quality;
        set => quality = value;
    }

    public bool IsTrace
    {
        readonly get => istrace != 0;
        set => istrace = ToByte(value);
    }

    public bool IsAlarm
    {
        readonly get => isalarm != 0;
        set => isalarm = ToByte(value);
    }

    public bool Connected
    {
        readonly get => isConnected != 0;
        internal set => isConnected = ToByte(value);
    }

    /// <summary>强制值。若当前处于强制状态，写入会同步覆盖过程值（老系统语义）。</summary>
    public bool ForceValue
    {
        readonly get => forcevalue != 0;
        set
        {
            if (isforced != 0)
                buffer = ToByte(value);
            forcevalue = ToByte(value);
        }
    }

    /// <summary>是否强制（非 0 即强制）。置为强制时立即用强制值覆盖过程值（老系统语义）。</summary>
    public byte IsForced
    {
        readonly get => isforced;
        set
        {
            isforced = value;
            if (isforced != 0)
                buffer = forcevalue;
        }
    }

    /// <summary>
    /// 过程值。注意：老系统 LD.Value 的 setter 不检查强制状态（与索引器不对称），忠实保留。
    /// </summary>
    public object Value
    {
        readonly get => buffer != 0;
        set
        {
            if (value is bool b)
                buffer = ToByte(b);
            else if (value is LD ld)
                buffer = ld.buffer;
            else
                buffer = ToByte(((IConvertible)value).ToBoolean(null));
        }
    }

    /// <summary>索引器写入：强制状态下写入被强制值覆盖（老系统语义）。</summary>
    public object this[int i]
    {
        readonly get => buffer != 0;
        set
        {
            if (isforced != 0)
                buffer = forcevalue;
            else
            {
                if (value is IValuable v)
                    Value = v.Value;
                else
                    Value = value;
            }
        }
    }

    public void SetMemberValue(object value, params string[] names)
    {
        if (value == null || names == null || names.Length < 1 || names[0] == null)
            return;
        switch (names[0])
        {
            case "buffer":
                Value = value;
                break;
            case "isforced":
                IsForced = (byte)value;
                break;
            case "forcevalue":
                if (value is bool b)
                    ForceValue = b;
                else
                    ForceValue = ((IConvertible)value).ToBoolean(null);
                break;
            case "isconnected":
                Connected = (bool)value;
                break;
            case "quality":
                Quality = (QualityTypes)value;
                break;
            case "isalarm":
                IsAlarm = (bool)value;
                break;
            case "istrace":
                IsTrace = (bool)value;
                break;
        }
    }

    public readonly object? GetMemberValue(params string[] names)
    {
        if (names == null || names.Length < 1 || names[0] == null)
            return null;
        return names[0] switch
        {
            "buffer" => buffer != 0,
            "isforced" => isforced,
            "forcevalue" => forcevalue != 0,
            "isconnected" => isConnected != 0,
            "quality" => quality,
            "isalarm" => isalarm != 0,
            "istrace" => istrace != 0,
            _ => null,
        };
    }

    public static bool operator &(LD a, LD b) => (a.buffer != 0) & (b.buffer != 0);
    public static bool operator &(bool a, LD b) => a & (b.buffer != 0);
    public static bool operator &(LD a, bool b) => (a.buffer != 0) & b;

    public static bool operator |(LD a, LD b) => (a.buffer != 0) | (b.buffer != 0);
    public static bool operator |(bool a, LD b) => a | (b.buffer != 0);
    public static bool operator |(LD a, bool b) => (a.buffer != 0) | b;

    public static bool operator ^(LD a, LD b) => (a.buffer != 0) ^ (b.buffer != 0);
    public static bool operator ^(bool a, LD b) => a ^ (b.buffer != 0);
    public static bool operator ^(LD a, bool b) => (a.buffer != 0) ^ b;

    public static bool operator ==(LD a, bool b) => (a.buffer != 0) == b;
    public static bool operator !=(LD a, bool b) => (a.buffer != 0) != b;

    public static implicit operator bool(LD a) => a.buffer != 0;

    public readonly override bool Equals(object? obj) => obj is LD other && buffer == other.buffer;
    public readonly override int GetHashCode() => buffer;
    public readonly override string ToString() => (buffer != 0).ToString();
}
