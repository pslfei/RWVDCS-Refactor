using System.Runtime.CompilerServices;
using RWVDCS.Core.PointStore;
using RWVDCS.Core.Types;
using RWVDCS.Hosting;

namespace RWVDCS.Core.Tests.Hosting;

/// <summary>
/// 热更 PoC 全链路验证（方案 §4.3 的三个核心承诺）：
/// 1. FB 源码字符串 → Roslyn 内存编译 → 装载运行；
/// 2. 换代热更：新逻辑生效、Arena 中的状态不丢；
/// 3. 旧代 ALC 被 GC 真实回收（无程序集泄漏）。
/// </summary>
public class HotReloadTests
{
    private const string KernelV1 = """
        using RWVDCS.Core.Execution;
        using RWVDCS.Core.PointStore;
        using RWVDCS.Core.Types;

        namespace FB.Generated;

        public sealed class DemoKernel : IScanKernel
        {
            public void Scan(PointArena arena)
            {
                arena.TryGetSid("AI.IN", out int input);
                arena.TryGetSid("AI.OUT", out int output);
                arena.TryGetSid("K.COUNT", out int counter);

                float x = (float)arena.GetRef<LA>(input);
                arena.GetRef<LA>(output).Value = x * 2f;               // v1 逻辑：×2
                arena.WriteField(counter, 0u, arena.ReadField<int>(counter, 0u) + 1);
            }
        }
        """;

    private const string KernelV2 = """
        using RWVDCS.Core.Execution;
        using RWVDCS.Core.PointStore;
        using RWVDCS.Core.Types;

        namespace FB.Generated;

        public sealed class DemoKernel : IScanKernel
        {
            public void Scan(PointArena arena)
            {
                arena.TryGetSid("AI.IN", out int input);
                arena.TryGetSid("AI.OUT", out int output);
                arena.TryGetSid("K.COUNT", out int counter);

                float x = (float)arena.GetRef<LA>(input);
                arena.GetRef<LA>(output).Value = x * 3f + 1f;          // v2 逻辑：×3+1
                arena.WriteField(counter, 0u, arena.ReadField<int>(counter, 0u) + 1);
            }
        }
        """;

    private static PointArena BuildArena()
    {
        var b = new ArenaBuilder();
        b.AddSlot<LA>("AI.IN", WellKnownTypeIds.LA,
            new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0f, 0, 10f));
        b.AddSlot<LA>("AI.OUT", WellKnownTypeIds.LA,
            new LA(QualityTypes.Good, false, false, false, false, false, float.MaxValue, float.MinValue, 0f, 0, 0f));
        b.AddRawSlot("K.COUNT", WellKnownTypeIds.Raw, 4); // FB 内部状态：扫描计数
        return PointArena.Create(b);
    }

    [Fact]
    public void Compile_error_returns_diagnostics_instead_of_throwing()
    {
        var result = KernelCompiler.Compile("bad", [new KernelSource("bad.cs", "class Broken {")]);
        Assert.False(result.Success);
        Assert.Null(result.AssemblyImage);
        Assert.NotEmpty(result.Errors);
        Assert.Contains("bad.cs", result.Errors[0]);
    }

    [Fact]
    public void Compiled_kernel_scans_arena()
    {
        using var arena = BuildArena();
        using var host = new KernelHost();

        LoadKernel(host, KernelV1, "gen1");
        Assert.Equal(1, host.GenerationNumber);

        for (int i = 0; i < 5; i++)
            host.Kernel!.Scan(arena);

        Assert.Equal(20f, (float)arena.GetRef<LA>(1));          // 10 × 2
        Assert.Equal(5, arena.ReadField<int>(2, 0u));           // 扫描 5 次
    }

    [Fact]
    public void HotSwap_keeps_arena_state_and_activates_new_logic()
    {
        using var arena = BuildArena();
        using var host = new KernelHost();

        LoadKernel(host, KernelV1, "gen1");
        for (int i = 0; i < 3; i++)
            host.Kernel!.Scan(arena);
        Assert.Equal(20f, (float)arena.GetRef<LA>(1));
        Assert.Equal(3, arena.ReadField<int>(2, 0u));

        // 热更换代：状态住在 Arena，换代不丢
        LoadKernel(host, KernelV2, "gen2");
        Assert.Equal(2, host.GenerationNumber);
        host.Kernel!.Scan(arena);

        Assert.Equal(31f, (float)arena.GetRef<LA>(1));          // 新逻辑 10×3+1
        Assert.Equal(4, arena.ReadField<int>(2, 0u));           // 计数从 3 继续 → 4：状态未丢
    }

    [Fact]
    public void Old_generation_is_truly_collected_after_swap()
    {
        using var arena = BuildArena();
        using var host = new KernelHost();

        var gen1Ref = LoadAndScanOnce(host, arena, KernelV1, "gen1");
        // 换代（丢弃旧代引用）
        LoadKernel(host, KernelV2, "gen2");

        Assert.True(KernelHost.WaitForCollected(gen1Ref),
            "旧代 AssemblyLoadContext 未被回收——存在对旧代内核的悬挂引用（程序集泄漏）。");

        // 新代仍可正常工作
        host.Kernel!.Scan(arena);
        Assert.Equal(31f, (float)arena.GetRef<LA>(1));
    }

    [Fact]
    public void UnloadCurrent_leaves_host_empty_and_collectible()
    {
        using var arena = BuildArena();
        using var host = new KernelHost();

        var genRef = LoadAndScanOnce(host, arena, KernelV1, "gen1");
        var retired = host.UnloadCurrent();

        Assert.Null(host.Kernel);
        Assert.NotNull(retired);
        Assert.True(KernelHost.WaitForCollected(genRef), "卸载后 ALC 未被回收。");
    }

    // NoInlining：确保方法返回后栈上不残留对内核程序集对象的引用，卸载判定才可靠
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference? LoadKernel(KernelHost host, string source, string name)
    {
        var result = KernelCompiler.Compile($"fb-{name}", [new KernelSource($"{name}.cs", source)]);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors));
        return host.LoadGeneration(result.AssemblyImage!, result.PdbImage);
    }

    /// <summary>装载并扫描一次，返回对"当前代 ALC"的弱引用（供换代/卸载后验证回收）。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference LoadAndScanOnce(KernelHost host, PointArena arena, string source, string name)
    {
        LoadKernel(host, source, name);
        host.Kernel!.Scan(arena);
        return new WeakReference(
            System.Runtime.Loader.AssemblyLoadContext.GetLoadContext(host.Kernel.GetType().Assembly));
    }
}
