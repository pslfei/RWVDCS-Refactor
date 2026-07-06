using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;

namespace RWVDCS.Runtime;

/// <summary>历史站记录选项。</summary>
public sealed class HistoryOptions
{
    /// <summary>历史文件目录（每 DPU 一个 .rwhist 文件）。</summary>
    public required string Directory { get; init; }

    /// <summary>LA 死区 = 量程 ×（本百分比 / 100）；量程无效时退化为绝对死区。默认 0.1%。</summary>
    public float DeadbandPercent { get; init; } = 0.1f;

    /// <summary>绝对死区（量程无效的 LA 点用）。</summary>
    public float DeadbandAbsolute { get; init; } = 1e-6f;

    /// <summary>强制存储间隔（周期数）：即使值未越过死区，每隔 N 周期也记一笔。默认 300（200ms 周期 = 1 分钟）。</summary>
    public int MaxIntervalCycles { get; init; } = 300;

    /// <summary>缓冲刷盘间隔（周期数）。</summary>
    public int FlushEveryCycles { get; init; } = 50;
}

/// <summary>
/// 内嵌历史站记录器（方案 §4.5 的"变化才存 + 死区压缩 + 周期强制存"落地）。
/// 挂在 <see cref="ScanScheduler.AfterDpuStep"/> 上，每周期扫描 DB 点的 buffer，
/// 越过死区（或到达强制间隔）才追加记录。
/// </summary>
/// <remarks>
/// 文件格式（每 DPU 一个追加式日志）：
/// <code>
/// [Header] magic "RWH1" | int version | long createdUnixMs | int pointCount
///          pointCount × { int sid | byte kind | float deadband | int nameLen | utf8 name }
/// [Frame]  uint cycle | long unixMs | int count | count × { int sid | float rawValue }
/// </code>
/// LD 记 0/1；LP/LP32 以位模式存进 float（查询按 kind 还原）。查询走顺序扫描（工具级）。
/// </remarks>
public sealed class HistoryRecorder : IDisposable
{
    private const uint Magic = 0x31485752; // "RWH1" little-endian

    private sealed class DpuChannel
    {
        public required int[] Sids;
        public required byte[] Kinds;          // (byte)PointKind
        public required uint[] BufferOffsets;
        public required float[] Deadbands;
        public required float[] LastStored;    // LA 存值；LD/LP/LP32 存位模式
        public required uint[] LastCycle;
        public required FileStream Stream;
        public required byte[] FrameBuffer;    // 单帧最大：16 + 8×点数
        public int CyclesSinceFlush;
    }

    private readonly HistoryOptions _options;
    private readonly Dictionary<string, DpuChannel> _channels = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    /// <summary>本次会话的实际存储目录（<see cref="HistoryOptions.Directory"/> 下按启动时间建子目录，避免覆盖历史会话）。</summary>
    public string SessionDirectory { get; }

    /// <summary>最近一次记录扫描耗时（毫秒，诊断用）。</summary>
    public double LastScanMs { get; private set; }

    /// <summary>累计写入记录条数。</summary>
    public long RecordsWritten { get; private set; }

    public HistoryRecorder(DcsRuntime runtime, HistoryOptions options)
    {
        _options = options;
        SessionDirectory = Path.Combine(options.Directory, DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(SessionDirectory);

        foreach (var dpu in runtime.Dpus)
        {
            var sids = new List<int>();
            var kinds = new List<byte>();
            var offsets = new List<uint>();
            var deadbands = new List<float>();
            var names = new List<string>();

            // DB 点占据 SID 前段（装配序：DB 点 → 中间点 → 块槽）
            for (int sid = 0; sid < dpu.DbPointSlotCount; sid++)
            {
                string? name = dpu.Arena.GetName(sid);
                if (name == null || !dpu.LocalSlots.TryGetValue(name, out var slot))
                    continue;

                float deadband = 0f;
                uint offset;
                switch (slot.Kind)
                {
                    case PointKind.LA:
                        offset = PointLayout.LaBufferOffset;
                        float max = dpu.Arena.ReadField<float>(sid, PointLayout.LaMaxValueOffset);
                        float min = dpu.Arena.ReadField<float>(sid, PointLayout.LaMinValueOffset);
                        float range = max - min;
                        deadband = float.IsFinite(range) && range > 0
                            ? range * (_options.DeadbandPercent / 100f)
                            : _options.DeadbandAbsolute;
                        break;
                    case PointKind.LD:
                        offset = PointLayout.LdBufferOffset;
                        break;
                    case PointKind.LP:
                        offset = PointLayout.LpBufferOffset;
                        break;
                    case PointKind.LP32:
                        offset = PointLayout.Lp32BufferOffset;
                        break;
                    default:
                        continue;
                }

                sids.Add(sid);
                kinds.Add((byte)slot.Kind);
                offsets.Add(offset);
                deadbands.Add(deadband);
                names.Add(name);
            }

            string file = Path.Combine(SessionDirectory, SanitizeFileName(dpu.Name) + ".rwhist");
            var stream = new FileStream(file, FileMode.Create, FileAccess.Write, FileShare.Read, 1 << 20);

            var channel = new DpuChannel
            {
                Sids = sids.ToArray(),
                Kinds = kinds.ToArray(),
                BufferOffsets = offsets.ToArray(),
                Deadbands = deadbands.ToArray(),
                LastStored = new float[sids.Count],
                LastCycle = new uint[sids.Count],
                Stream = stream,
                FrameBuffer = new byte[16 + 8L * sids.Count],
            };
            Array.Fill(channel.LastStored, float.NaN); // 首周期全量记录
            _channels[dpu.Name] = channel;

            WriteHeader(channel, names);
        }
    }

    /// <summary>周期回调：扫描该 DPU 的 DB 点，越过死区/到达强制间隔则追加一帧。</summary>
    public void OnDpuStep(DpuRuntime dpu)
    {
        if (_disposed || !_channels.TryGetValue(dpu.Name, out var ch))
            return;

        long t0 = Stopwatch.GetTimestamp();
        uint cycle = dpu.CycleCount;
        var arena = dpu.Arena;
        var buf = ch.FrameBuffer;
        int count = 0;
        int pos = 16; // 帧头之后

        for (int i = 0; i < ch.Sids.Length; i++)
        {
            float raw = ch.Kinds[i] switch
            {
                (byte)PointKind.LA => arena.ReadField<float>(ch.Sids[i], ch.BufferOffsets[i]),
                (byte)PointKind.LD => arena.ReadField<byte>(ch.Sids[i], ch.BufferOffsets[i]),
                (byte)PointKind.LP => arena.ReadField<ushort>(ch.Sids[i], ch.BufferOffsets[i]),
                _ => BitConverter.UInt32BitsToSingle(arena.ReadField<uint>(ch.Sids[i], ch.BufferOffsets[i])),
            };

            float last = ch.LastStored[i];
            bool changed;
            if (float.IsNaN(last))
            {
                changed = true;
            }
            else if (ch.Kinds[i] == (byte)PointKind.LA)
            {
                changed = Math.Abs(raw - last) > ch.Deadbands[i];
            }
            else
            {
                changed = BitConverter.SingleToUInt32Bits(raw) != BitConverter.SingleToUInt32Bits(last);
            }

            if (!changed && cycle - ch.LastCycle[i] < (uint)_options.MaxIntervalCycles)
                continue;

            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(pos), ch.Sids[i]);
            BinaryPrimitives.WriteSingleLittleEndian(buf.AsSpan(pos + 4), raw);
            pos += 8;
            count++;
            ch.LastStored[i] = raw;
            ch.LastCycle[i] = cycle;
        }

        if (count > 0)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buf, cycle);
            BinaryPrimitives.WriteInt64LittleEndian(buf.AsSpan(4), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(12), count);
            ch.Stream.Write(buf, 0, pos);
            RecordsWritten += count;
        }

        if (++ch.CyclesSinceFlush >= _options.FlushEveryCycles)
        {
            ch.Stream.Flush();
            ch.CyclesSinceFlush = 0;
        }

        LastScanMs = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
    }

    /// <summary>历史文件总大小（字节）。</summary>
    public long TotalBytes()
    {
        long total = 0;
        foreach (var ch in _channels.Values)
            total += ch.Stream.Length;
        return total;
    }

    public void Flush()
    {
        foreach (var ch in _channels.Values)
            ch.Stream.Flush();
    }

    private static void WriteHeader(DpuChannel ch, List<string> names)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);
        w.Write(Magic);
        w.Write(1);
        w.Write(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        w.Write(ch.Sids.Length);
        for (int i = 0; i < ch.Sids.Length; i++)
        {
            w.Write(ch.Sids[i]);
            w.Write(ch.Kinds[i]);
            w.Write(ch.Deadbands[i]);
            var utf8 = Encoding.UTF8.GetBytes(names[i]);
            w.Write(utf8.Length);
            w.Write(utf8);
        }
        w.Flush();
        ch.Stream.Write(ms.GetBuffer(), 0, (int)ms.Length);
    }

    // =================================================================
    // 查询（顺序扫描；进程内工具级用途）
    // =================================================================

    public readonly record struct HistorySample(uint Cycle, long UnixMs, float Value);

    /// <summary>查询某 DPU 某点的历史序列（按 kind 还原原始值语义由调用方处理，这里返回 raw float）。</summary>
    public static IEnumerable<HistorySample> Query(string historyFile, string pointName)
    {
        using var fs = new FileStream(historyFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20);
        using var r = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true);

        if (r.ReadUInt32() != Magic)
            throw new InvalidDataException($"不是历史文件：{historyFile}");
        r.ReadInt32();  // version
        r.ReadInt64();  // createdUnixMs
        int pointCount = r.ReadInt32();

        int targetSid = -1;
        for (int i = 0; i < pointCount; i++)
        {
            int sid = r.ReadInt32();
            r.ReadByte();
            r.ReadSingle();
            int nameLen = r.ReadInt32();
            var nameBytes = r.ReadBytes(nameLen);
            if (targetSid < 0 && string.Equals(Encoding.UTF8.GetString(nameBytes), pointName, StringComparison.OrdinalIgnoreCase))
                targetSid = sid;
        }
        if (targetSid < 0)
            yield break;

        // 帧流
        while (fs.Position + 16 <= fs.Length)
        {
            uint cycle = r.ReadUInt32();
            long unixMs = r.ReadInt64();
            int count = r.ReadInt32();
            if (count < 0 || fs.Position + 8L * count > fs.Length)
                yield break; // 尾帧不完整（进程中断），停止

            for (int i = 0; i < count; i++)
            {
                int sid = r.ReadInt32();
                float val = r.ReadSingle();
                if (sid == targetSid)
                    yield return new HistorySample(cycle, unixMs, val);
            }
        }
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        foreach (var ch in _channels.Values)
        {
            ch.Stream.Flush();
            ch.Stream.Dispose();
        }
    }
}
