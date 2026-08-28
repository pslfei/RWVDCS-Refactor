using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using RWVDCS.Api;

namespace RWVDCS.Runtime.Tests;

[CollectionDefinition(CollectionName, DisableParallelization = true)]
public sealed class LegacyRequestLimitCollection
{
    public const string CollectionName = "ApiServer legacy request limits";
}

[Collection(LegacyRequestLimitCollection.CollectionName)]
public sealed class LegacyRequestLimitTests
{
    [Fact]
    public async Task Partial_legacy_write_disconnect_releases_admission_and_keeps_server_available()
    {
        string dataDirectory = Path.Combine(
            Path.GetTempPath(),
            $"rwvdcs-legacy-request-cancel-tests-{Guid.NewGuid():N}");
        try
        {
            using var host = new RuntimeHost(new RuntimeHostOptions
            {
                BlocksAssembly = typeof(LegacyRequestLimitTests).Assembly,
                DataDirectory = dataDirectory,
            });

            int port = ReserveTcpPort();
            await using var server = new ApiServer(host, port);
            await server.StartAsync();

            SemaphoreSlim admission = GetLegacyWriteAdmission();
            int initialCount = admission.CurrentCount;
            Assert.True(initialCount > 0);

            using (var partialClient = new TcpClient())
            {
                await partialClient.ConnectAsync(IPAddress.Loopback, port);
                partialClient.Client.LingerState = new LingerOption(enable: true, seconds: 0);
                NetworkStream stream = partialClient.GetStream();
                byte[] partialRequest = Encoding.ASCII.GetBytes(
                    "POST /api/point/SetVariables HTTP/1.1\r\n"
                    + $"Host: localhost:{port}\r\n"
                    + "Content-Type: application/json\r\n"
                    + "Content-Length: 100000\r\n"
                    + "Connection: close\r\n\r\n"
                    + "{");
                await stream.WriteAsync(partialRequest);
                await stream.FlushAsync();

                Assert.True(SpinWait.SpinUntil(
                    () => admission.CurrentCount == initialCount - 1,
                    TimeSpan.FromSeconds(5)),
                    "Legacy写请求没有进入请求体缓冲阶段");
            }

            Assert.True(SpinWait.SpinUntil(
                () => admission.CurrentCount == initialCount,
                TimeSpan.FromSeconds(5)),
                "客户端断开后Legacy写请求许可没有释放");

            using var handler = new HttpClientHandler { UseProxy = false };
            using var client = new HttpClient(handler) { BaseAddress = new Uri(server.Url) };
            using HttpResponseMessage response = await client.GetAsync("/api/logs?after=0&max=1");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            if (Directory.Exists(dataDirectory))
                Directory.Delete(dataDirectory, recursive: true);
        }
    }

    private static SemaphoreSlim GetLegacyWriteAdmission()
    {
        FieldInfo field = typeof(ApiServer).GetField(
            "LegacyWriteAdmission",
            BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("找不到 ApiServer.LegacyWriteAdmission 字段");
        return (SemaphoreSlim)(field.GetValue(null)
            ?? throw new InvalidOperationException("LegacyWriteAdmission 未初始化"));
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
