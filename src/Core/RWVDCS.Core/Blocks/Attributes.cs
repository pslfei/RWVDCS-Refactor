using RWVDCS.Core.Types;

namespace RWVDCS.Core.Blocks;

// 与老系统 DCSCommon 的特性一一对应，保证功能块源码原样移植后语义不变。

/// <summary>功能码名称（老系统 FCNameAttribute）。</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class FCNameAttribute(string fcName) : Attribute
{
    public string FCName { get; } = fcName;
    public override string ToString() => FCName;
}

/// <summary>功能码显示描述（老系统 FCDisplayAttribute）。</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class FCDisplayAttribute(string displayString) : Attribute
{
    public string Display { get; } = displayString;
    public override string ToString() => Display;
}

/// <summary>管脚类型（老系统 PinTypeAttribute）。</summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class PinTypeAttribute(PinTypes pinType) : Attribute
{
    public PinTypes PinType { get; } = pinType;
    public override string ToString() => PinType.ToString();
}

/// <summary>管脚标识（老系统 PinAttribute）。</summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class PinAttribute : Attribute;

/// <summary>管脚显示描述（老系统 PinDisplayAttribute）。</summary>
[AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
public sealed class PinDisplayAttribute(string displayString) : Attribute
{
    public string Display { get; } = displayString;
    public override string ToString() => Display;
}

/// <summary>变量类别（老系统 VariableClassAttribute）。</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, AllowMultiple = false, Inherited = true)]
public sealed class VariableClassAttribute(VariableClass variableClass) : Attribute
{
    public VariableClass VariableClass { get; } = variableClass;
    public override string ToString() => VariableClass.ToString();
}
