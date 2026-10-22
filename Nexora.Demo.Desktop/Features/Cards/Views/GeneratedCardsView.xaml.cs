using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
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
        CardSystemFilterComboBox.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
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
        if (GeneratedCardsGrid is null) return;
        var systemKey = GetCurrentSystemKey();
        var cardNumberColumn = GeneratedCardsGrid.Columns.FirstOrDefault(column =>
            column.Header?.ToString() == "رقم الكرت / اسم المستخدم");
        if (cardNumberColumn is not null)
            cardNumberColumn.Header = "رقم الكرت / اسم المستخدم";
    }

    private void RefreshBatchFilterOptions(string? preferredBatchId = null)
    {
        if (CardBatchFilterComboBox is null) return;
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
        if (item is not GeneratedDemoCard card) return false;

        var systemFilter = (CardSystemFilterComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (systemFilter == "hotspot" && card.TargetSystem != "مستخدمو الهوتسبوت") return false;
        if (systemFilter == "user-manager" && card.TargetSystem != "مدير المستخدمين") return false;

        var batchFilter = (CardBatchFilterComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (!string.IsNullOrWhiteSpace(batchFilter) && batchFilter != "all" && !string.Equals(card.BatchId, batchFilter, StringComparison.OrdinalIgnoreCase))
            return false;

        var search = CardSearchTextBox?.Text.Trim();
        if (string.IsNullOrWhiteSpace(search)) return true;
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
        if (_refreshingBatchFilter) return;
        if (ReferenceEquals(sender, CardSystemFilterComboBox))
        {
            var systemKey = GetCurrentSystemKey();
            RefreshBatchFilterOptions(systemKey == "all" ? null : _workflowStore.GetPreferredBatchFilter(systemKey));
            ApplySystemColumnVisibility();
        }
        RefreshFilteredCards();
    }

    public void RefreshFilteredCards()
    {
        if (_cardsView is null) return;
        _cardsView.Refresh();
        UpdateSummary();
        UpdateWorkflowStatus();
    }

    private List<GeneratedDemoCard> GetSelectedCards() => GeneratedCardsGrid.SelectedItems.OfType<GeneratedDemoCard>().ToList();

    private void UpdateWorkflowStatus()
    {
        if (WorkflowStatusTextBlock is null || GeneratedCardsGrid is null) return;
        var selected = GetSelectedCards();
        if (selected.Count == 0)
        {
            WorkflowStatusTextBlock.Text = "حدد كرتًا لعرض تفاصيل حالته التشغيلية.";
            return;
        }
        if (selected.Count > 1)
        {
            WorkflowStatusTextBlock.Text = $"تم تحديد {selected.Count:N0} كرت؛ الإجراءات ستطبق على المحدد فقط.";
            return;
        }
        var card = selected[0];
        WorkflowStatusTextBlock.Text = $"{card.CardNumber} · {card.BatchId} · الحالة: {_workflowStore.GetCardStatusLabel(card.BatchId, card.CardNumber)}";
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

    // النافذة التفاعلية الاحترافية لتفاصيل الكرت
    private void ShowCardDetails(GeneratedDemoCard card)
    {
        var owner = Window.GetWindow(this);
        var details = new Window
        {
            Title = $"تفاصيل الحساب والتجهيز — {card.CardNumber}",
            Width = 690,
            Height = 600,
            MinWidth = 580,
            MinHeight = 480,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            FlowDirection = FlowDirection.RightToLeft,
            Background = new SolidColorBrush(Color.FromRgb(244, 246, 249)),
            Owner = owner
        };

        var root = new DockPanel { Margin = new Thickness(18) };

        // الهيدر العلوي
        var headerBorder = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 12)
        };

        var headerPanel = new StackPanel();
        var statusLabelText = _workflowStore.GetCardStatusLabel(card.BatchId, card.CardNumber);

        headerPanel.Children.Add(new TextBlock { Text = $"👤 رقم الكرت / اسم المستخدم: {card.CardNumber}", FontSize = 17, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(32, 43, 69)) });

        var statusTextBlock = new TextBlock { Text = $"🏷️ النظام: {card.TargetSystem}  |  📂 الدفعة: {card.BatchId}  |  📌 الحالة الإجرائية: {statusLabelText}", FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(201, 24, 61)), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 5, 0, 0) };
        headerPanel.Children.Add(statusTextBlock);
        headerBorder.Child = headerPanel;
        DockPanel.SetDock(headerBorder, Dock.Top);
        root.Children.Add(headerBorder);

        // شريط الأزرار التفاعلية المباشرة داخل نافذة التفاصيل
        var actionPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) };

        var syncBtn = new Button { Content = "⚡ طلب مزامنة", Width = 110, Height = 34, Margin = new Thickness(4, 0, 4, 0), Background = new SolidColorBrush(Color.FromRgb(238, 242, 255)), Foreground = new SolidColorBrush(Color.FromRgb(67, 56, 202)), FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand };
        var printBtn = new Button { Content = "🖨️ تجهيز طباعة", Width = 110, Height = 34, Margin = new Thickness(4, 0, 4, 0), Background = new SolidColorBrush(Color.FromRgb(236, 253, 245)), Foreground = new SolidColorBrush(Color.FromRgb(4, 120, 87)), FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand };
        var saleBtn = new Button { Content = "🛒 بيع إلكتروني", Width = 110, Height = 34, Margin = new Thickness(4, 0, 4, 0), Background = new SolidColorBrush(Color.FromRgb(255, 241, 242)), Foreground = new SolidColorBrush(Color.FromRgb(225, 29, 72)), FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand };

        syncBtn.Click += (_, _) =>
        {
            _workflowStore.RecordRouterUpload(new[] { card });
            statusTextBlock.Text = $"🏷️ النظام: {card.TargetSystem}  |  📂 الدفعة: {card.BatchId}  |  📌 الحالة الإجرائية: {_workflowStore.GetCardStatusLabel(card.BatchId, card.CardNumber)}";
            RefreshFilteredCards();
            MessageBox.Show(details, "⚡ تم تسجيل طلب المزامنة للراوتر بنجاح.", "المزامنة", MessageBoxButton.OK, MessageBoxImage.Information);
        };

        printBtn.Click += (_, _) =>
        {
            _workflowStore.QueuePrintSelection(new[] { card });
            statusTextBlock.Text = $"🏷️ النظام: {card.TargetSystem}  |  📂 الدفعة: {card.BatchId}  |  📌 الحالة الإجرائية: {_workflowStore.GetCardStatusLabel(card.BatchId, card.CardNumber)}";
            RefreshFilteredCards();
            MessageBox.Show(details, "🖨️ تم تجهيز الكرت وإضافته إلى قائمة الطباعة.", "الطباعة", MessageBoxButton.OK, MessageBoxImage.Information);
        };

        saleBtn.Click += (_, _) =>
        {
            _workflowStore.RecordOnlineSalePreparation(new[] { card });
            statusTextBlock.Text = $"🏷️ النظام: {card.TargetSystem}  |  📂 الدفعة: {card.BatchId}  |  📌 الحالة الإجرائية: {_workflowStore.GetCardStatusLabel(card.BatchId, card.CardNumber)}";
            RefreshFilteredCards();
            MessageBox.Show(details, "🛒 تم تجهيز الكرت للبيع الإلكتروني بنجاح.", "البيع الإلكتروني", MessageBoxButton.OK, MessageBoxImage.Information);
        };

        actionPanel.Children.Add(syncBtn);
        actionPanel.Children.Add(printBtn);
        actionPanel.Children.Add(saleBtn);

        // زر الإغلاق السفلي
        var footerPanel = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var closeButton = new Button { Content = "إغلاق النافذة", Width = 100, Height = 34, Background = new SolidColorBrush(Color.FromRgb(233, 37, 77)), Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand };
        closeButton.Click += (_, _) => details.Close();
        DockPanel.SetDock(closeButton, Dock.Left);
        footerPanel.Children.Add(closeButton);
        footerPanel.Children.Add(actionPanel);

        DockPanel.SetDock(footerPanel, Dock.Bottom);
        root.Children.Add(footerPanel);

        // جدول التفاصيل
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var detailsGrid = new Grid();
        detailsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        detailsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var rows = new (string Label, string Value)[]
        {
            ("كلمة المرور", string.IsNullOrEmpty(card.Password) ? "🔒 (فارغة)" : $"🔑 {card.Password}"),
            ("الباقة المرتبطة", $"📦 {card.PackageName}"),
            ("قيمة السعر", $"💵 {card.UnitPriceLabel}"),
            ("الوقت المسموح", $"⏱️ {card.TimeLimit}"),
            ("حد البيانات", $"📊 {card.DataLimit}"),
            ("سياسة السرعة", $"🚀 {card.RateLimit}"),
            ("مدة الصلاحية", $"📅 {card.PackageValidity}"),
            ("ملف الراوتر (Profile)", $"⚙️ {card.RouterProfile}"),
            ("نقطة البيع", $"🏢 {card.PointOfSale}"),
            ("حساب المشرف", $"👨‍💻 {card.ManagerAccount}"),
            ("المستخدمون المتزامنون", $"👥 {card.SharedUsersLabel}"),
            ("تاريخ الإنشاء المحلي", $"🕒 {card.CreatedAtLabel}"),
            ("ملاحظات إضافية", string.IsNullOrWhiteSpace(card.Comment) ? "—" : card.Comment)
        };

        for (var i = 0; i < rows.Length; i++)
        {
            detailsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var bg = i % 2 == 0 ? Colors.White : Color.FromRgb(248, 250, 253);

            var lblBorder = new Border { Padding = new Thickness(10, 8, 10, 8), Background = new SolidColorBrush(bg), BorderBrush = new SolidColorBrush(Color.FromRgb(232, 221, 228)), BorderThickness = new Thickness(0, 0, 0, 1) };
            lblBorder.Child = new TextBlock { Text = rows[i].Label, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(104, 117, 141)), FontSize = 11.5 };

            var valBorder = new Border { Padding = new Thickness(10, 8, 10, 8), Background = new SolidColorBrush(bg), BorderBrush = new SolidColorBrush(Color.FromRgb(232, 221, 228)), BorderThickness = new Thickness(0, 0, 0, 1) };
            valBorder.Child = new TextBlock { Text = rows[i].Value, Foreground = new SolidColorBrush(Color.FromRgb(32, 43, 69)), FontSize = 11.5, TextWrapping = TextWrapping.Wrap };

            Grid.SetRow(lblBorder, i); Grid.SetColumn(lblBorder, 0); detailsGrid.Children.Add(lblBorder);
            Grid.SetRow(valBorder, i); Grid.SetColumn(valBorder, 1); detailsGrid.Children.Add(valBorder);
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
            RefreshFilteredCards();
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
        Dispatcher.InvokeAsync(RefreshFilteredCards);
    }

    private void OpenPrintQueueButton_Click(object sender, RoutedEventArgs e) =>
        NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("printing", "قائمة الطباعة"));

    private void UpdateSummary()
    {
        if (GeneratedCardsGrid is null || CardsCountSummaryTextBlock is null) return;

        var displayed = GeneratedCardsGrid.Items.Count;
        var totalSystemCards = GetCardsInCurrentSystemScope().Count();
        var totalBatches = GetCardsInCurrentSystemScope().Select(card => card.BatchId).Distinct(StringComparer.OrdinalIgnoreCase).Count();

        CardsCountSummaryTextBlock.Text = $"المعروض: {displayed:N0} سجل معلق · إجمالي السجلات ضمن النطاق: {totalSystemCards:N0} · إجمالي الدفعات: {totalBatches:N0}";
        EmptyCardsPanel.Visibility = displayed == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyCardsDescriptionTextBlock.Text = _issuanceService.Cards.Count == 0
            ? "لا توجد حسابات معلقة. أنشئ دفعة جديدة."
            : "لا توجد سجلات تطابق البحث والمرشحات الحالية.";
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

    // أحداث الأيقونات الفردية
    private void RowSyncIcon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is GeneratedDemoCard card)
        {
            _workflowStore.RecordRouterUpload(new[] { card });
            RefreshFilteredCards();
            MessageBox.Show(Window.GetWindow(this),
                $"⚡ تم تسجيل طلب المزامنة للكرت ({card.CardNumber}) بنجاح إلى الراوتر.",
                "المزامنة الفورية", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void RowPrintIcon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is GeneratedDemoCard card)
        {
            _workflowStore.QueuePrintSelection(new[] { card });
            RefreshFilteredCards();
            MessageBox.Show(Window.GetWindow(this),
                $"🖨️ تم تجهيز الكرت ({card.CardNumber}) وإضافته مباشرة إلى قائمة الطباعة.",
                "تجهيز الطباعة", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void RowSaleIcon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is GeneratedDemoCard card)
        {
            _workflowStore.RecordOnlineSalePreparation(new[] { card });
            RefreshFilteredCards();
            MessageBox.Show(Window.GetWindow(this),
                $"🛒 تم تسجيل الكرت ({card.CardNumber}) كمنتج جاهز للبيع الإلكتروني.",
                "البيع الإلكتروني", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}

// محول العرض التفاعلي لحالة الكرت باللغة والنص الملون
public class WorkflowStatusConverter : IValueConverter
{
    private static readonly DemoCardWorkflowStore WorkflowStore = new();

    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is GeneratedDemoCard card)
        {
            return WorkflowStore.GetCardStatusLabel(card.BatchId, card.CardNumber);
        }
        return "بانتظار المزامنة";
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}