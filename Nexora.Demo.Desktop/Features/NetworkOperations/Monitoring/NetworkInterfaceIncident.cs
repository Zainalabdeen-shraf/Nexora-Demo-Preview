using System.Text.Json.Serialization;

namespace Nexora.Demo.Desktop.Features.NetworkOperations.Monitoring;

/// <summary>A real observation-derived interface incident; it does not assert that the whole internet is down.</summary>
public sealed class NetworkInterfaceIncident
{
    public string Id { get; set; } = string.Empty;
    public string InterfaceName { get; set; } = string.Empty;
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset LastObservedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public string Status { get; set; } = "مفتوحة";
    public string Evidence { get; set; } = string.Empty;

    [JsonIgnore]
    public string StartedAtDisplay => StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    [JsonIgnore]
    public string LastObservedAtDisplay => LastObservedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
}