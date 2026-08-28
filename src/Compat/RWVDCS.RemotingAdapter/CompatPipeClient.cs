using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using RWVDCS.CompatProtocol;

namespace RWVDCS.RemotingAdapter
{
    internal struct PipeSubscribeItem
    {
        public long Handle;
        public CompatValueKind ValueKind;
        public bool Found;
        public bool Writable;
    }

    /// <summary>net48 Edge 到 net10 Host 的固定二进制命名管道客户端。</summary>
    internal sealed class CompatPipeClient : IDisposable
    {
        private readonly string _requestPipeName;
        private readonly string _eventPipeName;
        private readonly int _timeoutMs;
        private readonly Action<string> _log;
        private readonly object _requestGate = new object();
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private NamedPipeClientStream _requestPipe;
        private NamedPipeClientStream _eventPipe;
        private Thread _eventThread;
        private ulong _nextRequestId;
        private ulong _generation;
        private long _connectionEpoch;
        private bool _disposed;

        public CompatPipeClient(string requestPipeName, string eventPipeName, int timeoutMs, Action<string> log)
        {
            _requestPipeName = requestPipeName;
            _eventPipeName = eventPipeName;
            _timeoutMs = Math.Max(500, timeoutMs);
            _log = log ?? (_ => { });
        }

        public event Action<int, long[], object[]> DataChanged;
        public event Action<ulong> RuntimeChanging;
        public event Action<ulong, long[]> RuntimeRebound;
        /// <summary>
        /// 事件管道连接成功。参数为 true 表示曾经连接过，通常意味着 Host 已重启或管道曾中断；
        /// false 表示首次连接，用于覆盖 Adapter 先启动、Host 后启动的恢复场景。
        /// </summary>
        public event Action<bool> EventChannelConnected;

        public ulong RuntimeGeneration => _generation;
        public long ConnectionEpoch => Interlocked.Read(ref _connectionEpoch);

        public void StartEvents()
        {
            if (_eventThread != null)
                return;
            _eventThread = new Thread(EventLoop)
            {
                IsBackground = true,
                Name = "compat-pipe-events",
            };
            _eventThread.Start();
        }

        public bool TryConnect()
        {
            try
            {
                lock (_requestGate)
                    EnsureRequestConnected();
                return true;
            }
            catch (Exception ex)
            {
                _log("二进制请求管道暂不可用：" + ex.Message);
                return false;
            }
        }

        public void ForceReconnect()
        {
            lock (_requestGate)
            {
                ThrowIfDisposed();
                CloseRequestPipe();
                EnsureRequestConnected();
            }
        }

        public int Attach(int requestedHandle, bool useDataChange)
        {
            var response = Send(CompatOperation.Attach, requestedHandle,
                CompatBinary.Build(w =>
                {
                    w.Write(requestedHandle);
                    w.Write(useDataChange);
                }));
            using (var reader = OpenSuccess(response))
                return reader.ReadInt32();
        }

        public void Detach(int clientHandle)
            => ExpectOk(Send(CompatOperation.Detach, clientHandle, new byte[0]));

        public bool Renew(int clientHandle)
        {
            using (var reader = OpenSuccess(Send(CompatOperation.Renew, clientHandle, new byte[0])))
                return reader.ReadBoolean();
        }

        public PipeSubscribeItem[] Subscribe(int clientHandle, string[] names)
        {
            names = names ?? new string[0];
            var totalWatch = Stopwatch.StartNew();
            var encodeWatch = Stopwatch.StartNew();
            int estimatedBytes = names.Length > (CompatProtocolConstants.DefaultMaxPayloadBytes - 4) / 24
                ? CompatProtocolConstants.DefaultMaxPayloadBytes
                : 4 + names.Length * 24;
            byte[] payload = BuildSubscribePayload(names, estimatedBytes);
            encodeWatch.Stop();
            var sendWatch = Stopwatch.StartNew();
            var response = Send(CompatOperation.SubscribeBatch, clientHandle, payload);
            sendWatch.Stop();
            var decodeWatch = Stopwatch.StartNew();
            using (var reader = OpenSuccess(response))
            {
                int count = reader.ReadInt32();
                CompatBinary.ValidateCount(count);
                var result = new PipeSubscribeItem[count];
                for (int i = 0; i < count; i++)
                {
                    result[i] = new PipeSubscribeItem
                    {
                        Handle = reader.ReadInt64(),
                        ValueKind = (CompatValueKind)reader.ReadByte(),
                        Found = reader.ReadBoolean(),
                        Writable = reader.ReadBoolean(),
                    };
                }
                decodeWatch.Stop();
                totalWatch.Stop();
                _log($"[IPC性能] Subscribe count={names.Length} payload={payload.Length / 1024d:F1} KB "
                    + $"encode={encodeWatch.Elapsed.TotalMilliseconds:F3} ms "
                    + $"send={sendWatch.Elapsed.TotalMilliseconds:F3} ms "
                    + $"decode={decodeWatch.Elapsed.TotalMilliseconds:F3} ms "
                    + $"total={totalWatch.Elapsed.TotalMilliseconds:F3} ms");
                return result;
            }
        }

        /// <summary>
        /// 在 Adapter 本地预分配订阅负载，避免依赖 CompatProtocol 新增重载造成 Visual Studio
        /// 设计时缓存旧 DLL 后误报 CS1501；线路格式仍由 CompatBinary.WriteStrings 统一定义。
        /// </summary>
        private static byte[] BuildSubscribePayload(string[] names, int initialCapacity)
        {
            using (var stream = new MemoryStream(initialCapacity))
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, true))
            {
                CompatBinary.WriteStrings(writer, names);
                writer.Flush();
                return stream.ToArray();
            }
        }

        public void Unsubscribe(int clientHandle, long[] handles)
        {
            CompatOperation operation = handles == null
                ? CompatOperation.UnsubscribeAll
                : CompatOperation.UnsubscribeBatch;
            byte[] payload = handles == null
                ? new byte[0]
                : CompatBinary.Build(w => CompatBinary.WriteLongs(w, handles));
            ExpectOk(Send(operation, clientHandle, payload));
        }

        public object[] Read(long[] handles)
        {
            var response = Send(CompatOperation.ReadBatch, 0,
                CompatBinary.Build(w => CompatBinary.WriteLongs(w, handles)));
            using (var reader = OpenSuccess(response))
                return ToObjects(CompatBinary.ReadValues(reader));
        }

        public object[] ReadAll(int clientHandle)
        {
            using (var reader = OpenSuccess(Send(CompatOperation.ReadAll, clientHandle, new byte[0])))
                return ToObjects(CompatBinary.ReadValues(reader));
        }

        public bool[] Write(int clientHandle, long[] handles, object[] values, string clientInfo)
        {
            int count = Math.Min(handles == null ? 0 : handles.Length, values == null ? 0 : values.Length);
            var trimmedHandles = new long[count];
            var typed = new CompatValue[count];
            for (int i = 0; i < count; i++)
            {
                trimmedHandles[i] = handles[i];
                typed[i] = CompatValue.FromObject(values[i]);
            }
            var response = Send(CompatOperation.WriteBatch, clientHandle,
                CompatBinary.Build(w =>
                {
                    CompatBinary.WriteString(w, clientInfo);
                    CompatBinary.WriteLongs(w, trimmedHandles);
                    CompatBinary.WriteValues(w, typed);
                }));
            using (var reader = OpenSuccess(response))
            {
                bool[] compact = CompatBinary.ReadBooleans(reader);
                var result = new bool[handles == null ? 0 : handles.Length];
                Array.Copy(compact, result, Math.Min(compact.Length, result.Length));
                return result;
            }
        }

        public KeyValuePair<long, object>[] PollChanges(int clientHandle)
        {
            using (var reader = OpenSuccess(Send(CompatOperation.PollChanged, clientHandle, new byte[0])))
            {
                int count = reader.ReadInt32();
                CompatBinary.ValidateCount(count);
                var result = new KeyValuePair<long, object>[count];
                for (int i = 0; i < count; i++)
                    result[i] = new KeyValuePair<long, object>(reader.ReadInt64(), CompatBinary.ReadValue(reader).ToObject());
                return result;
            }
        }

        public void SetDataInformType(int clientHandle, bool use)
            => ExpectOk(Send(CompatOperation.SetDataInformType, clientHandle,
                CompatBinary.Build(w => w.Write(use))));

        public void SetPaused(int clientHandle, bool paused)
            => ExpectOk(Send(CompatOperation.PauseSession, clientHandle,
                CompatBinary.Build(w => w.Write(paused))));

        private CompatFrame Send(CompatOperation operation, int sessionId, byte[] payload)
        {
            long totalStarted = Stopwatch.GetTimestamp();
            long lockStarted = totalStarted;
            Monitor.Enter(_requestGate);
            long lockAcquired = Stopwatch.GetTimestamp();
            long connectTicks = 0;
            long writeTicks = 0;
            long readTicks = 0;
            try
            {
                ThrowIfDisposed();
                try
                {
                    long connectStarted = Stopwatch.GetTimestamp();
                    EnsureRequestConnected();
                    connectTicks = Stopwatch.GetTimestamp() - connectStarted;
                    CompatFrame response = ExchangeConnected(new CompatFrame
                    {
                        Operation = operation,
                        RequestId = ++_nextRequestId,
                        SessionId = sessionId,
                        RuntimeGeneration = _generation,
                        Payload = payload,
                    }, out writeTicks, out readTicks);
                    if (operation == CompatOperation.SubscribeBatch)
                    {
                        long completed = Stopwatch.GetTimestamp();
                        _log($"[IPC性能] Send op={operation} payload={(payload == null ? 0 : payload.Length) / 1024d:F1} KB "
                            + $"lockWait={ToMilliseconds(lockAcquired - lockStarted):F3} ms "
                            + $"connect={ToMilliseconds(connectTicks):F3} ms "
                            + $"write={ToMilliseconds(writeTicks):F3} ms "
                            + $"host+read={ToMilliseconds(readTicks):F3} ms "
                            + $"total={ToMilliseconds(completed - totalStarted):F3} ms");
                    }
                    return response;
                }
                catch (Exception ex)
                {
                    long failed = Stopwatch.GetTimestamp();
                    double totalMs = ToMilliseconds(failed - totalStarted);
                    if (operation == CompatOperation.SubscribeBatch || totalMs >= 100d)
                    {
                        _log($"[IPC性能] Send失败 op={operation} payload={(payload == null ? 0 : payload.Length) / 1024d:F1} KB "
                            + $"lockWait={ToMilliseconds(lockAcquired - lockStarted):F3} ms "
                            + $"connect={ToMilliseconds(connectTicks):F3} ms "
                            + $"total={totalMs:F3} ms "
                            + $"error={ex.GetType().Name}: {ex.Message}");
                    }
                    CloseRequestPipe();
                    throw;
                }
            }
            finally
            {
                Monitor.Exit(_requestGate);
            }
        }

        private void EnsureRequestConnected()
        {
            if (_requestPipe != null && _requestPipe.IsConnected)
                return;
            CloseRequestPipe();
            _requestPipe = new NamedPipeClientStream(".", _requestPipeName,
                PipeDirection.InOut, PipeOptions.Asynchronous);
            _requestPipe.Connect(_timeoutMs);
            var hello = ExchangeConnected(new CompatFrame
            {
                Operation = CompatOperation.Hello,
                RequestId = ++_nextRequestId,
                Payload = CompatBinary.Build(w => CompatBinary.WriteString(w, "RWVDCS.RemotingAdapter")),
            });
            using (var reader = OpenSuccess(hello))
            {
                CompatBinary.ReadString(reader);
                CompatBinary.ReadString(reader);
            }
            Interlocked.Increment(ref _connectionEpoch);
            _log("已连接 Host 二进制请求管道：" + _requestPipeName);
        }

        private CompatFrame ExchangeConnected(CompatFrame request)
        {
            long ignoredWrite;
            long ignoredRead;
            return ExchangeConnected(request, out ignoredWrite, out ignoredRead);
        }

        private CompatFrame ExchangeConnected(CompatFrame request, out long writeTicks, out long readTicks)
        {
            using (var timeout = new CancellationTokenSource(_timeoutMs))
            {
                try
                {
                    long started = Stopwatch.GetTimestamp();
                    Task writeTask = CompatFrameCodec.WriteAsync(_requestPipe, request, timeout.Token);
                    WaitForPipeTask(writeTask, started, request.Operation);
                    writeTask.GetAwaiter().GetResult();
                    long written = Stopwatch.GetTimestamp();
                    Task<CompatFrame> readTask = CompatFrameCodec.ReadAsync(_requestPipe, timeout.Token);
                    WaitForPipeTask(readTask, started, request.Operation);
                    CompatFrame response = readTask.GetAwaiter().GetResult();
                    long read = Stopwatch.GetTimestamp();
                    writeTicks = written - started;
                    readTicks = read - written;
                    if (response.RequestId != request.RequestId)
                        throw new InvalidDataException("兼容管道响应 RequestId 不匹配");
                    _generation = response.RuntimeGeneration;
                    return response;
                }
                catch (OperationCanceledException ex) when (timeout.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        $"Host 二进制请求 {request.Operation} 超时（配置 {_timeoutMs} ms）", ex);
                }
            }
        }

        /// <summary>
        /// net48 的 NamedPipeClientStream.ReadAsync 在部分系统上不能靠 CancellationToken
        /// 及时中止内核管道读取。这里用同步等待句柄执行真正的总时限，到期后主动关闭
        /// 当前请求管道，让 Remoting 调用不会把 3 秒配置放大成数十秒。
        /// </summary>
        private void WaitForPipeTask(Task task, long exchangeStarted, CompatOperation operation)
        {
            double elapsedMs = ToMilliseconds(Stopwatch.GetTimestamp() - exchangeStarted);
            int remainingMs = _timeoutMs - (int)Math.Ceiling(elapsedMs);
            if (remainingMs > 0 && WaitForCompletion(task, remainingMs))
                return;

            // Dispose 是可靠取消 net48 命名管道异步 I/O 的兜底手段。
            var pipe = _requestPipe;
            if (pipe != null)
                pipe.Dispose();
            ObserveFault(task);
            throw new TimeoutException(
                $"Host 二进制请求 {operation} 超时（配置 {_timeoutMs} ms，实际等待 {ToMilliseconds(Stopwatch.GetTimestamp() - exchangeStarted):F0} ms）");
        }

        private static bool WaitForCompletion(Task task, int timeoutMs)
        {
            try
            {
                return task.Wait(timeoutMs);
            }
            catch (AggregateException)
            {
                // 已完成但失败/取消；由 GetAwaiter().GetResult() 还原原始异常类型。
                return true;
            }
        }

        private static void ObserveFault(Task task)
        {
            task.ContinueWith(t =>
            {
                var ignored = t.Exception;
            }, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);
        }

        private static double ToMilliseconds(long stopwatchTicks)
            => stopwatchTicks * 1000d / Stopwatch.Frequency;

        private static BinaryReader OpenSuccess(CompatFrame response)
        {
            BinaryReader reader = CompatBinary.Open(response.Payload);
            CompatErrorCode error = (CompatErrorCode)reader.ReadInt32();
            if ((response.Flags & CompatFrameFlags.Error) != 0 || error != CompatErrorCode.Ok)
            {
                string message;
                try { message = CompatBinary.ReadString(reader); }
                catch { message = "未知兼容管道错误"; }
                reader.Dispose();
                throw new InvalidOperationException(error + ": " + message);
            }
            return reader;
        }

        private static void ExpectOk(CompatFrame response)
        {
            using (OpenSuccess(response)) { }
        }

        private void EventLoop()
        {
            bool connectedBefore = false;
            while (!_stop.IsCancellationRequested)
            {
                try
                {
                    var pipe = new NamedPipeClientStream(".", _eventPipeName,
                        PipeDirection.InOut, PipeOptions.Asynchronous);
                    pipe.Connect(_timeoutMs);
                    _eventPipe = pipe;
                    _log("已连接 Host 变化事件管道：" + _eventPipeName);
                    EventChannelConnected?.Invoke(connectedBefore);
                    connectedBefore = true;
                    while (!_stop.IsCancellationRequested && pipe.IsConnected)
                    {
                        CompatFrame frame = CompatFrameCodec.ReadAsync(pipe, _stop.Token).GetAwaiter().GetResult();
                        _generation = frame.RuntimeGeneration;
                        DispatchEvent(frame);
                    }
                }
                catch (OperationCanceledException) when (_stop.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    if (!_stop.IsCancellationRequested)
                    {
                        _log("变化事件管道断开，将重连：" + ex.Message);
                        Thread.Sleep(1000);
                    }
                }
                finally
                {
                    var pipe = _eventPipe;
                    _eventPipe = null;
                    if (pipe != null) pipe.Dispose();
                }
            }
        }

        private void DispatchEvent(CompatFrame frame)
        {
            switch (frame.Operation)
            {
                case CompatOperation.DataChanged:
                    using (var reader = CompatBinary.Open(frame.Payload))
                    {
                        int count = reader.ReadInt32();
                        CompatBinary.ValidateCount(count);
                        var handles = new long[count];
                        var values = new object[count];
                        for (int i = 0; i < count; i++)
                        {
                            handles[i] = reader.ReadInt64();
                            values[i] = CompatBinary.ReadValue(reader).ToObject();
                        }
                        DataChanged?.Invoke(frame.SessionId, handles, values);
                    }
                    break;
                case CompatOperation.RuntimeChanging:
                    RuntimeChanging?.Invoke(frame.RuntimeGeneration);
                    break;
                case CompatOperation.RuntimeRebound:
                    using (var reader = CompatBinary.Open(frame.Payload))
                        RuntimeRebound?.Invoke(frame.RuntimeGeneration, CompatBinary.ReadLongs(reader));
                    break;
            }
        }

        private static object[] ToObjects(CompatValue[] values)
        {
            var result = new object[values.Length];
            for (int i = 0; i < values.Length; i++)
                result[i] = values[i].ToObject();
            return result;
        }

        private void CloseRequestPipe()
        {
            var pipe = _requestPipe;
            _requestPipe = null;
            if (pipe != null) pipe.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(CompatPipeClient));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _stop.Cancel();
            lock (_requestGate) CloseRequestPipe();
            var eventPipe = _eventPipe;
            _eventPipe = null;
            if (eventPipe != null) eventPipe.Dispose();
            if (_eventThread != null && _eventThread.IsAlive)
                _eventThread.Join(2000);
            _stop.Dispose();
        }
    }
}
