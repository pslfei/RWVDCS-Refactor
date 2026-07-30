using System.Text;
using System.Text.Json;

namespace RWVDCS.Tools.ApiClient;

/// <summary>
/// 教练员站接口测试客户端（rwvdcs-cli）。
/// 覆盖运行控制、工况/快照管理、批量读写、在线下装、日志等 REST 接口，
/// 同时充当教练员站对接的调用示例：每条命令即一个最小可用的 HTTP 调用序列。
/// </summary>
internal static class Program
{
    private static readonly JsonSerializerOptions JsonOpt = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static HttpClient _http = null!;

    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        string url = Environment.GetEnvironmentVariable("RWVDCS_URL") ?? "http://localhost:8080";
        var rest = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--url" && i + 1 < args.Length)
                url = args[++i];
            else
                rest.Add(args[i]);
        }

        if (rest.Count == 0 || rest[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        _http = new HttpClient { BaseAddress = new Uri(url.TrimEnd('/') + "/api/"), Timeout = TimeSpan.FromSeconds(30) };

        try
        {
            return await Dispatch(rest);
        }
        catch (ApiException ex)
        {
            Console.Error.WriteLine($"[API错误] {ex.Message}");
            return 2;
        }
        catch (HttpRequestException ex)
        {
            Console.Error.WriteLine($"[连接失败] {url} ：{ex.Message}");
            return 3;
        }
    }

    private static async Task<int> Dispatch(List<string> a)
    {
        string cmd = a[0].ToLowerInvariant();
        switch (cmd)
        {
            case "status":
            {
                var doc = await Get("status");
                var root = doc.RootElement;
                if (root.TryGetProperty("project", out var proj) && proj.ValueKind == JsonValueKind.Object)
                {
                    Console.WriteLine($"工程: {proj.GetProperty("mdbPath").GetString()}");
                    Console.WriteLine($"指纹: {proj.GetProperty("fingerprint").GetString()}  版本: v{proj.GetProperty("version").GetInt32()}");
                    Console.WriteLine($"规模: {proj.GetProperty("dpuCount").GetInt32()} DPU / {proj.GetProperty("pointCount").GetInt32()} 点 / {proj.GetProperty("commandCount").GetInt32()} 块");
                }
                else
                {
                    Console.WriteLine("工程: (未装载)");
                }
                Console.WriteLine($"运行: {root.GetProperty("run").GetProperty("state").GetString()}");
                return 0;
            }

            case "dpus":
            {
                var doc = await Get("runtime/dpus");
                Console.WriteLine($"{"DPU",-12} {"状态",-10} {"周期s",8} {"块数",8} {"当前ms",9} {"均值ms",9} {"P99ms",9} {"超限",6} {"扫描次数",10}");
                foreach (var d in doc.RootElement.EnumerateArray())
                {
                    bool hasStats = d.TryGetProperty("stats", out var s) && s.ValueKind == JsonValueKind.Object;
                    Console.WriteLine($"{d.GetProperty("name").GetString(),-12} {d.GetProperty("state").GetString(),-10} " +
                        $"{d.GetProperty("cycleSeconds").GetSingle(),8:F3} {d.GetProperty("commandCount").GetInt32(),8} " +
                        (hasStats
                            ? $"{s.GetProperty("curMs").GetDouble(),9:F2} {s.GetProperty("avgMs").GetDouble(),9:F2} {s.GetProperty("p99Ms").GetDouble(),9:F2} {s.GetProperty("overruns").GetInt64(),6} {s.GetProperty("count").GetInt64(),10}"
                            : $"{"-",9} {"-",9} {"-",9} {"-",6} {"-",10}"));
                }
                return 0;
            }

            case "start": Print(await Post("run/start")); return 0;
            case "pause": Print(await Post("run/pause")); return 0;
            case "stop": Print(await Post("run/stop")); return 0;

            case "step":
            {
                int n = a.Count > 1 ? int.Parse(a[1]) : 1;
                Print(await Post("run/step", new { cycles = n }));
                return 0;
            }

            case "cycle":
            {
                if (a.Count < 2)
                    return Fail("用法: cycle <秒> [DPU名]");
                float sec = float.Parse(a[1]);
                if (a.Count > 2)
                    Print(await Put($"dpus/{Uri.EscapeDataString(a[2])}/cycle", new { seconds = sec }));
                else
                    Print(await Put("dpus/cycle", new { seconds = sec }));
                return 0;
            }

            case "load":
            {
                if (a.Count < 2)
                    return Fail("用法: load <工程.mdb路径>");
                Print(await Post("project/load", new { mdbPath = Path.GetFullPath(a[1]) }));
                return 0;
            }

            case "read":
            {
                if (a.Count < 2)
                    return Fail("用法: read <名字...>（POINT / POINT.member / DPU$POINT.member）");
                var names = a.Skip(1).ToArray();
                var doc = await Post("values/read", new { names });
                var values = doc.RootElement.GetProperty("values");
                for (int i = 0; i < names.Length; i++)
                    Console.WriteLine($"{names[i],-40} = {values[i].ToString()}");
                return 0;
            }

            case "write":
            {
                if (a.Count < 2)
                    return Fail("用法: write <名字=值 ...>");
                var items = new List<object>();
                foreach (var kv in a.Skip(1))
                {
                    int eq = kv.IndexOf('=');
                    if (eq <= 0)
                        return Fail($"格式错误（应为 名字=值）：{kv}");
                    items.Add(new { name = kv[..eq], value = kv[(eq + 1)..] });
                }
                var doc = await Post("values/write", new { items });
                var results = doc.RootElement.GetProperty("results");
                for (int i = 0; i < items.Count; i++)
                    Console.WriteLine($"{a[i + 1],-40} {(results[i].GetBoolean() ? "OK" : "失败")}");
                return 0;
            }

            case "force":
            {
                if (a.Count < 3)
                    return Fail("用法: force <点名> <on|off> [强制值]");
                bool on = a[2].ToLowerInvariant() is "on" or "1" or "true";
                Print(await Post($"point/{Uri.EscapeDataString(a[1])}/force",
                    new { forced = on, value = a.Count > 3 ? a[3] : null }));
                return 0;
            }

            case "watch":
            {
                var names = new List<string>();
                int intervalMs = 1000;
                for (int i = 1; i < a.Count; i++)
                {
                    if (a[i] == "--ms" && i + 1 < a.Count)
                        intervalMs = int.Parse(a[++i]);
                    else
                        names.Add(a[i]);
                }
                if (names.Count == 0)
                    return Fail("用法: watch <名字...> [--ms 1000]");
                Console.WriteLine($"每 {intervalMs}ms 轮询，Ctrl+C 退出");
                while (true)
                {
                    var doc = await Post("values/read", new { names = names.ToArray() });
                    var values = doc.RootElement.GetProperty("values");
                    var sb = new StringBuilder().Append(DateTime.Now.ToString("HH:mm:ss.fff"));
                    for (int i = 0; i < names.Count; i++)
                        sb.Append("  ").Append(names[i]).Append('=').Append(values[i].ToString());
                    Console.WriteLine(sb.ToString());
                    await Task.Delay(intervalMs);
                }
            }

            case "cond":
            case "snap":
            {
                string res = cmd == "cond" ? "conditions" : "snapshots";
                string what = cmd == "cond" ? "工况" : "快照";
                string sub = a.Count > 1 ? a[1].ToLowerInvariant() : "list";
                switch (sub)
                {
                    case "list":
                    {
                        var doc = await Get($"store/{res}");
                        Console.WriteLine($"{"名称",-24} {"指纹",-18} {"版本",4} {"大小",10}  保存时间");
                        foreach (var e in doc.RootElement.EnumerateArray())
                        {
                            Console.WriteLine($"{e.GetProperty("name").GetString(),-24} {e.GetProperty("fingerprint").GetString(),-18} " +
                                $"{FmtVersion(e),4} {FmtBytes(e.GetProperty("sizeBytes").GetInt64()),10}  {e.GetProperty("savedAtUtc").GetDateTime().ToLocalTime():yyyy-MM-dd HH:mm:ss}");
                        }
                        return 0;
                    }
                    case "save":
                        if (a.Count < 3)
                            return Fail($"用法: {cmd} save <名称> [备注]");
                        Print(await Post($"store/{res}", new { name = a[2], comment = a.Count > 3 ? string.Join(' ', a.Skip(3)) : null }));
                        return 0;
                    case "load":
                        if (a.Count < 3)
                            return Fail($"用法: {cmd} load <名称>");
                        Print(await Post($"store/{res}/{Uri.EscapeDataString(a[2])}/load"));
                        return 0;
                    case "del":
                    case "delete":
                        if (a.Count < 3)
                            return Fail($"用法: {cmd} del <名称>");
                        Print(await Delete($"store/{res}/{Uri.EscapeDataString(a[2])}"));
                        return 0;
                    default:
                        return Fail($"未知 {what}子命令: {sub}（list/save/load/del）");
                }
            }

            case "download":
            {
                string sub = a.Count > 1 ? a[1].ToLowerInvariant() : "";
                if (sub == "prepare" && a.Count > 2)
                {
                    var doc = await Post("download/prepare", new { mdbPath = Path.GetFullPath(a[2]) });
                    Print(doc);
                    return 0;
                }
                if (sub == "commit" && a.Count > 2)
                {
                    Print(await Post("download/commit", new { planId = a[2], backup = true }));
                    return 0;
                }
                return Fail("用法: download prepare <新工程.mdb> | download commit <planId>");
            }

            case "versions": Print(await Get("project/versions")); return 0;

            case "logs":
            {
                if (a.Count > 1 && a[1].ToLowerInvariant() is "follow" or "-f")
                {
                    await FollowLogs();
                    return 0;
                }
                int max = a.Count > 1 ? int.Parse(a[1]) : 50;
                var doc = await Get($"logs?max={max}");
                foreach (var e in doc.RootElement.EnumerateArray())
                    PrintLog(e);
                return 0;
            }

            default:
                PrintUsage();
                return Fail($"未知命令: {cmd}");
        }
    }

    // ---------------- HTTP 基础 ----------------
    private static async Task<JsonDocument> Get(string path) => await Parse(await _http.GetAsync(path));

    private static async Task<JsonDocument> Post(string path, object? body = null)
        => await Parse(await _http.PostAsync(path, ToContent(body)));

    private static async Task<JsonDocument> Put(string path, object body)
        => await Parse(await _http.PutAsync(path, ToContent(body)));

    private static async Task<JsonDocument> Delete(string path)
        => await Parse(await _http.DeleteAsync(path));

    private static StringContent? ToContent(object? body) => body == null
        ? null
        : new StringContent(JsonSerializer.Serialize(body, JsonOpt), Encoding.UTF8, "application/json");

    private static async Task<JsonDocument> Parse(HttpResponseMessage resp)
    {
        string text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode)
        {
            string message = text;
            try
            {
                using var err = JsonDocument.Parse(text);
                if (err.RootElement.TryGetProperty("error", out var e))
                    message = e.GetString() ?? text;
            }
            catch
            {
                // 非 JSON 错误体，原样展示
            }
            throw new ApiException($"{(int)resp.StatusCode} {message}");
        }
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
    }

    private sealed class ApiException(string message) : Exception(message);

    // ---------------- 输出 ----------------
    private static void Print(JsonDocument doc)
        => Console.WriteLine(JsonSerializer.Serialize(doc.RootElement, JsonOpt));

    private static void PrintLog(JsonElement e)
    {
        string level = e.GetProperty("level").GetString() ?? "";
        var old = Console.ForegroundColor;
        Console.ForegroundColor = level switch
        {
            "Warn" => ConsoleColor.Yellow,
            "Error" => ConsoleColor.Red,
            _ => old,
        };
        Console.WriteLine($"{e.GetProperty("time").GetString()} [{level,-5}] [{e.GetProperty("source").GetString()}] {e.GetProperty("message").GetString()}");
        Console.ForegroundColor = old;
    }

    private static async Task FollowLogs()
    {
        Console.WriteLine("实时日志（SSE），Ctrl+C 退出");
        using var stream = await _http.GetStreamAsync("logs/stream");
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (await reader.ReadLineAsync() is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;
            try
            {
                using var doc = JsonDocument.Parse(line[5..].Trim());
                PrintLog(doc.RootElement);
            }
            catch
            {
                Console.WriteLine(line);
            }
        }
    }

    private static string FmtVersion(JsonElement e)
        => e.TryGetProperty("projectVersion", out var v) && v.ValueKind == JsonValueKind.Number ? $"v{v.GetInt32()}" : "-";

    private static string FmtBytes(long n) => n switch
    {
        >= 1 << 20 => $"{n / 1048576.0:F1}MB",
        >= 1 << 10 => $"{n / 1024.0:F1}KB",
        _ => $"{n}B",
    };

    private static int Fail(string msg)
    {
        Console.Error.WriteLine(msg);
        return 1;
    }

    private static void PrintUsage() => Console.WriteLine("""
        rwvdcs-cli - RWVDCS.Next 教练员站接口测试客户端

        全局参数:
          --url <地址>     服务地址（默认 http://localhost:8080，也可用环境变量 RWVDCS_URL）

        命令:
          status                         系统状态（工程/指纹/运行态）
          dpus                           DPU 列表（状态/周期/超限）
          load <工程.mdb>                装载工程（冷替换）
          start | pause | stop           连续运行 / 暂停 / 完全停止
          step [n]                       单步 n 个周期（默认 1）
          cycle <秒> [DPU]               设置扫描周期（省略 DPU 为全部统一设置）

          read <名字...>                 批量读值，名字形态 POINT / POINT.member / DPU$POINT.member
          write <名字=值 ...>            批量写值
          force <点名> <on|off> [值]     点强制 / 解除强制
          watch <名字...> [--ms 1000]    周期轮询显示值

          cond list                      工况列表
          cond save <名称> [备注]        保存工况（含工程库副本 + 全量镜像）
          cond load <名称>               加载工况（整体回切到该工程+状态）
          cond del <名称>                删除工况
          snap list|save|load|del        快照管理（同上，仅点/块状态，依赖当前工程）

          download prepare <新.mdb>      在线下装预检（返回差异报告 + planId）
          download commit <planId>       提交下装（保状态原子切换）
          versions                       工程版本档案

          logs [n]                       最近 n 条日志（默认 50）
          logs follow                    SSE 实时日志流

        示例:
          rwvdcs-cli status
          rwvdcs-cli read 1LAB20CP101XQ01 1LAB20CP101XQ01.isforced DPU1$P123.buffer
          rwvdcs-cli write 1LAB20CP101XQ01=3.14
          rwvdcs-cli cond save 满负荷工况 机组100%负荷
          rwvdcs-cli download prepare D:\proj\新版本.mdb
        """);
}
