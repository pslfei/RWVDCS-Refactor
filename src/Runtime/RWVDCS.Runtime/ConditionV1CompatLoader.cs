using System.Reflection;
using System.Text.Json;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.PointStore;
using RWVDCS.Core.Types;

namespace RWVDCS.Runtime;

/// <summary>
/// v1 全量工况兼容加载器。结构完全一致时走原始快速恢复；仅对已知的
/// EDEVICEM 583B→609B 布局变化执行字段级迁移，其他未知差异继续拒绝。
/// </summary>
internal static class ConditionV1CompatLoader
{
    private const int LegacyEdevicemLength = 583;
    private const int CurrentEdevicemLength = 609;
    private const int LegacyRsLength = 255;
    private const int CurrentRsLength = 256;

    private static readonly IReadOnlyDictionary<string, (int Offset, int Length)> LegacyEdevicemFields =
        new Dictionary<string, (int, int)>(StringComparer.Ordinal)
        {
            ["FcName"] = (0, 68),
            ["FcCode"] = (68, 68),
            ["runable"] = (136, 1),
            ["Description"] = (137, 54),
            ["Enable"] = (191, 10),
            ["EnOn"] = (201, 10),
            ["EnOff"] = (211, 10),
            ["ToM"] = (221, 10),
            ["ReqA"] = (231, 10),
            ["AOn"] = (241, 10),
            ["AOff"] = (251, 10),
            ["FBOn"] = (261, 10),
            ["FBOff"] = (271, 10),
            ["Loc"] = (281, 10),
            ["FBat"] = (291, 10),
            ["FDev"] = (301, 10),
            ["POpe"] = (311, 10),
            ["FSpr"] = (321, 10),
            ["On"] = (331, 10),
            ["Off"] = (341, 10),
            ["MA"] = (351, 10),
            ["NoCon"] = (361, 10),
            ["FBFl"] = (371, 10),
            ["Trip"] = (381, 10),
            ["OpFl"] = (391, 10),
            ["Forbid"] = (401, 10),
            ["OpFlOn"] = (411, 10),
            ["OpFlOff"] = (421, 10),
            // TAG: LA→LP32，旧原始字节不可转换；首个扫描周期会重新组装。
            ["ResetM"] = (507, 4),
            ["SetT"] = (511, 8),
            ["FLB"] = (519, 1),
            ["Tover"] = (520, 8),
            ["EnLoc"] = (528, 1),
            ["EnFBat"] = (529, 1),
            ["EnFDev"] = (530, 1),
            ["EnFSpr"] = (531, 1),
            ["MP"] = (532, 4),
            ["firstRun"] = (536, 1),
            ["oldFBOn"] = (537, 1),
            ["oldFBOff"] = (538, 1),
            ["onCmdActive"] = (539, 1),
            ["offCmdActive"] = (540, 1),
            ["onTimer"] = (541, 8),
            ["offTimer"] = (549, 8),
            ["onToverTimer"] = (557, 8),
            ["offToverTimer"] = (565, 8),
            ["manualForbid"] = (573, 1),
            ["debugMode"] = (574, 1),
        };

    public static int Load(DcsRuntime runtime, string directory)
    {
        string manifestPath = Path.Combine(directory, DcsRuntime.ManifestFileName);
        SnapshotManifest manifest = JsonSerializer.Deserialize(
                File.ReadAllText(manifestPath),
                SnapshotJsonContext.Default.SnapshotManifest)
            ?? throw new InvalidDataException($"工况清单损坏：{manifestPath}");
        if (manifest.Version != DcsRuntime.SnapshotVersion)
            throw new InvalidDataException(
                $"工况版本 {manifest.Version} 不受支持（当前 {DcsRuntime.SnapshotVersion}）。");

        var byName = manifest.Dpus.ToDictionary(dpu => dpu.Name, StringComparer.OrdinalIgnoreCase);
        var migratedEdevicemCommands = new List<BlockCommand>();
        int migratedBlockCount = 0;
        foreach (DpuRuntime dpu in runtime.Dpus)
        {
            if (!byName.TryGetValue(dpu.Name, out SnapshotDpuEntry? entry))
                throw new InvalidDataException($"工况中缺少 DPU：{dpu.Name}");

            string snapshotPath = ResolveSnapshotPath(directory, entry.File);
            if (entry.SchemaHash == dpu.Arena.SchemaHash)
            {
                dpu.Arena.LoadSnapshotInPlace(snapshotPath);
            }
            else
            {
                migratedBlockCount += LoadByNamedSlots(dpu, snapshotPath, migratedEdevicemCommands);
            }

            dpu.CycleCount = (uint)entry.CycleCount;
            dpu.Cycle = entry.CycleSeconds;
        }

        runtime.LoadBlockStates();
        foreach (BlockCommand command in migratedEdevicemCommands)
        {
            command.SyncInputsForStateRestore();
            PrimeEdevicemCommandEdges(command.Fc);
            FlushCommandState(command);
        }
        return migratedBlockCount;
    }

    private static string ResolveSnapshotPath(string directory, string file)
    {
        string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(directory, file));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"工况 Arena 路径越界：{file}");
        return path;
    }

    private static int LoadByNamedSlots(
        DpuRuntime dpu,
        string snapshotPath,
        ICollection<BlockCommand> migratedEdevicemCommands)
    {
        using PointArena oldArena = PointArena.LoadFrom(snapshotPath);
        byte[] buffer = [];
        int migratedBlockCount = 0;

        for (int newSid = 0; newSid < dpu.Arena.SlotCount; newSid++)
        {
            string? name = dpu.Arena.GetName(newSid);
            if (name == null || !oldArena.TryGetSid(name, out int oldSid))
                throw new InvalidDataException($"旧工况 {dpu.Name} 中缺少槽位：{name ?? $"SID={newSid}"}");

            int oldLength = oldArena.GetByteLength(oldSid);
            int newLength = dpu.Arena.GetByteLength(newSid);
            if (oldLength == newLength)
            {
                EnsureCapacity(ref buffer, newLength);
                oldArena.CopySlotTo(oldSid, buffer, newLength);
                dpu.Arena.CopySlotFrom(newSid, buffer, newLength);
                continue;
            }

            BlockCommand? command = dpu.FindCommand(name);
            if (command != null
                && command.FcName.Equals("EDEVICEM", StringComparison.OrdinalIgnoreCase)
                && oldLength == LegacyEdevicemLength
                && newLength == CurrentEdevicemLength)
            {
                MigrateEdevicemSlot(oldArena, oldSid, dpu.Arena, newSid, command);
                migratedEdevicemCommands.Add(command);
                migratedBlockCount++;
                continue;
            }

            if (command != null
                && command.FcName.Equals("RS", StringComparison.OrdinalIgnoreCase)
                && oldLength == LegacyRsLength
                && newLength == CurrentRsLength)
            {
                MigrateRsSlot(oldArena, oldSid, dpu.Arena, newSid, command);
                migratedBlockCount++;
                continue;
            }

            throw new InvalidDataException(
                $"工况 {dpu.Name}/{name} 状态槽不兼容：旧 {oldLength}B，新 {newLength}B。");
        }

        dpu.Arena.CycleCount = oldArena.CycleCount;
        return migratedBlockCount;
    }

    private static void MigrateEdevicemSlot(
        PointArena oldArena,
        int oldSid,
        PointArena newArena,
        int newSid,
        BlockCommand command)
    {
        byte[] oldState = new byte[LegacyEdevicemLength];
        byte[] newState = new byte[CurrentEdevicemLength];
        oldArena.CopySlotTo(oldSid, oldState, oldState.Length);

        BlockStateCodec codec = BlockStateCodec.For(command.Fc.GetType());
        if (codec.Schema.ByteLength != CurrentEdevicemLength)
            throw new InvalidDataException(
                $"当前 EDEVICEM 状态长度不是预期的 {CurrentEdevicemLength}B：{codec.Schema.ByteLength}B。");

        // 先写入新对象的 MDB 默认值，确保新增命令 Input 和 QualityT 有正确初值。
        codec.Flush(command.Fc, newState, 0);
        foreach (var (fieldName, oldField) in LegacyEdevicemFields)
        {
            if (!codec.Schema.TryGetField(fieldName, out BlockStateField? newField)
                || newField.ByteLength != oldField.Length)
            {
                throw new InvalidDataException($"EDEVICEM 兼容字段布局异常：{fieldName}");
            }

            oldState.AsSpan(oldField.Offset, oldField.Length)
                .CopyTo(newState.AsSpan(newField.Offset, newField.ByteLength));
        }

        // 旧实现没有独立脉冲状态；若快照保存时输出仍为高，恢复剩余 SetT 硬脉宽保护。
        codec.Load(command.Fc, newState, 0);
        SetBooleanField(command.Fc, "onPulseActive", ReadPinValue(command.Fc, "On"));
        SetBooleanField(command.Fc, "offPulseActive", ReadPinValue(command.Fc, "Off"));
        codec.Flush(command.Fc, newState, 0);
        newArena.CopySlotFrom(newSid, newState, newState.Length);
    }

    private static void MigrateRsSlot(
        PointArena oldArena,
        int oldSid,
        PointArena newArena,
        int newSid,
        BlockCommand command)
    {
        byte[] oldState = new byte[LegacyRsLength];
        byte[] newState = new byte[CurrentRsLength];
        oldArena.CopySlotTo(oldSid, oldState, oldState.Length);

        BlockStateCodec codec = BlockStateCodec.For(command.Fc.GetType());
        if (codec.Schema.ByteLength != CurrentRsLength)
            throw new InvalidDataException(
                $"当前 RS 状态长度不是预期的 {CurrentRsLength}B：{codec.Schema.ByteLength}B。");

        codec.Flush(command.Fc, newState, 0);
        oldState.CopyTo(newState, 0); // OldQ 是唯一新增且追加在末尾的 1B 字段。
        codec.Load(command.Fc, newState, 0);
        SetBooleanField(command.Fc, "OldQ", ReadPinValue(command.Fc, "Q"));
        codec.Flush(command.Fc, newState, 0);
        newArena.CopySlotFrom(newSid, newState, newState.Length);
    }

    private static bool ReadPinValue(Function function, string fieldName)
    {
        FieldInfo field = function.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidDataException($"EDEVICEM 缺少字段：{fieldName}");
        return field.GetValue(function) is IValuable valuable
               && Convert.ToBoolean(valuable.Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void SetBooleanField(Function function, string fieldName, bool value)
    {
        FieldInfo field = function.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidDataException($"EDEVICEM 缺少字段：{fieldName}");
        if (field.FieldType != typeof(bool))
            throw new InvalidDataException($"EDEVICEM 字段类型异常：{fieldName}");
        field.SetValue(function, value);
    }

    private static void PrimeEdevicemCommandEdges(Function function)
    {
        foreach ((string Pin, string History) pair in new[]
                 {
                     ("CON", "oldCON"),
                     ("COF", "oldCOF"),
                     ("CTA", "oldCTA"),
                     ("CTM", "oldCTM"),
                     ("CAK", "oldCAK"),
                     ("CFB", "oldCFB"),
                     ("CRS", "oldCRS"),
                     ("CDB", "oldCDB"),
                 })
        {
            SetBooleanField(function, pair.History, ReadPinValue(function, pair.Pin));
        }
    }

    private static void FlushCommandState(BlockCommand command)
    {
        BlockStateCodec codec = BlockStateCodec.For(command.Fc.GetType());
        byte[] state = new byte[codec.Schema.ByteLength];
        codec.Flush(command.Fc, state, 0);
        ((DpuRuntime)command.Dpu).Arena.CopySlotFrom(command.StateSid, state, state.Length);
    }

    private static void EnsureCapacity(ref byte[] buffer, int length)
    {
        if (buffer.Length < length)
            buffer = new byte[length];
    }
}
