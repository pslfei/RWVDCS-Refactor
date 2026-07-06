namespace RWVDCS.Runtime;

/// <summary>日志级别。</summary>
public enum LogLevel
{
    Info = 0,
    Warn = 1,
    Error = 2,
}

/// <summary>单条运行日志。</summary>
public sealed record LogEntry(long Seq, DateTime TimeUtc, LogLevel Level, string Source, string Message);

/// <summary>
/// 运行日志环形缓冲：宿主各子系统（装载/运行控制/工况快照/下装/热更/接口）统一入口，
/// Web 界面日志窗口经 REST 拉取 + SSE 推送消费。
/// </summary>
public sealed class LogBuffer
{
    private readonly Lock _gate = new();
    private readonly LogEntry?[] _ring;
    private long _seq;

    /// <summary>新日志事件（SSE 推送用；在调用 Append 的线程上触发，订阅方自行排队）。</summary>
    public event Action<LogEntry>? Appended;

    /// <summary>是否同时镜像到控制台。</summary>
    public bool MirrorToConsole { get; set; } = true;

    public LogBuffer(int capacity = 2000) => _ring = new LogEntry?[capacity];

    public LogEntry Append(LogLevel level, string source, string message)
    {
        LogEntry entry;
        lock (_gate)
        {
            entry = new LogEntry(++_seq, DateTime.UtcNow, level, source, message);
            _ring[_seq % _ring.Length] = entry;
        }

        if (MirrorToConsole)
        {
            string tag = level switch { LogLevel.Warn => "警告", LogLevel.Error => "错误", _ => "信息" };
            Console.WriteLine($"[{entry.TimeUtc.ToLocalTime():HH:mm:ss}] [{tag}] [{source}] {message}");
        }

        Appended?.Invoke(entry);
        return entry;
    }

    public void Info(string source, string message) => Append(LogLevel.Info, source, message);
    public void Warn(string source, string message) => Append(LogLevel.Warn, source, message);
    public void Error(string source, string message) => Append(LogLevel.Error, source, message);

    /// <summary>取 seq 大于 afterSeq 的日志（最多 max 条，时间序，超量时保留最新的）。</summary>
    public List<LogEntry> Tail(long afterSeq = 0, int max = 500)
    {
        lock (_gate)
        {
            long start = Math.Max(afterSeq + 1, _seq - _ring.Length + 1);
            if (max > 0)
                start = Math.Max(start, _seq - max + 1);

            var result = new List<LogEntry>();
            for (long s = Math.Max(start, 1); s <= _seq; s++)
            {
                var e = _ring[s % _ring.Length];
                if (e != null && e.Seq == s)
                    result.Add(e);
            }
            return result;
        }
    }

    public long LastSeq
    {
        get
        {
            lock (_gate)
                return _seq;
        }
    }
}
