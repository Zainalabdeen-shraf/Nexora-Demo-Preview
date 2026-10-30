using System.Globalization;

namespace Nexora.Demo.Desktop.Features.Cards.Models;

/// <summary>Demo-only card record. Does not imply a matching RouterOS user exists.</summary>
public sealed record GeneratedDemoCard(
    string BatchId,
    string CardNumber,
    string Username,
    string Password,
    string TargetSystem,
    string PackageName,
    decimal UnitPrice,
    string RouterProfile,
    string TimeLimit,
    string DataLimit,
    string RateLimit,
    DateTimeOffset CreatedAt,
    string LocalStatus,
    string PointOfSale,
    string Comment,
    string ManagerAccount = "admin",
    int SharedUsers = 1,
    string PackageValidity = "غير محددة",
    string DemoStatus = "متاحة",
    DateTimeOffset? ArchivedAt = null,
    string ArchiveReason = "")
{
    public string PasswordDisplay => string.IsNullOrEmpty(Password) ? "بدون كلمة مرور" : Password;
    public string CreatedAtLabel => CreatedAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);
    public string UnitPriceLabel => $"{UnitPrice:N0} ريال";
    public string TargetSystemLabel => TargetSystem;
    public string SharedUsersLabel => SharedUsers <= 0 ? "غير محدود" : SharedUsers.ToString(CultureInfo.CurrentCulture);
    public string SerialNumber { get; init; } = string.Empty;
    public bool IsArchived => ArchivedAt.HasValue;
    public string ArchivedAtLabel => ArchivedAt?.ToLocalTime().ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture) ?? "—";
}
