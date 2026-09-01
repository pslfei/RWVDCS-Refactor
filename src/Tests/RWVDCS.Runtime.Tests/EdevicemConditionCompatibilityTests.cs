using System.Text.Json;
using RWVDCS.Blocks.RW;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.PointStore;
using RWVDCS.Core.Types;
using RWVDCS.Engineering;
using RWVDCS.Runtime;

namespace RWVDCS.Runtime.Tests;

public sealed class EdevicemConditionCompatibilityTests
{
    [Fact]
    public void LegacyV1Condition_MigratesEdevicemStateByFieldName()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"rwvdcs-edevicem-v1-compat-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using DcsRuntime runtime = RuntimeBuilder.Build(
                BuildModel(),
                new BlockCatalog(typeof(EDEVICEM).Assembly));
            DpuRuntime dpu = Assert.Single(runtime.Dpus);
            BlockCommand command = Assert.Single(dpu.Commands);
            Assert.Equal(609, dpu.Arena.GetByteLength(command.StateSid));

            byte[] legacyState = BuildLegacyState();
            var builder = new ArenaBuilder();
            byte[] commandPoint = new byte[LD.Size];
            dpu.Arena.CopySlotTo(0, commandPoint, commandPoint.Length);
            builder.AddRawSlot(
                "CMD",
                dpu.Arena.GetTypeId(0),
                commandPoint.Length,
                commandPoint);
            int legacySid = builder.AddRawSlot(
                "BLOCK1",
                dpu.Arena.GetTypeId(command.StateSid),
                legacyState.Length,
                legacyState);
            using PointArena legacyArena = PointArena.Create(builder);
            legacyArena.CycleCount = 42;
            string arenaFile = Path.Combine(directory, "DPU1.arena");
            legacyArena.SaveSnapshot(arenaFile);
            Assert.Equal(583, legacyArena.GetByteLength(legacySid));
            Assert.NotEqual(legacyArena.SchemaHash, dpu.Arena.SchemaHash);

            var manifest = new SnapshotManifest
            {
                Version = DcsRuntime.SnapshotVersion,
                SavedAtUtc = DateTime.UtcNow,
                Dpus =
                [
                    new SnapshotDpuEntry
                    {
                        ControllerId = 1,
                        Name = "DPU1",
                        File = "DPU1.arena",
                        SchemaHash = legacyArena.SchemaHash,
                        CycleSeconds = 0.25f,
                        CycleCount = 42,
                        CommandCount = 1,
                    },
                ],
            };
            File.WriteAllText(
                Path.Combine(directory, DcsRuntime.ManifestFileName),
                JsonSerializer.Serialize(manifest));

            runtime.LoadSnapshot(directory);

            var device = Assert.IsType<EDEVICEM>(command.Fc);
            Assert.True((bool)device.On);
            Assert.True((bool)device.MA);
            Assert.True((bool)device.Trip);
            Assert.True((bool)device.OpFlOn);
            Assert.True(device.onCmdActive);
            Assert.True(device.onPulseActive);
            Assert.Equal(0.2, device.onTimer, 6);
            Assert.Equal(0.4, device.onToverTimer, 6);
            Assert.True(device.manualForbid);
            Assert.True(device.debugMode);
            Assert.True((bool)device.CON);
            Assert.True(device.oldCON);
            Assert.False((bool)device.COF);
            Assert.Equal(2u, device.QualityT);
            Assert.Equal(0u, (uint)device.TAG.Value);
            Assert.Equal(1, runtime.LastConditionCompatibilityMigrationCount);
            Assert.Equal(42u, dpu.CycleCount);
            Assert.Equal(0.25f, dpu.Cycle);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void LegacyV1Condition_MigratesRsAppendedOldQState()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            $"rwvdcs-rs-v1-compat-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            using DcsRuntime runtime = RuntimeBuilder.Build(
                BuildRsModel(),
                new BlockCatalog(typeof(RS).Assembly));
            DpuRuntime dpu = Assert.Single(runtime.Dpus);
            BlockCommand command = Assert.Single(dpu.Commands);
            var rs = Assert.IsType<RS>(command.Fc);
            rs.Q.Value = true;
            rs.QN.Value = false;

            BlockStateCodec codec = BlockStateCodec.For(typeof(RS));
            Assert.Equal(256, codec.Schema.ByteLength);
            byte[] currentState = new byte[codec.Schema.ByteLength];
            codec.Flush(rs, currentState, 0);
            byte[] legacyState = currentState[..255];

            var builder = new ArenaBuilder();
            builder.AddRawSlot(
                "RS1",
                dpu.Arena.GetTypeId(command.StateSid),
                legacyState.Length,
                legacyState);
            using PointArena legacyArena = PointArena.Create(builder);
            string arenaFile = Path.Combine(directory, "DPU1.arena");
            legacyArena.SaveSnapshot(arenaFile);
            WriteManifest(directory, legacyArena.SchemaHash, "DPU1.arena");

            runtime.LoadSnapshot(directory);

            Assert.True((bool)rs.Q);
            Assert.True(rs.OldQ);
            Assert.Equal(1, runtime.LastConditionCompatibilityMigrationCount);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static byte[] BuildLegacyState()
    {
        var state = new byte[583];
        StateIo.WriteString(state, 0, "EDEVICEM", 64);
        StateIo.WriteString(state, 68, "", 64);
        StateIo.Write(state, 136, true);
        StateIo.WriteString(state, 137, "legacy", 50);

        var on = new LD(QualityTypes.Good, false, false, false, 0, true);
        var off = new LD(QualityTypes.Good, false, false, false, 0, false);
        var ma = new LD(QualityTypes.Good, false, false, false, 0, true);
        var trip = new LD(QualityTypes.Good, false, false, false, 0, true);
        var opFl = new LD(QualityTypes.Good, false, false, false, 0, true);

        StateIo.Write(state, 331, on);
        StateIo.Write(state, 341, off);
        StateIo.Write(state, 351, ma);
        StateIo.Write(state, 381, trip);
        StateIo.Write(state, 391, opFl);
        StateIo.Write(state, 411, opFl);

        StateIo.Write(state, 507, 2u);
        StateIo.Write(state, 511, 0.5d);
        StateIo.Write(state, 519, false);
        StateIo.Write(state, 520, 1.0d);
        StateIo.Write(state, 528, true);
        StateIo.Write(state, 529, true);
        StateIo.Write(state, 530, true);
        StateIo.Write(state, 531, true);
        StateIo.Write(state, 532, 1u);

        StateIo.Write(state, 536, false); // firstRun
        StateIo.Write(state, 539, true);  // onCmdActive
        StateIo.Write(state, 541, 0.2d);  // onTimer
        StateIo.Write(state, 557, 0.4d);  // onToverTimer
        StateIo.Write(state, 573, true);  // manualForbid
        StateIo.Write(state, 574, true);  // debugMode
        return state;
    }

    private static EngineeringModel BuildModel() => new()
    {
        ProjectPath = "edevicem-condition-compat-test",
        Controllers =
        [
            new ControllerModel
            {
                Id = 1,
                Address = "1",
                Name = "DPU1",
                Points =
                [
                    new PointModel
                    {
                        ID = 1,
                        Name = "CMD",
                        DataType = "LD",
                        DefaultValue = true,
                    },
                ],
                Blocks =
                [
                    new BlockModel
                    {
                        ID = 1,
                        Name = "BLOCK1",
                        FcName = "EDEVICEM",
                        Pins =
                        [
                            new PinDetailModel
                            {
                                PinName = "QualityT",
                                HasDefaultValue = true,
                                DefaultValue = 2f,
                            },
                            new PinDetailModel
                            {
                                PinName = "CON",
                                PointName = "CMD",
                                HasDefaultValue = true,
                                DefaultValue = true,
                            },
                            new PinDetailModel
                            {
                                PinName = "COF",
                                HasDefaultValue = true,
                                DefaultValue = false,
                            },
                        ],
                    },
                ],
            },
        ],
    };

    private static EngineeringModel BuildRsModel() => new()
    {
        ProjectPath = "rs-condition-compat-test",
        Controllers =
        [
            new ControllerModel
            {
                Id = 1,
                Address = "1",
                Name = "DPU1",
                Points = [],
                Blocks =
                [
                    new BlockModel
                    {
                        ID = 1,
                        Name = "RS1",
                        FcName = "RS",
                        Pins = [],
                    },
                ],
            },
        ],
    };

    private static void WriteManifest(string directory, long schemaHash, string file)
    {
        var manifest = new SnapshotManifest
        {
            Version = DcsRuntime.SnapshotVersion,
            SavedAtUtc = DateTime.UtcNow,
            Dpus =
            [
                new SnapshotDpuEntry
                {
                    ControllerId = 1,
                    Name = "DPU1",
                    File = file,
                    SchemaHash = schemaHash,
                    CycleSeconds = 0.2f,
                    CycleCount = 1,
                    CommandCount = 1,
                },
            ],
        };
        File.WriteAllText(
            Path.Combine(directory, DcsRuntime.ManifestFileName),
            JsonSerializer.Serialize(manifest));
    }
}
