using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using RWVDCS.Api;
using RWVDCS.Core.Blocks;
using RWVDCS.Engineering;
using RWVDCS.Runtime;

namespace RWVDCS.Runtime.Tests;

public sealed class RuntimeLifetimeTests
{
    [Fact]
    public async Task Runtime_swap_waits_for_active_read_lease_before_disposing_old_arena()
    {
        string dataDirectory = Path.Combine(
            Path.GetTempPath(),
            $"rwvdcs-runtime-lifetime-tests-{Guid.NewGuid():N}");
        var host = new RuntimeHost(new RuntimeHostOptions
        {
            BlocksAssembly = typeof(RuntimeLifetimeTests).Assembly,
            DataDirectory = dataDirectory,
            EnableHistory = false,
        });
        RuntimeReadLease? oldLease = null;

        try
        {
            EngineeringModel oldModel = BuildModel(defaultValue: 10f);
            DcsRuntime oldRuntime = BuildRuntime(oldModel);
            SetRuntime(host, oldRuntime);
            PointSlotRef oldSlot = oldRuntime.Dpus[0].LocalSlots["AI001"];

            EngineeringModel newModel = BuildModel(defaultValue: 20f);
            DcsRuntime newRuntime = BuildRuntime(newModel);
            MethodInfo swapRuntime = typeof(RuntimeHost).GetMethod(
                "SwapRuntime",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("找不到 RuntimeHost.SwapRuntime 方法");

            oldLease = host.AcquireRuntimeLease();

            Task swapTask = StartRuntimeSwap(host, swapRuntime, newRuntime, newModel);

            Assert.True(WaitUntilRuntimeRetiring(host), "换代线程没有进入 Runtime 退休屏障");
            Assert.False(swapTask.IsCompleted, "仍有旧代读取租约时，Runtime 换代不应完成");
            Assert.Equal(10f, oldSlot.ReadBoxedBuffer());

            oldLease.Dispose();
            oldLease = null;
            await swapTask.WaitAsync(TimeSpan.FromSeconds(5));

            using RuntimeReadLease newLease = host.AcquireRuntimeLease();
            Assert.Same(newRuntime, newLease.Runtime);
            Assert.Equal(20f, newRuntime.Dpus[0].LocalSlots["AI001"].ReadBoxedBuffer());
            Assert.Throws<ObjectDisposedException>(() => oldSlot.ReadBoxedBuffer());
            Assert.Throws<ObjectDisposedException>(() => oldSlot.WriteBoxedBuffer(11f));
        }
        finally
        {
            oldLease?.Dispose();
            host.Dispose();
            if (Directory.Exists(dataDirectory))
                Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task GetPointValues_waits_for_runtime_swap_and_reads_new_generation()
    {
        string dataDirectory = Path.Combine(
            Path.GetTempPath(),
            $"rwvdcs-runtime-api-lifetime-tests-{Guid.NewGuid():N}");
        var host = new RuntimeHost(new RuntimeHostOptions
        {
            BlocksAssembly = typeof(RuntimeLifetimeTests).Assembly,
            DataDirectory = dataDirectory,
            EnableHistory = false,
        });
        RuntimeReadLease? oldLease = null;

        try
        {
            EngineeringModel oldModel = BuildModel(defaultValue: 10f);
            SetRuntime(host, BuildRuntime(oldModel));

            int port = ReserveTcpPort();
            await using var server = new ApiServer(host, port);
            await server.StartAsync();
            using var handler = new HttpClientHandler { UseProxy = false };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(server.Url) };

            EngineeringModel newModel = BuildModel(defaultValue: 20f);
            DcsRuntime newRuntime = BuildRuntime(newModel);
            MethodInfo swapRuntime = typeof(RuntimeHost).GetMethod(
                "SwapRuntime",
                BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("找不到 RuntimeHost.SwapRuntime 方法");

            oldLease = host.AcquireRuntimeLease();
            Task swapTask = StartRuntimeSwap(host, swapRuntime, newRuntime, newModel);
            Assert.True(WaitUntilRuntimeRetiring(host), "换代线程没有进入 Runtime 退休屏障");

            Task<HttpResponseMessage> requestTask = client.PostAsJsonAsync(
                "/api/point/GetPointValues",
                new[] { "AI001.Value" });
            await Task.Delay(100);
            Assert.False(requestTask.IsCompleted, "Runtime 换代期间，点值请求不应越过退休屏障");

            oldLease.Dispose();
            oldLease = null;
            await swapTask.WaitAsync(TimeSpan.FromSeconds(5));

            using HttpResponseMessage response = await requestTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
            Assert.Equal("20", json.RootElement.GetProperty("AI001.Value").GetString());
        }
        finally
        {
            oldLease?.Dispose();
            host.Dispose();
            if (Directory.Exists(dataDirectory))
                Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Host_dispose_waits_for_active_read_lease_before_disposing_arena()
    {
        string dataDirectory = Path.Combine(
            Path.GetTempPath(),
            $"rwvdcs-runtime-dispose-tests-{Guid.NewGuid():N}");
        var host = new RuntimeHost(new RuntimeHostOptions
        {
            BlocksAssembly = typeof(RuntimeLifetimeTests).Assembly,
            DataDirectory = dataDirectory,
            EnableHistory = false,
        });
        RuntimeReadLease? lease = null;

        try
        {
            DcsRuntime runtime = BuildRuntime(BuildModel(defaultValue: 30f));
            SetRuntime(host, runtime);
            PointSlotRef slot = runtime.Dpus[0].LocalSlots["AI001"];
            lease = host.AcquireRuntimeLease();

            Task disposeTask = Task.Run(host.Dispose);
            Assert.True(WaitUntilRuntimeRetiring(host), "宿主关闭没有进入 Runtime 退休屏障");
            Assert.False(disposeTask.IsCompleted, "仍有读取租约时，宿主不应完成关闭");
            Assert.Equal(30f, slot.ReadBoxedBuffer());

            lease.Dispose();
            lease = null;
            await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Null(host.TryAcquireRuntimeLease());
            Assert.Throws<ObjectDisposedException>(() => slot.ReadBoxedBuffer());
            Assert.Throws<ObjectDisposedException>(() => slot.WriteBoxedBuffer(31f));
        }
        finally
        {
            lease?.Dispose();
            host.Dispose();
            if (Directory.Exists(dataDirectory))
                Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Realtime_value_service_dispose_waits_for_inflight_change_callback()
    {
        string dataDirectory = Path.Combine(
            Path.GetTempPath(),
            $"rwvdcs-realtime-value-dispose-tests-{Guid.NewGuid():N}");
        var host = new RuntimeHost(new RuntimeHostOptions
        {
            BlocksAssembly = typeof(RuntimeLifetimeTests).Assembly,
            DataDirectory = dataDirectory,
            EnableHistory = false,
        });
        var service = new RealtimeValueService(host, changeScanIntervalMs: 50);
        using var callbackEntered = new ManualResetEventSlim(initialState: false);
        using var releaseCallback = new ManualResetEventSlim(initialState: false);
        Task? disposeTask = null;

        try
        {
            SetRuntime(host, BuildRuntime(BuildModel(defaultValue: 40f)));
            int client = service.Attach(requestedClientHandle: 0, useDataChange: true);
            RealtimeSubscribeResult subscription = Assert.Single(service.Subscribe(client, ["AI001"]));
            Assert.True(subscription.Found);

            service.DataChanged += (_, _, _) =>
            {
                callbackEntered.Set();
                releaseCallback.Wait(TimeSpan.FromSeconds(5));
            };

            Assert.True(callbackEntered.Wait(TimeSpan.FromSeconds(5)), "变化扫描回调没有按期进入");

            disposeTask = Task.Run(service.Dispose);
            await Task.Delay(100);
            Assert.False(disposeTask.IsCompleted, "实时值服务不应在在途变化扫描回调结束前完成释放");

            releaseCallback.Set();
            await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));
            disposeTask = null;
        }
        finally
        {
            releaseCallback.Set();
            if (disposeTask != null)
                await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));
            service.Dispose();
            host.Dispose();
            if (Directory.Exists(dataDirectory))
                Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Realtime_write_holds_runtime_lease_until_point_write_finishes()
    {
        string dataDirectory = Path.Combine(
            Path.GetTempPath(),
            $"rwvdcs-realtime-write-lifetime-tests-{Guid.NewGuid():N}");
        var host = new RuntimeHost(new RuntimeHostOptions
        {
            BlocksAssembly = typeof(RuntimeLifetimeTests).Assembly,
            DataDirectory = dataDirectory,
            EnableHistory = false,
        });
        var service = new RealtimeValueService(host, changeScanIntervalMs: 1000);
        using var conversionEntered = new ManualResetEventSlim(initialState: false);
        using var releaseConversion = new ManualResetEventSlim(initialState: false);
        Task<bool[]>? writeTask = null;
        Task? disposeTask = null;

        try
        {
            DcsRuntime runtime = BuildRuntime(BuildModel(defaultValue: 10f));
            SetRuntime(host, runtime);
            PointSlotRef slot = runtime.Dpus[0].LocalSlots["AI001"];
            var blockingValue = new BlockingConvertible(55f, conversionEntered, releaseConversion);

            writeTask = Task.Run(() => service.WriteByNames(
                ["AI001"],
                [blockingValue],
                clientInfo: null));

            Assert.True(conversionEntered.Wait(TimeSpan.FromSeconds(5)), "实时写入没有进入类型转换阶段");
            Assert.Equal(1, GetActiveRuntimeLeaseCount(host));

            disposeTask = Task.Run(host.Dispose);
            await Task.Delay(100);
            Assert.False(disposeTask.IsCompleted, "实时点写入完成前不应释放 Runtime/Arena");

            releaseConversion.Set();
            Assert.True(Assert.Single(await writeTask.WaitAsync(TimeSpan.FromSeconds(5))));
            writeTask = null;
            await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));
            disposeTask = null;

            Assert.Throws<ObjectDisposedException>(() => slot.WriteBoxedBuffer(56f));
        }
        finally
        {
            releaseConversion.Set();
            if (writeTask != null)
                await writeTask.WaitAsync(TimeSpan.FromSeconds(5));
            if (disposeTask != null)
                await disposeTask.WaitAsync(TimeSpan.FromSeconds(5));
            service.Dispose();
            host.Dispose();
            if (Directory.Exists(dataDirectory))
                Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private static EngineeringModel BuildModel(float defaultValue) => new()
    {
        ProjectPath = "runtime-lifetime-test",
        Controllers =
        [
            new ControllerModel
            {
                Id = 1,
                Address = "1",
                Name = "DPU1",
                Points =
                [
                    new PointModel
                    {
                        ID = 1,
                        Name = "AI001",
                        DataType = "LA",
                        DefaultValue = defaultValue,
                    },
                ],
                Blocks = [],
            },
        ],
    };

    private static DcsRuntime BuildRuntime(EngineeringModel model) => RuntimeBuilder.Build(
        model.Clone(),
        new BlockCatalog(typeof(RuntimeLifetimeTests).Assembly));

    private static void SetRuntime(RuntimeHost host, DcsRuntime runtime)
    {
        PropertyInfo runtimeProperty = typeof(RuntimeHost).GetProperty(nameof(RuntimeHost.Runtime))
            ?? throw new InvalidOperationException("找不到 RuntimeHost.Runtime 属性");
        runtimeProperty.SetValue(host, runtime);
    }

    private static Task StartRuntimeSwap(
        RuntimeHost host,
        MethodInfo swapRuntime,
        DcsRuntime newRuntime,
        EngineeringModel newModel) => Task.Run(() => swapRuntime.Invoke(host,
    [
        newRuntime,
        newModel.Clone(),
        newModel,
        "runtime-lifetime-test.mdb",
        ProjectFingerprint.Compute(newModel),
        ScanState.Stopped,
    ]));

    private static bool WaitUntilRuntimeRetiring(RuntimeHost host)
    {
        FieldInfo gateField = typeof(RuntimeHost).GetField(
            "_runtimeLeaseGate",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 RuntimeHost._runtimeLeaseGate 字段");
        FieldInfo retiringField = typeof(RuntimeHost).GetField(
            "_runtimeRetiring",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 RuntimeHost._runtimeRetiring 字段");
        object gate = gateField.GetValue(host)
            ?? throw new InvalidOperationException("Runtime 生命周期锁为空");

        return SpinWait.SpinUntil(() =>
        {
            lock (gate)
                return (bool)(retiringField.GetValue(host) ?? false);
        }, TimeSpan.FromSeconds(5));
    }

    private static int GetActiveRuntimeLeaseCount(RuntimeHost host)
    {
        FieldInfo field = typeof(RuntimeHost).GetField(
            "_activeRuntimeLeases",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 RuntimeHost._activeRuntimeLeases 字段");
        return (int)(field.GetValue(host) ?? 0);
    }

    private sealed class BlockingConvertible(
        float value,
        ManualResetEventSlim entered,
        ManualResetEventSlim release) : IConvertible
    {
        public TypeCode GetTypeCode() => TypeCode.Single;

        public float ToSingle(IFormatProvider? provider)
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5)))
                throw new TimeoutException("等待测试释放类型转换超时");
            return value;
        }

        public object ToType(Type conversionType, IFormatProvider? provider)
            => conversionType == typeof(float)
                ? ToSingle(provider)
                : throw new InvalidCastException();

        public bool ToBoolean(IFormatProvider? provider) => throw new InvalidCastException();
        public byte ToByte(IFormatProvider? provider) => throw new InvalidCastException();
        public char ToChar(IFormatProvider? provider) => throw new InvalidCastException();
        public DateTime ToDateTime(IFormatProvider? provider) => throw new InvalidCastException();
        public decimal ToDecimal(IFormatProvider? provider) => throw new InvalidCastException();
        public double ToDouble(IFormatProvider? provider) => ToSingle(provider);
        public short ToInt16(IFormatProvider? provider) => throw new InvalidCastException();
        public int ToInt32(IFormatProvider? provider) => throw new InvalidCastException();
        public long ToInt64(IFormatProvider? provider) => throw new InvalidCastException();
        public sbyte ToSByte(IFormatProvider? provider) => throw new InvalidCastException();
        public string ToString(IFormatProvider? provider) => value.ToString(provider);
        public ushort ToUInt16(IFormatProvider? provider) => throw new InvalidCastException();
        public uint ToUInt32(IFormatProvider? provider) => throw new InvalidCastException();
        public ulong ToUInt64(IFormatProvider? provider) => throw new InvalidCastException();
    }

    private static int ReserveTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
