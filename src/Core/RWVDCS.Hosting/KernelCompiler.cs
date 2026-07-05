using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;

namespace RWVDCS.Hosting;

/// <summary>单个源文件：Path 仅作为调试符号里的文件标识（可为虚拟路径）。</summary>
public readonly record struct KernelSource(string Path, string Text);

/// <summary>编译结果。失败时 AssemblyImage 为 null，Errors 携带可展示的诊断信息。</summary>
public sealed record KernelCompilationResult(
    bool Success,
    byte[]? AssemblyImage,
    byte[]? PdbImage,
    ImmutableArray<string> Errors,
    ImmutableArray<string> Warnings);

/// <summary>
/// FB 内核编译器：Roslyn 全内存编译，产出 PE + PortablePdb（源码内嵌，断点可用）。
/// 对应老系统 Plug.cs 的离线 csc 编译，但升级为进程内、毫秒级、带诊断回传。
/// </summary>
public static class KernelCompiler
{
    private static readonly Lazy<ImmutableArray<MetadataReference>> s_platformReferences = new(LoadPlatformReferences);

    public static KernelCompilationResult Compile(
        string assemblyName,
        IReadOnlyList<KernelSource> sources,
        bool debug = true,
        IEnumerable<MetadataReference>? extraReferences = null)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var trees = new SyntaxTree[sources.Count];
        var embeddedTexts = new EmbeddedText[sources.Count];
        for (int i = 0; i < sources.Count; i++)
        {
            var text = SourceText.From(sources[i].Text, Encoding.UTF8);
            trees[i] = CSharpSyntaxTree.ParseText(text, parseOptions, path: sources[i].Path);
            // 源码内嵌进 PDB：内存编译的代码也能被调试器直接展示与断点（"现调现改"体验的关键）
            embeddedTexts[i] = EmbeddedText.FromSource(sources[i].Path, text);
        }

        var references = extraReferences is null
            ? s_platformReferences.Value
            : s_platformReferences.Value.AddRange(extraReferences);

        var compilation = CSharpCompilation.Create(
            assemblyName,
            trees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                // FB 源码一律禁 unsafe（原则2：上层不再出现指针）
                allowUnsafe: false,
                optimizationLevel: debug ? OptimizationLevel.Debug : OptimizationLevel.Release,
                deterministic: true));

        using var pe = new MemoryStream();
        using var pdb = new MemoryStream();
        var emitResult = compilation.Emit(pe, pdb,
            options: new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb),
            embeddedTexts: embeddedTexts);

        var errors = emitResult.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(FormatDiagnostic).ToImmutableArray();
        var warnings = emitResult.Diagnostics
            .Where(d => d.Severity == DiagnosticSeverity.Warning)
            .Select(FormatDiagnostic).ToImmutableArray();

        return emitResult.Success
            ? new KernelCompilationResult(true, pe.ToArray(), pdb.ToArray(), errors, warnings)
            : new KernelCompilationResult(false, null, null, errors, warnings);
    }

    private static string FormatDiagnostic(Diagnostic d)
    {
        var pos = d.Location.GetLineSpan();
        return $"{pos.Path}({pos.StartLinePosition.Line + 1},{pos.StartLinePosition.Character + 1}): {d.Id} {d.GetMessage()}";
    }

    /// <summary>
    /// 引用集 = 运行时 TPA 全量 + 当前进程里的 RWVDCS.Core / RWVDCS.Hosting。
    /// 全量 TPA 换来的是 FB 源码"想用什么 BCL 就用什么"的老系统体验；首次构建约百毫秒，进程内缓存。
    /// </summary>
    private static ImmutableArray<MetadataReference> LoadPlatformReferences()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var builder = ImmutableArray.CreateBuilder<MetadataReference>();

        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
        {
            foreach (var path in tpa.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                if (seen.Add(path) && File.Exists(path))
                    builder.Add(MetadataReference.CreateFromFile(path));
            }
        }

        foreach (var anchor in new[]
                 {
                     typeof(Core.Execution.IScanKernel).Assembly,   // RWVDCS.Core
                     typeof(LowLevel.MappedMemory).Assembly,        // RWVDCS.LowLevel（Core 的传递依赖）
                     typeof(KernelCompiler).Assembly,               // RWVDCS.Hosting
                 })
        {
            if (!string.IsNullOrEmpty(anchor.Location) && seen.Add(anchor.Location))
                builder.Add(MetadataReference.CreateFromFile(anchor.Location));
        }

        return builder.ToImmutable();
    }
}
