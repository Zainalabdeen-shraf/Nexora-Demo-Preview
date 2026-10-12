using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using Nexora.Demo.Desktop.Features.Sales.Models;
using Nexora.Demo.Desktop.Features.Sales.Services;

namespace Nexora.Demo.Desktop.Features.Sales.Views;

public partial class SalesReportView : UserControl
{
    private readonly DemoSalesService _salesService;
    private DateOnly? _selectedDay;
    private DemoSaleTransaction[] _visibleRows = [];

    public SalesReportView(DemoSalesService salesService)
    {
        InitializeComponent();
        _salesService = salesService;
        BuildDailyChart();
        UpdateReport();
    }

    public void ShowDay(DateOnly day)
    {
        SalesDateFilterComboBox.SelectedIndex = 0;
        _selectedDay = day;
        SalesScenarioTextBlock.Text = $"تفاصيل المبيعات يوم {GetArabicDayName(day.DayOfWeek)} {day:dd/MM/yyyy}.";
        UpdateReport();
    }

    public void ShowTransaction(string transactionNumber)
    {
        _selectedDay = null;
        SalesDateFilterComboBox.SelectedIndex = 0;
        SalesSearchTextBox.Text = transactionNumber;
        UpdateReport();
    }

    private void BuildDailyChart()
    {
        SalesDaysItemsControl.ItemsSource = _salesService.GetDailySummaries();
    }

    private void UpdateReport()
    {
        var query = _salesService.GetAll().AsEnumerable();
        var selectedDateFilter = (SalesDateFilterComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (_selectedDay.HasValue)
        {
            query = query.Where(row => row.SaleDate == _selectedDay.Value);
        }
        else if (selectedDateFilter == "last-day")
        {
            var lastDay = _salesService.GetAll().Max(row => row.SaleDate);
            query = query.Where(row => row.SaleDate == lastDay);
        }
        else if (selectedDateFilter == "week")
        {
            var lastDay = _salesService.GetAll().Max(row => row.SaleDate);
            query = query.Where(row => row.SaleDate >= lastDay.AddDays(-6) && row.SaleDate <= lastDay);
        }

        var search = SalesSearchTextBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(row =>
                row.TransactionNumber.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                row.PackageName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                row.PaymentMethod.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                row.PointOfSale.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                row.OperatorName.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var rows = query.OrderByDescending(row => row.OccurredAt).ToArray();
        _visibleRows = rows;
        var total = rows.Sum(row => row.Total);
        var cards = rows.Sum(row => row.Quantity);

        SalesTransactionsGrid.ItemsSource = rows;
        TotalSalesTextBlock.Text = $"{total:N0} ريال";
        TransactionCountTextBlock.Text = rows.Length.ToString("N0", CultureInfo.InvariantCulture);
        CardsSoldTextBlock.Text = cards.ToString("N0", CultureInfo.InvariantCulture);
        AverageSaleTextBlock.Text = rows.Length == 0 ? "—" : $"{total / rows.Length:N0} ريال";
        SalesFilterSummaryTextBlock.Text = _selectedDay.HasValue
            ? $"{rows.Length:N0} عملية ضمن {GetArabicDayName(_selectedDay.Value.DayOfWeek)} {_selectedDay:dd/MM/yyyy}. اختر «عرض كل الأيام» لمسح تحديد الرسم."
            : $"يعرض الجدول {rows.Length:N0} من أصل {_salesService.GetAll().Count:N0} سجل بيع.";
    }

    private void SalesDayButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DateOnly day })
            ShowDay(day);
    }

    private void SalesSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SalesTransactionsGrid is not null)
            UpdateReport();
    }

    private void SalesDateFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SalesTransactionsGrid is null)
            return;

        if ((SalesDateFilterComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "all")
            _selectedDay = null;
        else if ((SalesDateFilterComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() is "week" or "last-day")
            _selectedDay = null;

        UpdateReport();
    }

    private void ClearDayFilterButton_Click(object sender, RoutedEventArgs e)
    {
        _selectedDay = null;
        SalesDateFilterComboBox.SelectedIndex = 0;
        UpdateReport();
    }

    private void ExportReportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_visibleRows.Length == 0)
        {
            MessageBox.Show("لا توجد سجلات في التصفية الحالية لتصديرها.", "تصدير التقرير", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "تصدير تقرير المبيعات",
            Filter = "ملف CSV (*.csv)|*.csv",
            DefaultExt = ".csv",
            AddExtension = true,
            FileName = $"تقرير-المبيعات-{DateTime.Now:yyyyMMdd-HHmm}.csv"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;

        var csv = new StringBuilder();
        csv.AppendLine("رقم العملية,تاريخ البيع,الباقة,الكمية,سعر الكرت,الإجمالي,طريقة الدفع,نقطة البيع,الموظف,الحالة");
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
}
