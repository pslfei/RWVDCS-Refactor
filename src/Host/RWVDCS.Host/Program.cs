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

        string mdbPath = args[0];
        int steps = 0;
        string? saveDir = null, loadDir = null, dumpFile = null, arenaDir = null, importLegacy = null;
        bool repl = false, firstRun = true;
        var traceBlocks = new List<string>();

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--steps": steps = int.Parse(args[++i]); break;
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

            // ---- 4. 步进
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

            // ---- 5. 保存/导出
            if (saveDir != null)
            {
                sw.Restart();
                runtime.SaveSnapshot(saveDir);
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

            // ---- 6. 交互
            if (repl)
                RunRepl(runtime);
        }

        return 0;
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
    private static void RunRepl(DcsRuntime runtime)
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
                        return;

                    case "help" or "h":
                        Console.WriteLine("""
                            r|read <点名>            读点值
                            w|write <点名> <值>      写点值
                            s|step [n]               步进 n 个周期（默认 1）
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
                        int n = parts.Length >= 2 ? int.Parse(parts[1]) : 1;
                        var sw = Stopwatch.StartNew();
                        runtime.Step(n);
                        sw.Stop();
                        Console.WriteLine($"步进 {n} 周期，{sw.Elapsed.TotalMilliseconds:F2} ms");
                        break;
                    }

                    case "firstrun":
                        runtime.FirstRun();
                        Console.WriteLine("FirstRun 完成");
                        break;

                    case "save" when parts.Length >= 2:
                        runtime.SaveSnapshot(parts[1]);
                        Console.WriteLine($"工况已保存: {parts[1]}");
                        break;

                    case "load" when parts.Length >= 2:
                        runtime.LoadSnapshot(parts[1]);
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
              --steps N               FirstRun 后步进 N 个周期
              --save <目录>           运行结束后保存工况
              --load <目录>           加载工况（跳过 FirstRun）
              --import-legacy <文件>  导入老 .wrk 迁移桥接文件（LegacyRunner --export-state 产物；跳过 FirstRun）
              --dump <文件>           导出全部点值（对账格式）
              --arena <目录>          Arena 使用 MMF 后备文件（默认纯内存）
              --no-firstrun           跳过 FirstRun
              --repl                  进入交互模式
            """);
    }
}
