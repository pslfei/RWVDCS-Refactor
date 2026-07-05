namespace RWVDCS.Core.Types;

/// <summary>
/// 信号质量。与老系统 DCSCommon.QualityTypes 逐值对齐（底层为 int32，参与内存布局）。
/// </summary>
public enum QualityTypes
{
    Good = 0,
    Bad,
    Fair,
    NotGood,
}

/// <summary>
/// 变量类别。与老系统 DCSCommon.VariableClass 逐值对齐。
/// </summary>
public enum VariableClass
{
    Point = 0,
    Tag,
    Block,
    Macro,
    Basic,
}

/// <summary>
/// 管脚类别。与老系统 DCSCommon.PinTypes 逐值对齐（组态/元数据契约）。
/// </summary>
public enum PinKind
{
    None = 0,
    Input,
    Output,
    IO,
    Constant,
    Internal,
    Cascaded,
}
