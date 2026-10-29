using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Sales.Models;
using Nexora.Demo.Desktop.Features.Sales.Services;
using Nexora.Demo.Desktop.Features.Shared;

namespace Nexora.Demo.Desktop.Features.Sales.Views;

public partial class SalesStockMovementView : UserControl
{
    private readonly DemoCardIssuanceService _cardService;
    private readonly SalesPointDirectoryService _pointsService;
    private readonly SalesOperationsService _operationsService;
    private SalesStockTransferRecord[] _allTransfers = [];
    private SalesStockTransferRecord[] _visibleTransfers = [];
    private bool _loading;

    public SalesStockMovementView(DemoCardIssuanceService cardService, SalesPointDirectoryService pointsService, SalesOperationsService operationsService)
    {
        _loading = true;
        ArgumentNullException.ThrowIfNull(cardService);
        ArgumentNullException.ThrowIfNull(pointsService);
        ArgumentNullException.ThrowIfNull(operationsService);
        InitializeComponent();
        _cardService = cardService;
        _pointsService = pointsService;
        _operationsService = operationsService;
        _cardService.ScenarioChanged += Source_Changed;
        _pointsService.Changed += Source_Changed;
        _operationsService.Changed += Source_Changed;
        RefreshData();
    }

    private void Source_Changed(object? sender, EventArgs e) => RefreshData();
    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshData();
    private void Inputs_TextChanged(object sender, TextChangedEventArgs e) => UpdatePreview();
    private void Inputs_Changed(object sender, SelectionChangedEventArgs e) => UpdatePreview();
    private void TransferSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading) ApplyTransferFilters();
    }
    private void TransferStatusFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading) ApplyTransferFilters();
    }

    private void RefreshData()
    {
        _loading = true;
        var batches = _cardService.Batches
            .Where(batch => batch.Quantity > 0)
            .Select(batch => new BatchChoice(batch, _operationsService.GetAvailableBatchQuantity(batch.BatchId, batch.Quantity)))
            .Where(choice => choice.AvailableQuantity > 0)
            .OrderByDescending(choice => choice.Batch.CreatedAt)
            .ToArray();
        var oldBatch = (BatchComboBox.SelectedItem as BatchChoice)?.Batch.BatchId;
        BatchComboBox.ItemsSource = batches;
        var selectedBatch = batches.FirstOrDefault(item => string.Equals(item.Batch.BatchId, oldBatch, StringComparison.OrdinalIgnoreCase)) ?? batches.FirstOrDefault();
        BatchComboBox.SelectedItem = selectedBatch;

        var points = _pointsService.GetAll().Where(point => point.IsActive && !point.IsSystemPoint).ToArray();
        var oldPointId = (PointComboBox.SelectedItem as SalesPointRecord)?.Id;
        PointComboBox.ItemsSource = points;
        PointComboBox.SelectedItem = points.FirstOrDefault(point => string.Equals(point.Id, oldPointId, StringComparison.OrdinalIgnoreCase))
            ?? points.FirstOrDefault(point => point.PointType.Contains("بقالة", StringComparison.CurrentCultureIgnoreCase))
            ?? points.FirstOrDefault();
        _allTransfers = _operationsService.GetTransfers().ToArray();
        ApplyTransferFilters();
        StockNoticeTextBlock.Text = _operationsService.StorageWarning ?? "التوزيع هنا يُسجّل حركة الكمية والفاتورة المالية على مستوى رقم الدفعة. لا ينقل بيانات كل كرت داخل وحدة الكروت ولا يبدأ الطباعة تلقائيًا؛ يجب أن تكون الدفعة جاهزة فعليًا قبل تسليمها للمنفذ.";
        SaveTransferButton.IsEnabled = _operationsService.CanWrite;
        _loading = false;
        UpdatePreview();
    }

    private void ApplyTransferFilters()
    {
        if (TransfersGrid is null || TransferSearchTextBox is null || TransferStatusFilterComboBox is null) return;
        IEnumerable<SalesStockTransferRecord> query = _allTransfers;
        var search = TransferSearchTextBox.Text?.Trim() ?? string.Empty;
        if (search.Length > 0)
        {
            query = query.Where(item => item.Reference.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || item.PointName.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || item.BatchId.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || item.PackageName.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        }
        var state = (TransferStatusFilterComboBox.SelectedValue as string) ?? "all";
        query = state switch
        {
            "open" => query.Where(item => item.Balance > 0m),
            "settled" => query.Where(item => item.Balance <= 0m),
            _ => query
        };
        _visibleTransfers = query.OrderByDescending(item => item.OccurredAt).ToArray();
        TransfersGrid.ItemsSource = _visibleTransfers;
        TransfersGrid.Visibility = _visibleTransfers.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        EmptyTransfersPanel.Visibility = _visibleTransfers.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        TransferResultsTextBlock.Text = $"{_visibleTransfers.Length:N0} من {_allTransfers.Length:N0} فاتورة";
    }

    private void UpdatePreview()
    {
        if (AvailableQuantityTextBlock is null || _loading) return;
        if (BatchComboBox.SelectedItem is not BatchChoice batchChoice)
        {
            AvailableQuantityTextBlock.Text = "لا توجد دفعات";
            CommissionTextBlock.Text = "—";
            NetAmountTextBlock.Text = "—";
            SaveTransferButton.IsEnabled = false;
            return;
        }
        var available = _operationsService.GetAvailableBatchQuantity(batchChoice.Batch.BatchId, batchChoice.Batch.Quantity);
        AvailableQuantityTextBlock.Text = available.ToString("N0", CultureInfo.CurrentCulture);
        if (PointComboBox.SelectedItem is not SalesPointRecord point)
        {
            PointTermsTextBlock.Text = "أضف نقطة بيع ميدانية نشطة أولًا.";
            CommissionTextBlock.Text = "—";
            NetAmountTextBlock.Text = "—";
            SaveTransferButton.IsEnabled = false;
            return;
        }
        PointTermsTextBlock.Text = $"النوع: {point.PointType} · عمولة {point.CommissionPercent:0.##}% · {(point.AllowCredit ? "الآجل مسموح" : "الدفع الكامل مطلوب")} · الحساب المعتاد: {point.SettlementCycle}. تُحفظ العمولة في هذه الفاتورة ولا تتغير عند تعديل النسبة مستقبلًا.";
        var quantityOk = int.TryParse(QuantityTextBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var quantity) && quantity > 0;
        var paidOk = TryReadDecimal(PaidTextBox.Text, out var paid) && paid >= 0m;
        var costOk = TryReadDecimal(PrintingCostPerCardTextBox.Text, out var printCost) && printCost >= 0m;
        if (!quantityOk || !paidOk || !costOk)
        {
            CommissionTextBlock.Text = "—";
            NetAmountTextBlock.Text = "—";
            SaveTransferButton.IsEnabled = false;
            return;
        }
        var gross = batchChoice.Batch.UnitPrice * quantity;
        var commission = Math.Round(gross * point.CommissionPercent / 100m, 2, MidpointRounding.AwayFromZero);
        var net = gross - commission;
        CommissionTextBlock.Text = $"{commission:N0} ريال";
        NetAmountTextBlock.Text = $"{net:N0} ريال";
        if (!point.AllowCredit && paid < net)
            PointTermsTextBlock.Text += " · يلزم إدخال كامل صافي المستحق لأن الآجل غير مسموح.";
        SaveTransferButton.IsEnabled = _operationsService.CanWrite && quantity <= available && paid <= net && printCost <= batchChoice.Batch.UnitPrice && (point.AllowCredit || paid >= net);
    }

    private void SaveTransferButton_Click(object sender, RoutedEventArgs e)
    {
        if (BatchComboBox.SelectedItem is not BatchChoice choice || PointComboBox.SelectedItem is not SalesPointRecord point)
        {
            ShowError("اختر دفعة كروت ونقطة بيع نشطة.");
            return;
        }
        if (!int.TryParse(QuantityTextBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var quantity) || quantity < 1)
        {
            ShowError("أدخل كمية صحيحة أكبر من صفر.");
            return;
        }
        if (!TryReadDecimal(PaidTextBox.Text, out var paid) || paid < 0m || !TryReadDecimal(PrintingCostPerCardTextBox.Text, out var printCost) || printCost < 0m)
        {
            ShowError("راجع المبلغ المدفوع وتكلفة الطباعة.");
            return;
        }
        if (MessageBox.Show(Window.GetWindow(this),
            $"تأكيد توريد {quantity:N0} كرت من الدفعة {choice.Batch.BatchId} إلى «{point.Name}»؟\n\nسيُنشأ مرجع فاتورة مستقل، وستُحفظ عمولة {point.CommissionPercent:0.##}% وتكلفة التجهيز. لا تُطبع الكروت تلقائيًا.",
            "تأكيد التوريد", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No, MessageBoxOptions.RtlReading) != MessageBoxResult.Yes)
            return;
        try
        {
            var saved = _operationsService.RecordStockTransfer(new SalesStockTransferDraft(
                choice.Batch.BatchId, choice.Batch.TargetSystem, choice.Batch.PackageName, choice.Batch.Quantity,
                point.Id, point.Name, point.AllowCredit, point.CommissionPercent,
                quantity, choice.Batch.UnitPrice, paid, printCost, NotesTextBox.Text?.Trim() ?? string.Empty));
            MessageBox.Show(Window.GetWindow(this),
                $"تم تسجيل فاتورة التوريد {saved.Reference}.\n\nقيمة الدفعة: {saved.GrossAmount:N0} ريال\nعمولة النقطة: {saved.CommissionAmount:N0} ريال\nصافي مستحق الشبكة: {saved.NetAmount:N0} ريال\nالمدفوع الآن: {saved.PaidAtTransfer:N0} ريال\nالرصيد المفتوح: {saved.Balance:N0} ريال\nتكلفة التجهيز: {saved.PrintingExpense:N0} ريال",
                "تم تسجيل التوريد", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
            NotesTextBox.Clear();
            QuantityTextBox.Text = "1";
            PaidTextBox.Text = point.AllowCredit ? "0" : (choice.Batch.UnitPrice * (1m - point.CommissionPercent / 100m)).ToString("0.##", CultureInfo.CurrentCulture);
            RefreshData();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            ShowError("لم يتم اعتماد التوريد. راجع الرصيد المتاح وسياسة الآجل.\n\n" + ex.Message);
            RefreshData();
        }
    }

    private void ExpenseButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new SalesExpenseWindow { Owner = Window.GetWindow(this) };
        if (window.ShowDialog() != true || window.Draft is null) return;
        try
        {
            var saved = _operationsService.RecordExpense(window.Draft);
            MessageBox.Show(Window.GetWindow(this), $"تم تسجيل المصروف {saved.Reference} بقيمة {saved.Amount:N0} ريال.", "مصروف محفوظ", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            ShowError("تعذر تسجيل المصروف.\n\n" + ex.Message);
        }
    }

    private static bool TryReadDecimal(string? value, out decimal result)
        => decimal.TryParse(value?.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out result)
           || decimal.TryParse(value?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out result);

    private static void ShowError(string message)
        => MessageBox.Show(message, "حركة الكروت", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.RtlReading);

    private sealed record BatchChoice(GeneratedCardBatch Batch, int AvailableQuantity)
    {
        public string DisplayLabel => $"{Batch.BatchId} · {Batch.PackageName} · متاح {AvailableQuantity:N0} من {Batch.Quantity:N0} · {Batch.UnitPrice:N0} ريال · {Batch.TargetSystem}";
    }
}
