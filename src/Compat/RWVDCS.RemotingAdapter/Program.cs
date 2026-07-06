using System;
using System.Collections;
using System.Runtime.Remoting;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Ipc;
using System.Runtime.Remoting.Channels.Tcp;
using System.Runtime.Serialization.Formatters;
using System.Text;

namespace RWVDCS.RemotingAdapter
{
    /// <summary>
    /// Remoting 兼容适配器入口。
    /// 对老客户端（HMI/IOMAP/Alarm/教练员站）呈现与老 Simulator 完全一致的
    /// tcp|ipc://host:port/Communication 端点；内部转发到新系统 REST API。
    /// 用法：rwvdcs-remoting-adapter [--port 8000] [--api http://localhost:8080] [--poll 200]
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            int port = 8000;                              // 老 Simulator 默认端口
            string api = "http://localhost:8080";
            int pollMs = 200;                             // 老系统默认扫描周期同款

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--port": port = int.Parse(args[++i]); break;
                    case "--api": api = args[++i]; break;
                    case "--poll": pollMs = int.Parse(args[++i]); break;
                    case "-h":
                    case "--help":
                        Console.WriteLine("用法: rwvdcs-remoting-adapter [--port 8000] [--api http://localhost:8080] [--poll 200]");
                        return 0;
                }
            }

            Action<string> log = msg => Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} [适配器] {msg}");

            var rest = new RestBridge(api);
            string project, run;
            if (rest.TryGetStatus(out project, out run))
                log($"已连上新系统 {api}（工程: {project ?? "未装载"}，运行态: {run}）");
            else
                log($"警告：暂时连不上新系统 {api}，将在客户端调用时重试");

            var registry = new SubscriptionRegistry(rest, pollMs);
            registry.Log += log;

            // 通道注册（对齐老 ServerObj：TCP + IPC，BinaryFormatter Full 信任）
            var serverProvider = new BinaryServerFormatterSinkProvider { TypeFilterLevel = TypeFilterLevel.Full };
            var clientProvider = new BinaryClientFormatterSinkProvider();

            try
            {
                IDictionary tcpProps = new Hashtable();
                tcpProps["port"] = port;
                tcpProps["timeout"] = 3000;
                ChannelServices.RegisterChannel(new TcpChannel(tcpProps, clientProvider, serverProvider), false);
                log($"TCP 通道就绪：tcp://0.0.0.0:{port}/Communication");
            }
            catch (Exception ex)
            {
                log($"TCP 端口 {port} 注册失败：{ex.Message}");
                return 2;
            }

            try
            {
                var ipcServerProvider = new BinaryServerFormatterSinkProvider { TypeFilterLevel = TypeFilterLevel.Full };
                IDictionary ipcProps = new Hashtable();
                ipcProps["portName"] = "localhost:" + port;   // 老客户端 ipc://localhost:{port}/Communication
                ipcProps["timeout"] = 3000;
                ChannelServices.RegisterChannel(new IpcChannel(ipcProps, new BinaryClientFormatterSinkProvider(), ipcServerProvider), false);
                log($"IPC 通道就绪：ipc://localhost:{port}/Communication");
            }
            catch (Exception ex)
            {
                log($"IPC 通道注册失败（仅影响本机 IPC 客户端）：{ex.Message}");
            }

            var bridge = new RemotingBridge(rest, registry, port, log);
            RemotingServices.Marshal(bridge, "Communication");
            log("Remoting 兼容适配器已启动（Ctrl+C 或输入 quit 退出）");

            var quit = new System.Threading.ManualResetEventSlim(false);
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                quit.Set();
            };
            var stdin = new System.Threading.Thread(() =>
            {
                while (true)
                {
                    string line = Console.ReadLine();
                    if (line == null)
                        return;
                    string t = line.Trim().ToLowerInvariant();
                    if (t == "quit" || t == "exit" || t == "q")
                    {
                        quit.Set();
                        return;
                    }
                }
            }) { IsBackground = true };
            stdin.Start();

            quit.Wait();
            log("正在退出……");
            RemotingServices.Disconnect(bridge);
            registry.Dispose();
            rest.Dispose();
            return 0;
        }
    }
}
