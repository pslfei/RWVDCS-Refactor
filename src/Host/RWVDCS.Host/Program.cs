using System.Diagnostics;
using System.Globalization;
using System.Text;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using RWVDCS.Engineering;
using RWVDCS.Runtime;

namespace RWVDCS.Host;

/// <summary>
/// 新系统宿主控制台：加载工程 mdb → 装配运行时 → FirstRun/步进/存取工况/点值查询。
/// 同时是对账（parity）阶段的驱动器：--dump 导出全部点值供与老系统比对。
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        // Web 管理台模式（RuntimeHost + Kestrel + 静态界面），与经典 CLI 模式互斥
        if (args.Contains("--web"))
            return WebHost.Run(args);

        string mdbPath = args[0];
        int steps = 0, runSeconds = -1, soakSteps = 0, monitorInterval = 5;
        string? saveDir = null, loadDir = null, dumpFile = null, arenaDir = null, importLegacy = null;
        string? historyDir = null, statsCsv = null, blocksSrc = null;
        bool repl = false, firstRun = true;
        var traceBlocks = new List<string>();

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--steps": steps = int.Parse(args[++i]); break;
                case "--run": runSeconds = int.Parse(args[++i]); break;
                case "--soak-steps": soakSteps = int.Parse(args[++i]); break;
                case "--monitor": monitorInterval = int.Parse(args[++i]); break;
                case "--stats-csv": statsCsv = args[++i]; break;
                case "--history": historyDir = args[++i]; break;
                case "--blocks-src": blocksSrc = args[++i]; break;
                case "--save": saveDir = args[++i]; break;
                case "--load": loadDir = args[++i]; break;
                case "--dump": dumpFile = args[++i]; break;
                case "--arena": arenaDir = args[++i]; break;
                case "--no-firstrun": firstRun = false; break;
                case "--import-legacy": importLegacy = args[++i]; break;
                case "--trace": traceBlocks.Add(args[++i]); break;
                case "--repl": repl = true; break;
                default:
                    Console.Error.WriteLine($"未知参数: {args[i]}");
                    return 2;
            }
        }

        if (!File.Exists(mdbPath))
        {
            Console.Error.WriteLine($"工程库不存在: {mdbPath}");
            return 2;
        }

        // ---- 1. 读工程库
        var sw = Stopwatch.StartNew();
        var model = MdbEngineeringReader.Load(mdbPath);
        sw.Stop();
        int totalPoints = model.Controllers.Sum(c => c.Points.Count);
        int totalBlocks = model.Controllers.Sum(c => c.Blocks.Count);
        Console.WriteLine($"[工程] {Path.GetFileName(mdbPath)}：{model.Controllers.Count} 控制器 / {totalPoints:N0} 点 / {totalBlocks:N0} 块，读库 {sw.ElapsedMilliseconds:N0} ms");

        // ---- 2. 装配运行时
        var catalog = new BlockCatalog(typeof(Blocks.RW.VSET).Assembly);
        sw.Restart();
        var runtime = RuntimeBuilder.Build(model, catalog, new RuntimeBuildOptions { ArenaDirectory = arenaDir });
        sw.Stop();

        var rpt = runtime.Report;
        Console.WriteLine($"[装配] {runtime.Dpus.Count} DPU / {rpt.PointCount:N0} 点 + {rpt.IntermediatePointCount:N0} 中间点 / {rpt.CommandCount:N0} 命令，{sw.ElapsedMilliseconds:N0} ms");
        if (rpt.MissingFcTypes.Count > 0)
        {
            int missing = rpt.MissingFcTypes.Values.Sum();
            Console.WriteLine($"[装配] 缺失功能码 {rpt.MissingFcTypes.Count} 种 / {missing:N0} 块（老系统同样跳过）：" +
                              string.Join(", ", rpt.MissingFcTypes.OrderByDescending(kv => kv.Value).Take(10).Select(kv => $"{kv.Key}×{kv.Value}")));
        }
        if (rpt.DeadInputBindings + rpt.DeadOutputBindings > 0)
            Console.WriteLine($"[装配] 死绑定：输入 {rpt.DeadInputBindings:N0} / 输出 {rpt.DeadOutputBindings:N0}（点名解析不到，走老系统兜底语义）");
        foreach (var err in rpt.Errors.Take(20))
            Console.WriteLine($"[错误] {err}");
        if (rpt.Errors.Count > 20)
            Console.WriteLine($"[错误] …… 共 {rpt.Errors.Count} 条");

        using (runtime)
        {
            // ---- 3. 老工况迁移 / 工况加载 / FirstRun（三选一）
            if (importLegacy != null)
            {
                sw.Restart();
                var rep = LegacyStateImporter.Import(runtime, importLegacy);
                sw.Stop();
                Console.WriteLine($"[迁移] 点 {rep.PointsApplied:N0}（跳过副本/缺失 {rep.PointsSkipped:N0}）、" +
                                  $"块字段 {rep.BlockFieldsApplied:N0}（跳过 {rep.BlockFieldsSkipped:N0}，缺块 {rep.BlocksMissing:N0}），" +
                                  $"{sw.ElapsedMilliseconds:N0} ms");
                foreach (var w in rep.Warnings.Take(10))
                    Console.WriteLine($"[迁移] 警告：{w}");
                if (rep.Warnings.Count > 10)
                    Console.WriteLine($"[迁移] …… 共 {rep.Warnings.Count} 条警告");
            }
            else if (loadDir != null)
            {
                sw.Restart();
                runtime.LoadSnapshot(loadDir);
                sw.Stop();
                Console.WriteLine($"[工况] 已加载 {loadDir}（{sw.ElapsedMilliseconds:N0} ms）");
            }
            else if (firstRun)
            {
                sw.Restart();
                runtime.FirstRun();
                sw.Stop();
                Console.WriteLine($"[运行] FirstRun 完成（{sw.ElapsedMilliseconds:N0} ms）");
            }

            // ---- 4. 运行设施：调度器 / 历史站 / 稳定性监控 / 热更换代器
            using var scheduler = new ScanScheduler(runtime);
            using var history = historyDir != null
                ? new HistoryRecorder(runtime, new HistoryOptions { Directory = historyDir })
                : null;
            if (history != null)
            {
                scheduler.AfterDpuStep = history.OnDpuStep;
                Console.WriteLine($"[历史] 记录器已启用 → {history.SessionDirectory}（死区 0.1% 量程 / 强制间隔 300 周期）");
            }
            using var monitor = new StabilityMonitor(scheduler, history, statsCsv);
            var swapper = new BlockHotSwapper(runtime, model);
            blocksSrc ??= Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Blocks", "RWVDCS.Blocks", "RW");
            blocksSrc = Path.GetFullPath(blocksSrc);

            // Ctrl+C：优雅暂停而非硬杀
            bool cancelled = false;
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cancelled = true;
            };

            // ---- 5. 步进（对账路径，保持原语义：直接串行步进，不经调度器）
            if (steps > 0)
            {
                if (traceBlocks.Count > 0)
                {
                    TraceBlocksLine(runtime, traceBlocks, 0);
                    for (int c = 1; c <= steps; c++)
                    {
                        runtime.Step(1);
                        TraceBlocksLine(runtime, traceBlocks, c);
                    }
                }
                else
                {
                    sw.Restart();
                    runtime.Step(steps);
                    sw.Stop();
                    double perCycle = sw.Elapsed.TotalMilliseconds / steps;
                    Console.WriteLine($"[运行] 步进 {steps} 周期，共 {sw.ElapsedMilliseconds:N0} ms（{perCycle:F2} ms/周期，全部 {runtime.Dpus.Count} DPU 串行）");
                }
            }

            // ---- 6. 全速浸泡（分块步进 + 周期统计 + 采样监控）
            if (soakSteps > 0)
                RunSoak(scheduler, monitor, soakSteps, ref cancelled);

            // ---- 7. 连续运行（实时节拍）
            if (runSeconds >= 0)
            {
                Console.WriteLine($"[运行] 连续运行{(runSeconds == 0 ? "（Ctrl+C 停止）" : $" {runSeconds} 秒")}，" +
                                  $"节拍 = 各 DPU Cycle（{runtime.Dpus[0].Cycle * 1000:F0} ms），监控每 {monitorInterval}s");
                monitor.Start(monitorInterval);
                scheduler.Start();
                var runClock = Stopwatch.StartNew();
                while (!cancelled && (runSeconds == 0 || runClock.Elapsed.TotalSeconds < runSeconds))
                    Thread.Sleep(200);
                scheduler.Pause();
                Console.WriteLine($"[运行] 已暂停（实际运行 {runClock.Elapsed.TotalSeconds:F0} s）");
                monitor.Sample();
                monitor.PrintDpuStats();
            }

            // ---- 8. 保存/导出
            if (saveDir != null)
            {
                sw.Restart();
                scheduler.RunAtCycleBoundary(() => runtime.SaveSnapshot(saveDir));
                sw.Stop();
                long bytes = Directory.EnumerateFiles(saveDir).Sum(f => new FileInfo(f).Length);
                Console.WriteLine($"[工况] 已保存 {saveDir}（{bytes / 1024.0 / 1024.0:F1} MB，{sw.ElapsedMilliseconds:N0} ms）");
            }

            if (dumpFile != null)
            {
                sw.Restart();
                int n = DumpPoints(runtime, dumpFile);
                sw.Stop();
                Console.WriteLine($"[导出] {n:N0} 点 → {dumpFile}（{sw.ElapsedMilliseconds:N0} ms）");
            }

            // ---- 9. 交互
            if (repl)
                RunRepl(runtime, scheduler, monitor, swapper, history, blocksSrc);

            scheduler.Stop();
        }

        return 0;
    }

    /// <summary>全速浸泡：不设节拍分块步进，块间采样稳定性指标（内存增长/GC 抖动检测）。</summary>
    private static void RunSoak(ScanScheduler scheduler, StabilityMonitor monitor, int totalSteps, ref bool cancelled)
    {
        const int chunk = 500;
        Console.WriteLine($"[浸泡] 全速步进 {totalSteps:N0} 周期（每 {chunk} 周期采样一次，Ctrl+C 提前结束）");
        var sw = Stopwatch.StartNew();
        int done = 0;
        while (done < totalSteps && !cancelled)
        {
            int n = Math.Min(chunk, totalSteps - done);
            scheduler.StepOnce(n);
            done += n;
            if (done % (chunk * 10) == 0 || done >= totalSteps)
                monitor.Sample();
        }
        sw.Stop();
        Console.WriteLine($"[浸泡] 完成 {done:N0} 周期，共 {sw.Elapsed.TotalSeconds:F1} s（{sw.Elapsed.TotalMilliseconds / Math.Max(done, 1):F2} ms/周期）");
        monitor.PrintDpuStats();
    }

    // =================================================================
    // 点值导出（对账格式：DPU\t点名\t类别\t值，名字序稳定）
    // =================================================================
    private static int DumpPoints(DcsRuntime runtime, string file)
    {
        var lines = runtime.EnumeratePoints()
            .Select(p => $"{p.DpuName}\t{p.Name}\t{p.Kind}\t{FormatValue(p.Value)}")
            .OrderBy(l => l, StringComparer.Ordinal)
            .ToList();
        File.WriteAllLines(file, lines, new UTF8Encoding(false));
        return lines.Count;
    }

    internal static string FormatValue(object? value) => value switch
    {
        null => "<null>",
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        bool b => b ? "1" : "0",
        ushort u => u.ToString(CultureInfo.InvariantCulture),
        uint u => u.ToString(CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    /// <summary>
    /// 单行紧凑跟踪（与 LegacyRunner --trace 同格式）：全部 LA/LD 管脚 buffer + 私有标量状态字段。
    /// </summary>
    private static void TraceBlocksLine(DcsRuntime runtime, List<string> blocks, int cycle)
    {
        foreach (string blockName in blocks)
        {
            BlockCommand? cmd = null;
            foreach (var dpu in runtime.Dpus)
            {
                cmd = dpu.FindCommand(blockName);
                if (cmd != null)
                    break;
            }
            if (cmd == null)
            {
                Console.WriteLine($"[trace c{cycle}] {blockName}: <块不存在>");
                continue;
            }
            var sb = new StringBuilder();
            sb.Append("[trace c").Append(cycle).Append("] ").Append(cmd.Name).Append(':');
            var schema = BlockStateSchema.For(cmd.Fc.GetType());
            foreach (var f in schema.Fields)
            {
                var ft = f.Field.FieldType;
                if (ft == typeof(LA) || ft == typeof(LD))
                {
                    object? pin = f.Field.GetValue(cmd.Fc);
                    string v = pin is IValuable val ? FormatValue(val.Value) : "<null>";
                    sb.Append(' ').Append(f.Name).Append('=').Append(v);
                }
                else if (!f.Field.IsPublic &&
                         (ft == typeof(bool) || ft == typeof(float) || ft == typeof(double) || ft == typeof(int) || ft == typeof(uint)))
                {
                    sb.Append(' ').Append(f.Name).Append('=').Append(FormatValue(f.Field.GetValue(cmd.Fc)));
                }
            }
            Console.WriteLine(sb.ToString());
        }
    }

    // =================================================================
    // 交互模式
    // =================================================================
    private static void RunRepl(
        DcsRuntime runtime,
        ScanScheduler scheduler,
        StabilityMonitor monitor,
        BlockHotSwapper swapper,
        HistoryRecorder? history,
        string blocksSrc)
    {
        Console.WriteLine();
        Console.WriteLine("交互模式（help 查看命令，quit 退出）");
        while (true)
        {
            Console.Write("rwvdcs> ");
            string? line = Console.ReadLine();
            if (line == null)
                break;
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
                continue;

            try
            {
                switch (parts[0].ToLowerInvariant())
                {
                    case "quit" or "exit" or "q":
                        scheduler.Pause();
                        return;

                    case "help" or "h":
                        Console.WriteLine("""
                            r|read <点名>            读点值
                            w|write <点名> <值>      写点值
                            s|step [n]               单步 n 个周期（默认 1；需处于暂停态）
                            run                      开始连续运行（按周期节拍）
                            pause                    暂停连续运行（周期边界）
                            stats                    周期耗时统计（每 DPU）+ 进程稳定性采样
                            hotload <FC名|.cs 文件>  热更换代功能块（Roslyn 编译 → 周期边界原子替换，状态保留）
                            hist <点名> [n]          查询历史站最近 n 条记录（默认 10）
                            cycle <秒>               修改全部 DPU 扫描周期
                            firstrun                 执行 FirstRun
                            save <目录>              保存工况
                            load <目录>              加载工况
                            dump <文件>              导出全部点值
                            find <子串> [max]        按名字查点（默认最多 20 条）
                            block <块名>             查看块命令的绑定与状态
                            info                     运行时概览
                            quit                     退出
                            """);
                        break;

                    case "run":
                        scheduler.Start();
                        Console.WriteLine($"连续运行中（节拍 {runtime.Dpus[0].Cycle * 1000:F0} ms；pause 暂停）");
                        break;

                    case "pause":
                        scheduler.Pause();
                        Console.WriteLine($"已暂停（周期边界）：" + string.Join(" ", runtime.Dpus.Select(d => $"{d.Name}=c{d.CycleCount}")));
                        break;

                    case "stats":
                        monitor.PrintDpuStats();
                        monitor.Sample();
                        break;

                    case "hotload" when parts.Length >= 2:
                        HotLoad.Execute(swapper, scheduler, blocksSrc, parts[1..]);
                        break;

                    case "hist" when parts.Length >= 2:
                    {
                        if (history == null)
                        {
                            Console.WriteLine("历史站未启用（启动时加 --history <目录>）");
                            break;
                        }
                        history.Flush();
                        int max = parts.Length >= 3 ? int.Parse(parts[2]) : 10;
                        string pointName = parts[1];
                        DpuRuntime? owner = runtime.Dpus.FirstOrDefault(d => d.LocalSlots.ContainsKey(pointName));
                        if (owner == null)
                        {
                            Console.WriteLine($"点不存在：{pointName}");
                            break;
                        }
                        string file = Path.Combine(history.SessionDirectory, string.Join("_", owner.Name.Split(Path.GetInvalidFileNameChars())) + ".rwhist");
                        var samples = HistoryRecorder.Query(file, pointName).ToList();
                        foreach (var s in samples.TakeLast(max))
                            Console.WriteLine($"  c{s.Cycle,-8} {DateTimeOffset.FromUnixTimeMilliseconds(s.UnixMs).ToLocalTime():HH:mm:ss.fff}  {s.Value}");
                        Console.WriteLine($"共 {samples.Count} 条记录（显示最近 {Math.Min(max, samples.Count)} 条）");
                        break;
                    }

                    case "cycle" when parts.Length >= 2:
                    {
                        float sec = float.Parse(parts[1], CultureInfo.InvariantCulture);
                        scheduler.RunAtCycleBoundary(() =>
                        {
                            foreach (var dpu in runtime.Dpus)
                                dpu.Cycle = sec;
                        });
                        Console.WriteLine($"扫描周期 → {runtime.Dpus[0].Cycle * 1000:F0} ms");
                        break;
                    }

                    case "r" or "read" when parts.Length >= 2:
                    {
                        string name = parts[1];
                        if (runtime.TryGetSlot(name, out var slot) && slot.IsRealPoint)
                            Console.WriteLine($"{name} [{slot.Kind}] = {FormatValue(slot.ReadBoxedBuffer())}");
                        else
                            Console.WriteLine($"点不存在或不是数据点: {name}");
                        break;
                    }

                    case "w" or "write" when parts.Length >= 3:
                    {
                        string name = parts[1];
                        if (!runtime.TryGetSlot(name, out var slot) || !slot.IsRealPoint)
                        {
                            Console.WriteLine($"点不存在或不是数据点: {name}");
                            break;
                        }
                        object boxed = slot.Kind switch
                        {
                            PointKind.LA => float.Parse(parts[2], CultureInfo.InvariantCulture),
                            PointKind.LD => parts[2] is "1" or "true" or "True",
                            PointKind.LP => ushort.Parse(parts[2], CultureInfo.InvariantCulture),
                            PointKind.LP32 => uint.Parse(parts[2], CultureInfo.InvariantCulture),
                            _ => throw new InvalidOperationException(),
                        };
                        slot.WriteBoxedBuffer(boxed);
                        Console.WriteLine($"{name} <= {FormatValue(slot.ReadBoxedBuffer())}");
                        break;
                    }

                    case "s" or "step":
                    {
                        if (scheduler.State == ScanState.Running)
                        {
                            Console.WriteLine("连续运行中，请先 pause 再单步。");
                            break;
                        }
                        int n = parts.Length >= 2 ? int.Parse(parts[1]) : 1;
                        var sw = Stopwatch.StartNew();
                        scheduler.StepOnce(n);
                        sw.Stop();
                        Console.WriteLine($"步进 {n} 周期，{sw.Elapsed.TotalMilliseconds:F2} ms → " +
                                          string.Join(" ", runtime.Dpus.Select(d => $"{d.Name}=c{d.CycleCount}")));
                        break;
                    }

                    case "firstrun":
                        runtime.FirstRun();
                        Console.WriteLine("FirstRun 完成");
                        break;

                    case "save" when parts.Length >= 2:
                        scheduler.RunAtCycleBoundary(() => runtime.SaveSnapshot(parts[1]));
                        Console.WriteLine($"工况已保存: {parts[1]}");
                        break;

                    case "load" when parts.Length >= 2:
                        scheduler.RunAtCycleBoundary(() => runtime.LoadSnapshot(parts[1]));
                        Console.WriteLine($"工况已加载: {parts[1]}");
                        break;

                    case "dump" when parts.Length >= 2:
                        Console.WriteLine($"导出 {DumpPoints(runtime, parts[1]):N0} 点 → {parts[1]}");
                        break;

                    case "find" when parts.Length >= 2:
                    {
                        int max = parts.Length >= 3 ? int.Parse(parts[2]) : 20;
                        int shown = 0;
                        foreach (var (dpuName, name, kind, value) in runtime.EnumeratePoints())
                        {
                            if (!name.Contains(parts[1], StringComparison.OrdinalIgnoreCase))
                                continue;
                            Console.WriteLine($"  [{dpuName}] {name} [{kind}] = {FormatValue(value)}");
                            if (++shown >= max)
                                break;
                        }
                        Console.WriteLine($"共显示 {shown} 条");
                        break;
                    }

                    case "block" when parts.Length >= 2:
                    {
                        BlockCommand? cmd = null;
                        DpuRuntime? owner = null;
                        foreach (var dpu in runtime.Dpus)
                        {
                            cmd = dpu.FindCommand(parts[1]);
                            if (cmd != null)
                            {
                                owner = dpu;
                                break;
                            }
                        }
                        if (cmd == null)
                        {
                            Console.WriteLine($"块不存在: {parts[1]}");
                            break;
                        }
                        Console.WriteLine($"[{owner!.Name}] {cmd.Name} ({cmd.FcName})");
                        foreach (var b in cmd.Inputs)
                            Console.WriteLine($"  IN  {b.Pin.Field.Name,-12} <- {b.PointName}{(b.Reversed ? " (~)" : "")}{(b.Source is { IsRealPoint: true } ? "" : "  [死绑定]")}");
                        foreach (var b in cmd.Outputs)
                            Console.WriteLine($"  OUT {b.Pin.Field.Name,-12} -> {b.PointName}{(b.Reversed ? " (~)" : "")}{(b.Target is { IsRealPoint: true } ? "" : "  [死绑定]")}");
                        var schema = BlockStateSchema.For(cmd.Fc.GetType());
                        foreach (var f in schema.Fields.Where(f => f.PinType is PinTypes.Input or PinTypes.Output or PinTypes.IO))
                        {
                            object? pin = f.Field.GetValue(cmd.Fc);
                            string v = pin is IValuable val ? FormatValue(val.Value) : pin?.ToString() ?? "<null>";
                            Console.WriteLine($"  PIN {f.Name,-12} = {v}");
                        }
                        break;
                    }

                    case "info":
                    {
                        foreach (var dpu in runtime.Dpus)
                            Console.WriteLine($"  {dpu.Name}（Id={dpu.ControllerId}）: {dpu.LocalSlots.Count:N0} 槽 / {dpu.Commands.Count:N0} 命令 / Cycle={dpu.Cycle}s / CycleCount={dpu.CycleCount}");
                        break;
                    }

                    default:
                        Console.WriteLine("无法识别的命令，help 查看用法");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"错误: {ex.Message}");
            }
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            RWVDCS.Next 宿主控制台

            用法: rwvdcs <工程.mdb> [选项]

            选项:
              --steps N               FirstRun 后步进 N 个周期（对账路径）
              --run N                 连续运行 N 秒（0 = 直到 Ctrl+C；按各 DPU 周期节拍）
              --soak-steps N          全速浸泡 N 个周期（分块步进 + 稳定性采样）
              --monitor N             连续运行时监控采样间隔秒数（默认 5）
              --stats-csv <文件>      稳定性指标落 CSV（长跑趋势分析）
              --history <目录>        启用内嵌历史站（死区变化存储）
              --blocks-src <目录>     热更源码目录（hotload 按 FC 名找文件；默认仓库内 Blocks/RW）
              --save <目录>           运行结束后保存工况
              --load <目录>           加载工况（跳过 FirstRun）
              --import-legacy <文件>  导入老 .wrk 迁移桥接文件（LegacyRunner --export-state 产物；跳过 FirstRun）
              --dump <文件>           导出全部点值（对账格式）
              --arena <目录>          Arena 使用 MMF 后备文件（默认纯内存）
              --no-firstrun           跳过 FirstRun
              --repl                  进入交互模式

            Web 管理台模式（可不带 mdb 空载启动，从界面装载工程）:
              rwvdcs [工程.mdb] --web [端口] [--data 目录] [--arena 目录] [--start] [--no-history]
                --web [端口]          启动 Web 管理台 + REST/SSE 接口（默认 8080）
                --data <目录>         数据目录：工况/快照仓库、版本档案、历史站（默认 ./rwvdcs-data）
                --start               装载后自动开始连续运行
                --no-history          关闭内嵌历史站
            """);
    }
}
