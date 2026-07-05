using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using RWVDCS.Core.PointStore;
using RWVDCS.Core.Types;
using RWVDCS.Hosting;

namespace RWVDCS.Core.Tests.Hosting;

/// <summary>
/// PoC 门槛 1（方案 §7）：ALC 反复热更无程序集泄漏。
/// 单测规模取 300 代（秒级完成）；万次规模的浸泡跑放在基准工程里按需执行。
/// </summary>
public class AlcLeakStressTests
{
    private const string KernelTemplate = """
        using RWVDCS.Core.Execution;
        using RWVDCS.Core.PointStore;

        namespace FB.Generated;

        public sealed class StressKernel : IScanKernel
        {
            public void Scan(PointArena arena)
            {
                arena.WriteField(0, 0u, arena.ReadField<int>(0, 0u) + __GEN__);
            }
        }
        """;

    [Fact]
    public void Repeated_hot_swaps_do_not_leak_load_contexts()
    {
        const int Generations = 300;

        var b = new ArenaBuilder();
        b.AddRawSlot("S", WellKnownTypeIds.Raw, 4);
        using var arena = PointArena.Create(b);

        int baselineAlcCount = AssemblyLoadContext.All.Count();
        var samples = new List<WeakReference>();

        RunGenerations(arena, Generations, samples);

        // 全部换代完成后：所有被采样的旧代 ALC 都应可回收
        for (int i = 0; i < 10 && samples.Any(w => w.IsAlive); i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        int alive = samples.Count(w => w.IsAlive);
        Assert.True(alive == 0, $"{alive}/{samples.Count} 个已退役 ALC 仍存活——热更链路存在泄漏。");

        int finalAlcCount = AssemblyLoadContext.All.Count();
        Assert.True(finalAlcCount <= baselineAlcCount + 1,
            $"ALC 数量从 {baselineAlcCount} 涨到 {finalAlcCount}——收尾后不应留存多于 1 个内核上下文。");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunGenerations(PointArena arena, int generations, List<WeakReference> samples)
    {
        using var host = new KernelHost();
        for (int gen = 1; gen <= generations; gen++)
        {
            var result = KernelCompiler.Compile($"stress-{gen}",
                [new KernelSource("s.cs", KernelTemplate.Replace("__GEN__", "1"))]);
            Assert.True(result.Success, string.Join('\n', result.Errors));

            var retired = host.LoadGeneration(result.AssemblyImage!, result.PdbImage);
            if (retired is not null && gen % 20 == 0)
                samples.Add(retired);

            host.Kernel!.Scan(arena);
        }

        Assert.Equal(generations, arena.ReadField<int>(0, 0u)); // 每代都真实执行过
        host.UnloadCurrent();
    }
}
