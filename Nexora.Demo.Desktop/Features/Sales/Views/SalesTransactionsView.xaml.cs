using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Nexora.Demo.Desktop.Features.Sales.Models;
using Nexora.Demo.Desktop.Features.Sales.Services;

namespace Nexora.Demo.Desktop.Features.Sales.Views;

public partial class SalesTransactionsView : UserControl
{
    private readonly DemoSalesService _salesService;
    private DemoSaleTransaction[] _visibleRows = [];
    private bool _refreshing = true;

    public SalesTransactionsView(DemoSalesService salesService)
    {
        ArgumentNullException.ThrowIfNull(salesService);
        InitializeComponent();
        _salesService = salesService;
        _refreshing = false;
        RefreshRows();
    }

    public void FocusTransaction(string transactionNumber, bool showDetails = false)
    {
        SearchTextBox.Text = transactionNumber;
        RefreshRows();
        TransactionsGrid.SelectedItem = _visibleRows.FirstOrDefault(row => string.Equals(row.TransactionNumber, transactionNumber, StringComparison.OrdinalIgnoreCase));
        if (TransactionsGrid.SelectedItem is DemoSaleTransaction row)
        {
            TransactionsGrid.ScrollIntoView(row);
            if (showDetails) ShowTransactionDetails(row);
        }
    }

    public void FocusPointOfSale(string pointOfSale)
    {
        SearchTextBox.Text = pointOfSale;
        PeriodComboBox.SelectedIndex = 0;
        RefreshRows();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (!_refreshing && TransactionsGrid is not null) RefreshRows();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshRows();

    private void RefreshRows()
    {
        if (TransactionsGrid is null) return;
        IEnumerable<DemoSaleTransaction> query = _salesService.GetAll();
        var period = (PeriodComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";
        if (period == "last-day" && query.Any())
        {
            var last = query.Max(row => row.SaleDate);
            query = query.Where(row => row.SaleDate == last);
        }
        else if (period == "week" && query.Any())
        {
            var last = query.Max(row => row.SaleDate);
            query = query.Where(row => row.SaleDate >= last.AddDays(-6) && row.SaleDate <= last);
        }

        var text = SearchTextBox.Text?.Trim() ?? string.Empty;
        if (text.Length > 0)
            query = query.Where(row => Match(row, text));

        _visibleRows = query.OrderByDescending(row => row.OccurredAt).ToArray();
        TransactionsGrid.ItemsSource = _visibleRows;
        var hasRows = _visibleRows.Length > 0;
        TransactionsGrid.Visibility = hasRows ? Visibility.Visible : Visibility.Collapsed;
        EmptyStatePanel.Visibility = hasRows ? Visibility.Collapsed : Visibility.Visible;
        EmptyStateTitleTextBlock.Text = "لا توجد معاملات مطابقة";
        EmptyStateDescriptionTextBlock.Text = "غيّر نطاق التاريخ أو عبارة البحث. لا يتم احتساب الكروت المُصدرة كمعاملات بيع.";
        TotalSalesTextBlock.Text = $"{_visibleRows.Sum(row => row.Total):N0} ريال";
        TransactionCountTextBlock.Text = _visibleRows.Length.ToString("N0", CultureInfo.CurrentCulture);
        CardsCountTextBlock.Text = _visibleRows.Sum(row => row.Quantity).ToString("N0", CultureInfo.CurrentCulture);
        ResultSummaryTextBlock.Text = $"عرض {_visibleRows.Length:N0} من أصل {_salesService.GetAll().Count:N0} معاملة ضمن الفلاتر الحالية.";
    }

    private static bool Match(DemoSaleTransaction row, string value) =>
        row.TransactionNumber.Contains(value, StringComparison.CurrentCultureIgnoreCase) ||
        row.PackageName.Contains(value, StringComparison.CurrentCultureIgnoreCase) ||
        row.PointOfSale.Contains(value, StringComparison.CurrentCultureIgnoreCase) ||
        row.PaymentMethod.Contains(value, StringComparison.CurrentCultureIgnoreCase) ||
        row.OperatorName.Contains(value, StringComparison.CurrentCultureIgnoreCase) ||
        row.Status.Contains(value, StringComparison.CurrentCultureIgnoreCase);

    private void TransactionsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TransactionsGrid.SelectedItem is DemoSaleTransaction row)
            ShowTransactionDetails(row);
    }

    private void ShowTransactionDetails(DemoSaleTransaction row)
    {
        SelectedTransactionTextBlock.Text = $"{row.TransactionNumber} · {row.TotalLabel}";
        var details = string.Join(Environment.NewLine, new[]
        {
            $"رقم العملية: {row.TransactionNumber}",
            $"التاريخ: {row.OccurredAtLabel}",
            $"الباقة: {row.PackageName}",
            $"الكمية: {row.Quantity:N0}",
            $"سعر الوحدة: {row.UnitPriceLabel}",
            $"الإجمالي المسجل: {row.TotalLabel}",
            $"طريقة الدفع: {row.PaymentMethod}",
            $"نقطة البيع: {row.PointOfSale}",
            $"الموظف: {row.OperatorName}",
            $"الحالة: {row.Status}"
        });
        MessageBox.Show(Window.GetWindow(this), details,
            "تفاصيل معاملة البيع", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
    }

    private void TransactionsGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        // DataGrid sorting is enabled on bound properties; WPF applies sort descriptions to the current view.
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_visibleRows.Length == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "لا توجد معاملات لتصديرها ضمن الفلاتر الحالية.", "تصدير معاملات البيع", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new SaveFileDialog { Title = "تصدير معاملات البيع", Filter = "CSV UTF-8 (*.csv)|*.csv", DefaultExt = ".csv", AddExtension = true, FileName = $"Nexora-Sales-{DateTime.Now:yyyyMMdd-HHmm}.csv" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
            var builder = new StringBuilder();
            builder.AppendLine(string.Join(",", new[] { "رقم العملية", "تاريخ البيع", "الباقة", "الكمية", "سعر الوحدة", "الإجمالي", "طريقة الدفع", "نقطة البيع", "الموظف", "الحالة" }.Select(Csv)));
            foreach (var row in _visibleRows)
                builder.AppendLine(string.Join(",", new[] { row.TransactionNumber, row.OccurredAtLabel, row.PackageName, row.Quantity.ToString(CultureInfo.InvariantCulture), row.UnitPrice.ToString(CultureInfo.InvariantCulture), row.Total.ToString(CultureInfo.InvariantCulture), row.PaymentMethod, row.PointOfSale, row.OperatorName, row.Status }.Select(Csv)));
            File.WriteAllText(dialog.FileName, "﻿" + builder, new UTF8Encoding(true));
            MessageBox.Show(Window.GetWindow(this), $"تم تصدير {_visibleRows.Length:N0} معاملة.", "تصدير معاملات البيع", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), "تعذر تصدير معاملات البيع: " + ex.Message, "تصدير معاملات البيع", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
