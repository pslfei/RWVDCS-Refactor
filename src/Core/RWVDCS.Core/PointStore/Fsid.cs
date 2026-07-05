namespace RWVDCS.Core.PointStore;

/// <summary>
/// FSID 编解码。格式与老系统完全一致：FSID = SID &lt;&lt; 32 | offset；
/// offset == uint.MaxValue 表示"整点"（非成员域）。
/// </summary>
public static class Fsid
{
    /// <summary>老系统约定：offset 为 uint.MaxValue 时表示引用整点而非某个成员域。</summary>
    public const uint WholePointOffset = uint.MaxValue;

    public static long Make(int sid, uint offset) => ((long)sid << 32) | offset;

    public static long MakeWholePoint(int sid) => Make(sid, WholePointOffset);

    public static int GetSid(long fsid) => (int)(fsid >> 32);

    public static uint GetOffset(long fsid) => (uint)(fsid & 0xffffffff);

    public static bool IsWholePoint(long fsid) => GetOffset(fsid) == WholePointOffset;

    public static void Split(long fsid, out int sid, out uint offset)
    {
        sid = GetSid(fsid);
        offset = GetOffset(fsid);
    }
}
