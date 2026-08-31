using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using RWVDCS.Api;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using RWVDCS.Engineering;
using RWVDCS.Runtime;

namespace RWVDCS.Runtime.Tests;

[Collection(LegacyRequestLimitCollection.CollectionName)]
public sealed class FcPinValueApiTests
{
    [Fact]
    public async Task Endpoint_updates_constant_and_unconnected_input_and_rejects_unsupported_pins()
    {
        string dataDirectory = Path.Combine(
            Path.GetTempPath(),
            $"rwvdcs-fc-pin-api-tests-{Guid.NewGuid():N}");
        var store = new FakeFcPinValueStore(constantValue: "1", inputValue: "0");
        try
        {
            EngineeringModel model = BuildModel();
            DcsRuntime runtime = RuntimeBuilder.Build(
                model.Clone(),
                new BlockCatalog(typeof(FcPinValueApiTests).Assembly));
            using var host = new RuntimeHost(new RuntimeHostOptions
            {
                BlocksAssembly = typeof(FcPinValueApiTests).Assembly,
                DataDirectory = dataDirectory,
                FcPinValueStore = store,
            });
            InstallRuntimeAndMetadata(host, runtime, model);

            int port = ReserveTcpPort();
            await using var server = new ApiServer(host, port);
            await server.StartAsync();
            using var handler = new HttpClientHandler { UseProxy = false };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(server.Url) };

            using HttpResponseMessage constantSuccess = await client.PutAsJsonAsync(
                "/api/engineering/fc-pin/value",
                new { dpuName = "dpu1", algName = "block1", pinName = "gain", pValue = "3.5" });
            Assert.Equal(HttpStatusCode.OK, constantSuccess.StatusCode);
            using (JsonDocument json = JsonDocument.Parse(await constantSuccess.Content.ReadAsStreamAsync()))
            {
                Assert.True(json.RootElement.GetProperty("success").GetBoolean());
                Assert.Equal(9001, json.RootElement.GetProperty("cldFCBlockId").GetInt32());
                Assert.Equal(7001, json.RootElement.GetProperty("databaseRecordId").GetInt32());
                Assert.Equal("Constant", json.RootElement.GetProperty("pinType").GetString());
                Assert.Equal(Path.GetFullPath("fc-pin-api-test.mdb"), json.RootElement.GetProperty("mdbPath").GetString());
                Assert.Equal("Cld_FCParameter", json.RootElement.GetProperty("databaseTable").GetString());
                Assert.Equal("PValue", json.RootElement.GetProperty("databaseColumn").GetString());
                Assert.Equal("3.5", json.RootElement.GetProperty("persistedDatabaseValue").GetString());
                Assert.True(json.RootElement.GetProperty("databaseVerified").GetBoolean());
                Assert.Equal(3.5f, json.RootElement.GetProperty("newRuntimeValue").GetSingle());
            }
            Assert.Equal(9001, store.LastBlockId);
            Assert.Equal("Gain", store.LastPinName);
            Assert.Equal("3.5", store.ConstantValue);
            Assert.Equal(3.5f, GetFunction(runtime).Gain);
            Assert.Equal(3.5f, model.Controllers[0].Blocks[0].FindPin("Gain")!.DefaultValue);

            OnlinePinValueTestFunction function = GetFunction(runtime);
            function.Enable.Quality = (QualityTypes)7;
            function.Enable.IsAlarm = true;
            using HttpResponseMessage inputSuccess = await client.PutAsJsonAsync(
                "/api/engineering/fc-pin/value",
                new { dpuName = "DPU1", algName = "BLOCK1", pinName = "enable", pValue = "1" });
            Assert.Equal(HttpStatusCode.OK, inputSuccess.StatusCode);
            using (JsonDocument json = JsonDocument.Parse(await inputSuccess.Content.ReadAsStreamAsync()))
            {
                Assert.Equal(8001, json.RootElement.GetProperty("databaseRecordId").GetInt32());
                Assert.Equal("Input", json.RootElement.GetProperty("pinType").GetString());
                Assert.Equal("Cld_FCInput", json.RootElement.GetProperty("databaseTable").GetString());
                Assert.Equal("InitialValue", json.RootElement.GetProperty("databaseColumn").GetString());
                Assert.Equal(string.Empty, json.RootElement.GetProperty("pointName").GetString());
                Assert.Equal("1", json.RootElement.GetProperty("persistedDatabaseValue").GetString());
                Assert.True(json.RootElement.GetProperty("databaseVerified").GetBoolean());
                Assert.True(json.RootElement.GetProperty("newRuntimeValue").GetBoolean());
            }
            Assert.Equal("1", store.InputValue);
            Assert.True(function.Enable.Value is true);
            Assert.Equal((QualityTypes)7, function.Enable.Quality);
            Assert.True(function.Enable.IsAlarm);
            Assert.Equal(true, model.Controllers[0].Blocks[0].FindPin("Enable")!.DefaultValue);

            int beginCountAfterSuccess = store.BeginCount;
            using HttpResponseMessage connectedInput = await client.PutAsJsonAsync(
                "/api/engineering/fc-pin/value",
                new { dpuName = "DPU1", algName = "BLOCK1", pinName = "Connected", pValue = "1" });
            Assert.Equal(HttpStatusCode.BadRequest, connectedInput.StatusCode);
            Assert.Equal(beginCountAfterSuccess, store.BeginCount);

            using HttpResponseMessage output = await client.PutAsJsonAsync(
                "/api/engineering/fc-pin/value",
                new { dpuName = "DPU1", algName = "BLOCK1", pinName = "Output", pValue = "9" });
            Assert.Equal(HttpStatusCode.BadRequest, output.StatusCode);
            Assert.Equal(beginCountAfterSuccess, store.BeginCount);

            using HttpResponseMessage badInputValue = await client.PutAsJsonAsync(
                "/api/engineering/fc-pin/value",
                new { dpuName = "DPU1", algName = "BLOCK1", pinName = "Enable", pValue = "invalid" });
            Assert.Equal(HttpStatusCode.BadRequest, badInputValue.StatusCode);
            Assert.Equal(beginCountAfterSuccess, store.BeginCount);

            using HttpResponseMessage missingBlock = await client.PutAsJsonAsync(
                "/api/engineering/fc-pin/value",
                new { dpuName = "DPU1", algName = "MISSING", pinName = "Gain", pValue = "2" });
            Assert.Equal(HttpStatusCode.NotFound, missingBlock.StatusCode);
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
                Directory.Delete(dataDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Endpoint_rolls_back_runtime_and_model_when_database_commit_fails()
    {
        string dataDirectory = Path.Combine(
            Path.GetTempPath(),
            $"rwvdcs-fc-pin-rollback-tests-{Guid.NewGuid():N}");
        var store = new FakeFcPinValueStore(constantValue: "1", inputValue: "0")
        {
            ThrowOnCommit = true,
        };
        try
        {
            EngineeringModel model = BuildModel();
            DcsRuntime runtime = RuntimeBuilder.Build(
                model.Clone(),
                new BlockCatalog(typeof(FcPinValueApiTests).Assembly));
            using var host = new RuntimeHost(new RuntimeHostOptions
            {
                BlocksAssembly = typeof(FcPinValueApiTests).Assembly,
                DataDirectory = dataDirectory,
                FcPinValueStore = store,
            });
            InstallRuntimeAndMetadata(host, runtime, model);

            OnlinePinValueTestFunction function = GetFunction(runtime);
            function.Enable.Quality = (QualityTypes)5;
            function.Enable.IsAlarm = true;

            int port = ReserveTcpPort();
            await using var server = new ApiServer(host, port);
            await server.StartAsync();
            using var handler = new HttpClientHandler { UseProxy = false };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(server.Url) };

            using HttpResponseMessage commitFailure = await client.PutAsJsonAsync(
                "/api/engineering/fc-pin/value",
                new { dpuName = "DPU1", algName = "BLOCK1", pinName = "Enable", pValue = "1" });
            Assert.Equal(HttpStatusCode.InternalServerError, commitFailure.StatusCode);
            Assert.Equal("0", store.InputValue);
            Assert.False((bool)function.Enable.Value);
            Assert.Equal((QualityTypes)5, function.Enable.Quality);
            Assert.True(function.Enable.IsAlarm);
            Assert.Equal(false, model.Controllers[0].Blocks[0].FindPin("Enable")!.DefaultValue);
            Assert.True(store.LastUpdateDisposedWithoutCommit);
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
                Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private static OnlinePinValueTestFunction GetFunction(DcsRuntime runtime)
        => (OnlinePinValueTestFunction)runtime.Dpus[0].FindCommand("BLOCK1")!.Fc;

    private static EngineeringModel BuildModel() => new()
    {
        ProjectPath = "fc-pin-api-test.mdb",
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
                        ID = 1001,
                        Name = "DI001",
                        DataType = "LD",
                        DefaultValue = false,
                    },
                ],
                Blocks =
                [
                    new BlockModel
                    {
                        ID = 9001,
                        Name = "BLOCK1",
                        FcName = "ONLINE_PIN_VALUE_TEST",
                        Pins =
                        [
                            new PinDetailModel
                            {
                                PinName = "Gain",
                                HasDefaultValue = true,
                                DefaultValue = 1f,
                            },
                            new PinDetailModel
                            {
                                PinName = "Enable",
                                PointName = "",
                                HasDefaultValue = true,
                                DefaultValue = false,
                            },
                            new PinDetailModel
                            {
                                PinName = "Connected",
                                PointName = "DI001",
                                HasDefaultValue = true,
                                DefaultValue = false,
                            },
                            new PinDetailModel
                            {
                                PinName = "Output",
                                HasDefaultValue = false,
                            },
                        ],
                    },
                ],
            },
        ],
    };

    private static void InstallRuntimeAndMetadata(
        RuntimeHost host,
        DcsRuntime runtime,
        EngineeringModel model)
    {
        SetProperty(host, nameof(RuntimeHost.Runtime), runtime);
        SetProperty(host, nameof(RuntimeHost.PristineModel), model);
        SetProperty(host, nameof(RuntimeHost.MdbPath), "fc-pin-api-test.mdb");
        SetProperty(host, nameof(RuntimeHost.Fingerprint), ProjectFingerprint.Compute(model));
        SetPrivateField(
            host,
            "_blockMetadataByDpu",
            (IReadOnlyDictionary<string, IReadOnlyDictionary<string, BlockModel>>)
            new Dictionary<string, IReadOnlyDictionary<string, BlockModel>>(StringComparer.OrdinalIgnoreCase)
            {
                ["DPU1"] = model.Controllers[0].Blocks.ToDictionary(
                    block => block.Name,
                    StringComparer.OrdinalIgnoreCase),
            });
    }

    private static void SetProperty(RuntimeHost host, string name, object value)
    {
        PropertyInfo property = typeof(RuntimeHost).GetProperty(name)
            ?? throw new InvalidOperationException($"找不到 RuntimeHost.{name} 属性");
        property.SetValue(host, value);
    }

    private static void SetPrivateField(RuntimeHost host, string name, object value)
    {
        FieldInfo field = typeof(RuntimeHost).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"找不到 RuntimeHost.{name} 字段");
        field.SetValue(host, value);
    }

    private static int ReserveTcpPort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class FakeFcPinValueStore(string constantValue, string inputValue) : IFcPinValueStore
    {
        public string ConstantValue { get; private set; } = constantValue;
        public string InputValue { get; private set; } = inputValue;
        public int BeginCount { get; private set; }
        public int LastBlockId { get; private set; }
        public string LastPinName { get; private set; } = "";
        public bool ThrowOnCommit { get; set; }
        public bool LastUpdateDisposedWithoutCommit { get; private set; }

        public IFcPinValueUpdate BeginConstantUpdate(
            string mdbPath,
            int cldFcBlockId,
            string pinName,
            string newValue)
            => BeginUpdate(mdbPath, cldFcBlockId, pinName, newValue, isInput: false);

        public IFcPinValueUpdate BeginInputUpdate(
            string mdbPath,
            int cldFcBlockId,
            string pinName,
            string newValue)
            => BeginUpdate(mdbPath, cldFcBlockId, pinName, newValue, isInput: true);

        private IFcPinValueUpdate BeginUpdate(
            string mdbPath,
            int cldFcBlockId,
            string pinName,
            string newValue,
            bool isInput)
        {
            BeginCount++;
            LastBlockId = cldFcBlockId;
            LastPinName = pinName;
            LastUpdateDisposedWithoutCommit = false;
            return new FakeUpdate(
                this,
                isInput ? InputValue : ConstantValue,
                newValue,
                isInput,
                Path.GetFullPath(mdbPath));
        }

        private sealed class FakeUpdate(
            FakeFcPinValueStore owner,
            string oldValue,
            string newValue,
            bool isInput,
            string mdbPath) : IFcPinValueUpdate
        {
            private bool _committed;

            public int RecordId => isInput ? 8001 : 7001;
            public string OldValue => oldValue;
            public string? PointName => isInput ? string.Empty : null;
            public string MdbPath => mdbPath;
            public string DatabaseTable => isInput ? "Cld_FCInput" : "Cld_FCParameter";
            public string DatabaseColumn => isInput ? "InitialValue" : "PValue";
            public string RequestedValue => newValue;
            public string? PersistedValue { get; private set; }
            public bool DatabaseVerified { get; private set; }
            public bool CommitSucceeded { get; private set; }
            public bool DatabaseRestored => false;

            public void Commit()
            {
                if (owner.ThrowOnCommit)
                    throw new IOException("模拟 MDB 提交失败");
                if (isInput)
                    owner.InputValue = newValue;
                else
                    owner.ConstantValue = newValue;
                PersistedValue = newValue;
                DatabaseVerified = true;
                CommitSucceeded = true;
                _committed = true;
            }

            public void Dispose()
            {
                if (!_committed)
                    owner.LastUpdateDisposedWithoutCommit = true;
            }
        }
    }
}

[FCName("ONLINE_PIN_VALUE_TEST")]
public sealed class OnlinePinValueTestFunction : Function
{
    [PinType(PinTypes.Constant)]
    public float Gain = 1f;

    [PinType(PinTypes.Input)]
    public LD Enable = new();

    [PinType(PinTypes.Input)]
    public LD Connected = new();

    [PinType(PinTypes.Output)]
    public LA Output = new();

    protected override void Run(ICommand cmd)
    {
    }
}
