using System.Runtime.InteropServices;

namespace RWVDCS.Core.Types;

/// <summary>
/// 模拟量点。语义与老系统 DCSType.LA 逐字段对齐；布局固定 28 字节。
/// </summary>
/// <remarks>
/// 老系统的关键副作用忠实保留：
/// <list type="bullet">
/// <item>Value 写入时：强制状态下忽略入参、写强制值；随后按量程更新达限/高低报警/报警标志（直写字段，不走属性）。</item>
/// <item>IsHighalarm/IsLowalarm 属性置 true 时联动 IsAlarm=true（仅属性路径）。</item>
/// <item>ForceValue/IsForced 写入路径不触发报警计算。</item>
/// </list>
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct LA : IValuable, IPointOperation
{
    // 布局与老系统 CLR 布局一致（28B）：
    // quality(4) istrace(1) isalarm(1) forcevalue(4) isforced(1)
    // maxreached(1) minreached(1) ishighalarm(1) islowalarm(1) isConnected(1)
    // maxvalue(4) minvalue(4) buffer(4)
    private QualityTypes quality;
    private byte istrace;
    private byte isalarm;
    private float forcevalue;
    private byte isforced;
    private byte maxreached;
    private byte minreached;
    private byte ishighalarm;
    private byte islowalarm;
    private byte isConnected;
    private float maxvalue;
    private float minvalue;
    private float buffer;

    public const int Size = 28;

    public LA(QualityTypes quality, bool isTrace, bool maxReached, bool minReached,
        bool isHighalarm, bool isLowalarm, float maxValue, float minValue,
        float forceValue, byte isForced, float value)
    {
        this.quality = quality;
        istrace = ToByte(isTrace);
        isalarm = ToByte(isHighalarm | isLowalarm);
        forcevalue = forceValue;
        isforced = isForced;
        buffer = value;
        maxreached = ToByte(maxReached);
        minreached = ToByte(minReached);
        ishighalarm = ToByte(isHighalarm);
        islowalarm = ToByte(isLowalarm);
        maxvalue = maxValue;
        minvalue = minValue;
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

    /// <summary>强制值。强制状态下写入同步覆盖过程值（不触发报警计算，老系统语义）。</summary>
    public float ForceValue
    {
        readonly get => forcevalue;
        set
        {
            if (isforced != 0)
                buffer = value;
            forcevalue = value;
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

    public bool MaxReached
    {
        readonly get => maxreached != 0;
        set => maxreached = ToByte(value);
    }

    public bool MinReached
    {
        readonly get => minreached != 0;
        set => minreached = ToByte(value);
    }

    /// <summary>置 true 时联动 IsAlarm=true（仅属性路径，老系统语义）。</summary>
    public bool IsHighalarm
    {
        readonly get => ishighalarm != 0;
        set
        {
            if (value)
                IsAlarm = true;
            ishighalarm = ToByte(value);
        }
    }

    /// <summary>置 true 时联动 IsAlarm=true（仅属性路径，老系统语义）。</summary>
    public bool IsLowalarm
    {
        readonly get => islowalarm != 0;
        set
        {
            if (value)
                IsAlarm = true;
            islowalarm = ToByte(value);
        }
    }

    public bool Connected
    {
        readonly get => isConnected != 0;
        internal set => isConnected = ToByte(value);
    }

    public float MaxValue
    {
        readonly get => maxvalue;
        set => maxvalue = value;
    }

    public float MinValue
    {
        readonly get => minvalue;
        set => minvalue = value;
    }

    /// <summary>
    /// 过程值。写入语义（老系统 LA.Value setter 逐行对齐）：
    /// 强制优先 → 转 float → 量程比较更新达限/高低报警（直写字段）→ 汇总 isalarm。
    /// </summary>
    public object Value
    {
        readonly get => buffer;
        set
        {
            if (isforced != 0)
                buffer = forcevalue;
            else
            {
                if (value is float f)
                    buffer = f;
                else
                    buffer = ((IConvertible)value).ToSingle(null);
            }

            ApplyRangeSideEffects();
        }
    }

    /// <summary>老系统 Value setter 尾部的报警/达限计算（直写字段，不走属性联动）。</summary>
    private void ApplyRangeSideEffects()
    {
        if (buffer > maxvalue)
        {
            maxreached = 1;
            ishighalarm = 1;
        }
        else if (buffer == maxvalue)
        {
            maxreached = 1;
            ishighalarm = 0;
        }
        else
        {
            maxreached = 0;
            ishighalarm = 0;
        }

        if (buffer < minvalue)
        {
            minreached = 1;
            islowalarm = 1;
        }
        else if (buffer == minvalue)
        {
            minreached = 1;
            islowalarm = 0;
        }
        else
        {
            minreached = 0;
            islowalarm = 0;
        }

        isalarm = (byte)(ishighalarm | islowalarm);
    }

    /// <summary>索引器：读为裸 buffer；写等价 Value 写入（老系统语义）。</summary>
    public object this[int i]
    {
        readonly get => buffer;
        set
        {
            if (value is IValuable v)
                Value = v.Value;
            else
                Value = value;
        }
    }

    public void SetMemberValue(object value, params string[] names)
    {
        if (value == null || names == null || names.Length < 1 || names[0] == null)
            return;
        // 老系统 LA.SetMemberValue 用 ToLower()（LD/LP 为精确大小写），忠实保留差异。
        switch (names[0].ToLowerInvariant())
        {
            case "value":
                Value = value;
                break;
            case "buffer":
                Value = value;
                break;
            case "isforced":
                IsForced = (byte)value;
                break;
            case "forcevalue":
                if (value is float f)
                    ForceValue = f;
                else
                    ForceValue = ((IConvertible)value).ToSingle(null);
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
            case "maxvalue":
                MaxValue = (float)value;
                break;
            case "minvalue":
                MinValue = (float)value;
                break;
            case "ishighalarm":
                IsHighalarm = (bool)value;
                break;
            case "islowalarm":
                IsLowalarm = (bool)value;
                break;
            case "maxreached":
                MaxReached = (bool)value;
                break;
            case "minreached":
                MinReached = (bool)value;
                break;
        }
    }

    public readonly object? GetMemberValue(params string[] names)
    {
        if (names == null || names.Length < 1 || names[0] == null)
            return null;
        // 老系统 LA.GetMemberValue 按精确大小写匹配（与 setter 不对称），忠实保留。
        return names[0] switch
        {
            "buffer" => buffer,
            "isforced" => isforced,
            "forcevalue" => forcevalue,
            "isconnected" => isConnected != 0,
            "quality" => quality,
            "isalarm" => isalarm != 0,
            "istrace" => istrace != 0,
            "maxvalue" => maxvalue,
            "minvalue" => minvalue,
            "ishighalarm" => ishighalarm != 0,
            "islowalarm" => islowalarm != 0,
            "maxreached" => maxreached != 0,
            "minreached" => minreached != 0,
            _ => null,
        };
    }

    #region 运算符（与老系统 LA 完全一致）

    public static float operator +(LA a, LA b) => a.buffer + b.buffer;
    public static float operator +(LA a, float b) => a.buffer + b;
    public static float operator +(float a, LA b) => a + b.buffer;

    public static float operator -(LA a, LA b) => a.buffer - b.buffer;
    public static float operator -(LA a, float b) => a.buffer - b;
    public static float operator -(float a, LA b) => a - b.buffer;

    public static float operator *(LA a, LA b) => a.buffer * b.buffer;
    public static float operator *(LA a, float b) => a.buffer * b;
    public static float operator *(float a, LA b) => a * b.buffer;

    public static float operator /(LA a, LA b) => a.buffer / b.buffer;
    public static float operator /(LA a, float b) => a.buffer / b;
    public static float operator /(float a, LA b) => a / b.buffer;

    public static bool operator >(LA a, LA b) => a.buffer > b.buffer;
    public static bool operator >(LA a, float b) => a.buffer > b;
    public static bool operator >(float a, LA b) => a > b.buffer;

    public static bool operator >=(LA a, LA b) => a.buffer >= b.buffer;
    public static bool operator >=(LA a, float b) => a.buffer >= b;
    public static bool operator >=(float a, LA b) => a >= b.buffer;

    public static bool operator <(LA a, LA b) => a.buffer < b.buffer;
    public static bool operator <(LA a, float b) => a.buffer < b;
    public static bool operator <(float a, LA b) => a < b.buffer;

    public static bool operator <=(LA a, LA b) => a.buffer <= b.buffer;
    public static bool operator <=(LA a, float b) => a.buffer <= b;
    public static bool operator <=(float a, LA b) => a <= b.buffer;

    public static bool operator ==(LA a, LA b) => a.buffer == b.buffer;
    public static bool operator ==(LA a, float b) => a.buffer == b;
    public static bool operator ==(float a, LA b) => a == b.buffer;

    public static bool operator !=(LA a, LA b) => a.buffer != b.buffer;
    public static bool operator !=(LA a, float b) => a.buffer != b;
    public static bool operator !=(float a, LA b) => a != b.buffer;

    public static float operator &(LA a, LA b) => Convert.ToInt32(a.buffer) & Convert.ToInt32(b.buffer);
    public static float operator &(float a, LA b) => Convert.ToInt32(a) & Convert.ToInt32(b.buffer);
    public static float operator &(LA a, float b) => Convert.ToInt32(a.buffer) & Convert.ToInt32(b);

    public static float operator ^(LA a, LA b) => Convert.ToInt32(a.buffer) ^ Convert.ToInt32(b.buffer);
    public static float operator ^(LA a, float b) => Convert.ToInt32(a.buffer) ^ Convert.ToInt32(b);
    public static float operator ^(float a, LA b) => Convert.ToInt32(a) ^ Convert.ToInt32(b.buffer);

    public static float operator |(LA a, LA b) => Convert.ToInt32(a.buffer) | Convert.ToInt32(b.buffer);
    public static float operator |(LA a, float b) => Convert.ToInt32(a.buffer) | Convert.ToInt32(b);
    public static float operator |(float a, LA b) => Convert.ToInt32(a) | Convert.ToInt32(b.buffer);

    public static float operator %(LA a, float b) => a.buffer % b;
    public static float operator %(float a, LA b) => a % b.buffer;
    public static float operator %(LA a, LA b) => a.buffer % b.buffer;

    public static float operator !(LA a) => a.buffer != 0 ? 0f : 1f;
    public static float operator ~(LA a) => ~Convert.ToInt32(a.buffer);

    public static bool operator true(LA a) => a.buffer != 0;
    public static bool operator false(LA a) => a.buffer == 0;

    public static implicit operator float(LA a) => a.buffer;

    #endregion

    public readonly override bool Equals(object? obj) => obj is LA other && buffer == other.buffer;
    public readonly override int GetHashCode() => buffer.GetHashCode();
    public readonly override string ToString() => buffer.ToString();
}
