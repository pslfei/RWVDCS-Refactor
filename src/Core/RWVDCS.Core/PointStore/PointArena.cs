using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using RWVDCS.LowLevel;

namespace RWVDCS.Core.PointStore;

public delegate void SlotUpdater<T>(ref T value) where T : unmanaged;

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
    private static long s_nextInstanceId;

    private readonly MappedMemory _memory;
    private readonly SlotEntry[] _directory;
    private readonly FrozenDictionary<string, int> _names;
    private readonly string?[] _sidToName;
    private readonly long _dataOffset;
    private readonly int _dataLength;
    private readonly long _schemaHash;
    // 0=可访问，1=正在释放，2=已释放。访问先登记，Dispose 进入状态 1 后
    // 拒绝新访问并等待在途访问排空，避免 MMF 指针在读写中途失效。
    private int _disposeState;
    private int _activeAccesses;

    private PointArena(MappedMemory memory, SlotEntry[] directory,
        FrozenDictionary<string, int> names, string?[] sidToName, long dataOffset, long schemaHash)
    {
        InstanceId = Interlocked.Increment(ref s_nextInstanceId);
        _memory = memory;
        _directory = directory;
        _names = names;
        _sidToName = sidToName;
        _dataOffset = dataOffset;
        _dataLength = checked(memory.Length - (int)dataOffset);
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

    /// <summary>进程内单调递增的 Arena 身份，用于跨 DPU 槽键隔离；不复用对象引用哈希。</summary>
    public long InstanceId { get; }

    public long SchemaHash => _schemaHash;

    /// <summary>
    /// 为必须短期使用 <see cref="GetRef{T}"/>、<see cref="GetSlotSpan"/> 或
    /// <see cref="DataRegion"/> 的结构性操作持有访问租约；租约释放前 Dispose 不会解除 MMF 映射。
    /// </summary>
    public AccessLease AcquireAccessLease()
    {
        EnterAccess();
        return new AccessLease(this);
    }

    public sealed class AccessLease : IDisposable
    {
        private PointArena? _owner;

        internal AccessLease(PointArena owner) => _owner = owner;

        public void Dispose()
        {
            PointArena? owner = Interlocked.Exchange(ref _owner, null);
            owner?.ExitAccess();
        }
    }

    /// <summary>周期计数（随快照保存/恢复，工况语义）。</summary>
    public long CycleCount
    {
        get
        {
            EnterAccess();
            try { return MemoryMarshal.AsRef<ArenaHeader>(_memory.Span).CycleCount; }
            finally { ExitAccess(); }
        }
        set
        {
            EnterAccess();
            try { MemoryMarshal.AsRef<ArenaHeader>(_memory.Span).CycleCount = value; }
            finally { ExitAccess(); }
        }
    }

    /// <summary>点名 → SID（不区分大小写，语义对齐老系统 nameTable）。</summary>
    public bool TryGetSid(string name, out int sid) => _names.TryGetValue(name, out sid);

    public string? GetName(int sid) => _sidToName[sid];

    public int GetTypeId(int sid) => _directory[sid].TypeId;

    public int GetByteLength(int sid) => _directory[sid].ByteLength;

    /// <summary>
    /// 取槽位原始字节视图（零拷贝，等价老系统 MemorySlot）。调用方必须在视图使用期间
    /// 持有 <see cref="AcquireAccessLease"/>，或保证 Arena 尚未发布且不可能并发释放。
    /// </summary>
    public Span<byte> GetSlotSpan(int sid)
    {
        var entry = _directory[sid];
        return _memory.Span.Slice((int)(_dataOffset + entry.ByteOffset), entry.ByteLength);
    }

    /// <summary>
    /// 按类型取槽位引用（就地读写，FB 视图的底层原语）。调用方必须在 ref 使用期间
    /// 持有 <see cref="AcquireAccessLease"/>，或保证 Arena 不可能并发释放。
    /// </summary>
    public ref T GetRef<T>(int sid) where T : unmanaged
    {
        var entry = _directory[sid];
        int size = Unsafe.SizeOf<T>();
        if (size > entry.ByteLength)
            throw new ArgumentException($"SID {sid} 槽位长度 {entry.ByteLength} 小于类型 {typeof(T).Name} 的 {size} 字节。");
        return ref MemoryMarshal.AsRef<T>(_memory.Span.Slice((int)(_dataOffset + entry.ByteOffset), size));
    }

    /// <summary>在访问屏障内读取整槽结构副本，避免 ref 逃逸到 Arena 释放边界之外。</summary>
    public T ReadSlot<T>(int sid) where T : unmanaged
    {
        EnterAccess();
        try
        {
            var entry = _directory[sid];
            int size = Unsafe.SizeOf<T>();
            if (size > entry.ByteLength)
                throw new ArgumentException($"SID {sid} 槽位长度 {entry.ByteLength} 小于类型 {typeof(T).Name} 的 {size} 字节。");
            return MemoryMarshal.Read<T>(_memory.Span.Slice((int)(_dataOffset + entry.ByteOffset), size));
        }
        finally
        {
            ExitAccess();
        }
    }

    /// <summary>在访问屏障内原地更新整槽结构，不向调用方暴露 MMF ref。</summary>
    public void UpdateSlot<T>(int sid, SlotUpdater<T> updater) where T : unmanaged
    {
        ArgumentNullException.ThrowIfNull(updater);
        EnterAccess();
        try
        {
            var entry = _directory[sid];
            int size = Unsafe.SizeOf<T>();
            if (size > entry.ByteLength)
                throw new ArgumentException($"SID {sid} 槽位长度 {entry.ByteLength} 小于类型 {typeof(T).Name} 的 {size} 字节。");
            ref T target = ref MemoryMarshal.AsRef<T>(
                _memory.Span.Slice((int)(_dataOffset + entry.ByteOffset), size));
            updater(ref target);
        }
        finally
        {
            ExitAccess();
        }
    }

    /// <summary>按 (SID, 字段偏移) 读单值——FSID 读路径的核心原语。</summary>
    public T ReadField<T>(int sid, uint offset) where T : unmanaged
    {
        EnterAccess();
        try
        {
            var entry = _directory[sid];
            int size = Unsafe.SizeOf<T>();
            if (offset + (uint)size > (uint)entry.ByteLength)
                throw new ArgumentOutOfRangeException(nameof(offset),
                    $"SID {sid} 读越界：offset={offset} size={size} slot={entry.ByteLength}。");
            return MemoryMarshal.Read<T>(_memory.Span.Slice((int)(_dataOffset + entry.ByteOffset + offset), size));
        }
        finally
        {
            ExitAccess();
        }
    }

    /// <summary>按 (SID, 字段偏移) 写单值——FSID 写路径的核心原语。</summary>
    public void WriteField<T>(int sid, uint offset, in T value) where T : unmanaged
    {
        EnterAccess();
        try
        {
            var entry = _directory[sid];
            int size = Unsafe.SizeOf<T>();
            if (offset + (uint)size > (uint)entry.ByteLength)
                throw new ArgumentOutOfRangeException(nameof(offset),
                    $"SID {sid} 写越界：offset={offset} size={size} slot={entry.ByteLength}。");
            MemoryMarshal.Write(_memory.Span.Slice((int)(_dataOffset + entry.ByteOffset + offset), size), in value);
        }
        finally
        {
            ExitAccess();
        }
    }

    public T ReadField<T>(long fsid) where T : unmanaged
        => ReadField<T>(Fsid.GetSid(fsid), Fsid.GetOffset(fsid));

    public void WriteField<T>(long fsid, in T value) where T : unmanaged
        => WriteField(Fsid.GetSid(fsid), Fsid.GetOffset(fsid), in value);

    /// <summary>数据区总长（字节）。</summary>
    public int DataRegionLength => _dataLength;

    /// <summary>数据区只读视图；调用方必须在使用期间持有 <see cref="AcquireAccessLease"/>。</summary>
    public ReadOnlySpan<byte> DataRegion => _memory.Span.Slice((int)_dataOffset, DataRegionLength);

    /// <summary>整体覆写数据区（恢复基线用；长度必须一致）。</summary>
    public void RestoreDataRegion(ReadOnlySpan<byte> data)
    {
        if (data.Length != DataRegionLength)
            throw new ArgumentException($"数据区长度不符：{data.Length} ≠ {DataRegionLength}");
        EnterAccess();
        try { data.CopyTo(_memory.Span.Slice((int)_dataOffset, _dataLength)); }
        finally { ExitAccess(); }
    }

    /// <summary>在访问屏障内把槽位的指定长度复制到托管缓冲区。</summary>
    public void CopySlotTo(int sid, Span<byte> destination, int length)
    {
        EnterAccess();
        try
        {
            var entry = _directory[sid];
            if (length < 0 || length > entry.ByteLength || length > destination.Length)
                throw new ArgumentOutOfRangeException(nameof(length),
                    $"SID {sid} 复制长度非法：length={length}, slot={entry.ByteLength}, destination={destination.Length}。");
            _memory.Span.Slice((int)(_dataOffset + entry.ByteOffset), length).CopyTo(destination);
        }
        finally
        {
            ExitAccess();
        }
    }

    /// <summary>在访问屏障内把托管缓冲区写入槽位的指定长度。</summary>
    public void CopySlotFrom(int sid, ReadOnlySpan<byte> source, int length)
    {
        EnterAccess();
        try
        {
            var entry = _directory[sid];
            if (length < 0 || length > entry.ByteLength || length > source.Length)
                throw new ArgumentOutOfRangeException(nameof(length),
                    $"SID {sid} 复制长度非法：length={length}, slot={entry.ByteLength}, source={source.Length}。");
            source[..length].CopyTo(_memory.Span.Slice((int)(_dataOffset + entry.ByteOffset), length));
        }
        finally
        {
            ExitAccess();
        }
    }

    /// <summary>同时租住源、目标 Arena 后做零中间分配的跨 Arena 槽拷贝。</summary>
    public static void CopySlotBetween(PointArena source, int sourceSid,
        PointArena destination, int destinationSid, int length)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        using var sourceAccess = source.AcquireAccessLease();
        using var destinationAccess = destination.AcquireAccessLease();

        var sourceEntry = source._directory[sourceSid];
        var destinationEntry = destination._directory[destinationSid];
        if (length < 0 || length > sourceEntry.ByteLength || length > destinationEntry.ByteLength)
            throw new ArgumentOutOfRangeException(nameof(length));

        source._memory.Span
            .Slice((int)(source._dataOffset + sourceEntry.ByteOffset), length)
            .CopyTo(destination._memory.Span
                .Slice((int)(destination._dataOffset + destinationEntry.ByteOffset), length));
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
        EnterAccess();
        try
        {
            var srcEntry = _directory[srcSid];
            var dstEntry = _directory[dstSid];
            if (length < 0 || srcOffset + (uint)length > (uint)srcEntry.ByteLength
                || dstOffset + (uint)length > (uint)dstEntry.ByteLength)
                throw new ArgumentOutOfRangeException(nameof(length));

            var src = _memory.Span.Slice((int)(_dataOffset + srcEntry.ByteOffset + srcOffset), length);
            var dst = _memory.Span.Slice((int)(_dataOffset + dstEntry.ByteOffset + dstOffset), length);
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
        finally
        {
            ExitAccess();
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
        using var access = AcquireAccessLease();
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
        using var access = AcquireAccessLease();
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
    public void Flush()
    {
        using var access = AcquireAccessLease();
        _memory.Flush();
    }

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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnterAccess()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposeState) != 0, this);

        Interlocked.Increment(ref _activeAccesses);
        if (Volatile.Read(ref _disposeState) == 0)
            return;

        Interlocked.Decrement(ref _activeAccesses);
        throw new ObjectDisposedException(nameof(PointArena));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ExitAccess()
        => Interlocked.Decrement(ref _activeAccesses);

    #endregion

    public void Dispose()
    {
        int previousState = Interlocked.CompareExchange(ref _disposeState, 1, 0);
        if (previousState != 0)
        {
            var concurrentDisposeWait = new SpinWait();
            while (Volatile.Read(ref _disposeState) == 1)
                concurrentDisposeWait.SpinOnce();
            return;
        }

        var spinner = new SpinWait();
        while (Volatile.Read(ref _activeAccesses) != 0)
            spinner.SpinOnce();

        try
        {
            _memory.Dispose();
        }
        finally
        {
            Volatile.Write(ref _disposeState, 2);
        }
    }
}
