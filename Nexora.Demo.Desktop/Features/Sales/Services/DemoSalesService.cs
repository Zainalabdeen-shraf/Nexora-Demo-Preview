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
        var maximum = grouped.Max(group => group.Sum(transaction => transaction.Total));

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
                Math.Max(16d, (double)(amount / maximum) * 132d));
        }).ToArray();
    }

    public decimal TotalRevenue => _transactions.Sum(transaction => transaction.Total);
    public int TotalCardsSold => _transactions.Sum(transaction => transaction.Quantity);

    private static IReadOnlyList<DemoSaleTransaction> BuildScenario()
    {
        var rows = new List<DemoSaleTransaction>();
        AddDay(rows, new DateOnly(2026, 10, 3), [88, 8, 16], [500m, 300m, 100m]);   // 48,000
        AddDay(rows, new DateOnly(2026, 10, 4), [105, 9, 18], [500m, 300m, 100m]); // 57,000
        AddDay(rows, new DateOnly(2026, 10, 5), [80, 7, 14], [500m, 300m, 100m]);  // 43,500
        AddDay(rows, new DateOnly(2026, 10, 6), [130, 12, 24], [500m, 300m, 100m]); // 71,000
        AddDay(rows, new DateOnly(2026, 10, 7), [118, 10, 20], [500m, 300m, 100m]); // 64,000
        AddDay(rows, new DateOnly(2026, 10, 8), [165, 14, 28], [500m, 300m, 100m]); // 89,500
        AddDay(rows, new DateOnly(2026, 10, 9), [232, 20, 40], [500m, 300m, 100m]); // 126,000

        return rows.AsReadOnly();
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
