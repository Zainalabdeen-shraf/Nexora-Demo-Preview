using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Nexora.Demo.Desktop.Features.NetworkOperations.Connections;

public sealed record RouterOsConnectionOptions(Uri BaseAddress, string Username, string Password)
{
    public static RouterOsConnectionOptions Create(string address, string username, string password)
    {
        if (string.IsNullOrWhiteSpace(address) ||
            !Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri))
        {
            throw new ArgumentException("أدخل عنوانًا كاملًا بصيغة https://عنوان-الراوتر.");
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("الاتصال مقيّد بـ HTTPS لحماية بيانات الدخول؛ لن يُستخدم HTTP غير المشفر.");

        if (!string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) || (uri.AbsolutePath != "/" && uri.AbsolutePath.Length != 0))
        {
            throw new ArgumentException("أدخل عنوان الراوتر فقط مثل https://192.168.88.1، دون بيانات دخول أو مسار إضافي.");
        }

        if (string.IsNullOrWhiteSpace(username))
            throw new ArgumentException("أدخل اسم مستخدم مخصصًا لإدارة الراوتر.");
        if (username.Contains(':'))
            throw new ArgumentException("اسم المستخدم لا يمكن أن يحتوي على النقطتين (:)، لأنها تفصل مكونات Basic Auth.");

        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("أدخل كلمة المرور. لا يُسمح بكلمة مرور فارغة في اختبار الاتصال هذا.");

        var baseAddress = new UriBuilder(uri) { Path = "/", Query = string.Empty, Fragment = string.Empty }.Uri;
        return new RouterOsConnectionOptions(baseAddress, username.Trim(), password);
    }
}

public sealed record RouterOsInterfaceSnapshot(
    string Name,
    string Type,
    bool Running,
    bool Disabled,
    long? RxBytes,
    long? TxBytes,
    long? RxPackets,
    long? TxPackets,
    long? LinkDowns,
    string LastLinkUpTime,
    string LastLinkDownTime);

public sealed record RouterOsNeighborSnapshot(
    string InterfaceName,
    string Address,
    string MacAddress,
    string Identity,
    string Board,
    string Version,
    string DiscoveredBy,
    string Age);

public sealed record RouterOsArpSnapshot(
    string Address,
    string MacAddress,
    string InterfaceName,
    string State);

public sealed record RouterOsConnectionSnapshot(
    string Identity,
    string BoardName,
    string Platform,
    string Version,
    string Uptime,
    string CpuLoad,
    long? TotalMemoryBytes,
    long? FreeMemoryBytes,
    IReadOnlyList<RouterOsInterfaceSnapshot> Interfaces,
    IReadOnlyList<RouterOsNeighborSnapshot> Neighbors,
    IReadOnlyList<RouterOsArpSnapshot> ArpEntries,
    DateTimeOffset CollectedAt);

public sealed class RouterOsConnectionException : Exception
{
    public HttpStatusCode? StatusCode { get; }
    public string UserMessage { get; }

    public RouterOsConnectionException(string userMessage, HttpStatusCode? statusCode = null, Exception? innerException = null)
        : base(userMessage, innerException)
    {
        UserMessage = userMessage;
        StatusCode = statusCode;
    }
}

/// <summary>
/// Read-only RouterOS REST connector. It never disables certificate validation,
/// never persists credentials, and issues no configuration-changing requests.
/// </summary>
public sealed class RouterOsRestClient : IDisposable
{
    private readonly RouterOsConnectionOptions _options;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly string _basicAuthParameter;

    public RouterOsRestClient(RouterOsConnectionOptions options, HttpClient? httpClient = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(12) };
        _basicAuthParameter = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_options.Username}:{_options.Password}"));
    }

    public async Task<RouterOsConnectionSnapshot> ReadDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        var identityJson = await SendAsync(HttpMethod.Get, "rest/system/identity", null, cancellationToken).ConfigureAwait(false);
        var resourceJson = await SendAsync(HttpMethod.Get, "rest/system/resource", null, cancellationToken).ConfigureAwait(false);
        var interfacesJson = await SendAsync(
            HttpMethod.Post,
            "rest/interface/print",
            "{\"stats-detail\":\"\"}",
            cancellationToken).ConfigureAwait(false);
        var neighborsJson = await SendAsync(HttpMethod.Get, "rest/ip/neighbor", null, cancellationToken).ConfigureAwait(false);
        var arpJson = await SendAsync(HttpMethod.Get, "rest/ip/arp", null, cancellationToken).ConfigureAwait(false);

        var identity = ParseFirstObject(identityJson);
        var resource = ParseFirstObject(resourceJson);
        var interfaces = ParseInterfaces(interfacesJson);
        var neighbors = ParseNeighbors(neighborsJson);
        var arpEntries = ParseArpEntries(arpJson);

        var identityName = GetString(identity, "name");
        if (string.IsNullOrWhiteSpace(identityName)) identityName = "غير معروف";

        return new RouterOsConnectionSnapshot(
            identityName,
            GetString(resource, "board-name"),
            GetString(resource, "platform"),
            GetString(resource, "version"),
            GetString(resource, "uptime"),
            GetString(resource, "cpu-load"),
            GetLong(resource, "total-memory"),
            GetLong(resource, "free-memory"),
            interfaces,
            neighbors,
            arpEntries,
            DateTimeOffset.Now);
    }

    public async Task<IReadOnlyList<RouterOsInterfaceSnapshot>> ReadInterfaceStatsAsync(CancellationToken cancellationToken = default)
    {
        var interfacesJson = await SendAsync(
            HttpMethod.Post,
            "rest/interface/print",
            "{\"stats-detail\":\"\"}",
            cancellationToken).ConfigureAwait(false);

        return ParseInterfaces(interfacesJson);
    }
    private async Task<string> SendAsync(HttpMethod method, string relativePath, string? jsonBody, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(_options.BaseAddress, relativePath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _basicAuthParameter);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (jsonBody is not null)
            request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new RouterOsConnectionException("انتهت مهلة الاتصال. تحقق من الوصول إلى الراوتر وخدمة HTTPS.");
        }
        catch (HttpRequestException ex)
        {
            throw new RouterOsConnectionException(
                "تعذر إنشاء اتصال HTTPS موثوق. تحقق من الشبكة، وتفعيل www-ssl، وصحة شهادة TLS وثقتها في Windows.",
                null,
                ex);
        }

        using (response)
        {
            var body = response.Content is null
                ? string.Empty
                : await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var message = response.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "رفض الراوتر بيانات الدخول (401). تحقق من اسم المستخدم وكلمة المرور.",
                    HttpStatusCode.Forbidden => "رفض الراوتر الصلاحيات (403). استخدم حسابًا بصلاحية قراءة الموارد والواجهات.",
                    HttpStatusCode.NotFound => "لم يُعثر على مسار REST. تحقق من إصدار RouterOS وتفعيل خدمة www-ssl.",
                    _ => $"أعاد الراوتر HTTP {(int)response.StatusCode} ({response.StatusCode}). لم يُنفّذ أي أمر تغيير."
                };
                throw new RouterOsConnectionException(message, response.StatusCode);
            }
            return body;
        }
    }

    private static JsonElement ParseFirstObject(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Array)
        {
            var enumerator = document.RootElement.EnumerateArray();
            if (!enumerator.MoveNext())
                throw new RouterOsConnectionException("أعاد الراوتر استجابة فارغة عند طلب معلومات النظام.");
            return enumerator.Current.Clone();
        }

        if (document.RootElement.ValueKind == JsonValueKind.Object)
            return document.RootElement.Clone();

        throw new RouterOsConnectionException("أعاد الراوتر تنسيق JSON غير متوقع.");
    }

    private static IReadOnlyList<RouterOsInterfaceSnapshot> ParseInterfaces(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new RouterOsConnectionException("استجابة الواجهات ليست قائمة JSON كما هو متوقع.");

        var items = new List<RouterOsInterfaceSnapshot>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            items.Add(new RouterOsInterfaceSnapshot(
                GetString(item, "name"),
                GetString(item, "type"),
                GetBoolean(item, "running"),
                GetBoolean(item, "disabled"),
                GetLong(item, "rx-byte"),
                GetLong(item, "tx-byte"),
                GetLong(item, "rx-packet"),
                GetLong(item, "tx-packet"),
                GetLong(item, "link-downs"),
                GetString(item, "last-link-up-time"),
                GetString(item, "last-link-down-time")));
        }
        return items;
    }

    private static IReadOnlyList<RouterOsNeighborSnapshot> ParseNeighbors(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new RouterOsConnectionException("استجابة الجيران من RouterOS ليست قائمة JSON كما هو متوقع.");

        var items = new List<RouterOsNeighborSnapshot>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            items.Add(new RouterOsNeighborSnapshot(
                GetFirstString(item, "interface"),
                GetFirstString(item, "address", "address4"),
                GetFirstString(item, "mac-address", "mac-addresses"),
                GetString(item, "identity"),
                GetString(item, "board"),
                GetString(item, "version"),
                GetString(item, "discovered-by"),
                GetString(item, "age")));
        }
        return items;
    }

    private static IReadOnlyList<RouterOsArpSnapshot> ParseArpEntries(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            throw new RouterOsConnectionException("استجابة ARP من RouterOS ليست قائمة JSON كما هو متوقع.");

        var items = new List<RouterOsArpSnapshot>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var state = GetString(item, "invalid") == "true" ? "غير صالح"
                : GetString(item, "disabled") == "true" ? "معطل"
                : GetString(item, "dynamic") == "true" ? "ديناميكي"
                : GetString(item, "dynamic") == "false" ? "ثابت / غير ديناميكي"
                : "غير محدد";

            items.Add(new RouterOsArpSnapshot(
                GetString(item, "address"),
                GetString(item, "mac-address"),
                GetString(item, "interface"),
                state));
        }
        return items;
    }

    private static string GetFirstString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            var value = GetString(element, name);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return string.Empty;
    }
    private static string GetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value)) return string.Empty;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => string.Empty
        };
    }

    private static long? GetLong(JsonElement element, string name)
    {
        var value = GetString(element, name);
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static bool GetBoolean(JsonElement element, string name) =>
        bool.TryParse(GetString(element, name), out var value) && value;

    public void Dispose()
    {
        if (_ownsHttpClient) _httpClient.Dispose();
    }
}


