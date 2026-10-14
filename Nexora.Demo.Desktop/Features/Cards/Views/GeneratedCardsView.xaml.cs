using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Shared;

namespace Nexora.Demo.Desktop.Features.Cards.Views;

public partial class GeneratedCardsView : UserControl
{
    private readonly DemoCardIssuanceService _issuanceService;
    private readonly ListCollectionView _cardsView;
    private readonly DemoCardWorkflowStore _workflowStore = new();
    private bool _isSubscribed;
    private bool _refreshingBatchFilter;
    private string _activityFilter = "all";

    public GeneratedCardsView(DemoCardIssuanceService issuanceService, DemoAccountSystem targetSystem)
    {
        InitializeComponent();
        _issuanceService = issuanceService;
        _cardsView = new ListCollectionView(_issuanceService.Cards);
        _cardsView.Filter = MatchCard;
        GeneratedCardsGrid.ItemsSource = _cardsView;
        CardSystemFilterComboBox.SelectedItem = CardSystemFilterComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == (targetSystem == DemoAccountSystem.HotSpot ? "hotspot" : "user-manager"));
        CardsScopeTextBlock.Text = targetSystem == DemoAccountSystem.HotSpot
            ? "حسابات HotSpot مع بيانات الكرت والدفعة والحالة."
            : "حسابات الشبكة مع بيانات الكرت والدفعة والحالة.";
        RefreshBatchFilterOptions(_workflowStore.GetPreferredBatchFilter(GetCurrentSystemKey()));
        ApplySystemColumnVisibility();
        Loaded += GeneratedCardsView_Loaded;
        Unloaded += GeneratedCardsView_Unloaded;
        UpdateSummary();
    }

    public event EventHandler<FeatureNavigationRequestedEventArgs>? NavigationRequested;

    private void GeneratedCardsView_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_isSubscribed)
        {
            _issuanceService.ScenarioChanged += IssuanceService_ScenarioChanged;
            _isSubscribed = true;
        }
        RefreshBatchFilterOptions(_workflowStore.GetPreferredBatchFilter(GetCurrentSystemKey()));
        RefreshFilteredCards();
    }

    private void GeneratedCardsView_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_isSubscribed)
        {
            _issuanceService.ScenarioChanged -= IssuanceService_ScenarioChanged;
            _isSubscribed = false;
        }
    }

    public void SetSystemFilter(string systemKey)
    {
        var item = CardSystemFilterComboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(option => option.Tag?.ToString() == systemKey);
        if (item is not null)
            CardSystemFilterComboBox.SelectedItem = item;
        RefreshBatchFilterOptions(systemKey == "all" ? null : _workflowStore.GetPreferredBatchFilter(systemKey));
        ApplySystemColumnVisibility();
        RefreshFilteredCards();
    }

    public void SetSystemFilterControlVisible(bool isVisible)
        {
            CardSystemFilterComboBox.Visibility = isVisible
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

    public void ShowAllSystems() => SetSystemFilter("all");

    public void SetActivityFilter(string activityKey)
    {
        _activityFilter = activityKey is "active" or "inactive" ? activityKey : "all";
        RefreshFilteredCards();
    }

    public void SetStatusFilter(string statusKey)
    {
        var desired = CardStatusFilterComboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == statusKey);
        if (desired is not null) CardStatusFilterComboBox.SelectedItem = desired;
        RefreshFilteredCards();
    }

    public void SetSearchQuery(string query)
    {
        if (CardSearchTextBox.Text == query) return;
        CardSearchTextBox.Text = query;
        RefreshFilteredCards();
    }

    public void SetBatchFilter(string batchId)
    {
        var desired = CardBatchFilterComboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == batchId);
        if (desired is null && batchId != "all")
        {
            RefreshBatchFilterOptions();
            desired = CardBatchFilterComboBox.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag?.ToString() == batchId);
        }
        desired ??= CardBatchFilterComboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == "all");
        if (desired is not null) CardBatchFilterComboBox.SelectedItem = desired;
        RefreshFilteredCards();
    }

    public void FocusCard(string cardNumber)
    {
        SetSearchQuery(cardNumber);
        var card = _cardsView.Cast<object>().OfType<GeneratedDemoCard>()
            .FirstOrDefault(item => string.Equals(item.CardNumber, cardNumber, StringComparison.OrdinalIgnoreCase));
        if (card is not null)
        {
            GeneratedCardsGrid.SelectedItem = card;
            GeneratedCardsGrid.ScrollIntoView(card);
        }
    }

    public void OpenCardDetails(string cardNumber)
    {
        FocusCard(cardNumber);
        if (GeneratedCardsGrid.SelectedItem is GeneratedDemoCard card)
            ShowCardDetails(card);
    }

    private string GetCurrentSystemKey() => (CardSystemFilterComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";

    private static string SystemKeyForCard(GeneratedDemoCard card) =>
        card.TargetSystem.Contains("هوتسبوت", StringComparison.OrdinalIgnoreCase) ? "hotspot" : "user-manager";

    private static string SystemKeyForBatch(GeneratedCardBatch batch) =>
        batch.TargetSystem.Contains("هوتسبوت", StringComparison.OrdinalIgnoreCase) ? "hotspot" : "user-manager";

    private void ApplySystemColumnVisibility()
    {
        if (GeneratedCardsGrid is null)
            return;
        var systemKey = GetCurrentSystemKey();
        var usernameColumn = GeneratedCardsGrid.Columns.FirstOrDefault(column => column.Header?.ToString() == "اسم المستخدم");
        if (usernameColumn is not null)
            usernameColumn.Visibility = systemKey == "user-manager" ? Visibility.Collapsed : Visibility.Visible;
        var cardNumberColumn = GeneratedCardsGrid.Columns.FirstOrDefault(column =>
            column.Header?.ToString() == "رقم الكرت" || column.Header?.ToString() == "رقم الكرت / اسم المستخدم");
        if (cardNumberColumn is not null)
            cardNumberColumn.Header = systemKey == "user-manager" ? "رقم الكرت / اسم المستخدم" : "رقم الكرت";
    }

    private void RefreshBatchFilterOptions(string? preferredBatchId = null)
    {
        if (CardBatchFilterComboBox is null)
            return;
        var previousBatchId = (CardBatchFilterComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var systemKey = GetCurrentSystemKey();
        var batches = _issuanceService.Batches
            .Where(batch => systemKey == "all" || SystemKeyForBatch(batch) == systemKey)
            .OrderByDescending(batch => batch.CreatedAt)
            .ToList();

        _refreshingBatchFilter = true;
        try
        {
            CardBatchFilterComboBox.Items.Clear();
            CardBatchFilterComboBox.Items.Add(new ComboBoxItem { Content = "كل الدفعات", Tag = "all" });
            foreach (var batch in batches)
                CardBatchFilterComboBox.Items.Add(new ComboBoxItem
                {
                    Content = batch.BatchId,
                    Tag = batch.BatchId,
                    ToolTip = $"{batch.Quantity:N0} كرت · {batch.PackageName}"
                });

            var desired = systemKey == "all" ? "all" : preferredBatchId ?? previousBatchId;
            CardBatchFilterComboBox.SelectedItem = CardBatchFilterComboBox.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => item.Tag?.ToString() == desired)
                ?? CardBatchFilterComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
        }
        finally
        {
            _refreshingBatchFilter = false;
        }
    }

    private bool MatchCard(object item)
    {
        if (item is not GeneratedDemoCard card)
            return false;

        var systemFilter = (CardSystemFilterComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (systemFilter == "hotspot" && card.TargetSystem != "مستخدمو الهوتسبوت")
            return false;
        if (systemFilter == "user-manager" && card.TargetSystem != "مدير المستخدمين")
            return false;
        if (_activityFilter == "active" && card.DemoStatus != "قيد الاستخدام")
            return false;
        if (_activityFilter == "inactive" && card.DemoStatus == "قيد الاستخدام")
            return false;

        var batchFilter = (CardBatchFilterComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (!string.IsNullOrWhiteSpace(batchFilter) && batchFilter != "all" && !string.Equals(card.BatchId, batchFilter, StringComparison.OrdinalIgnoreCase))
            return false;

        var statusFilter = (CardStatusFilterComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (!string.IsNullOrWhiteSpace(statusFilter) && statusFilter != "all" && card.DemoStatus != statusFilter)
            return false;

        var search = CardSearchTextBox?.Text.Trim();
        if (string.IsNullOrWhiteSpace(search))
            return true;
        return card.BatchId.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               card.CardNumber.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               card.Username.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               card.PackageName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               card.RouterProfile.Contains(search, StringComparison.OrdinalIgnoreCase) ||
               card.PointOfSale.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void CardSearchTextBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshFilteredCards();

    private void CardFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingBatchFilter)
            return;
        if (ReferenceEquals(sender, CardSystemFilterComboBox))
        {
            var systemKey = GetCurrentSystemKey();
            RefreshBatchFilterOptions(systemKey == "all" ? null : _workflowStore.GetPreferredBatchFilter(systemKey));
            ApplySystemColumnVisibility();
        }
        RefreshFilteredCards();
    }

    private void RefreshFilteredCards()
    {
        if (_cardsView is null)
            return;
        _cardsView.Refresh();
        UpdateSummary();
        UpdateWorkflowStatus();
    }

    private List<GeneratedDemoCard> GetSelectedCards() => GeneratedCardsGrid.SelectedItems.OfType<GeneratedDemoCard>().ToList();

    private void UpdateWorkflowStatus()
    {
        if (WorkflowStatusTextBlock is null || GeneratedCardsGrid is null)
            return;
        var selected = GetSelectedCards();
        if (selected.Count == 0)
        {
            WorkflowStatusTextBlock.Text = "حدد كرتًا لعرض تفاصيل حالته.";
            return;
        }
        if (selected.Count > 1)
        {
            WorkflowStatusTextBlock.Text = $"تم تحديد {selected.Count:N0} كرت؛ الإجراءات ستطبق على المحدد فقط.";
            return;
        }
        var card = selected[0];
        WorkflowStatusTextBlock.Text = $"{card.CardNumber} · {card.BatchId} · {_workflowStore.GetCardStatusLabel(card.BatchId, card.CardNumber)}";
    }

    private void GeneratedCardsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateWorkflowStatus();

    private void SelectAllVisibleCardsButton_Click(object sender, RoutedEventArgs e)
    {
        GeneratedCardsGrid.SelectAll();
        UpdateWorkflowStatus();
    }

    private void ClearSelectedCardsButton_Click(object sender, RoutedEventArgs e)
    {
        GeneratedCardsGrid.UnselectAll();
        UpdateWorkflowStatus();
    }

    private void OpenSelectedCardDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedCards();
        if (selected.Count != 1)
        {
            MessageBox.Show(Window.GetWindow(this), "حدد كرتًا واحدًا لعرض تفاصيله.", "تفاصيل الكرت", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        ShowCardDetails(selected[0]);
    }

    private void GeneratedCardsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GeneratedCardsGrid.SelectedItem is GeneratedDemoCard card)
            ShowCardDetails(card);
    }

    private void ShowCardDetails(GeneratedDemoCard card)
    {
        var owner = Window.GetWindow(this);
        var details = new Window
        {
            Title = "تفاصيل الكرت",
            Width = 650,
            Height = 540,
            MinWidth = 560,
            MinHeight = 450,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            FlowDirection = FlowDirection.RightToLeft,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(246, 248, 251)),
            Owner = owner
        };
        var root = new DockPanel { Margin = new Thickness(16) };
        var titlePanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        titlePanel.Children.Add(new TextBlock { Text = card.TargetSystem == "مدير المستخدمين" ? $"{card.CardNumber} — اسم المستخدم" : card.CardNumber, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(32, 43, 69)) });
        titlePanel.Children.Add(new TextBlock { Text = $"{card.TargetSystem} · الدفعة {card.BatchId} · {_workflowStore.GetCardStatusLabel(card.BatchId, card.CardNumber)}", FontSize = 12, Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(201, 24, 61)), Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap });
        DockPanel.SetDock(titlePanel, Dock.Top);
        root.Children.Add(titlePanel);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var detailsGrid = new Grid();
        detailsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        detailsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var rows = new (string Label, string Value)[]
        {
            ("رقم الكرت / اسم المستخدم", card.TargetSystem == "مدير المستخدمين" ? card.CardNumber : card.Username),
            ("كلمة المرور", string.IsNullOrEmpty(card.Password) ? "فارغة" : card.Password),
            ("الباقة", card.PackageName),
            ("السعر", card.UnitPriceLabel),
            ("الوقت", card.TimeLimit),
            ("حد البيانات", card.DataLimit),
            ("السرعة", card.RateLimit),
            ("الصلاحية", card.PackageValidity),
            ("ملف الاستخدام", card.RouterProfile),
            ("نقطة البيع", card.PointOfSale),
            ("مستخدم الإدارة", card.ManagerAccount),
            ("الأجهزة المتزامنة", card.SharedUsersLabel),
            ("تاريخ الإنشاء", card.CreatedAtLabel),
            ("حالة الحساب", card.DemoStatus),
            ("حالة الإجراء", _workflowStore.GetCardStatusLabel(card.BatchId, card.CardNumber)),
            ("ملاحظة", card.Comment)
        };
        for (var i = 0; i < rows.Length; i++)
        {
            detailsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var background = new System.Windows.Media.SolidColorBrush(i % 2 == 0
                ? System.Windows.Media.Color.FromRgb(255, 255, 255)
                : System.Windows.Media.Color.FromRgb(248, 250, 253));
            var label = new Border { Padding = new Thickness(10, 8, 10, 8), Background = background, BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(232, 221, 228)), BorderThickness = new Thickness(0, 0, 0, 1) };
            label.Child = new TextBlock { Text = rows[i].Label, FontWeight = FontWeights.SemiBold, Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(104, 117, 141)), TextWrapping = TextWrapping.Wrap };
            var value = new Border { Padding = new Thickness(10, 8, 10, 8), Background = background, BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(232, 221, 228)), BorderThickness = new Thickness(0, 0, 0, 1) };
            value.Child = new TextBlock { Text = string.IsNullOrWhiteSpace(rows[i].Value) ? "—" : rows[i].Value, Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(32, 43, 69)), TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(label, i); Grid.SetColumn(label, 0); detailsGrid.Children.Add(label);
            Grid.SetRow(value, i); Grid.SetColumn(value, 1); detailsGrid.Children.Add(value);
        }
        scroll.Content = detailsGrid;
        root.Children.Add(scroll);
        details.Content = root;
        details.ShowDialog();
    }

    private void UploadSelectedToRouterButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedCards();
        if (selected.Count == 0)
        {
            CardsScopeTextBlock.Text = "حدد كرتًا أو أكثر من القائمة أولًا.";
            return;
        }
        try
        {
            _workflowStore.RecordRouterUpload(selected);
            SetPreferredBatchesForSelection(selected);
            CardsScopeTextBlock.Text = $"تم تسجيل طلب مزامنة لـ {selected.Count:N0} كرت.";
            RefreshFilteredCards();
        }
        catch (Exception ex) { CardsScopeTextBlock.Text = "تعذر تسجيل الإجراء: " + ex.Message; }
    }

    private void UploadSelectedAndPrintButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedCards();
        if (selected.Count == 0)
        {
            CardsScopeTextBlock.Text = "حدد كرتًا أو أكثر قبل الرفع والطباعة.";
            return;
        }
        try
        {
            _workflowStore.RecordRouterUpload(selected);
            _workflowStore.QueuePrintSelection(selected);
            SetPreferredBatchesForSelection(selected);
            CardsScopeTextBlock.Text = $"تم تسجيل طلب مزامنة لـ {selected.Count:N0} كرت وإضافتها إلى قائمة الطباعة.";
            NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("printing", "قائمة الطباعة"));
        }
        catch (Exception ex) { CardsScopeTextBlock.Text = "تعذر تجهيز مسار الطباعة: " + ex.Message; }
    }

    private void PrepareSelectedForOnlineSaleButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedCards();
        if (selected.Count == 0)
        {
            CardsScopeTextBlock.Text = "حدد كرتًا أو أكثر قبل تجهيز البيع أونلاين.";
            return;
        }
        try
        {
            _workflowStore.RecordOnlineSalePreparation(selected);
            SetPreferredBatchesForSelection(selected);
            CardsScopeTextBlock.Text = $"تم تسجيل تجهيز {selected.Count:N0} كرت للبيع الإلكتروني.";
            RefreshFilteredCards();
        }
        catch (Exception ex) { CardsScopeTextBlock.Text = "تعذر تجهيز البيع الإلكتروني: " + ex.Message; }
    }

    private void SetPreferredBatchesForSelection(IEnumerable<GeneratedDemoCard> cards)
    {
        foreach (var group in cards.GroupBy(card => SystemKeyForCard(card)))
        {
            var batchIds = group.Select(card => card.BatchId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (batchIds.Count == 1)
                _workflowStore.SetPreferredBatchFilter(group.Key, batchIds[0]);
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshFilteredCards();
        CardsScopeTextBlock.Text = "تم تحديث قائمة الكروت والدفعات.";
    }

    private void IssuanceService_ScenarioChanged(object? sender, EventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            RefreshFilteredCards();
        });
    }
    private void OpenPrintQueueButton_Click(object sender, RoutedEventArgs e) =>
        NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("printing", "قائمة الطباعة"));

    private void UpdateSummary()
    {
        if (GeneratedCardsGrid is null || CardsCountSummaryTextBlock is null)
            return;

        var displayed = GeneratedCardsGrid.Items.Count;
        var totalSystemCards = GetCardsInCurrentSystemScope().Count();
        var totalBatches = GetCardsInCurrentSystemScope().Select(card => card.BatchId).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        VisibleCardsMetricTextBlock.Text = displayed.ToString("N0");
        VisibleBatchesMetricTextBlock.Text = totalBatches.ToString("N0");
        CardsCountSummaryTextBlock.Text = $"المعروض: {displayed:N0} سجل · إجمالي السجلات ضمن النطاق: {totalSystemCards:N0} · مصادر الإصدار: HotSpot وUser Manager.";
        EmptyCardsPanel.Visibility = displayed == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyCardsDescriptionTextBlock.Text = _issuanceService.Cards.Count == 0
            ? "لا توجد حسابات مطابقة. أنشئ دفعة جديدة أو غيّر الفلاتر."
            : "لا توجد سجلات تطابق البحث والمرشحات الحالية. غيّر خيارات التصفية أو امسح البحث.";
        UpdateWorkflowStatus();
    }

    private IEnumerable<GeneratedDemoCard> GetCardsInCurrentSystemScope()
    {
        var systemFilter = (CardSystemFilterComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var cards = _issuanceService.Cards.AsEnumerable();
        if (systemFilter == "hotspot")
            cards = cards.Where(card => card.TargetSystem == "مستخدمو الهوتسبوت");
        else if (systemFilter == "user-manager")
            cards = cards.Where(card => card.TargetSystem == "مدير المستخدمين");
        return cards;
    }
}
