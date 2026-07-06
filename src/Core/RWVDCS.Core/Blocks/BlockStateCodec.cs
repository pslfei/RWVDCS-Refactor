using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace RWVDCS.Core.Blocks;

/// <summary>
/// 块状态编解码器：把功能块实例的全部状态字段按 <see cref="BlockStateSchema"/> 布局
/// 写入/读出一段字节。只在快照边界、热重载换代、外部检视时调用（每周期扫描不经过这里）。
/// 委托用表达式树编译，百万字段级 Flush 在毫秒量级。
/// </summary>
public sealed class BlockStateCodec
{
    // ConditionalWeakTable：值（编译委托）可引用键（块类型）而不阻止可回收 ALC 卸载
    private static readonly ConditionalWeakTable<Type, BlockStateCodec> Cache = new();

    public BlockStateSchema Schema { get; }

    private readonly Action<Function, byte[], int> _flush;
    private readonly Action<Function, byte[], int> _load;

    private BlockStateCodec(BlockStateSchema schema)
    {
        Schema = schema;
        _flush = BuildFlush(schema);
        _load = BuildLoad(schema);
    }

    public static BlockStateCodec For(Type blockType)
        => Cache.GetValue(blockType, static t => new BlockStateCodec(BlockStateSchema.For(t)));

    /// <summary>把块实例状态写入缓冲区（buffer 长度必须 ≥ offset + Schema.ByteLength）。</summary>
    public void Flush(Function block, byte[] buffer, int offset) => _flush(block, buffer, offset);

    /// <summary>从缓冲区恢复块实例状态。</summary>
    public void Load(Function block, byte[] buffer, int offset) => _load(block, buffer, offset);

    private static Action<Function, byte[], int> BuildFlush(BlockStateSchema schema)
    {
        var blockParam = Expression.Parameter(typeof(Function), "block");
        var bufParam = Expression.Parameter(typeof(byte[]), "buf");
        var offParam = Expression.Parameter(typeof(int), "off");
        var typed = Expression.Variable(schema.BlockType, "b");

        var body = new List<Expression>
        {
            Expression.Assign(typed, Expression.Convert(blockParam, schema.BlockType)),
        };

        foreach (var f in schema.Fields)
        {
            var fieldExpr = Expression.Field(typed, f.Field);
            var at = Expression.Add(offParam, Expression.Constant(f.Offset));
            body.Add(f.Kind switch
            {
                StateFieldKind.Unmanaged => Expression.Call(
                    WriteMethod.MakeGenericMethod(f.Field.FieldType), bufParam, at, fieldExpr),
                StateFieldKind.FixedArray => Expression.Call(
                    WriteArrayMethod.MakeGenericMethod(f.Field.FieldType.GetElementType()!),
                    bufParam, at, fieldExpr, Expression.Constant(f.Capacity)),
                StateFieldKind.FixedString => Expression.Call(
                    WriteStringMethod, bufParam, at, fieldExpr, Expression.Constant(f.Capacity)),
                _ => throw new InvalidOperationException(),
            });
        }

        var block = Expression.Block([typed], body);
        return Expression.Lambda<Action<Function, byte[], int>>(block, blockParam, bufParam, offParam).Compile();
    }

    private static Action<Function, byte[], int> BuildLoad(BlockStateSchema schema)
    {
        var blockParam = Expression.Parameter(typeof(Function), "block");
        var bufParam = Expression.Parameter(typeof(byte[]), "buf");
        var offParam = Expression.Parameter(typeof(int), "off");
        var typed = Expression.Variable(schema.BlockType, "b");

        var body = new List<Expression>
        {
            Expression.Assign(typed, Expression.Convert(blockParam, schema.BlockType)),
        };

        foreach (var f in schema.Fields)
        {
            var fieldExpr = Expression.Field(typed, f.Field);
            var at = Expression.Add(offParam, Expression.Constant(f.Offset));
            body.Add(f.Kind switch
            {
                StateFieldKind.Unmanaged => Expression.Assign(fieldExpr, Expression.Call(
                    ReadMethod.MakeGenericMethod(f.Field.FieldType), bufParam, at)),
                StateFieldKind.FixedArray => Expression.Call(
                    ReadArrayMethod.MakeGenericMethod(f.Field.FieldType.GetElementType()!),
                    bufParam, at, fieldExpr, Expression.Constant(f.Capacity)),
                StateFieldKind.FixedString => Expression.Assign(fieldExpr, Expression.Call(
                    ReadStringMethod, bufParam, at, Expression.Constant(f.Capacity))),
                _ => throw new InvalidOperationException(),
            });
        }

        var block = Expression.Block([typed], body);
        return Expression.Lambda<Action<Function, byte[], int>>(block, blockParam, bufParam, offParam).Compile();
    }

    private static readonly MethodInfo WriteMethod = typeof(StateIo).GetMethod(nameof(StateIo.Write))!;
    private static readonly MethodInfo ReadMethod = typeof(StateIo).GetMethod(nameof(StateIo.Read))!;
    private static readonly MethodInfo WriteArrayMethod = typeof(StateIo).GetMethod(nameof(StateIo.WriteArray))!;
    private static readonly MethodInfo ReadArrayMethod = typeof(StateIo).GetMethod(nameof(StateIo.ReadArray))!;
    private static readonly MethodInfo WriteStringMethod = typeof(StateIo).GetMethod(nameof(StateIo.WriteString))!;
    private static readonly MethodInfo ReadStringMethod = typeof(StateIo).GetMethod(nameof(StateIo.ReadString))!;
}

/// <summary>状态编解码的原语（供表达式树调用，公开是实现需要，勿在业务代码直接使用）。</summary>
public static class StateIo
{
    public static void Write<T>(byte[] buf, int off, T value) where T : unmanaged
        => Unsafe.WriteUnaligned(ref buf[off], value);

    public static T Read<T>(byte[] buf, int off) where T : unmanaged
        => Unsafe.ReadUnaligned<T>(ref buf[off]);

    public static void WriteArray<T>(byte[] buf, int off, T[]? array, int capacity) where T : unmanaged
    {
        int size = Unsafe.SizeOf<T>();
        int n = Math.Min(array?.Length ?? 0, capacity);
        for (int i = 0; i < n; i++)
            Unsafe.WriteUnaligned(ref buf[off + i * size], array![i]);
        // 余量清零，保证快照字节确定性
        int used = n * size;
        int total = capacity * size;
        if (used < total)
            Array.Clear(buf, off + used, total - used);
    }

    public static void ReadArray<T>(byte[] buf, int off, T[]? array, int capacity) where T : unmanaged
    {
        if (array == null)
            return;
        int size = Unsafe.SizeOf<T>();
        int n = Math.Min(array.Length, capacity);
        for (int i = 0; i < n; i++)
            array[i] = Unsafe.ReadUnaligned<T>(ref buf[off + i * size]);
    }

    public static void WriteString(byte[] buf, int off, string? value, int capacity)
    {
        Array.Clear(buf, off, 4 + capacity);
        if (string.IsNullOrEmpty(value))
        {
            // 长度 -1 表示 null，与空串区分
            if (value == null)
                Unsafe.WriteUnaligned(ref buf[off], -1);
            return;
        }
        var span = buf.AsSpan(off + 4, capacity);
        // 超容量字符串按 UTF-8 字符边界截断（块状态里的字符串是名称/编码类元数据，截断不破坏状态语义）
        Encoding.UTF8.GetEncoder().Convert(value.AsSpan(), span, flush: true, out _, out int written, out _);
        Unsafe.WriteUnaligned(ref buf[off], written);
    }

    public static string? ReadString(byte[] buf, int off, int capacity)
    {
        int len = Unsafe.ReadUnaligned<int>(ref buf[off]);
        if (len < 0)
            return null;
        if (len == 0)
            return "";
        len = Math.Min(len, capacity);
        return Encoding.UTF8.GetString(buf, off + 4, len);
    }
}
