using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using Nexora.Demo.Desktop.Features.Sales.Models;
using Nexora.Demo.Desktop.Features.Sales.Services;

namespace Nexora.Demo.Desktop.Features.Sales.Views;

/// <summary>
/// Owner-oriented sales dashboard for the local demo. All figures are derived from
/// DemoSalesService records; no collection, debt, or settlement is inferred from sales alone.
/// </summary>
public partial class SalesReportView : UserControl
{
    private readonly DemoSalesService _salesService;
    private DateOnly? _selectedDay;
    private DemoSaleTransaction[] _visibleRows = [];
    private bool _isInitializing = true;

    public SalesReportView(DemoSalesService salesService)
    {
        InitializeComponent();
        _salesService = salesService ?? throw new ArgumentNullException(nameof(salesService));

        _isInitializing = true;
        PopulateFilterOptions();
        SalesDateFilterComboBox.SelectedIndex = 0;
        PackageFilterComboBox.SelectedIndex = 0;
        PointFilterComboBox.SelectedIndex = 0;
        PaymentFilterComboBox.SelectedIndex = 0;
        _isInitializing = false;

        UpdateReport();
    }

    public void ShowDay(DateOnly day)
    {
        _isInitializing = true;
        SalesDateFilterComboBox.SelectedIndex = 0;
        _selectedDay = day;
        _isInitializing = false;
        SalesScenarioTextBlock.Text = $"تم تحديد يوم {GetArabicDayName(day.DayOfWeek)} {day:dd/MM/yyyy} من الرسم؛ الأرقام من بيانات الديمو المحلية.";
        UpdateReport();
    }

    public void ShowTransaction(string transactionNumber)
    {
        _isInitializing = true;
        _selectedDay = null;
        SalesDateFilterComboBox.SelectedIndex = 2;
        PackageFilterComboBox.SelectedIndex = 0;
        PointFilterComboBox.SelectedIndex = 0;
        PaymentFilterComboBox.SelectedIndex = 0;
        _isInitializing = false;
        SalesSearchTextBox.Text = transactionNumber ?? string.Empty;
        UpdateReport();
    }

    private void PopulateFilterOptions()
    {
        AddFilterOption(PackageFilterComboBox, "كل الباقات", "all");
        foreach (var value in _salesService.GetAll().Select(row => row.PackageName).Distinct().OrderBy(value => value, StringComparer.CurrentCulture))
            AddFilterOption(PackageFilterComboBox, value, value);

        AddFilterOption(PointFilterComboBox, "كل نقاط البيع", "all");
        foreach (var value in _salesService.GetAll().Select(row => row.PointOfSale).Distinct().OrderBy(value => value, StringComparer.CurrentCulture))
            AddFilterOption(PointFilterComboBox, value, value);

        AddFilterOption(PaymentFilterComboBox, "كل وسائل الدفع", "all");
        foreach (var value in _salesService.GetAll().Select(row => row.PaymentMethod).Distinct().OrderBy(value => value, StringComparer.CurrentCulture))
            AddFilterOption(PaymentFilterComboBox, value, value);
    }

    private static void AddFilterOption(ComboBox comboBox, string label, string tag)
    {
        comboBox.Items.Add(new ComboBoxItem { Content = label, Tag = tag });
    }

    private void UpdateReport()
    {
        if (_isInitializing || SalesTransactionsGrid is null)
            return;

        var sourceRows = _salesService.GetAll();
        var rows = ApplyFilters(sourceRows, includeSearch: true, includePackage: true, includePoint: true, includePayment: true, includeSelectedDay: true)
            .OrderByDescending(row => row.OccurredAt)
            .ToArray();

        _visibleRows = rows;
        var total = rows.Sum(row => row.Total);
        var cards = rows.Sum(row => row.Quantity);

        SalesTransactionsGrid.ItemsSource = rows;
        TotalSalesTextBlock.Text = $"{total:N0} ريال";
        TransactionCountTextBlock.Text = rows.Length.ToString("N0", CultureInfo.InvariantCulture);
        CardsSoldTextBlock.Text = cards.ToString("N0", CultureInfo.InvariantCulture);
        AverageSaleTextBlock.Text = rows.Length == 0 ? "—" : $"{total / rows.Length:N0} ريال";

        // Trend, package ranking, and payment mix describe the selected period and point of sale.
        // They intentionally remain useful when the user searches for one transaction or selects one package.
        var analyticRows = ApplyFilters(sourceRows, includeSearch: false, includePackage: false, includePoint: true, includePayment: false, includeSelectedDay: false)
            .ToArray();
        SalesDaysItemsControl.ItemsSource = BuildDailyBars(analyticRows);
        TopPackagesItemsControl.ItemsSource = BuildPackageRanking(analyticRows);
        PaymentBreakdownItemsControl.ItemsSource = BuildPaymentBreakdown(analyticRows);

        SalesScenarioTextBlock.Text = _selectedDay.HasValue
            ? $"عرض يوم {GetArabicDayName(_selectedDay.Value.DayOfWeek)} {_selectedDay:dd/MM/yyyy}. جميع الأرقام محسوبة من سجل الديمو المحلي."
            : "لوحة تشغيلية لسجل الديمو المحلي؛ إجمالي البيع المسجل لا يعني أن المبلغ تم تحصيله فعليًا.";

        SalesFilterSummaryTextBlock.Text = _selectedDay.HasValue
            ? $"يعرض الجدول {rows.Length:N0} عملية في {GetArabicDayName(_selectedDay.Value.DayOfWeek)} {_selectedDay:dd/MM/yyyy} من أصل {sourceRows.Count:N0} سجل."
            : $"يعرض الجدول {rows.Length:N0} من أصل {sourceRows.Count:N0} سجل بيع. الرسوم تلخص الفترة ونقطة البيع ولا تتأثر ببحث العملية أو فلتر الباقة أو وسيلة الدفع.";
    }

    private IEnumerable<DemoSaleTransaction> ApplyFilters(
        IEnumerable<DemoSaleTransaction> sourceRows,
        bool includeSearch,
        bool includePackage,
        bool includePoint,
        bool includePayment,
        bool includeSelectedDay)
    {
        var query = sourceRows;
        var dateFilter = (SalesDateFilterComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (includeSelectedDay && _selectedDay.HasValue)
        {
            query = query.Where(row => row.SaleDate == _selectedDay.Value);
        }
        else if (dateFilter == "last-day")
        {
            var lastDay = _salesService.GetAll().Max(row => row.SaleDate);
            query = query.Where(row => row.SaleDate == lastDay);
        }
        else if (dateFilter == "week")
        {
            var lastDay = _salesService.GetAll().Max(row => row.SaleDate);
            query = query.Where(row => row.SaleDate >= lastDay.AddDays(-6) && row.SaleDate <= lastDay);
        }

        if (includePackage)
        {
            var package = SelectedFilterTag(PackageFilterComboBox);
            if (!string.IsNullOrWhiteSpace(package) && package != "all")
                query = query.Where(row => string.Equals(row.PackageName, package, StringComparison.OrdinalIgnoreCase));
        }

        if (includePoint)
        {
            var point = SelectedFilterTag(PointFilterComboBox);
            if (!string.IsNullOrWhiteSpace(point) && point != "all")
                query = query.Where(row => string.Equals(row.PointOfSale, point, StringComparison.OrdinalIgnoreCase));
        }

        if (includePayment)
        {
            var payment = SelectedFilterTag(PaymentFilterComboBox);
            if (!string.IsNullOrWhiteSpace(payment) && payment != "all")
                query = query.Where(row => string.Equals(row.PaymentMethod, payment, StringComparison.OrdinalIgnoreCase));
        }

        if (includeSearch)
        {
            var search = SalesSearchTextBox.Text?.Trim();
            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(row =>
                    row.TransactionNumber.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    row.PackageName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    row.PaymentMethod.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    row.PointOfSale.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    row.OperatorName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                    row.Status.Contains(search, StringComparison.OrdinalIgnoreCase));
            }
        }

        return query;
    }

    private static string? SelectedFilterTag(ComboBox comboBox)
        => (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();

    private static IReadOnlyList<SalesDayBar> BuildDailyBars(IReadOnlyCollection<DemoSaleTransaction> rows)
    {
        if (rows.Count == 0)
            return [];

        var grouped = rows.GroupBy(row => row.SaleDate)
            .Select(group => new { Day = group.Key, Amount = group.Sum(row => row.Total), Cards = group.Sum(row => row.Quantity) })
            .OrderBy(group => group.Day)
            .ToArray();
        var maxAmount = grouped.Max(group => group.Amount);

        return grouped.Select(group => new SalesDayBar(
            group.Day,
            GetArabicDayName(group.Day.DayOfWeek),
            group.Day.ToString("dd/MM", CultureInfo.InvariantCulture),
            group.Amount,
            group.Cards,
            maxAmount == 0 ? 14 : Math.Max(14, (double)(group.Amount / maxAmount) * 92))).ToArray();
    }

    private static IReadOnlyList<SalesPackageSummary> BuildPackageRanking(IReadOnlyCollection<DemoSaleTransaction> rows)
    {
        if (rows.Count == 0)
            return [];

        var grouped = rows.GroupBy(row => row.PackageName)
            .Select(group => new
            {
                PackageName = group.Key,
                Quantity = group.Sum(row => row.Quantity),
                Revenue = group.Sum(row => row.Total),
                Transactions = group.Count()
            })
            .OrderByDescending(group => group.Quantity)
            .ThenByDescending(group => group.Revenue)
            .Take(5)
            .ToArray();
        var maxQuantity = grouped.Max(group => group.Quantity);
        var totalCards = rows.Sum(row => row.Quantity);

        return grouped.Select((group, index) => new SalesPackageSummary(
            group.PackageName,
            group.Quantity.ToString("N0", CultureInfo.InvariantCulture),
            $"{group.Revenue:N0} ريال",
            $"{(totalCards == 0 ? 0 : (double)group.Quantity / totalCards * 100):0.#}%",
            $"{group.Transactions:N0} عمليات · {group.Revenue:N0} ريال",
            maxQuantity == 0 ? 0 : (double)group.Quantity / maxQuantity * 100,
            index + 1)).ToArray();
    }

    private static IReadOnlyList<PaymentSummary> BuildPaymentBreakdown(IReadOnlyCollection<DemoSaleTransaction> rows)
    {
        if (rows.Count == 0)
            return [];

        var grouped = rows.GroupBy(row => row.PaymentMethod)
            .Select(group => new { Method = group.Key, Revenue = group.Sum(row => row.Total), Transactions = group.Count() })
            .OrderByDescending(group => group.Revenue)
            .ToArray();
        var total = grouped.Sum(group => group.Revenue);

        return grouped.Select(group => new PaymentSummary(
            group.Method,
            $"{group.Revenue:N0} ريال",
            $"{group.Transactions:N0} عملية",
            $"{(total == 0 ? 0 : (double)(group.Revenue / total) * 100):0.#}%",
            total == 0 ? 0 : (double)(group.Revenue / total) * 100)).ToArray();
    }

    private void SalesDayButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DateOnly day })
            ShowDay(day);
    }

    private void PackageRankingButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string packageName })
            return;

        _isInitializing = true;
        SelectFilterByTag(PackageFilterComboBox, packageName);
        SalesSearchTextBox.Clear();
        _isInitializing = false;
        UpdateReport();
        SalesFilterSummaryTextBlock.Text = $"تمت تصفية سجل العمليات إلى باقة «{packageName}». الرسوم تستمر بعرض ترتيب الباقات في الفترة المحددة.";
    }

    private static void SelectFilterByTag(ComboBox comboBox, string tag)
    {
        foreach (var item in comboBox.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
    }

    private void SalesSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitializing && SalesTransactionsGrid is not null)
            UpdateReport();
    }

    private void SalesDateFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || SalesTransactionsGrid is null)
            return;

        _selectedDay = null;
        UpdateReport();
    }

    private void SalesFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitializing && SalesTransactionsGrid is not null)
            UpdateReport();
    }

    private void ResetFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        _isInitializing = true;
        _selectedDay = null;
        SalesSearchTextBox.Clear();
        SalesDateFilterComboBox.SelectedIndex = 0;
        PackageFilterComboBox.SelectedIndex = 0;
        PointFilterComboBox.SelectedIndex = 0;
        PaymentFilterComboBox.SelectedIndex = 0;
        _isInitializing = false;
        UpdateReport();
    }

    private void RefreshReportButton_Click(object sender, RoutedEventArgs e) => UpdateReport();

    private void ClearDayFilterButton_Click(object sender, RoutedEventArgs e)
    {
        _isInitializing = true;
        _selectedDay = null;
        SalesDateFilterComboBox.SelectedIndex = 0;
        _isInitializing = false;
        UpdateReport();
    }

    private void ExportReportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_visibleRows.Length == 0)
        {
            MessageBox.Show("لا توجد سجلات في التصفية الحالية لتصديرها.", "تصدير النتائج", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "تصدير نتائج المبيعات",
            Filter = "ملف CSV (*.csv)|*.csv",
            DefaultExt = ".csv",
            AddExtension = true,
            FileName = $"مبيعات-الشبكة-{DateTime.Now:yyyyMMdd-HHmm}.csv"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;

        var csv = new StringBuilder();
        csv.AppendLine("رقم العملية,تاريخ البيع,الباقة,الكمية,سعر الكرت,الإجمالي المسجل,طريقة الدفع,نقطة البيع,الموظف,الحالة");
        foreach (var row in _visibleRows)
        {
            csv.AppendLine(string.Join(",", new[]
            {
                Csv(row.TransactionNumber), Csv(row.OccurredAtLabel), Csv(row.PackageName),
                Csv(row.Quantity.ToString(CultureInfo.InvariantCulture)), Csv(row.UnitPrice.ToString(CultureInfo.InvariantCulture)),
                Csv(row.Total.ToString(CultureInfo.InvariantCulture)), Csv(row.PaymentMethod), Csv(row.PointOfSale),
                Csv(row.OperatorName), Csv(row.Status)
            }));
        }

        File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(true));
        MessageBox.Show($"تم تصدير {_visibleRows.Length:N0} سجل بيع إلى الملف المحدد.",
            "تم التصدير", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

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

    private sealed record SalesDayBar(DateOnly Day, string DayLabel, string DateLabel, decimal Amount, int CardsSold, double BarHeight)
    {
        public string AmountShortLabel => Amount >= 1000m ? $"{Amount / 1000m:0.#} ألف" : $"{Amount:N0}";
    }

    private sealed record SalesPackageSummary(
        string PackageName,
        string QuantityLabel,
        string RevenueLabel,
        string ShareLabel,
        string DetailLabel,
        double RelativeValue,
        int Rank);

    private sealed record PaymentSummary(
        string PaymentMethod,
        string RevenueLabel,
        string TransactionCountLabel,
        string ShareLabel,
        double SharePercent);
}
