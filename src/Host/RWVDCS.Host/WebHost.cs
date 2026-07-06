using System.Text;
using RWVDCS.Api;

namespace RWVDCS.Host;

/// <summary>
/// Web 管理台模式：RuntimeHost（编排层）+ ApiServer（Kestrel REST/SSE + 静态界面）。
/// 用法：rwvdcs [工程.mdb] --web [端口] [--data 目录] [--arena 目录] [--blocks-src 目录]
///       [--no-history] [--start]
/// 不带 mdb 参数则空载启动，从 Web 界面装载工程。
/// </summary>
internal static class WebHost
{
    public static int Run(string[] args)
    {
        string? mdbPath = args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal) ? args[0] : null;
        int port = 8080;
        string? dataDir = null, arenaDir = null, blocksSrc = null;
        bool history = true, autoStart = false;

        for (int i = mdbPath == null ? 0 : 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--web":
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out int p))
                    {
                        port = p;
                        i++;
                    }
                    break;
                case "--data": dataDir = args[++i]; break;
                case "--arena": arenaDir = args[++i]; break;
                case "--blocks-src": blocksSrc = args[++i]; break;
                case "--no-history": history = false; break;
                case "--start": autoStart = true; break;
                // 与经典模式共用的参数在 web 模式下无意义，宽容跳过其值
                case "--history" or "--stats-csv" or "--monitor": i++; break;
                default:
                    Console.Error.WriteLine($"[web] 忽略参数: {args[i]}");
                    break;
            }
        }

        blocksSrc ??= Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Blocks", "RWVDCS.Blocks", "RW"));
        dataDir ??= Path.GetFullPath("rwvdcs-data");

        using var host = new RuntimeHost(new RuntimeHostOptions
        {
            BlocksAssembly = typeof(Blocks.RW.VSET).Assembly,
            DataDirectory = dataDir,
            BlocksSourceDir = Directory.Exists(blocksSrc) ? blocksSrc : null,
            EnableHistory = history,
            ArenaDirectory = arenaDir,
        });

        if (mdbPath != null)
        {
            if (!File.Exists(mdbPath))
            {
                Console.Error.WriteLine($"工程库不存在: {mdbPath}");
                return 2;
            }
            host.LoadProject(mdbPath);
            if (autoStart)
                host.Start();
        }

        return RunServer(host, port).GetAwaiter().GetResult();
    }

    private static async Task<int> RunServer(RuntimeHost host, int port)
    {
        await using var server = new ApiServer(host, port);
        try
        {
            await server.StartAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[web] 端口 {port} 启动失败：{ex.Message}");
            return 3;
        }

        host.Log.Info("Web", $"管理台已启动：{server.Url}（Ctrl+C 或输入 quit 退出）");

        var quit = new TaskCompletionSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            quit.TrySetResult();
        };

        // 控制台仍接收 quit/exit（服务化部署时 stdin 关闭，则只有 Ctrl+C/SIGTERM）
        _ = Task.Run(() =>
        {
            while (true)
            {
                string? line = Console.ReadLine();
                if (line == null)
                    return;                    // stdin 关闭：交给 Ctrl+C
                if (line.Trim().ToLowerInvariant() is "quit" or "exit" or "q")
                {
                    quit.TrySetResult();
                    return;
                }
            }
        });

        await quit.Task;
        host.Log.Info("Web", "正在退出（停扫描线程、落盘历史站）……");
        return 0;
    }
}
