using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Shared;

namespace Nexora.Demo.Desktop.Features.Cards.Views;

/// <summary>
/// Demo-only exception/work inbox. It reflects incomplete local card workflows and never contacts a router.
/// </summary>
public partial class PendingCardsView : UserControl
{
    private readonly DemoCardIssuanceService _issuanceService;
    private readonly DemoAccountSystem _targetSystem;
    private readonly DemoCardWorkflowStore _workflowStore = new();
    private bool _isSubscribed;
    private bool _isReady;
    private bool _isRefreshingPackageOptions;
    private int _pageSize = 9;
    private int _currentPage = 1;
    private List<PendingCardRow> _allRows = new();
    private List<PendingCardRow> _filteredRows = new();

    private static readonly Brush ReviewBackground = FreezeBrush(Color.FromRgb(0xFC, 0xE8, 0xEB));
    private static readonly Brush ReviewForeground = FreezeBrush(Color.FromRgb(0xB4, 0x23, 0x32));
    private static readonly Brush SyncBackground = FreezeBrush(Color.FromRgb(0xFF, 0xF3, 0xD9));
    private static readonly Brush SyncForeground = FreezeBrush(Color.FromRgb(0xA8, 0x5A, 0x00));
    private static readonly Brush PrintBackground = FreezeBrush(Color.FromRgb(0xE9, 0xF2, 0xFF));
    private static readonly Brush PrintForeground = FreezeBrush(Color.FromRgb(0x24, 0x5E, 0xA8));
    private static readonly Brush QueueBackground = FreezeBrush(Color.FromRgb(0xE8, 0xF7, 0xEE));
    private static readonly Brush QueueForeground = FreezeBrush(Color.FromRgb(0x18, 0x74, 0x48));
    private static readonly Brush BothBackground = FreezeBrush(Color.FromRgb(0xFF, 0xEF, 0xE0));
    private static readonly Brush BothForeground = FreezeBrush(Color.FromRgb(0x9A, 0x4A, 0x08));

    public event EventHandler<FeatureNavigationRequestedEventArgs>? NavigationRequested;

    public PendingCardsView(
        DemoCardIssuanceService issuanceService,
        DemoAccountSystem targetSystem)
    {
        ArgumentNullException.ThrowIfNull(issuanceService);

        InitializeComponent();

        _issuanceService = issuanceService;
        _targetSystem = targetSystem;

        SearchPlaceholderTextBlock.Visibility = string.IsNullOrWhiteSpace(SearchTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        _isReady = true;
        Loaded += PendingCardsView_Loaded;
        Unloaded += PendingCardsView_Unloaded;
        RefreshRows();
    }

    private void PendingCardsView_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_isSubscribed)
        {
            _issuanceService.ScenarioChanged += IssuanceService_ScenarioChanged;
            _isSubscribed = true;
        }

        RefreshRows();
    }

    private void PendingCardsView_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_isSubscribed)
        {
            _issuanceService.ScenarioChanged -= IssuanceService_ScenarioChanged;
            _isSubscribed = false;
        }
    }

    private void IssuanceService_ScenarioChanged(object? sender, EventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (IsLoaded)
                RefreshRows();
        });
    }

    private void RefreshRows()
    {
        var systemKey = _targetSystem == DemoAccountSystem.HotSpot ? "hotspot" : "user-manager";
        var targetName = _targetSystem == DemoAccountSystem.HotSpot ? "مستخدمو الهوتسبوت" : "مدير المستخدمين";

        var statusByKey = _workflowStore.GetCardWorkflowStatuses()
            .GroupBy(item => MakeKey(item.TargetSystem, item.BatchId, item.CardNumber), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.UpdatedAt).First(),
                StringComparer.OrdinalIgnoreCase);

        var rows = new List<PendingCardRow>();
        foreach (var card in _issuanceService.Cards
                     .Where(item => string.Equals(item.TargetSystem, targetName, StringComparison.Ordinal) && !item.IsArchived)
                     .OrderByDescending(item => item.CreatedAt))
        {
            statusByKey.TryGetValue(MakeKey(systemKey, card.BatchId, card.CardNumber), out var workflow);

            var needsSync = workflow is null || !workflow.RouterUploadSimulated;
            var needsPrint = workflow is null || !workflow.PrintSubmitted;
            var printQueued = workflow?.PrintQueued ?? false;
            var needsReview = RequiresReview(card, workflow);

            // The inbox is for unresolved work only. Completed normal workflows remain outside this list.
            if (!needsSync && !needsPrint && !needsReview)
                continue;

            var statusLabel = GetStatusLabel(needsReview, needsSync, needsPrint, printQueued);
            var pendingAction = GetPendingActionLabel(card, workflow, needsReview, needsSync, needsPrint, printQueued);
            var actionReason = GetPendingActionShort(needsReview, needsSync, needsPrint, printQueued);
            var lastAction = workflow?.LastAction;
            if (string.IsNullOrWhiteSpace(lastAction))
                lastAction = "لا يوجد";

            var updatedAt = workflow?.UpdatedAt ?? card.CreatedAt;
            var updatedAtLabel = updatedAt.ToLocalTime().ToString("HH:mm yyyy/MM/dd", CultureInfo.InvariantCulture);
            var badgeColors = GetBadgeColors(needsReview, needsSync, needsPrint, printQueued);

            rows.Add(new PendingCardRow(
                card,
                card.BatchId,
                card.CardNumber,
                card.Username,
                card.PackageName,
                statusLabel,
                pendingAction,
                actionReason,
                lastAction,
                updatedAtLabel,
                updatedAt,
                needsSync,
                needsPrint,
                printQueued,
                needsReview,
                badgeColors.Background,
                badgeColors.Foreground));
        }

        _allRows = rows
            .OrderByDescending(row => row.NeedsReview)
            .ThenByDescending(row => row.NeedsSync && row.NeedsPrint)
            .ThenByDescending(row => row.UpdatedAt)
            .ThenByDescending(row => row.Card.CreatedAt)
            .ToList();

        PendingCountTextBlock.Text = _allRows.Count.ToString("N0", CultureInfo.CurrentCulture);
        ReviewNeededCountTextBlock.Text = _allRows.Count(row => row.NeedsReview).ToString("N0", CultureInfo.CurrentCulture);
        SyncNeededCountTextBlock.Text = _allRows.Count(row => row.NeedsSync).ToString("N0", CultureInfo.CurrentCulture);
        PrintNeededCountTextBlock.Text = _allRows.Count(row => row.NeedsPrint && !row.PrintQueued).ToString("N0", CultureInfo.CurrentCulture);
        PrintQueueCountTextBlock.Text = _allRows.Count(row => row.PrintQueued).ToString("N0", CultureInfo.CurrentCulture);

        RefreshPackageFilterOptions();
        ApplyFilters();
    }

    private void RefreshPackageFilterOptions()
    {
        if (PackageFilterComboBox is null)
            return;

        var selectedTag = (PackageFilterComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";
        _isRefreshingPackageOptions = true;
        try
        {
            PackageFilterComboBox.Items.Clear();
            PackageFilterComboBox.Items.Add(new ComboBoxItem { Content = "كل الباقات", Tag = "all" });

            foreach (var packageName in _allRows
                         .Select(row => row.PackageName)
                         .Where(name => !string.IsNullOrWhiteSpace(name))
                         .Distinct(StringComparer.CurrentCultureIgnoreCase)
                         .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase))
            {
                PackageFilterComboBox.Items.Add(new ComboBoxItem { Content = packageName, Tag = packageName });
            }

            var matchingItem = PackageFilterComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), selectedTag, StringComparison.CurrentCultureIgnoreCase));
            PackageFilterComboBox.SelectedItem = matchingItem ?? PackageFilterComboBox.Items[0];
        }
        finally
        {
            _isRefreshingPackageOptions = false;
        }
    }

    private void ApplyFilters()
    {
        if (!_isReady)
            return;

        var search = SearchTextBox.Text?.Trim() ?? string.Empty;
        var statusFilter = SelectedTag(StatusFilterComboBox, "all");
        var packageFilter = SelectedTag(PackageFilterComboBox, "all");
        var dateFilter = SelectedTag(DatePeriodFilterComboBox, "all");

        _filteredRows = _allRows
            .Where(row => MatchesStatusFilter(row, statusFilter))
            .Where(row => packageFilter == "all" || string.Equals(row.PackageName, packageFilter, StringComparison.CurrentCultureIgnoreCase))
            .Where(row => MatchesDateFilter(row, dateFilter))
            .Where(row => search.Length == 0 || MatchesSearch(row, search))
            .ToList();

        var totalPages = Math.Max(1, (int)Math.Ceiling(_filteredRows.Count / (double)_pageSize));
        _currentPage = Math.Clamp(_currentPage, 1, totalPages);
        var startIndex = (_currentPage - 1) * _pageSize;
        var visibleRows = _filteredRows.Skip(startIndex).Take(_pageSize).ToList();

        PendingCardsGrid.ItemsSource = visibleRows;
        EmptyStateBorder.Visibility = _filteredRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var firstItem = _filteredRows.Count == 0 ? 0 : startIndex + 1;
        var lastItem = Math.Min(startIndex + _pageSize, _filteredRows.Count);
        ResultSummaryTextBlock.Text = _filteredRows.Count == 0
            ? "لا توجد نتائج مطابقة"
            : $"عرض {firstItem:N0}–{lastItem:N0} من {_filteredRows.Count:N0} معلّق";
        BuildPagerButtons(totalPages);
        UpdateBulkActionButtons();
    }

    private static string SelectedTag(ComboBox comboBox, string fallback) =>
        (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;

    private static bool MatchesStatusFilter(PendingCardRow row, string filter) => filter switch
    {
        "review" => row.NeedsReview,
        "sync" => row.CanRetrySync,
        "print" => row.CanQueuePrint,
        "queued" => row.PrintQueued,
        "both" => row.CanRetrySync && row.CanQueuePrint,
        _ => true
    };

    private static bool MatchesDateFilter(PendingCardRow row, string filter)
    {
        var updated = row.UpdatedAt.ToLocalTime();
        var now = DateTimeOffset.Now;
        return filter switch
        {
            "today" => updated.Date == now.Date,
            "7-days" => updated >= now.AddDays(-7),
            "30-days" => updated >= now.AddDays(-30),
            _ => true
        };
    }

    private static bool MatchesSearch(PendingCardRow row, string search)
    {
        var comparison = StringComparison.CurrentCultureIgnoreCase;
        return row.BatchId.Contains(search, comparison)
            || row.CardNumber.Contains(search, comparison)
            || row.Username.Contains(search, comparison)
            || row.PackageName.Contains(search, comparison)
            || row.StatusLabel.Contains(search, comparison)
            || row.ActionReasonLabel.Contains(search, comparison)
            || row.PendingActionLabel.Contains(search, comparison)
            || row.LastActionLabel.Contains(search, comparison);
    }

    private List<PendingCardRow> GetSelectedRows() =>
        PendingCardsGrid.SelectedItems.OfType<PendingCardRow>().ToList();

    private void UpdateBulkActionButtons()
    {
        if (RetrySyncButton is null || QueuePrintButton is null || PendingCardsGrid is null)
            return;

        var selected = GetSelectedRows();
        RetrySyncButton.IsEnabled = selected.Any(row => row.CanRetrySync);
        QueuePrintButton.IsEnabled = selected.Any(row => row.CanQueuePrint);
    }

    private void PendingCardsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateBulkActionButtons();

    private void RetrySyncButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedRows().Where(row => row.CanRetrySync).ToList();
        if (selected.Count == 0)
        {
            const string message = "حدد عناصر تحتاج إلى مزامنة أولاً.";
            SetStatus(message, true);
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "لا توجد عناصر للمزامنة", message, WorkflowFeedbackKind.Warning, null,
                new WorkflowFeedbackAction("close", "حسنًا", true));
            return;
        }

        try
        {
            // This project only records an offline request; it does not write accounts to RouterOS.
            _workflowStore.RecordRouterUpload(selected.Select(row => row.Card));
            RefreshRows();
            SetStatus($"تم تسجيل طلب مزامنة محلي لـ {selected.Count:N0} كرت؛ لم يتم الاتصال بالراوتر.", false);
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "تم تسجيل الطلب محليًا",
                "حُفظت محاولة المزامنة في سجل Nexora فقط. المزامنة الفعلية تتطلب موصل كتابة للراوتر غير متوفر في هذا المسار.",
                WorkflowFeedbackKind.Warning,
                [new("عدد الكروت", selected.Count.ToString("N0")), new("حالة الراوتر", "لم تتغير")],
                new WorkflowFeedbackAction("close", "مفهوم", true));
        }
        catch (Exception ex)
        {
            SetStatus("تعذر تسجيل محاولة المزامنة التجريبية: " + ex.Message, true);
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "تعذر تسجيل الطلب", ex.Message, WorkflowFeedbackKind.Error, null,
                new WorkflowFeedbackAction("close", "إغلاق", true));
        }
    }

    private void QueuePrintButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedRows().Where(row => row.CanQueuePrint).ToList();
        if (selected.Count == 0)
        {
            const string message = "حدد عناصر تحتاج إلى الطباعة وليست موجودة بالفعل في قائمة الطباعة.";
            SetStatus(message, true);
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "لا توجد عناصر للطباعة", message, WorkflowFeedbackKind.Warning, null,
                new WorkflowFeedbackAction("close", "حسنًا", true));
            return;
        }

        try
        {
            _workflowStore.QueuePrintSelection(selected.Select(row => row.Card));
            RefreshRows();
            SetStatus($"تمت إضافة {selected.Count:N0} كرت إلى قائمة الطباعة.", false);
            var choice = WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "تم تجهيز الكروت للطباعة",
                "تم حفظ اختيار الكروت في قائمة الطباعة. لم يتم إرسالها للطابعة بعد.", WorkflowFeedbackKind.Success,
                [new("عدد الكروت", selected.Count.ToString("N0")), new("الحالة", "بانتظار المعاينة والإرسال")],
                new WorkflowFeedbackAction("open", "فتح قائمة الطباعة", true), new WorkflowFeedbackAction("close", "لاحقًا"));
            if (choice == "open")
                NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("printing", "قائمة الطباعة"));
        }
        catch (Exception ex)
        {
            SetStatus("تعذرت إضافة العناصر إلى قائمة الطباعة: " + ex.Message, true);
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "تعذر تجهيز الطباعة", ex.Message, WorkflowFeedbackKind.Error, null,
                new WorkflowFeedbackAction("close", "إغلاق", true));
        }
    }

    private void RowActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not PendingCardRow row)
            return;

        var action = button.Tag?.ToString();
        try
        {
            switch (action)
            {
                case "review" when row.NeedsReview:
                    SetStatus("تم فتح إدارة الكروت لمراجعة العنصر المحدد.", false);
                    NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("cards", "إدارة الكروت"));
                    break;
                case "sync" when row.CanRetrySync:
                    _workflowStore.RecordRouterUpload(new[] { row.Card });
                    RefreshRows();
                    SetStatus($"تم تسجيل طلب مزامنة محلي للكرت {row.CardNumber}. لم يتم الاتصال بالراوتر.", false);
                    WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "طلب المزامنة محفوظ محليًا",
                        $"تم تسجيل الإجراء للكرت {row.CardNumber} في Nexora فقط؛ لم يتم إنشاء الحساب على الراوتر.",
                        WorkflowFeedbackKind.Warning, [new("الكرت", row.CardNumber), new("الحالة", "بانتظار موصل كتابة")],
                        new WorkflowFeedbackAction("close", "إغلاق", true));
                    break;
                case "print" when row.CanQueuePrint:
                    _workflowStore.QueuePrintSelection(new[] { row.Card });
                    RefreshRows();
                    SetStatus($"تمت إضافة الكرت {row.CardNumber} إلى قائمة الطباعة.", false);
                    var choice = WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "تم تجهيز الكرت للطباعة",
                        "أُضيف الكرت إلى القائمة المحلية؛ لم يتم إرسال مستند للطابعة.", WorkflowFeedbackKind.Success,
                        [new("الكرت", row.CardNumber)], new WorkflowFeedbackAction("open", "فتح قائمة الطباعة", true), new WorkflowFeedbackAction("close", "لاحقًا"));
                    if (choice == "open")
                        NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("printing", "قائمة الطباعة"));
                    break;
                case "queued" when row.PrintQueued:
                    SetStatus("تم فتح قائمة الطباعة لإكمال العنصر.", false);
                    NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("printing", "قائمة الطباعة"));
                    break;
            }
        }
        catch (Exception ex)
        {
            SetStatus("تعذر تنفيذ الإجراء التجريبي: " + ex.Message, true);
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "تعذر تنفيذ الإجراء", ex.Message, WorkflowFeedbackKind.Error, null,
                new WorkflowFeedbackAction("close", "إغلاق", true));
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshRows();
        SetStatus("تم تحديث قائمة المعلّقات.", false);
    }

    private void ClearFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        SearchTextBox.Clear();
        StatusFilterComboBox.SelectedIndex = 0;
        PackageFilterComboBox.SelectedIndex = 0;
        DatePeriodFilterComboBox.SelectedIndex = 0;
        _currentPage = 1;
        ApplyFilters();
        SetStatus("تم مسح البحث والفلاتر.", false);
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchPlaceholderTextBlock is not null)
            SearchPlaceholderTextBlock.Visibility = string.IsNullOrWhiteSpace(SearchTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        ApplyFilterChange();
    }

    private void FilterSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRefreshingPackageOptions)
            return;
        ApplyFilterChange();
    }

    private void ApplyFilterChange()
    {
        if (!_isReady)
            return;
        _currentPage = 1;
        ApplyFilters();
    }

    private void PageSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isReady || PageSizeComboBox.SelectedItem is not ComboBoxItem item)
            return;
        if (int.TryParse(item.Tag?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            && value is 9 or 18 or 27 or 45 or 90)
        {
            if (_pageSize == value)
                return;
            _pageSize = value;
            _currentPage = 1;
            ApplyFilters();
        }
    }

    private void BuildPagerButtons(int pageCount)
    {
        PagerButtonsPanel.Children.Clear();
        AddPagerButton("«", "first", _currentPage > 1, false, "الصفحة الأولى");
        AddPagerButton("‹", "previous", _currentPage > 1, false, "الصفحة السابقة");

        var pages = new SortedSet<int> { 1, pageCount };
        for (var page = Math.Max(1, _currentPage - 1); page <= Math.Min(pageCount, _currentPage + 1); page++)
            pages.Add(page);

        var previousPage = 0;
        foreach (var page in pages)
        {
            if (previousPage > 0 && page - previousPage > 1)
            {
                PagerButtonsPanel.Children.Add(new TextBlock
                {
                    Text = "…",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(5, 0, 5, 0),
                    Foreground = (Brush)FindResource("MutedBrush")
                });
            }

            AddPagerButton(page.ToString(CultureInfo.CurrentCulture), page.ToString(CultureInfo.InvariantCulture), true,
                page == _currentPage, $"الصفحة {page}");
            previousPage = page;
        }

        AddPagerButton("›", "next", _currentPage < pageCount, false, "الصفحة التالية");
        AddPagerButton("»", "last", _currentPage < pageCount, false, "الصفحة الأخيرة");
    }

    private void AddPagerButton(string label, string tag, bool enabled, bool current, string tooltip)
    {
        var button = new Button
        {
            Content = label,
            Tag = tag,
            IsEnabled = enabled,
            ToolTip = tooltip,
            Style = (Style)FindResource(current ? "PendingPagerButtonCurrent" : "PendingPagerButton"),
            Height = 29,
            MinWidth = 31,
            Margin = new Thickness(2, 0, 2, 0),
            Padding = new Thickness(5, 0, 5, 0)
        };
        button.Click += PagerButton_Click;
        PagerButtonsPanel.Children.Add(button);
    }

    private void PagerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string tag)
            return;

        var pageCount = Math.Max(1, (int)Math.Ceiling(_filteredRows.Count / (double)_pageSize));
        if (tag == "first") _currentPage = 1;
        else if (tag == "previous") _currentPage = Math.Max(1, _currentPage - 1);
        else if (tag == "next") _currentPage = Math.Min(pageCount, _currentPage + 1);
        else if (tag == "last") _currentPage = pageCount;
        else if (int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out var page))
            _currentPage = Math.Clamp(page, 1, pageCount);

        ApplyFilters();
    }

    private void SetStatus(string message, bool isError)
    {
        PendingStatusTextBlock.Text = message;
        PendingStatusTextBlock.Foreground = (Brush)FindResource(isError ? "DangerBrush" : "SuccessBrush");
    }

    private static bool RequiresReview(GeneratedDemoCard card, DemoCardWorkflowStatus? workflow)
    {
        if (string.Equals(card.DemoStatus, "موقوفة", StringComparison.OrdinalIgnoreCase)
            || string.Equals(card.DemoStatus, "موقوف", StringComparison.OrdinalIgnoreCase)
            || string.Equals(card.DemoStatus, "منتهية", StringComparison.OrdinalIgnoreCase)
            || string.Equals(card.DemoStatus, "منتهي", StringComparison.OrdinalIgnoreCase)
            || string.Equals(card.DemoStatus, "غير صالحة", StringComparison.OrdinalIgnoreCase)
            || string.Equals(card.DemoStatus, "مرفوضة", StringComparison.OrdinalIgnoreCase))
            return true;

        return HasFailureSignal(card.LocalStatus)
            || HasFailureSignal(card.Comment)
            || HasFailureSignal(workflow?.LastAction);
    }

    private static bool HasFailureSignal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string[] markers =
        [
            "فشل", "فاشل", "تعذر", "خطأ", "مرفوض", "مشكلة", "غير صالح", "غير مكتمل", "استثناء",
            "failed", "failure", "error", "rejected", "exception"
        ];
        return markers.Any(marker => value.Contains(marker, StringComparison.CurrentCultureIgnoreCase));
    }

    private static string GetStatusLabel(bool needsReview, bool needsSync, bool needsPrint, bool printQueued)
    {
        if (needsReview)
            return "تحتاج مراجعة";
        if (needsSync && needsPrint)
            return printQueued ? "مزامنة + قائمة طباعة" : "مزامنة وطباعة";
        if (needsSync)
            return "تحتاج مزامنة";
        if (printQueued)
            return "في قائمة الطباعة";
        if (needsPrint)
            return "تحتاج طباعة";
        return "تحتاج مراجعة";
    }

    private static string GetPendingActionShort(bool needsReview, bool needsSync, bool needsPrint, bool printQueued)
    {
        if (needsReview)
            return "مراجعة بيانات الكرت";
        if (needsSync && needsPrint)
            return printQueued ? "مزامنة الكرت" : "مزامنة ثم طباعة";
        if (needsSync)
            return "تسجيل المزامنة";
        if (printQueued)
            return "استكمال الطباعة";
        if (needsPrint)
            return "إضافة للطباعة";
        return "مراجعة العنصر";
    }

    private static string GetPendingActionLabel(GeneratedDemoCard card, DemoCardWorkflowStatus? workflow,
        bool needsReview, bool needsSync, bool needsPrint, bool printQueued)
    {
        if (needsReview)
        {
            if (string.Equals(card.DemoStatus, "موقوفة", StringComparison.OrdinalIgnoreCase))
                return "راجع حالة الكرت الموقوف قبل إكمال المسار.";
            if (HasFailureSignal(card.LocalStatus) || HasFailureSignal(workflow?.LastAction))
                return "يوجد فشل مسجل في الحالة أو آخر إجراء؛ راجع السجل قبل إعادة المحاولة.";
            if (HasFailureSignal(card.Comment))
                return "توجد ملاحظة تحتاج مراجعة قبل إكمال هذا العنصر.";
            return "راجع بيانات الكرت وحالته قبل إكمال المسار.";
        }
        if (needsSync && needsPrint)
            return printQueued
                ? "تمت إضافة الكرت إلى قائمة الطباعة، وما زالت المزامنة التجريبية غير مسجلة."
                : "سجّل المزامنة التجريبية ثم أضف الكرت إلى قائمة الطباعة.";
        if (needsSync)
            return "لم تُسجّل المزامنة التجريبية لهذا الكرت بعد.";
        if (printQueued)
            return "الكرت موجود في قائمة الطباعة وينتظر استكمال الإرسال للطابعة.";
        if (needsPrint)
            return "لم يُسجّل إرسال هذا الكرت للطباعة بعد.";
        return "تحقق من سجل الكرت.";
    }

    private static (Brush Background, Brush Foreground) GetBadgeColors(bool needsReview, bool needsSync, bool needsPrint, bool printQueued)
    {
        if (needsReview)
            return (ReviewBackground, ReviewForeground);
        if (needsSync && needsPrint)
            return (BothBackground, BothForeground);
        if (needsSync)
            return (SyncBackground, SyncForeground);
        if (printQueued)
            return (QueueBackground, QueueForeground);
        return (PrintBackground, PrintForeground);
    }

    private static Brush FreezeBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static string MakeKey(string systemKey, string batchId, string cardNumber) =>
        $"{NormalizeSystemKey(systemKey)}\u001F{batchId}\u001F{cardNumber}";

    private static string NormalizeSystemKey(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && (value.Contains("hotspot", StringComparison.OrdinalIgnoreCase)
            || value.Contains("هوتسبوت", StringComparison.OrdinalIgnoreCase))
            ? "hotspot"
            : "user-manager";

    private sealed record PendingCardRow(
        GeneratedDemoCard Card,
        string BatchId,
        string CardNumber,
        string Username,
        string PackageName,
        string StatusLabel,
        string PendingActionLabel,
        string ActionReasonLabel,
        string LastActionLabel,
        string UpdatedAtLabel,
        DateTimeOffset UpdatedAt,
        bool NeedsSync,
        bool NeedsPrint,
        bool PrintQueued,
        bool NeedsReview,
        Brush BadgeBackground,
        Brush BadgeForeground)
    {
        public bool CanRetrySync => NeedsSync && !NeedsReview;
        public bool CanQueuePrint => NeedsPrint && !PrintQueued && !NeedsReview;
    }
}
