namespace Nexora.Demo.Desktop.Features.Sales.Models;

/// <summary>
/// Sales point profile. Financial terms are the defaults for future deliveries only;
/// historical invoices keep a snapshot of the terms used when they were posted.
/// </summary>
public sealed record SalesPointRecord(
    string Id,
    string Code,
    string Name,
    string PointType,
    string ContactName,
    string Phone,
    string Location,
    string Notes,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    decimal CommissionPercent = 0m,
    bool AllowCredit = false,
    string SettlementCycle = "فوري",
    bool IsSystemPoint = false)
{
    public string StatusLabel => IsActive ? "نشطة" : "موقوفة";
    public string CreatedAtLabel => CreatedAt.LocalDateTime.ToString("yyyy/MM/dd");
    public string UpdatedAtLabel => UpdatedAt.LocalDateTime.ToString("yyyy/MM/dd HH:mm");
    public string CommissionLabel => $"{CommissionPercent:0.##}%";
    public string CreditPolicyLabel => AllowCredit ? $"آجل · {SettlementCycle}" : "فوري";
}

public sealed record SalesPointDraft(
    string Name,
    string PointType,
    string ContactName,
    string Phone,
    string Location,
    string Notes,
    decimal CommissionPercent = 0m,
    bool AllowCredit = false,
    string SettlementCycle = "فوري");
