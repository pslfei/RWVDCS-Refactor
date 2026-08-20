using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using RWVDCS.Engineering;
using RWVDCS.Runtime;

namespace RWVDCS.Api;

/// <summary>
/// Web 管理接口（Kestrel 自承载）：REST + SSE 日志流 + 静态 Web 界面。
/// 同一套 REST 即教练员站接口（工况/快照/运行控制），见 docs/教练员站接口。
/// </summary>
public sealed class ApiServer : IAsyncDisposable
{
    private const int LegacyMaxRequestBodyBytes = 4 * 1024 * 1024;
    private const int LegacyMaxBatchItems = 10_000;
    private const int LegacyMaxStringValueBytes = 64 * 1024;
    private static readonly SemaphoreSlim LegacyWriteAdmission = new(4, 4);

    private readonly WebApplication _app;
    private readonly RuntimeHost _host;
    private readonly RealtimeCompatGateway _compatGateway;

    public string Url { get; }

    public ApiServer(RuntimeHost host, int port)
    {
        _host = host;
        _compatGateway = new RealtimeCompatGateway(host);
        Url = $"http://localhost:{port}";

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://*:{port}");
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals);
        builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

        _app = builder.Build();

        _app.UseCors();
        UseLegacyRequestLimits(_app);

        _app.UseSwagger();
        _app.UseSwaggerUI(options =>
        {
            options.DocumentTitle = "RWVDCS API";
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "RWVDCS API v1");
            options.RoutePrefix = "swagger";
        });

        // 静态界面（wwwroot 随 Api 项目发布）
        string wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        if (Directory.Exists(wwwroot))
        {
            var provider = new PhysicalFileProvider(wwwroot);
            _app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = provider });
            _app.UseStaticFiles(new StaticFileOptions { FileProvider = provider });
        }

        MapEndpoints(_app);
    }

    public Task StartAsync()
    {
        _compatGateway.Start();
        return _app.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _compatGateway.DisposeAsync();
        await _app.StopAsync(TimeSpan.FromSeconds(2));
        await _app.DisposeAsync();
    }

    // =================================================================
    // 端点
    // =================================================================
    private void MapEndpoints(WebApplication app)
    {
        var api = app.MapGroup("/api");

        // 统一异常 → {error}
        api.AddEndpointFilter(async (ctx, next) =>
        {
            try
            {
                return await next(ctx);
            }
            catch (Exception ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: 400);
            }
        });

        // ---------------- 状态与总览 ----------------
        api.MapGet("/status", () =>
        {
            using var runtimeLease = TryGetRuntime(out var rt);
            var gcInfo = GC.GetGCMemoryInfo();
            return Results.Json(new
            {
                project = rt == null ? null : new
                {
                    mdbPath = _host.MdbPath,
                    fingerprint = _host.Fingerprint,
                    version = _host.ProjectVersion,
                    loadedAtUtc = _host.LoadedAtUtc,
                    dpuCount = rt.Dpus.Count,
                    pointCount = _host.BuildReport?.PointCount ?? 0,
                    intermediatePointCount = _host.BuildReport?.IntermediatePointCount ?? 0,
                    commandCount = _host.BuildReport?.CommandCount ?? 0,
                },
                run = new { state = _host.RunState.ToString() },
                monitor = new
                {
                    heapMb = gcInfo.HeapSizeBytes / 1024.0 / 1024.0,
                    workingSetMb = Environment.WorkingSet / 1024.0 / 1024.0,
                    gcPausePct = gcInfo.PauseTimePercentage,
                    gen0 = GC.CollectionCount(0),
                    gen1 = GC.CollectionCount(1),
                    gen2 = GC.CollectionCount(2),
                    threads = System.Diagnostics.Process.GetCurrentProcess().Threads.Count,
                    historyMb = (_host.History?.TotalBytes() ?? 0) / 1024.0 / 1024.0,
                },
                pendingDownload = _host.PendingPlan == null ? null : new
                {
                    planId = _host.PendingPlan.PlanId,
                    mdbPath = _host.PendingPlan.MdbPath,
                    preparedAtUtc = _host.PendingPlan.PreparedAtUtc,
                },
            });
        });

        // 管理端丰富 DPU 视图。/api/dpus 保留给 EmbeddedHttpApi 旧协议。
        api.MapGet("/runtime/dpus", () =>
        {
            using var runtimeLease = RequireRuntime(out var rt);
            var stats = _host.Scheduler?.Stats;
            return Results.Json(rt.Dpus.Select((d, i) => new
            {
                name = d.Name,
                controllerId = d.ControllerId,
                cycleSeconds = d.Cycle,
                cycleCount = d.CycleCount,
                slotCount = d.LocalSlots.Count,
                commandCount = d.Commands.Count,
                state = _host.RunState.ToString(),
                stats = stats == null || i >= stats.Count ? null : new
                {
                    count = stats[i].Count,
                    curMs = stats[i].CurrentMs,
                    avgMs = stats[i].AverageMs,
                    maxMs = stats[i].MaxMs,
                    p99Ms = stats[i].PercentileMs(99),
                    overruns = stats[i].Overruns,
                },
            }).ToArray());
        });

        // ---------------- 工程 ----------------
        api.MapPost("/project/load", (LoadProjectRequest req) =>
        {
            _host.LoadProject(req.MdbPath, req.FirstRun ?? true);
            return Results.Json(new { ok = true, fingerprint = _host.Fingerprint, version = _host.ProjectVersion });
        });

        api.MapGet("/project/versions", () => Results.Json(_host.Versions));

        // ---------------- 运行控制 ----------------
        api.MapPost("/run/start", () =>
        {
            _host.Start();
            return OkState();
        });
        api.MapPost("/run/pause", () =>
        {
            _host.Pause();
            return OkState();
        });
        api.MapPost("/run/stop", () =>
        {
            _host.Stop();
            return OkState();
        });
        api.MapPost("/run/step", (StepRequest? req) =>
        {
            _host.Step(Math.Clamp(req?.Cycles ?? 1, 1, 100_000));
            return OkState();
        });
        api.MapPut("/dpus/cycle", (SetCycleRequest req) =>
        {
            _host.SetCycle(null, req.Seconds);
            return Results.Json(new { ok = true });
        });
        api.MapPut("/dpus/{name}/cycle", (string name, SetCycleRequest req) =>
        {
            _host.SetCycle(name, req.Seconds);
            return Results.Json(new { ok = true });
        });

        // ---------------- 点/块检索 ----------------
        api.MapGet("/points", (string? q, string? kind, string? dpu, int page = 1, int pageSize = 50) =>
        {
            using var runtimeLease = RequireRuntime(out var rt);
            pageSize = Math.Clamp(pageSize, 1, 500);
            var items = new List<object>();
            int total = 0;

            foreach (var d in rt.Dpus)
            {
                if (dpu != null && !d.Name.Equals(dpu, StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var (name, slot) in d.LocalSlots)
                {
                    if (!slot.IsRealPoint)
                        continue;
                    if (kind != null && !slot.Kind.ToString().Equals(kind, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (q != null && !name.Contains(q, StringComparison.OrdinalIgnoreCase))
                        continue;

                    total++;
                    int skip = (page - 1) * pageSize;
                    if (total > skip && items.Count < pageSize)
                    {
                        string? description = _host.TryGetPointModel(d.Name, name, out var pointModel)
                            ? pointModel.Description
                            : null;

                        items.Add(new
                        {
                            dpu = d.Name,
                            name,
                            description,
                            kind = slot.Kind.ToString(),
                            value = slot.ReadBoxedBuffer(),
                            forced = IsPointForced(slot),
                        });
                    }
                }
            }

            return Results.Json(new { total, page, pageSize, items });
        });

        api.MapGet("/blocks", (string? q, string? fc, string? dpu, int page = 1, int pageSize = 50) =>
        {
            using var runtimeLease = RequireRuntime(out var rt);
            pageSize = Math.Clamp(pageSize, 1, 500);
            var items = new List<object>();
            int total = 0;

            foreach (var d in rt.Dpus)
            {
                if (dpu != null && !d.Name.Equals(dpu, StringComparison.OrdinalIgnoreCase))
                    continue;
                foreach (var cmd in d.Commands)
                {
                    if (fc != null && !cmd.FcName.Equals(fc, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (q != null && !cmd.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                        continue;

                    total++;
                    int skip = (page - 1) * pageSize;
                    if (total > skip && items.Count < pageSize)
                    {
                        string? description = _host.TryGetBlockModel(d.Name, cmd.Name, out var blockModel)
                            ? blockModel.Description
                            : null;

                        items.Add(new
                        {
                            dpu = d.Name,
                            name = cmd.Name,
                            description,
                            fc = cmd.FcName,
                            inputs = cmd.Inputs.Count,
                            outputs = cmd.Outputs.Count,
                            forced = cmd.ForceStates is { Count: > 0 } && cmd.ForceStates.Any(f => f.Value.IsForced),
                        });
                    }
                }
            }

            return Results.Json(new { total, page, pageSize, items });
        });

        api.MapGet("/fcs", () =>
        {
            using var runtimeLease = RequireRuntime(out var rt);
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in rt.Dpus)
            foreach (var cmd in d.Commands)
                counts[cmd.FcName] = counts.GetValueOrDefault(cmd.FcName) + 1;
            return Results.Json(counts.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => new { fc = kv.Key, count = kv.Value }));
        });

        // ---------------- 原版 EmbeddedHttpApi 兼容接口 ----------------
        api.MapGet("/diagnostics", () =>
        {
            using var runtimeLease = TryGetRuntime(out var rt);
            using var process = System.Diagnostics.Process.GetCurrentProcess();
            var stats = _host.Scheduler?.Stats;
            var dpus = rt == null
                ? Array.Empty<object>()
                : rt.Dpus.Select((d, i) => (object)new
                {
                    name = d.Name,
                    cycleCount = d.CycleCount,
                    currentCycleMilliseconds = stats != null && i < stats.Count
                        ? (long)Math.Round(stats[i].CurrentMs, MidpointRounding.AwayFromZero)
                        : 0L,
                    lastCompletedCycleUtc = d.LastCompletedCycleUtc,
                    iomapPending = 0,
                }).ToArray();
            return Results.Json(new
            {
                pid = process.Id,
                processStartTime = process.StartTime,
                uptimeSeconds = (DateTime.Now - process.StartTime).TotalSeconds,
                state = _host.RunState.ToString(),
                modelGeneration = (long)_compatGateway.Values.RuntimeGeneration,
                privateBytes = process.PrivateMemorySize64,
                virtualBytes = process.VirtualMemorySize64,
                workingSetBytes = process.WorkingSet64,
                gcHeapBytes = GC.GetTotalMemory(false),
                gen0Collections = GC.CollectionCount(0),
                gen1Collections = GC.CollectionCount(1),
                gen2Collections = GC.CollectionCount(2),
                dpuHeartbeat = rt?.Dpus.Count > 0
                    ? rt.Dpus.Max(d => d.LastCompletedCycleUtc.Ticks)
                    : 0L,
                iomapPending = 0,
                iomapRejectedBackpressure = 0L,
                iomapExpired = 0L,
                iomapApplyFailed = 0L,
                pointValueCacheCount = _compatGateway.Values.BindingCount,
                writableRouteCount = _compatGateway.Values.WritableBindingCount,
                dpus,
            });
        });

        api.MapGet("/dpus", () =>
        {
            using var runtimeLease = TryGetRuntime(out var rt);
            string[] dpus = rt?.Dpus.Select(d => d.Name).ToArray() ?? [];
            return Results.Json(new { count = dpus.Length, dpus });
        });

        api.MapGet("/dpu/blocks", (string? name) =>
        {
            using var runtimeLease = RequireRuntime(out var rt);
            var dpu = rt.FindDpu(name ?? "");
            if (dpu == null)
                return Results.Json(new { dpu = name, count = 0, blocks = Array.Empty<object>() });

            var blocks = dpu.Commands.Select(cmd => new
            {
                name = cmd.Name,
                fcName = cmd.FcName,
            }).ToArray();
            return Results.Json(new { dpu = name, count = blocks.Length, blocks });
        });

        api.MapGet("/dpu/blocks/details", (string? dpu) =>
        {
            using var runtimeLease = RequireRuntime(out var rt);
            var target = rt.FindDpu(dpu ?? "");
            if (target == null)
                return Results.Json(new { dpu, count = 0, blocks = Array.Empty<object>() });

            var blocks = target.Commands.Select(cmd => new
            {
                name = cmd.Name,
                pins = BuildLegacyBlockPinValues(cmd),
            }).ToArray();
            // 原版成功时直接返回数组，空 DPU 时才返回包装对象；此处保持原协议。
            return Results.Json(blocks);
        });

        api.MapGet("/dpu/points", (string? name) =>
        {
            using var runtimeLease = RequireRuntime(out var rt);
            var dpu = rt.FindDpu(name ?? "");
            if (dpu == null)
                return Results.Json(new { dpu = name, count = 0, points = Array.Empty<object>() });

            var points = dpu.LocalSlots
                .Where(kv => kv.Value.IsRealPoint)
                .Select(kv => new
                {
                    name = kv.Key,
                    type = kv.Value.Kind.ToString(),
                    extra = (string[]?)null,
                })
                .ToArray();
            return Results.Json(new { dpu = name, count = points.Length, points });
        });

        api.MapGet("/dpu/block", (string? dpu, string? block) =>
        {
            using var runtimeLease = RequireRuntime(out var rt);
            if (!TryFindBlock(rt, dpu, block, out var d, out var cmd))
                return Results.Json(new { dpu, block, count = 0, pins = Array.Empty<object>() });

            var pins = BuildCompatPins(d, cmd, includePointName: false);
            return Results.Json(new { dpu, block, count = pins.Count, pins });
        });

        api.MapGet("/dpu/block/pins", (string? dpu, string? name) =>
        {
            using var runtimeLease = RequireRuntime(out var rt);
            if (!TryFindBlock(rt, dpu, name, out var d, out var cmd))
                return Results.Json(new { dpu, block = name, count = 0, pins = Array.Empty<object>() });

            var pins = BuildCompatPins(d, cmd, includePointName: true);
            return Results.Json(new { dpu, block = name, count = pins.Count, pins });
        });

        api.MapGet("/dpu/block/full", (string? dpu, string? block) =>
        {
            using var runtimeLease = RequireRuntime(out var rt);
            if (!TryFindBlock(rt, dpu, block, out var targetDpu, out var cmd))
                return Results.Json(new { dpu, block, count = 0, pins = Array.Empty<object>() });

            var pins = BuildCompatPinRows(targetDpu, cmd).Select(row =>
            {
                object? point = null;
                if (!string.IsNullOrEmpty(row.PointName))
                {
                    string originalName = row.PointName;
                    string cleanName = originalName.Split(':')[0];
                    var found = FindPoint(rt, cleanName);
                    point = found == null ? null : new
                    {
                        name = cleanName,
                        originalName = originalName != cleanName ? originalName : null,
                        dpu = found.Value.Dpu.Name,
                        members = BuildLegacyPointMembers(found.Value.Dpu, cleanName, found.Value.Slot, searchProjection: false),
                    };
                }
                return new { pin = row.Pin, point };
            }).ToArray();
            return Results.Json(new { dpu, block, count = pins.Length, pins });
        });

        api.MapGet("/dpu/block/pin/point", (string? dpu, string? block, string? pin) =>
        {
            using var runtimeLease = RequireRuntime(out var rt);
            string? pointName = TryFindBlock(rt, dpu, block, out _, out var cmd)
                ? FindCompatPinPointName(cmd, pin ?? "")
                : null;
            return Results.Json(new { dpu, block, pin, pointName });
        });

        api.MapPost("/dpu/pins/values", (CompatDpuPinRequest? request) =>
        {
            if (request == null || request.PinPaths == null || request.PinPaths.Count == 0)
                return Results.Json(new { error = "请求体不能为空，需传入包含 Dpu 和 PinPaths 数组的对象" });

            using var runtimeLease = RequireRuntime(out var rt);
            var values = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (string path in request.PinPaths)
            {
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                int dot = path.LastIndexOf('.');
                if (dot < 0)
                    continue;

                string blockName = path[..dot];
                string pinName = path[(dot + 1)..];
                values[path] = TryFindBlock(rt, request.Dpu, blockName, out _, out var cmd)
                    ? FormatCompatString(ReadBlockPinValue(cmd, pinName))
                    : null;
            }
            return Results.Json(values);
        });

        api.MapPost("/point/SetVariables", (CompatSetVariablesRequest? request) =>
        {
            if (request == null)
                return Results.Json(new { error = "请求体不能为空" });
            if (request.Items == null || request.Items.Count == 0)
                return Results.Json(new { error = "items 不能为空" });
            if (request.Items.Count > LegacyMaxBatchItems)
                return Results.Json(new { error = "单批最多 10,000 项" }, statusCode: StatusCodes.Status413PayloadTooLarge);

            using var runtimeLease = RequireRuntime(out _);
            string clientInfo = string.IsNullOrWhiteSpace(request.ClientInfo) ? "HttpApi" : request.ClientInfo.Trim();
            var results = new List<CompatSetVariablesItemResult>(request.Items.Count);
            var names = new string[request.Items.Count];
            var values = new object?[request.Items.Count];

            for (int i = 0; i < request.Items.Count; i++)
            {
                var item = request.Items[i];
                string? original = item?.PointName?.Trim();
                string? pointName = string.IsNullOrEmpty(original)
                    ? original
                    : StripPrefixAndSuffix(original, "DCS01_", ".Value");
                var result = new CompatSetVariablesItemResult
                {
                    Index = i,
                    OriginalPointName = original,
                    PointName = pointName,
                    Success = false,
                };
                results.Add(result);

                if (item == null)
                {
                    result.Error = "第 " + i + " 项不能为空";
                    continue;
                }
                if (string.IsNullOrWhiteSpace(pointName))
                {
                    result.Error = "pointName 不能为空";
                    continue;
                }
                if (!TryGetJsonValue(item.Value, out object? value, out string? error))
                {
                    result.Error = error;
                    continue;
                }
                if (value is string text && Encoding.UTF8.GetByteCount(text) > LegacyMaxStringValueBytes)
                {
                    result.Error = "单个字符串值超过 64 KiB";
                    continue;
                }

                result.Value = value;
                names[i] = pointName;
                values[i] = value;
            }

            bool[] writeResults = _compatGateway.Values.WriteByNames(names, values, clientInfo);
            int successCount = 0;
            for (int i = 0; i < results.Count && i < writeResults.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(names[i]) || values[i] == null)
                    continue;
                results[i].Success = writeResults[i];
                if (writeResults[i])
                    successCount++;
                else if (string.IsNullOrEmpty(results[i].Error))
                    results[i].Error = "写入失败";
            }

            return Results.Json(new
            {
                clientInfo,
                count = request.Items.Count,
                successCount,
                failCount = request.Items.Count - successCount,
                results,
            });
        });

        api.MapPost("/point/SetVariables2", (CompatSetVariables2Request? request) =>
        {
            if (request == null)
                return Results.Json(new { error = "请求体不能为空" });
            if (request.Items == null || request.Items.Count == 0)
                return Results.Json(new { error = "items 不能为空" });
            if (request.Items.Count > LegacyMaxBatchItems)
                return Results.Json(new { error = "单批最多 10,000 项" }, statusCode: StatusCodes.Status413PayloadTooLarge);

            string clientInfo = string.IsNullOrWhiteSpace(request.ClientInfo) ? "HttpApi" : request.ClientInfo.Trim();
            int count = request.Items.Count;
            var fsids = new long[count];
            var values = new object?[count];
            var results = new CompatSetVariables2ItemResult[count];

            for (int i = 0; i < count; i++)
            {
                CompatSetVariables2ItemRequest? item = request.Items[i];
                var result = new CompatSetVariables2ItemResult
                {
                    Index = i,
                    Fsid = item?.Fsid ?? 0,
                };
                results[i] = result;
                if (item == null)
                {
                    result.Error = "第 " + i + " 项不能为空";
                    continue;
                }
                if (item.Fsid <= 0)
                {
                    result.Error = "fsid 必须大于 0";
                    continue;
                }
                if (!TryGetJsonValue(item.Value, out object? value, out string? error))
                {
                    result.Error = error;
                    continue;
                }
                if (value is string text && Encoding.UTF8.GetByteCount(text) > LegacyMaxStringValueBytes)
                {
                    result.Error = "单个字符串值超过 64 KiB";
                    continue;
                }
                fsids[i] = item.Fsid;
                values[i] = value;
                result.Value = value;
            }

            bool[] writeResults = _compatGateway.Values.WriteByHandles(fsids, values, clientInfo);
            int successCount = 0;
            for (int i = 0; i < count && i < writeResults.Length; i++)
            {
                if (fsids[i] <= 0)
                    continue;
                results[i].Success = writeResults[i];
                if (writeResults[i])
                    successCount++;
                else if (string.IsNullOrEmpty(results[i].Error))
                    results[i].Error = "写入失败";
            }

            return Results.Json(new
            {
                clientInfo,
                count,
                successCount,
                failCount = count - successCount,
                results,
            });
        });

        api.MapPost("/point/SubscribeBatch", (CompatSubscribeBatchRequest? request) =>
        {
            if (request == null || request.DpuNames == null || request.Names == null || request.Members == null)
                return Results.Json(Array.Empty<long>());

            int count = request.Names.Length;
            if (count == 0 || request.DpuNames.Length != count || request.Members.Length != count)
                return Results.Json(Array.Empty<long>());

            using var runtimeLease = RequireRuntime(out _);
            var canonicalNames = new string[count];
            var nullNames = new bool[count];
            for (int i = 0; i < count; i++)
            {
                string? name = request.Names[i];
                if (name == null)
                {
                    nullNames[i] = true;
                    canonicalNames[i] = "";
                    continue;
                }
                canonicalNames[i] = BuildLegacySubscriptionName(request.DpuNames[i], name, request.Members[i]);
            }

            RealtimeSubscribeResult[] subscriptions = _compatGateway.Values.SubscribeByNames(canonicalNames);
            var fsids = new long[count];
            for (int i = 0; i < count; i++)
                fsids[i] = nullNames[i] ? 0 : subscriptions[i].Found ? subscriptions[i].Handle : -1;
            return Results.Json(fsids);
        });

        api.MapGet("/dpu/point", (string? dpu, string? point) =>
        {
            using var runtimeLease = RequireRuntime(out var rt);
            var target = rt.FindDpu(dpu ?? "");
            if (target == null || string.IsNullOrEmpty(point))
                return Results.Json(new { dpu, point, count = 0, members = Array.Empty<object>() });

            object[] members;
            if (target.LocalSlots.TryGetValue(point, out var slot) && slot.IsRealPoint)
                members = BuildLegacyPointMembers(target, point, slot, searchProjection: false);
            else if (target.FindCommand(point) is { } command)
                members = BuildLegacyBlockMembers(target, command);
            else
                members = [];
            return Results.Json(new { dpu, point, count = members.Length, members });
        });

        api.MapPost("/point/GetPointValues", (List<string>? pointNames) =>
        {
            if (pointNames == null || pointNames.Count == 0)
                return Results.Json(new { error = "请求体不能为空，需传入测点名称的数组" });

            using var runtimeLease = RequireRuntime(out var rt);
            var result = new Dictionary<string, string?>(pointNames.Count, StringComparer.Ordinal);
            foreach (string raw in pointNames)
            {
                if (!TryParseCompatPointValueKey(raw, out string pointName, out string field, out string echoKey))
                    continue;

                result[echoKey] = field.ToLowerInvariant() switch
                {
                    "value" => FormatCompatString(ReadCompatPointValue(rt, pointName)),
                    "curoverstate" => FormatCompatString(ReadCompatPointField(rt, pointName, nameof(LA.CurOverState))),
                    "dataquality" => "0",
                    "eu" or "unit" => FindPointModel(pointName)?.Unit,
                    "desc" or "description" => FindPointModel(pointName)?.Description,
                    "minad" => FormatCompatString(FindPointModel(pointName)?.MinValue),
                    "maxad" => FormatCompatString(FindPointModel(pointName)?.MaxValue),
                    _ => null,
                };
            }
            return Results.Json(result);
        });

        api.MapPost("/point/GetPointValues2", (List<string>? pointNames) =>
        {
            if (pointNames == null || pointNames.Count == 0)
                return Results.Json(new { error = "请求体不能为空，需传入测点名称的数组" });

            int count = pointNames.Count;
            var cleanNames = new string[count];
            var subscriptionNames = new string[count];
            for (int i = 0; i < count; i++)
            {
                cleanNames[i] = StripPrefixAndSuffix(pointNames[i], "DCS01_", ".Value");
                subscriptionNames[i] = cleanNames[i] + ".buffer";
            }

            RealtimeSubscribeResult[] subscriptions = _compatGateway.Values.SubscribeByNames(subscriptionNames);
            long[] handles = subscriptions.Select(s => s.Found ? s.Handle : -1).ToArray();
            var values = _compatGateway.Values.Read(handles);
            var result = new Dictionary<string, string?>(count, StringComparer.Ordinal);
            int n = Math.Min(count, values.Length);
            for (int i = 0; i < n; i++)
            {
                if (string.IsNullOrEmpty(cleanNames[i]))
                    continue;
                result[cleanNames[i] + ".Value"] = handles[i] <= 0
                    ? null
                    : FormatCompatString(values[i].ToObject());
            }
            return Results.Json(result);
        });

        api.MapGet("/point/search", (string? name) =>
        {
            if (string.IsNullOrEmpty(name))
                return Results.Json(new { error = "参数 name 不能为空" });

            using var runtimeLease = RequireRuntime(out var rt);
            var found = FindPoint(rt, name);
            if (found == null)
                return Results.Json(new { point = name, dpu = (string?)null, count = 0, members = Array.Empty<object>() });

            object[] members = BuildLegacyPointMembers(found.Value.Dpu, name, found.Value.Slot, searchProjection: true);
            return Results.Json(new { point = name, dpu = found.Value.Dpu.Name, count = members.Length, members });
        });

        api.MapGet("/value", (string? names) =>
        {
            if (string.IsNullOrEmpty(names))
                return Results.Json(new { error = "参数 names 不能为空，格式: dpu.pointName 用逗号分隔" });

            using var runtimeLease = RequireRuntime(out var rt);
            string[] requested = names.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var values = new List<object>(requested.Length);
            foreach (string fullName in requested)
            {
                int dot = fullName.IndexOf('.');
                if (dot < 0)
                {
                    var found = FindPoint(rt, fullName);
                    if (found != null)
                    {
                        values.Add(new
                        {
                            name = fullName,
                            members = BuildLegacyPointMembers(found.Value.Dpu, fullName, found.Value.Slot,
                                searchProjection: false, includeEngineeringMetadata: true),
                        });
                    }
                    else if (TryFindBlock(rt, null, fullName, out var foundDpu, out var foundBlock))
                    {
                        values.Add(new { name = fullName, members = BuildLegacyBlockMembers(foundDpu, foundBlock) });
                    }
                    else
                    {
                        values.Add(new { name = fullName, value = (object?)null, error = "未找到" });
                    }
                    continue;
                }

                string dpuName = fullName[..dot];
                string pointName = fullName[(dot + 1)..];
                DpuRuntime? dpu = rt.FindDpu(dpuName);
                if (dpu != null && dpu.LocalSlots.TryGetValue(pointName, out var slot) && slot.IsRealPoint)
                {
                    values.Add(new
                    {
                        name = fullName,
                        dpu = dpuName,
                        members = BuildLegacyPointMembers(dpu, pointName, slot,
                            searchProjection: false, includeEngineeringMetadata: true),
                    });
                }
                else
                {
                    BlockCommand? command = dpu?.FindCommand(pointName);
                    if (dpu != null && command != null)
                        values.Add(new { name = fullName, dpu = dpuName, members = BuildLegacyBlockMembers(dpu, command) });
                    else
                        values.Add(new { name = fullName, value = (object?)null, error = "未找到" });
                }
            }
            return Results.Json(new { count = values.Count, values });
        });

        api.MapPost("/point/setvalue", (CompatSetValueRequest? request) =>
            SetLegacyPointValue(request, iomapOwned: false, useIomapClientInfo: false));

        api.MapPost("/point/setvaluetest", (CompatSetValueRequest? request) =>
            SetLegacyPointValue(request, iomapOwned: true, useIomapClientInfo: false));

        api.MapPost("/point/setvaluetest2", (CompatSetValueRequest? request) =>
            SetLegacyPointValue(request, iomapOwned: true, useIomapClientInfo: true));

        api.MapPost("/pin/force", (CompatPinForceRequest? request) =>
        {
            if (request == null)
                return Results.Json(new { error = "请求体不能为空" });
            if (string.IsNullOrEmpty(request.Dpu) || string.IsNullOrEmpty(request.Block) || string.IsNullOrEmpty(request.Pin))
                return Results.Json(new { error = "参数 dpu, block, pin 不能为空" });

            using var runtimeLease = RequireRuntime(out var rt);
            bool ok = false;
            object? forceValue = TryGetJsonValue(request.Value, out var parsed, out _) ? parsed : null;
            if (TryFindBlock(rt, request.Dpu, request.Block, out _, out var cmd))
            {
                try
                {
                    object? value = request.IsForce && forceValue != null
                        ? TryParseCompatPinValue(cmd, request.Pin, forceValue)
                        : request.IsForce ? null : ReadBlockPinValue(cmd, request.Pin) ?? 0;
                    cmd.SetPinForce(request.Pin, request.IsForce, value!);
                    ok = true;
                }
                catch
                {
                    ok = false;
                }
            }

            return Results.Json(new
            {
                dpu = request.Dpu,
                block = request.Block,
                pin = request.Pin,
                isForce = request.IsForce,
                forceValue,
                success = ok,
            });
        });

        // ---------------- 点详情（PointInfo）----------------
        api.MapGet("/point/{name}", (string name) =>
        {
            using var runtimeLease = RequireRuntime(out var rt);
            var (dpu, slot) = FindPoint(rt, name) ?? throw new InvalidOperationException($"点不存在：{name}");
            return Results.Json(new
            {
                dpu = dpu.Name,
                name,
                kind = slot.Kind.ToString(),
                value = slot.ReadBoxedBuffer(),
                fields = PointFieldAccess.ReadAll(slot).Select(f => new { f.Name, f.Type, f.Value }),
                producers = _host.Xref!.ProducersOf(name),
                consumers = _host.Xref!.ConsumersOf(name),
            });
        });

        api.MapPut("/point/{name}/value", (string name, SetValueRequest req) =>
        {
            using var runtimeLease = RequireRuntime(out var rt);
            var (_, slot) = FindPoint(rt, name) ?? throw new InvalidOperationException($"点不存在：{name}");
            slot.WriteBoxedBuffer(ParsePointValue(slot.Kind, req.Value));
            _host.Log.Info("写值", $"点 {name} <= {req.Value}");
            return Results.Json(new { ok = true, value = slot.ReadBoxedBuffer() });
        });

        api.MapPut("/point/{name}/field", (string name, SetFieldRequest req) =>
        {
            using var runtimeLease = RequireRuntime(out var rt);
            var (_, slot) = FindPoint(rt, name) ?? throw new InvalidOperationException($"点不存在：{name}");
            if (!PointFieldAccess.Write(slot, req.Field, req.Value))
                throw new InvalidOperationException($"字段写入失败：{name}.{req.Field}");
            _host.Log.Info("写值", $"点 {name}.{req.Field} <= {req.Value}");
            return Results.Json(new { ok = true, fields = PointFieldAccess.ReadAll(slot).Select(f => new { f.Name, f.Type, f.Value }) });
        });

        api.MapPost("/point/{name}/force", (string name, ForceRequest req) =>
        {
            using var runtimeLease = RequireRuntime(out var rt);
            var (_, slot) = FindPoint(rt, name) ?? throw new InvalidOperationException($"点不存在：{name}");
            if (!PointFieldAccess.SetForce(slot, req.Forced, req.Value))
                throw new InvalidOperationException($"点强制失败：{name}");
            _host.Log.Info("强制", req.Forced ? $"点 {name} 强制 = {req.Value}" : $"点 {name} 解除强制");
            return Results.Json(new { ok = true, value = slot.ReadBoxedBuffer() });
        });

        // ---------------- 块详情（PointInfo 的块视图）----------------
        api.MapGet("/block/{dpu}/{name}", (string dpu, string name) =>
        {
            using var runtimeLease = RequireRuntime(out var rt);
            var (d, cmd) = FindBlock(rt, dpu, name);
            return Results.Json(BuildBlockDetail(d, cmd));
        });

        // 跨 DPU 找块（列表页跳转用）
        api.MapGet("/blockfind/{name}", (string name) =>
        {
            using var runtimeLease = RequireRuntime(out var rt);
            foreach (var d in rt.Dpus)
            {
                var cmd = d.FindCommand(name);
                if (cmd != null)
                    return Results.Json(new { dpu = d.Name, name = cmd.Name });
            }
            throw new InvalidOperationException($"块不存在：{name}");
        });

        api.MapPost("/block/{dpu}/{name}/force", (string dpu, string name, PinForceRequest req) =>
        {
            using var runtimeLease = RequireRuntime(out var rt);
            var (_, cmd) = FindBlock(rt, dpu, name);
            object forceValue = ParseForPin(cmd, req.Pin, req.Value);
            cmd.SetPinForce(req.Pin, req.Forced, forceValue);
            _host.Log.Info("强制", req.Forced
                ? $"块 {dpu}/{name} 管脚 {req.Pin} 强制 = {req.Value}"
                : $"块 {dpu}/{name} 管脚 {req.Pin} 解除强制");
            return Results.Json(new { ok = true });
        });

        api.MapPut("/block/{dpu}/{name}/field", (string dpu, string name, SetFieldRequest req) =>
        {
            using var runtimeLease = RequireRuntime(out var rt);
            var (d, cmd) = FindBlock(rt, dpu, name);
            object value = ParseForField(cmd, req.Field, req.Value);
            if (!cmd.SetField(req.Field, value))
                throw new InvalidOperationException($"字段写入失败：{name}.{req.Field}");
            _host.Log.Info("写值", $"块 {dpu}/{name}.{req.Field} <= {req.Value}");
            return Results.Json(BuildBlockDetail(d, cmd));
        });

        // ---------------- 批量读写（Remoting 适配器/教练员站高频路径） ----------------
        // 名字形态与老系统订阅名一致：POINT、POINT.member、DPU$POINT.member（member 缺省 = buffer）
        api.MapPost("/values/read", (BatchReadRequest req) =>
        {
            var values = _compatGateway.Values.ReadByNames(req.Names);
            return Results.Json(new { values });
        });

        api.MapPost("/values/write", (BatchWriteRequest req) =>
        {
            var items = req.Items ?? [];
            var names = new string[items.Length];
            var values = new object?[items.Length];
            var iomapOwned = new bool[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                var item = items[i];
                if (item == null)
                    continue;
                names[i] = item.Name;
                values[i] = item.Value;
                iomapOwned[i] = item.IomapOwned;
            }
            var results = _compatGateway.Values.WriteByNames(names, values, req.ClientInfo, iomapOwned);
            return Results.Json(new { results });
        });

        api.MapPost("/values/iomap/mark", (IomapMarkRequest req) =>
        {
            using var runtimeLease = RequireRuntime(out var rt);
            var names = req.Names ?? [];
            var results = _compatGateway.Values.MarkIomapByNames(names);
            return Results.Json(new { results, ownedCount = rt.Iomap.OwnedCount });
        });

        // 批量类型描述：Remoting 适配器订阅时定型（保证回传老客户端的装箱类型一致）
        api.MapPost("/values/describe", (BatchReadRequest req) =>
        {
            using var runtimeLease = RequireRuntime(out var rt);
            var items = new object?[req.Names.Length];
            for (int i = 0; i < req.Names.Length; i++)
                items[i] = DescribeMember(rt, req.Names[i]);
            return Results.Json(new { items });
        });

        // ---------------- 交叉引用 ----------------
        api.MapGet("/xref/{point}", (string point) => Results.Json(new
        {
            point,
            producers = _host.Xref!.ProducersOf(point),
            consumers = _host.Xref!.ConsumersOf(point),
        }));

        // ---------------- 工况/快照 ----------------
        api.MapGet("/store/conditions", () => Results.Json(_host.Store.ListConditions()
            .Select(e => Enrich(e))));
        api.MapPost("/store/conditions", (SaveEntryRequest req) =>
        {
            _host.SaveCondition(req.Name, req.Comment);
            return Results.Json(new { ok = true });
        });
        api.MapPost("/store/conditions/{name}/load", (string name) =>
        {
            _host.LoadCondition(name);
            return Results.Json(new { ok = true, fingerprint = _host.Fingerprint, version = _host.ProjectVersion });
        });
        api.MapDelete("/store/conditions/{name}", (string name) =>
            Results.Json(new { ok = _host.Store.DeleteCondition(name) }));

        api.MapGet("/store/snapshots", () => Results.Json(_host.Store.ListSnapshots()
            .Select(e => Enrich(e))));
        api.MapPost("/store/snapshots", (SaveEntryRequest req) =>
        {
            var manifest = _host.SaveSnapshot(req.Name, req.Comment);
            return Results.Json(new
            {
                ok = true,
                changedSlots = manifest.Dpus.Sum(d => d.ChangedSlots),
                totalSlots = manifest.Dpus.Sum(d => d.TotalSlots),
            });
        });
        api.MapPost("/store/snapshots/{name}/load", (string name) =>
        {
            var report = _host.LoadSnapshot(name);
            return Results.Json(new
            {
                ok = true,
                compatMode = report.CompatMode,
                summary = report.ToString(),
                pointsApplied = report.PointsApplied,
                pointsSkipped = report.PointsSkipped,
                blocksRawCopied = report.BlocksRawCopied,
                blocksFieldConverted = report.BlocksFieldConverted,
                blocksSkipped = report.BlocksSkipped,
                messages = report.Messages,
            });
        });
        api.MapDelete("/store/snapshots/{name}", (string name) =>
            Results.Json(new { ok = _host.Store.DeleteSnapshot(name) }));

        // ---------------- 在线下装 ----------------
        api.MapPost("/download/prepare", (PrepareDownloadRequest req) =>
        {
            var plan = _host.PrepareDownload(req.MdbPath);
            return Results.Json(new
            {
                planId = plan.PlanId,
                mdbPath = plan.MdbPath,
                oldFingerprint = plan.OldFingerprint,
                newFingerprint = plan.NewFingerprint,
                identical = plan.NewFingerprint == plan.OldFingerprint,
                summary = new
                {
                    pointsAdded = plan.Diff.PointsAdded,
                    pointsRemoved = plan.Diff.PointsRemoved,
                    pointsChanged = plan.Diff.PointsChanged,
                    blocksAdded = plan.Diff.BlocksAdded,
                    blocksRemoved = plan.Diff.BlocksRemoved,
                    blocksTypeChanged = plan.Diff.BlocksTypeChanged,
                    blocksWiringChanged = plan.Diff.BlocksWiringChanged,
                    blocksParamChanged = plan.Diff.BlocksParamChanged,
                    controllersAdded = plan.Diff.ControllersAdded,
                    controllersRemoved = plan.Diff.ControllersRemoved,
                    controllersChanged = plan.Diff.ControllersChanged,
                    destructive = plan.Diff.HasDestructiveChanges,
                },
                entries = plan.Diff.Entries.Take(500).Select(e => new
                {
                    kind = e.Kind.ToString(),
                    controller = e.Controller,
                    name = e.Name,
                    detail = e.Detail,
                    destructive = e.IsDestructive,
                }),
                entriesTruncated = plan.Diff.Entries.Count > 500,
                totalEntries = plan.Diff.Entries.Count,
                errors = plan.Errors,
            });
        });

        api.MapPost("/download/commit", (CommitDownloadRequest req) =>
        {
            var result = _host.CommitDownload(req.PlanId, req.Backup ?? true);
            return Results.Json(new
            {
                ok = result.Success,
                fingerprint = _host.Fingerprint,
                version = _host.ProjectVersion,
                pointsPreserved = result.PointsPreserved,
                pointsNew = result.PointsNew,
                pointsDropped = result.PointsDropped,
                pointsTypeChanged = result.PointsTypeChanged,
                blocksPreserved = result.BlocksPreserved,
                blocksNew = result.BlocksNew,
                blocksDropped = result.BlocksDropped,
                blocksTypeChanged = result.BlocksTypeChanged,
                fieldsTransferred = result.FieldsTransferred,
                forcesCarried = result.ForcesCarried,
                transferMs = result.TransferMs,
                messages = result.Messages,
            });
        });

        // ---------------- 热更 ----------------
        api.MapPost("/hotload", (HotloadRequest req) =>
        {
            var report = _host.HotLoad(req.Targets);
            return Results.Json(new
            {
                ok = report.Success,
                generation = report.Generation,
                fcNames = report.SwappedFcNames,
                commandsSwapped = report.CommandsSwapped,
                fieldsTransferred = report.FieldsTransferred,
                messages = report.Messages,
            });
        });

        // ---------------- 历史站 ----------------
        api.MapGet("/history/query", (string point, int max = 200) =>
        {
            using var runtimeLease = RequireRuntime(out var rt);
            var history = _host.History ?? throw new InvalidOperationException("历史站未启用（启动加 --history）");
            var (dpu, _) = FindPoint(rt, point) ?? throw new InvalidOperationException($"点不存在：{point}");
            history.Flush();
            string file = Path.Combine(history.SessionDirectory,
                string.Join("_", dpu.Name.Split(Path.GetInvalidFileNameChars())) + ".rwhist");
            var samples = HistoryRecorder.Query(file, point).ToList();
            return Results.Json(new
            {
                point,
                total = samples.Count,
                samples = samples.TakeLast(Math.Clamp(max, 1, 10000)).Select(s => new
                {
                    cycle = s.Cycle,
                    timeMs = s.UnixMs,
                    value = s.Value,
                }),
            });
        });

        // ---------------- 日志 ----------------
        api.MapGet("/logs", (long after = 0, int max = 500) => Results.Json(
            _host.Log.Tail(after, Math.Clamp(max, 1, 2000)).Select(ToLogDto)));

        api.MapGet("/logs/stream", async (HttpContext ctx) =>
        {
            ctx.Response.Headers.ContentType = "text/event-stream";
            ctx.Response.Headers.CacheControl = "no-cache";

            var channel = Channel.CreateUnbounded<LogEntry>();
            void OnLog(LogEntry e) => channel.Writer.TryWrite(e);
            _host.Log.Appended += OnLog;
            try
            {
                // 先回放最近 100 条
                foreach (var e in _host.Log.Tail(0, 100))
                    await WriteSse(ctx, e);
                await ctx.Response.Body.FlushAsync(ctx.RequestAborted);

                await foreach (var e in channel.Reader.ReadAllAsync(ctx.RequestAborted))
                {
                    await WriteSse(ctx, e);
                    await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _host.Log.Appended -= OnLog;
            }
        });
    }

    // =================================================================
    // 详情构建与解析辅助
    // =================================================================

    private object BuildBlockDetail(DpuRuntime dpu, BlockCommand cmd)
    {
        var schema = BlockStateSchema.For(cmd.Fc.GetType());
        var forceStates = cmd.ForceStates;
        BlockModel? blockModel = _host.TryGetBlockModel(dpu.Name, cmd.Name, out var metadata)
            ? metadata
            : null;

        string? PinDescription(string pinName) => blockModel?.FindPin(pinName)?.Description;

        var inputsByField = cmd.Inputs.ToLookup(b => b.Pin.Field.Name, StringComparer.Ordinal);
        var outputsByField = cmd.Outputs.ToLookup(b => b.Pin.Field.Name, StringComparer.Ordinal);

        var inputs = new List<object>();
        var outputs = new List<object>();
        var constants = new List<object>();
        var internals = new List<object>();

        foreach (var f in schema.Fields)
        {
            switch (f.PinType)
            {
                case PinTypes.Input:
                case PinTypes.IO:
                case PinTypes.Output:
                {
                    object? pinObj = f.Field.GetValue(cmd.Fc);
                    object? value = pinObj is IValuable v ? v.Value : pinObj;
                    bool pinForced = false;
                    object? pinForceValue = null;
                    if (pinObj is IPointOperation po)
                    {
                        pinForced = po.IsForced != 0;
                        pinForceValue = po.GetMemberValue("forcevalue");
                    }
                    bool uiForced = forceStates != null && forceStates.TryGetValue(f.Name, out var fs) && fs.IsForced;

                    if (f.PinType is PinTypes.Input or PinTypes.IO)
                    {
                        var binding = inputsByField[f.Name].FirstOrDefault();
                        inputs.Add(new
                        {
                            pin = f.Name,
                            description = PinDescription(f.Name),
                            type = f.Field.FieldType.Name,
                            value,
                            point = binding?.PointName,
                            reversed = binding?.Reversed ?? false,
                            dead = binding != null && binding.Source is not { IsRealPoint: true },
                            forced = pinForced || uiForced,
                            forceValue = pinForceValue,
                            // 交叉引用：该输入的源头（写连接点的输出管脚）
                            sources = binding == null ? [] : _host.Xref!.ProducersOf(binding.PointName),
                        });
                    }
                    if (f.PinType is PinTypes.Output or PinTypes.IO)
                    {
                        var bindings = outputsByField[f.Name].ToList();
                        outputs.Add(new
                        {
                            pin = f.Name,
                            description = PinDescription(f.Name),
                            type = f.Field.FieldType.Name,
                            value,
                            targets = bindings.Select(b => new
                            {
                                point = b.PointName,
                                reversed = b.Reversed,
                                dead = b.Target is not { IsRealPoint: true },
                                // 交叉引用：该输出目标点的全部使用方
                                consumers = _host.Xref!.ConsumersOf(b.PointName),
                            }),
                            forced = pinForced || uiForced,
                            forceValue = pinForceValue,
                        });
                    }
                    break;
                }

                case PinTypes.Constant:
                    constants.Add(new
                    {
                        name = f.Name,
                        description = PinDescription(f.Name),
                        type = FriendlyType(f),
                        value = FormatFieldValue(f, cmd.Fc),
                        writable = f.Kind == StateFieldKind.Unmanaged || f.Kind == StateFieldKind.FixedString,
                    });
                    break;

                case PinTypes.Internal:
                case PinTypes.None:
                case PinTypes.Cascaded:
                    internals.Add(new
                    {
                        name = f.Name,
                        description = PinDescription(f.Name),
                        type = FriendlyType(f),
                        value = FormatFieldValue(f, cmd.Fc),
                        writable = f.Kind == StateFieldKind.Unmanaged || f.Kind == StateFieldKind.FixedString,
                    });
                    break;
            }
        }

        return new
        {
            dpu = dpu.Name,
            name = cmd.Name,
            fc = cmd.FcName,
            description = blockModel?.Description,
            stateBytes = schema.ByteLength,
            inputs,
            outputs,
            constants,
            internals,
        };
    }

    private static string FriendlyType(BlockStateField f) => f.Kind switch
    {
        StateFieldKind.FixedString => "string",
        StateFieldKind.FixedArray => $"{f.Field.FieldType.GetElementType()!.Name}[{f.Capacity}]",
        _ => f.Field.FieldType.Name,
    };

    private static object? FormatFieldValue(BlockStateField f, Function fc)
    {
        object? val = f.Field.GetValue(fc);
        if (val == null)
            return null;
        if (val is IValuable v)
            return v.Value;
        if (val is Array arr)
        {
            var preview = new List<object?>();
            for (int i = 0; i < Math.Min(arr.Length, 16); i++)
                preview.Add(arr.GetValue(i));
            return new { length = arr.Length, preview };
        }
        if (val.GetType().IsEnum)
            return val.ToString();
        return val;
    }

    private static void UseLegacyRequestLimits(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            bool isPost = HttpMethods.IsPost(context.Request.Method);
            bool isLegacyPost = isPost && IsLegacyPostPath(context.Request.Path.Value);
            bool isLegacyWrite = isLegacyPost
                && (context.Request.Path.Value?.EndsWith("/api/point/SetVariables", StringComparison.OrdinalIgnoreCase) == true
                    || context.Request.Path.Value?.EndsWith("/api/point/SetVariables2", StringComparison.OrdinalIgnoreCase) == true);
            bool admitted = false;
            MemoryStream? bufferedBody = null;
            try
            {
                if (isLegacyPost && context.Request.ContentLength is > LegacyMaxRequestBodyBytes)
                {
                    await WriteLegacyError(context, StatusCodes.Status413PayloadTooLarge, "request body exceeds 4 MiB");
                    return;
                }

                if (isLegacyWrite)
                {
                    admitted = LegacyWriteAdmission.Wait(0);
                    if (!admitted)
                    {
                        await WriteLegacyError(context, StatusCodes.Status429TooManyRequests,
                            "too many concurrent write requests");
                        return;
                    }
                }

                if (isLegacyPost)
                {
                    int capacity = context.Request.ContentLength is > 0 and <= LegacyMaxRequestBodyBytes
                        ? (int)context.Request.ContentLength.Value
                        : 0;
                    bufferedBody = new MemoryStream(capacity);
                    byte[] chunk = new byte[8192];
                    int total = 0;
                    while (true)
                    {
                        int read = await context.Request.Body.ReadAsync(chunk.AsMemory(0, chunk.Length), context.RequestAborted);
                        if (read <= 0)
                            break;
                        total += read;
                        if (total > LegacyMaxRequestBodyBytes)
                        {
                            await WriteLegacyError(context, StatusCodes.Status413PayloadTooLarge,
                                "request body exceeds 4 MiB");
                            return;
                        }
                        await bufferedBody.WriteAsync(chunk.AsMemory(0, read), context.RequestAborted);
                    }
                    bufferedBody.Position = 0;
                    context.Request.Body = bufferedBody;
                }

                await next(context);
            }
            finally
            {
                if (admitted)
                    LegacyWriteAdmission.Release();
                if (bufferedBody != null)
                    await bufferedBody.DisposeAsync();
            }
        });
    }

    private static bool IsLegacyPostPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return false;
        return path.Equals("/api/dpu/pins/values", StringComparison.OrdinalIgnoreCase)
               || path.Equals("/api/point/SetVariables", StringComparison.OrdinalIgnoreCase)
               || path.Equals("/api/point/SetVariables2", StringComparison.OrdinalIgnoreCase)
               || path.Equals("/api/point/SubscribeBatch", StringComparison.OrdinalIgnoreCase)
               || path.Equals("/api/point/GetPointValues", StringComparison.OrdinalIgnoreCase)
               || path.Equals("/api/point/GetPointValues2", StringComparison.OrdinalIgnoreCase)
               || path.Equals("/api/point/setvalue", StringComparison.OrdinalIgnoreCase)
               || path.Equals("/api/point/setvaluetest", StringComparison.OrdinalIgnoreCase)
               || path.Equals("/api/point/setvaluetest2", StringComparison.OrdinalIgnoreCase)
               || path.Equals("/api/pin/force", StringComparison.OrdinalIgnoreCase);
    }

    private static Task WriteLegacyError(HttpContext context, int statusCode, string message)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsJsonAsync(new { error = message });
    }

    private static string BuildLegacySubscriptionName(string? dpu, string name, string? member)
    {
        string target = string.IsNullOrWhiteSpace(dpu) ? name : dpu + "$" + name;
        return string.IsNullOrEmpty(member) ? target : target + "." + member;
    }

    private long GetLegacyHandle(string name)
    {
        RealtimeSubscribeResult result = _compatGateway.Values.SubscribeByNames([name])[0];
        return result.Found ? result.Handle : -1;
    }

    private object[] BuildLegacyPointMembers(DpuRuntime dpu, string pointName, PointSlotRef slot,
        bool searchProjection, bool includeEngineeringMetadata = false)
    {
        if (searchProjection)
        {
            string[] names = ["buffer", "isforced", "forcevalue", "istrace"];
            var projected = new List<object>(names.Length);
            foreach (string member in names)
            {
                object? value = member.Equals("buffer", StringComparison.OrdinalIgnoreCase)
                    ? slot.ReadBoxedBuffer()
                    : PointFieldAccess.TryRead(slot, member, out object? fieldValue, out _) ? fieldValue : null;
                projected.Add(new
                {
                    name = member,
                    value = TrimNullIfString(value),
                    fsid = GetLegacyHandle(BuildLegacySubscriptionName(dpu.Name, pointName, member)),
                });
            }
            return projected.ToArray();
        }

        var members = PointFieldAccess.ReadAll(slot).Select(field => (object)new
        {
            name = field.Name,
            value = TrimNullIfString(field.Value),
            fsid = GetLegacyHandle(BuildLegacySubscriptionName(dpu.Name, pointName, field.Name)),
        }).ToList();

        if (includeEngineeringMetadata)
        {
            PointModel? point = _host.TryGetPointModel(dpu.Name, pointName, out var model) ? model : null;
            string? dpuNo = _host.TryGetControllerAddress(dpu.ControllerId, out string address) ? address : null;
            members.Add(BuildEngineeringMetadataMember("ID", point?.ID));
            members.Add(BuildEngineeringMetadataMember("LowAlarm1Priority", point?.LowAlarm1Priority));
            members.Add(BuildEngineeringMetadataMember("LowAlarm2Priority", point?.LowAlarm2Priority));
            members.Add(BuildEngineeringMetadataMember("LowAlarm3Priority", point?.LowAlarm3Priority));
            members.Add(BuildEngineeringMetadataMember("HighAlarm1Priority", point?.HighAlarm1Priority));
            members.Add(BuildEngineeringMetadataMember("HighAlarm2Priority", point?.HighAlarm2Priority));
            members.Add(BuildEngineeringMetadataMember("HighAlarm3Priority", point?.HighAlarm3Priority));
            members.Add(BuildEngineeringMetadataMember("dpuNO", dpuNo));
            members.Add(BuildEngineeringMetadataMember("LowAlarmLimit1Value", point?.LowAlarmLimit1Value));
            members.Add(BuildEngineeringMetadataMember("LowAlarmLimit2Value", point?.LowAlarmLimit2Value));
            members.Add(BuildEngineeringMetadataMember("LowAlarmLimit3Value", point?.LowAlarmLimit3Value));
            members.Add(BuildEngineeringMetadataMember("HighAlarmLimit1Value", point?.HighAlarmLimit1Value));
            members.Add(BuildEngineeringMetadataMember("HighAlarmLimit2Value", point?.HighAlarmLimit2Value));
            members.Add(BuildEngineeringMetadataMember("HighAlarmLimit3Value", point?.HighAlarmLimit3Value));
            members.Add(BuildEngineeringMetadataMember("Description", point?.Description));
            members.Add(BuildEngineeringMetadataMember("Unit", point?.Unit));
            members.Add(BuildEngineeringMetadataMember("DataType", point?.DataType));
        }

        return members.ToArray();
    }

    private static object BuildEngineeringMetadataMember(string name, object? value) => new
    {
        name,
        value,
        fsid = -1L,
    };

    private static object[] BuildLegacyBlockPinValues(BlockCommand cmd)
    {
        var schema = BlockStateSchema.For(cmd.Fc.GetType());
        return schema.Fields
            .Select(field => (object)new
            {
                name = field.Name,
                value = TrimNullIfString(ReadBlockPinValue(cmd, field.Name)),
            })
            .ToArray();
    }

    private object[] BuildLegacyBlockMembers(DpuRuntime dpu, BlockCommand cmd)
    {
        return BlockStateSchema.For(cmd.Fc.GetType()).Fields.Select(field => (object)new
        {
            name = field.Name,
            value = TrimNullIfString(ReadBlockPinValue(cmd, field.Name)),
            fsid = GetLegacyHandle(BuildLegacySubscriptionName(dpu.Name, cmd.Name, field.Name)),
        }).ToArray();
    }

    private List<CompatPinRow> BuildCompatPinRows(DpuRuntime dpu, BlockCommand cmd)
    {
        List<object> pins = BuildCompatPins(dpu, cmd, includePointName: false);
        string[] names = BlockStateSchema.For(cmd.Fc.GetType()).Fields
            .Select(field => field.Name)
            .ToArray();
        int count = Math.Min(pins.Count, names.Length);
        var rows = new List<CompatPinRow>(count);
        for (int i = 0; i < count; i++)
            rows.Add(new CompatPinRow(pins[i], FindCompatPinPointName(cmd, names[i])));
        return rows;
    }

    private IResult SetLegacyPointValue(CompatSetValueRequest? request, bool iomapOwned, bool useIomapClientInfo)
    {
        if (request == null)
            return Results.Json(new { error = "请求体不能为空，请传入 JSON 对象" });

        string? pointName = request.PointName?.Replace(".Value", "", StringComparison.Ordinal);
        string? text = request.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? null
            : request.Value.ToString();
        if (string.IsNullOrWhiteSpace(pointName) || string.IsNullOrEmpty(text))
            return Results.Json(new { error = "参数 PointName 或 Value 不能为空" });

        if (!float.TryParse(text, out float value))
            return Results.Json(new
            {
                pointName,
                success = false,
                error = "参数 Value 无法转换为数值类型",
            });

        string subscribedPoint = iomapOwned ? IomapOwnership.PointNamePrefix + pointName : pointName;
        RealtimeSubscribeResult subscription = _compatGateway.Values
            .SubscribeByNames([subscribedPoint + ".buffer"])[0];
        if (!subscription.Found || subscription.Handle < 0)
            return Results.Json(new { pointName, success = false, error = "订阅测点失败" });

        string? clientInfo = useIomapClientInfo ? IomapOwnership.ClientInfoPrefix : null;
        bool success = _compatGateway.Values.WriteByHandles([subscription.Handle], [value], clientInfo)[0];
        if (iomapOwned)
        {
            return Results.Json(new
            {
                pointName,
                fsid = subscription.Handle,
                value = text,
                iomapOwned = true,
                success,
            });
        }
        return Results.Json(new { pointName, fsid = subscription.Handle, value = text, success });
    }

    private static object? TryParseCompatPinValue(BlockCommand cmd, string pinName, object? value)
    {
        if (value == null)
            return null;
        FieldInfo? field = cmd.Fc.GetType().GetField(pinName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field == null)
            return value;
        return ParseForPin(cmd, pinName, FormatCompatString(value) ?? "");
    }

    private List<object> BuildCompatPins(DpuRuntime dpu, BlockCommand cmd, bool includePointName)
    {
        var result = new List<object>();
        var schema = BlockStateSchema.For(cmd.Fc.GetType());
        var forceStates = cmd.ForceStates;
        foreach (var f in schema.Fields)
        {
            object? raw = f.Field.GetValue(cmd.Fc);
            object? value = raw is IValuable valuable ? valuable.Value : raw;
            object? forceValue = raw is IPointOperation po ? po.GetMemberValue("forcevalue") : null;
            bool pinForce = raw is IPointOperation po2 && po2.IsForced != 0;
            bool uiForce = false;
            object? uiForceValue = null;
            if (forceStates != null && forceStates.TryGetValue(f.Name, out var fs))
            {
                uiForce = fs.IsForced;
                uiForceValue = fs.ForceValue;
            }
            if (uiForce)
                forceValue = uiForceValue;

            string? pointName = FindCompatPinPointName(cmd, f.Name);
            long fsid = GetLegacyHandle(BuildLegacySubscriptionName(dpu.Name, cmd.Name, f.Name));
            object? defaultValue = FindCompatPinDefault(dpu.Name, cmd.Name, f.Name);
            string? display = f.Field.GetCustomAttribute<PinDisplayAttribute>()?.Display;

            if (includePointName)
            {
                result.Add(new
                {
                    name = f.Name,
                    pintype = f.PinType.ToString(),
                    datatype = f.Field.FieldType.Name,
                    value = TrimNullIfString(value),
                    defaultvalue = TrimNullIfString(defaultValue),
                    minvalue = TrimNullIfString(raw is IPointOperation po3 ? po3.GetMemberValue("minvalue") : null),
                    maxvalue = TrimNullIfString(raw is IPointOperation po4 ? po4.GetMemberValue("maxvalue") : null),
                    fsid,
                    display,
                    isForce = pinForce || uiForce,
                    forceValue = TrimNullIfString(forceValue),
                    isTrace = raw is IPointOperation po5 && po5.IsTrace,
                    pointName,
                });
            }
            else
            {
                result.Add(new
                {
                    name = f.Name,
                    pintype = f.PinType.ToString(),
                    datatype = f.Field.FieldType.Name,
                    value = TrimNullIfString(value),
                    defaultvalue = TrimNullIfString(defaultValue),
                    minvalue = TrimNullIfString(raw is IPointOperation po3 ? po3.GetMemberValue("minvalue") : null),
                    maxvalue = TrimNullIfString(raw is IPointOperation po4 ? po4.GetMemberValue("maxvalue") : null),
                    fsid,
                    display,
                    isForce = pinForce || uiForce,
                    forceValue = TrimNullIfString(forceValue),
                    isTrace = raw is IPointOperation po5 && po5.IsTrace,
                    dpu = dpu.Name,
                    block = cmd.Name,
                });
            }
        }
        return result;
    }

    private object? FindCompatPinDefault(string dpuName, string blockName, string pinName)
    {
        var controller = _host.PristineModel?.Controllers
            .FirstOrDefault(c => c.Name.Equals(dpuName, StringComparison.OrdinalIgnoreCase));
        var block = controller?.Blocks
            .FirstOrDefault(b => b.Name.Equals(blockName, StringComparison.OrdinalIgnoreCase));
        return block?.Pins
            .FirstOrDefault(p => p.PinName.Equals(pinName, StringComparison.OrdinalIgnoreCase))
            ?.DefaultValue;
    }

    private static string? FindCompatPinPointName(BlockCommand cmd, string pinName)
    {
        foreach (var b in cmd.Inputs)
            if (b.Pin.Field.Name.Equals(pinName, StringComparison.OrdinalIgnoreCase))
                return b.PointName;
        foreach (var b in cmd.Outputs)
            if (b.Pin.Field.Name.Equals(pinName, StringComparison.OrdinalIgnoreCase))
                return b.PointName;
        return null;
    }

    private static object? ReadBlockPinValue(BlockCommand cmd, string pinName)
    {
        var fi = cmd.Fc.GetType().GetField(pinName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (fi == null)
            return null;
        object? raw = fi.GetValue(cmd.Fc);
        return raw is IValuable valuable ? valuable.Value : raw;
    }

    private object? ReadCompatPointValue(DcsRuntime rt, string pointName)
    {
        return TryFindCompatPointSlot(rt, pointName, out var slot)
            ? slot.ReadBoxedBuffer()
            : null;
    }

    private object? ReadCompatPointField(DcsRuntime rt, string pointName, string fieldName)
    {
        return TryFindCompatPointSlot(rt, pointName, out var slot)
               && PointFieldAccess.TryRead(slot, fieldName, out object? value, out _)
            ? value
            : null;
    }

    private bool TryFindCompatPointSlot(DcsRuntime rt, string pointName, out PointSlotRef slot)
    {
        if (rt.TryGetSlot(pointName, out slot) && slot.IsRealPoint)
            return true;

        var found = FindPoint(rt, pointName);
        if (found != null)
        {
            slot = found.Value.Slot;
            return true;
        }

        slot = default;
        return false;
    }

    private PointModel? FindPointModel(string pointName)
    {
        var model = _host.PristineModel;
        if (model == null)
            return null;
        foreach (var c in model.Controllers)
        foreach (var p in c.Points)
            if (p.Name.Equals(pointName, StringComparison.OrdinalIgnoreCase))
                return p;
        return null;
    }

    private static bool TryParseCompatPointValueKey(string? raw, out string pointName, out string field, out string echoKey)
    {
        pointName = "";
        field = "";
        echoKey = "";
        if (string.IsNullOrEmpty(raw))
            return false;

        string pointPart;
        int dot = raw.LastIndexOf('.');
        if (dot <= 0 || dot >= raw.Length - 1)
        {
            pointPart = raw;
            field = "Value";
            echoKey = raw + ".Value";
        }
        else
        {
            pointPart = raw[..dot];
            field = raw[(dot + 1)..];
            echoKey = raw;
        }

        pointName = StripPrefixAndSuffix(pointPart, "DCS01_", null);
        return pointName.Length > 0;
    }

    private static string StripPrefixAndSuffix(string? value, string? prefix, string? suffix)
    {
        if (string.IsNullOrEmpty(value))
            return "";

        int start = 0;
        int length = value.Length;
        if (!string.IsNullOrEmpty(prefix) && value.Length >= prefix.Length &&
            string.Compare(value, 0, prefix, 0, prefix.Length, StringComparison.OrdinalIgnoreCase) == 0)
        {
            start = prefix.Length;
            length -= prefix.Length;
        }

        if (!string.IsNullOrEmpty(suffix) && length >= suffix.Length &&
            string.Compare(value, start + length - suffix.Length, suffix, 0, suffix.Length, StringComparison.OrdinalIgnoreCase) == 0)
        {
            length -= suffix.Length;
        }

        return start == 0 && length == value.Length ? value : value.Substring(start, length);
    }

    private static object? TrimNullIfString(object? value)
    {
        if (value is not string s)
            return value;
        int idx = s.IndexOf('\0');
        return idx >= 0 ? s[..idx] : s;
    }

    private static string? FormatCompatString(object? value)
    {
        return value switch
        {
            null => null,
            float f => f.ToString("R", CultureInfo.InvariantCulture),
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString(),
        };
    }

    private static bool TryGetJsonValue(JsonElement value, out object? result, out string? error)
    {
        error = null;
        switch (value.ValueKind)
        {
            case JsonValueKind.Undefined:
            case JsonValueKind.Null:
                result = null;
                error = "value 不能为空";
                return false;
            case JsonValueKind.True:
                result = true;
                return true;
            case JsonValueKind.False:
                result = false;
                return true;
            case JsonValueKind.Number:
                if (value.TryGetInt64(out long l))
                    result = l;
                else
                    result = value.GetDouble();
                return true;
            case JsonValueKind.String:
                result = value.GetString();
                return true;
            default:
                result = null;
                error = "value 仅支持 JSON 基本类型";
                return false;
        }
    }

    private static bool TryFindBlock(DcsRuntime rt, string? dpuName, string? blockName,
        out DpuRuntime dpu, out BlockCommand cmd)
    {
        dpu = null!;
        cmd = null!;
        if (string.IsNullOrWhiteSpace(blockName))
            return false;

        if (!string.IsNullOrWhiteSpace(dpuName))
        {
            dpu = rt.FindDpu(dpuName)!;
            if (dpu == null)
                return false;
            cmd = dpu.FindCommand(blockName)!;
            return cmd != null;
        }

        foreach (var d in rt.Dpus)
        {
            var c = d.FindCommand(blockName);
            if (c != null)
            {
                dpu = d;
                cmd = c;
                return true;
            }
        }
        return false;
    }

    private RuntimeReadLease RequireRuntime(out DcsRuntime runtime)
    {
        RuntimeReadLease lease = _host.AcquireRuntimeLease();
        runtime = lease.Runtime;
        return lease;
    }

    private RuntimeReadLease? TryGetRuntime(out DcsRuntime? runtime)
    {
        RuntimeReadLease? lease = _host.TryAcquireRuntimeLease();
        runtime = lease?.Runtime;
        return lease;
    }

    private (DpuRuntime Dpu, PointSlotRef Slot)? FindPoint(DcsRuntime rt, string name)
    {
        foreach (var d in rt.Dpus)
        {
            if (d.LocalSlots.TryGetValue(name, out var slot) && slot.IsRealPoint)
                return (d, slot);
        }
        return null;
    }

    /// <summary>
    /// 解析老系统订阅名形态 [DPU$]NAME[.member] 并读取成员值。
    /// NAME 优先按点解析（member 缺省 = buffer），点不中则按块解析（member = 管脚/字段名）。
    /// 找不到返回 null（与老系统 GetValue 缺项行为一致）。
    /// </summary>
    private object? TryReadMember(DcsRuntime rt, string name)
    {
        if (TryResolveMember(rt, name, out var slot, out string member))
        {
            if (IsBufferMember(member))
                return slot.ReadBoxedBuffer();
            foreach (var f in PointFieldAccess.ReadAll(slot))
            {
                if (f.Name.Equals(member, StringComparison.OrdinalIgnoreCase))
                    return f.Value;
            }
            return null;
        }

        // 块管脚路径（老系统 rtd[块名] 命中块、BLOCK.PIN 读管脚的等价物）
        if (TryResolveBlockMember(rt, name, out var cmd, out string field) && field.Length > 0)
        {
            var fi = cmd.Fc.GetType().GetField(field,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (fi == null)
                return null;
            object? raw = fi.GetValue(cmd.Fc);
            return raw is IValuable v ? v.Value : raw;
        }
        return null;
    }

    /// <summary>按 [DPU$]NAME[.member] 写值；点不中则尝试块字段。返回是否成功。</summary>
    private bool TryWriteMember(DcsRuntime rt, string name, string value, string? clientInfo = null, bool iomapOwned = false)
    {
        if (TryResolveMember(rt, name, out var slot, out string member))
        {
            if (IsBufferMember(member))
            {
                try
                {
                    object parsed = ParsePointValue(slot.Kind, value);
                    slot.WriteBoxedBuffer(parsed);
                    if (iomapOwned || IomapOwnership.IsIomapClient(clientInfo))
                        rt.Iomap.SetOwnedValue(slot, parsed);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
            return PointFieldAccess.Write(slot, member, value);
        }

        if (TryResolveBlockMember(rt, name, out var cmd, out string field) && field.Length > 0)
        {
            try
            {
                return cmd.SetField(field, ParseForField(cmd, field, value));
            }
            catch
            {
                return false;
            }
        }
        return false;
    }

    /// <summary>
    /// 名字→类型描述（适配器订阅时定型）。返回匿名对象：
    /// found / target(point|block) / valueType（.NET 装箱类型名，与 /values/read 回读一致）/ writable。
    /// </summary>
    private object DescribeMember(DcsRuntime rt, string name)
    {
        if (TryResolveMember(rt, name, out var slot, out string member))
        {
            if (IsBufferMember(member))
            {
                // 与 ReadBoxedBuffer 的装箱类型对齐
                string vt = slot.Kind switch
                {
                    PointKind.LA => "Single",
                    PointKind.LD => "Boolean",
                    PointKind.LP => "UInt16",
                    PointKind.LP32 => "UInt32",
                    _ => "Object",
                };
                return new { found = true, target = "point", kind = slot.Kind.ToString(), valueType = vt, writable = true };
            }
            foreach (var f in PointFieldAccess.ReadAll(slot))
            {
                if (f.Name.Equals(member, StringComparison.OrdinalIgnoreCase))
                    return new { found = true, target = "point", kind = slot.Kind.ToString(), valueType = f.Type, writable = true };
            }
            return new { found = false, target = "point", kind = (string?)null, valueType = (string?)null, writable = false };
        }

        if (TryResolveBlockMember(rt, name, out var cmd, out string field) && field.Length > 0)
        {
            var fi = cmd.Fc.GetType().GetField(field,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (fi != null)
            {
                var t = fi.FieldType;
                string vt = t == typeof(Core.Types.LA) ? "Single"
                    : t == typeof(Core.Types.LD) ? "Boolean"
                    : t == typeof(Core.Types.LP) ? "UInt16"
                    : t == typeof(Core.Types.LP32) ? "UInt32"
                    : t.IsEnum ? "Int32"
                    : Type.GetTypeCode(t).ToString();
                return new { found = true, target = "block", kind = cmd.FcName, valueType = vt, writable = true };
            }
        }
        return new { found = false, target = (string?)null, kind = (string?)null, valueType = (string?)null, writable = false };
    }

    private static bool IsBufferMember(string member)
        => member.Length == 0
           || member.Equals("buffer", StringComparison.OrdinalIgnoreCase)
           || member.Equals("value", StringComparison.OrdinalIgnoreCase);

    private static string StripIomapPointNamePrefixInSubscriptionName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        int dollar = name.IndexOf('$');
        if (dollar > 0 && dollar < name.Length - 1)
        {
            string rest = name[(dollar + 1)..];
            return IomapOwnership.HasPointNamePrefix(rest)
                ? name[..(dollar + 1)] + IomapOwnership.StripPointNamePrefix(rest)
                : name;
        }

        return IomapOwnership.HasPointNamePrefix(name)
            ? IomapOwnership.StripPointNamePrefix(name)
            : name;
    }

    /// <summary>
    /// 名字解析。点名可能本身含 '$' 与 '.'（本工程中间点如 1001$83$PAI121.OUT），
    /// 所以顺序是：整名全局命中 → DPU$ 前缀限定命中 → 按最后一个 '.' 拆成员再查。
    /// </summary>
    private bool TryResolveMember(DcsRuntime rt, string name, out PointSlotRef slot, out string member)
    {
        name = StripIomapPointNamePrefixInSubscriptionName(name);
        member = "";

        // 1) 整名直接命中（优先级最高，避免把点名里的 $ / . 误当分隔符）
        if (TryFindSlot(rt, null, name, out slot))
            return true;

        // 2) DPU$ 前缀限定（仅当前缀确实是 DPU 名时才生效）
        DpuRuntime? scope = null;
        string rest = name;
        int dollar = name.IndexOf('$');
        if (dollar > 0)
        {
            scope = rt.FindDpu(name[..dollar]);
            if (scope != null)
            {
                rest = name[(dollar + 1)..];
                if (TryFindSlot(rt, scope, rest, out slot))
                    return true;
            }
        }

        // 3) 成员拆分（scope 内或全局）
        int dot = rest.LastIndexOf('.');
        if (dot > 0)
        {
            member = rest[(dot + 1)..];
            if (TryFindSlot(rt, scope, rest[..dot], out slot))
                return true;
        }

        // 4) 有 DPU 前缀但没命中：回退整名做成员拆分
        if (scope != null)
        {
            int dot2 = name.LastIndexOf('.');
            if (dot2 > 0)
            {
                member = name[(dot2 + 1)..];
                if (TryFindSlot(rt, null, name[..dot2], out slot))
                    return true;
            }
        }

        member = "";
        slot = default;
        return false;
    }

    /// <summary>[DPU$]BLOCK.PIN → 块命令 + 字段名（块整名不带成员时 field 为空串）。</summary>
    private static bool TryResolveBlockMember(DcsRuntime rt, string name, out BlockCommand cmd, out string field)
    {
        field = "";

        // 整名当块名（块名可含 $ / .）
        if (TryFindCommand(rt, null, name, out cmd))
            return true;

        DpuRuntime? scope = null;
        string rest = name;
        int dollar = name.IndexOf('$');
        if (dollar > 0)
        {
            scope = rt.FindDpu(name[..dollar]);
            if (scope != null)
            {
                rest = name[(dollar + 1)..];
                if (TryFindCommand(rt, scope, rest, out cmd))
                    return true;
            }
        }

        int dot = rest.LastIndexOf('.');
        if (dot > 0)
        {
            field = rest[(dot + 1)..];
            if (TryFindCommand(rt, scope, rest[..dot], out cmd))
                return true;
        }

        if (scope != null)
        {
            int dot2 = name.LastIndexOf('.');
            if (dot2 > 0)
            {
                field = name[(dot2 + 1)..];
                if (TryFindCommand(rt, null, name[..dot2], out cmd))
                    return true;
            }
        }

        field = "";
        cmd = null!;
        return false;
    }

    private static bool TryFindCommand(DcsRuntime rt, DpuRuntime? scope, string blockName, out BlockCommand cmd)
    {
        cmd = null!;
        if (scope != null)
        {
            cmd = scope.FindCommand(blockName)!;
            return cmd != null;
        }
        foreach (var d in rt.Dpus)
        {
            var c = d.FindCommand(blockName);
            if (c != null)
            {
                cmd = c;
                return true;
            }
        }
        return false;
    }

    private static bool TryFindSlot(DcsRuntime rt, DpuRuntime? scope, string pointName, out PointSlotRef slot)
    {
        if (scope != null)
        {
            if (scope.LocalSlots.TryGetValue(pointName, out slot) && slot.IsRealPoint)
                return true;
            slot = default;
            return false;
        }
        if (rt.TryGetSlot(pointName, out slot) && slot.IsRealPoint)
        {
            return true;
        }
        slot = default;
        return false;
    }

    private static (DpuRuntime, BlockCommand) FindBlock(DcsRuntime rt, string dpuName, string blockName)
    {
        var dpu = rt.FindDpu(dpuName) ?? throw new InvalidOperationException($"DPU 不存在：{dpuName}");
        var cmd = dpu.FindCommand(blockName) ?? throw new InvalidOperationException($"块不存在：{dpuName}/{blockName}");
        return (dpu, cmd);
    }

    private static bool IsPointForced(PointSlotRef slot)
    {
        foreach (var f in PointFieldAccess.ReadAll(slot))
        {
            if (f.Name == "isforced")
                return Convert.ToInt32(f.Value, CultureInfo.InvariantCulture) != 0;
        }
        return false;
    }

    private static object ParsePointValue(PointKind kind, string value) => kind switch
    {
        PointKind.LA => float.Parse(value, CultureInfo.InvariantCulture),
        PointKind.LD => value is "1" or "true" or "True",
        PointKind.LP => ushort.Parse(value, CultureInfo.InvariantCulture),
        PointKind.LP32 => uint.Parse(value, CultureInfo.InvariantCulture),
        _ => throw new InvalidOperationException("块槽不可写"),
    };

    /// <summary>管脚强制值解析：按管脚字段类型定型（LA→float，LD→bool，其余按字面）。</summary>
    private static object ParseForPin(BlockCommand cmd, string pinName, string value)
    {
        var fi = cmd.Fc.GetType().GetField(pinName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"管脚不存在：{pinName}");
        var t = fi.FieldType;
        if (t == typeof(Core.Types.LA))
            return float.Parse(value, CultureInfo.InvariantCulture);
        if (t == typeof(Core.Types.LD))
            return value is "1" or "true" or "True";
        if (t == typeof(Core.Types.LP))
            return ushort.Parse(value, CultureInfo.InvariantCulture);
        if (t == typeof(Core.Types.LP32))
            return uint.Parse(value, CultureInfo.InvariantCulture);
        return float.Parse(value, CultureInfo.InvariantCulture);
    }

    /// <summary>规格数/内部变量文本值解析：按字段类型定型。</summary>
    private static object ParseForField(BlockCommand cmd, string fieldName, string value)
    {
        var fi = cmd.Fc.GetType().GetField(fieldName,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"字段不存在：{fieldName}");
        var t = fi.FieldType;
        if (typeof(IValuable).IsAssignableFrom(t))
        {
            // 管脚型字段直接给原始文本让 SetField 走 Value 语义
            if (t == typeof(Core.Types.LD))
                return value is "1" or "true" or "True";
            return float.Parse(value, CultureInfo.InvariantCulture);
        }
        if (t == typeof(string))
            return value;
        if (t.IsEnum)
            return value;
        if (t == typeof(bool))
            return value is "1" or "true" or "True";
        if (t == typeof(float))
            return float.Parse(value, CultureInfo.InvariantCulture);
        if (t == typeof(double))
            return double.Parse(value, CultureInfo.InvariantCulture);
        if (t == typeof(int))
            return int.Parse(value, CultureInfo.InvariantCulture);
        if (t == typeof(uint))
            return uint.Parse(value, CultureInfo.InvariantCulture);
        if (t == typeof(long))
            return long.Parse(value, CultureInfo.InvariantCulture);
        if (t == typeof(ushort))
            return ushort.Parse(value, CultureInfo.InvariantCulture);
        if (t == typeof(byte))
            return byte.Parse(value, CultureInfo.InvariantCulture);
        return value;
    }

    private object Enrich(StoreEntryInfo e) => new
    {
        name = e.Name,
        kind = e.Kind,
        fingerprint = e.Fingerprint,
        projectVersion = e.ProjectVersion,
        savedAtUtc = e.SavedAtUtc,
        comment = e.Comment,
        sizeBytes = e.SizeBytes,
        matchesCurrent = e.Fingerprint == _host.Fingerprint,
    };

    private IResult OkState() => Results.Json(new { ok = true, state = _host.RunState.ToString() });

    private static object ToLogDto(LogEntry e) => new
    {
        seq = e.Seq,
        time = e.TimeUtc.ToLocalTime().ToString("HH:mm:ss.fff"),
        level = e.Level.ToString(),
        source = e.Source,
        message = e.Message,
    };

    private static async Task WriteSse(HttpContext ctx, LogEntry e)
    {
        string json = System.Text.Json.JsonSerializer.Serialize(ToLogDto(e));
        await ctx.Response.WriteAsync($"data: {json}\n\n", Encoding.UTF8, ctx.RequestAborted);
    }

    // ---- 请求 DTO
    public sealed record LoadProjectRequest(string MdbPath, bool? FirstRun);
    public sealed record StepRequest(int Cycles);
    public sealed record SetCycleRequest(float Seconds);
    public sealed record SetValueRequest(string Value);
    public sealed record SetFieldRequest(string Field, string Value);
    public sealed record ForceRequest(bool Forced, string? Value);
    public sealed record PinForceRequest(string Pin, bool Forced, string Value);
    public sealed record SaveEntryRequest(string Name, string? Comment);
    public sealed record PrepareDownloadRequest(string MdbPath);
    public sealed record CommitDownloadRequest(string PlanId, bool? Backup);
    public sealed record HotloadRequest(string[] Targets);
    public sealed record BatchReadRequest(string[] Names);

    public sealed class BatchWriteRequest
    {
        public string? ClientInfo { get; set; }
        public BatchWriteItem[] Items { get; set; } = [];
    }

    public sealed class BatchWriteItem
    {
        public string Name { get; set; } = "";
        public string Value { get; set; } = "";
        public bool IomapOwned { get; set; }
    }

    public sealed class IomapMarkRequest
    {
        public string[] Names { get; set; } = [];
    }

    public sealed class CompatDpuPinRequest
    {
        public string Dpu { get; set; } = "";
        public List<string> PinPaths { get; set; } = [];
    }

    public sealed class CompatSetVariablesRequest
    {
        public string? ClientInfo { get; set; }
        public List<CompatSetVariablesItemRequest> Items { get; set; } = [];
    }

    public sealed class CompatSetVariablesItemRequest
    {
        public string? PointName { get; set; }
        public JsonElement Value { get; set; }
    }

    public sealed class CompatSetVariablesItemResult
    {
        public int Index { get; set; }
        public string? OriginalPointName { get; set; }
        public string? PointName { get; set; }
        public object? Value { get; set; }
        public bool Success { get; set; }
        public string? Error { get; set; }
    }

    public sealed class CompatSetVariables2Request
    {
        public string? ClientInfo { get; set; }
        public List<CompatSetVariables2ItemRequest?> Items { get; set; } = [];
    }

    public sealed class CompatSetVariables2ItemRequest
    {
        public long Fsid { get; set; }
        public JsonElement Value { get; set; }
    }

    public sealed class CompatSetVariables2ItemResult
    {
        public int Index { get; set; }
        public long Fsid { get; set; }
        public object? Value { get; set; }
        public bool Success { get; set; }
        public string? Error { get; set; }
    }

    public sealed class CompatSubscribeBatchRequest
    {
        public string?[] DpuNames { get; set; } = [];
        public string?[] Names { get; set; } = [];
        public string?[] Members { get; set; } = [];
        public bool Unknow { get; set; }
    }

    public sealed class CompatPinForceRequest
    {
        public string Dpu { get; set; } = "";
        public string Block { get; set; } = "";
        public string Pin { get; set; } = "";
        public bool IsForce { get; set; }
        public JsonElement Value { get; set; }
    }

    public sealed class CompatSetValueRequest
    {
        public string? PointName { get; set; }
        public JsonElement Value { get; set; }
    }

    private sealed record CompatPinRow(object Pin, string? PointName);
}
