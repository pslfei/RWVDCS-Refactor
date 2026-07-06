using System.Diagnostics;
using System.Globalization;
using System.Text;
using RWVDCS.Runtime;

namespace RWVDCS.Host;

/// <summary>
/// 进程级稳定性监控：周期耗时（来自调度器）+ GC/内存/线程指标，
/// 定期输出控制台一行摘要，可选落 CSV 供长跑趋势分析（内存泄漏/抖动检测）。
/// </summary>
internal sealed class StabilityMonitor : IDisposable
{
    private readonly ScanScheduler? _scheduler;
    private readonly HistoryRecorder? _history;
    private readonly string? _csvFile;
    private readonly Stopwatch _uptime = Stopwatch.StartNew();
    private readonly int[] _lastGcCounts = new int[3];
    private long _lastAllocated;
    private long _lastCycles;
    private double _lastSampleSeconds;
    private Timer? _timer;

    public StabilityMonitor(ScanScheduler? scheduler, HistoryRecorder? history, string? csvFile)
    {
        _scheduler = scheduler;
        _history = history;
        _csvFile = csvFile;
        _lastAllocated = GC.GetTotalAllocatedBytes();
        for (int g = 0; g < 3; g++)
            _lastGcCounts[g] = GC.CollectionCount(g);

        if (csvFile != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(csvFile))!);
            File.WriteAllText(csvFile,
                "time,uptime_s,cycles,cur_ms,avg_ms,max_ms,p99_ms,overruns," +
                "gen0,gen1,gen2,heap_mb,ws_mb,alloc_mb_s,pause_pct,threads,hist_mb\n",
                Encoding.UTF8);
        }
    }

    /// <summary>启动定时采样（间隔秒）。</summary>
    public void Start(int intervalSeconds)
        => _timer = new Timer(_ => Sample(), null, TimeSpan.FromSeconds(intervalSeconds), TimeSpan.FromSeconds(intervalSeconds));

    /// <summary>手动采一次样（全速浸泡按块采样用）。</summary>
    public void Sample()
    {
        double now = _uptime.Elapsed.TotalSeconds;
        double elapsed = Math.Max(now - _lastSampleSeconds, 1e-3);
        _lastSampleSeconds = now;

        // ---- 周期统计
        long cycles = 0;
        double curMs = 0, avgMs = 0, maxMs = 0, p99Ms = 0;
        long overruns = 0;
        if (_scheduler != null)
        {
            foreach (var s in _scheduler.Stats)
            {
                cycles += s.Count;
                curMs = Math.Max(curMs, s.CurrentMs);
                avgMs = Math.Max(avgMs, s.AverageMs);
                maxMs = Math.Max(maxMs, s.MaxMs);
                p99Ms = Math.Max(p99Ms, s.PercentileMs(99));
                overruns += s.Overruns;
            }
        }
        double cps = (cycles - _lastCycles) / elapsed;
        _lastCycles = cycles;

        // ---- GC / 内存
        Span<int> gcDelta = stackalloc int[3];
        for (int g = 0; g < 3; g++)
        {
            int c = GC.CollectionCount(g);
            gcDelta[g] = c - _lastGcCounts[g];
            _lastGcCounts[g] = c;
        }
        long allocated = GC.GetTotalAllocatedBytes();
        double allocMbs = (allocated - _lastAllocated) / elapsed / 1024 / 1024;
        _lastAllocated = allocated;

        var gcInfo = GC.GetGCMemoryInfo();
        double heapMb = gcInfo.HeapSizeBytes / 1024.0 / 1024.0;
        double wsMb = Environment.WorkingSet / 1024.0 / 1024.0;
        double pausePct = gcInfo.PauseTimePercentage;
        int threads = Process.GetCurrentProcess().Threads.Count;
        double histMb = (_history?.TotalBytes() ?? 0) / 1024.0 / 1024.0;

        string state = _scheduler?.State.ToString() ?? "-";
        Console.WriteLine(
            $"[监控 +{TimeSpan.FromSeconds(now):hh\\:mm\\:ss}] {state} " +
            $"周期={cycles:N0}（{cps:F1}/s） 耗时 cur={curMs:F2} avg={avgMs:F2} max={maxMs:F2} p99={p99Ms:F1} ms 超限={overruns} | " +
            $"GC {gcDelta[0]}/{gcDelta[1]}/{gcDelta[2]} 堆={heapMb:F0}MB WS={wsMb:F0}MB 分配={allocMbs:F1}MB/s 暂停={pausePct:F2}%" +
            (_history != null ? $" | 历史={histMb:F1}MB" : ""));

        if (_csvFile != null)
        {
            var line = string.Create(CultureInfo.InvariantCulture,
                $"{DateTime.Now:HH:mm:ss},{now:F0},{cycles},{curMs:F3},{avgMs:F3},{maxMs:F3},{p99Ms:F1},{overruns}," +
                $"{_lastGcCounts[0]},{_lastGcCounts[1]},{_lastGcCounts[2]},{heapMb:F1},{wsMb:F1},{allocMbs:F2},{pausePct:F2},{threads},{histMb:F1}\n");
            File.AppendAllText(_csvFile, line);
        }
    }

    /// <summary>打印周期统计明细（每 DPU 一行）。</summary>
    public void PrintDpuStats()
    {
        if (_scheduler == null)
        {
            Console.WriteLine("（未启用调度器）");
            return;
        }
        Console.WriteLine($"{"DPU",-12} {"周期数",10} {"cur ms",9} {"avg ms",9} {"min ms",9} {"max ms",9} {"p95 ms",8} {"p99 ms",8} {"超限",6}");
        foreach (var s in _scheduler.Stats)
        {
            Console.WriteLine(
                $"{s.DpuName,-12} {s.Count,10:N0} {s.CurrentMs,9:F3} {s.AverageMs,9:F3} " +
                $"{(s.Count == 0 ? 0 : s.MinMs),9:F3} {s.MaxMs,9:F3} {s.PercentileMs(95),8:F1} {s.PercentileMs(99),8:F1} {s.Overruns,6}");
        }
    }

    public void Dispose() => _timer?.Dispose();
}
