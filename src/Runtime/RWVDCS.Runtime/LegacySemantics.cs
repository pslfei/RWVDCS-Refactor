namespace RWVDCS.Runtime;

/// <summary>
/// 老系统若干"事实语义"函数的逐行移植（DCSBase\Command.cs）。
/// 对账期间必须与老系统 bit 级一致，勿做"合理化"改写。
/// </summary>
public static class LegacySemantics
{
    /// <summary>
    /// 判断点名是否为"块名.管脚名"式的 FC 引脚引用（Command.cs:2304-2317）。
    /// </summary>
    public static bool IsPinPointName(string pointName)
    {
        if (!pointName.Contains('.'))
            return false;
        if (pointName.StartsWith('!'))
            return false;
        if (pointName.EndsWith(".0", StringComparison.Ordinal))
            return false;
        return true;
    }

    /// <summary>
    /// 取反语义（Command.cs:3146-3174）：
    /// bool → 逻辑非；float 0↔1（其余 float 原样）；其他 IConvertible → 按位取反（long）。
    /// </summary>
    public static object ReversePointValue(object value)
    {
        if (value is bool b)
            return !b;

        if (value is float f)
        {
            if (f == 0f) return 1f;
            if (f == 1f) return 0f;
            return value;
        }

        if (value is IConvertible)
        {
            try
            {
                long n = Convert.ToInt64(value);
                return ~n;
            }
            catch
            {
                // 老系统吞掉转换失败并原样返回
            }
        }

        return value;
    }
}
