using System.Diagnostics;
using Microsoft.CodeAnalysis;
using RWVDCS.Hosting;
using RWVDCS.Runtime;

namespace RWVDCS.Host;

/// <summary>
/// 宿主的"现调现改"入口：源文件 → Roslyn 内存编译 → 周期边界原子换代。
/// </summary>
internal static class HotLoad
{
    /// <summary>
    /// 参数既可以是 .cs 文件路径，也可以是功能码名（在块源码目录中自动配齐
    /// FC_&lt;名&gt;.cs / FC_&lt;名&gt;_RUN.cs 两个 partial 文件）。
    /// </summary>
    public static void Execute(BlockHotSwapper swapper, ScanScheduler? scheduler, string blocksSrcDir, string[] args)
    {
        var files = new List<string>();
        foreach (string a in args)
        {
            if (a.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                files.Add(Path.GetFullPath(a));
            }
            else
            {
                string decl = Path.Combine(blocksSrcDir, $"FC_{a}.cs");
                string run = Path.Combine(blocksSrcDir, $"FC_{a}_RUN.cs");
                if (File.Exists(decl)) files.Add(decl);
                if (File.Exists(run)) files.Add(run);
                if (!File.Exists(decl) && !File.Exists(run))
                {
                    Console.WriteLine($"块源码目录中找不到 FC_{a}.cs / FC_{a}_RUN.cs（目录：{blocksSrcDir}）");
                    return;
                }
            }
        }

        var missing = files.Where(f => !File.Exists(f)).ToList();
        if (missing.Count > 0)
        {
            foreach (var f in missing)
                Console.WriteLine($"文件不存在：{f}");
            return;
        }
        if (files.Count == 0)
        {
            Console.WriteLine("用法：hotload <功能码名|源文件.cs> [更多…]");
            return;
        }

        // ---- 编译（内嵌源码 PDB，调试器可断点）
        var sw = Stopwatch.StartNew();
        var sources = files.Select(f => new KernelSource(f, File.ReadAllText(f))).ToList();
        var extraRefs = new[]
        {
            MetadataReference.CreateFromFile(typeof(Blocks.RW.VSET).Assembly.Location),      // RWVDCS.Blocks（源里引用其他块类型时可解析）
            MetadataReference.CreateFromFile(typeof(BlockHotSwapper).Assembly.Location),     // RWVDCS.Runtime
            MetadataReference.CreateFromFile(typeof(Engineering.EngineeringModel).Assembly.Location),
        };
        var result = KernelCompiler.Compile(
            $"blocks-hot-{DateTime.Now:HHmmss}", sources, debug: true, extraReferences: extraRefs);
        sw.Stop();

        if (!result.Success)
        {
            Console.WriteLine($"编译失败（{result.Errors.Length} 个错误）：");
            foreach (var e in result.Errors.Take(10))
                Console.WriteLine($"  {e}");
            return;
        }
        Console.WriteLine($"[热更] 编译 {files.Count} 个文件 OK（{sw.ElapsedMilliseconds} ms）");

        // ---- 周期边界原子换代
        sw.Restart();
        HotSwapReport report = null!;
        if (scheduler != null)
            scheduler.RunAtCycleBoundary(() => report = swapper.Apply(result.AssemblyImage!, result.PdbImage));
        else
            report = swapper.Apply(result.AssemblyImage!, result.PdbImage);
        sw.Stop();

        foreach (var m in report.Messages)
            Console.WriteLine($"[热更] {m}");
        if (report.Success)
        {
            Console.WriteLine(
                $"[热更] 第 {report.Generation} 代生效：功能码 [{string.Join(", ", report.SwappedFcNames)}]，" +
                $"替换 {report.CommandsSwapped} 个块实例，转移 {report.FieldsTransferred:N0} 个状态字段（{sw.ElapsedMilliseconds} ms）");

            // 上一代 ALC 真卸载诊断（协作式卸载：等待 GC 回收）
            if (report.RetiredAlc is { } retired)
            {
                bool collected = WaitForCollected(retired);
                Console.WriteLine(collected
                    ? $"[热更] 第 {report.Generation - 1} 代 ALC 已被 GC 回收（无程序集泄漏）"
                    : $"[热更] 第 {report.Generation - 1} 代 ALC 尚未回收（存在其他功能码仍引用该代属正常；持续多代不回收则需排查）");
            }
        }
        else
        {
            Console.WriteLine("[热更] 未生效。");
        }
    }

    private static bool WaitForCollected(WeakReference contextRef, int maxGcRounds = 10)
    {
        for (int i = 0; contextRef.IsAlive && i < maxGcRounds; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        return !contextRef.IsAlive;
    }
}
