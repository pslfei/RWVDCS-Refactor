using System.Runtime.InteropServices;

namespace RWVDCS.Core.PointStore;

/// <summary>
/// Arena 文件/内存镜像布局常量与头结构。
/// 布局：Header(64) | SlotDirectory(16×N) | NameBlob(8 对齐) | Data。
/// 该布局同时是运行时内存镜像与快照文件格式（快照=整片镜像落盘）。
/// </summary>
internal static class ArenaLayout
{
    /// <summary>"RWNXARN1" 的小端 int64。</summary>
    public const long Magic = 0x314E_5241_584E_5752;

    public const int Version = 1;
    public const int HeaderSize = 64;
    public const int DirectoryEntrySize = 16;

    public static int DirectoryOffset => HeaderSize;

    public static long Align8(long value) => (value + 7) & ~7L;
}

/// <summary>Arena 头（64 字节，位于镜像起始）。</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = ArenaLayout.HeaderSize)]
internal struct ArenaHeader
{
    public long Magic;
    public int Version;
    public int SlotCount;
    /// <summary>目录+名字区的指纹；快照按此校验"同一工程编译产物"。</summary>
    public long SchemaHash;
    public long DataOffset;
    public long DataLength;
    /// <summary>保存时的周期计数（工况语义）。</summary>
    public long CycleCount;
    /// <summary>保存时刻（Unix 毫秒）。</summary>
    public long SavedAtUnixMs;
    public long Reserved;
}

/// <summary>槽位目录项（16 字节）。SID 即目录下标。</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1, Size = ArenaLayout.DirectoryEntrySize)]
internal struct SlotEntry
{
    /// <summary>类型标识（TypeRegistry 中的 ID；0 保留为原始字节块）。</summary>
    public int TypeId;
    /// <summary>名字在 NameBlob 内的偏移（-1 表示匿名槽）。</summary>
    public int NameOffset;
    /// <summary>数据在 Data 区内的偏移。</summary>
    public int ByteOffset;
    /// <summary>数据长度（字节）。</summary>
    public int ByteLength;
}
