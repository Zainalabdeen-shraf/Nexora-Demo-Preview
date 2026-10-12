using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Sales.Models;
using Nexora.Demo.Desktop.Features.Sales.Services;
using Nexora.Demo.Desktop.Features.Shared;

namespace Nexora.Demo.Desktop.Features.Reports.Views;

/// <summary>Unified report workspace over the demo's existing issuance, sales, print and workflow records.</summary>
public partial class ReportsCenterView : UserControl
{
    private readonly DemoCardIssuanceService _issuanceService;
    private readonly DemoSalesService _salesService;
    private readonly DemoCardWorkflowStore _workflowStore = new();
    private ReportTableRow[] _visibleRows = [];
    private string _reportKey = "cards";
    private bool _refreshing = true;
    private bool _isSubscribed;

    public ReportsCenterView(DemoCardIssuanceService issuanceService, DemoSalesService salesService)
    {
        ArgumentNullException.ThrowIfNull(issuanceService);
        ArgumentNullException.ThrowIfNull(salesService);
        InitializeComponent();
        _issuanceService = issuanceService;
        _salesService = salesService;
        FromDatePicker.SelectedDate = DateTime.Today.AddDays(-30);
        ToDatePicker.SelectedDate = DateTime.Today;
        Loaded += ReportsCenterView_Loaded;
        Unloaded += ReportsCenterView_Unloaded;
        _refreshing = false;
        RefreshReport();
    }

    public event EventHandler<FeatureNavigationRequestedEventArgs>? NavigationRequested;

    private void ReportsCenterView_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_isSubscribed)
        {
            _issuanceService.Cards.CollectionChanged += Source_Changed;
            _issuanceService.Batches.CollectionChanged += Source_Changed;
            _isSubscribed = true;
        }
        RefreshReport();
    }

    private void ReportsCenterView_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_isSubscribed)
        {
            _issuanceService.Cards.CollectionChanged -= Source_Changed;
            _issuanceService.Batches.CollectionChanged -= Source_Changed;
            _isSubscribed = false;
        }
    }

    private void Source_Changed(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        Dispatcher.InvokeAsync(RefreshReport);

    private string SelectedTag(ComboBox combo, string fallback = "all") =>
        (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;

    private void Filters_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing && ReportGrid is not null)
            RefreshReport();
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_refreshing && ReportGrid is not null)
            RefreshReport();
    }

    private void DateFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing && ReportGrid is not null)
            RefreshReport();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshReport();

    private void ColumnsButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = sender as UIElement,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
        };
        menu.Items.Add(new MenuItem { Header = "إظهار أو إخفاء الأعمدة", IsEnabled = false });
        menu.Items.Add(new Separator());

        foreach (var column in ReportGrid.Columns.OrderBy(candidate => candidate.DisplayIndex))
        {
            var item = new MenuItem
            {
                Header = Convert.ToString(column.Header, CultureInfo.CurrentCulture) ?? string.Empty,
                IsCheckable = true,
                IsChecked = column.Visibility == Visibility.Visible,
                StaysOpenOnClick = true
            };
            item.Click += (_, _) =>
            {
                var shouldShow = item.IsChecked;
                var visibleCount = ReportGrid.Columns.Count(candidate => candidate.Visibility == Visibility.Visible);
                if (!shouldShow && visibleCount <= 1)
                {
                    item.IsChecked = true;
                    return;
                }
                column.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void RefreshReport()
    {
        if (ReportGrid is null || ReportTypeComboBox.SelectedItem is not ComboBoxItem reportItem)
            return;

        _reportKey = reportItem.Tag?.ToString() ?? "cards";
        var sourceKey = SelectedTag(SourceFilterComboBox);
        var statusKey = SelectedTag(StatusFilterComboBox);
        var search = SearchTextBox.Text?.Trim() ?? string.Empty;
        var from = FromDatePicker.SelectedDate?.Date;
        var to = ToDatePicker.SelectedDate?.Date;
        if (from.HasValue && to.HasValue && from.Value > to.Value)
        {
            EmptyStatePanel.Visibility = Visibility.Visible;
            ReportGrid.Visibility = Visibility.Collapsed;
            EmptyStateTitleTextBlock.Text = "نطاق التاريخ غير صحيح";
            EmptyStateDescriptionTextBlock.Text = "تأكد أن تاريخ البداية يسبق تاريخ النهاية.";
            _visibleRows = [];
            SetMetrics("عدد النتائج", "0", "نطاق التاريخ", "غير صحيح", "المصدر", "—");
            ResultSummaryTextBlock.Text = "لم يتم تنفيذ التصفية؛ تاريخ البداية بعد تاريخ النهاية.";
            return;
        }

        ConfigureReportColumns(_reportKey);
        SourceFilterLabelTextBlock.Visibility = _reportKey == "sales" ? Visibility.Collapsed : Visibility.Visible;
        SourceFilterComboBox.Visibility = _reportKey == "sales" ? Visibility.Collapsed : Visibility.Visible;
        StatusFilterLabelTextBlock.Visibility = _reportKey == "cards" ? Visibility.Visible : Visibility.Collapsed;
        StatusFilterComboBox.Visibility = _reportKey == "cards" ? Visibility.Visible : Visibility.Collapsed;
        _visibleRows = _reportKey switch
        {
            "cards" => BuildCardRows(sourceKey, statusKey, search, from, to),
            "batches" => BuildBatchRows(sourceKey, search, from, to),
            "sales" => BuildSalesRows(search, from, to),
            "printing" => BuildPrintRows(sourceKey, search, from, to),
            "actions" => BuildActionRows(sourceKey, search, from, to, syncOnly: false),
            "sync" => BuildActionRows(sourceKey, search, from, to, syncOnly: true),
            "usage" => [],
            _ => []
        };

        ReportGrid.ItemsSource = _visibleRows;
        var hasRows = _visibleRows.Length > 0;
        ReportGrid.Visibility = hasRows ? Visibility.Visible : Visibility.Collapsed;
        EmptyStatePanel.Visibility = hasRows ? Visibility.Collapsed : Visibility.Visible;
        EmptyStateTitleTextBlock.Text = "لا توجد نتائج مطابقة";
        EmptyStateDescriptionTextBlock.Text = _reportKey == "usage"
            ? "لا تتوفر في مصادر البيانات الحالية سجلات جلسات واستهلاك يمكن عرضها لهذا التقرير."
            : search.Length > 0
                ? "لا توجد سجلات تطابق البحث الحالي مع بقية الفلاتر. غيّر البحث أو امسحه."
                : "لا توجد سجلات في هذا النطاق من المصدر المحدد.";

        UpdateMetrics(_reportKey, _visibleRows);
        var count = _visibleRows.Length;
        var sourceLabel = _reportKey == "sales" ? "سجلات المبيعات" : sourceKey switch
        {
            "hotspot" => "HotSpot",
            "user-manager" => "User Manager",
            _ => "كل المصادر ذات الصلة"
        };
        var rangeText = $"{(from.HasValue ? from.Value.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) : "بداية غير محددة")} — {(to.HasValue ? to.Value.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) : "نهاية غير محددة")}";
        ResultSummaryTextBlock.Text = $"{count:N0} نتيجة · {sourceLabel} · {rangeText}";
        ReportUpdatedAtTextBlock.Text = $"آخر تحديث للعرض: {DateTime.Now:HH:mm:ss}";
        ReportSourceBadgeTextBlock.Text = _reportKey switch
        {
            "sales" => "مصدر: معاملات المبيعات",
            "printing" => "مصدر: قائمة الطباعة وسجل المهام",
            "actions" => "مصدر: سجل إجراءات الكروت",
            "sync" => "مصدر: سجل طلبات المزامنة",
            "usage" => "مصدر: سجلات الاستخدام غير متاحة",
            _ => "مصدر: سجلات Nexora"
        };
    }

    private ReportTableRow[] BuildCardRows(string sourceKey, string statusKey, string search, DateTime? from, DateTime? to) =>
        _issuanceService.Cards
            .Where(card => MatchesSystem(card.TargetSystem, sourceKey))
            .Where(card => statusKey == "all" || card.DemoStatus == statusKey)
            .Where(card => WithinRange(card.CreatedAt.DateTime, from, to))
            .Where(card => Matches(search, card.CardNumber, card.Username, card.TargetSystem, card.PackageName, card.BatchId, card.PointOfSale, card.DemoStatus))
            .OrderByDescending(card => card.CreatedAt)
            .Select(card => new ReportTableRow(
                card.CardNumber, card.Username, card.TargetSystem, card.PackageName, card.BatchId,
                card.DemoStatus, card.CreatedAtLabel, card.CardNumber, "card", SystemKey(card.TargetSystem)))
            .ToArray();

    private ReportTableRow[] BuildBatchRows(string sourceKey, string search, DateTime? from, DateTime? to) =>
        _issuanceService.Batches
            .Where(batch => MatchesSystem(batch.TargetSystem, sourceKey))
            .Where(batch => WithinRange(batch.CreatedAt.DateTime, from, to))
            .Where(batch => Matches(search, batch.BatchId, batch.TargetSystem, batch.PackageName, batch.PointOfSale, batch.LocalStatus))
            .OrderByDescending(batch => batch.CreatedAt)
            .Select(batch => new ReportTableRow(
                batch.BatchId, batch.TargetSystem, batch.PackageName, batch.Quantity.ToString("N0", CultureInfo.CurrentCulture),
                $"{batch.UnitPrice:N0} ريال", batch.TotalLabel, batch.CreatedAtLabel, batch.BatchId, "batch-details", SystemKey(batch.TargetSystem)))
            .ToArray();

    private ReportTableRow[] BuildSalesRows(string search, DateTime? from, DateTime? to) =>
        _salesService.GetAll()
            .Where(row => WithinRange(row.OccurredAt, from, to))
            .Where(row => Matches(search, row.TransactionNumber, row.PackageName, row.PointOfSale, row.PaymentMethod, row.OperatorName, row.Status))
            .OrderByDescending(row => row.OccurredAt)
            .Select(row => new ReportTableRow(
                row.TransactionNumber, row.OccurredAtLabel, row.PackageName, row.Quantity.ToString("N0", CultureInfo.CurrentCulture),
                row.PointOfSale, row.PaymentMethod, row.TotalLabel, row.TransactionNumber, "sale", null))
            .ToArray();

    private ReportTableRow[] BuildPrintRows(string sourceKey, string search, DateTime? from, DateTime? to)
    {
        var pendingRows = _workflowStore.GetPendingPrintSelection()
            .Where(item => MatchesSystem(item.TargetSystem, sourceKey))
            .Select(item => new
            {
                Item = item,
                Card = _issuanceService.Cards.FirstOrDefault(card =>
                    string.Equals(card.BatchId, item.BatchId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(card.CardNumber, item.CardNumber, StringComparison.OrdinalIgnoreCase) &&
                    SystemKey(card.TargetSystem) == SystemKey(item.TargetSystem))
            })
            .Where(entry => entry.Card is not null)
            .Where(entry => WithinRange((entry.Item.QueuedAt == default ? entry.Card!.CreatedAt : entry.Item.QueuedAt).DateTime, from, to))
            .Where(entry => Matches(search, entry.Item.CardNumber, entry.Item.BatchId, entry.Card!.PackageName, entry.Item.TargetSystem, entry.Card.PointOfSale))
            .Select(entry => new ReportTableRow(
                entry.Item.CardNumber, entry.Item.BatchId, entry.Card!.PackageName, entry.Card.TargetSystem,
                entry.Card.PointOfSale, (entry.Item.QueuedAt == default ? entry.Card.CreatedAt : entry.Item.QueuedAt).ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture), "بانتظار الطباعة", entry.Item.CardNumber, "card", SystemKey(entry.Card.TargetSystem)))
            .ToList();

        var submittedRows = _workflowStore.GetActionHistory()
            .Where(action => action.ActionType == "PrintSubmitted")
            .Where(action => MatchesSystem(action.TargetSystem, sourceKey))
            .Where(action => WithinRange(action.CreatedAt.DateTime, from, to))
            .Where(action => Matches(search, action.ActionLabel, action.BatchId, action.TargetSystem, action.Description))
            .Select(action => new ReportTableRow(
                action.ActionLabel, action.BatchId, action.TargetSystem, action.Count.ToString("N0", CultureInfo.CurrentCulture),
                action.Description, action.CreatedAtLabel, "أُرسلت إلى الطابعة", action.BatchId, "batch", SystemKey(action.TargetSystem)))
            .ToList();

        return pendingRows.Concat(submittedRows)
            .OrderByDescending(row => row.Value6, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private ReportTableRow[] BuildActionRows(string sourceKey, string search, DateTime? from, DateTime? to, bool syncOnly) =>
        _workflowStore.GetActionHistory()
            .Where(action => !syncOnly || action.ActionType == "SyncRequested")
            .Where(action => MatchesSystem(action.TargetSystem, sourceKey))
            .Where(action => WithinRange(action.CreatedAt.DateTime, from, to))
            .Where(action => Matches(search, action.ActionLabel, action.BatchId, action.TargetSystem, action.Description, action.ActionType))
            .OrderByDescending(action => action.CreatedAt)
            .Select(action => new ReportTableRow(
                action.ActionLabel, action.TargetSystem, action.BatchId, action.Count.ToString("N0", CultureInfo.CurrentCulture),
                action.CreatedAtLabel, syncOnly ? "تم تسجيل طلب المزامنة" : action.ActionType, action.Description,
                action.BatchId, "batch", SystemKey(action.TargetSystem)))
            .ToArray();

    private void ConfigureReportColumns(string reportKey)
    {
        var configuration = reportKey switch
        {
            "batches" => new[] { "رقم الدفعة", "جهة الإصدار", "الباقة", "عدد الكروت", "سعر الوحدة", "قيمة الإصدار", "تاريخ الإنشاء" },
            "sales" => new[] { "رقم العملية", "تاريخ البيع", "الباقة", "الكمية", "نقطة البيع", "طريقة الدفع", "الإجمالي" },
            "printing" => new[] { "رقم الكرت", "رقم الدفعة", "الباقة", "جهة الإصدار", "نقطة البيع", "تاريخ الإضافة", "حالة القائمة" },
            "actions" => new[] { "العملية", "المصدر", "رقم الدفعة", "عدد العناصر", "وقت العملية", "نوع السجل", "التفاصيل" },
            "sync" => new[] { "الطلب", "جهة الإصدار", "رقم الدفعة", "العناصر", "وقت التسجيل", "الحالة", "التفاصيل" },
            _ => new[] { "رقم الكرت", "اسم المستخدم", "جهة الإصدار", "الباقة", "رقم الدفعة", "حالة الحساب", "تاريخ الإنشاء" }
        };
        var columns = new[] { ColumnOne, ColumnTwo, ColumnThree, ColumnFour, ColumnFive, ColumnSix, ColumnSeven };
        for (var index = 0; index < columns.Length; index++)
        {
            columns[index].Header = configuration[index];
            columns[index].Visibility = Visibility.Visible;
        }
        ReportGrid.ItemsSource = null;
    }

    private void UpdateMetrics(string reportKey, IReadOnlyCollection<ReportTableRow> rows)
    {
        switch (reportKey)
        {
            case "sales":
            {
                var numbers = rows.Select(row => row.ContextValue)
                    .Select(id => _salesService.GetAll().FirstOrDefault(sale => sale.TransactionNumber == id))
                    .Where(sale => sale is not null).Select(sale => sale!).ToArray();
                SetMetrics("إجمالي المبيعات", $"{numbers.Sum(sale => sale.Total):N0} ريال",
                    "معاملات البيع", numbers.Length.ToString("N0", CultureInfo.CurrentCulture),
                    "الكروت المباعة", numbers.Sum(sale => sale.Quantity).ToString("N0", CultureInfo.CurrentCulture));
                break;
            }
            case "batches":
            {
                var ids = rows.Select(row => row.ContextValue).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var batches = _issuanceService.Batches.Where(batch => ids.Contains(batch.BatchId)).ToArray();
                SetMetrics("عدد الدفعات", batches.Length.ToString("N0", CultureInfo.CurrentCulture),
                    "عدد الكروت المُنشأة", batches.Sum(batch => batch.Quantity).ToString("N0", CultureInfo.CurrentCulture),
                    "قيمة الإصدار المسجلة", $"{batches.Sum(batch => batch.Total):N0} ريال");
                break;
            }
            case "printing":
                SetMetrics("عناصر في قائمة الطباعة", rows.Count(row => row.Value7 == "بانتظار الطباعة").ToString("N0", CultureInfo.CurrentCulture),
                    "مهام أُرسلت للطابعة", rows.Count(row => row.Value7 == "أُرسلت إلى الطابعة").ToString("N0", CultureInfo.CurrentCulture),
                    "إجمالي السجلات", rows.Count.ToString("N0", CultureInfo.CurrentCulture));
                break;
            case "usage":
                SetMetrics("الوقت المستخدم", "غير متاح", "حركة البيانات", "غير متاحة", "الجلسات", "مصدر غير متاح");
                break;
            case "actions":
            case "sync":
                SetMetrics("عدد العمليات", rows.Count.ToString("N0", CultureInfo.CurrentCulture),
                    "العناصر المرتبطة", rows.Sum(row => int.TryParse(row.Value4, NumberStyles.Any, CultureInfo.CurrentCulture, out var count) ? count : 0).ToString("N0", CultureInfo.CurrentCulture),
                    "نوع السجل", reportKey == "sync" ? "الربط" : "إجراءات النظام");
                break;
            default:
                SetMetrics("عدد الكروت", rows.Count.ToString("N0", CultureInfo.CurrentCulture),
                    "الدفعات المرتبطة", rows.Select(row => row.Value5).Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString("N0", CultureInfo.CurrentCulture),
                    "قيد الاستخدام", rows.Count(row => row.Value6 == "قيد الاستخدام").ToString("N0", CultureInfo.CurrentCulture));
                break;
        }
    }

    private void SetMetrics(string l1, string v1, string l2, string v2, string l3, string v3)
    {
        MetricOneLabelTextBlock.Text = l1; MetricOneValueTextBlock.Text = v1;
        MetricTwoLabelTextBlock.Text = l2; MetricTwoValueTextBlock.Text = v2;
        MetricThreeLabelTextBlock.Text = l3; MetricThreeValueTextBlock.Text = v3;
    }

    private static bool WithinRange(DateTime value, DateTime? from, DateTime? to) =>
        (!from.HasValue || value.Date >= from.Value.Date) && (!to.HasValue || value.Date <= to.Value.Date);

    private static bool Matches(string search, params string?[] fields) =>
        search.Length == 0 || fields.Any(value => !string.IsNullOrWhiteSpace(value) && value.Contains(search, StringComparison.CurrentCultureIgnoreCase));

    private static bool MatchesSystem(string targetSystem, string sourceKey) => sourceKey == "all" || SystemKey(targetSystem) == sourceKey;

    private static string SystemKey(string targetSystem) =>
        targetSystem.Contains("hotspot", StringComparison.OrdinalIgnoreCase) || targetSystem.Contains("هوتسبوت", StringComparison.OrdinalIgnoreCase)
            ? "hotspot" : "user-manager";

    private void ReportGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        // Keep the WPF DataGrid's normal sortable-column behavior.
    }

    private void ReportGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ReportGrid.SelectedItem is not ReportTableRow row || string.IsNullOrWhiteSpace(row.ContextValue)) return;
        switch (row.ContextKind)
        {
            case "card":
                NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("network-users", "كل الحسابات", row.ContextValue, "card", row.SystemKey));
                break;
            case "batch-details":
                NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("cards", "إدارة الدفعات", row.ContextValue, "batch-details", row.SystemKey));
                break;
            case "batch":
                NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("network-users", "كل الحسابات", row.ContextValue, "batch", row.SystemKey));
                break;
            case "sale":
                NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("sales", "معاملات البيع", row.ContextValue, "sale"));
                break;
        }
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_visibleRows.Length == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "لا توجد نتائج لتصديرها ضمن الفلاتر الحالية.", "مركز التقارير", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "تصدير التقرير",
            Filter = "CSV UTF-8 (*.csv)|*.csv",
            DefaultExt = ".csv",
            AddExtension = true,
            FileName = $"Nexora-{_reportKey}-{DateTime.Now:yyyyMMdd-HHmm}.csv"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        try
        {
            var visibleColumns = ReportGrid.Columns.Where(column => column.Visibility == Visibility.Visible).OrderBy(column => column.DisplayIndex).ToArray();
            var headers = visibleColumns.Select(column => Convert.ToString(column.Header, CultureInfo.CurrentCulture) ?? string.Empty).ToArray();
            var valueIndexes = visibleColumns.Select(GetReportColumnValueIndex).ToArray();
            var csv = new StringBuilder();
            csv.AppendLine(string.Join(",", headers.Select(Csv)));
            foreach (var row in _visibleRows)
                csv.AppendLine(string.Join(",", valueIndexes.Select(index => Csv(row.Values[index]))));
            File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(true));
            ResultSummaryTextBlock.Text = $"تم تصدير {_visibleRows.Length:N0} نتيجة إلى الملف المحدد.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), "تعذر تصدير التقرير: " + ex.Message, "مركز التقارير", MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
        }
    }

    private void PrintButton_Click(object sender, RoutedEventArgs e)
    {
        if (_visibleRows.Length == 0) return;
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;
        try
        {
            var printable = new Grid
            {
                Width = dialog.PrintableAreaWidth,
                Background = System.Windows.Media.Brushes.White,
                FlowDirection = FlowDirection.RightToLeft
            };
            printable.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            printable.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var title = new TextBlock { Text = $"NEXORA · {Convert.ToString(ReportTypeComboBox.Text, CultureInfo.CurrentCulture)}", FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12) };
            Grid.SetRow(title, 0); printable.Children.Add(title);
            var table = new DataGrid
            {
                ItemsSource = _visibleRows,
                AutoGenerateColumns = false,
                IsReadOnly = true,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                FlowDirection = FlowDirection.RightToLeft,
                Width = dialog.PrintableAreaWidth
            };
            foreach (var column in ReportGrid.Columns.Where(column => column.Visibility == Visibility.Visible).OrderBy(column => column.DisplayIndex))
            {
                var binding = new System.Windows.Data.Binding($"Value{GetReportColumnValueIndex(column) + 1}");
                table.Columns.Add(new DataGridTextColumn { Header = column.Header, Binding = binding, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            }
            Grid.SetRow(table, 1); printable.Children.Add(table);
            printable.Measure(new Size(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight));
            printable.Arrange(new Rect(new Point(0, 0), printable.DesiredSize));
            printable.UpdateLayout();
            dialog.PrintVisual(printable, "Nexora Report");
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), "تعذرت طباعة التقرير: " + ex.Message, "مركز التقارير", MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
        }
    }

    private int GetReportColumnValueIndex(DataGridColumn column)
    {
        var member = column.SortMemberPath;
        if (!string.IsNullOrWhiteSpace(member) && member.StartsWith("Value", StringComparison.Ordinal) &&
            int.TryParse(member.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index is >= 1 and <= 7)
            return index - 1;
        return Math.Clamp(ReportColumnFallbackIndex(column), 0, 6);
    }

    private int ReportColumnFallbackIndex(DataGridColumn column) => Math.Max(0, ReportGrid.Columns.IndexOf(column));

    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    public void FocusTransaction(string transactionNumber)
    {
        ReportTypeComboBox.SelectedIndex = 2;
        SearchTextBox.Text = transactionNumber;
        RefreshReport();
    }

    private sealed record ReportTableRow(
        string Value1, string Value2, string Value3, string Value4, string Value5, string Value6, string Value7,
        string ContextValue, string ContextKind, string? SystemKey)
    {
        public IReadOnlyList<string> Values => [Value1, Value2, Value3, Value4, Value5, Value6, Value7];
    }
}
