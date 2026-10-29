using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Nexora.Demo.Desktop.Features.Sales.Models;
using Nexora.Demo.Desktop.Features.Sales.Services;
using Nexora.Demo.Desktop.Features.Shared;

namespace Nexora.Demo.Desktop.Features.Sales.Views;

public partial class SalesPointManagementView : UserControl
{
    private readonly SalesPointDirectoryService _directoryService;
    private readonly DemoSalesService _salesService;
    private readonly SalesOperationsService? _operationsService;
    private SalesPointGridRow[] _allRows = [];
    private SalesPointGridRow[] _visibleRows = [];
    private bool _initializing = true;

    public SalesPointManagementView(SalesPointDirectoryService directoryService, DemoSalesService salesService, SalesOperationsService? operationsService = null)
    {
        ArgumentNullException.ThrowIfNull(directoryService);
        ArgumentNullException.ThrowIfNull(salesService);
        InitializeComponent();
        _directoryService = directoryService;
        _salesService = salesService;
        _operationsService = operationsService;
        if (_operationsService is not null)
            _operationsService.Changed += DirectoryService_Changed;
        _directoryService.Changed += DirectoryService_Changed;
        _initializing = false;
        RefreshRows();
    }

    public event EventHandler<FeatureNavigationRequestedEventArgs>? NavigationRequested;

    private void DirectoryService_Changed(object? sender, EventArgs e) => RefreshRows();

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshRows();

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initializing && PointsGrid is not null)
            RefreshRows();
    }

    private void StatusFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing && PointsGrid is not null)
            RefreshRows();
    }

    private void RefreshRows()
    {
        if (PointsGrid is null)
            return;

        var preferredPointId = (PointsGrid.SelectedItem as SalesPointGridRow)?.Point.Id;

        // The managed-point metrics are sourced from the operational ledger only.
        // Legacy demonstration transactions remain separate so the new figures cannot mix demo totals with saved operations.
        var realOperations = _operationsService?.GetOperations() ?? [];
        var transfers = _operationsService?.GetTransfers() ?? [];
        var settlements = _operationsService?.GetSettlements() ?? [];

        _allRows = _directoryService.GetAll().Select(point =>
        {
            var realPointOperations = realOperations.Where(row => string.Equals(row.PointId, point.Id, StringComparison.OrdinalIgnoreCase)).ToArray();
            var pointTransfers = transfers.Where(row => string.Equals(row.PointId, point.Id, StringComparison.OrdinalIgnoreCase)).ToArray();
            var pointSettlements = settlements.Where(row => string.Equals(row.PointId, point.Id, StringComparison.OrdinalIgnoreCase)).ToArray();
            var transactionCount = realPointOperations.Length + pointTransfers.Length + pointSettlements.Length;
            var latestDates = realPointOperations.Select(row => (DateTimeOffset?)row.OccurredAt)
                .Concat(pointTransfers.Select(row => (DateTimeOffset?)row.OccurredAt))
                .Concat(pointSettlements.Select(row => (DateTimeOffset?)row.OccurredAt))
                .Where(value => value.HasValue).Select(value => value!.Value).ToArray();
            return new SalesPointGridRow(
                point,
                transactionCount,
                realPointOperations.Sum(row => row.Quantity) + pointTransfers.Sum(row => row.Quantity),
                realPointOperations.Sum(row => row.GrossAmount) + pointTransfers.Sum(row => row.GrossAmount),
                latestDates.Length == 0 ? null : latestDates.Max().LocalDateTime,
                _directoryService.CanWrite && !point.IsSystemPoint,
                realPointOperations.Length + pointTransfers.Length + pointSettlements.Length > 0);
        }).OrderBy(row => row.Point.IsSystemPoint ? 0 : 1).ThenBy(row => row.Point.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();

        IEnumerable<SalesPointGridRow> query = _allRows;
        var search = SearchTextBox.Text?.Trim() ?? string.Empty;
        if (search.Length > 0)
        {
            query = query.Where(row =>
                row.Point.Name.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || row.Point.Code.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || row.Point.PointType.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || row.Point.ContactName.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || row.Point.Phone.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || row.Point.Location.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        }

        var state = (StatusFilterComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";
        if (state == "active")
            query = query.Where(row => row.Point.IsActive);
        else if (state == "inactive")
            query = query.Where(row => !row.Point.IsActive);

        _visibleRows = query.ToArray();
        PointsGrid.ItemsSource = _visibleRows;
        var hasRows = _visibleRows.Length > 0;
        PointsGrid.Visibility = hasRows ? Visibility.Visible : Visibility.Collapsed;
        EmptyStatePanel.Visibility = hasRows ? Visibility.Collapsed : Visibility.Visible;
        PointsGrid.SelectedItem = _visibleRows.FirstOrDefault(row => string.Equals(row.Point.Id, preferredPointId, StringComparison.OrdinalIgnoreCase))
            ?? _visibleRows.FirstOrDefault();

        TotalPointsTextBlock.Text = _allRows.Length.ToString("N0", CultureInfo.CurrentCulture);
        ActivePointsTextBlock.Text = _allRows.Count(row => row.Point.IsActive).ToString("N0", CultureInfo.CurrentCulture);
        InactivePointsTextBlock.Text = _allRows.Count(row => !row.Point.IsActive).ToString("N0", CultureInfo.CurrentCulture);
        TrackedRevenueTextBlock.Text = $"{_allRows.Sum(row => row.Revenue):N0} ريال";
        ResultSummaryTextBlock.Text = $"عرض {_visibleRows.Length:N0} من أصل {_allRows.Length:N0} نقطة بيع · {_visibleRows.Sum(row => row.TransactionCount):N0} معاملة و{_visibleRows.Sum(row => row.CardsSold):N0} كرت ضمن النقاط المطابقة.";

        var canWrite = _directoryService.CanWrite;
        AddPointButton.IsEnabled = canWrite;
        ExportButton.IsEnabled = _visibleRows.Length > 0;
        StorageWarningBorder.Visibility = string.IsNullOrWhiteSpace(_directoryService.StorageWarning) ? Visibility.Collapsed : Visibility.Visible;
        StorageWarningTextBlock.Text = _directoryService.StorageWarning ?? string.Empty;
        EmptyStateTitleTextBlock.Text = _directoryService.StorageWarning is not null ? "تعذر تحميل سجل نقاط البيع" : "لا توجد نقاط بيع مطابقة";
        EmptyStateDescriptionTextBlock.Text = _directoryService.StorageWarning is not null
            ? "لم يُستبدل ملف السجل المتعذر قراءته. راجع رسالة التنبيه أعلاه قبل محاولة الإصلاح."
            : _allRows.Length == 0 ? "أضف أول نقطة بيع لبدء تنظيم الفروع والوكلاء." : "غيّر البحث أو فلتر الحالة لعرض نقاط أخرى.";
        UpdateSelectedPointDetails();
    }

    private void PointsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelectedPointDetails();

    private void UpdateSelectedPointDetails()
    {
        if (SelectedPointDetailsPanel is null || SelectedPointEmptyPanel is null) return;
        if (PointsGrid.SelectedItem is not SalesPointGridRow row)
        {
            SelectedPointDetailsPanel.Visibility = Visibility.Collapsed;
            SelectedPointEmptyPanel.Visibility = Visibility.Visible;
            SelectedPointStatusTextBlock.Text = "—";
            ViewSelectedTransactionsButton.IsEnabled = false;
            return;
        }

        SelectedPointEmptyPanel.Visibility = Visibility.Collapsed;
        SelectedPointDetailsPanel.Visibility = Visibility.Visible;
        SelectedPointNameTextBlock.Text = row.Point.Name;
        SelectedPointCodeTextBlock.Text = $"الرمز {row.Point.Code}";
        SelectedPointTypeTextBlock.Text = row.Point.PointType;
        SelectedPointContactTextBlock.Text = string.IsNullOrWhiteSpace(row.Point.ContactName) ? "غير مسجل" : row.Point.ContactName;
        SelectedPointPhoneTextBlock.Text = string.IsNullOrWhiteSpace(row.Point.Phone) ? "غير مسجل" : row.Point.Phone;
        SelectedPointLocationTextBlock.Text = string.IsNullOrWhiteSpace(row.Point.Location) ? "الموقع غير محدد" : row.Point.Location;
        SelectedPointCommissionTextBlock.Text = row.CommissionLabel;
        SelectedPointPolicyTextBlock.Text = row.CreditPolicyLabel;
        SelectedPointTransactionsTextBlock.Text = row.TransactionCount.ToString("N0", CultureInfo.CurrentCulture);
        SelectedPointRevenueTextBlock.Text = row.RevenueLabel;
        SelectedPointLastSaleTextBlock.Text = row.LastSaleLabel;
        SelectedPointNotesTextBlock.Text = string.IsNullOrWhiteSpace(row.Point.Notes) ? "لا توجد ملاحظات مسجلة" : row.Point.Notes;
        SelectedPointStatusTextBlock.Text = row.Point.StatusLabel;
        SelectedPointStatusTextBlock.Foreground = row.Point.IsActive ? Brushes.SeaGreen : Brushes.Firebrick;
        ViewSelectedTransactionsButton.IsEnabled = row.HasTransactions;
    }

    private void ViewSelectedTransactionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (PointsGrid.SelectedItem is not SalesPointGridRow row || !row.HasTransactions) return;
        NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("sales", "سجل العمليات", row.Point.Name, "point"));
    }

    private void AddPointButton_Click(object sender, RoutedEventArgs e) => OpenEditor(null);

    private void EditPointButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is SalesPointGridRow row && !row.Point.IsSystemPoint)
            OpenEditor(row.Point);
    }

    private void OpenEditor(SalesPointRecord? existing)
    {
        if (!_directoryService.CanWrite)
        {
            ShowError(_directoryService.StorageWarning ?? "تعذر تعديل سجل نقاط البيع.");
            return;
        }

        var editor = new SalesPointEditorWindow(existing) { Owner = Window.GetWindow(this) };
        var accepted = editor.ShowDialog();
        var draft = editor.Draft;
        if (accepted != true || draft is null)
            return;

        var keptOriginalName = false;
        if (existing is not null
            && !string.Equals(existing.Name, draft.Name.Trim(), StringComparison.CurrentCultureIgnoreCase)
            && _salesService.GetAll().Any(transaction => string.Equals(transaction.PointOfSale, existing.Name, StringComparison.CurrentCultureIgnoreCase)))
        {
            var keepName = MessageBox.Show(Window.GetWindow(this),
                "اسم نقطة البيع مرتبط بمعاملات سابقة، والسجل الحالي يحفظ اسم النقطة نصيًا. لا يمكن تغيير الاسم بأمان دون إعادة ربط معاملات المبيعات. هل تريد حفظ بقية التعديلات مع الإبقاء على الاسم الحالي؟",
                "الاسم مرتبط بسجل مبيعات", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No, MessageBoxOptions.RtlReading);
            if (keepName != MessageBoxResult.Yes)
                return;
            draft = draft with { Name = existing.Name };
            keptOriginalName = true;
        }

        try
        {
            var saved = _directoryService.Save(draft, existing?.Id);
            var message = keptOriginalName
                ? $"تم حفظ بقية بيانات نقطة البيع مع الإبقاء على اسمها المرتبط بالسجل: «{saved.Name}» ({saved.Code})."
                : $"تم حفظ نقطة البيع «{saved.Name}» بالرمز {saved.Code}.";
            MessageBox.Show(Window.GetWindow(this), message, "تم الحفظ", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
            RefreshRows();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or JsonException)
        {
            ShowError("تعذر حفظ بيانات نقطة البيع. لم يتم اعتماد التعديل في الذاكرة.\n\n" + ex.Message);
        }
    }

    private void TogglePointButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SalesPointGridRow row)
            return;

        if (row.Point.IsSystemPoint)
        {
            ShowError("نقطة «كروت الشبكات» افتراضية وثابتة ولا يمكن إيقافها.");
            return;
        }
        var nextState = !row.Point.IsActive;
        var action = nextState ? "تفعيل" : "إيقاف";
        var explanation = nextState
            ? $"هل تريد تفعيل نقطة البيع «{row.Point.Name}»؟"
            : $"هل تريد إيقاف نقطة البيع «{row.Point.Name}»؟\n\nستبقى المعاملات السابقة محفوظة، ولن يُحذف سجل النقطة.";
        if (MessageBox.Show(Window.GetWindow(this), explanation, action + " نقطة بيع", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No, MessageBoxOptions.RtlReading) != MessageBoxResult.Yes)
            return;

        try
        {
            _directoryService.SetActive(row.Point.Id, nextState);
            RefreshRows();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or JsonException)
        {
            ShowError("تعذر تغيير حالة نقطة البيع.\n\n" + ex.Message);
        }
    }

    private void DeletePointButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SalesPointGridRow row)
            return;
        if (row.Point.IsSystemPoint)
        {
            ShowError("لا يمكن حذف نقطة «كروت الشبكات» الافتراضية.");
            return;
        }
        if (row.HasTransactions)
        {
            ShowError("لا يمكن حذف نقطة بيع لها معاملات سابقة. أوقفها بدلًا من حذف سجلها.");
            return;
        }

        if (MessageBox.Show(Window.GetWindow(this), $"هل تريد حذف نقطة البيع «{row.Point.Name}»؟\n\nلا توجد معاملات مرتبطة بها حسب السجل المحلي الحالي.", "تأكيد حذف نقطة بيع", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No, MessageBoxOptions.RtlReading) != MessageBoxResult.Yes)
            return;

        try
        {
            _directoryService.Delete(row.Point.Id, hasLinkedTransactions: false);
            RefreshRows();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or JsonException)
        {
            ShowError("تعذر حذف نقطة البيع.\n\n" + ex.Message);
        }
    }

    private void ViewTransactionsButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not SalesPointGridRow row || !row.HasTransactions)
            return;
        NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("sales", "سجل العمليات", row.Point.Name, "point"));
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_visibleRows.Length == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "لا توجد نقاط بيع لتصديرها ضمن النتائج الحالية.", "تصدير نقاط البيع", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "تصدير قائمة نقاط البيع",
            Filter = "CSV UTF-8 (*.csv)|*.csv",
            DefaultExt = ".csv",
            AddExtension = true,
            FileName = $"Nexora-Sales-Points-{DateTime.Now:yyyyMMdd-HHmm}.csv"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;

        try
        {
            static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
            var builder = new StringBuilder();
            builder.AppendLine(string.Join(",", new[] { "الرمز", "اسم نقطة البيع", "النوع", "العمولة %", "سياسة الحساب", "السماح بالآجل", "دورية التسوية", "المسؤول", "رقم التواصل", "الموقع", "الحالة", "عدد المعاملات", "عدد الكروت", "قيمة البيع المسجل", "الملاحظات" }.Select(Csv)));
            foreach (var row in _visibleRows)
            {
                var fields = new[]
                {
                    row.Point.Code, row.Point.Name, row.Point.PointType,
                    row.Point.CommissionPercent.ToString(CultureInfo.InvariantCulture), row.Point.CreditPolicyLabel,
                    row.Point.AllowCredit ? "نعم" : "لا", row.Point.SettlementCycle,
                    row.Point.ContactName, row.Point.Phone, row.Point.Location, row.Point.StatusLabel,
                    row.TransactionCount.ToString(CultureInfo.InvariantCulture),
                    row.CardsSold.ToString(CultureInfo.InvariantCulture),
                    row.Revenue.ToString(CultureInfo.InvariantCulture), row.Point.Notes
                };
                builder.AppendLine(string.Join(",", fields.Select(Csv)));
            }
            File.WriteAllText(dialog.FileName, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            MessageBox.Show(Window.GetWindow(this), $"تم تصدير {_visibleRows.Length:N0} نقطة بيع.", "اكتمل التصدير", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError("تعذر تصدير قائمة نقاط البيع.\n\n" + ex.Message);
        }
    }

    private static void ShowError(string message)
    {
        MessageBox.Show(message, "نقاط البيع", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
    }

    public sealed record SalesPointGridRow(
        SalesPointRecord Point,
        int TransactionCount,
        int CardsSold,
        decimal Revenue,
        DateTime? LatestSale,
        bool CanModify,
        bool HasOperationalRecords = false)
    {
        public string ContactLabel => string.IsNullOrWhiteSpace(Point.ContactName) ? "—" : Point.ContactName;
        public string PhoneLabel => string.IsNullOrWhiteSpace(Point.Phone) ? "—" : Point.Phone;
        public string ContactAndPhoneLabel
        {
            get
            {
                var values = new[] { ContactLabel, PhoneLabel }.Where(value => value != "—").ToArray();
                return values.Length == 0 ? "—" : string.Join(" · ", values);
            }
        }
        public string LocationLabel => string.IsNullOrWhiteSpace(Point.Location) ? "الموقع غير محدد" : Point.Location;
        public string RevenueLabel => $"{Revenue:N0} ريال";
        public string LastSaleLabel => LatestSale?.ToString("yyyy/MM/dd HH:mm", CultureInfo.CurrentCulture) ?? "—";
        public string ToggleLabel => Point.IsActive ? "إيقاف" : "تفعيل";
        public bool HasTransactions => TransactionCount > 0 || HasOperationalRecords;
        public bool CanDelete => CanModify && !HasTransactions && !Point.IsSystemPoint;
        public string CommissionLabel => $"{Point.CommissionPercent:0.##}%";
        public string CreditPolicyLabel => Point.AllowCredit ? $"آجل · {Point.SettlementCycle}" : "فوري";
    }
}
