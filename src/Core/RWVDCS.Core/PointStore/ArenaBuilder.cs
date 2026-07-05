using System.Text;

namespace RWVDCS.Core.PointStore;

/// <summary>
/// Arena 构建器：工程编译器把点表/块表逐槽登记进来，Build 产出运行时 Arena。
/// 槽位次序即 SID 次序（与老系统"注册顺序分配 SID"语义一致）。
/// </summary>
public sealed class ArenaBuilder
{
    private readonly record struct PendingSlot(string? Name, int TypeId, int ByteLength, byte[]? Init);

    private readonly List<PendingSlot> _slots = [];
    private readonly Dictionary<string, int> _names = new(StringComparer.OrdinalIgnoreCase);
    private long _dataLength;

    public int SlotCount => _slots.Count;

    /// <summary>
    /// 登记一个原始字节槽位，返回 SID。点名不区分大小写唯一（与老系统 nameTable 的
    /// OrdinalIgnoreCase 语义一致）；重名直接抛错（工程数据问题应在编译期暴露）。
    /// </summary>
    /// <remarks>
    /// 命名刻意与 <see cref="AddSlot{T}"/> 区分：若两者同名，`AddSlot(name, id, 4)`
    /// 会被 C# 重载决议解析为 `AddSlot&lt;int&gt;(name, id, initialValue: 4)`——
    /// 长度静默变成初值，属于会导致数据损坏的隐性陷阱。
    /// </remarks>
    public int AddRawSlot(string? name, int typeId, int byteLength, ReadOnlySpan<byte> initialValue = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLength);
        if (!initialValue.IsEmpty && initialValue.Length > byteLength)
            throw new ArgumentException($"初值长度 {initialValue.Length} 超过槽位长度 {byteLength}。", nameof(initialValue));

        int sid = _slots.Count;
        if (name is not null)
        {
            if (!_names.TryAdd(name, sid))
                throw new InvalidOperationException($"点名重复：\"{name}\"（已注册为 SID {_names[name]}）。");
        }

        _slots.Add(new PendingSlot(name, typeId, byteLength, initialValue.IsEmpty ? null : initialValue.ToArray()));
        _dataLength += ArenaLayout.Align8(byteLength);
        return sid;
    }

    /// <summary>登记一个类型化槽位（长度取 sizeof(T)），返回 SID。</summary>
    public int AddSlot<T>(string? name, int typeId, in T initialValue) where T : unmanaged
    {
        var bytes = new byte[System.Runtime.CompilerServices.Unsafe.SizeOf<T>()];
        System.Runtime.CompilerServices.Unsafe.As<byte, T>(ref bytes[0]) = initialValue;
        return AddRawSlot(name, typeId, bytes.Length, bytes);
    }

    /// <summary>产出内存镜像规格（总长、各区偏移、目录与名字区内容）。</summary>
    internal BuiltImage BuildImage()
    {
        int slotCount = _slots.Count;
        var directory = new SlotEntry[slotCount];

        // 名字区
        var nameBlob = new MemoryStream();
        var nameOffsets = new int[slotCount];
        Span<byte> lenPrefix = stackalloc byte[4];
        for (int i = 0; i < slotCount; i++)
        {
            var name = _slots[i].Name;
            if (name is null)
            {
                nameOffsets[i] = -1;
                continue;
            }
            nameOffsets[i] = (int)nameBlob.Position;
            var utf8 = Encoding.UTF8.GetBytes(name);
            BitConverter.TryWriteBytes(lenPrefix, utf8.Length);
            nameBlob.Write(lenPrefix);
            nameBlob.Write(utf8);
        }

        long nameBlobOffset = ArenaLayout.DirectoryOffset + (long)slotCount * ArenaLayout.DirectoryEntrySize;
        long dataOffset = ArenaLayout.Align8(nameBlobOffset + nameBlob.Length);

        // 目录区
        int dataCursor = 0;
        for (int i = 0; i < slotCount; i++)
        {
            var s = _slots[i];
            directory[i] = new SlotEntry
            {
                TypeId = s.TypeId,
                NameOffset = nameOffsets[i],
                ByteOffset = dataCursor,
                ByteLength = s.ByteLength,
            };
            dataCursor = (int)ArenaLayout.Align8(dataCursor + s.ByteLength);
        }

        return new BuiltImage
        {
            Directory = directory,
            NameBlob = nameBlob.ToArray(),
            NameBlobOffset = nameBlobOffset,
            DataOffset = dataOffset,
            DataLength = _dataLength,
            TotalLength = dataOffset + _dataLength,
            InitValues = _slots.Select(s => s.Init).ToArray(),
            Names = _names,
        };
    }

    internal sealed class BuiltImage
    {
        public required SlotEntry[] Directory { get; init; }
        public required byte[] NameBlob { get; init; }
        public required long NameBlobOffset { get; init; }
        public required long DataOffset { get; init; }
        public required long DataLength { get; init; }
        public required long TotalLength { get; init; }
        public required byte[]?[] InitValues { get; init; }
        public required Dictionary<string, int> Names { get; init; }
    }
}
