using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using RWVDCS.CompatProtocol;

namespace RWVDCS.Api;

/// <summary>
/// Host 内本机兼容网关：请求管道处理订阅/读写，事件管道向 net48 Edge 推送变化和 Runtime 换代。
/// 协议为固定二进制格式，不接受 CLR 类型名或任意对象图。
/// </summary>
public sealed class RealtimeCompatGateway : IAsyncDisposable
{
    private readonly RuntimeHost _host;
    private readonly RealtimeValueService _values;
    private readonly string _requestPipeName;
    private readonly string _eventPipeName;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _eventSignal = new(0);
    private readonly object _pendingGate = new();
    private readonly Dictionary<int, Dictionary<long, CompatValue>> _pendingChanges = [];
    private readonly ConcurrentQueue<CompatFrame> _controlEvents = new();
    private Task? _requestTask;
    private Task? _eventTask;

    public RealtimeCompatGateway(RuntimeHost host, string? requestPipeName = null,
        string? eventPipeName = null, int changeScanIntervalMs = 200)
    {
        _host = host;
        _requestPipeName = string.IsNullOrWhiteSpace(requestPipeName)
            ? CompatProtocolConstants.DefaultRequestPipe
            : requestPipeName;
        _eventPipeName = string.IsNullOrWhiteSpace(eventPipeName)
            ? CompatProtocolConstants.DefaultEventPipe
            : eventPipeName;
        _values = new RealtimeValueService(host, changeScanIntervalMs);
        _values.DataChanged += QueueDataChanged;
        _values.RuntimeChanging += QueueRuntimeChanging;
        _values.RuntimeRebound += QueueRuntimeRebound;
    }

    public RealtimeValueService Values => _values;
    public string RequestPipeName => _requestPipeName;
    public string EventPipeName => _eventPipeName;

    public void Start()
    {
        if (_requestTask != null)
            return;
        _requestTask = Task.Run(RequestAcceptLoop);
        _eventTask = Task.Run(EventAcceptLoop);
        _host.Log.Info("兼容通讯", $"二进制管道已启动：request={_requestPipeName}, events={_eventPipeName}, "
            + $"account={Environment.UserDomainName}\\{Environment.UserName}");
    }

    private async Task RequestAcceptLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            bool hadConnection = false;
            try
            {
                using var pipe = CreateServer(_requestPipeName);
                await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                hadConnection = true;
                _host.Log.Info("兼容通讯", "Remoting Edge 请求管道已连接");
                while (pipe.IsConnected && !_stop.IsCancellationRequested)
                {
                    CompatFrame request = await CompatFrameCodec.ReadAsync(pipe, _stop.Token).ConfigureAwait(false);
                    CompatFrame response = HandleRequest(request);
                    await CompatFrameCodec.WriteAsync(pipe, response, _stop.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!_stop.IsCancellationRequested)
                    _host.Log.Warn("兼容通讯", $"请求管道断开：{ex.Message}");
            }
            finally
            {
                if (hadConnection)
                {
                    // 单 Edge 连接模型：请求管道断开即释放 Host 会话和扫描基线。
                    // Edge 重连后会使用稳定的外部 Handle 按原顺序重放订阅。
                    _values.DetachAll();
                    lock (_pendingGate) _pendingChanges.Clear();
                }
            }
        }
    }

    private CompatFrame HandleRequest(CompatFrame request)
    {
        try
        {
            byte[] payload;
            using var reader = CompatBinary.Open(request.Payload);
            switch (request.Operation)
            {
                case CompatOperation.Hello:
                {
                    string? edgeName = CompatBinary.ReadString(reader);
                    payload = CompatBinary.Build(w =>
                    {
                        w.Write((int)CompatErrorCode.Ok);
                        CompatBinary.WriteString(w, "RWVDCS.Host");
                        CompatBinary.WriteString(w, edgeName);
                    });
                    break;
                }
                case CompatOperation.Attach:
                {
                    int requested = reader.ReadInt32();
                    bool useDataChange = reader.ReadBoolean();
                    int actual = _values.Attach(requested, useDataChange);
                    payload = CompatBinary.Build(w =>
                    {
                        w.Write((int)CompatErrorCode.Ok);
                        w.Write(actual);
                    });
                    break;
                }
                case CompatOperation.Detach:
                    _values.Detach(request.SessionId);
                    payload = OkPayload();
                    break;
                case CompatOperation.Renew:
                    payload = CompatBinary.Build(w =>
                    {
                        w.Write((int)CompatErrorCode.Ok);
                        w.Write(_values.Renew(request.SessionId));
                    });
                    break;
                case CompatOperation.SubscribeBatch:
                {
                    long subscribeStarted = Stopwatch.GetTimestamp();
                    string[] names = CompatBinary.ReadStrings(reader);
                    long namesRead = Stopwatch.GetTimestamp();
                    RealtimeSubscribeResult[] results = _values.Subscribe(request.SessionId, names);
                    long resolved = Stopwatch.GetTimestamp();
                    int found = 0;
                    payload = CompatBinary.Build(w =>
                    {
                        w.Write((int)CompatErrorCode.Ok);
                        w.Write(results.Length);
                        foreach (var result in results)
                        {
                            if (result.Found)
                                found++;
                            w.Write(result.Handle);
                            w.Write((byte)result.ValueKind);
                            w.Write(result.Found);
                            w.Write(result.Writable);
                        }
                    });
                    long encoded = Stopwatch.GetTimestamp();
                    double totalMs = Stopwatch.GetElapsedTime(subscribeStarted, encoded).TotalMilliseconds;
                    if (names.Length >= 1000 || totalMs >= 100d)
                    {
                        _host.Log.Info("兼容通讯",
                            $"[IPC性能] Host Subscribe count={names.Length}, found={found}, "
                            + $"read={Stopwatch.GetElapsedTime(subscribeStarted, namesRead).TotalMilliseconds:F3} ms, "
                            + $"resolve={Stopwatch.GetElapsedTime(namesRead, resolved).TotalMilliseconds:F3} ms, "
                            + $"encode={Stopwatch.GetElapsedTime(resolved, encoded).TotalMilliseconds:F3} ms, "
                            + $"total={totalMs:F3} ms");
                        if (found < names.Length)
                        {
                            var samples = new List<string>(3);
                            for (int i = 0; i < results.Length && samples.Count < 3; i++)
                            {
                                if (results[i].Found)
                                    continue;
                                string sample = names[i] ?? "<null>";
                                sample = sample.Replace('\r', ' ').Replace('\n', ' ');
                                if (sample.Length > 120)
                                    sample = sample[..120] + "…";
                                samples.Add(sample);
                            }
                            _host.Log.Warn("兼容通讯",
                                $"批量订阅未命中 {names.Length - found}/{names.Length}，样本：{string.Join(" | ", samples)}");
                        }
                    }
                    break;
                }
                case CompatOperation.UnsubscribeBatch:
                    _values.Unsubscribe(request.SessionId, CompatBinary.ReadLongs(reader));
                    payload = OkPayload();
                    break;
                case CompatOperation.UnsubscribeAll:
                    _values.Unsubscribe(request.SessionId, null);
                    payload = OkPayload();
                    break;
                case CompatOperation.ReadBatch:
                {
                    CompatValue[] values = _values.Read(CompatBinary.ReadLongs(reader));
                    payload = CompatBinary.Build(w =>
                    {
                        w.Write((int)CompatErrorCode.Ok);
                        CompatBinary.WriteValues(w, values);
                    });
                    break;
                }
                case CompatOperation.ReadAll:
                {
                    CompatValue[] values = _values.ReadAll(request.SessionId);
                    payload = CompatBinary.Build(w =>
                    {
                        w.Write((int)CompatErrorCode.Ok);
                        CompatBinary.WriteValues(w, values);
                    });
                    break;
                }
                case CompatOperation.WriteBatch:
                {
                    string? clientInfo = CompatBinary.ReadString(reader);
                    long[] handles = CompatBinary.ReadLongs(reader);
                    CompatValue[] values = CompatBinary.ReadValues(reader);
                    bool[] results = _values.Write(request.SessionId, handles, values, clientInfo);
                    payload = CompatBinary.Build(w =>
                    {
                        w.Write((int)CompatErrorCode.Ok);
                        CompatBinary.WriteBooleans(w, results);
                    });
                    break;
                }
                case CompatOperation.PollChanged:
                {
                    var changes = _values.PollChanges(request.SessionId);
                    payload = CompatBinary.Build(w =>
                    {
                        w.Write((int)CompatErrorCode.Ok);
                        w.Write(changes.Length);
                        foreach (var change in changes)
                        {
                            w.Write(change.Key);
                            CompatBinary.WriteValue(w, change.Value);
                        }
                    });
                    break;
                }
                case CompatOperation.SetDataInformType:
                    _values.SetUseDataChange(request.SessionId, reader.ReadBoolean());
                    payload = OkPayload();
                    break;
                case CompatOperation.PauseSession:
                    _values.SetPaused(request.SessionId, reader.ReadBoolean());
                    payload = OkPayload();
                    break;
                case CompatOperation.Heartbeat:
                    payload = OkPayload();
                    break;
                default:
                    return ErrorResponse(request, CompatErrorCode.InvalidRequest,
                        "不支持的操作：" + request.Operation);
            }

            return new CompatFrame
            {
                Operation = request.Operation,
                Flags = CompatFrameFlags.Response,
                RequestId = request.RequestId,
                SessionId = request.SessionId,
                RuntimeGeneration = _values.RuntimeGeneration,
                Payload = payload,
            };
        }
        catch (InvalidDataException ex)
        {
            return ErrorResponse(request, CompatErrorCode.InvalidRequest, ex.Message);
        }
        catch (Exception ex)
        {
            _host.Log.Warn("兼容通讯", $"{request.Operation} 处理失败：{ex.Message}");
            return ErrorResponse(request, CompatErrorCode.InternalError, ex.Message);
        }
    }

    private async Task EventAcceptLoop()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                using var pipe = CreateServer(_eventPipeName);
                await pipe.WaitForConnectionAsync(_stop.Token).ConfigureAwait(false);
                _host.Log.Info("兼容通讯", "Remoting Edge 事件管道已连接");
                while (pipe.IsConnected && !_stop.IsCancellationRequested)
                {
                    // 定期发心跳主动探测对端。NamedPipeServerStream.IsConnected 在只写管道空闲时
                    // 不会主动变为 false；没有心跳时，旧 Edge 退出可能使新 Edge 长期无法接入。
                    await _eventSignal.WaitAsync(TimeSpan.FromSeconds(1), _stop.Token).ConfigureAwait(false);
                    if (!pipe.IsConnected)
                        break;
                    while (_controlEvents.TryDequeue(out var control))
                        await CompatFrameCodec.WriteAsync(pipe, control, _stop.Token).ConfigureAwait(false);

                    var pending = DrainPendingChanges();
                    foreach (var item in pending)
                    {
                        var frame = new CompatFrame
                        {
                            Operation = CompatOperation.DataChanged,
                            Flags = CompatFrameFlags.Event,
                            SessionId = item.Client,
                            RuntimeGeneration = _values.RuntimeGeneration,
                            Payload = CompatBinary.Build(w =>
                            {
                                w.Write(item.Handles.Length);
                                for (int i = 0; i < item.Handles.Length; i++)
                                {
                                    w.Write(item.Handles[i]);
                                    CompatBinary.WriteValue(w, item.Values[i]);
                                }
                            }),
                        };
                        try
                        {
                            await CompatFrameCodec.WriteAsync(pipe, frame, _stop.Token).ConfigureAwait(false);
                        }
                        catch
                        {
                            MergePending(item.Client, item.Handles, item.Values);
                            throw;
                        }
                    }

                    await CompatFrameCodec.WriteAsync(pipe, new CompatFrame
                    {
                        Operation = CompatOperation.Heartbeat,
                        Flags = CompatFrameFlags.Event,
                        RuntimeGeneration = _values.RuntimeGeneration,
                        Payload = Array.Empty<byte>(),
                    }, _stop.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!_stop.IsCancellationRequested)
                    _host.Log.Warn("兼容通讯", $"事件管道断开：{ex.Message}");
            }
        }
    }

    private void QueueDataChanged(int client, long[] handles, CompatValue[] values)
    {
        MergePending(client, handles, values);
        SignalEvents();
    }

    private void MergePending(int client, long[] handles, CompatValue[] values)
    {
        lock (_pendingGate)
        {
            if (!_pendingChanges.TryGetValue(client, out var pending))
            {
                pending = [];
                _pendingChanges[client] = pending;
            }
            int count = Math.Min(handles.Length, values.Length);
            for (int i = 0; i < count; i++)
                pending[handles[i]] = values[i];
        }
    }

    private List<(int Client, long[] Handles, CompatValue[] Values)> DrainPendingChanges()
    {
        lock (_pendingGate)
        {
            var result = new List<(int, long[], CompatValue[])>(_pendingChanges.Count);
            foreach (var pair in _pendingChanges)
            {
                long[] handles = pair.Value.Keys.ToArray();
                var values = new CompatValue[handles.Length];
                for (int i = 0; i < handles.Length; i++)
                    values[i] = pair.Value[handles[i]];
                result.Add((pair.Key, handles, values));
            }
            _pendingChanges.Clear();
            return result;
        }
    }

    private void QueueRuntimeChanging(ulong generation)
    {
        _controlEvents.Enqueue(new CompatFrame
        {
            Operation = CompatOperation.RuntimeChanging,
            Flags = CompatFrameFlags.Event,
            RuntimeGeneration = generation,
        });
        SignalEvents();
    }

    private void QueueRuntimeRebound(ulong generation, long[] invalidHandles)
    {
        _controlEvents.Enqueue(new CompatFrame
        {
            Operation = CompatOperation.RuntimeRebound,
            Flags = CompatFrameFlags.Event,
            RuntimeGeneration = generation,
            Payload = CompatBinary.Build(w => CompatBinary.WriteLongs(w, invalidHandles)),
        });
        SignalEvents();
    }

    private void SignalEvents()
    {
        try { _eventSignal.Release(); }
        catch (SemaphoreFullException) { }
    }

    private static NamedPipeServerStream CreateServer(string name)
        => new(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly, 64 * 1024, 64 * 1024);

    private CompatFrame ErrorResponse(CompatFrame request, CompatErrorCode error, string message)
        => new()
        {
            Operation = CompatOperation.Error,
            Flags = CompatFrameFlags.Response | CompatFrameFlags.Error,
            RequestId = request.RequestId,
            SessionId = request.SessionId,
            RuntimeGeneration = _values.RuntimeGeneration,
            Payload = CompatBinary.Build(w =>
            {
                w.Write((int)error);
                CompatBinary.WriteString(w, message);
            }),
        };

    private static byte[] OkPayload() => CompatBinary.Build(w => w.Write((int)CompatErrorCode.Ok));

    public async ValueTask DisposeAsync()
    {
        _values.DataChanged -= QueueDataChanged;
        _values.RuntimeChanging -= QueueRuntimeChanging;
        _values.RuntimeRebound -= QueueRuntimeRebound;
        _values.Dispose();
        _stop.Cancel();
        SignalEvents();
        var tasks = new[] { _requestTask, _eventTask }.Where(t => t != null).Cast<Task>().ToArray();
        if (tasks.Length > 0)
        {
            try { await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch { }
        }
        _eventSignal.Dispose();
        _stop.Dispose();
    }
}
