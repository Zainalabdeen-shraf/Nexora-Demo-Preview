namespace Nexora.Demo.Desktop.Features.Sales.Models;

public sealed record SalesDaySummary(
    DateOnly Day,
    string DayLabel,
    string DateLabel,
    decimal Amount,
    int CardsSold,
    double BarHeight)
{
    public string AmountShortLabel => Amount >= 1000m
        ? $"{Amount / 1000m:0.#} ألف"
        : $"{Amount:N0}";
}
