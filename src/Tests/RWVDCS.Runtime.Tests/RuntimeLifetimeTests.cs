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
        }
        finally
        {
            lease?.Dispose();
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

    private static int ReserveTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
