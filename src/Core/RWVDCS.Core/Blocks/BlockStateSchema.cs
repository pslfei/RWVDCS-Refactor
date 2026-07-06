using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RWVDCS.Core.Types;

namespace RWVDCS.Core.Blocks;

/// <summary>块状态字段的存储种类。</summary>
public enum StateFieldKind : byte
{
    /// <summary>非托管定长标量/结构（含 LD/LA/LP/LP32、float、bool 等）。</summary>
    Unmanaged = 0,
    /// <summary>定容原生数组（容量取 MarshalAs SizeConst，否则取模板实例的数组长度）。</summary>
    FixedArray = 1,
    /// <summary>定容 UTF-8 字符串（容量取 MarshalAs SizeConst，否则 64 字节）。</summary>
    FixedString = 2,
}

/// <summary>
/// 单个状态字段的描述：名称、CLR 字段、在块状态槽内的偏移与占用字节数。
/// </summary>
public sealed record BlockStateField(
    FieldInfo Field,
    StateFieldKind Kind,
    PinTypes PinType,
    int Offset,
    int ByteLength,
    int ElementSize,
    int Capacity)
{
    public string Name => Field.Name;
}

/// <summary>
/// 块状态布局：把一个功能块类型的全部实例字段（管脚 + 内部状态）映射为
/// 一段连续字节（Arena 槽）的确定性布局。字段顺序 = 基类先、按声明顺序，
/// 与老系统 CLR Sequential 布局的遍历序一致；但偏移是新系统自己的紧凑布局，
/// 不追求与老系统字节兼容（对账走"名字→值"而非字节比对）。
/// </summary>
public sealed class BlockStateSchema
{
    // ConditionalWeakTable：热更换代后旧块类型（可回收 ALC 中）不被缓存钉住，ALC 可真卸载
    private static readonly ConditionalWeakTable<Type, BlockStateSchema> Cache = new();

    public Type BlockType { get; }
    public IReadOnlyList<BlockStateField> Fields { get; }
    public int ByteLength { get; }

    /// <summary>
    /// 布局指纹（FNV-1a：字段名+种类+偏移+长度）。两个类型布局哈希相同 ⇒ 状态槽字节可直拷；
    /// 不同 ⇒ 跨版本快照需走字段级按名转换。
    /// </summary>
    public long LayoutHash { get; }

    private readonly Dictionary<string, BlockStateField> _byName;

    private BlockStateSchema(Type blockType)
    {
        BlockType = blockType;
        var fields = new List<BlockStateField>();
        _byName = new Dictionary<string, BlockStateField>(StringComparer.Ordinal);

        // 模板实例用于取数组初始容量（如 DELAY.buffer = new float[30]）
        object template = Activator.CreateInstance(blockType)!;

        int offset = 0;
        foreach (var fi in EnumerateInstanceFieldsBaseFirst(blockType))
        {
            var entry = Describe(fi, template, offset);
            if (entry == null)
                continue;
            fields.Add(entry);
            _byName[entry.Name] = entry;
            offset += entry.ByteLength;
        }

        Fields = fields;
        ByteLength = offset;
        LayoutHash = ComputeLayoutHash(fields);
    }

    private static long ComputeLayoutHash(List<BlockStateField> fields)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offsetBasis;
        void Mix(int v)
        {
            for (int i = 0; i < 4; i++)
                hash = (hash ^ (byte)(v >> (i * 8))) * prime;
        }
        foreach (var f in fields)
        {
            foreach (char c in f.Name)
                hash = (hash ^ (byte)c) * prime;
            Mix((int)f.Kind);
            Mix(f.Offset);
            Mix(f.ByteLength);
        }
        return unchecked((long)hash);
    }

    public static BlockStateSchema For(Type blockType)
        => Cache.GetValue(blockType, static t => new BlockStateSchema(t));

    public bool TryGetField(string name, out BlockStateField field) => _byName.TryGetValue(name, out field!);

    /// <summary>基类字段在前（Function.FcName/FcCode/runable），再按声明顺序列出本类字段。</summary>
    internal static IEnumerable<FieldInfo> EnumerateInstanceFieldsBaseFirst(Type type)
    {
        var chain = new List<Type>();
        for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            chain.Add(t);
        chain.Reverse();

        foreach (var t in chain)
        {
            foreach (var fi in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (fi.IsLiteral)
                    continue;
                // Function 基类的 command 引用不是状态
                if (fi.FieldType == typeof(ICommand) || typeof(ICommand).IsAssignableFrom(fi.FieldType))
                    continue;
                yield return fi;
            }
        }
    }

    private static BlockStateField? Describe(FieldInfo fi, object template, int offset)
    {
        var pinType = fi.GetCustomAttribute<PinTypeAttribute>()?.PinType ?? PinTypes.None;
        var ft = fi.FieldType;

        if (ft == typeof(string))
        {
            int cap = fi.GetCustomAttribute<MarshalAsAttribute>()?.SizeConst ?? 64;
            return new BlockStateField(fi, StateFieldKind.FixedString, pinType, offset, 4 + cap, 1, cap);
        }

        if (ft.IsArray)
        {
            var elem = ft.GetElementType()!;
            if (!IsSupportedUnmanaged(elem))
                return null;
            int elemSize = SizeOfUnmanaged(elem);
            int cap = fi.GetCustomAttribute<MarshalAsAttribute>()?.SizeConst
                      ?? (fi.GetValue(template) as Array)?.Length
                      ?? 0;
            if (cap <= 0)
                return null;
            return new BlockStateField(fi, StateFieldKind.FixedArray, pinType, offset, elemSize * cap, elemSize, cap);
        }

        if (IsSupportedUnmanaged(ft))
        {
            int size = SizeOfUnmanaged(ft);
            return new BlockStateField(fi, StateFieldKind.Unmanaged, pinType, offset, size, size, 1);
        }

        return null;
    }

    internal static bool IsSupportedUnmanaged(Type t)
    {
        if (t.IsEnum)
            return true;
        if (t == typeof(LD) || t == typeof(LA) || t == typeof(LP) || t == typeof(LP32))
            return true;
        return Type.GetTypeCode(t) switch
        {
            TypeCode.Boolean or TypeCode.Byte or TypeCode.SByte or TypeCode.Char
                or TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Int32 or TypeCode.UInt32
                or TypeCode.Int64 or TypeCode.UInt64 or TypeCode.Single or TypeCode.Double => true,
            _ => false,
        };
    }

    internal static int SizeOfUnmanaged(Type t)
    {
        if (t.IsEnum)
            t = Enum.GetUnderlyingType(t);
        if (t == typeof(LD)) return Unsafe.SizeOf<LD>();
        if (t == typeof(LA)) return Unsafe.SizeOf<LA>();
        if (t == typeof(LP)) return Unsafe.SizeOf<LP>();
        if (t == typeof(LP32)) return Unsafe.SizeOf<LP32>();
        if (t == typeof(bool)) return 1;
        if (t == typeof(char)) return 2;
        return Marshal.SizeOf(t);
    }
}
