using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Shared;

namespace Nexora.Demo.Desktop.Features.Cards.Views;

/// <summary>
/// Management projection over the same locally persisted records used by issuance,
/// import, batches, and printing. It does not create a second card store.
/// </summary>
public partial class CardManagementView : UserControl
{
    private readonly DemoCardIssuanceService _issuanceService;
    private readonly DemoCardWorkflowStore _workflowStore = new();
    private readonly DemoAccountSystem _targetSystem;
    private readonly string _targetName;
    private List<CardManagementRow> _rows = [];
    private ListCollectionView _view;
    private bool _filtersReady;
    private bool _suppressFilters;
    private bool _isSubscribed;
    private int _duplicateSourceRecords;

    public CardManagementView(DemoCardIssuanceService issuanceService, DemoAccountSystem targetSystem)
    {
        ArgumentNullException.ThrowIfNull(issuanceService);
        _issuanceService = issuanceService;
        _targetSystem = targetSystem;
        _targetName = targetSystem == DemoAccountSystem.HotSpot ? "مستخدمو الهوتسبوت" : "مدير المستخدمين";

        InitializeComponent();
        TargetSystemTextBlock.Text = targetSystem == DemoAccountSystem.HotSpot
            ? "النظام الحالي: هوتسبوت"
            : "النظام الحالي: يوزر منجر";

        _view = new ListCollectionView(_rows);
        _view.Filter = MatchesCard;
        CardsDataGrid.ItemsSource = _view;
        _filtersReady = true;
        RefreshFromSource(preserveSelection: false);
        Loaded += CardManagementView_Loaded;
        Unloaded += CardManagementView_Unloaded;
    }

    public event EventHandler<FeatureNavigationRequestedEventArgs>? NavigationRequested;

    private void CardManagementView_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_isSubscribed)
        {
            _issuanceService.ScenarioChanged += IssuanceService_ScenarioChanged;
            _isSubscribed = true;
        }
        RefreshFromSource(preserveSelection: true);
    }

    private void CardManagementView_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_isSubscribed)
        {
            _issuanceService.ScenarioChanged -= IssuanceService_ScenarioChanged;
            _isSubscribed = false;
        }
    }

    private void IssuanceService_ScenarioChanged(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(() => RefreshFromSource(preserveSelection: true));

    private void RefreshFromSource(bool preserveSelection)
    {
        try
        {
            var selectedKeys = preserveSelection
                ? _rows.Where(row => row.IsMarked).Select(row => IdentityKey(row.Card)).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var currentSystemCards = _issuanceService.Cards
                .Where(card => string.Equals(card.TargetSystem, _targetName, StringComparison.Ordinal))
                .ToList();

            var grouped = currentSystemCards
                .GroupBy(IdentityKey, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _duplicateSourceRecords = grouped.Sum(group => Math.Max(0, group.Count() - 1));
            var uniqueCards = grouped
                .Select(group => group.OrderByDescending(card => card.CreatedAt).First())
                .OrderByDescending(card => card.CreatedAt)
                .ToList();

            var workflowByKey = _workflowStore.GetCardWorkflowStatuses()
                .GroupBy(WorkflowKey, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.OrderByDescending(item => item.UpdatedAt).First(), StringComparer.OrdinalIgnoreCase);

            _suppressFilters = true;
            try
            {
                RefreshDynamicFilterOptions(uniqueCards);
            }
            finally
            {
                _suppressFilters = false;
            }

            _rows = new List<CardManagementRow>(uniqueCards.Count);
            foreach (var card in uniqueCards)
            {
                workflowByKey.TryGetValue(WorkflowKey(card), out var workflow);
                var row = new CardManagementRow(card, workflow, selectedKeys.Contains(IdentityKey(card)));
                row.PropertyChanged += ManagementRow_PropertyChanged;
                _rows.Add(row);
            }

            _view = new ListCollectionView(_rows) { Filter = MatchesCard };
            CardsDataGrid.ItemsSource = _view;
            _view.Refresh();
            UpdateMetrics();
            UpdateFooter();

            var notices = new List<string>();
            if (!string.IsNullOrWhiteSpace(_issuanceService.StorageWarning))
                notices.Add("تعذر الوصول إلى سجل الكروت أو حفظه.");
            if (!string.IsNullOrWhiteSpace(_workflowStore.StorageWarning))
                notices.Add("تعذر تحديث سجل إجراءات الطباعة.");
            if (_duplicateSourceRecords > 0)
                notices.Add($"اكتُشفت {_duplicateSourceRecords:N0} سجلات مكررة في المصدر؛ عُرض كل رقم كرت مرة واحدة لتجنب التكرار.");
            if (notices.Count > 0)
            {
                OperationStatusTextBlock.Text = string.Join(" ", notices);
                OperationStatusTextBlock.Foreground = (Brush)FindResource("WarningBrush");
            }
            else
            {
                OperationStatusTextBlock.Text = string.Empty;
            }
        }
        catch (Exception)
        {
            OperationStatusTextBlock.Text = "تعذر تحديث سجل الكروت. حاول التحديث مرة أخرى.";
            OperationStatusTextBlock.Foreground = (Brush)FindResource("DangerBrush");
            ResultSummaryTextBlock.Text = "لم يكتمل تحديث السجل.";
        }
    }

    private void RefreshDynamicFilterOptions(IReadOnlyCollection<GeneratedDemoCard> sourceCards)
    {
        var selectedPackage = SelectedTag(PackageFilterComboBox);
        var selectedBatch = SelectedTag(BatchFilterComboBox);

        PackageFilterComboBox.Items.Clear();
        PackageFilterComboBox.Items.Add(new ComboBoxItem { Content = "كل الباقات", Tag = "all" });
        foreach (var package in sourceCards.Select(card => card.PackageName)
                     .Where(value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.CurrentCultureIgnoreCase)
                     .OrderBy(value => value, StringComparer.CurrentCulture))
        {
            PackageFilterComboBox.Items.Add(new ComboBoxItem { Content = package, Tag = package });
        }
        SetComboTag(PackageFilterComboBox, selectedPackage);

        var currentBatchIds = _issuanceService.Batches
            .Where(batch => string.Equals(batch.TargetSystem, _targetName, StringComparison.Ordinal))
            .OrderByDescending(batch => batch.CreatedAt)
            .Select(batch => batch.BatchId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        BatchFilterComboBox.Items.Clear();
        BatchFilterComboBox.Items.Add(new ComboBoxItem { Content = "كل الدفعات", Tag = "all" });
        foreach (var batchId in currentBatchIds)
            BatchFilterComboBox.Items.Add(new ComboBoxItem { Content = batchId, Tag = batchId });
        SetComboTag(BatchFilterComboBox, selectedBatch);
    }

    private static string SelectedTag(ComboBox combo) =>
        (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";

    private static void SetComboTag(ComboBox combo, string tag)
    {
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
            string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            ?? combo.Items.OfType<ComboBoxItem>().FirstOrDefault();
    }

    private bool MatchesCard(object item)
    {
        if (item is not CardManagementRow row) return false;
        var card = row.Card;

        var query = CardSearchTextBox?.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(query) &&
            !Contains(card.CardNumber, query) &&
            !Contains(card.Username, query) &&
            !Contains(card.BatchId, query) &&
            !Contains(card.PackageName, query))
            return false;

        var status = SelectedTag(CardStatusFilterComboBox);
        if (status == "archived" && !card.IsArchived) return false;
        if (status != "all" && status != "archived" && (card.IsArchived || !string.Equals(card.DemoStatus, status, StringComparison.Ordinal))) return false;

        var package = SelectedTag(PackageFilterComboBox);
        if (package != "all" && !string.Equals(card.PackageName, package, StringComparison.OrdinalIgnoreCase)) return false;

        var batch = SelectedTag(BatchFilterComboBox);
        if (batch != "all" && !string.Equals(card.BatchId, batch, StringComparison.OrdinalIgnoreCase)) return false;

        var source = SelectedTag(SourceFilterComboBox);
        if (source != "all" && !string.Equals(row.SourceKey, source, StringComparison.Ordinal)) return false;

        var print = SelectedTag(PrintFilterComboBox);
        if (print == "queued" && row.Workflow?.PrintQueued != true) return false;
        if (print == "submitted" && row.Workflow?.PrintSubmitted != true) return false;
        if (print == "not-scheduled" && (row.Workflow?.PrintQueued == true || row.Workflow?.PrintSubmitted == true || row.Workflow?.PrintPrepared == true)) return false;
        return true;
    }

    private static bool Contains(string? source, string value) =>
        !string.IsNullOrEmpty(source) && source.Contains(value, StringComparison.CurrentCultureIgnoreCase);

    private void Filter_Changed(object sender, EventArgs e)
    {
        if (!_filtersReady || _suppressFilters || _view is null) return;
        _view.Refresh();
        UpdateFooter();
    }

    private void ClearFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        _suppressFilters = true;
        try
        {
            CardSearchTextBox.Clear();
            SetComboTag(CardStatusFilterComboBox, "all");
            SetComboTag(PackageFilterComboBox, "all");
            SetComboTag(BatchFilterComboBox, "all");
            SetComboTag(SourceFilterComboBox, "all");
            SetComboTag(PrintFilterComboBox, "all");
        }
        finally
        {
            _suppressFilters = false;
        }
        _view.Refresh();
        UpdateFooter();
        SetOperationStatus("تمت إعادة الفلاتر إلى القيم الافتراضية.", false);
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshFromSource(preserveSelection: true);
        SetOperationStatus("تم تحديث السجل.", false);
    }

    private void SelectVisibleButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _view.Cast<object>().OfType<CardManagementRow>())
            row.IsMarked = true;
        UpdateFooter();
    }

    private void ClearSelectionButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _rows.Where(row => row.IsMarked).ToList())
            row.IsMarked = false;
        CardsDataGrid.UnselectAll();
        UpdateFooter();
    }

    private List<CardManagementRow> GetMarkedRows() => _rows.Where(row => row.IsMarked).ToList();

    private void ManagementRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CardManagementRow.IsMarked))
            Dispatcher.InvokeAsync(UpdateFooter);
    }

    private void UpdateMetrics()
    {
        AvailableCardsCountTextBlock.Text = _rows.Count(row => !row.Card.IsArchived && string.Equals(row.Card.DemoStatus, "متاحة", StringComparison.Ordinal)).ToString("N0", CultureInfo.CurrentCulture);
        ReviewCardsCountTextBlock.Text = _rows.Count(row => row.NeedsReview).ToString("N0", CultureInfo.CurrentCulture);
        TotalCardsCountTextBlock.Text = _rows.Count.ToString("N0", CultureInfo.CurrentCulture);
    }

    private void UpdateFooter()
    {
        if (ResultSummaryTextBlock is null || CardsDataGrid is null) return;
        var visibleCount = _view.Cast<object>().OfType<CardManagementRow>().Count();
        var selected = GetMarkedRows();
        var visibleSelected = _view.Cast<object>().OfType<CardManagementRow>().Count(row => row.IsMarked);
        var batchCount = _issuanceService.Batches.Count(batch => string.Equals(batch.TargetSystem, _targetName, StringComparison.Ordinal));
        ResultSummaryTextBlock.Text = $"المعروض: {visibleCount:N0} من {_rows.Count:N0} كرت · الدفعات ضمن النظام: {batchCount:N0}";
        SelectedCountTextBlock.Text = selected.Count == visibleSelected
            ? $"المحدد: {selected.Count:N0}"
            : $"المحدد: {selected.Count:N0} (داخل النتائج: {visibleSelected:N0})";
        BulkActionsBorder.Visibility = selected.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        BulkArchiveButton.Visibility = selected.Any(row => !row.Card.IsArchived) ? Visibility.Visible : Visibility.Collapsed;
        BulkRestoreButton.Visibility = selected.Any(row => row.Card.IsArchived) ? Visibility.Visible : Visibility.Collapsed;
        EmptyStatePanel.Visibility = visibleCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyStateDescriptionTextBlock.Text = _rows.Count == 0
            ? "لا توجد كروت مسجلة لهذا النظام حتى الآن. يمكنك إصدار كروت أو استيراد سجل موجود."
            : "لا توجد سجلات تطابق البحث أو الفلاتر الحالية. امسح الفلاتر لعرض السجل مرة أخرى.";
        CardsDataGrid.Visibility = visibleCount == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void CardsDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (CardsDataGrid.SelectedItem is CardManagementRow row) ShowCardDetails(row.Card);
    }

    private void RowDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is CardManagementRow row)
            ShowCardDetails(row.Card);
    }

    private void RowBatchButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is CardManagementRow row)
            OpenBatch(row.Card);
    }

    private void OpenBatch(GeneratedDemoCard card) =>
        NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("cards", "إدارة الدفعات", card.BatchId, "batch", SystemKey(card.TargetSystem)));

    private void RowActionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not CardManagementRow row) return;
        var card = row.Card;
        var menu = new ContextMenu { FlowDirection = FlowDirection.RightToLeft, PlacementTarget = button, Placement = PlacementMode.Bottom };
        AddMenuItem(menu, "عرض التفاصيل", () => ShowCardDetails(card));
        AddMenuItem(menu, "فتح الدفعة الأصلية", () => OpenBatch(card));
        AddMenuItem(menu, row.Workflow?.PrintQueued == true ? "فتح قائمة الطباعة" : "إضافة إلى قائمة الطباعة", () => QueueForPrinting(new[] { card }));
        menu.Items.Add(new Separator());
        if (card.IsArchived)
            AddMenuItem(menu, "استعادة من الأرشيف", () => RestoreCards(new[] { card }));
        else
            AddMenuItem(menu, "أرشفة", () => ArchiveCards(new[] { card }));

        button.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private static void AddMenuItem(ContextMenu menu, string label, Action action)
    {
        var item = new MenuItem { Header = label, Padding = new Thickness(10, 6, 10, 6) };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private void BulkPrintButton_Click(object sender, RoutedEventArgs e) => QueueForPrinting(GetMarkedRows().Select(row => row.Card));

    private void QueueForPrinting(IEnumerable<GeneratedDemoCard> cards)
    {
        var selected = cards.DistinctBy(card => (card.TargetSystem, card.CardNumber)).ToList();
        if (selected.Count == 0)
        {
            SetOperationStatus("لم يتم تحديد كروت للطباعة.", true);
            return;
        }
        try
        {
            var pendingKeys = _workflowStore.GetPendingPrintSelection()
                .Select(item => $"{NormalizeSystem(item.TargetSystem)}\u001F{item.BatchId}\u001F{item.CardNumber}")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var newlyQueued = selected.Count(card => !pendingKeys.Contains(WorkflowKey(card)));
            _workflowStore.QueuePrintSelection(selected);
            SetOperationStatus($"أُضيف {newlyQueued:N0} كرت إلى قائمة الطباعة؛ {selected.Count - newlyQueued:N0} كرت موجودة فيها مسبقًا.", false);
            NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("printing", "قائمة الطباعة"));
        }
        catch (Exception)
        {
            SetOperationStatus("تعذر تحديث قائمة الطباعة. حاول مرة أخرى.", true);
        }
    }

    private void BulkArchiveButton_Click(object sender, RoutedEventArgs e) => ArchiveCards(GetMarkedRows().Select(row => row.Card));

    private void ArchiveCards(IEnumerable<GeneratedDemoCard> cards)
    {
        var selected = cards.Where(card => !card.IsArchived).DistinctBy(card => (card.TargetSystem, card.CardNumber)).ToList();
        if (selected.Count == 0)
        {
            SetOperationStatus("لا توجد كروت غير مؤرشفة في التحديد الحالي.", false);
            return;
        }
        var answer = MessageBox.Show(Window.GetWindow(this),
            $"ستؤرشف {selected.Count:N0} كرت. ستبقى سجلاتها محفوظة ويمكن استعادتها لاحقًا. هل تريد المتابعة؟",
            "أرشفة الكروت", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;
        try
        {
            _issuanceService.ArchiveCards(selected, "أرشفة من شاشة إدارة الكروت");
            RefreshFromSource(preserveSelection: false);
            SetOperationStatus($"تمت أرشفة {selected.Count:N0} كرت.", false);
        }
        catch (Exception)
        {
            SetOperationStatus("تعذر أرشفة الكروت. حاول مرة أخرى.", true);
        }
    }

    private void BulkRestoreButton_Click(object sender, RoutedEventArgs e) => RestoreCards(GetMarkedRows().Select(row => row.Card));

    private void RestoreCards(IEnumerable<GeneratedDemoCard> cards)
    {
        var selected = cards.Where(card => card.IsArchived).DistinctBy(card => (card.TargetSystem, card.CardNumber)).ToList();
        if (selected.Count == 0)
        {
            SetOperationStatus("لا توجد سجلات مؤرشفة في التحديد الحالي.", false);
            return;
        }
        try
        {
            _issuanceService.RestoreCards(selected);
            RefreshFromSource(preserveSelection: false);
            SetOperationStatus($"تمت استعادة {selected.Count:N0} كرت من الأرشيف.", false);
        }
        catch (Exception)
        {
            SetOperationStatus("تعذرت استعادة الكروت. حاول مرة أخرى.", true);
        }
    }

    private void SetOperationStatus(string message, bool isError)
    {
        OperationStatusTextBlock.Text = message;
        OperationStatusTextBlock.Foreground = (Brush)FindResource(isError ? "DangerBrush" : "MutedBrush");
    }

    private void ShowCardDetails(GeneratedDemoCard card)
    {
        var workflow = _workflowStore.GetCardWorkflowStatuses()
            .Where(item => string.Equals(item.BatchId, card.BatchId, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(item.CardNumber, card.CardNumber, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(item.TargetSystem, SystemKey(card.TargetSystem), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.UpdatedAt)
            .FirstOrDefault();

        var details = new Window
        {
            Title = "تفاصيل الكرت — " + card.CardNumber,
            Width = 720,
            Height = 620,
            MinWidth = 610,
            MinHeight = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            FlowDirection = FlowDirection.RightToLeft,
            Background = (Brush)FindResource("AppBgBrush"),
            Owner = Window.GetWindow(this)
        };

        var root = new DockPanel { Margin = new Thickness(16) };
        var header = new Border
        {
            Background = Brushes.White,
            BorderBrush = (Brush)FindResource("BorderBrushSoft"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var headerContent = new StackPanel();
        headerContent.Children.Add(new TextBlock { Text = card.CardNumber, FontSize = 18, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("TextBrush") });
        headerContent.Children.Add(new TextBlock { Text = $"{card.TargetSystem} · الدفعة {card.BatchId}", FontSize = 11.5, Foreground = (Brush)FindResource("MutedBrush"), Margin = new Thickness(0, 4, 0, 0) });
        header.Child = headerContent;
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        var batchButton = new Button { Content = "فتح الدفعة الأصلية", Style = (Style)FindResource("SecondaryButton"), Height = 34, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 6, 0) };
        batchButton.Click += (_, _) => { details.Close(); OpenBatch(card); };
        var printButton = new Button { Content = "إضافة إلى الطباعة / إعادة الطباعة", Style = (Style)FindResource("SecondaryButton"), Height = 34, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 6, 0) };
        printButton.Click += (_, _) => { details.Close(); QueueForPrinting(new[] { card }); };
        var closeButton = new Button { Content = "إغلاق", Style = (Style)FindResource("PrimaryButton"), Height = 34, Padding = new Thickness(16, 0, 16, 0) };
        closeButton.Click += (_, _) => details.Close();
        footer.Children.Add(batchButton);
        footer.Children.Add(printButton);
        footer.Children.Add(closeButton);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var rows = new List<(string Label, string Value)>
        {
            ("اسم المستخدم", string.IsNullOrWhiteSpace(card.Username) ? "—" : card.Username),
            ("حالة الكرت", card.IsArchived ? "مؤرشفة" : card.DemoStatus),
            ("حالة الطباعة", PrintStatusLabelFor(workflow)),
            ("المصدر", IsImported(card) ? "مستورد" : "إصدار"),
            ("الباقة", card.PackageName),
            ("السعر", card.UnitPriceLabel),
            ("رقم الدفعة", card.BatchId),
            ("تاريخ الإنشاء", card.CreatedAtLabel),
            ("ملف الاستخدام", card.RouterProfile),
            ("الوقت المسموح", card.TimeLimit),
            ("حد البيانات", card.DataLimit),
            ("سياسة السرعة", card.RateLimit),
            ("مدة الصلاحية", card.PackageValidity),
            ("نقطة البيع", card.PointOfSale),
            ("الأجهزة المتزامنة المسموحة", card.SharedUsersLabel),
            ("ملاحظات", string.IsNullOrWhiteSpace(card.Comment) ? "—" : card.Comment)
        };
        if (SystemKey(card.TargetSystem) == "user-manager")
            rows.Insert(rows.FindIndex(row => row.Label == "ملاحظات"), ("حساب مدير المستخدمين", card.ManagerAccount));
        if (card.IsArchived)
            rows.Insert(3, ("سبب الأرشفة", string.IsNullOrWhiteSpace(card.ArchiveReason) ? "أرشفة" : card.ArchiveReason));

        var batchActions = _workflowStore.GetActionHistory()
            .Where(action => action.ActionType != "SyncRequested" && action.ActionType != "OnlineSalePrepared")
            .Where(action => string.Equals(action.BatchId, card.BatchId, StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(NormalizeSystem(action.TargetSystem), SystemKey(card.TargetSystem), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(action => action.CreatedAt)
            .Take(5)
            .Select(action => $"{action.CreatedAtLabel} · {action.ActionLabel} · {action.Count:N0} سجل · {action.Description}")
            .ToList();
        rows.Add(("سجل إجراءات الدفعة", batchActions.Count == 0 ? "لا توجد إجراءات مسجلة لهذه الدفعة." : string.Join(Environment.NewLine, batchActions)));

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(175) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var index = 0; index < rows.Count; index++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var background = index % 2 == 0 ? Brushes.White : new SolidColorBrush(Color.FromRgb(248, 250, 252));
            var label = new Border { Background = background, Padding = new Thickness(10, 7, 10, 7), BorderBrush = (Brush)FindResource("BorderBrushSoft"), BorderThickness = new Thickness(0, 0, 0, 1) };
            label.Child = new TextBlock { Text = rows[index].Label, FontWeight = FontWeights.SemiBold, FontSize = 11, Foreground = (Brush)FindResource("MutedBrush"), TextWrapping = TextWrapping.Wrap };
            var value = new Border { Background = background, Padding = new Thickness(10, 7, 10, 7), BorderBrush = (Brush)FindResource("BorderBrushSoft"), BorderThickness = new Thickness(0, 0, 0, 1) };
            value.Child = new TextBlock { Text = string.IsNullOrWhiteSpace(rows[index].Value) ? "—" : rows[index].Value, FontSize = 11.5, Foreground = (Brush)FindResource("TextBrush"), TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(label, index); Grid.SetColumn(label, 0); grid.Children.Add(label);
            Grid.SetRow(value, index); Grid.SetColumn(value, 1); grid.Children.Add(value);
        }
        scroll.Content = grid;
        root.Children.Add(scroll);
        details.Content = root;
        details.ShowDialog();
    }

    internal static string PrintStatusLabelFor(DemoCardWorkflowStatus? workflow) =>
        workflow?.PrintQueued == true ? "في قائمة الطباعة" :
        workflow?.PrintSubmitted == true ? "مهمة طباعة مسجلة" :
        workflow?.PrintPrepared == true ? "مجهز للطباعة" : "غير مضاف للطباعة";

    private static string IdentityKey(GeneratedDemoCard card) => SystemKey(card.TargetSystem) + "\u001F" + card.CardNumber;

    private static string WorkflowKey(GeneratedDemoCard card) => SystemKey(card.TargetSystem) + "\u001F" + card.BatchId + "\u001F" + card.CardNumber;

    private static string WorkflowKey(DemoCardWorkflowStatus status) => NormalizeSystem(status.TargetSystem) + "\u001F" + status.BatchId + "\u001F" + status.CardNumber;

    private static string SystemKey(string targetSystem) => targetSystem.Contains("هوتسبوت", StringComparison.OrdinalIgnoreCase) ? "hotspot" : "user-manager";

    private static string NormalizeSystem(string value) =>
        value.Contains("hotspot", StringComparison.OrdinalIgnoreCase) || value.Contains("هوتسبوت", StringComparison.OrdinalIgnoreCase)
            ? "hotspot"
            : "user-manager";

    private static bool IsImported(GeneratedDemoCard card) =>
        (card.Comment ?? string.Empty).StartsWith("استيراد من ملف:", StringComparison.OrdinalIgnoreCase);
}

public sealed class CardManagementRow : INotifyPropertyChanged
{
    private bool _isMarked;

    public CardManagementRow(GeneratedDemoCard card, DemoCardWorkflowStatus? workflow, bool isMarked)
    {
        Card = card;
        Workflow = workflow;
        _isMarked = isMarked;
        SourceKey = (card.Comment ?? string.Empty).StartsWith("استيراد من ملف:", StringComparison.OrdinalIgnoreCase) ? "import" : "local";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public GeneratedDemoCard Card { get; }
    public DemoCardWorkflowStatus? Workflow { get; }
    public string CardNumber => Card.CardNumber;
    public string Username => Card.Username;
    public bool ShowUsername => !string.IsNullOrWhiteSpace(Card.Username) && !string.Equals(Card.Username, Card.CardNumber, StringComparison.OrdinalIgnoreCase);
    public string PackageName => Card.PackageName;
    public string BatchId => Card.BatchId;
    public string CreatedAtLabel => Card.CreatedAtLabel;
    public string SourceKey { get; }
    public string SourceLabel => SourceKey == "import" ? "مستورد" : "إصدار";
    public bool IsArchived => Card.IsArchived;
    public string CardStatusLabel => Card.IsArchived ? "مؤرشفة" : string.IsNullOrWhiteSpace(Card.DemoStatus) ? "غير محددة" : Card.DemoStatus;
    public string PrintStatusLabel => CardManagementView.PrintStatusLabelFor(Workflow);
    public bool NeedsReview => !Card.IsArchived && (string.Equals(Card.DemoStatus, "موقوفة", StringComparison.Ordinal) ||
        ContainsAny(Card.LocalStatus, "فشل", "خطأ", "غير مؤكد", "مراجعة"));
    public bool IsMarked
    {
        get => _isMarked;
        set
        {
            if (_isMarked == value) return;
            _isMarked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMarked)));
        }
    }

    private static bool ContainsAny(string? text, params string[] tokens) =>
        !string.IsNullOrWhiteSpace(text) && tokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));
}
