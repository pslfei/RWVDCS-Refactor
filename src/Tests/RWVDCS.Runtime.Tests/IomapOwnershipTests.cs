using RWVDCS.Core.PointStore;
using RWVDCS.Core.Types;
using RWVDCS.Runtime;

namespace RWVDCS.Runtime.Tests;

public sealed class IomapOwnershipTests
{
    [Fact]
    public void Same_sid_in_different_arenas_has_independent_ownership()
    {
        using PointArena firstArena = CreateArena();
        using PointArena secondArena = CreateArena();
        var first = new PointSlotRef(firstArena, 0, PointKind.LA);
        var second = new PointSlotRef(secondArena, 0, PointKind.LA);
        var ownership = new IomapOwnership();

        ownership.SetOwnedValue(first, 12.5f);

        Assert.True(ownership.IsOwned(first));
        Assert.False(ownership.IsOwned(second));
        Assert.True(ownership.TryGetOwnedValue(first, out object? value));
        Assert.Equal(12.5f, value);
        Assert.False(ownership.TryGetOwnedValue(second, out _));
    }

    [Fact]
    public void Concurrent_mark_read_and_write_keeps_numeric_slot_keys_consistent()
    {
        const int pointCount = 128;
        using PointArena arena = CreateArena(pointCount);
        var slots = Enumerable.Range(0, pointCount)
            .Select(sid => new PointSlotRef(arena, sid, PointKind.LA))
            .ToArray();
        var ownership = new IomapOwnership();

        Parallel.For(0, 100_000, i =>
        {
            PointSlotRef slot = slots[i % slots.Length];
            ownership.Mark(slot);
            ownership.SetOwnedValue(slot, (float)i);
            Assert.True(ownership.IsOwned(slot));
            Assert.True(ownership.TryGetOwnedValue(slot, out _));
        });

        Assert.Equal(pointCount, ownership.OwnedCount);
        Assert.Equal(pointCount, ownership.OwnedValueCount);
        Assert.False(ownership.IsOwned(default));
    }

    private static PointArena CreateArena(int pointCount = 1)
    {
        var builder = new ArenaBuilder();
        for (int i = 0; i < pointCount; i++)
            builder.AddSlot<LA>($"AI{i:D4}", WellKnownTypeIds.LA, default);
        return PointArena.Create(builder);
    }
}
