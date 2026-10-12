namespace Nexora.Demo.Desktop.Features.Sales.Models;

public sealed record DemoSaleTransaction(
    string TransactionNumber,
    DateTime OccurredAt,
    DateOnly SaleDate,
    string PackageName,
    int Quantity,
    decimal UnitPrice,
    decimal Total,
    string PaymentMethod,
    string PointOfSale,
    string OperatorName,
    string Status)
{
    public string OccurredAtLabel => OccurredAt.ToString("yyyy/MM/dd HH:mm");
    public string SaleDateLabel => SaleDate.ToString("yyyy/MM/dd");
    public string UnitPriceLabel => $"{UnitPrice:N0} ريال";
    public string TotalLabel => $"{Total:N0} ريال";
}
