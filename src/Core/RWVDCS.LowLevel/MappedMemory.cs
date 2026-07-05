using System.IO.MemoryMappedFiles;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace RWVDCS.LowLevel;

/// <summary>
/// MemoryMappedFile 的最小零拷贝封装：整个系统中唯一的 unsafe 审计点。
/// 上层（PointStore 等）只见 <see cref="Span"/>，不接触指针。
/// </summary>
/// <remarks>
/// 生命周期约束：<see cref="Span"/> 只在本实例 Dispose 前有效；
/// 持有 Span 的调用方必须保证不越过 Arena 重建/关闭边界（由 PointStore 的
/// revision 机制守护，语义对齐老系统 BufferAccessRevision 的教训）。
/// </remarks>
public sealed unsafe class MappedMemory : IDisposable
{
    private readonly MemoryMappedFile _mmf;
    private readonly MemoryMappedViewAccessor _accessor;
    private byte* _pointer;
    private readonly int _length;
    private bool _disposed;

    private MappedMemory(MemoryMappedFile mmf, int length)
    {
        _mmf = mmf;
        _length = length;
        _accessor = mmf.CreateViewAccessor(0, length, MemoryMappedFileAccess.ReadWrite);
        byte* ptr = null;
        _accessor.SafeMemoryMappedViewHandle.AcquirePointer(ref ptr);
        _pointer = ptr;
    }

    /// <summary>纯内存模式（不落盘），可选命名共享（Windows 命名映射，跨进程可 Open）。</summary>
    public static MappedMemory CreateNew(long capacity, string? mapName = null)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, int.MaxValue);
        var mmf = mapName is null
            ? MemoryMappedFile.CreateNew(null, capacity)
            : MemoryMappedFile.CreateNew(mapName, capacity);
        return new MappedMemory(mmf, (int)capacity);
    }

    /// <summary>持久化模式：文件即内存镜像（工况文件路径）。文件不存在则创建并扩展到 capacity。</summary>
    public static MappedMemory CreateFromFile(string filePath, long capacity, string? mapName = null)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(capacity, int.MaxValue);
        var mmf = MemoryMappedFile.CreateFromFile(
            filePath, FileMode.OpenOrCreate, mapName, capacity, MemoryMappedFileAccess.ReadWrite);
        return new MappedMemory(mmf, (int)capacity);
    }

    /// <summary>打开既有命名映射（监视/采集等旁路进程使用）。仅 Windows 支持命名映射。</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static MappedMemory OpenExisting(string mapName, long length)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, int.MaxValue);
        var mmf = MemoryMappedFile.OpenExisting(mapName, MemoryMappedFileRights.ReadWrite);
        return new MappedMemory(mmf, (int)length);
    }

    public int Length => _length;

    /// <summary>映射区的可写视图。Dispose 后不得再使用。</summary>
    public Span<byte> Span
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return MemoryMarshal.CreateSpan(ref Unsafe.AsRef<byte>(_pointer), _length);
        }
    }

    /// <summary>将脏页刷到后备文件（纯内存模式为空操作）。</summary>
    public void Flush() => _accessor.Flush();

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_pointer != null)
        {
            _accessor.SafeMemoryMappedViewHandle.ReleasePointer();
            _pointer = null;
        }
        _accessor.Dispose();
        _mmf.Dispose();
    }
}
