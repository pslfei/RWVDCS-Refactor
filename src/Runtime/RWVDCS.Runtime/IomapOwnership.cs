using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using RWVDCS.Core.PointStore;

namespace RWVDCS.Runtime;

/// <summary>
/// IOMAP 客户端接管点表。
/// 老系统语义：一旦 IOMAP 订阅/写入某个点，功能块输出不再覆盖该点，由 IOMAP 最近写入值驱动。
/// </summary>
public sealed class IomapOwnership
{
    public const string ClientInfoPrefix = "IOMAP_";
    public const string PointNamePrefix = "IOMapDirection2_";

    private readonly ConcurrentDictionary<PointSlotKey, byte> _owned = new();
    private readonly ConcurrentDictionary<PointSlotKey, object> _ownedValues = new();

    public static bool IsIomapClient(string? clientInfo)
        => !string.IsNullOrEmpty(clientInfo)
           && clientInfo.StartsWith(ClientInfoPrefix, StringComparison.Ordinal);

    public static bool HasPointNamePrefix(string? name)
        => !string.IsNullOrEmpty(name)
           && name.StartsWith(PointNamePrefix, StringComparison.Ordinal);

    public static string StripPointNamePrefix(string name)
        => HasPointNamePrefix(name) ? name[PointNamePrefix.Length..] : name;

    public bool IsOwned(PointSlotRef slot)
        => slot.IsRealPoint && _owned.ContainsKey(PointSlotKey.From(slot));

    public void Mark(PointSlotRef slot)
    {
        if (slot.IsRealPoint)
            _owned.TryAdd(PointSlotKey.From(slot), 1);
    }

    public void SetOwnedValue(PointSlotRef slot, object? value)
    {
        if (!slot.IsRealPoint)
            return;

        var key = PointSlotKey.From(slot);
        _owned.TryAdd(key, 1);
        if (value != null)
            _ownedValues[key] = value;
    }

    public bool TryGetOwnedValue(PointSlotRef slot, out object? value)
    {
        if (slot.IsRealPoint && _ownedValues.TryGetValue(PointSlotKey.From(slot), out var stored))
        {
            value = stored;
            return true;
        }

        value = null;
        return false;
    }

    public int OwnedCount => _owned.Count;

    private readonly struct PointSlotKey : IEquatable<PointSlotKey>
    {
        private readonly PointArena _arena;
        private readonly int _sid;

        private PointSlotKey(PointArena arena, int sid)
        {
            _arena = arena;
            _sid = sid;
        }

        public static PointSlotKey From(PointSlotRef slot)
            => new(slot.Arena, slot.Sid);

        public bool Equals(PointSlotKey other)
            => ReferenceEquals(_arena, other._arena) && _sid == other._sid;

        public override bool Equals(object? obj)
            => obj is PointSlotKey other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(RuntimeHelpers.GetHashCode(_arena), _sid);
    }
}
