namespace RWVDCS.Core.Types;

/// <summary>
/// 按成员名读写（老系统 DCSCommon.IO 的等价接口，供 API 层按 "point.member" 寻址）。
/// </summary>
public interface IMemberAccess
{
    void SetMemberValue(object value, params string[] names);
    object? GetMemberValue(params string[] names);
}

/// <summary>
/// 可取值对象（老系统 DCSCommon.IValuable 的等价接口）。
/// object 装箱语义与老系统一致；热路径不走本接口。
/// </summary>
public interface IValuable
{
    object Value { get; set; }
    object this[int i] { get; set; }
}

/// <summary>
/// 点操作（老系统 DCSCommon.IPointOperation 的等价接口）。
/// </summary>
public interface IPointOperation : IMemberAccess
{
    bool IsAlarm { get; set; }
    byte IsForced { get; set; }
    bool IsTrace { get; set; }
    QualityTypes Quality { get; set; }
}
