using System.Buffers;
using System.Text.Json;
using System.Text.Json.Serialization;
using RWVDCS.Core.Blocks;

namespace RWVDCS.Runtime;

/// <summary>
/// 顶层运行时：全部 DPU + 跨 DPU 名字表 + 工况快照。
/// 老系统 DCS.Dcs（Remoting 宿主）职责中的"运行/存取"部分的新实现。
/// </summary>
public sealed class DcsRuntime : IDisposable
{
    /// <summary>工况快照清单文件名。</summary>
    public const string ManifestFileName = "manifest.json";

    /// <summary>工况格式版本。</summary>
    public const int SnapshotVersion = 1;

    private readonly Dictionary<string, PointSlotRef> _globalSlots;

    internal DcsRuntime(List<DpuRuntime> dpus, Dictionary<string, PointSlotRef> globalSlots, RuntimeBuildReport report)
    {
        Dpus = dpus;
        _globalSlots = globalSlots;
        Report = report;
        Iomap = new IomapOwnership();
        foreach (var dpu in dpus)
            dpu.Iomap = Iomap;
    }

    public IReadOnlyList<DpuRuntime> Dpus { get; }

    public RuntimeBuildReport Report { get; }

    public IomapOwnership Iomap { get; }

    /// <summary>最近一次 v1 工况加载中按白名单执行字段兼容迁移的块数量。</summary>
    public int LastConditionCompatibilityMigrationCount { get; private set; }

    /// <summary>跨 DPU 名字表（热更换代重建绑定用）。</summary>
    internal Dictionary<string, PointSlotRef> GlobalSlots => _globalSlots;

    public DpuRuntime? FindDpu(string name)
    {
        foreach (var d in Dpus)
            if (string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase))
                return d;
        return null;
    }

    // =================================================================
    // 运行控制
    // =================================================================

    /// <summary>全体 DPU 首次运行（装载后、进入周期扫描前调用一次；对齐老系统 FirstRun 流程）。</summary>
    public void FirstRun()
    {
        foreach (var dpu in Dpus)
            dpu.FirstRun();
    }

    /// <summary>全体 DPU 步进 n 个周期（对账用同步步进；实时调度由宿主层负责）。</summary>
    public void Step(int cycles = 1)
    {
        for (int c = 0; c < cycles; c++)
            foreach (var dpu in Dpus)
                dpu.Step();
    }

    // =================================================================
    // 点值访问（老系统 rtd.Master[名字] 的等价物）
    // =================================================================

    public bool TryGetSlot(string name, out PointSlotRef slot) => _globalSlots.TryGetValue(name, out slot);

    /// <summary>按名读点值（跨 DPU；块槽返回 null）。</summary>
    public object? ReadPoint(string name)
        => _globalSlots.TryGetValue(name, out var slot) && slot.IsRealPoint ? slot.ReadBoxedBuffer() : null;

    /// <summary>按名写点值（跨 DPU；带老系统 WriteValue 的类型转换语义）。</summary>
    public bool WritePoint(string name, object value)
    {
        if (!_globalSlots.TryGetValue(name, out var slot) || !slot.IsRealPoint)
            return false;
        slot.WriteBoxedBuffer(value);
        return true;
    }

    /// <summary>枚举全部真实点（名字、类别、当前值），供对账/调试导出。</summary>
    public IEnumerable<(string DpuName, string Name, PointKind Kind, object? Value)> EnumeratePoints()
    {
        foreach (var dpu in Dpus)
        {
            foreach (var (name, slot) in dpu.LocalSlots)
            {
                if (slot.IsRealPoint)
                    yield return (dpu.Name, name, slot.Kind, slot.ReadBoxedBuffer());
            }
        }
    }

    // =================================================================
    // 块状态 ⇋ Arena 槽（快照边界批量搬运）
    // =================================================================

    /// <summary>把全部块 live 状态刷入各自 Arena 槽（保存工况/热重载换代前调用）。</summary>
    public void FlushBlockStates()
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            foreach (var dpu in Dpus)
            {
                foreach (var cmd in dpu.Commands)
                {
                    var codec = BlockStateCodec.For(cmd.Fc.GetType());
                    int len = codec.Schema.ByteLength;
                    if (len == 0)
                        continue;
                    if (buffer.Length < len)
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                        buffer = ArrayPool<byte>.Shared.Rent(len);
                    }
                    codec.Flush(cmd.Fc, buffer, 0);
                    dpu.Arena.CopySlotFrom(cmd.StateSid, buffer.AsSpan(0, len), len);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    /// <summary>从 Arena 槽恢复全部块 live 状态（加载工况后调用）。</summary>
    public void LoadBlockStates()
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            foreach (var dpu in Dpus)
            {
                foreach (var cmd in dpu.Commands)
                {
                    var codec = BlockStateCodec.For(cmd.Fc.GetType());
                    int len = codec.Schema.ByteLength;
                    if (len == 0)
                        continue;
                    if (buffer.Length < len)
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                        buffer = ArrayPool<byte>.Shared.Rent(len);
                    }
                    dpu.Arena.CopySlotTo(cmd.StateSid, buffer.AsSpan(0, len), len);
                    codec.Load(cmd.Fc, buffer, 0);
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    // =================================================================
    // 工况快照（新格式：目录 = manifest.json + 每 DPU 一个 arena 镜像）
    // 相比老系统 .wrk（BinaryFormatter 全图序列化），这里是 O(内存拷贝) 的镜像落盘。
    // =================================================================

    /// <summary>保存工况到目录（覆盖同名文件）。</summary>
    public void SaveSnapshot(string directory)
    {
        Directory.CreateDirectory(directory);
        FlushBlockStates();

        var manifest = new SnapshotManifest
        {
            Version = SnapshotVersion,
            SavedAtUtc = DateTime.UtcNow,
            Dpus = [],
        };

        foreach (var dpu in Dpus)
        {
            string file = SanitizeFileName(dpu.Name) + ".arena";
            dpu.Arena.CycleCount = dpu.CycleCount;
            dpu.Arena.SaveSnapshot(Path.Combine(directory, file));
            manifest.Dpus.Add(new SnapshotDpuEntry
            {
                ControllerId = dpu.ControllerId,
                Name = dpu.Name,
                File = file,
                SchemaHash = dpu.Arena.SchemaHash,
                CycleSeconds = dpu.Cycle,
                CycleCount = dpu.CycleCount,
                CommandCount = dpu.Commands.Count,
            });
        }

        string json = JsonSerializer.Serialize(manifest, SnapshotJsonContext.Default.SnapshotManifest);
        File.WriteAllText(Path.Combine(directory, ManifestFileName), json);
    }

    /// <summary>
    /// 从目录加载工况（就地覆盖 Arena 内存 + 恢复块 live 状态 + 周期计数）。
    /// 工程结构必须与保存时一致（SchemaHash 校验），否则抛异常。
    /// </summary>
    public void LoadSnapshot(string directory)
    {
        LastConditionCompatibilityMigrationCount = ConditionV1CompatLoader.Load(this, directory);
    }

    private static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    public void Dispose()
    {
        foreach (var dpu in Dpus)
            dpu.Dispose();
    }
}

/// <summary>工况清单（manifest.json）。</summary>
public sealed class SnapshotManifest
{
    public int Version { get; set; }
    public DateTime SavedAtUtc { get; set; }
    public List<SnapshotDpuEntry> Dpus { get; set; } = [];
}

public sealed class SnapshotDpuEntry
{
    public int ControllerId { get; set; }
    public string Name { get; set; } = "";
    public string File { get; set; } = "";
    public long SchemaHash { get; set; }
    public float CycleSeconds { get; set; }
    public long CycleCount { get; set; }
    public int CommandCount { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SnapshotManifest))]
internal partial class SnapshotJsonContext : JsonSerializerContext;
