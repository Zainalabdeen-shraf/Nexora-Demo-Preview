namespace Nexora.Demo.Desktop.Features.Cards.Models;

public sealed record DemoCardPackage(
    string Key,
    string DisplayName,
    decimal Price,
    string TimeLimit,
    string DataLimit,
    string RateLimit,
    string UserManagerProfile,
    string HotSpotProfile,
    decimal? CardDisplayPrice = null,
    string Validity = "غير محددة")
{
    public decimal PrintPrice => CardDisplayPrice ?? Price;
    public string PriceLabel => $"{Price:N0} ريال";
    public string PrintPriceLabel => $"{PrintPrice:N0} ريال";
}
