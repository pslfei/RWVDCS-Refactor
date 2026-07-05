using System.Runtime.InteropServices;

namespace RWVDCS.Core.Types;

/// <summary>
/// 32 位打包数字量点。语义与老系统 DCSType.LP32 逐字段对齐；布局固定 16 字节。
/// 索引器边界与老系统一致：i 合法域为 0–32（含 32，老系统即如此，i=32 时 1&lt;&lt;32 依 C# 移位规则等价 1&lt;&lt;0）。
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct LP32 : IValuable, IPointOperation
{
    // 布局与老系统 CLR 布局一致（16B）：
    // quality(4) istrace(1) isalarm(1) isConnected(1) forcevalue(4) isforced(1) buffer(4)
    private QualityTypes quality;
    private byte istrace;
    private byte isalarm;
    private byte isConnected;
    private uint forcevalue;
    private byte isforced;
    private uint buffer;

    public const int Size = 16;

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

    public uint ForceValue
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

    /// <summary>按位读写。边界判定（0–32）与移位行为与老系统逐行一致。</summary>
    public object this[int i]
    {
        readonly get
        {
            if (i < 0 || i > 32)
                return buffer;
            uint temp = (uint)(1 << i);
            temp = buffer & temp;
            temp >>= i;
            return temp;
        }
        set
        {
            uint val = value is uint u ? u : ((IConvertible)value).ToUInt32(null);
            if (i < 0 || i > 32 || val > 1)
                return;
            uint temp = (uint)(1 << i);
            if (val == 0)
            {
                temp = ~temp;
                buffer &= temp;
            }
            else
            {
                buffer |= temp;
            }
        }
    }

    public object Value
    {
        readonly get => buffer;
        set
        {
            if (value is uint u)
                buffer = u;
            else
                buffer = ((IConvertible)value).ToUInt32(null);
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
                if (value is uint u)
                    ForceValue = u;
                else
                    ForceValue = ((IConvertible)value).ToUInt32(null);
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
