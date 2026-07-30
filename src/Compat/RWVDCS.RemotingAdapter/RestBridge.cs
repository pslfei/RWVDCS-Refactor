using System;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RWVDCS.RemotingAdapter
{
    /// <summary>
    /// REST 桥：同步封装新系统 HTTP API。
    /// Remoting 服务方法本身是同步调用语义，这里直接同步等待，超时由 HttpClient 控制。
    /// </summary>
    internal sealed class RestBridge : IDisposable
    {
        private readonly HttpClient _http;
        private readonly TimeSpan _timeout;

        public RestBridge(string baseUrl, TimeSpan? timeout = null)
        {
            _timeout = timeout ?? TimeSpan.FromSeconds(60);
            _http = new HttpClient
            {
                BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/api/"),
                Timeout = _timeout,
            };
        }

        public string BaseUrl => _http.BaseAddress.ToString();

        public JsonDocument Get(string path)
            => Send("GET", path, null);

        public JsonDocument Post(string path, object body = null)
            => Send("POST", path, body);

        public JsonDocument Put(string path, object body)
            => Send("PUT", path, body);

        private JsonDocument Send(string method, string path, object body)
        {
            try
            {
                HttpResponseMessage response;
                switch (method)
                {
                    case "GET":
                        response = _http.GetAsync(path).GetAwaiter().GetResult();
                        break;
                    case "POST":
                        response = _http.PostAsync(path, Content(body)).GetAwaiter().GetResult();
                        break;
                    case "PUT":
                        response = _http.PutAsync(path, Content(body)).GetAwaiter().GetResult();
                        break;
                    default:
                        throw new InvalidOperationException("不支持的 HTTP 方法：" + method);
                }
                return Parse(response);
            }
            catch (TaskCanceledException ex)
            {
                throw new TimeoutException(
                    $"{method} {_http.BaseAddress}{path} 超时（{_timeout.TotalSeconds:F0}s）。请检查 Web Host 是否可达、是否已装载工程，或调大适配器 --timeout。", ex);
            }
        }

        /// <summary>探活：新系统是否可达且已装载工程。</summary>
        public bool TryGetStatus(out string project, out string runState)
        {
            project = null;
            runState = null;
            try
            {
                using (var doc = Get("status"))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("project", out var proj) && proj.ValueKind == JsonValueKind.Object)
                        project = proj.GetProperty("mdbPath").GetString();
                    runState = root.GetProperty("run").GetProperty("state").GetString();
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        private static StringContent Content(object body) => body == null
            ? null
            : new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        private static JsonDocument Parse(HttpResponseMessage resp)
        {
            string text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode)
            {
                string message = text;
                try
                {
                    using (var err = JsonDocument.Parse(text))
                    {
                        if (err.RootElement.TryGetProperty("error", out var e))
                            message = e.GetString() ?? text;
                    }
                }
                catch
                {
                    // 非 JSON 错误体
                }
                throw new InvalidOperationException($"API {(int)resp.StatusCode}: {message}");
            }
            return JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
        }

        /// <summary>
        /// JSON 值 → 与老系统 RTD 装箱类型一致的 object。
        /// valueType 来自 /values/describe（Single/Boolean/Byte/UInt16/UInt32/Int32/...）。
        /// </summary>
        public static object FromJson(JsonElement e, string valueType)
        {
            if (e.ValueKind == JsonValueKind.Null || e.ValueKind == JsonValueKind.Undefined)
                return null;
            switch (valueType)
            {
                case "Single":
                    return e.ValueKind == JsonValueKind.Number ? e.GetSingle()
                        : Convert.ToSingle(e.ToString(), CultureInfo.InvariantCulture);
                case "Double":
                    return e.ValueKind == JsonValueKind.Number ? e.GetDouble()
                        : Convert.ToDouble(e.ToString(), CultureInfo.InvariantCulture);
                case "Boolean":
                    if (e.ValueKind == JsonValueKind.True) return true;
                    if (e.ValueKind == JsonValueKind.False) return false;
                    if (e.ValueKind == JsonValueKind.Number) return e.GetDouble() != 0;
                    return bool.Parse(e.ToString());
                case "Byte":
                    return e.ValueKind == JsonValueKind.Number ? e.GetByte()
                        : e.ValueKind == JsonValueKind.True ? (byte)1
                        : e.ValueKind == JsonValueKind.False ? (byte)0
                        : byte.Parse(e.ToString(), CultureInfo.InvariantCulture);
                case "UInt16":
                    return e.ValueKind == JsonValueKind.Number ? e.GetUInt16()
                        : ushort.Parse(e.ToString(), CultureInfo.InvariantCulture);
                case "UInt32":
                    return e.ValueKind == JsonValueKind.Number ? e.GetUInt32()
                        : uint.Parse(e.ToString(), CultureInfo.InvariantCulture);
                case "Int32":
                    return e.ValueKind == JsonValueKind.Number ? e.GetInt32()
                        : int.Parse(e.ToString(), CultureInfo.InvariantCulture);
                case "Int64":
                    return e.ValueKind == JsonValueKind.Number ? e.GetInt64()
                        : long.Parse(e.ToString(), CultureInfo.InvariantCulture);
                default:
                    // 未定型：按 JSON 本身的形态装箱（number→double / bool / string）
                    switch (e.ValueKind)
                    {
                        case JsonValueKind.Number: return e.GetDouble();
                        case JsonValueKind.True: return true;
                        case JsonValueKind.False: return false;
                        case JsonValueKind.String: return e.GetString();
                        default: return e.ToString();
                    }
            }
        }

        /// <summary>老客户端 SetValue 的 object → REST 文本值。</summary>
        public static string ToWireText(object value)
        {
            if (value == null)
                return "";
            if (value is bool b)
                return b ? "True" : "False";
            if (value is float f)
                return f.ToString("R", CultureInfo.InvariantCulture);
            if (value is double d)
                return d.ToString("R", CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        public void Dispose() => _http.Dispose();
    }
}
