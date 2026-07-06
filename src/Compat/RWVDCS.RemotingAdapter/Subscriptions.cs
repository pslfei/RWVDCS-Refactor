using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using PS.Comm.Interfaces;

namespace RWVDCS.RemotingAdapter
{
    /// <summary>订阅项：名字 ↔ 句柄 ↔ 装箱类型（全局共享，句柄稳定）。</summary>
    internal sealed class SubscriptionEntry
    {
        public long Handle;
        public string Name;       // 原始订阅名（REST 查询用）
        public string ValueType;  // /values/describe 定型结果；null=未找到
        public bool Found;
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
    }

    /// <summary>
    /// 订阅注册表 + 变化推送轮询器。
    /// 老系统由 RTD 变化扫描驱动 InformDataChange；适配器以固定周期批量读新系统并做差分推送。
    /// </summary>
    internal sealed class SubscriptionRegistry : IDisposable
    {
        private readonly object _gate = new object();
        private readonly RestBridge _rest;
        private readonly Dictionary<string, SubscriptionEntry> _byName = new Dictionary<string, SubscriptionEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<long, SubscriptionEntry> _byHandle = new Dictionary<long, SubscriptionEntry>();
        private readonly Dictionary<int, ClientSession> _sessions = new Dictionary<int, ClientSession>();
        private long _nextHandle = 1;
        private int _nextClient = 1;
        private readonly Thread _poller;
        private volatile bool _stop;
        private readonly int _pollMs;

        public event Action<string> Log;

        public SubscriptionRegistry(RestBridge rest, int pollMs)
        {
            _rest = rest;
            _pollMs = Math.Max(50, pollMs);
            _poller = new Thread(PollLoop) { IsBackground = true, Name = "adapter-poller" };
            _poller.Start();
        }

        // ---------------- 会话 ----------------
        public int Attach(ICallBack callback, string userName, bool useDataChange)
        {
            lock (_gate)
            {
                var s = new ClientSession
                {
                    ClientHandle = _nextClient++,
                    Callback = callback,
                    UserName = userName ?? "",
                    UseDataChange = useDataChange,
                };
                _sessions[s.ClientHandle] = s;
                Log?.Invoke($"客户端接入 #{s.ClientHandle} user={s.UserName} datachange={useDataChange}");
                return s.ClientHandle;
            }
        }

        public void Detach(int clientHandle)
        {
            lock (_gate)
            {
                if (_sessions.Remove(clientHandle))
                    Log?.Invoke($"客户端断开 #{clientHandle}");
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
        }

        public void SetUseDataChange(int clientHandle, bool use)
        {
            var s = Find(clientHandle);
            if (s != null)
                s.UseDataChange = use;
        }

        // ---------------- 订阅 ----------------
        public long[] Subscribe(int clientHandle, string[] names)
        {
            if (names == null || names.Length == 0)
                return new long[0];

            var session = Find(clientHandle);
            var result = new long[names.Length];

            // 批量定型未注册的名字；此前定型失败的也重试（工程可能刚装载/下装后点已出现）
            List<string> missing;
            lock (_gate)
            {
                missing = names.Where(n =>
                {
                    if (n == null)
                        return false;
                    SubscriptionEntry e;
                    return !_byName.TryGetValue(n, out e) || !e.Found;
                }).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            if (missing.Count > 0)
            {
                var described = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    using (var doc = _rest.Post("values/describe", new { names = missing }))
                    {
                        var items = doc.RootElement.GetProperty("items");
                        for (int i = 0; i < missing.Count; i++)
                            described[missing[i]] = items[i].Clone();
                    }
                }
                catch (Exception ex)
                {
                    Log?.Invoke($"订阅定型失败（{missing.Count} 项）：{ex.Message}");
                }

                lock (_gate)
                {
                    foreach (var n in missing)
                    {
                        SubscriptionEntry entry;
                        if (!_byName.TryGetValue(n, out entry))
                        {
                            entry = new SubscriptionEntry { Handle = _nextHandle++, Name = n };
                            _byName[n] = entry;
                            _byHandle[entry.Handle] = entry;
                        }
                        JsonElement d;
                        if (described.TryGetValue(n, out d) && d.ValueKind == JsonValueKind.Object
                            && d.GetProperty("found").GetBoolean())
                        {
                            entry.Found = true;
                            entry.ValueType = d.GetProperty("valueType").GetString();
                        }
                    }
                }
            }

            lock (_gate)
            {
                for (int i = 0; i < names.Length; i++)
                {
                    SubscriptionEntry entry;
                    if (names[i] == null || !_byName.TryGetValue(names[i], out entry))
                    {
                        result[i] = -1;
                        continue;
                    }
                    result[i] = entry.Found ? entry.Handle : -1;
                    if (session != null && entry.Found && session.Set.Add(entry.Handle))
                        session.Order.Add(entry.Handle);
                }
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
                    return;
                }
                foreach (long h in handles)
                {
                    if (session.Set.Remove(h))
                        session.Order.Remove(h);
                    session.LastSent.Remove(h);
                    session.LastPolled.Remove(h);
                }
            }
        }

        /// <summary>强制反订阅：把句柄从所有会话中移除（IEdit.UnSubscribeForcibly）。</summary>
        public void UnsubscribeEverywhere(long handle)
        {
            lock (_gate)
            {
                foreach (var s in _sessions.Values)
                {
                    if (s.Set.Remove(handle))
                        s.Order.Remove(handle);
                    s.LastSent.Remove(handle);
                    s.LastPolled.Remove(handle);
                }
            }
        }

        // ---------------- 读写 ----------------
        public object[] Read(long[] handles)
        {
            if (handles == null || handles.Length == 0)
                return new object[0];

            var entries = new SubscriptionEntry[handles.Length];
            var names = new List<string>();
            lock (_gate)
            {
                for (int i = 0; i < handles.Length; i++)
                {
                    SubscriptionEntry e;
                    entries[i] = _byHandle.TryGetValue(handles[i], out e) ? e : null;
                    if (entries[i] != null)
                        names.Add(entries[i].Name);
                }
            }

            var byName = ReadByNames(names);
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

        public bool[] Write(long[] handles, object[] values)
        {
            var result = new bool[handles.Length];
            var items = new List<object>();
            var idx = new List<int>();
            lock (_gate)
            {
                for (int i = 0; i < handles.Length; i++)
                {
                    SubscriptionEntry e;
                    if (_byHandle.TryGetValue(handles[i], out e) && e.Found)
                    {
                        items.Add(new { name = e.Name, value = RestBridge.ToWireText(values[i]) });
                        idx.Add(i);
                    }
                }
            }
            if (items.Count == 0)
                return result;
            try
            {
                using (var doc = _rest.Post("values/write", new { items }))
                {
                    var arr = doc.RootElement.GetProperty("results");
                    for (int i = 0; i < idx.Count; i++)
                        result[idx[i]] = arr[i].GetBoolean();
                }
            }
            catch (Exception ex)
            {
                Log?.Invoke($"批量写失败：{ex.Message}");
            }
            return result;
        }

        /// <summary>按名批量读并按订阅类型定型。</summary>
        private Dictionary<string, object> ReadByNames(List<string> names)
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
                        lock (_gate)
                        {
                            if (_byName.TryGetValue(names[i], out entry))
                                vt = entry.ValueType;
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
                    }
                }
            }

            var byName = ReadByNames(unionNames);

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

        public void Dispose()
        {
            _stop = true;
        }
    }
}
