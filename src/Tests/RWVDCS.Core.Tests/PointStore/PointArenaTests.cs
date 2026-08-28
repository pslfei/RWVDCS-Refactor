using RWVDCS.Core.PointStore;
using RWVDCS.Core.Types;

namespace RWVDCS.Core.Tests.PointStore;

public class PointArenaTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "rwvdcs-tests", Guid.NewGuid().ToString("N"));

    public PointArenaTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* 测试清理尽力而为 */ }
    }

    private string TempFile(string name) => Path.Combine(_dir, name);

    private static ArenaBuilder SampleBuilder()
    {
        var b = new ArenaBuilder();
        b.AddSlot<LA>("DPU1.AI001", WellKnownTypeIds.LA,
            new LA(QualityTypes.Good, false, false, false, false, false, 100f, 0f, 0f, 0, 42.5f));
        b.AddSlot<LD>("DPU1.DI001", WellKnownTypeIds.LD,
            new LD(QualityTypes.Good, false, false, false, 0, true));
        b.AddSlot<LP32>("DPU1.PK001", WellKnownTypeIds.LP32, new LP32 { Value = 0xDEADu });
        b.AddRawSlot(null, WellKnownTypeIds.Raw, 64); // 匿名原始块（模拟 FB 状态区）
        return b;
    }

    [Fact]
    public void Create_assigns_sids_in_registration_order()
    {
        using var arena = PointArena.Create(SampleBuilder());

        Assert.Equal(4, arena.SlotCount);
        Assert.True(arena.TryGetSid("DPU1.AI001", out int sid0));
        Assert.Equal(0, sid0);
        Assert.True(arena.TryGetSid("dpu1.di001", out int sid1)); // 大小写不敏感（老系统语义）
        Assert.Equal(1, sid1);
        Assert.Equal("DPU1.PK001", arena.GetName(2));
        Assert.Null(arena.GetName(3));
        Assert.Equal(WellKnownTypeIds.LA, arena.GetTypeId(0));
    }

    [Fact]
    public void Duplicate_name_throws_at_build_registration()
    {
        var b = new ArenaBuilder();
        b.AddSlot<LD>("X", WellKnownTypeIds.LD, default);
        Assert.Throws<InvalidOperationException>(() => b.AddSlot<LD>("x", WellKnownTypeIds.LD, default));
    }

    [Fact]
    public void Initial_values_are_written()
    {
        using var arena = PointArena.Create(SampleBuilder());

        ref var ai = ref arena.GetRef<LA>(0);
        Assert.Equal(42.5f, (float)ai);
        ref var di = ref arena.GetRef<LD>(1);
        Assert.True(di);
        ref var pk = ref arena.GetRef<LP32>(2);
        Assert.Equal(0xDEADu, (uint)pk.Value);
    }

    [Fact]
    public void GetRef_mutation_is_in_place()
    {
        using var arena = PointArena.Create(SampleBuilder());

        ref var ai = ref arena.GetRef<LA>(0);
        ai.Value = 77f;

        // 重新取引用，应看到同一份存储
        ref var again = ref arena.GetRef<LA>(0);
        Assert.Equal(77f, (float)again);
    }

    [Fact]
    public void Field_read_write_by_fsid()
    {
        using var arena = PointArena.Create(SampleBuilder());
        arena.TryGetSid("DPU1.AI001", out int sid);

        // LA.buffer 位于偏移 24（布局守卫已断言）
        long fsid = Fsid.Make(sid, 24);
        arena.WriteField(fsid, 3.14f);
        Assert.Equal(3.14f, arena.ReadField<float>(fsid));
        Assert.Equal(3.14f, (float)arena.GetRef<LA>(sid));

        Fsid.Split(fsid, out int s2, out uint off);
        Assert.Equal(sid, s2);
        Assert.Equal(24u, off);
    }

    [Fact]
    public void Field_access_out_of_bounds_throws()
    {
        using var arena = PointArena.Create(SampleBuilder());
        Assert.Throws<ArgumentOutOfRangeException>(() => arena.ReadField<float>(0, LA.Size));
        Assert.Throws<ArgumentOutOfRangeException>(() => arena.WriteField(1, 9, 1.0d));
    }

    [Fact]
    public void CopySlot_supports_negate()
    {
        var b = new ArenaBuilder();
        b.AddRawSlot("src", WellKnownTypeIds.Raw, 4, new byte[] { 0x0F, 0xF0, 0x00, 0xFF });
        b.AddRawSlot("dst", WellKnownTypeIds.Raw, 4);
        using var arena = PointArena.Create(b);

        arena.CopySlot(0, 0, 1, 0, 4, negate: false);
        Assert.Equal(new byte[] { 0x0F, 0xF0, 0x00, 0xFF }, arena.GetSlotSpan(1).ToArray());

        arena.CopySlot(0, 0, 1, 0, 4, negate: true);
        Assert.Equal(new byte[] { 0xF0, 0x0F, 0xFF, 0x00 }, arena.GetSlotSpan(1).ToArray());
    }

    [Fact]
    public void Snapshot_roundtrip_preserves_data_and_cycle()
    {
        string path = TempFile("a.ckpt");
        using var arena = PointArena.Create(SampleBuilder());
        arena.GetRef<LA>(0).Value = 99f;
        arena.CycleCount = 12345;
        arena.SaveSnapshot(path);

        // 改动运行值后就地恢复
        arena.GetRef<LA>(0).Value = 1f;
        arena.CycleCount = 99999;
        arena.LoadSnapshotInPlace(path);

        Assert.Equal(99f, (float)arena.GetRef<LA>(0));
        Assert.Equal(12345, arena.CycleCount);
    }

    [Fact]
    public void Snapshot_from_different_schema_is_rejected()
    {
        string path = TempFile("b.ckpt");
        using (var arena = PointArena.Create(SampleBuilder()))
        {
            arena.SaveSnapshot(path);
        }

        var other = new ArenaBuilder();
        other.AddSlot<LD>("Different", WellKnownTypeIds.LD, default);
        using var arena2 = PointArena.Create(other);

        Assert.Throws<InvalidDataException>(() => arena2.LoadSnapshotInPlace(path));
    }

    [Fact]
    public void LoadFrom_rebuilds_full_arena_from_snapshot()
    {
        string path = TempFile("c.ckpt");
        using (var arena = PointArena.Create(SampleBuilder()))
        {
            arena.GetRef<LA>(0).Value = 55f;
            arena.CycleCount = 42;
            arena.SaveSnapshot(path);
        }

        using var restored = PointArena.LoadFrom(path);
        Assert.Equal(4, restored.SlotCount);
        Assert.True(restored.TryGetSid("DPU1.AI001", out int sid));
        Assert.Equal(55f, (float)restored.GetRef<LA>(sid));
        Assert.Equal(42, restored.CycleCount);
    }

    [Fact]
    public void FileBacked_arena_persists_through_flush()
    {
        string backing = TempFile("live.arena");
        using (var arena = PointArena.Create(SampleBuilder(), backingFile: backing))
        {
            arena.GetRef<LA>(0).Value = 88f;
            arena.Flush();
        }

        // 文件即镜像：直接从文件重建
        using var restored = PointArena.LoadFrom(backing);
        Assert.Equal(88f, (float)restored.GetRef<LA>(0));
    }

    [Fact]
    public void Large_arena_100k_points_snapshot_roundtrip()
    {
        var b = new ArenaBuilder();
        for (int i = 0; i < 100_000; i++)
            b.AddSlot<LA>($"P{i:D6}", WellKnownTypeIds.LA, default);
        using var arena = PointArena.Create(b);

        for (int i = 0; i < 100_000; i += 997)
            arena.GetRef<LA>(i).Value = i * 0.5f;

        string path = TempFile("big.ckpt");
        arena.SaveSnapshot(path);
        using var restored = PointArena.LoadFrom(path);

        for (int i = 0; i < 100_000; i += 997)
            Assert.Equal(i * 0.5f, (float)restored.GetRef<LA>(i));
    }

    [Fact]
    public async Task Concurrent_field_access_and_dispose_never_uses_released_mapping()
    {
        const int iterations = 25;
        const int workersPerIteration = 4;

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            var arena = PointArena.Create(SampleBuilder());
            using var start = new ManualResetEventSlim(initialState: false);
            int readyWorkers = 0;
            int completedWrites = 0;

            Task[] workers = Enumerable.Range(0, workersPerIteration)
                .Select(worker => Task.Run(() =>
                {
                    Interlocked.Increment(ref readyWorkers);
                    start.Wait();
                    try
                    {
                        while (true)
                        {
                            arena.WriteField(0, 24, iteration + worker + 0.5f);
                            _ = arena.ReadField<float>(0, 24);
                            Interlocked.Increment(ref completedWrites);
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        // Dispose 开始后，旧字段访问只能以此方式结束，不能触碰已释放映射。
                    }
                }))
                .ToArray();

            Assert.True(SpinWait.SpinUntil(
                () => Volatile.Read(ref readyWorkers) == workersPerIteration,
                TimeSpan.FromSeconds(5)));
            start.Set();
            Assert.True(SpinWait.SpinUntil(
                () => Volatile.Read(ref completedWrites) >= 100,
                TimeSpan.FromSeconds(5)));

            arena.Dispose();
            await Task.WhenAll(workers).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Throws<ObjectDisposedException>(() => arena.WriteField(0, 24, 1f));
            Assert.Throws<ObjectDisposedException>(() => arena.ReadField<float>(0, 24));
        }
    }

    [Fact]
    public async Task Access_lease_keeps_zero_copy_views_alive_until_released()
    {
        var arena = PointArena.Create(SampleBuilder());
        PointArena.AccessLease access = arena.AcquireAccessLease();

        Task disposeTask = Task.Run(arena.Dispose);
        await Task.Delay(50);
        Assert.False(disposeTask.IsCompleted);

        arena.GetRef<LA>(0).Value = 66f;
        Assert.Equal(66f, (float)arena.GetRef<LA>(0));
        Assert.Equal(LA.Size, arena.GetSlotSpan(0).Length);

        access.Dispose();
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Throws<ObjectDisposedException>(() => arena.AcquireAccessLease());
    }
}
