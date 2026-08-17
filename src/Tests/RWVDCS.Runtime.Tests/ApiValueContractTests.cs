using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using RWVDCS.Api;
using RWVDCS.Core.Blocks;
using RWVDCS.Engineering;
using RWVDCS.Runtime;

namespace RWVDCS.Runtime.Tests;

public sealed class ApiValueContractTests
{
    [Fact]
    public async Task Value_endpoint_returns_single_computed_over_state_for_la_points()
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
                    Blocks = [],
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
