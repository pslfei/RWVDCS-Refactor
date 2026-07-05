using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using RWVDCS.Core.Blocks;

namespace RWVDCS.Runtime;

/// <summary>
/// 块字段的装箱读写委托（等价老系统 Command.GetFieldReader/GetFieldWriter 的表达式树缓存）。
/// 读：装箱字段当前值；写：拆箱赋回（要求精确类型，与 FieldInfo.SetValue 语义一致）。
/// </summary>
public sealed class FieldAccessor
{
    private static readonly ConcurrentDictionary<FieldInfo, FieldAccessor> Cache = new();

    public FieldInfo Field { get; }
    public Func<Function, object?> Read { get; }
    public Action<Function, object?> Write { get; }

    private FieldAccessor(FieldInfo field)
    {
        Field = field;

        var fcParam = Expression.Parameter(typeof(Function), "fc");
        var typedFc = Expression.Convert(fcParam, field.DeclaringType!);

        Read = Expression.Lambda<Func<Function, object?>>(
            Expression.Convert(Expression.Field(typedFc, field), typeof(object)),
            fcParam).Compile();

        var valParam = Expression.Parameter(typeof(object), "val");
        Write = Expression.Lambda<Action<Function, object?>>(
            Expression.Assign(
                Expression.Field(typedFc, field),
                Expression.Convert(valParam, field.FieldType)),
            fcParam, valParam).Compile();
    }

    public static FieldAccessor For(FieldInfo field) => Cache.GetOrAdd(field, f => new FieldAccessor(f));
}
