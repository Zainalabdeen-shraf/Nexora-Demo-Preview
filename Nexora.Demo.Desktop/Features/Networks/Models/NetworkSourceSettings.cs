namespace Nexora.Demo.Desktop.Features.Networks.Models;

public enum RouterConnectionMethod
{
    RouterOsApi,
    RouterOsApiSsl,
    RestHttp,
    RestHttps
}

public enum HotSpotAuthenticationMode
{
    LocalUsers,
    Radius
}

public enum RadiusTransportProtocol
{
    Udp,
    RadSec
}

public sealed class NetworkSourceSettingsSnapshot
{
    public int SchemaVersion { get; set; } = 1;

    public List<RouterConnectionProfile> Routers { get; set; } = [];

    public HotSpotSourceSettings HotSpot { get; set; } = new();

    public UserManagerSourceSettings UserManager { get; set; } = new();
}

public sealed class RouterConnectionProfile
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "الراوتر الرئيسي";

    public string HostAddress { get; set; } = string.Empty;

    public RouterConnectionMethod Method { get; set; } =
        RouterConnectionMethod.RouterOsApiSsl;

    public int Port { get; set; } = 8729;

    public string Username { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public string Comment { get; set; } = string.Empty;

    public string InterfaceName { get; set; } = "bridge-hotspot";

    public string AddressPoolName { get; set; } = "hs-pool-1";

    public string DnsName { get; set; } = "login.nexora.local";

    public bool UseCookie { get; set; } = true;

    public int CookieTimeoutMinutes { get; set; } = 1440;

    public int ClientIdleTimeoutSeconds { get; set; } = 300;

    public int KeepaliveTimeoutSeconds { get; set; } = 30;

    public List<HotSpotProfileSettings> Profiles { get; set; } = [new()];

    // Local demo status only; this does not confirm a real connection.
    public DateTimeOffset? LastTestAt { get; set; }

    public string LastTestStatus { get; set; } = "لم يُختبر";

    public string LastTestMessage { get; set; } =
        "لم يتم إجراء اختبار اتصال بعد.";
}

public sealed class HotSpotSourceSettings
{
    public bool IsEnabled { get; set; } = true;

    public string ServerName { get; set; } = "hotspot1";

    public string ServerProfileName { get; set; } = "default";

    public HotSpotAuthenticationMode AuthenticationMode { get; set; } =
        HotSpotAuthenticationMode.LocalUsers;

    public bool UseRadius { get; set; }

    public bool RadiusAccounting { get; set; } = true;

    public int RadiusInterimUpdateSeconds { get; set; } = 300;

    public List<string> LoginMethods { get; set; } =
        ["http-chap", "http-pap"];

    public string Comment { get; set; } = string.Empty;

    public string? RouterId { get; set; }

    public string InterfaceName { get; set; } = "bridge-hotspot";

    public string AddressPoolName { get; set; } = "hs-pool-1";

    public string DnsName { get; set; } = "login.nexora.local";

    public bool UseCookie { get; set; } = true;

    public int CookieTimeoutMinutes { get; set; } = 1440;

    public int ClientIdleTimeoutSeconds { get; set; } = 300;

    public int KeepaliveTimeoutSeconds { get; set; } = 30;

    public List<HotSpotProfileSettings> Profiles { get; set; } = [new()];

    public string PortalTitle { get; set; } = "مرحبًا بك في الشبكة";

    public string PortalWelcomeMessage { get; set; } =
        "أدخل بيانات كرت الإنترنت للاتصال.";
}

public sealed class UserManagerSourceSettings
{
    public bool IsEnabled { get; set; }

    public string? RouterId { get; set; }

    public int AuthenticationPort { get; set; } = 1812;

    public int AccountingPort { get; set; } = 1813;

    public bool UseProfiles { get; set; } = true;

    public bool AccountingEnabled { get; set; } = true;

    public int InterimUpdateSeconds { get; set; } = 300;

    public List<UserManagerProfileSettings> Profiles { get; set; } = [new()];

    public List<UserManagerRouterClientSettings> RouterClients { get; set; } = [];

    public string Comment { get; set; } = string.Empty;
}





public sealed class UserManagerRouterClientSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public string Address { get; set; } = string.Empty;

    public bool IsEnabled { get; set; } = true;

    public string? RouterId { get; set; }

    public RadiusTransportProtocol Protocol { get; set; } =
        RadiusTransportProtocol.Udp;

    public int CoaPort { get; set; } = 3799;

    // The prototype stores no RADIUS shared-secret value.
    public bool SharedSecretConfigured { get; set; }

    public string Comment { get; set; } = string.Empty;
}

public sealed class HotSpotProfileSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "default";

    public bool IsEnabled { get; set; } = true;

    public bool UseRadius { get; set; }

    public bool RadiusAccounting { get; set; } = true;

    public int RadiusInterimUpdateSeconds { get; set; } = 300;

    public List<string> LoginMethods { get; set; } =
        ["http-chap", "http-pap"];

    public bool UseCookie { get; set; } = true;

    public int CookieTimeoutMinutes { get; set; } = 1440;

    public int ClientIdleTimeoutSeconds { get; set; } = 300;

    public int KeepaliveTimeoutSeconds { get; set; } = 30;

    public int SharedUsers { get; set; } = 1;

    public string Comment { get; set; } = string.Empty;
}

public sealed class UserManagerProfileSettings
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = "default";

    public bool IsEnabled { get; set; } = true;

    public int DownloadLimitKbps { get; set; }

    public int UploadLimitKbps { get; set; }

    public int SharedUsers { get; set; } = 1;

    public string Comment { get; set; } = string.Empty;
}