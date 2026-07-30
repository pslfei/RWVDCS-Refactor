using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using PS.Comm.DataType;
using PS.Comm.Enum;
using PS.Comm.Interfaces;

namespace RWVDCS.RemotingAdapter
{
    /// <summary>
    /// Remoting 兼容服务对象：对老客户端呈现 ICommunication/IEdit/IConsole（URI=Communication），
    /// 内部全部转发到新系统 REST API。是验证期的过渡层，验证完成后整体退役。
    /// </summary>
    public sealed class RemotingBridge : MarshalByRefObject, ICommunication, IEdit, IConsole
    {
        private readonly RestBridge _rest;
        private readonly SubscriptionRegistry _registry;
        private readonly int _port;
        private readonly Action<string> _log;

        internal RemotingBridge(RestBridge rest, SubscriptionRegistry registry, int port, Action<string> log)
        {
            _rest = rest;
            _registry = registry;
            _port = port;
            _log = log ?? (_ => { });
        }

        /// <summary>永不过期（老 RemotingObj 同款生命周期策略）。</summary>
        public override object InitializeLifetimeService() => null;

        // =================================================================
        // ICommunication - 会话
        // =================================================================
        public int Attach(ICallBack callbackobj) => _registry.Attach(callbackobj, null, true);

        public int Attach(ICallBack callbackobj, string UserName, string Password, ClientType Type, string CheckCode, bool IsUsingDataChange)
            => _registry.Attach(callbackobj, UserName, IsUsingDataChange);

        public int Attach(ICallBack callbackobj, string UserName, string Password, ClientType Type, bool IsUsingDataChange)
            => _registry.Attach(callbackobj, UserName, IsUsingDataChange);

        public void Detach(int ClientHandle) => _registry.Detach(ClientHandle);

        public bool Renew(int ClientHandle) => _registry.Find(ClientHandle) != null;

        public bool CheckClientIsValid(string UserName, string Password) => true;

        public void Pause(int ClientHandle, bool IsDoPause) => _registry.SetPaused(ClientHandle, IsDoPause);

        public bool SuperviseControl(int ClientHandle, bool IsSuperviseControlled) => true;

        public bool SetDataInformType(int ClientHandle, bool IsUsingDataChange)
        {
            _registry.SetUseDataChange(ClientHandle, IsUsingDataChange);
            return true;
        }

        // =================================================================
        // ICommunication - 订阅与读写
        // =================================================================
        public long SubscribeDirectly(int ClientHandle, string Name)
            => _registry.Subscribe(ClientHandle, new[] { Name })[0];

        public long[] SubscribeDirectly(int ClientHandle, string[] Names)
            => _registry.Subscribe(ClientHandle, Names);

        public long Subscribe(int ClientHandle, string Name)
            => _registry.Subscribe(ClientHandle, new[] { Name })[0];

        public long[] Subscribe(int ClientHandle, string[] Names)
            => _registry.Subscribe(ClientHandle, Names);

        public long[] Subscribe(int ClientHandle, string[] Names, bool IsInformed)
            => _registry.Subscribe(ClientHandle, Names);

        public void UnSubscribe(int ClientHandle, long Handle)
            => _registry.Unsubscribe(ClientHandle, new[] { Handle });

        public void UnSubscribe(int ClientHandle, long[] Handles)
            => _registry.Unsubscribe(ClientHandle, Handles);

        public void UnSubscribe(int ClientHandle)
            => _registry.Unsubscribe(ClientHandle, null);

        public object[] GetValue(int ClientHandle, long[] Handles) => _registry.Read(Handles);

        public object[] GetValue(int ClientHandle) => _registry.ReadAll(ClientHandle);

        public object GetValue(int ClientHandle, long Handle)
            => _registry.Read(new[] { Handle })[0];

        public bool SetValue(int ClientHandle, long Handle, object Value)
            => _registry.Write(ClientHandle, new[] { Handle }, new[] { Value }, null)[0];

        public bool SetValue(int ClientHandle, long Handle, object Value, string UserInfo)
            => _registry.Write(ClientHandle, new[] { Handle }, new[] { Value }, UserInfo)[0];

        public bool[] SetValue(int ClientHandle, long[] Handles, object[] Values)
            => _registry.Write(ClientHandle, Handles, Values, null);

        public bool[] SetValue(int ClientHandle, long[] Handles, object[] Values, string UserInfo)
            => _registry.Write(ClientHandle, Handles, Values, UserInfo);

        public OPCParams[] GetChangedData(int ClientHandle)
        {
            var changes = _registry.PollChanges(ClientHandle);
            var result = new OPCParams[changes.Length];
            for (int i = 0; i < changes.Length; i++)
                result[i] = new OPCParams { handle = changes[i].Key, value = changes[i].Value };
            return result;
        }

        // =================================================================
        // ICommunication - 元数据
        // =================================================================
        public string[] GetDpuCollection(int ClientHandle)
        {
            using (var doc = _rest.Get("dpus"))
                return doc.RootElement.EnumerateArray().Select(d => d.GetProperty("name").GetString()).ToArray();
        }

        public string[] GetProjectDpuCollection(int ClientHandle) => GetDpuCollection(ClientHandle);

        public string[][] GetPointCollection(int ClientHandle, string dpuname)
        {
            var rows = new List<string[]>();
            int page = 1;
            while (true)
            {
                using (var doc = _rest.Get($"points?dpu={Uri.EscapeDataString(dpuname ?? "")}&page={page}&pageSize=500"))
                {
                    foreach (var it in doc.RootElement.GetProperty("items").EnumerateArray())
                        rows.Add(new[] { it.GetProperty("name").GetString(), it.GetProperty("kind").GetString() });
                    int total = doc.RootElement.GetProperty("total").GetInt32();
                    if (page * 500 >= total)
                        break;
                    page++;
                }
            }
            return rows.ToArray();
        }

        public string[][] GetBlockCollection(int ClientHandle, string dpuname)
        {
            var rows = new List<string[]>();
            int page = 1;
            while (true)
            {
                using (var doc = _rest.Get($"blocks?dpu={Uri.EscapeDataString(dpuname ?? "")}&page={page}&pageSize=500"))
                {
                    foreach (var it in doc.RootElement.GetProperty("items").EnumerateArray())
                        rows.Add(new[] { it.GetProperty("name").GetString(), it.GetProperty("fc").GetString() });
                    int total = doc.RootElement.GetProperty("total").GetInt32();
                    if (page * 500 >= total)
                        break;
                    page++;
                }
            }
            return rows.ToArray();
        }

        public BlockDetails[] GetBlockDetails(int ClientHandle, string dpuname, string blockname)
        {
            using (var doc = _rest.Get($"block/{Uri.EscapeDataString(dpuname)}/{Uri.EscapeDataString(blockname)}"))
            {
                var root = doc.RootElement;
                var result = new List<BlockDetails>();

                // 管脚成员可通过 [DPU$]BLOCK.PIN 订阅（新 API 的块管脚读写路径）
                foreach (var pin in root.GetProperty("inputs").EnumerateArray())
                    result.Add(MakePinDetail(ClientHandle, dpuname, blockname, pin, 1));
                foreach (var pin in root.GetProperty("outputs").EnumerateArray())
                    result.Add(MakePinDetail(ClientHandle, dpuname, blockname, pin, 0));
                foreach (var f in root.GetProperty("constants").EnumerateArray())
                    result.Add(MakeFieldDetail(ClientHandle, dpuname, blockname, f, 2));
                foreach (var f in root.GetProperty("internals").EnumerateArray())
                    result.Add(MakeFieldDetail(ClientHandle, dpuname, blockname, f, 3));
                return result.ToArray();
            }
        }

        private BlockDetails MakePinDetail(int clientHandle, string dpu, string block, JsonElement pin, byte pintype)
        {
            string name = pin.GetProperty("pin").GetString();
            string type = pin.GetProperty("type").GetString();
            var d = new BlockDetails
            {
                name = name,
                pintype = pintype,
                datatype = type,
                value = JsonToObject(pin.GetProperty("value")),
                dpu = dpu,
                block = block,
                handle = SubscribeMember(clientHandle, $"{dpu}${block}.{name}"),
                isForce = pin.TryGetProperty("forced", out var f) && f.GetBoolean(),
            };
            if (pin.TryGetProperty("forceValue", out var fv) && fv.ValueKind != JsonValueKind.Null)
                d.forceValue = JsonToObject(fv);
            return d;
        }

        private BlockDetails MakeFieldDetail(int clientHandle, string dpu, string block, JsonElement f, byte pintype)
        {
            string name = f.GetProperty("name").GetString();
            bool writable = f.TryGetProperty("writable", out var w) && w.GetBoolean();
            return new BlockDetails
            {
                name = name,
                pintype = pintype,
                datatype = f.GetProperty("type").GetString(),
                value = JsonToObject(f.GetProperty("value")),
                dpu = dpu,
                block = block,
                handle = writable ? SubscribeMember(clientHandle, $"{dpu}${block}.{name}") : -1,
            };
        }

        public PointDetails[] GetPointDetail(int ClientHandle, string dpuname, string pointname)
        {
            using (var doc = _rest.Get($"point/{Uri.EscapeDataString(pointname)}"))
            {
                var root = doc.RootElement;
                var result = new List<PointDetails>();
                foreach (var f in root.GetProperty("fields").EnumerateArray())
                {
                    string member = f.GetProperty("name").GetString();
                    result.Add(new PointDetails
                    {
                        name = member,
                        value = JsonToObject(f.GetProperty("value")),
                        handle = SubscribeMember(ClientHandle, $"{pointname}.{member}"),
                    });
                }
                return result.ToArray();
            }
        }

        public PointDetails[] SearchPointDetail(int ClientHandle, string pointname)
            => GetPointDetail(ClientHandle, null, pointname);

        public PointDetails[] SearchPointDetail(int ClientHandle, ref string dpuname, string pointname)
        {
            using (var doc = _rest.Get($"point/{Uri.EscapeDataString(pointname)}"))
                dpuname = doc.RootElement.GetProperty("dpu").GetString();
            return GetPointDetail(ClientHandle, dpuname, pointname);
        }

        public string GetPointName(int ClientHandle, string dpuname, string blockname, string pinname)
        {
            using (var doc = _rest.Get($"block/{Uri.EscapeDataString(dpuname)}/{Uri.EscapeDataString(blockname)}"))
            {
                var root = doc.RootElement;
                foreach (var pin in root.GetProperty("inputs").EnumerateArray())
                {
                    if (string.Equals(pin.GetProperty("pin").GetString(), pinname, StringComparison.OrdinalIgnoreCase))
                        return pin.TryGetProperty("point", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : "";
                }
                foreach (var pin in root.GetProperty("outputs").EnumerateArray())
                {
                    if (!string.Equals(pin.GetProperty("pin").GetString(), pinname, StringComparison.OrdinalIgnoreCase))
                        continue;
                    foreach (var t in pin.GetProperty("targets").EnumerateArray())
                        return t.GetProperty("point").GetString();
                    return "";
                }
                return "";
            }
        }

        public Hashtable CheckDpu(int ClientHandle, string name)
        {
            // 0错误 1运行 2运行版本不一致 3暂停 4暂停版本不一致 5空 6DCS一致 7DCS不一致
            var table = new Hashtable();
            string state;
            using (var doc = _rest.Get("status"))
            {
                var root = doc.RootElement;
                if (!root.TryGetProperty("project", out var proj) || proj.ValueKind != JsonValueKind.Object)
                    return table;
                state = root.GetProperty("run").GetProperty("state").GetString();
            }
            int code = state == "Running" ? 1 : 3;

            bool all = string.IsNullOrEmpty(name) || name.ToUpperInvariant() == "DCS" || name.ToUpperInvariant().StartsWith("DCS|||");
            if (all)
            {
                foreach (string dpu in GetDpuCollection(ClientHandle))
                    table[dpu] = code;
                table["DCS"] = 6; // 适配器视角：运行中的就是已下装版本，恒一致
            }
            else
            {
                table[name] = code;
            }
            return table;
        }

        public string GetCurrentProjectInfo(int ClientHandle)
        {
            string project, run;
            return _rest.TryGetStatus(out project, out run) ? (project ?? "") : "";
        }

        public string GetDataFromEXDB(string ColumnName, string Key) => "";

        // =================================================================
        // ICommunication - 下装
        // =================================================================
        public bool DownLoad(int ClientHandle, string DBPath, string DataFilePath, string dpuname, int opid)
        {
            // 老语义：DBPath=工程库，DataFilePath=工况文件。
            // 新系统在线下装自带状态保留（prepare+commit），工况文件参数不再需要。
            try
            {
                string planId;
                using (var doc = _rest.Post("download/prepare", new { mdbPath = DBPath }))
                    planId = doc.RootElement.GetProperty("planId").GetString();
                using (_rest.Post("download/commit", new { planId, backup = true }))
                {
                }
                _log($"在线下装完成：{DBPath}");
                return true;
            }
            catch (Exception ex)
            {
                _log($"在线下装失败：{ex.Message}");
                return false;
            }
        }

        public bool DownLoad(int ClientHandle, string dpuname, int opid)
        {
            _log("按 DPU 下装在适配器中不支持（新系统以整工程差异下装）");
            return false;
        }

        // =================================================================
        // ICommunication - 事件通道/筛选/历史（过渡期空实现或映射）
        // =================================================================
        public string[] Review(int ClientHandle, string dpuname, string[] members, object[] values) => new string[0];

        public RRParams[] Review(int ClientHandle, string[] dpunames, string[] types, ReviewParams[][] reviewparams) => new RRParams[0];

        public Dictionary<string, BlockDetails[]> Review(int ClientHandle, string dpuname, ReviewFilter filter)
            => new Dictionary<string, BlockDetails[]>();

        public PointDetails[] GetEventChannelDataChanged(int ClientHandle, int EventType) => new PointDetails[0];

        public OperationRecordUnit[] GetOperationRecordChannelDataChanged(int ClientHandle) => new OperationRecordUnit[0];

        public void SetEventChange(int ClientHandle, int EventType, bool IsValid)
        {
        }

        public HistoryAlarmUnit[] GetRecordsHistoryAlarm(int ClientHandle, string PointName, long Timestart, long Timeend) => new HistoryAlarmUnit[0];

        public HistoryAlarmUnit[][] GetRecordsHistoryAlarm(int ClientHandle, string[] PointNames, long Timestart, long Timeend) => new HistoryAlarmUnit[0][];

        public HistoryAlarmUnit[] GetRecordsHistoryAlarm(int ClientHandle, long Timestart, long Timeend) => new HistoryAlarmUnit[0];

        public HistoryPointUnit[] GetRecordsHistoryPoint(int ClientHandle, string PointName, long Timestart, long Timeend, int RetMaxcount)
        {
            // 新系统内嵌历史站：unix 毫秒 → .NET Ticks（老客户端用本地时间 Ticks）
            try
            {
                var result = new List<HistoryPointUnit>();
                using (var doc = _rest.Get($"history/query?point={Uri.EscapeDataString(PointName)}&max={Math.Max(1, RetMaxcount)}"))
                {
                    foreach (var s in doc.RootElement.GetProperty("samples").EnumerateArray())
                    {
                        long ticks = DateTimeOffset.FromUnixTimeMilliseconds(s.GetProperty("timeMs").GetInt64()).LocalDateTime.Ticks;
                        if (Timestart > 0 && ticks < Timestart)
                            continue;
                        if (Timeend > 0 && ticks > Timeend)
                            continue;
                        result.Add(new HistoryPointUnit
                        {
                            TimeTick = ticks,
                            PointValue = Convert.ToString(JsonToObject(s.GetProperty("value")), CultureInfo.InvariantCulture),
                        });
                    }
                }
                return result.ToArray();
            }
            catch (Exception ex)
            {
                _log($"历史查询失败 {PointName}：{ex.Message}");
                return new HistoryPointUnit[0];
            }
        }

        public HistoryPointUnit[][] GetRecordsHistoryPoint(int ClientHandle, string[] PointNames, long Timestart, long Timeend, int RetMaxcount)
        {
            var result = new HistoryPointUnit[PointNames.Length][];
            for (int i = 0; i < PointNames.Length; i++)
                result[i] = GetRecordsHistoryPoint(ClientHandle, PointNames[i], Timestart, Timeend, RetMaxcount);
            return result;
        }

        public HistoryPointDetailsInfo1[] GetDetailsInfoHistoryPoint(int ClientHandle) => new HistoryPointDetailsInfo1[0];

        public HistoryOperationRecordUnit[] GetRecordsHistoryOperationRecord(int ClientHandle, long Timestart, long Timeend) => new HistoryOperationRecordUnit[0];

        public bool FlushHistoryinfoPointDetailsToHistorylibDB(int ClientHandle) => true;

        public HistoryPointDetailsInfo[] GetMainDetailsInfoHistoryPoint(int ClientHandle) => new HistoryPointDetailsInfo[0];

        public bool StartHistorylibRealtimeServer(int ClientHandle) => true;   // 内嵌历史站常开

        public bool StopHistorylibRealtimeServer(int ClientHandle) => false;   // 不允许老客户端关停

        public bool DeleteHistorylibAlarmRecords(int ClientHandle, long Timestart, long timeend) => false;

        public bool DeleteHistorylibOperationRecords(int ClientHandle, long Timestart, long timeend) => false;

        public bool DeleteHistorylibHistoryPointRecords(int ClientHandle, long Timestart, long timeend) => false;

        public int GetHistorylibServerState(int ClientHandle) => 1;

        public bool UserHistoryLog(int ClientHandle, string UserInfo, int Type, string Info)
        {
            _log($"用户操作记录 #{ClientHandle} {UserInfo}: {Info}");
            return true;
        }

        // =================================================================
        // IEdit - 在线编辑（过渡期不支持，指向在线下装）
        // =================================================================
        public int AddLink(int ClientHandle, string dpuname, string blockname, string pinname, string pinvalue) => NotEditable();

        public int DeleteLink(int ClientHandle, string dpuname, string blockname, string pinname) => NotEditable();

        public int AddBlock(int ClientHandle, string dpuname, string blockname, string fcname, string order, ArrayList paramslist) => NotEditable();

        public int DeleteBlock(int ClientHandle, string dpuname, string blockname, string opid) => NotEditable();

        public bool AddRealVar(int ClientHandle, string dpuname, string varname, string datatype, string defaultvalue, string forcevalue) => NotEditable() != 0;

        public bool DeleteRealVar(int ClientHandle, string dpuname, string varname) => NotEditable() != 0;

        public bool AddDpu(int ClientHandle, string dpuname) => NotEditable() != 0;

        public bool DeleteDpu(int ClientHandle, string dpuname) => NotEditable() != 0;

        public bool AddPoint(int ClientHandle, string name, string datatype, string defaultvalue, string forcevalue) => NotEditable() != 0;

        public bool DeletePoint(int ClientHandle, string name) => NotEditable() != 0;

        public void UnSubscribeForcibly(int ClientHandle, long Handle) => _registry.UnsubscribeEverywhere(Handle);

        private int NotEditable()
        {
            _log("在线编辑接口在适配器中不支持：请改库后走在线下装（DownLoad/Web 下装页）");
            return 0;
        }

        // =================================================================
        // IConsole - 运行控制/工况
        // =================================================================
        public void ResetServer(int ClientHandle) => _log("ResetServer 请求被忽略（请用新系统脚本重启）");

        public void ExitServer(int ClientHandle) => _log("ExitServer 请求被忽略（请用新系统脚本停机）");

        public bool InitProj(int ClientHandle, string dbpath)
        {
            try
            {
                using (_rest.Post("project/load", new { mdbPath = dbpath }))
                {
                }
                _log($"装载工程：{dbpath}");
                return true;
            }
            catch (Exception ex)
            {
                _log($"装载工程失败：{ex.Message}");
                return false;
            }
        }

        public int LoadFile(int ClientHandle, string filename)
        {
            // 老语义：加载 .wrk 工况文件路径。新语义：按名加载工况仓库条目（文件名去扩展名）。
            string name = System.IO.Path.GetFileNameWithoutExtension(filename);
            try
            {
                using (_rest.Post($"store/conditions/{Uri.EscapeDataString(name)}/load"))
                {
                }
                _log($"加载工况：{name}");
                return 1;
            }
            catch (Exception ex)
            {
                _log($"加载工况失败 {name}：{ex.Message}");
                return 0;
            }
        }

        public bool UpLoadState(int ClientHandle, string projfile, string statefile)
        {
            _log("UpLoadState（上装）在适配器中不支持");
            return false;
        }

        public bool SaveDCS(int ClientHandle, string path, string filename)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(filename);
            try
            {
                using (_rest.Post("store/conditions", new { name, comment = $"Remoting 客户端 #{ClientHandle} 保存" }))
                {
                }
                _log($"保存工况：{name}");
                return true;
            }
            catch (Exception ex)
            {
                _log($"保存工况失败 {name}：{ex.Message}");
                return false;
            }
        }

        public void SetDCSCycleTime(int ClientHandle, double time)
        {
            try
            {
                using (_rest.Put("dpus/cycle", new { seconds = time }))
                {
                }
                _log($"统一设置周期：{time}s");
            }
            catch (Exception ex)
            {
                _log($"设置周期失败：{ex.Message}");
            }
        }

        public void RunDCS(int ClientHandle) => RunControl("run/start", "连续运行");

        public void PauseDCS(int ClientHandle) => RunControl("run/pause", "暂停");

        public void StopDCS(int ClientHandle) => RunControl("run/stop", "完全停止");

        public void SingleStepDCS(int ClientHandle)
        {
            try
            {
                using (_rest.Post("run/step", new { cycles = 1 }))
                {
                }
            }
            catch (Exception ex)
            {
                _log($"单步失败：{ex.Message}");
            }
        }

        private void RunControl(string path, string what)
        {
            try
            {
                using (_rest.Post(path))
                {
                }
                _log($"运行控制：{what}");
            }
            catch (Exception ex)
            {
                _log($"{what}失败：{ex.Message}");
            }
        }

        // =================================================================
        // IState
        // =================================================================
        public MachineState GetMachineState() => MachineState.Running;

        public int GetMachineType() => (int)MachineType.SimulatorServer;

        public DateTime GetTime() => DateTime.Now;

        public string GetMachineName() => Environment.MachineName;

        public string GetNetAddress() => $"0.0.0.0:{_port}";

        public void InformMachineStateChange(MachineState ServerState, string Reason)
        {
        }

        public void InformServiceStateChange(ServiceState ServiceState, string Reason)
        {
        }

        public ServiceState GetServiceState()
        {
            string project, run;
            if (!_rest.TryGetStatus(out project, out run))
                return ServiceState.Stopped;
            switch (run)
            {
                case "Running": return ServiceState.Started;
                case "Paused": return ServiceState.Pausing;
                default: return ServiceState.Stopped;
            }
        }

        // =================================================================
        // 辅助
        // =================================================================
        /// <summary>成员级订阅：详情接口返回的 handle 可直接用于 GetValue/SetValue/回调。</summary>
        private long SubscribeMember(int clientHandle, string name)
        {
            try
            {
                return _registry.Subscribe(clientHandle, new[] { name })[0];
            }
            catch
            {
                return -1;
            }
        }

        private static object JsonToObject(JsonElement e)
        {
            switch (e.ValueKind)
            {
                case JsonValueKind.Number:
                    return e.TryGetInt64(out long l) && !e.GetRawText().Contains(".") ? (object)l : e.GetDouble();
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.String: return e.GetString();
                case JsonValueKind.Null:
                case JsonValueKind.Undefined: return null;
                default: return e.ToString();
            }
        }
    }
}
