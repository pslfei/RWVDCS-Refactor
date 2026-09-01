using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using RWVDCS.Api;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using RWVDCS.Engineering;
using RWVDCS.Runtime;

namespace RWVDCS.Runtime.Tests;

[Collection(LegacyRequestLimitCollection.CollectionName)]
public sealed class DpuPinStateApiTests
{
    [Fact]
    public async Task States_endpoint_returns_value_and_force_state_without_changing_values_contract()
    {
        string dataDirectory = CreateDataDirectory();
        try
        {
            using var host = CreateHost(dataDirectory, out DcsRuntime runtime);
            InitializePinValues(GetFunction(runtime));

            int port = ReserveTcpPort();
            await using var server = new ApiServer(host, port);
            await server.StartAsync();
            using var handler = new HttpClientHandler { UseProxy = false };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(server.Url) };

            string[] pinPaths =
            [
                "BLOCK1.Analog",
                "BLOCK1.Digital",
                "BLOCK1.Word",
                "BLOCK1.DWord",
                "BLOCK1.Gain",
                "BLOCK1.Counter",
                "BLOCK1.Missing",
                "MISSING.Analog",
                "malformed",
                "",
            ];
            using HttpResponseMessage response = await client.PostAsJsonAsync(
                "/api/dpu/pins/states",
                new { dpu = "DPU1", pinPaths });

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
            JsonElement root = json.RootElement;

            AssertPinState(root, "BLOCK1.Analog", "12.5", false, "20");
            AssertPinState(root, "BLOCK1.Digital", "False", false, "True");
            AssertPinState(root, "BLOCK1.Word", "3", false, "5");
            AssertPinState(root, "BLOCK1.DWord", "7", false, "9");
            AssertPinState(root, "BLOCK1.Gain", "2.5", false, null);
            AssertPinState(root, "BLOCK1.Counter", "42", false, null);
            Assert.Equal(JsonValueKind.Null, root.GetProperty("BLOCK1.Missing").ValueKind);
            Assert.Equal(JsonValueKind.Null, root.GetProperty("MISSING.Analog").ValueKind);
            Assert.False(root.TryGetProperty("malformed", out _));
            Assert.False(root.TryGetProperty("", out _));

            using HttpResponseMessage oldResponse = await client.PostAsJsonAsync(
                "/api/dpu/pins/values",
                new { dpu = "DPU1", pinPaths = new[] { "BLOCK1.Analog", "BLOCK1.Gain" } });
            Assert.Equal(HttpStatusCode.OK, oldResponse.StatusCode);
            using JsonDocument oldJson = JsonDocument.Parse(await oldResponse.Content.ReadAsStreamAsync());
            Assert.Equal(JsonValueKind.String, oldJson.RootElement.GetProperty("BLOCK1.Analog").ValueKind);
            Assert.Equal("12.5", oldJson.RootElement.GetProperty("BLOCK1.Analog").GetString());
            Assert.Equal("2.5", oldJson.RootElement.GetProperty("BLOCK1.Gain").GetString());
        }
        finally
        {
            DeleteDataDirectory(dataDirectory);
        }
    }

    [Fact]
    public async Task States_endpoint_uses_command_force_state_during_apply_and_release_windows()
    {
        string dataDirectory = CreateDataDirectory();
        try
        {
            using var host = CreateHost(dataDirectory, out DcsRuntime runtime);
            OnlinePinStateTestFunction function = GetFunction(runtime);
            InitializePinValues(function);
            BlockCommand command = runtime.Dpus[0].FindCommand("BLOCK1")!;

            int port = ReserveTcpPort();
            await using var server = new ApiServer(host, port);
            await server.StartAsync();
            using var handler = new HttpClientHandler { UseProxy = false };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(server.Url) };

            command.SetPinForce("Digital", isForced: true, forceValue: true);
            using (JsonDocument pendingForce = await ReadState(client, "BLOCK1.Digital"))
                AssertPinState(pendingForce.RootElement, "BLOCK1.Digital", "False", true, "True");

            command.Execute();
            using (JsonDocument appliedForce = await ReadState(client, "BLOCK1.Digital"))
                AssertPinState(appliedForce.RootElement, "BLOCK1.Digital", "True", true, "True");

            command.SetPinForce("Digital", isForced: false, forceValue: false);
            using (JsonDocument pendingRelease = await ReadState(client, "BLOCK1.Digital"))
                AssertPinState(pendingRelease.RootElement, "BLOCK1.Digital", "True", false, "True");

            command.Execute();
            using (JsonDocument released = await ReadState(client, "BLOCK1.Digital"))
                AssertPinState(released.RootElement, "BLOCK1.Digital", "False", false, "True");
        }
        finally
        {
            DeleteDataDirectory(dataDirectory);
        }
    }

    [Fact]
    public async Task States_endpoint_rejects_more_than_legacy_batch_limit()
    {
        string dataDirectory = CreateDataDirectory();
        try
        {
            using var host = CreateHost(dataDirectory, out _);
            int port = ReserveTcpPort();
            await using var server = new ApiServer(host, port);
            await server.StartAsync();
            using var handler = new HttpClientHandler { UseProxy = false };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(server.Url) };
            string[] pinPaths = Enumerable.Repeat("BLOCK1.Digital", 10_001).ToArray();

            using HttpResponseMessage response = await client.PostAsJsonAsync(
                "/api/dpu/pins/states",
                new { dpu = "DPU1", pinPaths });

            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);

            string oversizedBody = "{\"dpu\":\"DPU1\",\"pinPaths\":[\""
                + new string('x', 4 * 1024 * 1024)
                + "\"]}";
            using var oversizedContent = new StringContent(
                oversizedBody,
                Encoding.UTF8,
                "application/json");
            using HttpResponseMessage oversizedResponse = await client.PostAsync(
                "/api/dpu/pins/states",
                oversizedContent);
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversizedResponse.StatusCode);
        }
        finally
        {
            DeleteDataDirectory(dataDirectory);
        }
    }

    private static async Task<JsonDocument> ReadState(HttpClient client, string pinPath)
    {
        using HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/dpu/pins/states",
            new { dpu = "DPU1", pinPaths = new[] { pinPath } });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
    }

    private static void AssertPinState(
        JsonElement root,
        string pinPath,
        string? expectedValue,
        bool expectedIsForced,
        string? expectedForceValue)
    {
        JsonElement state = root.GetProperty(pinPath);
        Assert.Equal(
            new[] { "value", "isforced", "forcevalue" },
            state.EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(expectedValue, state.GetProperty("value").GetString());
        Assert.Equal(expectedIsForced, state.GetProperty("isforced").GetBoolean());
        if (expectedForceValue == null)
            Assert.Equal(JsonValueKind.Null, state.GetProperty("forcevalue").ValueKind);
        else
            Assert.Equal(expectedForceValue, state.GetProperty("forcevalue").GetString());
        Assert.False(state.TryGetProperty("isForced", out _));
        Assert.False(state.TryGetProperty("forceValue", out _));
    }

    private static RuntimeHost CreateHost(string dataDirectory, out DcsRuntime runtime)
    {
        EngineeringModel model = BuildModel();
        runtime = RuntimeBuilder.Build(
            model.Clone(),
            new BlockCatalog(typeof(DpuPinStateApiTests).Assembly));
        var host = new RuntimeHost(new RuntimeHostOptions
        {
            BlocksAssembly = typeof(DpuPinStateApiTests).Assembly,
            DataDirectory = dataDirectory,
        });
        typeof(RuntimeHost).GetProperty(nameof(RuntimeHost.Runtime))!.SetValue(host, runtime);
        return host;
    }

    private static EngineeringModel BuildModel() => new()
    {
        ProjectPath = "dpu-pin-state-api-test",
        Controllers =
        [
            new ControllerModel
            {
                Id = 1,
                Address = "1",
                Name = "DPU1",
                Points = [],
                Blocks =
                [
                    new BlockModel
                    {
                        ID = 1,
                        Name = "BLOCK1",
                        FcName = "ONLINE_PIN_STATE_TEST",
                        Pins =
                        [
                            new PinDetailModel { PinName = "Analog", HasDefaultValue = false },
                            new PinDetailModel { PinName = "Digital", HasDefaultValue = false },
                            new PinDetailModel { PinName = "Word", HasDefaultValue = false },
                            new PinDetailModel { PinName = "DWord", HasDefaultValue = false },
                            new PinDetailModel { PinName = "Gain", HasDefaultValue = true, DefaultValue = 2.5f },
                        ],
                    },
                ],
            },
        ],
    };

    private static OnlinePinStateTestFunction GetFunction(DcsRuntime runtime)
        => (OnlinePinStateTestFunction)runtime.Dpus[0].FindCommand("BLOCK1")!.Fc;

    private static void InitializePinValues(OnlinePinStateTestFunction function)
    {
        function.Analog.Value = 12.5f;
        function.Analog.ForceValue = 20f;
        function.Digital.Value = false;
        function.Digital.ForceValue = true;
        function.Word.Value = (ushort)3;
        function.Word.ForceValue = 5;
        function.DWord.Value = 7u;
        function.DWord.ForceValue = 9;
        function.Gain = 2.5f;
        function.Counter = 42;
    }

    private static string CreateDataDirectory() => Path.Combine(
        Path.GetTempPath(),
        $"rwvdcs-dpu-pin-state-api-tests-{Guid.NewGuid():N}");

    private static void DeleteDataDirectory(string dataDirectory)
    {
        if (Directory.Exists(dataDirectory))
            Directory.Delete(dataDirectory, recursive: true);
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

[FCName("ONLINE_PIN_STATE_TEST")]
public sealed class OnlinePinStateTestFunction : Function
{
    [PinType(PinTypes.Input)]
    public LA Analog = new();

    [PinType(PinTypes.Input)]
    public LD Digital = new();

    [PinType(PinTypes.Input)]
    public LP Word = new();

    [PinType(PinTypes.Input)]
    public LP32 DWord = new();

    [PinType(PinTypes.Constant)]
    public float Gain;

    [PinType(PinTypes.Internal)]
    public uint Counter;

    protected override void Run(ICommand cmd)
    {
    }
}
