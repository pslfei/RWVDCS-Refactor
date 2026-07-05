using System.Runtime.InteropServices;

namespace RWVDCS.Core.Types;

/// <summary>
/// 16 位打包数字量点。语义与老系统 DCSType.LP 逐字段对齐；布局固定 12 字节。
/// 索引器按位读写第 i 位（0–15）；越界读返回整个 buffer，越界写忽略（老系统语义）。
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct LP : IValuable, IPointOperation
{
    // 布局与老系统 CLR 布局一致（12B）：
    // quality(4) istrace(1) isalarm(1) isConnected(1) forcevalue(2) isforced(1) buffer(2)
    private QualityTypes quality;
    private byte istrace;
    private byte isalarm;
    private byte isConnected;
    private ushort forcevalue;
    private byte isforced;
    private ushort buffer;

    public const int Size = 12;

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

    public ushort ForceValue
    {
        readonly get => forcevalue;
        set
        {
            if (isforced != 0)
                buffer = value;
            forcevalue = value;
        }
    }

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

    /// <summary>按位读写。写入不检查强制状态（老系统 LP 语义：force 仅作用于 ForceValue/IsForced 路径）。</summary>
    public object this[int i]
    {
        readonly get
        {
            if (i < 0 || i > 15)
                return buffer;
            ushort temp = (ushort)(1 << i);
            temp = (ushort)(buffer & temp);
            temp >>= i;
            return temp;
        }
        set
        {
            ushort val = value is ushort u ? u : ((IConvertible)value).ToUInt16(null);
            if (i < 0 || i > 15 || val > 1)
                return;
            ushort temp = (ushort)(1 << i);
            if (val == 0)
            {
                temp = (ushort)~temp;
                buffer = (ushort)(buffer & temp);
            }
            else
            {
                buffer = (ushort)(buffer | temp);
            }
        }
    }

    public object Value
    {
        readonly get => buffer;
        set
        {
            if (value is ushort u)
                buffer = u;
            else
                buffer = ((IConvertible)value).ToUInt16(null);
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
                if (value is ushort u)
                    ForceValue = u;
                else
                    ForceValue = ((IConvertible)value).ToUInt16(null);
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
            "buffer" => buffer,
            "isforced" => isforced,
            "forcevalue" => forcevalue,
            "isconnected" => isConnected != 0,
            "quality" => quality,
            "isalarm" => isalarm != 0,
            "istrace" => istrace != 0,
            _ => null,
        };
    }
}
