using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PS.Comm.Interfaces;
using RWVDCS.CompatProtocol;

namespace RWVDCS.RemotingAdapter
{
    /// <summary>订阅项：名字 ↔ 句柄 ↔ 装箱类型（全局共享，句柄稳定）。</summary>
    internal sealed class SubscriptionEntry
    {
        public long Handle;       // 对旧客户端稳定的 Edge Handle
        public long BackendHandle; // 当前 Host generation 内的 Handle
        public string OriginalName;
        public string Name;       // REST 查询/写入用的真实订阅名
        public string ValueType;  // /values/describe 定型结果；null=未找到
        public bool Found;
        public bool IomapOwned;
        public CompatValueKind PipeValueKind;
    }

    internal sealed class NormalizedSubscriptionName
    {
        public string Original;
        public string RestName;
        public bool IomapOwned;
    }

    /// <summary>客户端会话（对齐老 SessionManager.Session 的有效面）。</summary>
    internal sealed class ClientSession
    {
        public int ClientHandle;
        public ICallBack Callback;
        public string UserName;
        public bool UseDataChange = true;
        public bool Paused;
        public readonly List<long> Order = new List<long>();
        public readonly HashSet<long> Set = new HashSet<long>();
        /// <summary>回调差分基线（handle → 最近一次已推送/已读取的值）。</summary>
        public readonly Dictionary<long, object> LastSent = new Dictionary<long, object>();
        /// <summary>GetChangedData 轮询差分基线。</summary>
        public readonly Dictionary<long, object> LastPolled = new Dictionary<long, object>();
        /// <summary>回调进行中标志（防慢客户端堆积）。</summary>
        public int CallbackBusy;
        /// <summary>慢回调期间按 Handle 合并的最新值。</summary>
        public readonly Dictionary<long, object> PendingChanges = new Dictionary<long, object>();
    }

    /// <summary>
    /// 订阅注册表 + 变化推送轮询器。
    /// 老系统由 RTD 变化扫描驱动 InformDataChange；适配器以固定周期批量读新系统并做差分推送。
    /// </summary>
    internal sealed class SubscriptionRegistry : IDisposable
    {
        private const int DescribeBatchSize = 512;
        private const string IomapPointNamePrefix = "IOMapDirection2_";
        private const string IomapClientInfoPrefix = "IOMAP_";

        private readonly object _gate = new object();
        private readonly object _pipeRecoveryGate = new object();
        private readonly RestBridge _rest;
        private readonly CompatPipeClient _pipe;
        private readonly Dictionary<string, SubscriptionEntry> _byName = new Dictionary<string, SubscriptionEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<long, SubscriptionEntry> _byHandle = new Dictionary<long, SubscriptionEntry>();
        private readonly Dictionary<long, SubscriptionEntry> _byBackendHandle = new Dictionary<long, SubscriptionEntry>();
        private readonly Dictionary<int, ClientSession> _sessions = new Dictionary<int, ClientSession>();
        private long _nextHandle = 1;
        private int _nextClient = 1;
        private readonly Thread _poller;
        private volatile bool _stop;
        private readonly int _pollMs;
        private long _pipeReplayEpoch;
        private readonly Timer _performanceTimer;
        private long _writePerfCalls;
        private long _writePerfItems;
        private long _writePerfSuccess;
        private long _writePerfErrors;
        private long _writePerfElapsedTicks;

        public event Action<string> Log;

        public SubscriptionRegistry(RestBridge rest, int pollMs, CompatPipeClient pipe = null)
        {
            _rest = rest;
            _pipe = pipe;
            _pollMs = Math.Max(50, pollMs);
            if (_pipe == null)
            {
                _poller = new Thread(PollLoop) { IsBackground = true, Name = "adapter-poller" };
                _poller.Start();
            }
            else
            {
                _pipeReplayEpoch = _pipe.ConnectionEpoch;
                _pipe.DataChanged += OnPipeDataChanged;
                _pipe.RuntimeChanging += generation => Log?.Invoke($"Host Runtime 即将换代，generation={generation}");
                _pipe.RuntimeRebound += OnPipeRuntimeRebound;
                _pipe.EventChannelReconnected += OnPipeEventChannelReconnected;
            }
            // 写值可能每秒调用数百/数千次，逐次 Console.WriteLine 会反过来成为性能瓶颈。
            // 这里只做无锁计数，并由定时器每秒输出一次聚合统计。
            _performanceTimer = new Timer(_ => FlushWritePerformance(), null, 1000, 1000);
        }

        // ---------------- 会话 ----------------
        public int Attach(ICallBack callback, string userName, bool useDataChange)
        {
            ClientSession s;
            lock (_gate)
            {
                s = new ClientSession
                {
                    ClientHandle = _nextClient++,
                    Callback = callback,
                    UserName = userName ?? "",
                    UseDataChange = useDataChange,
                };
                _sessions[s.ClientHandle] = s;
                Log?.Invoke($"客户端接入 #{s.ClientHandle} user={s.UserName} datachange={useDataChange}");
            }
            if (_pipe != null)
            {
                try { _pipe.Attach(s.ClientHandle, useDataChange); }
                catch (Exception ex) { Log?.Invoke($"客户端 #{s.ClientHandle} Host 会话创建失败：{ex.Message}"); }
            }
            return s.ClientHandle;
        }

        public void Detach(int clientHandle)
        {
            bool removed;
            lock (_gate)
            {
                removed = _sessions.Remove(clientHandle);
                if (removed)
                    Log?.Invoke($"客户端断开 #{clientHandle}");
            }
            if (removed && _pipe != null)
            {
                try { _pipe.Detach(clientHandle); }
                catch (Exception ex) { Log?.Invoke($"Host 会话断开失败 #{clientHandle}：{ex.Message}"); }
            }
        }

        public ClientSession Find(int clientHandle)
        {
            lock (_gate)
            {
                ClientSession s;
                return _sessions.TryGetValue(clientHandle, out s) ? s : null;
            }
        }

        public void SetPaused(int clientHandle, bool paused)
        {
            var s = Find(clientHandle);
            if (s != null)
                s.Paused = paused;
            if (_pipe != null)
            {
                try { _pipe.SetPaused(clientHandle, paused); }
                catch (Exception ex) { Log?.Invoke($"暂停状态同步失败 #{clientHandle}：{ex.Message}"); }
            }
        }

        public void SetUseDataChange(int clientHandle, bool use)
        {
            var s = Find(clientHandle);
            if (s != null)
                s.UseDataChange = use;
            if (_pipe != null)
            {
                try { _pipe.SetDataInformType(clientHandle, use); }
                catch (Exception ex) { Log?.Invoke($"变化通知状态同步失败 #{clientHandle}：{ex.Message}"); }
            }
        }

        // ---------------- 订阅 ----------------
        public long[] Subscribe(int clientHandle, string[] names)
        {
            if (names == null || names.Length == 0)
                return new long[0];

            if (_pipe != null)
            {
                var pipeWatch = Stopwatch.StartNew();
                long[] pipeResult = SubscribePipe(clientHandle, names);
                pipeWatch.Stop();
                LogSubscribePerformance("pipe", clientHandle, names.Length, pipeResult, pipeWatch.ElapsedTicks);
                return pipeResult;
            }

            var restWatch = Stopwatch.StartNew();

            var session = Find(clientHandle);
            var result = new long[names.Length];
            var normalized = new NormalizedSubscriptionName[names.Length];
            for (int i = 0; i < names.Length; i++)
                normalized[i] = NormalizeSubscriptionName(names[i]);

            // 批量定型未注册的名字；此前定型失败的也重试（工程可能刚装载/下装后点已出现）
            List<NormalizedSubscriptionName> missing;
            lock (_gate)
            {
                missing = normalized.Where(n =>
                {
                    if (n == null || n.Original == null)
                        return false;
                    SubscriptionEntry e;
                    return !_byName.TryGetValue(n.Original, out e) || !e.Found;
                })
                .GroupBy(n => n.Original, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            }
            if (missing.Count > 0)
            {
                var described = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                for (int start = 0; start < missing.Count; start += DescribeBatchSize)
                {
                    int count = Math.Min(DescribeBatchSize, missing.Count - start);
                    var originalBatch = new string[count];
                    var restBatch = new string[count];
                    for (int i = 0; i < count; i++)
                    {
                        originalBatch[i] = missing[start + i].Original;
                        restBatch[i] = missing[start + i].RestName;
                    }

                    try
                    {
                        using (var doc = _rest.Post("values/describe", new { names = restBatch }))
                        {
                            var items = doc.RootElement.GetProperty("items");
                            int itemCount = Math.Min(items.GetArrayLength(), count);
                            for (int i = 0; i < itemCount; i++)
                                described[originalBatch[i]] = items[i].Clone();
                        }
                    }
                    catch (Exception ex)
                    {
                        Log?.Invoke($"订阅定型失败（第 {start + 1}-{start + count}/{missing.Count} 项）：{ex.Message}");
                    }
                }

                lock (_gate)
                {
                    foreach (var n in missing)
                    {
                        SubscriptionEntry entry;
                        if (!_byName.TryGetValue(n.Original, out entry))
                        {
                            entry = new SubscriptionEntry { Handle = _nextHandle++, Name = n.RestName, IomapOwned = n.IomapOwned };
                            _byName[n.Original] = entry;
                            _byHandle[entry.Handle] = entry;
                        }
                        else
                        {
                            entry.Name = n.RestName;
                            entry.IomapOwned |= n.IomapOwned;
                        }

                        JsonElement d;
                        if (described.TryGetValue(n.Original, out d) && d.ValueKind == JsonValueKind.Object
                            && d.GetProperty("found").GetBoolean())
                        {
                            entry.Found = true;
                            entry.ValueType = d.GetProperty("valueType").GetString();
                        }
                    }
                }
            }

            var iomapNamesToMark = new List<string>();
            lock (_gate)
            {
                for (int i = 0; i < names.Length; i++)
                {
                    SubscriptionEntry entry;
                    var n = normalized[i];
                    if (n == null || n.Original == null || !_byName.TryGetValue(n.Original, out entry))
                    {
                        result[i] = -1;
                        continue;
                    }
                    if (n.IomapOwned)
                        entry.IomapOwned = true;
                    result[i] = entry.Found ? entry.Handle : -1;
                    if (entry.Found && entry.IomapOwned)
                        iomapNamesToMark.Add(entry.Name);
                    if (session != null && entry.Found && session.Set.Add(entry.Handle))
                        session.Order.Add(entry.Handle);
                }
            }
            MarkIomapNames(iomapNamesToMark);
            restWatch.Stop();
            LogSubscribePerformance("rest", clientHandle, names.Length, result, restWatch.ElapsedTicks);
            return result;
        }

        private long[] SubscribePipe(int clientHandle, string[] names)
        {
            var result = new long[names.Length];
            for (int i = 0; i < result.Length; i++) result[i] = -1;
            try
            {
                PipeSubscribeItem[] items = PipeCall(() => _pipe.Subscribe(clientHandle, names));
                int count = Math.Min(names.Length, items.Length);
                lock (_gate)
                {
                    ClientSession session;
                    _sessions.TryGetValue(clientHandle, out session);
                    for (int i = 0; i < count; i++)
                    {
                        if (names[i] == null || !items[i].Found || items[i].Handle < 0)
                            continue;

                        NormalizedSubscriptionName normalized = NormalizeSubscriptionName(names[i]);
                        SubscriptionEntry entry;
                        if (!_byName.TryGetValue(names[i], out entry))
                        {
                            entry = new SubscriptionEntry
                            {
                                Handle = _nextHandle++,
                                OriginalName = names[i],
                            };
                            _byName[names[i]] = entry;
                            _byHandle[entry.Handle] = entry;
                        }
                        if (entry.BackendHandle > 0 && entry.BackendHandle != items[i].Handle)
                            _byBackendHandle.Remove(entry.BackendHandle);
                        entry.BackendHandle = items[i].Handle;
                        entry.Name = normalized.RestName;
                        entry.Found = true;
                        entry.IomapOwned |= normalized.IomapOwned;
                        entry.PipeValueKind = items[i].ValueKind;
                        entry.ValueType = items[i].ValueKind.ToString();
                        _byBackendHandle[entry.BackendHandle] = entry;
                        result[i] = entry.Handle;
                        if (session != null && session.Set.Add(entry.Handle))
                            session.Order.Add(entry.Handle);
                    }
                }
            }
            catch (Exception ex)
            {
                Log?.Invoke($"二进制批量订阅失败：{ex.Message}");
            }
            return result;
        }

        public void Unsubscribe(int clientHandle, long[] handles)
        {
            var session = Find(clientHandle);
            if (session == null)
                return;
            lock (_gate)
            {
                if (handles == null)
                {
                    session.Order.Clear();
                    session.Set.Clear();
                    session.LastSent.Clear();
                    session.LastPolled.Clear();
                }
                else
                {
                    foreach (long h in handles)
                    {
                        if (session.Set.Remove(h))
                            session.Order.Remove(h);
                        session.LastSent.Remove(h);
                        session.LastPolled.Remove(h);
                    }
                }
            }
            if (_pipe != null)
            {
                try { PipeCall(() => _pipe.Unsubscribe(clientHandle, handles == null ? null : MapToBackend(handles))); }
                catch (Exception ex) { Log?.Invoke($"Host 退订失败 #{clientHandle}：{ex.Message}"); }
            }
        }

        /// <summary>强制反订阅：把句柄从所有会话中移除（IEdit.UnSubscribeForcibly）。</summary>
        public void UnsubscribeEverywhere(long handle)
        {
            int[] clients;
            lock (_gate)
            {
                clients = _sessions.Keys.ToArray();
                foreach (var s in _sessions.Values)
                {
                    if (s.Set.Remove(handle))
                        s.Order.Remove(handle);
                    s.LastSent.Remove(handle);
                    s.LastPolled.Remove(handle);
                }
            }
            if (_pipe != null)
            {
                foreach (int client in clients)
                {
                    try { PipeCall(() => _pipe.Unsubscribe(client, MapToBackend(new[] { handle }))); }
                    catch (Exception ex) { Log?.Invoke($"Host 强制退订失败 #{client}/{handle}：{ex.Message}"); }
                }
            }
        }

        // ---------------- 读写 ----------------
        public object[] Read(long[] handles)
        {
            if (handles == null || handles.Length == 0)
                return new object[0];

            if (_pipe != null)
            {
                try { return PipeCall(() => _pipe.Read(MapToBackend(handles))); }
                catch (Exception ex)
                {
                    Log?.Invoke($"二进制批量读失败：{ex.Message}");
                    return new object[handles.Length];
                }
            }

            var entries = new SubscriptionEntry[handles.Length];
            var names = new List<string>();
            var valueTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            lock (_gate)
            {
                for (int i = 0; i < handles.Length; i++)
                {
                    SubscriptionEntry e;
                    entries[i] = _byHandle.TryGetValue(handles[i], out e) ? e : null;
                    if (entries[i] != null)
                    {
                        names.Add(entries[i].Name);
                        if (!valueTypes.ContainsKey(entries[i].Name))
                            valueTypes[entries[i].Name] = entries[i].ValueType;
                    }
                }
            }

            var byName = ReadByNames(names, valueTypes);
            var result = new object[handles.Length];
            for (int i = 0; i < handles.Length; i++)
            {
                if (entries[i] == null)
                    continue;
                object v;
                if (byName.TryGetValue(entries[i].Name, out v))
                    result[i] = v;
            }
            return result;
        }

        /// <summary>会话全订阅读取（订阅顺序）。</summary>
        public object[] ReadAll(int clientHandle)
        {
            if (_pipe != null)
            {
                try { return PipeCall(() => _pipe.ReadAll(clientHandle)); }
                catch (Exception ex)
                {
                    Log?.Invoke($"二进制会话读取失败 #{clientHandle}：{ex.Message}");
                    return new object[0];
                }
            }
            var session = Find(clientHandle);
            if (session == null)
                return new object[0];
            long[] handles;
            lock (_gate)
            {
                handles = session.Order.ToArray();
            }
            return Read(handles);
        }

        public bool[] Write(int clientHandle, long[] handles, object[] values, string userInfo)
        {
            if (handles == null || handles.Length == 0)
                return new bool[0];

            var result = new bool[handles.Length];
            if (values == null || values.Length == 0)
                return result;

            if (_pipe != null)
            {
                var watch = Stopwatch.StartNew();
                bool[] pipeResult;
                bool error = false;
                long[] backendHandles = MapToBackend(handles);
                try { pipeResult = PipeCall(() => _pipe.Write(clientHandle, backendHandles, values, BuildClientInfo(clientHandle, userInfo))); }
                catch (Exception ex)
                {
                    error = true;
                    Log?.Invoke($"二进制批量写失败：{ex.Message}");
                    pipeResult = result;
                }
                watch.Stop();
                RecordWritePerformance(handles.Length, pipeResult, error, watch.ElapsedTicks);
                //if (!error)
                //    LogPipeWriteFailures(clientHandle, handles, backendHandles, pipeResult);
                return pipeResult;
            }

            var restWatch = Stopwatch.StartNew();
            bool restError = false;
            var items = new List<object>();
            var idx = new List<int>();
            string clientInfo = BuildClientInfo(clientHandle, userInfo);
            lock (_gate)
            {
                int count = Math.Min(handles.Length, values.Length);
                for (int i = 0; i < count; i++)
                {
                    SubscriptionEntry e;
                    if (_byHandle.TryGetValue(handles[i], out e) && e.Found)
                    {
                        items.Add(new { name = e.Name, value = RestBridge.ToWireText(values[i]), iomapOwned = e.IomapOwned });
                        idx.Add(i);
                    }
                }
            }
            if (items.Count == 0)
            {
                restWatch.Stop();
                RecordWritePerformance(handles.Length, result, false, restWatch.ElapsedTicks);
                return result;
            }
            try
            {
                using (var doc = _rest.Post("values/write", new { clientInfo, items }))
                {
                    var arr = doc.RootElement.GetProperty("results");
                    for (int i = 0; i < idx.Count; i++)
                        result[idx[i]] = arr[i].GetBoolean();
                }
            }
            catch (Exception ex)
            {
                restError = true;
                Log?.Invoke($"批量写失败：{ex.Message}");
            }
            restWatch.Stop();
            RecordWritePerformance(handles.Length, result, restError, restWatch.ElapsedTicks);
            return result;
        }

        /// <summary>
        /// 按 pipeResult 的位置把 false 映射回原始订阅点名。本方法只处理 Host 已正常
        /// 返回的逐项失败；整批通讯异常由“二进制批量写失败”日志负责，避免误报所有点。
        /// </summary>
        private void LogPipeWriteFailures(int clientHandle, long[] edgeHandles,
            long[] backendHandles, bool[] pipeResult)
        {
            const int maxNamesToLog = 100;
            int failedCount = 0;
            var failedNames = new List<string>();

            lock (_gate)
            {
                for (int i = 0; i < edgeHandles.Length; i++)
                {
                    bool failed = i >= pipeResult.Length || !pipeResult[i];
                    if (!failed)
                        continue;

                    failedCount++;
                    if (failedNames.Count >= maxNamesToLog)
                        continue;

                    SubscriptionEntry entry;
                    long backendHandle = backendHandles != null && i < backendHandles.Length
                        ? backendHandles[i]
                        : -1;
                    if (!_byHandle.TryGetValue(edgeHandles[i], out entry) && backendHandle > 0)
                        _byBackendHandle.TryGetValue(backendHandle, out entry);

                    string name;
                    if (entry != null)
                    {
                        name = !string.IsNullOrEmpty(entry.OriginalName)
                            ? entry.OriginalName
                            : !string.IsNullOrEmpty(entry.Name)
                                ? entry.Name
                                : $"<未命名点 edgeHandle={edgeHandles[i]} backendHandle={backendHandle}>";
                    }
                    else
                    {
                        name = $"<未知点名 index={i} edgeHandle={edgeHandles[i]} backendHandle={backendHandle}>";
                    }
                    failedNames.Add(name);
                }
            }

            if (failedCount == 0)
                return;

            string omitted = failedCount > failedNames.Count
                ? $"；仅显示前 {failedNames.Count} 个，另有 {failedCount - failedNames.Count} 个未显示"
                : "";
            Log?.Invoke($"[写值失败点] client=#{clientHandle} failed={failedCount}/{edgeHandles.Length} "
                + $"names={string.Join(" | ", failedNames)}{omitted}");
        }

        private void LogSubscribePerformance(string transport, int clientHandle, int requested,
            long[] result, long elapsedTicks)
        {
            int success = result == null ? 0 : result.Count(handle => handle > 0);
            double elapsedMs = elapsedTicks * 1000d / Stopwatch.Frequency;
            double averageUs = requested > 0 ? elapsedMs * 1000d / requested : 0d;
            Log?.Invoke($"[性能] 订阅 transport={transport} client=#{clientHandle} "
                        + $"requested={requested} success={success} failed={requested - success} "
                        + $"elapsed={elapsedMs:F3} ms avg={averageUs:F3} us/item");
        }

        private void RecordWritePerformance(int requested, bool[] result, bool error, long elapsedTicks)
        {
            int success = result == null ? 0 : result.Count(ok => ok);
            Interlocked.Increment(ref _writePerfCalls);
            Interlocked.Add(ref _writePerfItems, requested);
            Interlocked.Add(ref _writePerfSuccess, success);
            if (error)
                Interlocked.Increment(ref _writePerfErrors);
            Interlocked.Add(ref _writePerfElapsedTicks, elapsedTicks);
        }

        private void FlushWritePerformance()
        {
            long calls = Interlocked.Exchange(ref _writePerfCalls, 0);
            if (calls == 0)
                return;

            long items = Interlocked.Exchange(ref _writePerfItems, 0);
            long success = Interlocked.Exchange(ref _writePerfSuccess, 0);
            long errors = Interlocked.Exchange(ref _writePerfErrors, 0);
            long elapsedTicks = Interlocked.Exchange(ref _writePerfElapsedTicks, 0);
            double elapsedMs = elapsedTicks * 1000d / Stopwatch.Frequency;
            double averageMs = elapsedMs / calls;
            double averageUs = items > 0 ? elapsedMs * 1000d / items : 0d;
            string transport = _pipe == null ? "rest" : "pipe";
            Log?.Invoke($"[性能] 写值 1s汇总 transport={transport} calls={calls} items={items} "
                        + $"success={success} failed={items - success} errors={errors} "
                        + $"elapsedSum={elapsedMs:F3} ms avg={averageMs:F3} ms/call "
                        + $"avg={averageUs:F3} us/item");
        }

        /// <summary>按名批量读并按订阅类型定型。</summary>
        private Dictionary<string, object> ReadByNames(List<string> names, Dictionary<string, string> valueTypes = null)
        {
            var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            if (names.Count == 0)
                return result;
            try
            {
                using (var doc = _rest.Post("values/read", new { names }))
                {
                    var values = doc.RootElement.GetProperty("values");
                    for (int i = 0; i < names.Count; i++)
                    {
                        SubscriptionEntry entry;
                        string vt = null;
                        if (valueTypes != null)
                            valueTypes.TryGetValue(names[i], out vt);
                        if (vt == null)
                        {
                            lock (_gate)
                            {
                                if (_byName.TryGetValue(names[i], out entry))
                                    vt = entry.ValueType;
                            }
                        }
                        result[names[i]] = RestBridge.FromJson(values[i], vt);
                    }
                }
            }
            catch (Exception ex)
            {
                Log?.Invoke($"批量读失败：{ex.Message}");
            }
            return result;
        }

        /// <summary>GetChangedData：按会话做一次拉取差分（供不用回调的客户端轮询）。</summary>
        public KeyValuePair<long, object>[] PollChanges(int clientHandle)
        {
            if (_pipe != null)
            {
                try
                {
                    KeyValuePair<long, object>[] backend = PipeCall(() => _pipe.PollChanges(clientHandle));
                    var translated = new List<KeyValuePair<long, object>>(backend.Length);
                    lock (_gate)
                    {
                        foreach (var change in backend)
                        {
                            SubscriptionEntry entry;
                            if (_byBackendHandle.TryGetValue(change.Key, out entry))
                                translated.Add(new KeyValuePair<long, object>(entry.Handle, change.Value));
                        }
                    }
                    return translated.ToArray();
                }
                catch (Exception ex)
                {
                    Log?.Invoke($"二进制变化轮询失败 #{clientHandle}：{ex.Message}");
                    return new KeyValuePair<long, object>[0];
                }
            }
            var session = Find(clientHandle);
            if (session == null)
                return new KeyValuePair<long, object>[0];

            long[] handles;
            lock (_gate)
            {
                handles = session.Order.ToArray();
            }
            var values = Read(handles);
            var changed = new List<KeyValuePair<long, object>>();
            lock (_gate)
            {
                for (int i = 0; i < handles.Length; i++)
                {
                    object last;
                    bool has = session.LastPolled.TryGetValue(handles[i], out last);
                    if (!has || !Equals(last, values[i]))
                    {
                        changed.Add(new KeyValuePair<long, object>(handles[i], values[i]));
                        session.LastPolled[handles[i]] = values[i];
                    }
                }
            }
            return changed.ToArray();
        }

        // ---------------- 变化推送 ----------------
        private void PollLoop()
        {
            while (!_stop)
            {
                try
                {
                    PushOnce();
                }
                catch (Exception ex)
                {
                    Log?.Invoke($"推送轮询异常：{ex.Message}");
                }
                Thread.Sleep(_pollMs);
            }
        }

        private void PushOnce()
        {
            List<ClientSession> sessions;
            List<string> unionNames;
            var handleToName = new Dictionary<long, string>();
            var valueTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            lock (_gate)
            {
                sessions = _sessions.Values.Where(s => s.Callback != null && s.UseDataChange && !s.Paused && s.Order.Count > 0).ToList();
                if (sessions.Count == 0)
                    return;
                var union = new HashSet<long>();
                foreach (var s in sessions)
                    union.UnionWith(s.Set);
                unionNames = new List<string>(union.Count);
                foreach (long h in union)
                {
                    SubscriptionEntry e;
                    if (_byHandle.TryGetValue(h, out e))
                    {
                        handleToName[h] = e.Name;
                        unionNames.Add(e.Name);
                        if (!valueTypes.ContainsKey(e.Name))
                            valueTypes[e.Name] = e.ValueType;
                    }
                }
            }

            var byName = ReadByNames(unionNames, valueTypes);

            foreach (var s in sessions)
            {
                long[] handles;
                lock (_gate)
                {
                    handles = s.Order.ToArray();
                }
                var changedHandles = new List<long>();
                var changedValues = new List<object>();
                lock (_gate)
                {
                    foreach (long h in handles)
                    {
                        string name;
                        object v;
                        if (!handleToName.TryGetValue(h, out name) || !byName.TryGetValue(name, out v))
                            continue;
                        object last;
                        bool has = s.LastSent.TryGetValue(h, out last);
                        if (!has || !Equals(last, v))
                        {
                            changedHandles.Add(h);
                            changedValues.Add(v);
                            s.LastSent[h] = v;
                        }
                    }
                }
                if (changedHandles.Count == 0)
                    continue;

                // 每会话一个在途回调，慢/死客户端不拖垮轮询线程
                if (Interlocked.CompareExchange(ref s.CallbackBusy, 1, 0) != 0)
                    continue;
                var session = s;
                var hArr = changedHandles.ToArray();
                var vArr = changedValues.ToArray();
                Task.Run(() =>
                {
                    try
                    {
                        session.Callback.InformDataChange(hArr, vArr);
                    }
                    catch (Exception ex)
                    {
                        Log?.Invoke($"客户端 #{session.ClientHandle} 回调失败，移除会话：{ex.Message}");
                        Detach(session.ClientHandle);
                    }
                    finally
                    {
                        Interlocked.Exchange(ref session.CallbackBusy, 0);
                    }
                });
            }
        }

        private void OnPipeDataChanged(int clientHandle, long[] handles, object[] values)
        {
            ClientSession session;
            bool schedule = false;
            lock (_gate)
            {
                if (!_sessions.TryGetValue(clientHandle, out session)
                    || session.Callback == null || !session.UseDataChange || session.Paused)
                    return;
                int count = Math.Min(handles.Length, values.Length);
                for (int i = 0; i < count; i++)
                {
                    SubscriptionEntry entry;
                    if (_byBackendHandle.TryGetValue(handles[i], out entry))
                        session.PendingChanges[entry.Handle] = values[i];
                }
                if (session.CallbackBusy == 0)
                {
                    session.CallbackBusy = 1;
                    schedule = true;
                }
            }
            if (schedule)
                Task.Run(() => DrainPipeCallbacks(session));
        }

        private void DrainPipeCallbacks(ClientSession session)
        {
            while (true)
            {
                long[] handles;
                object[] values;
                lock (_gate)
                {
                    if (!_sessions.ContainsKey(session.ClientHandle) || session.PendingChanges.Count == 0)
                    {
                        session.CallbackBusy = 0;
                        return;
                    }
                    handles = session.PendingChanges.Keys.ToArray();
                    values = new object[handles.Length];
                    for (int i = 0; i < handles.Length; i++)
                        values[i] = session.PendingChanges[handles[i]];
                    session.PendingChanges.Clear();
                }

                try
                {
                    session.Callback.InformDataChange(handles, values);
                }
                catch (Exception ex)
                {
                    Log?.Invoke($"客户端 #{session.ClientHandle} 回调失败，移除会话：{ex.Message}");
                    Detach(session.ClientHandle);
                    lock (_gate) session.CallbackBusy = 0;
                    return;
                }
            }
        }

        private void OnPipeRuntimeRebound(ulong generation, long[] invalidHandles)
        {
            lock (_gate)
            {
                foreach (long handle in invalidHandles)
                {
                    SubscriptionEntry entry;
                    if (_byBackendHandle.TryGetValue(handle, out entry))
                        entry.Found = false;
                }
                foreach (var session in _sessions.Values)
                {
                    session.LastSent.Clear();
                    session.LastPolled.Clear();
                }
            }
            Log?.Invoke($"Host Runtime 重绑定完成：generation={generation}, invalid={invalidHandles.Length}");
        }

        private void OnPipeEventChannelReconnected()
        {
            // 事件管道是 Host 重启的最早可观测信号。后台重建请求管道、Host 会话及名称订阅，
            // 使只依赖推送、暂时没有主动读请求的老客户端也能自动恢复。
            Task.Run(() =>
            {
                try
                {
                    EnsurePipeSessions(true);
                }
                catch (Exception ex)
                {
                    Log?.Invoke("Host 重连后自动恢复会话失败，下次调用将再重试：" + ex.Message);
                }
            });
        }

        private long[] MapToBackend(long[] handles)
        {
            if (handles == null)
                return null;
            var result = new long[handles.Length];
            lock (_gate)
            {
                for (int i = 0; i < handles.Length; i++)
                {
                    SubscriptionEntry entry;
                    result[i] = _byHandle.TryGetValue(handles[i], out entry)
                        ? entry.BackendHandle
                        : -1;
                }
            }
            return result;
        }

        private T PipeCall<T>(Func<T> call)
        {
            EnsurePipeSessions(false);
            try
            {
                return call();
            }
            catch (Exception first)
            {
                Log?.Invoke("二进制管道调用失败，重连后重试一次：" + first.Message);
                EnsurePipeSessions(true);
                return call();
            }
        }

        private void PipeCall(Action call)
        {
            PipeCall(() =>
            {
                call();
                return true;
            });
        }

        /// <summary>请求管道重连后重建 Host 会话，并按各会话原顺序重放名称订阅。</summary>
        private void EnsurePipeSessions(bool forceReconnect)
        {
            if (_pipe == null)
                return;
            lock (_pipeRecoveryGate)
            {
                if (forceReconnect)
                    _pipe.ForceReconnect();
                else if (!_pipe.TryConnect())
                    throw new IOException("Host 二进制请求管道不可用");

                long epoch = _pipe.ConnectionEpoch;
                if (!forceReconnect && epoch == _pipeReplayEpoch)
                    return;

                ClientSession[] sessions;
                lock (_gate) sessions = _sessions.Values.ToArray();
                foreach (var session in sessions)
                {
                    _pipe.Attach(session.ClientHandle, session.UseDataChange);

                    string[] names;
                    SubscriptionEntry[] entries;
                    lock (_gate)
                    {
                        entries = session.Order
                            .Select(h => _byHandle.TryGetValue(h, out var e) ? e : null)
                            .Where(e => e != null && e.OriginalName != null)
                            .ToArray();
                        names = entries.Select(e => e.OriginalName).ToArray();
                    }
                    if (names.Length > 0)
                    {
                        PipeSubscribeItem[] rebound = _pipe.Subscribe(session.ClientHandle, names);
                        int count = Math.Min(entries.Length, rebound.Length);
                        lock (_gate)
                        {
                            for (int i = 0; i < count; i++)
                            {
                                var entry = entries[i];
                                if (entry.BackendHandle > 0)
                                    _byBackendHandle.Remove(entry.BackendHandle);
                                entry.BackendHandle = rebound[i].Handle;
                                entry.Found = rebound[i].Found;
                                entry.PipeValueKind = rebound[i].ValueKind;
                                entry.ValueType = rebound[i].ValueKind.ToString();
                                if (entry.Found && entry.BackendHandle > 0)
                                    _byBackendHandle[entry.BackendHandle] = entry;
                            }
                        }
                    }
                    if (session.Paused)
                        _pipe.SetPaused(session.ClientHandle, true);
                }
                _pipeReplayEpoch = epoch;
                Log?.Invoke($"Host 会话和订阅已恢复：epoch={epoch}, sessions={sessions.Length}");
            }
        }

        private static NormalizedSubscriptionName NormalizeSubscriptionName(string name)
        {
            if (name == null)
                return null;

            string restName;
            bool iomapOwned;
            StripIomapPointNamePrefix(name, out restName, out iomapOwned);
            return new NormalizedSubscriptionName
            {
                Original = name,
                RestName = restName,
                IomapOwned = iomapOwned,
            };
        }

        private static void StripIomapPointNamePrefix(string name, out string restName, out bool iomapOwned)
        {
            restName = name;
            iomapOwned = false;
            if (string.IsNullOrEmpty(name))
                return;

            int dollar = name.IndexOf('$');
            if (dollar > 0 && dollar < name.Length - 1)
            {
                string dpuPrefix = name.Substring(0, dollar + 1);
                string rest = name.Substring(dollar + 1);
                if (rest.StartsWith(IomapPointNamePrefix, StringComparison.Ordinal))
                {
                    restName = dpuPrefix + rest.Substring(IomapPointNamePrefix.Length);
                    iomapOwned = true;
                }
                return;
            }

            if (name.StartsWith(IomapPointNamePrefix, StringComparison.Ordinal))
            {
                restName = name.Substring(IomapPointNamePrefix.Length);
                iomapOwned = true;
            }
        }

        private string BuildClientInfo(int clientHandle, string userInfo)
        {
            string user = userInfo;
            if (string.IsNullOrEmpty(user))
            {
                var session = Find(clientHandle);
                user = session == null ? null : session.UserName;
            }

            if (string.IsNullOrEmpty(user))
                return "";
            if (IsIomapClientInfo(user))
                return user;

            // 老 Session 传到底层的是 UserName + "_" + Ip。适配器拿不到 IP，
            // 但追加下划线可以保持 UserName="IOMAP" 时仍匹配 IOMAP_ 前缀。
            return user + "_";
        }

        private static bool IsIomapClientInfo(string clientInfo)
            => !string.IsNullOrEmpty(clientInfo)
               && clientInfo.StartsWith(IomapClientInfoPrefix, StringComparison.Ordinal);

        private void MarkIomapNames(List<string> names)
        {
            if (names == null || names.Count == 0)
                return;

            var batch = names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            try
            {
                using (_rest.Post("values/iomap/mark", new { names = batch }))
                {
                }
            }
            catch (Exception ex)
            {
                Log?.Invoke($"IOMAP 订阅标记失败：{ex.Message}");
            }
        }

        public void Dispose()
        {
            _stop = true;
            _performanceTimer.Dispose();
            FlushWritePerformance();
            if (_pipe != null)
            {
                _pipe.DataChanged -= OnPipeDataChanged;
                _pipe.RuntimeRebound -= OnPipeRuntimeRebound;
                _pipe.EventChannelReconnected -= OnPipeEventChannelReconnected;
                _pipe.Dispose();
            }
            if (_poller != null && _poller.IsAlive)
                _poller.Join(2000);
        }
    }
}
