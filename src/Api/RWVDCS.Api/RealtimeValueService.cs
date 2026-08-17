using System.Globalization;
using System.Reflection;
using RWVDCS.CompatProtocol;
using RWVDCS.Core.Types;
using RWVDCS.Runtime;

namespace RWVDCS.Api;

/// <summary>兼容订阅的单项结果。</summary>
public readonly record struct RealtimeSubscribeResult(long Handle, CompatValueKind ValueKind, bool Found, bool Writable);

/// <summary>Host 内旧客户端会话。</summary>
internal sealed class RealtimeCompatSession
{
    public required int ClientHandle { get; init; }
    public bool UseDataChange { get; set; } = true;
    public bool Paused { get; set; }
    public List<long> Order { get; } = [];
    public HashSet<long> Set { get; } = [];
    public Dictionary<long, CompatValue> LastSent { get; } = [];
    public Dictionary<long, CompatValue> LastPolled { get; } = [];
}

internal interface IRealtimeValueAccessor
{
    CompatValueKind ValueKind { get; }
    bool Writable { get; }
    object? Read();
    bool Write(object? value);
}

internal sealed class PointBufferValueAccessor(PointSlotRef slot) : IRealtimeValueAccessor
{
    public PointSlotRef Slot { get; } = slot;

    public CompatValueKind ValueKind => Slot.Kind switch
    {
        PointKind.LA => CompatValueKind.Single,
        PointKind.LD => CompatValueKind.Boolean,
        PointKind.LP => CompatValueKind.UInt16,
        PointKind.LP32 => CompatValueKind.UInt32,
        _ => CompatValueKind.Null,
    };

    public bool Writable => Slot.IsRealPoint;

    public object? Read() => Slot.ReadBoxedBuffer();

    public bool Write(object? value)
    {
        if (value == null || !Slot.IsRealPoint)
            return false;
        try
        {
            object typed = Slot.Kind switch
            {
                PointKind.LA => Convert.ToSingle(value, CultureInfo.InvariantCulture),
                PointKind.LD => Convert.ToBoolean(value, CultureInfo.InvariantCulture),
                PointKind.LP => Convert.ToUInt16(value, CultureInfo.InvariantCulture),
                PointKind.LP32 => Convert.ToUInt32(value, CultureInfo.InvariantCulture),
                _ => throw new InvalidOperationException(),
            };
            Slot.WriteBoxedBuffer(typed);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

internal sealed class PointFieldValueAccessor(PointSlotRef slot, string field, Type fieldType) : IRealtimeValueAccessor
{
    public CompatValueKind ValueKind { get; } = RealtimeValueService.ToValueKind(fieldType);
    public bool Writable { get; } = !field.Equals(nameof(LA.CurOverState), StringComparison.OrdinalIgnoreCase);

    public object? Read()
        => PointFieldAccess.TryRead(slot, field, out object? value, out _) ? value : null;

    public bool Write(object? value) => Writable && PointFieldAccess.WriteObject(slot, field, value);
}

internal sealed class BlockFieldValueAccessor(BlockCommand command, FieldInfo field) : IRealtimeValueAccessor
{
    public CompatValueKind ValueKind { get; } = RealtimeValueService.ToValueKind(Unwrap(field.FieldType));
    public bool Writable => true;

    public object? Read()
    {
        object? raw = field.GetValue(command.Fc);
        return raw is IValuable valuable ? valuable.Value : raw;
    }

    public bool Write(object? value)
    {
        if (value == null)
            return false;
        return command.SetField(field.Name, value);
    }

    private static Type Unwrap(Type type)
    {
        if (type == typeof(LA)) return typeof(float);
        if (type == typeof(LD)) return typeof(bool);
        if (type == typeof(LP)) return typeof(ushort);
        if (type == typeof(LP32)) return typeof(uint);
        return type.IsEnum ? Enum.GetUnderlyingType(type) : type;
    }
}

internal sealed class LegacyHandleBinding
{
    public required long Handle { get; init; }
    public required string OriginalName { get; init; }
    public string CanonicalName { get; set; } = "";
    public bool IomapOwned { get; set; }
    public bool Found { get; set; }
    public bool Writable { get; set; }
    public CompatValueKind ValueKind { get; set; }
    public ulong RuntimeGeneration { get; set; }
    public IRealtimeValueAccessor? Accessor { get; set; }
    public object? LastIomapValue { get; set; }
}

/// <summary>
/// 新 Host 内的实时兼容值服务。名称在 Subscribe/Runtime 换代时解析，正常读写只按逻辑 Handle
/// 访问已绑定的 PointSlot/字段访问器；同时维护会话变化基线和 IOMAP 语义。
/// </summary>
public sealed class RealtimeValueService : IDisposable
{
    private const string IomapPointPrefix = "IOMapDirection2_";
    private const string IomapClientPrefix = "IOMAP_";

    private readonly RuntimeHost _host;
    private readonly object _gate = new();
    private readonly Dictionary<string, LegacyHandleBinding> _byName = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<long, LegacyHandleBinding> _byHandle = [];
    private readonly Dictionary<int, RealtimeCompatSession> _sessions = [];
    // 名称订阅的失败回退路径也必须是 O(1)。旧实现对每个未命中的名称遍历全部
    // DPU/Command；IOMAP 一次订阅数万个名称时会退化为 O(N*M)，最终触发管道超时。
    // 索引绑定到 Runtime 实例，工程换代后在第一次重新绑定时整体重建。
    private DcsRuntime? _indexedRuntime;
    private Dictionary<string, DpuRuntime> _dpuIndex = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, BlockCommand> _globalCommandIndex = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<DpuRuntime, Dictionary<string, BlockCommand>> _commandsByDpu = [];
    private readonly Timer _changeTimer;
    private long _nextHandle = 1;
    private int _nextClient = 1;
    private int _scanBusy;
    private bool _changing;
    private bool _disposed;
    private ulong _generation = 1;

    public RealtimeValueService(RuntimeHost host, int changeScanIntervalMs = 200)
    {
        _host = host;
        _host.RuntimeChanging += OnRuntimeChanging;
        _host.RuntimeSwapped += OnRuntimeSwapped;
        _changeTimer = new Timer(_ => ScanChanges(), null,
            Math.Max(50, changeScanIntervalMs), Math.Max(50, changeScanIntervalMs));
    }

    public event Action<int, long[], CompatValue[]>? DataChanged;
    public event Action<ulong>? RuntimeChanging;
    public event Action<ulong, long[]>? RuntimeRebound;

    public ulong RuntimeGeneration
    {
        get { lock (_gate) return _generation; }
    }

    public int BindingCount
    {
        get { lock (_gate) return _byHandle.Count; }
    }

    public int WritableBindingCount
    {
        get { lock (_gate) return _byHandle.Values.Count(binding => binding.Found && binding.Writable); }
    }

    public int Attach(int requestedClientHandle, bool useDataChange)
    {
        lock (_gate)
        {
            int handle = requestedClientHandle > 0 ? requestedClientHandle : _nextClient++;
            if (handle >= _nextClient)
                _nextClient = handle + 1;
            _sessions[handle] = new RealtimeCompatSession
            {
                ClientHandle = handle,
                UseDataChange = useDataChange,
            };
            return handle;
        }
    }

    public bool Renew(int clientHandle)
    {
        lock (_gate) return _sessions.ContainsKey(clientHandle);
    }

    public void Detach(int clientHandle)
    {
        lock (_gate) _sessions.Remove(clientHandle);
    }

    public void DetachAll()
    {
        lock (_gate) _sessions.Clear();
    }

    public void SetPaused(int clientHandle, bool paused)
    {
        lock (_gate)
            if (_sessions.TryGetValue(clientHandle, out var session))
                session.Paused = paused;
    }

    public void SetUseDataChange(int clientHandle, bool use)
    {
        lock (_gate)
            if (_sessions.TryGetValue(clientHandle, out var session))
                session.UseDataChange = use;
    }

    public RealtimeSubscribeResult[] Subscribe(int clientHandle, string[] names)
    {
        if (names == null || names.Length == 0)
            return [];

        DcsRuntime? runtime = _host.Runtime;
        var results = new RealtimeSubscribeResult[names.Length];
        lock (_gate)
        {
            if (_changing || runtime == null || !_sessions.TryGetValue(clientHandle, out var session))
                return results;

            for (int i = 0; i < names.Length; i++)
            {
                string? original = names[i];
                if (original == null)
                {
                    results[i] = new RealtimeSubscribeResult(-1, CompatValueKind.Null, false, false);
                    continue;
                }

                if (!_byName.TryGetValue(original, out var binding))
                {
                    binding = new LegacyHandleBinding { Handle = _nextHandle++, OriginalName = original };
                    Bind(binding, runtime);
                    _byName[original] = binding;
                    _byHandle[binding.Handle] = binding;
                }
                else if (binding.RuntimeGeneration != _generation)
                {
                    Bind(binding, runtime);
                }

                if (!binding.Found)
                {
                    results[i] = new RealtimeSubscribeResult(-1, binding.ValueKind, false, false);
                    continue;
                }

                if (session.Set.Add(binding.Handle))
                    session.Order.Add(binding.Handle);
                results[i] = new RealtimeSubscribeResult(binding.Handle, binding.ValueKind, true, binding.Writable);
            }
        }
        return results;
    }

    /// <summary>
    /// 无会话的旧 HTTP API 订阅入口。旧 EmbeddedHttpApi 返回的是进程级 FSID，
    /// 不具备 Remoting 客户端会话，因此这里只建立/复用全局名称绑定，不加入变化事件会话。
    /// 返回的 Handle 可继续交给 <see cref="Read"/> 和 <see cref="WriteByHandles"/>。
    /// </summary>
    public RealtimeSubscribeResult[] SubscribeByNames(string[] names)
    {
        if (names == null || names.Length == 0)
            return [];

        LegacyHandleBinding?[] bindings = GetOrBindByNames(names);
        var results = new RealtimeSubscribeResult[names.Length];
        lock (_gate)
        {
            for (int i = 0; i < bindings.Length; i++)
            {
                LegacyHandleBinding? binding = bindings[i];
                results[i] = binding == null
                    ? new RealtimeSubscribeResult(-1, CompatValueKind.Null, false, false)
                    : new RealtimeSubscribeResult(binding.Handle, binding.ValueKind, true, binding.Writable);
            }
        }
        return results;
    }

    public void Unsubscribe(int clientHandle, long[]? handles)
    {
        lock (_gate)
        {
            if (!_sessions.TryGetValue(clientHandle, out var session))
                return;
            if (handles == null)
            {
                session.Order.Clear();
                session.Set.Clear();
                session.LastSent.Clear();
                session.LastPolled.Clear();
                return;
            }
            foreach (long handle in handles)
            {
                if (session.Set.Remove(handle))
                    session.Order.Remove(handle);
                session.LastSent.Remove(handle);
                session.LastPolled.Remove(handle);
            }
        }
    }

    public void UnsubscribeEverywhere(long handle)
    {
        lock (_gate)
        {
            foreach (var session in _sessions.Values)
            {
                if (session.Set.Remove(handle))
                    session.Order.Remove(handle);
                session.LastSent.Remove(handle);
                session.LastPolled.Remove(handle);
            }
        }
    }

    public CompatValue[] Read(long[] handles)
    {
        if (handles == null || handles.Length == 0)
            return [];
        var result = new CompatValue[handles.Length];
        lock (_gate)
        {
            if (_changing)
                return result;
            for (int i = 0; i < handles.Length; i++)
            {
                try
                {
                    object? value = _byHandle.TryGetValue(handles[i], out var binding) && binding.Found
                        ? binding.Accessor?.Read()
                        : null;
                    result[i] = CompatValue.FromObject(value);
                }
                catch
                {
                    result[i] = new CompatValue(CompatValueKind.Null, null);
                }
            }
        }
        return result;
    }

    /// <summary>REST/诊断入口按名读取；与 Pipe 订阅共用同一 Binding 和访问器规则。</summary>
    public object?[] ReadByNames(string[] names)
    {
        if (names == null || names.Length == 0)
            return [];
        var bindings = GetOrBindByNames(names);
        var result = new object?[names.Length];
        lock (_gate)
        {
            if (_changing)
                return result;
            for (int i = 0; i < bindings.Length; i++)
            {
                try { result[i] = bindings[i]?.Accessor?.Read(); }
                catch { result[i] = null; }
            }
        }
        return result;
    }

    /// <summary>REST/诊断入口按名写入；正常 Remoting 热路径仍使用 Handle Write。</summary>
    public bool[] WriteByNames(string[] names, object?[] values, string? clientInfo, bool[]? iomapOwned = null)
    {
        if (names == null || names.Length == 0)
            return [];
        var result = new bool[names.Length];
        if (values == null || values.Length == 0)
            return result;
        var bindings = GetOrBindByNames(names);
        bool iomapClient = IsIomapClient(clientInfo);
        int count = Math.Min(names.Length, values.Length);
        lock (_gate)
        {
            if (_changing)
                return result;
            for (int i = 0; i < count; i++)
            {
                LegacyHandleBinding? binding = bindings[i];
                bool owned = iomapClient || (iomapOwned != null && i < iomapOwned.Length && iomapOwned[i]);
                if (binding?.Accessor == null || values[i] == null)
                    continue;
                try
                {
                    if (owned)
                        binding.IomapOwned = true;
                    result[i] = binding.Accessor.Write(values[i]);
                    if (result[i] && owned && binding.Accessor is PointBufferValueAccessor point)
                    {
                        _host.Runtime?.Iomap.SetOwnedValue(point.Slot, values[i]);
                        binding.LastIomapValue = values[i];
                    }
                }
                catch { result[i] = false; }
            }
        }
        return result;
    }

    public bool[] MarkIomapByNames(string[] names)
    {
        if (names == null || names.Length == 0)
            return [];
        var bindings = GetOrBindByNames(names);
        var result = new bool[names.Length];
        lock (_gate)
        {
            DcsRuntime? runtime = _host.Runtime;
            if (_changing || runtime == null)
                return result;
            for (int i = 0; i < bindings.Length; i++)
            {
                if (bindings[i]?.Accessor is not PointBufferValueAccessor point)
                    continue;
                bindings[i]!.IomapOwned = true;
                runtime.Iomap.Mark(point.Slot);
                result[i] = true;
            }
        }
        return result;
    }

    public CompatValue[] ReadAll(int clientHandle)
    {
        long[] handles;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(clientHandle, out var session))
                return [];
            handles = session.Order.ToArray();
        }
        return Read(handles);
    }

    public bool[] Write(int clientHandle, long[] handles, CompatValue[] values, string? clientInfo)
    {
        if (handles == null || handles.Length == 0)
            return [];
        var results = new bool[handles.Length];
        if (values == null || values.Length == 0)
            return results;

        bool iomapClient = IsIomapClient(clientInfo);
        int count = Math.Min(handles.Length, values.Length);
        lock (_gate)
        {
            if (_changing || !_sessions.ContainsKey(clientHandle))
                return results;
            for (int i = 0; i < count; i++)
            {
                if (_byHandle.TryGetValue(handles[i], out var binding) && binding.Found && binding.Writable)
                {
                    if (iomapClient)
                        binding.IomapOwned = true;
                    if (binding.Accessor == null || values[i].Kind == CompatValueKind.Null)
                        continue;
                    try
                    {
                        object? value = values[i].ToObject();
                        results[i] = binding.Accessor.Write(value);
                        if (results[i] && binding.Accessor is PointBufferValueAccessor point
                            && (binding.IomapOwned || iomapClient))
                        {
                            _host.Runtime?.Iomap.SetOwnedValue(point.Slot, value);
                            binding.LastIomapValue = value;
                        }
                    }
                    catch
                    {
                        results[i] = false;
                    }
                }
            }
        }
        return results;
    }

    /// <summary>
    /// 无会话的旧 HTTP API 按 Handle 写入入口。Handle 必须先由
    /// <see cref="SubscribeByNames"/> 建立；IOMAP 判定和 Runtime 换代重绑规则与管道写入一致。
    /// </summary>
    public bool[] WriteByHandles(long[] handles, object?[] values, string? clientInfo)
    {
        if (handles == null || handles.Length == 0)
            return [];
        var results = new bool[handles.Length];
        if (values == null || values.Length == 0)
            return results;

        bool iomapClient = IsIomapClient(clientInfo);
        int count = Math.Min(handles.Length, values.Length);
        lock (_gate)
        {
            if (_changing)
                return results;

            for (int i = 0; i < count; i++)
            {
                if (!_byHandle.TryGetValue(handles[i], out var binding)
                    || !binding.Found || !binding.Writable || binding.Accessor == null || values[i] == null)
                    continue;

                try
                {
                    if (iomapClient)
                        binding.IomapOwned = true;
                    results[i] = binding.Accessor.Write(values[i]);
                    if (results[i] && binding.Accessor is PointBufferValueAccessor point
                        && (binding.IomapOwned || iomapClient))
                    {
                        _host.Runtime?.Iomap.SetOwnedValue(point.Slot, values[i]);
                        binding.LastIomapValue = values[i];
                    }
                }
                catch
                {
                    results[i] = false;
                }
            }
        }
        return results;
    }

    public KeyValuePair<long, CompatValue>[] PollChanges(int clientHandle)
    {
        long[] handles;
        lock (_gate)
        {
            if (!_sessions.TryGetValue(clientHandle, out var session))
                return [];
            handles = session.Order.ToArray();
        }
        CompatValue[] values = Read(handles);
        var changed = new List<KeyValuePair<long, CompatValue>>();
        lock (_gate)
        {
            if (!_sessions.TryGetValue(clientHandle, out var session))
                return [];
            for (int i = 0; i < handles.Length; i++)
            {
                if (!session.LastPolled.TryGetValue(handles[i], out var previous)
                    || !ValueEquals(previous, values[i]))
                {
                    changed.Add(new KeyValuePair<long, CompatValue>(handles[i], values[i]));
                    session.LastPolled[handles[i]] = values[i];
                }
            }
        }
        return changed.ToArray();
    }

    private void ScanChanges()
    {
        if (_disposed || Interlocked.CompareExchange(ref _scanBusy, 1, 0) != 0)
            return;
        try
        {
            List<RealtimeCompatSession> sessions;
            long[] union;
            lock (_gate)
            {
                if (_changing)
                    return;
                sessions = _sessions.Values
                    .Where(s => s.UseDataChange && !s.Paused && s.Order.Count > 0)
                    .ToList();
                if (sessions.Count == 0)
                    return;
                union = sessions.SelectMany(s => s.Set).Distinct().ToArray();
            }

            CompatValue[] values = Read(union);
            var byHandle = new Dictionary<long, CompatValue>(union.Length);
            for (int i = 0; i < union.Length; i++)
                byHandle[union[i]] = values[i];

            var deliveries = new List<(int Client, long[] Handles, CompatValue[] Values)>();
            lock (_gate)
            {
                foreach (var snapshot in sessions)
                {
                    if (!_sessions.TryGetValue(snapshot.ClientHandle, out var session)
                        || !session.UseDataChange || session.Paused)
                        continue;
                    var changedHandles = new List<long>();
                    var changedValues = new List<CompatValue>();
                    foreach (long handle in session.Order)
                    {
                        if (!byHandle.TryGetValue(handle, out var value))
                            continue;
                        if (!session.LastSent.TryGetValue(handle, out var previous)
                            || !ValueEquals(previous, value))
                        {
                            session.LastSent[handle] = value;
                            changedHandles.Add(handle);
                            changedValues.Add(value);
                        }
                    }
                    if (changedHandles.Count > 0)
                        deliveries.Add((session.ClientHandle, changedHandles.ToArray(), changedValues.ToArray()));
                }
            }

            foreach (var delivery in deliveries)
                DataChanged?.Invoke(delivery.Client, delivery.Handles, delivery.Values);
        }
        catch (Exception ex)
        {
            _host.Log.Warn("兼容通讯", $"变化扫描失败：{ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _scanBusy, 0);
        }
    }

    private LegacyHandleBinding?[] GetOrBindByNames(string[] names)
    {
        var result = new LegacyHandleBinding?[names.Length];
        DcsRuntime? runtime = _host.Runtime;
        lock (_gate)
        {
            if (_changing || runtime == null)
                return result;
            for (int i = 0; i < names.Length; i++)
            {
                string? name = names[i];
                if (name == null)
                    continue;
                if (!_byName.TryGetValue(name, out var binding))
                {
                    binding = new LegacyHandleBinding { Handle = _nextHandle++, OriginalName = name };
                    _byName[name] = binding;
                    _byHandle[binding.Handle] = binding;
                }
                if (binding.RuntimeGeneration != _generation)
                    Bind(binding, runtime);
                result[i] = binding.Found ? binding : null;
            }
        }
        return result;
    }

    private void OnRuntimeChanging()
    {
        ulong generation;
        lock (_gate)
        {
            _changing = true;
            generation = _generation;
        }
        RuntimeChanging?.Invoke(generation);
    }

    private void OnRuntimeSwapped()
    {
        var invalid = new List<long>();
        DcsRuntime? runtime = _host.Runtime;
        ulong generation;
        lock (_gate)
        {
            _generation++;
            generation = _generation;
            if (runtime != null)
            {
                foreach (var binding in _byHandle.Values)
                {
                    Bind(binding, runtime);
                    if (!binding.Found)
                        invalid.Add(binding.Handle);
                }
            }
            else
            {
                foreach (var binding in _byHandle.Values)
                {
                    binding.Found = false;
                    binding.Accessor = null;
                    invalid.Add(binding.Handle);
                }
            }
            foreach (var session in _sessions.Values)
            {
                session.LastSent.Clear();
                session.LastPolled.Clear();
            }
            _changing = false;
        }
        RuntimeRebound?.Invoke(generation, invalid.ToArray());
    }

    private void Bind(LegacyHandleBinding binding, DcsRuntime runtime)
    {
        NormalizeName(binding.OriginalName, out string canonical, out bool iomapOwned);
        binding.CanonicalName = canonical;
        binding.IomapOwned |= iomapOwned;
        binding.Accessor = ResolveAccessor(runtime, canonical);
        binding.Found = binding.Accessor != null;
        binding.Writable = binding.Accessor?.Writable ?? false;
        binding.ValueKind = binding.Accessor?.ValueKind ?? CompatValueKind.Null;
        binding.RuntimeGeneration = _generation;

        if (binding.Accessor is PointBufferValueAccessor point && binding.IomapOwned)
        {
            runtime.Iomap.Mark(point.Slot);
            if (binding.LastIomapValue != null)
            {
                point.Write(binding.LastIomapValue);
                runtime.Iomap.SetOwnedValue(point.Slot, binding.LastIomapValue);
            }
        }
    }

    private IRealtimeValueAccessor? ResolveAccessor(DcsRuntime runtime, string name)
    {
        EnsureRuntimeIndexes(runtime);
        if (TryResolvePointMember(runtime, name, out var slot, out string member))
        {
            if (IsBufferMember(member))
                return new PointBufferValueAccessor(slot);
            if (PointFieldAccess.TryRead(slot, member, out _, out Type? fieldType) && fieldType != null)
                return new PointFieldValueAccessor(slot, member, fieldType);
        }

        if (TryResolveBlockMember(runtime, name, out var command, out string field) && field.Length > 0)
        {
            FieldInfo? fi = command.Fc.GetType().GetField(field,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fi != null)
                return new BlockFieldValueAccessor(command, fi);
        }
        return null;
    }

    private bool TryResolvePointMember(DcsRuntime runtime, string name, out PointSlotRef slot, out string member)
    {
        member = "";
        if (TryFindSlot(runtime, null, name, out slot))
            return true;

        DpuRuntime? scope = null;
        string rest = name;
        int dollar = name.IndexOf('$');
        if (dollar > 0)
        {
            _dpuIndex.TryGetValue(name[..dollar], out scope);
            if (scope != null)
            {
                rest = name[(dollar + 1)..];
                if (TryFindSlot(runtime, scope, rest, out slot))
                    return true;
            }
        }

        int dot = rest.LastIndexOf('.');
        if (dot > 0)
        {
            member = rest[(dot + 1)..];
            if (TryFindSlot(runtime, scope, rest[..dot], out slot))
                return true;
        }

        if (scope != null)
        {
            int dot2 = name.LastIndexOf('.');
            if (dot2 > 0)
            {
                member = name[(dot2 + 1)..];
                if (TryFindSlot(runtime, null, name[..dot2], out slot))
                    return true;
            }
        }
        member = "";
        slot = default;
        return false;
    }

    private bool TryResolveBlockMember(DcsRuntime runtime, string name, out BlockCommand command, out string field)
    {
        field = "";
        if (TryFindCommand(runtime, null, name, out command))
            return true;

        DpuRuntime? scope = null;
        string rest = name;
        int dollar = name.IndexOf('$');
        if (dollar > 0)
        {
            _dpuIndex.TryGetValue(name[..dollar], out scope);
            if (scope != null)
            {
                rest = name[(dollar + 1)..];
                if (TryFindCommand(runtime, scope, rest, out command))
                    return true;
            }
        }
        int dot = rest.LastIndexOf('.');
        if (dot > 0)
        {
            field = rest[(dot + 1)..];
            if (TryFindCommand(runtime, scope, rest[..dot], out command))
                return true;
        }
        command = null!;
        field = "";
        return false;
    }

    private static bool TryFindSlot(DcsRuntime runtime, DpuRuntime? scope, string name, out PointSlotRef slot)
    {
        if (scope != null)
            return scope.LocalSlots.TryGetValue(name, out slot) && slot.IsRealPoint;
        return runtime.TryGetSlot(name, out slot) && slot.IsRealPoint;
    }

    private bool TryFindCommand(DcsRuntime runtime, DpuRuntime? scope, string name, out BlockCommand command)
    {
        if (scope != null)
        {
            if (_commandsByDpu.TryGetValue(scope, out var commands)
                && commands.TryGetValue(name, out command!))
                return true;
            command = null!;
            return false;
        }

        return _globalCommandIndex.TryGetValue(name, out command!);
    }

    private void EnsureRuntimeIndexes(DcsRuntime runtime)
    {
        if (ReferenceEquals(_indexedRuntime, runtime))
            return;

        var dpuIndex = new Dictionary<string, DpuRuntime>(runtime.Dpus.Count, StringComparer.OrdinalIgnoreCase);
        var globalCommands = new Dictionary<string, BlockCommand>(StringComparer.OrdinalIgnoreCase);
        var commandsByDpu = new Dictionary<DpuRuntime, Dictionary<string, BlockCommand>>(runtime.Dpus.Count);

        foreach (var dpu in runtime.Dpus)
        {
            dpuIndex.TryAdd(dpu.Name, dpu);
            var localCommands = new Dictionary<string, BlockCommand>(dpu.Commands.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in dpu.Commands)
            {
                // 与旧线性查找保持一致：同名时装载顺序中的第一个生效。
                localCommands.TryAdd(candidate.Name, candidate);
                globalCommands.TryAdd(candidate.Name, candidate);
            }
            commandsByDpu[dpu] = localCommands;
        }

        _dpuIndex = dpuIndex;
        _globalCommandIndex = globalCommands;
        _commandsByDpu = commandsByDpu;
        _indexedRuntime = runtime;
    }

    private static void NormalizeName(string original, out string canonical, out bool iomapOwned)
    {
        canonical = original;
        iomapOwned = false;
        int dollar = original.IndexOf('$');
        if (dollar > 0 && dollar < original.Length - 1)
        {
            string rest = original[(dollar + 1)..];
            if (rest.StartsWith(IomapPointPrefix, StringComparison.Ordinal))
            {
                canonical = original[..(dollar + 1)] + rest[IomapPointPrefix.Length..];
                iomapOwned = true;
            }
            return;
        }
        if (original.StartsWith(IomapPointPrefix, StringComparison.Ordinal))
        {
            canonical = original[IomapPointPrefix.Length..];
            iomapOwned = true;
        }
    }

    private static bool IsBufferMember(string member)
        => member.Length == 0
           || member.Equals("buffer", StringComparison.OrdinalIgnoreCase)
           || member.Equals("value", StringComparison.OrdinalIgnoreCase);

    private static bool IsIomapClient(string? clientInfo)
        => !string.IsNullOrEmpty(clientInfo)
           && clientInfo.StartsWith(IomapClientPrefix, StringComparison.Ordinal);

    internal static CompatValueKind ToValueKind(Type type)
    {
        if (type.IsEnum) type = Enum.GetUnderlyingType(type);
        if (type == typeof(bool)) return CompatValueKind.Boolean;
        if (type == typeof(byte)) return CompatValueKind.Byte;
        if (type == typeof(ushort)) return CompatValueKind.UInt16;
        if (type == typeof(uint)) return CompatValueKind.UInt32;
        if (type == typeof(int)) return CompatValueKind.Int32;
        if (type == typeof(long)) return CompatValueKind.Int64;
        if (type == typeof(float)) return CompatValueKind.Single;
        if (type == typeof(double)) return CompatValueKind.Double;
        if (type == typeof(string)) return CompatValueKind.String;
        return CompatValueKind.String;
    }

    private static bool ValueEquals(CompatValue x, CompatValue y)
    {
        if (x.Kind != y.Kind)
            return false;
        if (x.Kind == CompatValueKind.Single)
            return BitConverter.SingleToInt32Bits(Convert.ToSingle(x.Value))
                   == BitConverter.SingleToInt32Bits(Convert.ToSingle(y.Value));
        if (x.Kind == CompatValueKind.Double)
            return BitConverter.DoubleToInt64Bits(Convert.ToDouble(x.Value))
                   == BitConverter.DoubleToInt64Bits(Convert.ToDouble(y.Value));
        return Equals(x.Value, y.Value);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _changeTimer.Dispose();
        _host.RuntimeChanging -= OnRuntimeChanging;
        _host.RuntimeSwapped -= OnRuntimeSwapped;
    }
}
