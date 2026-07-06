using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using RWVDCS.LowLevel;

namespace RWVDCS.Core.PointStore;

/// <summary>
/// 连续内存点仓（每 DPU 一个），替代老系统 MemoryManage + PointManage 的存储职责。
/// </summary>
/// <remarks>
/// 设计要点（对应方案 §4.2）：
/// <list type="bullet">
/// <item>底层是一整片 MemoryMappedFile 镜像（可选文件持久化=工况即文件），布局见 <see cref="ArenaLayout"/>。</item>
/// <item>寻址全部用 SID(index)/offset，无任何指针外泄；点名在构建期一次性冻结为 <see cref="FrozenDictionary{TKey,TValue}"/>。</item>
/// <item>并发模型：单写者（DPU 线程）+ 多读者；外部写走引擎的安全点队列（后续里程碑）。</item>
/// <item>快照 = 整片镜像顺序落盘（原子写：.tmp + rename）；按 SchemaHash 校验工程一致性。</item>
/// </list>
/// </remarks>
public sealed class PointArena : IDisposable
{
    private readonly MappedMemory _memory;
    private readonly SlotEntry[] _directory;
    private readonly FrozenDictionary<string, int> _names;
    private readonly string?[] _sidToName;
    private readonly long _dataOffset;
    private readonly long _schemaHash;
    private bool _disposed;

    private PointArena(MappedMemory memory, SlotEntry[] directory,
        FrozenDictionary<string, int> names, string?[] sidToName, long dataOffset, long schemaHash)
    {
        _memory = memory;
        _directory = directory;
        _names = names;
        _sidToName = sidToName;
        _dataOffset = dataOffset;
        _schemaHash = schemaHash;
    }

    #region 构建与加载

    /// <summary>
    /// 从构建器创建 Arena。<paramref name="backingFile"/> 非空则为持久化模式（文件即运行镜像）。
    /// </summary>
    public static PointArena Create(ArenaBuilder builder, string? backingFile = null, string? mapName = null)
    {
        var image = builder.BuildImage();
        var memory = backingFile is null
            ? MappedMemory.CreateNew(image.TotalLength, mapName)
            : MappedMemory.CreateFromFile(backingFile, image.TotalLength, mapName);

        var span = memory.Span;
        span.Clear();

        // 目录 + 名字区
        MemoryMarshal.AsBytes<SlotEntry>(image.Directory)
            .CopyTo(span.Slice(ArenaLayout.DirectoryOffset));
        image.NameBlob.CopyTo(span.Slice((int)image.NameBlobOffset));

        long schemaHash = ComputeSchemaHash(span, image.Directory.Length, (int)image.NameBlobOffset, image.NameBlob.Length);

        // 头
        ref var header = ref MemoryMarshal.AsRef<ArenaHeader>(span);
        header.Magic = ArenaLayout.Magic;
        header.Version = ArenaLayout.Version;
        header.SlotCount = image.Directory.Length;
        header.SchemaHash = schemaHash;
        header.DataOffset = image.DataOffset;
        header.DataLength = image.DataLength;
        header.CycleCount = 0;
        header.SavedAtUnixMs = 0;

        // 初值
        for (int sid = 0; sid < image.Directory.Length; sid++)
        {
            var init = image.InitValues[sid];
            if (init is null) continue;
            var entry = image.Directory[sid];
            init.CopyTo(span.Slice((int)(image.DataOffset + entry.ByteOffset), entry.ByteLength));
        }

        var (names, sidToName) = BuildNameLookup(image.Directory, span, (int)image.NameBlobOffset);
        return new PointArena(memory, image.Directory, names, sidToName, image.DataOffset, schemaHash);
    }

    /// <summary>从快照文件完整重建 Arena（工具/冷启动路径）。</summary>
    public static PointArena LoadFrom(string snapshotPath, string? backingFile = null, string? mapName = null)
    {
        byte[] file = File.ReadAllBytes(snapshotPath);
        var fileSpan = file.AsSpan();
        var header = ReadAndValidateHeader(fileSpan, snapshotPath);

        var memory = backingFile is null
            ? MappedMemory.CreateNew(file.Length, mapName)
            : MappedMemory.CreateFromFile(backingFile, file.Length, mapName);
        fileSpan.CopyTo(memory.Span);

        var directory = ReadDirectory(memory.Span, header.SlotCount);
        int nameBlobOffset = ArenaLayout.DirectoryOffset + header.SlotCount * ArenaLayout.DirectoryEntrySize;
        var (names, sidToName) = BuildNameLookup(directory, memory.Span, nameBlobOffset);
        return new PointArena(memory, directory, names, sidToName, header.DataOffset, header.SchemaHash);
    }

    #endregion

    #region 寻址与访问

    public int SlotCount => _directory.Length;

    public long SchemaHash => _schemaHash;

    /// <summary>周期计数（随快照保存/恢复，工况语义）。</summary>
    public long CycleCount
    {
        get => MemoryMarshal.AsRef<ArenaHeader>(_memory.Span).CycleCount;
        set => MemoryMarshal.AsRef<ArenaHeader>(_memory.Span).CycleCount = value;
    }

    /// <summary>点名 → SID（不区分大小写，语义对齐老系统 nameTable）。</summary>
    public bool TryGetSid(string name, out int sid) => _names.TryGetValue(name, out sid);

    public string? GetName(int sid) => _sidToName[sid];

    public int GetTypeId(int sid) => _directory[sid].TypeId;

    public int GetByteLength(int sid) => _directory[sid].ByteLength;

    /// <summary>取槽位原始字节视图（零拷贝，等价老系统 MemorySlot）。</summary>
    public Span<byte> GetSlotSpan(int sid)
    {
        var entry = _directory[sid];
        return _memory.Span.Slice((int)(_dataOffset + entry.ByteOffset), entry.ByteLength);
    }

    /// <summary>按类型取槽位引用（就地读写，FB 视图的底层原语）。</summary>
    public ref T GetRef<T>(int sid) where T : unmanaged
    {
        var entry = _directory[sid];
        int size = Unsafe.SizeOf<T>();
        if (size > entry.ByteLength)
            throw new ArgumentException($"SID {sid} 槽位长度 {entry.ByteLength} 小于类型 {typeof(T).Name} 的 {size} 字节。");
        return ref MemoryMarshal.AsRef<T>(_memory.Span.Slice((int)(_dataOffset + entry.ByteOffset), size));
    }

    /// <summary>按 (SID, 字段偏移) 读单值——FSID 读路径的核心原语。</summary>
    public T ReadField<T>(int sid, uint offset) where T : unmanaged
    {
        var entry = _directory[sid];
        int size = Unsafe.SizeOf<T>();
        if (offset + (uint)size > (uint)entry.ByteLength)
            throw new ArgumentOutOfRangeException(nameof(offset),
                $"SID {sid} 读越界：offset={offset} size={size} slot={entry.ByteLength}。");
        return MemoryMarshal.Read<T>(_memory.Span.Slice((int)(_dataOffset + entry.ByteOffset + offset), size));
    }

    /// <summary>按 (SID, 字段偏移) 写单值——FSID 写路径的核心原语。</summary>
    public void WriteField<T>(int sid, uint offset, in T value) where T : unmanaged
    {
        var entry = _directory[sid];
        int size = Unsafe.SizeOf<T>();
        if (offset + (uint)size > (uint)entry.ByteLength)
            throw new ArgumentOutOfRangeException(nameof(offset),
                $"SID {sid} 写越界：offset={offset} size={size} slot={entry.ByteLength}。");
        MemoryMarshal.Write(_memory.Span.Slice((int)(_dataOffset + entry.ByteOffset + offset), size), in value);
    }

    public T ReadField<T>(long fsid) where T : unmanaged
        => ReadField<T>(Fsid.GetSid(fsid), Fsid.GetOffset(fsid));

    public void WriteField<T>(long fsid, in T value) where T : unmanaged
        => WriteField(Fsid.GetSid(fsid), Fsid.GetOffset(fsid), in value);

    /// <summary>数据区总长（字节）。</summary>
    public int DataRegionLength => (int)(_memory.Span.Length - _dataOffset);

    /// <summary>数据区只读视图（基线捕获/增量快照用）。</summary>
    public ReadOnlySpan<byte> DataRegion => _memory.Span.Slice((int)_dataOffset, DataRegionLength);

    /// <summary>整体覆写数据区（恢复基线用；长度必须一致）。</summary>
    public void RestoreDataRegion(ReadOnlySpan<byte> data)
    {
        if (data.Length != DataRegionLength)
            throw new ArgumentException($"数据区长度不符：{data.Length} ≠ {DataRegionLength}");
        data.CopyTo(_memory.Span.Slice((int)_dataOffset));
    }

    /// <summary>槽位在数据区内的（偏移, 长度）。</summary>
    public (int Offset, int Length) GetSlotExtent(int sid)
    {
        var entry = _directory[sid];
        return (entry.ByteOffset, entry.ByteLength);
    }

    /// <summary>槽间拷贝（等价老系统 MemoryManage.Copy 的核心路径，可选按位取反）。</summary>
    public void CopySlot(int srcSid, uint srcOffset, int dstSid, uint dstOffset, int length, bool negate = false)
    {
        var src = GetSlotSpan(srcSid).Slice((int)srcOffset, length);
        var dst = GetSlotSpan(dstSid).Slice((int)dstOffset, length);
        if (!negate)
        {
            src.CopyTo(dst);
        }
        else
        {
            for (int i = 0; i < length; i++)
                dst[i] = (byte)~src[i];
        }
    }

    #endregion

    #region 快照

    /// <summary>
    /// 保存快照：更新头部时间戳后整片镜像原子落盘（.tmp + rename）。
    /// 单写者模型下应在周期边界调用（引擎负责时机）。
    /// </summary>
    public void SaveSnapshot(string path)
    {
        var span = _memory.Span;
        ref var header = ref MemoryMarshal.AsRef<ArenaHeader>(span);
        header.SavedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        string tmp = path + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(tmp))!);
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
        {
            fs.Write(span);
            fs.Flush(flushToDisk: true);
        }
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>
    /// 就地恢复快照：仅覆盖数据区（目录/名字不动），要求 SchemaHash 一致。
    /// 这是"运行中切工况"的路径：引擎在周期边界暂停后调用。
    /// </summary>
    public void LoadSnapshotInPlace(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        Span<byte> headerBytes = stackalloc byte[ArenaLayout.HeaderSize];
        fs.ReadExactly(headerBytes);
        var header = MemoryMarshal.Read<ArenaHeader>(headerBytes);
        ValidateHeader(in header, path);
        if (header.SchemaHash != _schemaHash)
            throw new InvalidDataException(
                $"快照与当前工程不匹配（SchemaHash {header.SchemaHash:x16} ≠ {_schemaHash:x16}）：{path}");
        if (header.SlotCount != _directory.Length || header.DataOffset != _dataOffset)
            throw new InvalidDataException($"快照结构与当前 Arena 不一致：{path}");

        var span = _memory.Span;
        fs.Position = header.DataOffset;
        fs.ReadExactly(span.Slice((int)_dataOffset, (int)header.DataLength));
        MemoryMarshal.AsRef<ArenaHeader>(span).CycleCount = header.CycleCount;
    }

    /// <summary>持久化模式下把脏页刷进后备文件。</summary>
    public void Flush() => _memory.Flush();

    #endregion

    #region 内部

    private static ArenaHeader ReadAndValidateHeader(ReadOnlySpan<byte> image, string source)
    {
        if (image.Length < ArenaLayout.HeaderSize)
            throw new InvalidDataException($"快照过短：{source}");
        var header = MemoryMarshal.Read<ArenaHeader>(image);
        ValidateHeader(in header, source);
        return header;
    }

    private static void ValidateHeader(in ArenaHeader header, string source)
    {
        if (header.Magic != ArenaLayout.Magic)
            throw new InvalidDataException($"不是 Arena 镜像（magic 不符）：{source}");
        if (header.Version != ArenaLayout.Version)
            throw new InvalidDataException($"Arena 版本不支持（{header.Version}）：{source}");
    }

    private static SlotEntry[] ReadDirectory(ReadOnlySpan<byte> image, int slotCount)
        => MemoryMarshal.Cast<byte, SlotEntry>(
               image.Slice(ArenaLayout.DirectoryOffset, slotCount * ArenaLayout.DirectoryEntrySize))
           .ToArray();

    private static (FrozenDictionary<string, int>, string?[]) BuildNameLookup(
        SlotEntry[] directory, ReadOnlySpan<byte> image, int nameBlobOffset)
    {
        var dict = new Dictionary<string, int>(directory.Length, StringComparer.OrdinalIgnoreCase);
        var sidToName = new string?[directory.Length];
        for (int sid = 0; sid < directory.Length; sid++)
        {
            int nameOffset = directory[sid].NameOffset;
            if (nameOffset < 0) continue;
            int pos = nameBlobOffset + nameOffset;
            int len = BitConverter.ToInt32(image.Slice(pos, 4));
            string name = Encoding.UTF8.GetString(image.Slice(pos + 4, len));
            sidToName[sid] = name;
            dict[name] = sid;
        }
        return (dict.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase), sidToName);
    }

    /// <summary>FNV-1a 64 位，覆盖目录区 + 名字区（工程结构指纹，无第三方依赖）。</summary>
    private static long ComputeSchemaHash(ReadOnlySpan<byte> image, int slotCount, int nameBlobOffset, int nameBlobLength)
    {
        const ulong offsetBasis = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offsetBasis;

        var dir = image.Slice(ArenaLayout.DirectoryOffset, slotCount * ArenaLayout.DirectoryEntrySize);
        foreach (byte b in dir)
            hash = (hash ^ b) * prime;
        var names = image.Slice(nameBlobOffset, nameBlobLength);
        foreach (byte b in names)
            hash = (hash ^ b) * prime;

        return unchecked((long)hash);
    }

    #endregion

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _memory.Dispose();
    }
}
