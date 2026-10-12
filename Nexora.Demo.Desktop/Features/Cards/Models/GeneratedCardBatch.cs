using System.Globalization;

namespace Nexora.Demo.Desktop.Features.Cards.Models;

public sealed record GeneratedCardBatch(
    string BatchId,
    string TargetSystem,
    string PackageName,
    int Quantity,
    decimal UnitPrice,
    DateTimeOffset CreatedAt,
    string LocalStatus,
    string RouterProfile = "",
    string PointOfSale = "",
    string ManagerAccount = "admin",
    int SharedUsers = 1)
{
    public decimal Total => Quantity * UnitPrice;
    public string CreatedAtLabel => CreatedAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);
    public string TotalLabel => $"{Total:N0} ريال";
}
