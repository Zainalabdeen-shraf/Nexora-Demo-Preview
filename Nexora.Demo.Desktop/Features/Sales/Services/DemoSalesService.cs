using Nexora.Demo.Desktop.Features.Sales.Models;

namespace Nexora.Demo.Desktop.Features.Sales.Services;

/// <summary>
/// Deterministic sales scenario for the demo. It is not imported from RouterOS,
/// User Manager, a cashier, or a production accounting system.
/// </summary>
public sealed class DemoSalesService
{
    private readonly IReadOnlyList<DemoSaleTransaction> _transactions = BuildScenario();

    public IReadOnlyList<DemoSaleTransaction> GetAll() => _transactions;

    public IReadOnlyList<SalesDaySummary> GetDailySummaries()
    {
        var grouped = _transactions
            .GroupBy(transaction => transaction.SaleDate)
            .OrderBy(group => group.Key)
            .ToArray();
        var maximum = grouped.Select(group => group.Sum(transaction => transaction.Total)).DefaultIfEmpty(0m).Max();

        return grouped.Select(group =>
        {
            var amount = group.Sum(transaction => transaction.Total);
            var cards = group.Sum(transaction => transaction.Quantity);
            return new SalesDaySummary(
                group.Key,
                GetArabicDayName(group.Key.DayOfWeek),
                group.Key.ToString("dd/MM"),
                amount,
                cards,
                maximum <= 0m ? 0d : Math.Max(16d, (double)(amount / maximum) * 132d));
        }).ToArray();
    }

    public decimal TotalRevenue => _transactions.Sum(transaction => transaction.Total);
    public int TotalCardsSold => _transactions.Sum(transaction => transaction.Quantity);

    private static IReadOnlyList<DemoSaleTransaction> BuildScenario()
    {
        return Array.Empty<DemoSaleTransaction>();
    }
    private static void AddDay(
        ICollection<DemoSaleTransaction> target,
        DateOnly day,
        int[] quantities,
        decimal[] prices)
    {
        var packageNames = new[] { "24 ساعة", "6 ساعات", "ساعة واحدة" };
        var paymentMethods = new[] { "نقدي", "محفظة إلكترونية", "حساب وكيل" };
        var pointsOfSale = new[] { "المركز الرئيسي", "نقطة بيع الحي", "وكيل الشبكة" };
        var operators = new[] { "موظف المبيعات 1", "موظف المبيعات 2", "وكيل الشبكة" };

        for (var index = 0; index < quantities.Length; index++)
        {
            var number = index + 1;
            var total = quantities[index] * prices[index];
            var timestamp = day.ToDateTime(new TimeOnly(8 + index * 4, 20));
            target.Add(new DemoSaleTransaction(
                $"POS-{day:yyMMdd}-{number:000}",
                timestamp,
                day,
                packageNames[index],
                quantities[index],
                prices[index],
                total,
                paymentMethods[index],
                pointsOfSale[index],
                operators[index],
                "مكتملة"));
        }
    }

    private static string GetArabicDayName(DayOfWeek day) => day switch
    {
        DayOfWeek.Saturday => "السبت",
        DayOfWeek.Sunday => "الأحد",
        DayOfWeek.Monday => "الاثنين",
        DayOfWeek.Tuesday => "الثلاثاء",
        DayOfWeek.Wednesday => "الأربعاء",
        DayOfWeek.Thursday => "الخميس",
        DayOfWeek.Friday => "الجمعة",
        _ => "اليوم"
    };
}
