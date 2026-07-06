using System.Text.Json;
using System.Text.Json.Serialization;

namespace RWVDCS.Runtime;

/// <summary>
/// 工况清单（condition.json）。工况 = 自带工程库副本 + 全量 Arena 镜像：
/// 与快照不同，工况在任何工程演化后都可完整重现保存时刻的系统（先装其内嵌工程库再回放数据）。
/// </summary>
public sealed class ConditionManifest
{
    public string Kind { get; set; } = "condition";
    public int FormatVersion { get; set; } = 1;
    public string Name { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public int ProjectVersion { get; set; }
    public DateTime SavedAtUtc { get; set; }
    public string? Comment { get; set; }
    /// <summary>内嵌工程库文件名（相对工况目录）。</summary>
    public string MdbFile { get; set; } = "project.mdb";
    /// <summary>保存时的源工程库路径（信息性）。</summary>
    public string? SourceMdbPath { get; set; }
    public int DpuCount { get; set; }
    public long TotalCycleCount { get; set; }
}

/// <summary>存储条目摘要（Web 列表用）。</summary>
public sealed record StoreEntryInfo(
    string Name, string Kind, string Fingerprint, int ProjectVersion,
    DateTime SavedAtUtc, string? Comment, long SizeBytes, string Directory);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ConditionManifest))]
internal partial class ConditionJsonContext : JsonSerializerContext;

/// <summary>
/// 工况/快照仓库：数据目录下的命名条目管理（保存/加载/列表/删除）。
/// 目录布局：
/// <code>
/// {root}/conditions/{名称}/condition.json + project.mdb + manifest.json + *.arena
/// {root}/snapshots/{名称}/manifest.json + *.snap2
/// </code>
/// 运行时重建与状态回放由宿主编排（RuntimeHost），这里只管文件与清单。
/// </summary>
public sealed class ConditionStore
{
    public string Root { get; }
    public string ConditionsDir => Path.Combine(Root, "conditions");
    public string SnapshotsDir => Path.Combine(Root, "snapshots");

    public ConditionStore(string root)
    {
        Root = Path.GetFullPath(root);
        Directory.CreateDirectory(ConditionsDir);
        Directory.CreateDirectory(SnapshotsDir);
    }

    // =================================================================
    // 工况
    // =================================================================

    /// <summary>保存工况：内嵌工程库副本 + v1 全量镜像 + 工况清单。返回目录。</summary>
    public string SaveCondition(DcsRuntime runtime, string name, string sourceMdbPath,
        string fingerprint, int projectVersion, string? comment)
    {
        string dir = EntryDir(ConditionsDir, name);
        Directory.CreateDirectory(dir);

        // 1. 工程库副本（工况的核心：工程信息随身携带）
        File.Copy(sourceMdbPath, Path.Combine(dir, "project.mdb"), overwrite: true);

        // 2. 全量 Arena 镜像（v1 格式，manifest.json + *.arena）
        runtime.SaveSnapshot(dir);

        // 3. 工况清单
        var manifest = new ConditionManifest
        {
            Name = name,
            Fingerprint = fingerprint,
            ProjectVersion = projectVersion,
            SavedAtUtc = DateTime.UtcNow,
            Comment = comment,
            SourceMdbPath = sourceMdbPath,
            DpuCount = runtime.Dpus.Count,
            TotalCycleCount = runtime.Dpus.Sum(d => (long)d.CycleCount),
        };
        File.WriteAllText(Path.Combine(dir, "condition.json"),
            JsonSerializer.Serialize(manifest, ConditionJsonContext.Default.ConditionManifest));
        return dir;
    }

    public ConditionManifest ReadConditionManifest(string name)
    {
        string path = Path.Combine(EntryDir(ConditionsDir, name), "condition.json");
        return JsonSerializer.Deserialize(File.ReadAllText(path), ConditionJsonContext.Default.ConditionManifest)
               ?? throw new InvalidDataException($"工况清单损坏：{path}");
    }

    /// <summary>工况内嵌工程库的绝对路径。</summary>
    public string ConditionMdbPath(string name)
        => Path.Combine(EntryDir(ConditionsDir, name), ReadConditionManifest(name).MdbFile);

    public string ConditionDir(string name) => EntryDir(ConditionsDir, name);

    // =================================================================
    // 快照
    // =================================================================

    public string SnapshotDir(string name) => EntryDir(SnapshotsDir, name);

    public SnapshotV2Manifest SaveSnapshot(DcsRuntime runtime, string name,
        string fingerprint, int projectVersion, string? comment)
        => SnapshotV2.Save(runtime, SnapshotDir(name), fingerprint, projectVersion, comment);

    public SnapshotLoadReport LoadSnapshot(DcsRuntime runtime, string name, string currentFingerprint)
        => SnapshotV2.Load(runtime, SnapshotDir(name), currentFingerprint);

    // =================================================================
    // 列表 / 删除
    // =================================================================

    public List<StoreEntryInfo> ListConditions()
    {
        var result = new List<StoreEntryInfo>();
        foreach (var dir in SafeEnumDirs(ConditionsDir))
        {
            string manifestPath = Path.Combine(dir, "condition.json");
            if (!File.Exists(manifestPath))
                continue;
            try
            {
                var m = JsonSerializer.Deserialize(File.ReadAllText(manifestPath), ConditionJsonContext.Default.ConditionManifest)!;
                result.Add(new StoreEntryInfo(Path.GetFileName(dir), "condition", m.Fingerprint,
                    m.ProjectVersion, m.SavedAtUtc, m.Comment, DirSize(dir), dir));
            }
            catch
            {
                // 损坏条目跳过
            }
        }
        return result.OrderByDescending(e => e.SavedAtUtc).ToList();
    }

    public List<StoreEntryInfo> ListSnapshots()
    {
        var result = new List<StoreEntryInfo>();
        foreach (var dir in SafeEnumDirs(SnapshotsDir))
        {
            string manifestPath = Path.Combine(dir, SnapshotV2.ManifestFileName);
            if (!File.Exists(manifestPath))
                continue;
            try
            {
                var m = SnapshotV2.ReadManifest(dir);
                result.Add(new StoreEntryInfo(Path.GetFileName(dir), "snapshot", m.Fingerprint,
                    m.ProjectVersion, m.SavedAtUtc, m.Comment, DirSize(dir), dir));
            }
            catch
            {
            }
        }
        return result.OrderByDescending(e => e.SavedAtUtc).ToList();
    }

    public bool DeleteCondition(string name) => DeleteEntry(ConditionsDir, name);
    public bool DeleteSnapshot(string name) => DeleteEntry(SnapshotsDir, name);

    private bool DeleteEntry(string parent, string name)
    {
        string dir = EntryDir(parent, name);
        if (!Directory.Exists(dir))
            return false;
        Directory.Delete(dir, recursive: true);
        return true;
    }

    private static IEnumerable<string> SafeEnumDirs(string parent)
        => Directory.Exists(parent) ? Directory.EnumerateDirectories(parent) : [];

    private static long DirSize(string dir)
        => Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);

    /// <summary>条目目录（名称做文件名清洗；拒绝路径穿越）。</summary>
    private static string EntryDir(string parent, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("名称不能为空", nameof(name));
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        string dir = Path.GetFullPath(Path.Combine(parent, name));
        if (!dir.StartsWith(Path.GetFullPath(parent), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("非法名称", nameof(name));
        return dir;
    }
}
