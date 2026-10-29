using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Nexora.Demo.Desktop.Features.Sales.Models;
using Nexora.Demo.Desktop.Features.Sales.Services;

namespace Nexora.Demo.Desktop.Features.Sales.Views;

public partial class SalesOperationsLedgerView : UserControl
{
    private readonly SalesOperationsService _operationsService;
    private SalesHistoryRow[] _allRows = [];
    private SalesHistoryRow[] _visibleRows = [];
    private bool _loading;

    public SalesOperationsLedgerView(SalesOperationsService operationsService)
    {
        _loading = true;
        ArgumentNullException.ThrowIfNull(operationsService);
        InitializeComponent();
        _operationsService = operationsService;
        _operationsService.Changed += Source_Changed;
        _loading = false;
        RefreshRows();
    }

    private void Source_Changed(object? sender, EventArgs e) => RefreshRows();
    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshRows();

    public void FocusPointOfSale(string pointName)
    {
        _loading = true;
        FromDatePicker.SelectedDate = null;
        ToDatePicker.SelectedDate = null;
        KindFilterComboBox.SelectedValue = "all";
        LedgerStatusFilterComboBox.SelectedValue = "all";
        PointFilterComboBox.SelectedIndex = 0;
        SearchTextBox.Text = pointName;
        _loading = false;
        ApplyFilters();
    }
    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e) { if (!_loading) ApplyFilters(); }
    private void KindFilter_Changed(object sender, SelectionChangedEventArgs e) { if (!_loading) ApplyFilters(); }
    private void StatusFilter_Changed(object sender, SelectionChangedEventArgs e) { if (!_loading) ApplyFilters(); }
    private void PointFilter_Changed(object sender, SelectionChangedEventArgs e) { if (!_loading) ApplyFilters(); }
    private void DateFilter_Changed(object sender, SelectionChangedEventArgs e) { if (!_loading) ApplyFilters(); }

    private void ClearFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        _loading = true;
        SearchTextBox.Clear();
        FromDatePicker.SelectedDate = null;
        ToDatePicker.SelectedDate = null;
        KindFilterComboBox.SelectedValue = "all";
        LedgerStatusFilterComboBox.SelectedValue = "all";
        PointFilterComboBox.SelectedIndex = 0;
        _loading = false;
        ApplyFilters();
    }

    private void PopulatePointFilter()
    {
        var current = PointFilterComboBox.SelectedItem as string;
        var values = new List<string> { "كل نقاط البيع" };
        values.AddRange(_allRows.Select(row => row.PointName)
            .Where(name => !string.IsNullOrWhiteSpace(name) && name != "—")
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase));
        PointFilterComboBox.ItemsSource = values;
        PointFilterComboBox.SelectedItem = values.Contains(current ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
            ? values.First(value => string.Equals(value, current, StringComparison.CurrentCultureIgnoreCase))
            : values[0];
    }

    private void RefreshRows()
    {
        _loading = true;
        var rows = new List<SalesHistoryRow>();
        rows.AddRange(_operationsService.GetOperations().Select(item => new SalesHistoryRow(
            item.OccurredAt, item.Reference, "بيع", item.ChannelLabel, item.PointName,
            item.PackageName, item.Quantity, item.GrossAmount, item.CommissionAmount,
            item.NetworkAmount, item.AmountReceived, item.OutstandingAmount, item.CollectionStatus)));
        rows.AddRange(_operationsService.GetTransfers().Select(item => new SalesHistoryRow(
            item.OccurredAt, item.Reference, "توريد دفعة", "نقاط البيع", item.PointName,
            $"{item.BatchId} · {item.PackageName}", item.Quantity, item.GrossAmount,
            item.CommissionAmount, item.NetAmount, item.PaidAtTransfer, item.Balance,
            item.Balance > 0m ? "رصيد مستحق" : "مسدد")));
        rows.AddRange(_operationsService.GetSettlements().Select(item => new SalesHistoryRow(
            item.OccurredAt, item.Reference, "تسوية", "تحصيل نقطة", item.PointName,
            string.IsNullOrWhiteSpace(item.ExternalReference) ? item.PaymentMethod : $"{item.PaymentMethod} · {item.ExternalReference}",
            0, item.Amount, 0m, item.Amount, item.Amount, 0m, "تم التحصيل")));
        rows.AddRange(_operationsService.GetExpenses().Select(item => new SalesHistoryRow(
            item.OccurredAt, item.Reference, "مصروف", item.Category, "—",
            string.IsNullOrWhiteSpace(item.RelatedReference) ? item.Description : $"{item.Description} · {item.RelatedReference}",
            0, item.Amount, 0m, -item.Amount, 0m, 0m, "مصروف مسجل")));
        _allRows = rows.OrderByDescending(row => row.OccurredAt).ToArray();
        PopulatePointFilter();
        _loading = false;
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        if (LedgerGrid is null) return;
        IEnumerable<SalesHistoryRow> query = _allRows;
        var kind = (KindFilterComboBox.SelectedValue as string) ?? "all";
        query = kind switch
        {
            "sale" => query.Where(row => row.ActivityType == "بيع"),
            "transfer" => query.Where(row => row.ActivityType == "توريد دفعة"),
            "settlement" => query.Where(row => row.ActivityType == "تسوية"),
            "expense" => query.Where(row => row.ActivityType == "مصروف"),
            _ => query
        };
        if (FromDatePicker.SelectedDate is DateTime fromDate)
            query = query.Where(row => row.OccurredAt.LocalDateTime.Date >= fromDate.Date);
        if (ToDatePicker.SelectedDate is DateTime toDate)
            query = query.Where(row => row.OccurredAt.LocalDateTime.Date <= toDate.Date);

        var selectedPoint = PointFilterComboBox.SelectedItem as string;
        if (!string.IsNullOrWhiteSpace(selectedPoint) && selectedPoint != "كل نقاط البيع")
            query = query.Where(row => string.Equals(row.PointName, selectedPoint, StringComparison.CurrentCultureIgnoreCase));

        var status = (LedgerStatusFilterComboBox.SelectedValue as string) ?? "all";
        query = status switch
        {
            "open" => query.Where(row => row.OutstandingAmount > 0m),
            "central" => query.Where(row => string.Equals(row.Status, "ضمن تجميع الشركة", StringComparison.Ordinal)),
            "settled" => query.Where(row => row.OutstandingAmount <= 0m && row.ActivityType != "مصروف" && row.Status != "مصروف مسجل"),
            _ => query
        };
        var search = SearchTextBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(row => row.Reference.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || row.ActivityType.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || row.Channel.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || row.PointName.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || row.Description.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || row.Status.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        var selectedReference = (LedgerGrid.SelectedItem as SalesHistoryRow)?.Reference;
        _visibleRows = query.ToArray();
        LedgerGrid.ItemsSource = _visibleRows;
        LedgerGrid.SelectedItem = _visibleRows.FirstOrDefault(row => string.Equals(row.Reference, selectedReference, StringComparison.OrdinalIgnoreCase))
            ?? _visibleRows.FirstOrDefault();
        LedgerGrid.Visibility = _visibleRows.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyStatePanel.Visibility = _visibleRows.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
        FilterCountTextBlock.Text = $"{_visibleRows.Length:N0} حركة";
        UpdateSelectedDetails();
        var sales = _visibleRows.Where(row => row.ActivityType is "بيع" or "توريد دفعة").ToArray();
        SummaryTextBlock.Text = $"إجمالي السجل: {_allRows.Length:N0} حركة · النتائج الحالية: {_visibleRows.Length:N0} · قيمة البيع/التوريد المطابقة: {sales.Sum(row => row.GrossAmount):N0} ريال · صافي الحركة: {sales.Sum(row => row.NetAmount):N0} ريال · الرصيد المفتوح في النتائج: {_visibleRows.Sum(row => row.OutstandingAmount):N0} ريال.";
        ExportButton.IsEnabled = _visibleRows.Length > 0;
    }

    private void LedgerGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelectedDetails();

    private void UpdateSelectedDetails()
    {
        if (DetailContentPanel is null || DetailEmptyPanel is null) return;
        if (LedgerGrid.SelectedItem is not SalesHistoryRow row)
        {
            DetailContentPanel.Visibility = Visibility.Collapsed;
            DetailEmptyPanel.Visibility = Visibility.Visible;
            return;
        }

        DetailEmptyPanel.Visibility = Visibility.Collapsed;
        DetailContentPanel.Visibility = Visibility.Visible;
        SelectedActivityTypeTextBlock.Text = row.ActivityType;
        SelectedReferenceTextBlock.Text = row.Reference;
        SelectedDateTextBlock.Text = row.OccurredAtLabel;
        SelectedChannelTextBlock.Text = string.IsNullOrWhiteSpace(row.Channel) ? "—" : row.Channel;
        SelectedPointTextBlock.Text = string.IsNullOrWhiteSpace(row.PointName) ? "—" : row.PointName;
        SelectedDescriptionTextBlock.Text = string.IsNullOrWhiteSpace(row.Description) ? "—" : row.Description;
        SelectedQuantityTextBlock.Text = row.QuantityLabel;
        SelectedStatusTextBlock.Text = row.Status;
        SelectedGrossTextBlock.Text = row.GrossLabel;
        SelectedCommissionTextBlock.Text = row.CommissionLabel;
        SelectedNetTextBlock.Text = row.NetLabel;
        SelectedReceivedTextBlock.Text = row.ReceivedLabel;
        SelectedOutstandingTextBlock.Text = row.OutstandingLabel;
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_visibleRows.Length == 0) return;
        var dialog = new SaveFileDialog
        {
            Title = "تصدير سجل عمليات المبيعات",
            Filter = "CSV UTF-8 (*.csv)|*.csv",
            DefaultExt = ".csv",
            AddExtension = true,
            FileName = $"Nexora-Sales-Ledger-{DateTime.Now:yyyyMMdd-HHmm}.csv"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        try
        {
            static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
            var builder = new StringBuilder();
            builder.AppendLine(string.Join(",", new[] { "التاريخ", "المرجع", "نوع الحركة", "القناة", "نقطة البيع", "التفاصيل", "الكمية", "الإجمالي", "العمولة", "صافي الحركة", "المقبوض", "المتبقي", "الحالة" }.Select(Csv)));
            foreach (var row in _visibleRows)
            {
                var fields = new[]
                {
                    row.OccurredAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), row.Reference,
                    row.ActivityType, row.Channel, row.PointName, row.Description,
                    row.Quantity.ToString(CultureInfo.InvariantCulture), row.GrossAmount.ToString(CultureInfo.InvariantCulture),
                    row.CommissionAmount.ToString(CultureInfo.InvariantCulture), row.NetAmount.ToString(CultureInfo.InvariantCulture),
                    row.ReceivedAmount.ToString(CultureInfo.InvariantCulture), row.OutstandingAmount.ToString(CultureInfo.InvariantCulture), row.Status
                };
                builder.AppendLine(string.Join(",", fields.Select(Csv)));
            }
            File.WriteAllText(dialog.FileName, builder.ToString(), new UTF8Encoding(true));
            MessageBox.Show(Window.GetWindow(this), $"تم تصدير {_visibleRows.Length:N0} حركة إلى الملف المحدد.", "تصدير سجل المبيعات", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(Window.GetWindow(this), "تعذر تصدير السجل.\n\n" + ex.Message, "تصدير سجل المبيعات", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
        }
    }
}


