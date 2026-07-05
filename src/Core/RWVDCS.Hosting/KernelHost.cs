using System.Reflection;
using System.Runtime.Loader;
using RWVDCS.Core.Execution;

namespace RWVDCS.Hosting;

/// <summary>
/// FB 内核宿主：把编译产物装入可回收 <see cref="AssemblyLoadContext"/>，
/// 支持"新代换旧代"的原子热更（状态在 Arena，换代不丢）与旧代真卸载。
/// </summary>
/// <remarks>
/// 卸载语义（.NET 官方约束）：Unload 是协作式的，须待旧代所有对象/方法栈不再被引用后
/// GC 才回收 ALC。宿主换代后不得保留旧代内核引用——这里通过换代即丢弃引用来保证。
/// <see cref="WaitForCollected"/> 仅测试/诊断用。
/// </remarks>
public sealed class KernelHost : IDisposable
{
    private sealed class CollectibleContext(string name) : AssemblyLoadContext(name, isCollectible: true);

    private sealed record Generation(CollectibleContext Context, IScanKernel Kernel, int Number, WeakReference ContextRef);

    private Generation? _current;
    private int _generationCounter;
    private bool _disposed;

    /// <summary>当前代内核；未加载时为 null。</summary>
    public IScanKernel? Kernel => _current?.Kernel;

    /// <summary>当前代号（从 1 开始，每次热更 +1）。</summary>
    public int GenerationNumber => _current?.Number ?? 0;

    /// <summary>
    /// 装载一代新内核并原子切换（旧代随后进入可回收状态）。
    /// 返回旧代的弱引用，测试/诊断可据此确认真卸载。
    /// </summary>
    public WeakReference? LoadGeneration(byte[] assemblyImage, byte[]? pdbImage, string? kernelTypeName = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        int number = ++_generationCounter;
        var context = new CollectibleContext($"fb-kernel-gen{number}");
        Assembly assembly;
        using (var pe = new MemoryStream(assemblyImage))
        using (var pdb = pdbImage is null ? null : new MemoryStream(pdbImage))
        {
            assembly = context.LoadFromStream(pe, pdb);
        }

        Type kernelType;
        if (kernelTypeName is not null)
        {
            kernelType = assembly.GetType(kernelTypeName, throwOnError: true)!;
        }
        else
        {
            kernelType = assembly.GetTypes()
                .FirstOrDefault(t => typeof(IScanKernel).IsAssignableFrom(t) && !t.IsAbstract)
                ?? throw new InvalidOperationException($"程序集 {assembly.GetName().Name} 中未找到 IScanKernel 实现。");
        }

        var kernel = (IScanKernel)Activator.CreateInstance(kernelType)!;
        var next = new Generation(context, kernel, number, new WeakReference(context));

        var old = _current;
        _current = next; // 单线程宿主假设（引擎在周期边界调用）；跨线程发布由引擎屏障保证

        return Retire(old);
    }

    /// <summary>卸载当前代（停机/移除插件路径）。</summary>
    public WeakReference? UnloadCurrent()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var old = _current;
        _current = null;
        return Retire(old);
    }

    private static WeakReference? Retire(Generation? generation)
    {
        if (generation is null) return null;
        generation.Context.Unload();
        return generation.ContextRef;
    }

    /// <summary>等待某代 ALC 被 GC 真正回收（测试/诊断用，生产不调用）。</summary>
    public static bool WaitForCollected(WeakReference contextRef, int maxGcRounds = 10)
    {
        for (int i = 0; contextRef.IsAlive && i < maxGcRounds; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        return !contextRef.IsAlive;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var old = _current;
        _current = null;
        Retire(old);
    }
}
