using System.Diagnostics;

namespace RWVDCS.Runtime;

/// <summary>运行状态（对齐老系统 DCSBase.RunState 的有效面；Step 是瞬态动作不作为驻留状态）。</summary>
public enum ScanState
{
    Stopped = 0,
    Running = 1,
    Paused = 2,
}

/// <summary>
/// 单 DPU 周期统计（对齐老系统 Dpu 的 Current/Max/Min/AverageCycleTime，另加直方图与超限计数）。
/// 由扫描线程独占写、其他线程读——读取到轻微撕裂的统计值可接受，不加锁。
/// </summary>
public sealed class DpuCycleStats
{
    /// <summary>直方图桶上界（毫秒）；最后一桶为 +∞。</summary>
    internal static readonly double[] BucketUppers = [0.5, 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000];

    private readonly long[] _buckets = new long[BucketUppers.Length + 1];
    private double _totalMs;

    public string DpuName { get; }

    public DpuCycleStats(string dpuName) => DpuName = dpuName;

    /// <summary>最近一个周期的执行耗时（毫秒）。</summary>
    public double CurrentMs { get; private set; }

    public double MinMs { get; private set; } = double.MaxValue;
    public double MaxMs { get; private set; }
    public long Count { get; private set; }

    /// <summary>执行耗时超过扫描周期的次数（节拍失守）。</summary>
    public long Overruns { get; internal set; }

    public double AverageMs => Count == 0 ? 0 : _totalMs / Count;

    internal void Record(double ms)
    {
        CurrentMs = ms;
        if (ms < MinMs) MinMs = ms;
        if (ms > MaxMs) MaxMs = ms;
        _totalMs += ms;
        Count++;

        int i = 0;
        while (i < BucketUppers.Length && ms > BucketUppers[i])
            i++;
        _buckets[i]++;
    }

    /// <summary>由直方图估算分位值（返回所在桶的上界；最后一桶返回观测到的最大值）。</summary>
    public double PercentileMs(double p)
    {
        long n = Count;
        if (n == 0)
            return 0;
        long rank = (long)Math.Ceiling(p / 100.0 * n);
        long acc = 0;
        for (int i = 0; i < _buckets.Length; i++)
        {
            acc += _buckets[i];
            if (acc >= rank)
                return i < BucketUppers.Length ? Math.Min(BucketUppers[i], MaxMs) : MaxMs;
        }
        return MaxMs;
    }

    internal void Reset()
    {
        CurrentMs = 0;
        MinMs = double.MaxValue;
        MaxMs = 0;
        Count = 0;
        Overruns = 0;
        _totalMs = 0;
        Array.Clear(_buckets);
    }
}

/// <summary>
/// 扫描调度器：单扫描线程按各 DPU 的周期节拍驱动 <see cref="DpuRuntime.Step"/>。
/// 替代老系统"每 DPU 一个 System.Threading.Timer"的模型——单线程串行保证了
/// 与对账步进完全相同的确定性执行序，同时周期到点才执行、互不抢占。
/// </summary>
/// <remarks>
/// 并发契约：
/// <list type="bullet">
/// <item>DPU 步进只发生在扫描线程；外部控制操作（暂停/单步/存取工况/热更）通过
/// <see cref="RunAtCycleBoundary"/> 在周期边界串行化。</item>
/// <item>点值读写（REPL/API 线程）不与扫描互斥——与老系统 Remoting 写入行为一致，
/// 4 字节对齐的 buffer 读写在 x64 上天然原子。</item>
/// </list>
/// </remarks>
public sealed class ScanScheduler : IDisposable
{
    private readonly DcsRuntime _runtime;
    private readonly Lock _gate = new();
    private readonly DpuCycleStats[] _stats;
    private Thread? _thread;
    private volatile bool _shutdown;
    private volatile ScanState _state = ScanState.Stopped;
    private volatile bool _reanchor;

    public ScanScheduler(DcsRuntime runtime)
    {
        _runtime = runtime;
        _stats = runtime.Dpus.Select(d => new DpuCycleStats(d.Name)).ToArray();
    }

    public ScanState State => _state;

    public IReadOnlyList<DpuCycleStats> Stats => _stats;

    /// <summary>每个 DPU 步进完成后的回调（扫描线程/单步线程调用；用于历史站等旁路记录）。</summary>
    public Action<DpuRuntime>? AfterDpuStep { get; set; }

    /// <summary>启动/恢复连续运行。首次调用创建扫描线程。</summary>
    public void Start()
    {
        if (_state == ScanState.Running)
            return;
        _reanchor = true;
        _state = ScanState.Running;
        if (_thread == null)
        {
            _thread = new Thread(ScanLoop)
            {
                Name = "rwvdcs-scan",
                IsBackground = true,
            };
            _thread.Start();
        }
    }

    /// <summary>
    /// 暂停连续运行；返回时保证当前周期已经完整结束（周期边界）。
    /// Stopped 态也可进入 Paused（"就绪待单步"，下装后恢复暂停态用）。
    /// </summary>
    public void Pause()
    {
        if (_state != ScanState.Paused)
            _state = ScanState.Paused;
        lock (_gate)
        {
            // 拿到 gate 即说明扫描线程不在周期中
        }
    }

    /// <summary>
    /// 暂停态单步 n 个周期（在调用线程上执行，与扫描线程通过周期边界锁互斥）。
    /// 语义对齐老系统 RunState.Step：执行一个完整周期后回到暂停。
    /// </summary>
    public void StepOnce(int cycles = 1)
    {
        if (_state == ScanState.Running)
            throw new InvalidOperationException("连续运行中不能单步，请先暂停。");
        lock (_gate)
        {
            for (int c = 0; c < cycles; c++)
            {
                foreach (var dpu in _runtime.Dpus)
                    StepDpu(dpu, IndexOf(dpu));
            }
        }
    }

    /// <summary>在周期边界执行动作（存/取工况、热更换代等结构性操作走这里）。</summary>
    public void RunAtCycleBoundary(Action action)
    {
        lock (_gate)
        {
            action();
        }
    }

    /// <summary>停止调度（线程退出）；可再 Start 重建。</summary>
    public void Stop()
    {
        _state = ScanState.Stopped;
        _shutdown = true;
        _thread?.Join(2000);
        _thread = null;
        _shutdown = false;
    }

    public void ResetStats()
    {
        foreach (var s in _stats)
            s.Reset();
    }

    private int IndexOf(DpuRuntime dpu)
    {
        for (int i = 0; i < _runtime.Dpus.Count; i++)
            if (ReferenceEquals(_runtime.Dpus[i], dpu))
                return i;
        return 0;
    }

    private void StepDpu(DpuRuntime dpu, int index)
    {
        long t0 = Stopwatch.GetTimestamp();
        dpu.Step();
        AfterDpuStep?.Invoke(dpu);
        _stats[index].Record(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
    }

    private void ScanLoop()
    {
        var dpus = _runtime.Dpus;
        int n = dpus.Count;
        var clock = Stopwatch.StartNew();
        var nextDue = new double[n];

        while (!_shutdown)
        {
            if (_state != ScanState.Running)
            {
                Thread.Sleep(5);
                continue;
            }

            if (_reanchor)
            {
                // 暂停恢复后从"现在"重新起拍，不追补暂停期间积欠的周期
                double now0 = clock.Elapsed.TotalMilliseconds;
                for (int i = 0; i < n; i++)
                    nextDue[i] = now0;
                _reanchor = false;
            }

            double nearest = double.MaxValue;
            lock (_gate)
            {
                if (_state != ScanState.Running)
                    continue;

                double now = clock.Elapsed.TotalMilliseconds;
                for (int i = 0; i < n; i++)
                {
                    // 0.5ms 容差：略早到点也执行，避免踩着睡眠粒度反复空转
                    if (now + 0.5 >= nextDue[i])
                    {
                        StepDpu(dpus[i], i);

                        double periodMs = dpus[i].Cycle * 1000.0;
                        nextDue[i] += periodMs;
                        double after = clock.Elapsed.TotalMilliseconds;
                        if (nextDue[i] <= after)
                        {
                            // 超限：本周期执行耗时吃掉了下一拍——重新起拍而非爆发式追补
                            _stats[i].Overruns++;
                            nextDue[i] = after + periodMs;
                        }
                        now = after;
                    }
                    if (nextDue[i] < nearest)
                        nearest = nextDue[i];
                }
            }

            double wait = nearest - clock.Elapsed.TotalMilliseconds;
            if (wait > 1.5)
                Thread.Sleep((int)(wait - 1)); // .NET 8+ Windows 高精度睡眠（~1ms 粒度）
            else if (wait > 0)
                Thread.Yield();
        }
    }

    public void Dispose() => Stop();
}
