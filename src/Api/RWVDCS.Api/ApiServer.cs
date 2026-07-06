using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using RWVDCS.Runtime;

namespace RWVDCS.Api;

/// <summary>
/// Web 管理接口（Kestrel 自承载）：REST + SSE 日志流 + 静态 Web 界面。
/// 同一套 REST 即教练员站接口（工况/快照/运行控制），见 docs/教练员站接口。
/// </summary>
public sealed class ApiServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly RuntimeHost _host;

    public string Url { get; }

    public ApiServer(RuntimeHost host, int port)
    {
        _host = host;
        Url = $"http://localhost:{port}";

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://*:{port}");

        _app = builder.Build();

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

    public Task StartAsync() => _app.StartAsync();

    public async ValueTask DisposeAsync()
    {
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
            var rt = _host.Runtime;
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

        api.MapGet("/dpus", () =>
        {
            var rt = RequireRuntime();
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
            }));
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
            var rt = RequireRuntime();
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
                        items.Add(new
                        {
                            dpu = d.Name,
                            name,
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
            var rt = RequireRuntime();
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
                        items.Add(new
                        {
                            dpu = d.Name,
                            name = cmd.Name,
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
            var rt = RequireRuntime();
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var d in rt.Dpus)
            foreach (var cmd in d.Commands)
                counts[cmd.FcName] = counts.GetValueOrDefault(cmd.FcName) + 1;
            return Results.Json(counts.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Select(kv => new { fc = kv.Key, count = kv.Value }));
        });

        // ---------------- 点详情（PointInfo）----------------
        api.MapGet("/point/{name}", (string name) =>
        {
            var rt = RequireRuntime();
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
            var rt = RequireRuntime();
            var (_, slot) = FindPoint(rt, name) ?? throw new InvalidOperationException($"点不存在：{name}");
            slot.WriteBoxedBuffer(ParsePointValue(slot.Kind, req.Value));
            _host.Log.Info("写值", $"点 {name} <= {req.Value}");
            return Results.Json(new { ok = true, value = slot.ReadBoxedBuffer() });
        });

        api.MapPut("/point/{name}/field", (string name, SetFieldRequest req) =>
        {
            var rt = RequireRuntime();
            var (_, slot) = FindPoint(rt, name) ?? throw new InvalidOperationException($"点不存在：{name}");
            if (!PointFieldAccess.Write(slot, req.Field, req.Value))
                throw new InvalidOperationException($"字段写入失败：{name}.{req.Field}");
            _host.Log.Info("写值", $"点 {name}.{req.Field} <= {req.Value}");
            return Results.Json(new { ok = true, fields = PointFieldAccess.ReadAll(slot).Select(f => new { f.Name, f.Type, f.Value }) });
        });

        api.MapPost("/point/{name}/force", (string name, ForceRequest req) =>
        {
            var rt = RequireRuntime();
            var (_, slot) = FindPoint(rt, name) ?? throw new InvalidOperationException($"点不存在：{name}");
            if (!PointFieldAccess.SetForce(slot, req.Forced, req.Value))
                throw new InvalidOperationException($"点强制失败：{name}");
            _host.Log.Info("强制", req.Forced ? $"点 {name} 强制 = {req.Value}" : $"点 {name} 解除强制");
            return Results.Json(new { ok = true, value = slot.ReadBoxedBuffer() });
        });

        // ---------------- 块详情（PointInfo 的块视图）----------------
        api.MapGet("/block/{dpu}/{name}", (string dpu, string name) =>
        {
            var (d, cmd) = FindBlock(dpu, name);
            return Results.Json(BuildBlockDetail(d, cmd));
        });

        // 跨 DPU 找块（列表页跳转用）
        api.MapGet("/blockfind/{name}", (string name) =>
        {
            var rt = RequireRuntime();
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
            var (_, cmd) = FindBlock(dpu, name);
            object forceValue = ParseForPin(cmd, req.Pin, req.Value);
            cmd.SetPinForce(req.Pin, req.Forced, forceValue);
            _host.Log.Info("强制", req.Forced
                ? $"块 {dpu}/{name} 管脚 {req.Pin} 强制 = {req.Value}"
                : $"块 {dpu}/{name} 管脚 {req.Pin} 解除强制");
            return Results.Json(new { ok = true });
        });

        api.MapPut("/block/{dpu}/{name}/field", (string dpu, string name, SetFieldRequest req) =>
        {
            var (d, cmd) = FindBlock(dpu, name);
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
            var rt = RequireRuntime();
            var values = new object?[req.Names.Length];
            for (int i = 0; i < req.Names.Length; i++)
                values[i] = TryReadMember(rt, req.Names[i]);
            return Results.Json(new { values });
        });

        api.MapPost("/values/write", (BatchWriteRequest req) =>
        {
            var rt = RequireRuntime();
            var results = new bool[req.Items.Length];
            for (int i = 0; i < req.Items.Length; i++)
            {
                var item = req.Items[i];
                results[i] = TryWriteMember(rt, item.Name, item.Value);
            }
            return Results.Json(new { results });
        });

        // 批量类型描述：Remoting 适配器订阅时定型（保证回传老客户端的装箱类型一致）
        api.MapPost("/values/describe", (BatchReadRequest req) =>
        {
            var rt = RequireRuntime();
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
            var history = _host.History ?? throw new InvalidOperationException("历史站未启用（启动加 --history）");
            var rt = RequireRuntime();
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

    private DcsRuntime RequireRuntime()
        => _host.Runtime ?? throw new InvalidOperationException("尚未装载工程");

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
            if (member.Length == 0 || member.Equals("buffer", StringComparison.OrdinalIgnoreCase))
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
    private bool TryWriteMember(DcsRuntime rt, string name, string value)
    {
        if (TryResolveMember(rt, name, out var slot, out string member))
        {
            if (member.Length == 0 || member.Equals("buffer", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    slot.WriteBoxedBuffer(ParsePointValue(slot.Kind, value));
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
            if (member.Length == 0 || member.Equals("buffer", StringComparison.OrdinalIgnoreCase))
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

    /// <summary>
    /// 名字解析。点名可能本身含 '$' 与 '.'（本工程中间点如 1001$83$PAI121.OUT），
    /// 所以顺序是：整名全局命中 → DPU$ 前缀限定命中 → 按最后一个 '.' 拆成员再查。
    /// </summary>
    private bool TryResolveMember(DcsRuntime rt, string name, out PointSlotRef slot, out string member)
    {
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
        foreach (var d in rt.Dpus)
        {
            if (d.LocalSlots.TryGetValue(pointName, out slot) && slot.IsRealPoint)
                return true;
        }
        slot = default;
        return false;
    }

    private (DpuRuntime, BlockCommand) FindBlock(string dpuName, string blockName)
    {
        var rt = RequireRuntime();
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
    public sealed record BatchWriteRequest(BatchWriteItem[] Items);
    public sealed record BatchWriteItem(string Name, string Value);
}
