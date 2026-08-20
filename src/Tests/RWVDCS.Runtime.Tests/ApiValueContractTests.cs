using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using RWVDCS.Api;
using RWVDCS.Core.Blocks;
using RWVDCS.Core.Types;
using RWVDCS.Engineering;
using RWVDCS.Runtime;

namespace RWVDCS.Runtime.Tests;

public sealed class ApiValueContractTests
{
    [Fact]
    public async Task Value_points_and_blocks_endpoints_return_engineering_metadata_and_compatibility_status_fields()
    {
        string dataDirectory = Path.Combine(
            Path.GetTempPath(),
            $"rwvdcs-api-value-tests-{Guid.NewGuid():N}");
        try
        {
            using var host = new RuntimeHost(new RuntimeHostOptions
            {
                BlocksAssembly = typeof(ApiValueContractTests).Assembly,
                DataDirectory = dataDirectory,
            });
            EngineeringModel model = BuildEngineeringModel();
            SetRuntime(host, BuildRuntime(model), model);

            int port = ReserveTcpPort();
            await using var server = new ApiServer(host, port);
            await server.StartAsync();

            using var handler = new HttpClientHandler { UseProxy = false };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(server.Url) };
            using HttpResponseMessage response = await client.GetAsync(
                "/api/value?names=AI001,DPU1.AI001,DI001");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using JsonDocument json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
            JsonElement root = json.RootElement;
            Assert.Equal(3, root.GetProperty("count").GetInt32());

            JsonElement values = root.GetProperty("values");
            AssertCurOverState(values[0], expected: 6);
            AssertCurOverState(values[1], expected: 6);
            AssertAlarmLimitsAreEngineeringMetadataOnly(values[0]);
            AssertAlarmLimitsAreEngineeringMetadataOnly(values[1]);
            AssertPointEngineeringMetadata(values[0]);
            AssertPointEngineeringMetadata(values[1]);
            Assert.DoesNotContain(
                values[2].GetProperty("members").EnumerateArray(),
                member => member.GetProperty("name").GetString() == "CurOverState");

            string[] requestedFields =
            [
                "AI001.CurOverState",
                "AI001.cUrOvErStAtE",
                "AI001.dataQuality",
                "AI001.DATAQUALITY",
            ];
            using HttpResponseMessage compatResponse = await client.PostAsJsonAsync(
                "/api/point/GetPointValues",
                requestedFields);

            Assert.Equal(HttpStatusCode.OK, compatResponse.StatusCode);
            using JsonDocument compatJson = JsonDocument.Parse(
                await compatResponse.Content.ReadAsStreamAsync());
            JsonElement compatValues = compatJson.RootElement;
            Assert.Equal("6", compatValues.GetProperty("AI001.CurOverState").GetString());
            Assert.Equal("6", compatValues.GetProperty("AI001.cUrOvErStAtE").GetString());
            Assert.Equal("1", compatValues.GetProperty("AI001.dataQuality").GetString());
            Assert.Equal("1", compatValues.GetProperty("AI001.DATAQUALITY").GetString());

            using HttpResponseMessage pointsResponse = await client.GetAsync(
                "/api/points?dpu=DPU1&page=1&pageSize=50");

            Assert.Equal(HttpStatusCode.OK, pointsResponse.StatusCode);
            using JsonDocument pointsJson = JsonDocument.Parse(
                await pointsResponse.Content.ReadAsStreamAsync());
            JsonElement pointItem = Assert.Single(
                pointsJson.RootElement.GetProperty("items").EnumerateArray(),
                item => item.GetProperty("name").GetString() == "AI001");
            Assert.Equal("给水流量", pointItem.GetProperty("description").GetString());

            JsonElement pointWithoutDescription = Assert.Single(
                pointsJson.RootElement.GetProperty("items").EnumerateArray(),
                item => item.GetProperty("name").GetString() == "DI001");
            Assert.Equal(
                JsonValueKind.Null,
                pointWithoutDescription.GetProperty("description").ValueKind);

            using HttpResponseMessage blocksResponse = await client.GetAsync(
                "/api/blocks?dpu=DPU1&page=1&pageSize=50");

            Assert.Equal(HttpStatusCode.OK, blocksResponse.StatusCode);
            using JsonDocument blocksJson = JsonDocument.Parse(
                await blocksResponse.Content.ReadAsStreamAsync());
            JsonElement blockItem = Assert.Single(
                blocksJson.RootElement.GetProperty("items").EnumerateArray(),
                item => item.GetProperty("name").GetString() == "BLOCK001");
            Assert.Equal("功能块描述", blockItem.GetProperty("description").GetString());

            using HttpResponseMessage blockResponse = await client.GetAsync(
                "/api/block/DPU1/BLOCK001");

            Assert.Equal(HttpStatusCode.OK, blockResponse.StatusCode);
            using JsonDocument blockJson = JsonDocument.Parse(
                await blockResponse.Content.ReadAsStreamAsync());
            JsonElement block = blockJson.RootElement;
            Assert.Equal("功能块描述", block.GetProperty("description").GetString());
            AssertBlockMemberDescription(block, "inputs", "pin", "Input", "输入管脚描述");
            AssertBlockMemberDescription(block, "outputs", "pin", "Output", "输出管脚描述");
            AssertBlockMemberDescription(block, "constants", "name", "Gain", "规格数描述");
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
                Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private static void AssertPointEngineeringMetadata(JsonElement point)
    {
        JsonElement[] members = point.GetProperty("members").EnumerateArray().ToArray();
        AssertEngineeringMember(members, "ID", 101);
        AssertEngineeringMember(members, "LowAlarm1Priority", 1);
        AssertEngineeringMember(members, "LowAlarm2Priority", 2);
        AssertEngineeringMember(members, "LowAlarm3Priority", 3);
        AssertEngineeringMember(members, "HighAlarm1Priority", 3);
        AssertEngineeringMember(members, "HighAlarm2Priority", 2);
        AssertEngineeringMember(members, "HighAlarm3Priority", 1);

        JsonElement dpuNo = Assert.Single(
            members,
            member => member.GetProperty("name").GetString() == "dpuNO");
        Assert.Equal(JsonValueKind.String, dpuNo.GetProperty("value").ValueKind);
        Assert.Equal("1", dpuNo.GetProperty("value").GetString());
        Assert.Equal(-1, dpuNo.GetProperty("fsid").GetInt64());
        Assert.DoesNotContain(
            members,
            member => member.GetProperty("name").GetString() == "ControllerAddress");
    }

    private static void AssertBlockMemberDescription(
        JsonElement block,
        string collectionName,
        string memberNameProperty,
        string memberName,
        string expectedDescription)
    {
        JsonElement member = Assert.Single(
            block.GetProperty(collectionName).EnumerateArray(),
            item => item.GetProperty(memberNameProperty).GetString() == memberName);
        Assert.Equal(expectedDescription, member.GetProperty("description").GetString());
    }

    private static void AssertEngineeringMember(JsonElement[] members, string name, int expected)
    {
        JsonElement member = Assert.Single(
            members,
            candidate => candidate.GetProperty("name").GetString() == name);
        Assert.Equal(JsonValueKind.Number, member.GetProperty("value").ValueKind);
        Assert.Equal(expected, member.GetProperty("value").GetInt32());
        Assert.Equal(-1, member.GetProperty("fsid").GetInt64());
    }

    private static void AssertCurOverState(JsonElement point, int expected)
    {
        JsonElement[] states = point.GetProperty("members")
            .EnumerateArray()
            .Where(member => member.GetProperty("name").GetString() == "CurOverState")
            .ToArray();

        JsonElement state = Assert.Single(states);
        Assert.Equal(JsonValueKind.Number, state.GetProperty("value").ValueKind);
        Assert.Equal(expected, state.GetProperty("value").GetInt32());
        Assert.True(state.GetProperty("fsid").GetInt64() > 0);
    }

    private static void AssertAlarmLimitsAreEngineeringMetadataOnly(JsonElement point)
    {
        JsonElement[] members = point.GetProperty("members").EnumerateArray().ToArray();
        string[] engineeringNames =
        [
            "HighAlarmLimit3Value",
            "HighAlarmLimit2Value",
            "HighAlarmLimit1Value",
            "LowAlarmLimit3Value",
            "LowAlarmLimit2Value",
            "LowAlarmLimit1Value",
        ];

        foreach (string name in engineeringNames)
        {
            JsonElement member = Assert.Single(
                members,
                candidate => candidate.GetProperty("name").GetString() == name);
            Assert.Equal(-1, member.GetProperty("fsid").GetInt64());

            string internalName = char.ToLowerInvariant(name[0]) + name[1..];
            Assert.DoesNotContain(
                members,
                candidate => candidate.GetProperty("name").GetString() == internalName);
        }
    }

    private static EngineeringModel BuildEngineeringModel()
    {
        return new EngineeringModel
        {
            ProjectPath = "api-value-contract-test",
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
                            ID = 101,
                            Name = "AI001",
                            DataType = "LA",
                            Description = "给水流量",
                            DefaultValue = 95f,
                            MaxValue = 1000f,
                            MinValue = -1000f,
                            LowAlarm1Priority = 1,
                            LowAlarm2Priority = 2,
                            LowAlarm3Priority = 3,
                            HighAlarm1Priority = 3,
                            HighAlarm2Priority = 2,
                            HighAlarm3Priority = 1,
                            HighAlarmLimit3Value = 90d,
                            HighAlarmLimit2Value = 80d,
                            HighAlarmLimit1Value = 70d,
                            LowAlarmLimit3Value = 10d,
                            LowAlarmLimit2Value = 20d,
                            LowAlarmLimit1Value = 30d,
                        },
                        new PointModel
                        {
                            ID = 102,
                            Name = "DI001",
                            DataType = "LD",
                            DefaultValue = false,
                        },
                    ],
                    Blocks =
                    [
                        new BlockModel
                        {
                            Name = "BLOCK001",
                            FcName = "API_METADATA_TEST",
                            Description = "功能块描述",
                            Pins =
                            [
                                new PinDetailModel
                                {
                                    PinName = "Input",
                                    Description = "输入管脚描述",
                                    HasDefaultValue = false,
                                },
                                new PinDetailModel
                                {
                                    PinName = "Output",
                                    Description = "输出管脚描述",
                                    HasDefaultValue = false,
                                },
                                new PinDetailModel
                                {
                                    PinName = "Gain",
                                    Description = "规格数描述",
                                    HasDefaultValue = true,
                                    DefaultValue = 2f,
                                },
                            ],
                        },
                    ],
                },
            ],
        };
    }

    private static DcsRuntime BuildRuntime(EngineeringModel model) => RuntimeBuilder.Build(
        model.Clone(),
        new BlockCatalog(typeof(ApiValueContractTests).Assembly));

    private static void SetRuntime(RuntimeHost host, DcsRuntime runtime, EngineeringModel model)
    {
        PropertyInfo runtimeProperty = typeof(RuntimeHost).GetProperty(nameof(RuntimeHost.Runtime))
            ?? throw new InvalidOperationException("找不到 RuntimeHost.Runtime 属性");
        runtimeProperty.SetValue(host, runtime);

        var pointsByDpu = model.Controllers.ToDictionary(
            controller => controller.Name,
            controller => (IReadOnlyDictionary<string, PointModel>)controller.Points.ToDictionary(
                point => point.Name,
                StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        SetPrivateField(host, "_pointMetadataByDpu", pointsByDpu);
        var blocksByDpu = model.Controllers.ToDictionary(
            controller => controller.Name,
            controller => (IReadOnlyDictionary<string, BlockModel>)controller.Blocks.ToDictionary(
                block => block.Name,
                StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
        SetPrivateField(host, "_blockMetadataByDpu", blocksByDpu);
        SetPrivateField(
            host,
            "_controllerMetadataById",
            (IReadOnlyDictionary<int, ControllerModel>)model.Controllers.ToDictionary(controller => controller.Id));
    }

    private static void SetPrivateField(RuntimeHost host, string name, object value)
    {
        FieldInfo field = typeof(RuntimeHost).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"找不到 RuntimeHost.{name} 字段");
        field.SetValue(host, value);
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

[FCName("API_METADATA_TEST")]
public sealed class ApiMetadataTestFunction : Function
{
    [PinType(PinTypes.Input)]
    public LA Input = new();

    [PinType(PinTypes.Output)]
    public LA Output = new();

    [PinType(PinTypes.Constant)]
    public float Gain;

    protected override void Run(ICommand cmd)
    {
    }
}
