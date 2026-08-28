using System.Buffers;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RWVDCS.Core.Blocks;

namespace RWVDCS.Runtime;

// =====================================================================
// 快照 v2：增量（相对装配基线的变化槽位）+ Brotli 压缩 + 跨版本兼容加载
//
// 语义（与工况的分工）：
//   快照 = 假定工程未变，只存"点数据 + 块内部数据"，小而快；
//   工况 = 自带工程库副本（见 ConditionStore），可完整重现。
// 兼容性：快照记录按【名字】寻址并随带保存时的块状态布局目录（SchemaCatalog），
//   工程指纹不一致时走"按名 + 按字段"转换加载，尽可能保留点值与块内部状态。
// =====================================================================

/// <summary>快照清单（manifest.json）。</summary>
public sealed class SnapshotV2Manifest
{
    public string Kind { get; set; } = "snapshot";
    public int FormatVersion { get; set; } = 2;
    public string Fingerprint { get; set; } = "";
    public int ProjectVersion { get; set; }
    public DateTime SavedAtUtc { get; set; }
    public string? Comment { get; set; }
    public List<SnapshotV2DpuEntry> Dpus { get; set; } = [];

    /// <summary>保存时全部在用功能码的状态布局（跨版本字段级转换的解码依据）。</summary>
    public Dictionary<string, SchemaCatalogEntry> SchemaCatalog { get; set; } = [];
}

public sealed class SnapshotV2DpuEntry
{
    public int ControllerId { get; set; }
    public string Name { get; set; } = "";
    public string File { get; set; } = "";
    public long SchemaHash { get; set; }
    public float CycleSeconds { get; set; }
    public long CycleCount { get; set; }
    public int ChangedSlots { get; set; }
    public int TotalSlots { get; set; }
}

public sealed class SchemaCatalogEntry
{
    public long LayoutHash { get; set; }
    public int ByteLength { get; set; }
    public List<SchemaFieldEntry> Fields { get; set; } = [];
}

public sealed class SchemaFieldEntry
{
    public string Name { get; set; } = "";
    /// <summary>规范化类型名：LA/LD/LP/LP32、System.Single、System.Single[]、string 等。</summary>
    public string Type { get; set; } = "";
    public int Offset { get; set; }
    public int Size { get; set; }
}

/// <summary>快照加载报告。</summary>
public sealed class SnapshotLoadReport
{
    public bool CompatMode { get; internal set; }
    public int PointsApplied { get; internal set; }
    public int PointsSkipped { get; internal set; }
    public int BlocksRawCopied { get; internal set; }
    public int BlocksFieldConverted { get; internal set; }
    public int BlocksSkipped { get; internal set; }
    public int DpusMissing { get; internal set; }
    public List<string> Messages { get; } = [];

    public override string ToString() => CompatMode
        ? $"兼容加载：点 {PointsApplied:N0}（跳过 {PointsSkipped:N0}）、块直拷 {BlocksRawCopied:N0} / 字段转换 {BlocksFieldConverted:N0}（跳过 {BlocksSkipped:N0}）、缺失 DPU {DpusMissing}"
        : $"快速加载：变化槽 {PointsApplied + BlocksRawCopied:N0}";
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SnapshotV2Manifest))]
internal partial class SnapshotV2JsonContext : JsonSerializerContext;

/// <summary>快照 v2 的保存/加载实现。调用方（宿主）负责在周期边界串行化。</summary>
public static class SnapshotV2
{
    public const string ManifestFileName = "manifest.json";
    private static ReadOnlySpan<byte> FileMagic => "RWSNAP2\0"u8;

    private const byte RecordPoint = 0;
    private const byte RecordBlock = 1;

    // =================================================================
    // 保存
    // =================================================================
    public static SnapshotV2Manifest Save(DcsRuntime runtime, string directory,
        string fingerprint, int projectVersion, string? comment)
    {
        Directory.CreateDirectory(directory);
        runtime.FlushBlockStates();

        var manifest = new SnapshotV2Manifest
        {
            Fingerprint = fingerprint,
            ProjectVersion = projectVersion,
            SavedAtUtc = DateTime.UtcNow,
            Comment = comment,
        };

        // 布局目录：全部在用功能码（跨版本转换的解码依据）
        var fcSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dpu in runtime.Dpus)
        {
            foreach (var cmd in dpu.Commands)
            {
                if (!fcSeen.Add(cmd.FcName))
                    continue;
                manifest.SchemaCatalog[cmd.FcName] = BuildCatalogEntry(BlockStateSchema.For(cmd.Fc.GetType()));
            }
        }

        foreach (var dpu in runtime.Dpus)
        {
            string file = Sanitize(dpu.Name) + ".snap2";
            int changed = SaveDpu(dpu, Path.Combine(directory, file));
            manifest.Dpus.Add(new SnapshotV2DpuEntry
            {
                ControllerId = dpu.ControllerId,
                Name = dpu.Name,
                File = file,
                SchemaHash = dpu.Arena.SchemaHash,
                CycleSeconds = dpu.Cycle,
                CycleCount = dpu.CycleCount,
                ChangedSlots = changed,
                TotalSlots = dpu.Arena.SlotCount,
            });
        }

        File.WriteAllText(Path.Combine(directory, ManifestFileName),
            JsonSerializer.Serialize(manifest, SnapshotV2JsonContext.Default.SnapshotV2Manifest));
        return manifest;
    }

    private static SchemaCatalogEntry BuildCatalogEntry(BlockStateSchema schema)
    {
        var entry = new SchemaCatalogEntry { LayoutHash = schema.LayoutHash, ByteLength = schema.ByteLength };
        foreach (var f in schema.Fields)
        {
            entry.Fields.Add(new SchemaFieldEntry
            {
                Name = f.Name,
                Type = CanonicalTypeName(f),
                Offset = f.Offset,
                Size = f.ByteLength,
            });
        }
        return entry;
    }

    /// <summary>字段类型的规范化名（跨版本按名匹配后还要按类型核对再拷字节）。</summary>
    private static string CanonicalTypeName(BlockStateField f) => f.Kind switch
    {
        StateFieldKind.FixedString => "string",
        StateFieldKind.FixedArray => (f.Field.FieldType.GetElementType()!.FullName ?? "?") + "[]",
        _ => f.Field.FieldType.FullName ?? "?",
    };

    private static int SaveDpu(DpuRuntime dpu, string path)
    {
        var arena = dpu.Arena;
        using var arenaAccess = arena.AcquireAccessLease();
        byte[] baseline = dpu.DecompressInitialData();
        var current = arena.DataRegion;

        string tmp = path + ".tmp";
        int changed = 0;
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
        {
            fs.Write(FileMagic);
            using var brotli = new BrotliStream(fs, CompressionLevel.Fastest);
            using var w = new BinaryWriter(brotli, Encoding.UTF8);

            w.Write(dpu.Name);
            w.Write(arena.SchemaHash);

            // 先数记录数（两遍扫描避免占大内存缓冲）
            for (int sid = 0; sid < arena.SlotCount; sid++)
            {
                var (off, len) = arena.GetSlotExtent(sid);
                if (!current.Slice(off, len).SequenceEqual(baseline.AsSpan(off, len)))
                    changed++;
            }
            w.Write(changed);

            // 命令索引：块槽记录附带功能码名
            var fcBySid = new Dictionary<int, string>();
            foreach (var cmd in dpu.Commands)
                fcBySid[cmd.StateSid] = cmd.FcName;

            for (int sid = 0; sid < arena.SlotCount; sid++)
            {
                var (off, len) = arena.GetSlotExtent(sid);
                var cur = current.Slice(off, len);
                if (cur.SequenceEqual(baseline.AsSpan(off, len)))
                    continue;

                bool isBlock = fcBySid.TryGetValue(sid, out string? fcName);
                w.Write(isBlock ? RecordBlock : RecordPoint);
                w.Write(sid);
                w.Write(arena.GetName(sid) ?? "");
                if (isBlock)
                    w.Write(fcName!);
                w.Write(len);
                w.Write(cur);
            }
        }
        File.Move(tmp, path, overwrite: true);
        return changed;
    }

    // =================================================================
    // 加载
    // =================================================================
    public static SnapshotV2Manifest ReadManifest(string directory)
    {
        string path = Path.Combine(directory, ManifestFileName);
        return JsonSerializer.Deserialize(File.ReadAllText(path), SnapshotV2JsonContext.Default.SnapshotV2Manifest)
               ?? throw new InvalidDataException($"快照清单损坏：{path}");
    }

    /// <summary>
    /// 加载快照。指纹一致走快速路径（基线回填 + SID 直拷）；
    /// 不一致走兼容路径（按名字 + 布局目录字段级转换，尽可能保留）。
    /// </summary>
    public static SnapshotLoadReport Load(DcsRuntime runtime, string directory, string currentFingerprint)
    {
        var manifest = ReadManifest(directory);
        if (manifest.Kind != "snapshot" || manifest.FormatVersion != 2)
            throw new InvalidDataException($"不是快照 v2 目录：{directory}");

        return manifest.Fingerprint == currentFingerprint
            ? LoadFast(runtime, directory, manifest)
            : LoadCompat(runtime, directory, manifest);
    }

    /// <summary>快速路径：工程一致 ⇒ 基线回填 + 变化槽按 SID 直拷 + 块状态整体重载。</summary>
    private static SnapshotLoadReport LoadFast(DcsRuntime runtime, string directory, SnapshotV2Manifest manifest)
    {
        var report = new SnapshotLoadReport();
        var byName = manifest.Dpus.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var dpu in runtime.Dpus)
        {
            if (!byName.TryGetValue(dpu.Name, out var entry))
                throw new InvalidDataException($"快照缺少 DPU：{dpu.Name}");
            if (entry.SchemaHash != dpu.Arena.SchemaHash)
                throw new InvalidDataException($"快照 SchemaHash 与运行时不一致（{dpu.Name}）——指纹匹配但结构不同？");

            dpu.Arena.RestoreDataRegion(dpu.DecompressInitialData());

            foreach (var rec in ReadRecords(Path.Combine(directory, entry.File)))
            {
                int slotLength = dpu.Arena.GetByteLength(rec.Sid);
                if (rec.Bytes.Length != slotLength)
                    throw new InvalidDataException($"槽长不符：{dpu.Name} sid={rec.Sid}");
                dpu.Arena.CopySlotFrom(rec.Sid, rec.Bytes, rec.Bytes.Length);
                if (rec.Kind == RecordBlock) report.BlocksRawCopied++;
                else report.PointsApplied++;
            }

            dpu.CycleCount = (uint)entry.CycleCount;
            dpu.Cycle = entry.CycleSeconds;
        }

        runtime.LoadBlockStates();
        return report;
    }

    /// <summary>
    /// 兼容路径：只回放能匹配上的状态（点按名+同类别；块按名，布局一致直拷、
    /// 不一致按字段名+类型转换），其余保持当前运行值。不回填基线——
    /// "尽可能保留"语义（想要完全确定的结果请先重载工程再加载快照，或改用工况）。
    /// </summary>
    private static SnapshotLoadReport LoadCompat(DcsRuntime runtime, string directory, SnapshotV2Manifest manifest)
    {
        var report = new SnapshotLoadReport { CompatMode = true };
        report.Messages.Add($"快照指纹 {manifest.Fingerprint} 与当前工程不一致，进入兼容转换加载。");

        byte[] scratch = ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            foreach (var entry in manifest.Dpus)
            {
                var dpu = runtime.FindDpu(entry.Name);
                if (dpu == null)
                {
                    report.DpusMissing++;
                    report.Messages.Add($"DPU {entry.Name} 在当前工程中不存在，跳过其 {entry.ChangedSlots} 条记录。");
                    continue;
                }

                var commandIndex = new Dictionary<string, BlockCommand>(StringComparer.OrdinalIgnoreCase);
                foreach (var cmd in dpu.Commands)
                    commandIndex.TryAdd(cmd.Name, cmd);

                foreach (var rec in ReadRecords(Path.Combine(directory, entry.File)))
                {
                    if (rec.Name.Length == 0)
                    {
                        report.PointsSkipped++;
                        continue;
                    }

                    if (rec.Kind == RecordPoint)
                        ApplyPointCompat(runtime, dpu, rec, report);
                    else
                        ApplyBlockCompat(dpu, commandIndex, rec, manifest, report, ref scratch);
                }

                dpu.CycleCount = (uint)entry.CycleCount;
                dpu.Cycle = entry.CycleSeconds;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(scratch);
        }

        return report;
    }

    private static void ApplyPointCompat(DcsRuntime runtime, DpuRuntime preferredDpu, in SlotRecord rec, SnapshotLoadReport report)
    {
        // 优先同名 DPU 本地，再全局（跨 DPU 点名首见语义）
        PointSlotRef slot;
        if (!preferredDpu.LocalSlots.TryGetValue(rec.Name, out slot) &&
            !runtime.TryGetSlot(rec.Name, out slot))
        {
            report.PointsSkipped++;
            return;
        }

        if (!slot.IsRealPoint)
        {
            report.PointsSkipped++;
            return;
        }

        int targetLength = slot.Arena.GetByteLength(slot.Sid);
        if (targetLength != rec.Bytes.Length)
        {
            // 点类型变了（LA↔LD 等）：布局不同不硬套
            report.PointsSkipped++;
            return;
        }

        slot.Arena.CopySlotFrom(slot.Sid, rec.Bytes, rec.Bytes.Length);
        report.PointsApplied++;
    }

    private static void ApplyBlockCompat(DpuRuntime dpu, Dictionary<string, BlockCommand> commandIndex,
        in SlotRecord rec, SnapshotV2Manifest manifest, SnapshotLoadReport report, ref byte[] scratch)
    {
        if (!commandIndex.TryGetValue(rec.Name, out var cmd))
        {
            report.BlocksSkipped++;
            return;
        }

        var newSchema = BlockStateSchema.For(cmd.Fc.GetType());
        manifest.SchemaCatalog.TryGetValue(rec.FcName, out var oldCatalog);

        // 同功能码 + 布局指纹一致 ⇒ 槽字节直拷 + codec 重载
        if (string.Equals(cmd.FcName, rec.FcName, StringComparison.OrdinalIgnoreCase) &&
            oldCatalog != null && oldCatalog.LayoutHash == newSchema.LayoutHash &&
            rec.Bytes.Length >= newSchema.ByteLength)
        {
            int slotLength = dpu.Arena.GetByteLength(cmd.StateSid);
            int copyLength = Math.Min(rec.Bytes.Length, slotLength);
            dpu.Arena.CopySlotFrom(cmd.StateSid, rec.Bytes.AsSpan(0, copyLength), copyLength);
            var codec = BlockStateCodec.For(cmd.Fc.GetType());
            EnsureCapacity(ref scratch, codec.Schema.ByteLength);
            dpu.Arena.CopySlotTo(cmd.StateSid, scratch.AsSpan(0, codec.Schema.ByteLength),
                codec.Schema.ByteLength);
            codec.Load(cmd.Fc, scratch, 0);
            report.BlocksRawCopied++;
            return;
        }

        // 布局不同（热更过/版本差异/功能码变了）：按字段名+类型逐个搬
        if (oldCatalog == null)
        {
            report.BlocksSkipped++;
            report.Messages.Add($"[{dpu.Name}] {rec.Name}：快照缺少 {rec.FcName} 的布局目录，无法转换。");
            return;
        }

        // 先把当前 live 状态刷进缓冲作为底版，再用旧字段覆盖，最后 Load 回 live
        var codec2 = BlockStateCodec.For(cmd.Fc.GetType());
        EnsureCapacity(ref scratch, codec2.Schema.ByteLength);
        codec2.Flush(cmd.Fc, scratch, 0);

        int moved = 0;
        foreach (var oldField in oldCatalog.Fields)
        {
            if (!newSchema.TryGetField(oldField.Name, out var nf))
                continue;
            if (!string.Equals(CanonicalTypeName(nf), oldField.Type, StringComparison.Ordinal))
                continue;
            if (oldField.Offset + oldField.Size > rec.Bytes.Length)
                continue;

            int n = Math.Min(oldField.Size, nf.ByteLength);
            rec.Bytes.AsSpan(oldField.Offset, n).CopyTo(scratch.AsSpan(nf.Offset, n));
            moved++;
        }

        codec2.Load(cmd.Fc, scratch, 0);
        // 同步刷回 Arena 槽，保持槽与 live 一致
        codec2.Flush(cmd.Fc, scratch, 0);
        dpu.Arena.CopySlotFrom(cmd.StateSid, scratch.AsSpan(0, codec2.Schema.ByteLength),
            codec2.Schema.ByteLength);

        if (moved > 0)
            report.BlocksFieldConverted++;
        else
            report.BlocksSkipped++;
    }

    private static void EnsureCapacity(ref byte[] buffer, int size)
    {
        if (buffer.Length < size)
        {
            ArrayPool<byte>.Shared.Return(buffer);
            buffer = ArrayPool<byte>.Shared.Rent(size);
        }
    }

    // =================================================================
    // 记录流读取
    // =================================================================
    private readonly record struct SlotRecord(byte Kind, int Sid, string Name, string FcName, byte[] Bytes);

    private static IEnumerable<SlotRecord> ReadRecords(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        Span<byte> magic = stackalloc byte[8];
        fs.ReadExactly(magic);
        if (!magic.SequenceEqual(FileMagic))
            throw new InvalidDataException($"不是快照 v2 数据文件：{path}");

        using var brotli = new BrotliStream(fs, CompressionMode.Decompress);
        using var r = new BinaryReader(brotli, Encoding.UTF8);

        r.ReadString();   // dpuName（清单已含，此处冗余自描述）
        r.ReadInt64();    // schemaHash
        int count = r.ReadInt32();

        for (int i = 0; i < count; i++)
        {
            byte kind = r.ReadByte();
            int sid = r.ReadInt32();
            string name = r.ReadString();
            string fcName = kind == RecordBlock ? r.ReadString() : "";
            int len = r.ReadInt32();
            byte[] bytes = r.ReadBytes(len);
            if (bytes.Length != len)
                throw new EndOfStreamException($"快照数据不完整：{path}");
            yield return new SlotRecord(kind, sid, name, fcName, bytes);
        }
    }

    private static string Sanitize(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}
