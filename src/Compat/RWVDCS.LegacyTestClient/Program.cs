using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Tcp;
using System.Runtime.Serialization.Formatters;
using System.Text;
using System.Threading;
using PS.Comm.Enum;
using PS.Comm.Interfaces;

namespace RWVDCS.LegacyTestClient
{
    /// <summary>
    /// 老协议测试客户端：以 HMI 同款方式（.NET Remoting + BinaryFormatter + ICallBack 回调）
    /// 连接兼容适配器，验证订阅/读写/回调/运行控制/元数据全链路。
    /// 用法：rwvdcs-legacy-client [--server tcp://localhost:8000/Communication]
    ///       [--points 名1,名2,...] [--watch 秒] [--set 名=值] [--run|--pause|--step]
    /// </summary>
    internal static class Program
    {
        /// <summary>客户端回调对象（老 HMI 的 CallBackObj 等价物）。</summary>
        private sealed class CallbackSink : MarshalByRefObject, ICallBack
        {
            public int DataChangeCount;

            public override object InitializeLifetimeService() => null;

            public void InformDataChange(long[] Handles, object[] Values)
            {
                Interlocked.Add(ref DataChangeCount, Handles.Length);
                var sb = new StringBuilder().Append($"{DateTime.Now:HH:mm:ss.fff} [回调] InformDataChange {Handles.Length} 项:");
                for (int i = 0; i < Math.Min(Handles.Length, 6); i++)
                    sb.Append($" #{Handles[i]}={FormatValue(Values[i])}");
                if (Handles.Length > 6)
                    sb.Append(" ...");
                Console.WriteLine(sb.ToString());
            }

            public string IP => "127.0.0.1";

            public ClientType ClientIdentityType { get; set; } = ClientType.Display;

            public string UserName { get; set; } = "legacy-test";

            public string Password { get; set; } = "";

            public void InformEventChannelDataChanged(int EventType, string[] ChangedTagNames, object[] ChangedValues)
                => Console.WriteLine($"[回调] 事件通道 {EventType}: {ChangedTagNames?.Length ?? 0} 项");

            public void InformEventChannelOperationRecordDataChanged(string[] OperNames, long[] TimeTicks, string[] OldValues, string[] NewValues, string[] Operators)
                => Console.WriteLine($"[回调] 操作记录: {OperNames?.Length ?? 0} 项");

            public void TSCCServerStateChange(string[] state)
            {
            }

            public void TSCCServerFaultDataChange(object changes)
            {
            }

            // IState（服务器可能反查客户端状态）
            public MachineState GetMachineState() => MachineState.Running;

            public int GetMachineType() => (int)MachineType.HMIClient;

            public DateTime GetTime() => DateTime.Now;

            public string GetMachineName() => Environment.MachineName;

            public string GetNetAddress() => "127.0.0.1:0";

            public void InformMachineStateChange(MachineState ServerState, string Reason)
                => Console.WriteLine($"[回调] 服务器状态: {ServerState} ({Reason})");

            public void InformServiceStateChange(ServiceState ServiceState, string Reason)
                => Console.WriteLine($"[回调] 服务状态: {ServiceState} ({Reason})");

            public ServiceState GetServiceState() => ServiceState.Started;
        }

        private static string FormatValue(object v)
            => v == null ? "null" : Convert.ToString(v, CultureInfo.InvariantCulture) + ":" + v.GetType().Name;

        private static string PinTypeName(byte t)
            => t == 0 ? "输出" : t == 1 ? "输入" : t == 2 ? "规格数" : t == 3 ? "内部" : "IO";

        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            string server = "tcp://localhost:8000/Communication";
            string[] points = new string[0];
            int watchSeconds = 0;
            string setExpr = null;
            string runCmd = null;
            string blockSpec = null;
            string pointDetail = null;
            string saveCond = null;
            string loadCond = null;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--server": server = args[++i]; break;
                    case "--points": points = args[++i].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries); break;
                    case "--watch": watchSeconds = int.Parse(args[++i]); break;
                    case "--set": setExpr = args[++i]; break;
                    case "--run": runCmd = "run"; break;
                    case "--pause": runCmd = "pause"; break;
                    case "--step": runCmd = "step"; break;
                    case "--block": blockSpec = args[++i]; break;          // DPU/BLOCK
                    case "--pointdetail": pointDetail = args[++i]; break;
                    case "--savecond": saveCond = args[++i]; break;
                    case "--loadcond": loadCond = args[++i]; break;
                    case "-h":
                    case "--help":
                        Console.WriteLine("用法: rwvdcs-legacy-client [--server tcp://...] [--points 名1,名2] [--watch 秒] [--set 名=值]");
                        Console.WriteLine("      [--run|--pause|--step] [--block DPU/块名] [--pointdetail 点名] [--savecond 名] [--loadcond 名]");
                        return 0;
                }
            }

            // 客户端通道（port=0 随机端口，供服务器回调进来；老 ClientObj 同款做法）
            var serverProvider = new BinaryServerFormatterSinkProvider { TypeFilterLevel = TypeFilterLevel.Full };
            var clientProvider = new BinaryClientFormatterSinkProvider();
            IDictionary props = new Hashtable();
            props["port"] = 0;
            props["timeout"] = 5000;
            ChannelServices.RegisterChannel(new TcpChannel(props, clientProvider, serverProvider), false);

            Console.WriteLine($"连接 {server} ...");
            var comm = (ICommunication)Activator.GetObject(typeof(ICommunication), server);
            var console = (IConsole)Activator.GetObject(typeof(IConsole), server);

            var sink = new CallbackSink();
            int client = comm.Attach(sink, "legacy-test", "", ClientType.Display, true);
            Console.WriteLine($"Attach 成功，ClientHandle = {client}");
            Console.WriteLine($"服务状态: {comm.GetServiceState()}  机器: {comm.GetMachineName()}  时间: {comm.GetTime():HH:mm:ss}");

            // 元数据
            string[] dpus = comm.GetDpuCollection(client);
            Console.WriteLine($"DPU 数量: {dpus.Length}（前5个: {string.Join(", ", dpus.Take(5))}）");

            var check = comm.CheckDpu(client, null);
            Console.WriteLine($"CheckDpu: {check.Count} 项（DCS => {check["DCS"]}）");

            if (points.Length > 0)
            {
                long[] handles = comm.Subscribe(client, points);
                Console.WriteLine("订阅结果:");
                for (int i = 0; i < points.Length; i++)
                    Console.WriteLine($"  {points[i],-40} handle={handles[i]}");

                object[] values = comm.GetValue(client, handles);
                Console.WriteLine("GetValue:");
                for (int i = 0; i < points.Length; i++)
                    Console.WriteLine($"  {points[i],-40} = {FormatValue(values[i])}");

                if (setExpr != null)
                {
                    int eq = setExpr.IndexOf('=');
                    string name = setExpr.Substring(0, eq);
                    string sval = setExpr.Substring(eq + 1);
                    int idx = Array.FindIndex(points, p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0)
                    {
                        object typed = values[idx] is bool ? (object)(sval == "1" || sval.Equals("true", StringComparison.OrdinalIgnoreCase))
                            : values[idx] is float ? (object)float.Parse(sval, CultureInfo.InvariantCulture)
                            : sval;
                        bool ok = comm.SetValue(client, handles[idx], typed);
                        object after = comm.GetValue(client, handles[idx]);
                        Console.WriteLine($"SetValue {name} <= {sval} : {(ok ? "OK" : "失败")}，回读 = {FormatValue(after)}");
                    }
                    else
                    {
                        Console.WriteLine($"--set 的点 {name} 不在 --points 里");
                    }
                }
            }

            if (blockSpec != null)
            {
                int slash = blockSpec.IndexOf('/');
                string dpu = blockSpec.Substring(0, slash);
                string block = blockSpec.Substring(slash + 1);
                var details = comm.GetBlockDetails(client, dpu, block);
                Console.WriteLine($"GetBlockDetails {dpu}/{block}: {details.Length} 项");
                foreach (var d in details.Take(20))
                    Console.WriteLine($"  [{PinTypeName(d.pintype)}] {d.name,-16} {d.datatype,-10} = {FormatValue(d.value)}  handle={d.handle}{(d.isForce ? " [强制]" : "")}");
                if (details.Length > 20)
                    Console.WriteLine($"  ...共 {details.Length} 项");
            }

            if (pointDetail != null)
            {
                string dpuName = null;
                var details = comm.SearchPointDetail(client, ref dpuName, pointDetail);
                Console.WriteLine($"SearchPointDetail {pointDetail}（DPU={dpuName}）: {details.Length} 个成员");
                foreach (var d in details)
                    Console.WriteLine($"  {d.name,-14} = {FormatValue(d.value),-22} handle={d.handle}");
            }

            if (saveCond != null)
            {
                bool ok = console.SaveDCS(client, "", saveCond);
                Console.WriteLine($"SaveDCS({saveCond}): {(ok ? "OK" : "失败")}");
            }

            if (loadCond != null)
            {
                int r = console.LoadFile(client, loadCond);
                Console.WriteLine($"LoadFile({loadCond}): {(r == 1 ? "OK" : "失败")}");
            }

            if (runCmd != null)
            {
                switch (runCmd)
                {
                    case "run":
                        console.RunDCS(client);
                        Console.WriteLine("已发送 RunDCS");
                        break;
                    case "pause":
                        console.PauseDCS(client);
                        Console.WriteLine("已发送 PauseDCS");
                        break;
                    case "step":
                        console.SingleStepDCS(client);
                        Console.WriteLine("已发送 SingleStepDCS");
                        break;
                }
            }

            if (watchSeconds > 0)
            {
                Console.WriteLine($"等待回调 {watchSeconds}s（InformDataChange）……");
                Thread.Sleep(watchSeconds * 1000);
                Console.WriteLine($"共收到 {sink.DataChangeCount} 项数据变化通知");
            }

            comm.Detach(client);
            Console.WriteLine("已断开");
            return 0;
        }
    }
}
