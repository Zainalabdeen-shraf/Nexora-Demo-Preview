using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;

namespace Nexora.Demo.Desktop.Features.Cards.Views;

/// <summary>
/// Dedicated network-user screen. It reuses the existing issuance model for local demo records;
/// session values listed in DemoSessions are clearly demo presentation data, not RouterOS telemetry.
/// </summary>
public partial class NetworkUsersView : UserControl
{
    private readonly DemoCardIssuanceService _issuanceService;
    private readonly ObservableCollection<NetworkUserDisplayRow> _rows = new();
    private readonly ListCollectionView _rowsView;
    private bool _viewReady;
    private bool _refreshingFilterOptions;
    private bool _isSubscribed;
    private string _activityFilter = "all";
    private string _systemKey = "user-manager";

    // Temporary state for the presentation demo only. Nothing is written to the issuance service,
    // production storage, or RouterOS.
    private readonly Dictionary<string, DemoCardState> _demoStates = new(StringComparer.OrdinalIgnoreCase);

    private static readonly DemoPackageOption[] DemoPackageOptions =
    {
        new("6 ساعات — 300 ريال", 300m, TimeSpan.FromHours(6), "6 ساعات تقريبًا"),
        new("24 ساعة — 500 ريال", 500m, TimeSpan.FromHours(24), "24 ساعة تقريبًا"),
        new("أسبوع — 1,500 ريال", 1500m, TimeSpan.FromDays(7), "7 أيام تقريبًا"),
        new("شهر — 2,500 ريال", 2500m, TimeSpan.FromDays(30), "30 يومًا تقريبًا")
    };

    private static readonly IReadOnlyDictionary<string, DemoSessionData> DemoSessions =
        new Dictionary<string, DemoSessionData>(StringComparer.OrdinalIgnoreCase)
        {
            ["26001001"] = new(
                "مفعل", "متصل الآن", "192.168.10.5", "منذ 3 دقائق", "4C:5E:0C:12:34:56",
                "01:24:16", "482.1 MB", "125.4 MB", "12.8 GB", "3.4 GB",
                "2026/10/06 07:34", "9 ساعة و 12 دقيقة", 44, 47),
            ["26001003"] = new(
                "غير متصل", "غير متصل", "—", "منذ 2 ساعة", "غير متاح",
                "غير متاحة", "—", "—", "—", "—", "غير متاح", "غير متاح", 0, 0),
            ["26001004"] = new(
                "موقوف", "غير متصل", "—", "منذ 5 ساعات", "غير متاح",
                "غير متاحة", "—", "—", "—", "—", "غير متاح", "غير متاح", 0, 0),
            ["26001005"] = new(
                "مفعل", "متصل الآن", "192.168.20.12", "منذ دقيقة", "غير متاح",
                "غير متاحة", "—", "—", "—", "—", "غير متاح", "غير متاح", 0, 0),
            ["26001006"] = new(
                "منتهي", "غير متصل", "—", "منذ يوم", "غير متاح",
                "غير متاحة", "—", "—", "—", "—", "غير متاح", "انتهت الصلاحية", 0, 0),

            // Separate User Manager examples: account status is known, but HotSpot-style
            // session telemetry (IP/MAC/traffic) is not reported by this demo for these rows.
            ["26001009"] = new(
                "غير متصل", "غير متصل", "—", "غير متاح", "غير متاح",
                "غير متاحة", "—", "—", "—", "—", "غير متاح", "غير متاح", 0, 0),
            ["26001010"] = new(
                "مفعل", "غير متصل", "—", "غير متاح", "غير متاح",
                "غير متاحة", "—", "—", "—", "—", "غير متاح", "غير متاح", 0, 0),
            ["26001011"] = new(
                "منتهي", "غير متصل", "—", "غير متاح", "غير متاح",
                "غير متاحة", "—", "—", "—", "—", "غير متاح", "غير متاح", 0, 0)
        };

    #region Construction and public screen API

    public NetworkUsersView(DemoCardIssuanceService issuanceService, DemoAccountSystem targetSystem)
    {
        InitializeComponent();
        _issuanceService = issuanceService ?? throw new ArgumentNullException(nameof(issuanceService));
        _rowsView = new ListCollectionView(_rows) { Filter = MatchesRow };
        GeneratedCardsGrid.ItemsSource = _rowsView;

        // InitializeComponent may raise SelectionChanged from ComboBox items before the service is set.
        // The guard in FilterSelectionChanged prevents access to uninitialized fields during XAML loading.
        _viewReady = true;
        _systemKey = targetSystem == DemoAccountSystem.HotSpot ? "hotspot" : "user-manager";
        SelectSystemFilter(_systemKey);
        RefreshBatchFilterOptions();
        UpdateActivityButtonStyles();
        UpdateSearchPlaceholder();
        RebuildRows(selectFirst: true);
        Loaded += NetworkUsersView_Loaded;
        Unloaded += NetworkUsersView_Unloaded;
    }

    // Kept as a no-op compatibility event for the shared shell. The dedicated screen no longer exposes a router-open button.
    public event EventHandler? RouterConnectionRequested { add { } remove { } }

    public void SetSystemFilter(string systemKey)
    {
        _systemKey = systemKey is "hotspot" or "user-manager" or "all" ? systemKey : "user-manager";
        SelectSystemFilter(_systemKey);
        RefreshBatchFilterOptions();
        RefreshRows(keepSelection: true);
    }

    public void ShowAllSystems() => SetSystemFilter("all");

    public void SetActivityFilter(string activityKey)
    {
        _activityFilter = activityKey is "active" or "inactive" ? activityKey : "all";
        UpdateActivityButtonStyles();
        RefreshRows(keepSelection: true);
    }

    public void SetStatusFilter(string statusKey)
    {
        var desired = CardStatusFilterComboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), statusKey, StringComparison.Ordinal));
        if (desired is not null && !ReferenceEquals(CardStatusFilterComboBox.SelectedItem, desired))
            CardStatusFilterComboBox.SelectedItem = desired;
        RefreshRows(keepSelection: true);
    }

    public void SetBatchFilter(string batchId)
    {
        var desired = CardBatchFilterComboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), batchId, StringComparison.OrdinalIgnoreCase));
        if (desired is null && batchId != "all")
        {
            RefreshBatchFilterOptions();
            desired = CardBatchFilterComboBox.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), batchId, StringComparison.OrdinalIgnoreCase));
        }
        desired ??= CardBatchFilterComboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == "all");
        if (desired is not null && !ReferenceEquals(CardBatchFilterComboBox.SelectedItem, desired))
            CardBatchFilterComboBox.SelectedItem = desired;
        RefreshRows(keepSelection: true);
    }

    public void SetSearchQuery(string query)
    {
        if (!string.Equals(CardSearchTextBox.Text, query, StringComparison.Ordinal))
            CardSearchTextBox.Text = query;
        UpdateSearchPlaceholder();
        RefreshRows(keepSelection: true);
    }

    public void FocusCard(string cardNumber)
    {
        SetSearchQuery(cardNumber);
        var row = _rowsView.Cast<object>().OfType<NetworkUserDisplayRow>()
            .FirstOrDefault(item => string.Equals(item.CardNumber, cardNumber, StringComparison.OrdinalIgnoreCase));
        if (row is not null)
        {
            GeneratedCardsGrid.SelectedItem = row;
            GeneratedCardsGrid.ScrollIntoView(row);
            UpdateSelectedDetails();
        }
    }

    public void OpenCardDetails(string cardNumber)
    {
        FocusCard(cardNumber);
    }

    private void NetworkUsersView_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_isSubscribed)
        {
            _issuanceService.ScenarioChanged += IssuanceService_ScenarioChanged;
            _isSubscribed = true;
        }
        RefreshBatchFilterOptions();
        RefreshRows(keepSelection: true);
        if (GeneratedCardsGrid.SelectedItem is null && GeneratedCardsGrid.Items.Count > 0)
            GeneratedCardsGrid.SelectedIndex = 0;
    }

    private void NetworkUsersView_Unloaded(object sender, RoutedEventArgs e)
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
            {
                RefreshBatchFilterOptions();
                RebuildRows(selectFirst: GeneratedCardsGrid.SelectedItem is null);
            }
        });
    }

    #endregion

    #region Filters and view-model rows

    private void SelectSystemFilter(string systemKey)
    {
        var desired = CardSystemFilterComboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), systemKey, StringComparison.Ordinal));
        if (desired is not null && !ReferenceEquals(CardSystemFilterComboBox.SelectedItem, desired))
            CardSystemFilterComboBox.SelectedItem = desired;
    }

    private string GetCurrentSystemKey() => (CardSystemFilterComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? _systemKey;

    private void RefreshBatchFilterOptions()
    {
        if (!_viewReady || CardBatchFilterComboBox is null || _issuanceService is null)
            return;

        var current = (CardBatchFilterComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";
        var systemKey = GetCurrentSystemKey();
        var batches = _issuanceService.Batches
            .Where(batch => systemKey == "all" || SystemKeyForBatch(batch) == systemKey)
            .OrderByDescending(batch => batch.CreatedAt)
            .ToList();

        _refreshingFilterOptions = true;
        try
        {
            CardBatchFilterComboBox.Items.Clear();
            CardBatchFilterComboBox.Items.Add(new ComboBoxItem { Content = "كل الدفعات", Tag = "all" });
            foreach (var batch in batches)
            {
                CardBatchFilterComboBox.Items.Add(new ComboBoxItem
                {
                    Content = batch.BatchId,
                    Tag = batch.BatchId,
                    ToolTip = $"{batch.Quantity:N0} كرت · {batch.PackageName}"
                });
            }

            var desired = CardBatchFilterComboBox.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), current, StringComparison.OrdinalIgnoreCase));
            CardBatchFilterComboBox.SelectedItem = desired ?? CardBatchFilterComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
        }
        finally
        {
            _refreshingFilterOptions = false;
        }
    }

    private static string SystemKeyForBatch(GeneratedCardBatch batch) =>
        batch.TargetSystem.Contains("هوتسبوت", StringComparison.OrdinalIgnoreCase) ? "hotspot" : "user-manager";

    private void RebuildRows(bool selectFirst)
    {
        if (!_viewReady)
            return;

        var selectedCardNumber = (GeneratedCardsGrid.SelectedItem as NetworkUserDisplayRow)?.CardNumber;
        _rows.Clear();

        // The current demo model has no persisted SerialNumber field. Derive the displayed
        // sequence from creation order within each batch; production should persist this value
        // at issuance time when the batch model is upgraded.
        var serialNumbers = new Dictionary<(string BatchId, string CardNumber), int>();
        foreach (var batchCards in _issuanceService.Cards.GroupBy(card => card.BatchId, StringComparer.OrdinalIgnoreCase))
        {
            var orderedCards = batchCards
                .OrderBy(card => card.CreatedAt)
                .ThenBy(card => card.CardNumber, StringComparer.OrdinalIgnoreCase)
                .ToList();
            for (var index = 0; index < orderedCards.Count; index++)
                serialNumbers[(orderedCards[index].BatchId, orderedCards[index].CardNumber)] = index + 1;
        }

        foreach (var card in _issuanceService.Cards)
        {
            if (!serialNumbers.TryGetValue((card.BatchId, card.CardNumber), out var serialNumber))
                serialNumber = 1;
            _rows.Add(CreateDisplayRow(card, serialNumber));
        }

        _rowsView.Refresh();
        UpdateEmptyState();
        var desired = !string.IsNullOrWhiteSpace(selectedCardNumber)
            ? _rowsView.Cast<object>().OfType<NetworkUserDisplayRow>()
                .FirstOrDefault(row => string.Equals(row.CardNumber, selectedCardNumber, StringComparison.OrdinalIgnoreCase))
            : null;
        if (desired is not null)
            GeneratedCardsGrid.SelectedItem = desired;
        else if (selectFirst && _rowsView.Cast<object>().OfType<NetworkUserDisplayRow>().FirstOrDefault() is { } first)
            GeneratedCardsGrid.SelectedItem = first;
        else
            GeneratedCardsGrid.UnselectAll();

        UpdateSelectedDetails();
    }

    private void RefreshRows(bool keepSelection)
    {
        if (!_viewReady || _rowsView is null)
            return;
        var selectedCardNumber = keepSelection ? (GeneratedCardsGrid.SelectedItem as NetworkUserDisplayRow)?.CardNumber : null;
        _rowsView.Refresh();
        UpdateEmptyState();
        if (!string.IsNullOrWhiteSpace(selectedCardNumber))
        {
            var selected = _rowsView.Cast<object>().OfType<NetworkUserDisplayRow>()
                .FirstOrDefault(row => string.Equals(row.CardNumber, selectedCardNumber, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
                GeneratedCardsGrid.SelectedItem = selected;
            else if (_rowsView.Cast<object>().OfType<NetworkUserDisplayRow>().FirstOrDefault() is { } first)
                GeneratedCardsGrid.SelectedItem = first;
            else
                GeneratedCardsGrid.UnselectAll();
        }
        else if (GeneratedCardsGrid.SelectedItem is not NetworkUserDisplayRow && _rowsView.Cast<object>().OfType<NetworkUserDisplayRow>().FirstOrDefault() is { } first)
        {
            GeneratedCardsGrid.SelectedItem = first;
        }
        UpdateSelectedDetails();
    }

    private NetworkUserDisplayRow CreateDisplayRow(GeneratedDemoCard card, int serialNumber)
    {
        DemoSessions.TryGetValue(card.CardNumber, out var sample);
        var state = GetDemoState(card);
        var accountStatus = state.AccountStatus ?? sample?.AccountStatus ?? (card.DemoStatus switch
        {
            "قيد الاستخدام" => "مفعل",
            "متاحة" => "غير متصل",
            "موقوفة" => "موقوف",
            "منتهية" => "منتهي",
            _ => card.DemoStatus
        });

        sample ??= new DemoSessionData(
            accountStatus,
            accountStatus == "مفعل" ? "بيانات غير متاحة" : "غير متصل",
            "—", "غير متاح", "غير متاح", "غير متاحة", "—", "—", "—", "—", "غير متاح", "غير متاح", 0, 0);

        var displayCard = state.PackageName is not null || state.UnitPrice.HasValue
            ? card with
            {
                PackageName = state.PackageName ?? card.PackageName,
                UnitPrice = state.UnitPrice ?? card.UnitPrice
            }
            : card;
        var connectionStatus = state.ConnectionStatus ?? sample.ConnectionStatus;

        return new NetworkUserDisplayRow(
            displayCard,
            serialNumber,
            accountStatus,
            connectionStatus,
            state.IpAddress ?? sample.IpAddress,
            state.LastSeen ?? sample.LastSeen,
            state.MacAddress ?? sample.MacAddress,
            state.SessionDuration ?? sample.SessionDuration,
            state.Download ?? sample.Download,
            state.Upload ?? sample.Upload,
            state.TotalDownload ?? sample.TotalDownload,
            state.TotalUpload ?? sample.TotalUpload,
            NormalizeDateLabel(card.CreatedAtLabel),
            state.EndDate ?? sample.EndDate,
            state.RemainingTime ?? sample.RemainingTime,
            state.PackageProgress ?? sample.PackageProgress,
            state.UsageProgress ?? sample.UsageProgress,
            state.ConnectionStatus is not null || !string.Equals(connectionStatus, "غير متاح", StringComparison.Ordinal),
            state.DemoPassword ?? card.Password,
            state.PackageStartDate ?? NormalizeDateLabel(card.CreatedAtLabel),
            state.AddedDataGb);
    }

    private DemoCardState GetDemoState(GeneratedDemoCard card)
    {
        var key = $"{card.BatchId}\u001F{card.CardNumber}";
        if (!_demoStates.TryGetValue(key, out var state))
        {
            state = new DemoCardState();
            _demoStates[key] = state;
        }
        return state;
    }

    private bool MatchesRow(object item)
    {
        if (item is not NetworkUserDisplayRow row)
            return false;
        if (GetDemoState(row.Card).IsDeleted)
            return false;

        var systemKey = GetCurrentSystemKey();
        if (systemKey == "hotspot" && !row.Card.TargetSystem.Contains("هوتسبوت", StringComparison.OrdinalIgnoreCase))
            return false;
        if (systemKey == "user-manager" && !row.Card.TargetSystem.Contains("مدير المستخدمين", StringComparison.OrdinalIgnoreCase))
            return false;

        if (_activityFilter == "active" && !row.IsConnected)
            return false;
        if (_activityFilter == "inactive" && (row.IsConnected || !row.HasKnownConnectionState))
            return false;

        var batchFilter = (CardBatchFilterComboBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (!string.IsNullOrWhiteSpace(batchFilter) && batchFilter != "all" &&
            !string.Equals(row.BatchId, batchFilter, StringComparison.OrdinalIgnoreCase))
            return false;

        var statusFilter = (CardStatusFilterComboBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (!string.IsNullOrWhiteSpace(statusFilter) && statusFilter != "all" && row.AccountStatus != statusFilter)
            return false;

        var query = CardSearchTextBox?.Text?.Trim();
        if (string.IsNullOrWhiteSpace(query))
            return true;

        return Contains(row.BatchId, query) || Contains(row.SerialNumber.ToString(CultureInfo.InvariantCulture), query) ||
               Contains(row.CardNumber, query) || Contains(row.Username, query) ||
               Contains(row.PackageName, query) || Contains(row.Card.RouterProfile, query) ||
               Contains(row.Card.PointOfSale, query) || Contains(row.IpAddress, query) || Contains(row.AccountStatus, query);
    }

    private static bool Contains(string? value, string search) =>
        !string.IsNullOrEmpty(value) && value.Contains(search, StringComparison.OrdinalIgnoreCase);

    private void FilterSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_viewReady || _refreshingFilterOptions)
            return;
        if (ReferenceEquals(sender, CardSystemFilterComboBox))
        {
            _systemKey = GetCurrentSystemKey();
            RefreshBatchFilterOptions();
        }
        RefreshRows(keepSelection: true);
    }

    private void CardSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_viewReady)
            return;
        UpdateSearchPlaceholder();
        RefreshRows(keepSelection: true);
    }

    private void UpdateSearchPlaceholder()
    {
        if (CardSearchPlaceholderTextBlock is not null && CardSearchTextBox is not null)
            CardSearchPlaceholderTextBlock.Visibility = string.IsNullOrWhiteSpace(CardSearchTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    #endregion

    #region Screen interactions and data-grid actions

    private void ActivityFilterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string key)
            return;
        SetActivityFilter(key);
    }

    private void UpdateActivityButtonStyles()
    {
        ApplyActivityButton(ActivityActiveButton, _activityFilter == "active");
        ApplyActivityButton(ActivityInactiveButton, _activityFilter == "inactive");
    }

    private static void ApplyActivityButton(Button? button, bool selected)
    {
        if (button is null) return;
        button.Background = selected ? new SolidColorBrush(Color.FromRgb(199, 25, 50)) : Brushes.White;
        button.Foreground = selected ? Brushes.White : new SolidColorBrush(Color.FromRgb(23, 40, 73));
        button.BorderBrush = selected ? new SolidColorBrush(Color.FromRgb(199, 25, 50)) : new SolidColorBrush(Color.FromRgb(220, 228, 239));
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshBatchFilterOptions();
        RebuildRows(selectFirst: true);
    }

    private void GeneratedCardsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewReady)
            UpdateSelectedDetails();
    }

    private void GeneratedCardsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GeneratedCardsGrid.SelectedItem is NetworkUserDisplayRow row)
            UpdateSelectedDetails(row);
    }

    private void CardMarkerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not NetworkUserDisplayRow row)
            return;

        // The red batch marker opens the full set of cards produced in this batch.
        GeneratedCardsGrid.UnselectAll();
        GeneratedCardsGrid.SelectedItem = row;
        if (!string.IsNullOrWhiteSpace(CardSearchTextBox.Text))
            CardSearchTextBox.Clear();
        if (CardStatusFilterComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag?.ToString() == "all") is { } allStatus)
            CardStatusFilterComboBox.SelectedItem = allStatus;
        SetActivityFilter("all");
        SetBatchFilter(row.BatchId);

        var visibleRow = _rowsView.Cast<object>().OfType<NetworkUserDisplayRow>()
            .FirstOrDefault(item => string.Equals(item.CardNumber, row.CardNumber, StringComparison.OrdinalIgnoreCase));
        if (visibleRow is not null)
        {
            GeneratedCardsGrid.SelectedItem = visibleRow;
            GeneratedCardsGrid.ScrollIntoView(visibleRow);
            UpdateSelectedDetails(visibleRow);
        }
    }

    private void RowActionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not NetworkUserDisplayRow row)
            return;

        // Quick actions are per row, never accidentally applied to a previous multi-selection.
        GeneratedCardsGrid.UnselectAll();
        GeneratedCardsGrid.SelectedItem = row;
        UpdateSelectedDetails(row);
        if (button.ContextMenu is { } menu)
        {
            menu.PlacementTarget = button;
            menu.DataContext = row;
            menu.IsOpen = true;
        }
    }

    private void RowActionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item)
            return;

        var contextMenu = item.Parent as ContextMenu ?? ItemsControl.ItemsControlFromItemContainer(item) as ContextMenu;
        var row = (contextMenu?.PlacementTarget as Button)?.DataContext as NetworkUserDisplayRow;
        if (row is null)
        {
            MessageBox.Show(Window.GetWindow(this), "لم يتم تحديد حساب من الجدول.", "مستخدمو الشبكة", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        GeneratedCardsGrid.UnselectAll();
        GeneratedCardsGrid.SelectedItem = row;
        UpdateSelectedDetails(row);
        ExecuteAction(item.Tag?.ToString() ?? string.Empty, new List<NetworkUserDisplayRow> { row });
    }

    private void ShowDetailsMenuItem_Click(object sender, RoutedEventArgs e) => UpdateSelectedDetails();

    private void UpdateSelectedDetails() => UpdateSelectedDetails(GeneratedCardsGrid.SelectedItem as NetworkUserDisplayRow);

    private void UpdateSelectedDetails(NetworkUserDisplayRow? row)
    {
        if (!_viewReady || SelectedCardNumberTextBlock is null)
            return;

        var selectedCount = GeneratedCardsGrid.SelectedItems.Count;
        if (selectedCount > 1)
        {
            SetDetailText("تم تحديد عدة حسابات", "حدد حسابًا واحدًا لعرض التفاصيل");
            return;
        }
        if (row is null)
        {
            SetDetailText("—", "—");
            return;
        }

        SelectedCardNumberTextBlock.Text = row.CardNumber;
        SelectedPasswordTextBlock.Text = row.PasswordDisplay;
        SelectedBatchIdTextBlock.Text = row.BatchId;
        SelectedPackageNameTextBlock.Text = string.IsNullOrWhiteSpace(row.PackageName) ? "—" : row.PackageName;
        SelectedCreatedAtTextBlock.Text = row.CreatedAtLabel;
        SelectedAccountExpiryTextBlock.Text = row.EndDate;
        SelectedCardStatusTextBlock.Text = row.AccountStatus;
        SelectedConnectionStatusTextBlock.Text = row.ConnectionStatus;
        SelectedIpAddressTextBlock.Text = row.IpAddress;
        SelectedMacAddressTextBlock.Text = row.MacAddress;
        SelectedLastSeenTextBlock.Text = row.LastSeenLabel;
        SelectedSessionDurationTextBlock.Text = row.SessionDuration;
        SelectedDownloadTextBlock.Text = row.Download;
        SelectedUploadTextBlock.Text = row.Upload;
        SelectedTotalDownloadTextBlock.Text = row.TotalDownload;
        SelectedTotalUploadTextBlock.Text = row.TotalUpload;
        SelectedPackageStartTextBlock.Text = row.PackageStartDate;
        SelectedPackageEndTextBlock.Text = row.EndDate;
        SelectedTimeRemainingTextBlock.Text = row.RemainingTime;
        SelectedPackageNameInSubscriptionTextBlock.Text = string.IsNullOrWhiteSpace(row.PackageName) ? "—" : row.PackageName;
        SelectedUsageProgressBar.Value = row.UsageProgress;
        SelectedPackageProgressBar.Value = row.PackageProgress;
        SelectedUsageSummaryTextBlock.Text = row.AddedDataGb > 0m
            ? $"إجمالي الاستخدام · رصيد إضافي: {row.AddedDataGb.ToString("0.##", CultureInfo.InvariantCulture)} GB"
            : "إجمالي الاستخدام";
        UpdateBadge(SelectedCardStatusBadgeBorder, SelectedCardStatusTextBlock, SelectedCardStatusDot, row.AccountStatus, account: true);
        UpdateBadge(SelectedConnectionStatusBorder, SelectedConnectionStatusTextBlock, SelectedConnectionStatusDot, row.ConnectionStatus, account: false);
        var connected = row.IsConnected;
        SelectedLiveBadgeTextBlock.Text = connected ? "متصل الآن" : row.ConnectionStatus;
        SelectedLiveBadgeBorder.Background = connected ? new SolidColorBrush(Color.FromRgb(213, 248, 228)) : new SolidColorBrush(Color.FromRgb(233, 238, 247));
        SelectedLiveBadgeBorder.BorderBrush = connected ? new SolidColorBrush(Color.FromRgb(120, 217, 160)) : new SolidColorBrush(Color.FromRgb(190, 202, 219));
        SelectedLiveBadgeDot.Fill = connected ? (Brush)FindResource("NuGreen") : (Brush)FindResource("NuGray");
        SelectedLiveBadgeTextBlock.Foreground = connected ? new SolidColorBrush(Color.FromRgb(8, 118, 66)) : new SolidColorBrush(Color.FromRgb(78, 94, 119));
    }

    private void SetDetailText(string cardNumber, string placeholder)
    {
        SelectedCardNumberTextBlock.Text = cardNumber;
        SelectedPasswordTextBlock.Text = placeholder;
        SelectedBatchIdTextBlock.Text = placeholder;
        SelectedPackageNameTextBlock.Text = placeholder;
        SelectedCreatedAtTextBlock.Text = placeholder;
        SelectedAccountExpiryTextBlock.Text = placeholder;
        SelectedCardStatusTextBlock.Text = "تحديد متعدد";
        SelectedConnectionStatusTextBlock.Text = placeholder;
        SelectedIpAddressTextBlock.Text = placeholder;
        SelectedMacAddressTextBlock.Text = placeholder;
        SelectedLastSeenTextBlock.Text = placeholder;
        SelectedSessionDurationTextBlock.Text = placeholder;
        SelectedDownloadTextBlock.Text = placeholder;
        SelectedUploadTextBlock.Text = placeholder;
        SelectedTotalDownloadTextBlock.Text = placeholder;
        SelectedTotalUploadTextBlock.Text = placeholder;
        SelectedPackageStartTextBlock.Text = placeholder;
        SelectedPackageEndTextBlock.Text = placeholder;
        SelectedTimeRemainingTextBlock.Text = placeholder;
        SelectedPackageNameInSubscriptionTextBlock.Text = placeholder;
        SelectedUsageProgressBar.Value = 0;
        SelectedPackageProgressBar.Value = 0;
        SelectedUsageSummaryTextBlock.Text = "إجمالي الاستخدام";
        UpdateBadge(SelectedCardStatusBadgeBorder, SelectedCardStatusTextBlock, SelectedCardStatusDot, "تحديد متعدد", account: true);
        UpdateBadge(SelectedConnectionStatusBorder, SelectedConnectionStatusTextBlock, SelectedConnectionStatusDot, placeholder, account: false);
        SelectedLiveBadgeTextBlock.Text = placeholder;
        SelectedLiveBadgeBorder.Background = (Brush)FindResource("NuGraySoft");
        SelectedLiveBadgeBorder.BorderBrush = (Brush)FindResource("NuLine");
        SelectedLiveBadgeDot.Fill = (Brush)FindResource("NuGray");
        SelectedLiveBadgeTextBlock.Foreground = (Brush)FindResource("NuGray");
    }

    private void UpdateBadge(Border border, TextBlock text, Ellipse dot, string status, bool account)
    {
        var colors = status switch
        {
            "مفعل" or "متصل الآن" => (Color.FromRgb(213, 248, 228), Color.FromRgb(120, 217, 160), Color.FromRgb(8, 118, 66)),
            "موقوف" => (Color.FromRgb(255, 233, 184), Color.FromRgb(240, 190, 89), Color.FromRgb(153, 92, 0)),
            "منتهي" => (Color.FromRgb(255, 221, 229), Color.FromRgb(237, 145, 162), Color.FromRgb(183, 19, 46)),
            _ => (Color.FromRgb(233, 238, 247), Color.FromRgb(190, 202, 219), Color.FromRgb(78, 94, 119))
        };
        border.Background = new SolidColorBrush(colors.Item1);
        border.BorderBrush = new SolidColorBrush(colors.Item2);
        text.Foreground = new SolidColorBrush(colors.Item3);
        dot.Fill = new SolidColorBrush(colors.Item3);
    }

    private void UpdateEmptyState()
    {
        if (EmptyCardsPanel is null || GeneratedCardsGrid is null)
            return;
        EmptyCardsPanel.Visibility = GeneratedCardsGrid.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyCardsDescriptionTextBlock.Text = _issuanceService.Cards.Count == 0
            ? "لا توجد حسابات في بيانات العرض الحالية."
            : "لا توجد حسابات تطابق المرشحات الحالية؛ غيّر الحالة أو امسح البحث.";
    }

    #endregion

    #region Detail panel and demo actions

    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = GeneratedCardsGrid.SelectedItems.OfType<NetworkUserDisplayRow>().ToList();
        if (selected.Count == 0 && GeneratedCardsGrid.SelectedItem is NetworkUserDisplayRow single)
            selected.Add(single);

        ExecuteAction((sender as FrameworkElement)?.Tag?.ToString() ?? string.Empty, selected);
    }

    private void ExecuteAction(string action, IReadOnlyList<NetworkUserDisplayRow> selected)
    {
        if (selected.Count == 0)
        {
            ShowDemoMessage("اختر حسابًا", "حدد مستخدمًا من الجدول أولًا.");
            return;
        }
        if (selected.Count > 1)
        {
            ShowDemoMessage("حدد مستخدمًا واحدًا", "هذه العمليات تعدّل الكرت المحدد فقط. اختر صفًا واحدًا حتى لا تلتبس عليك نتيجة التعديل.");
            return;
        }

        var row = selected[0];
        var state = GetDemoState(row.Card);
        switch (action)
        {
            case "activate":
                state.IsDeleted = false;
                state.AccountStatus = "مفعل";
                RefreshAfterDemoAction(row);
                ShowDemoMessage("تم تفعيل الحساب في المعاينة", $"تم عرض حالة {row.CardNumber} على أنها مفعل. لم يتغير أي حساب حقيقي.");
                break;

            case "suspend":
                state.AccountStatus = "موقوف";
                SetDemoConnectionDown(state);
                RefreshAfterDemoAction(row);
                ShowDemoMessage("تم إيقاف الحساب في المعاينة", $"أصبحت حالة {row.CardNumber} موقوف، وانعكس ذلك في الجدول والتفاصيل.");
                break;

            case "disconnect":
                SetDemoConnectionDown(state);
                RefreshAfterDemoAction(row);
                ShowDemoMessage("تم فصل الاتصال في المعاينة", $"تم عرض {row.CardNumber} كغير متصل، وأزيل عنوان IP وبيانات الجلسة من لوحة التفاصيل.");
                break;

            case "change-package":
            case "edit":
            {
                var result = ShowCardEditorDialog(row, title: "تعديل بيانات الكرت", applyLabel: "حفظ التغييرات");
                if (result is null) return;

                var packageChanged = !string.Equals(result.Package.Name, row.PackageName, StringComparison.OrdinalIgnoreCase);
                if (packageChanged)
                    ApplyDemoPackage(state, result.Package);

                if (result.ValidityExtension > TimeSpan.Zero)
                    ExtendDemoValidity(row, state, result.ValidityExtension);

                if (result.AdditionalDataGb > 0m)
                    state.AddedDataGb += result.AdditionalDataGb;

                if (!string.IsNullOrWhiteSpace(result.Password))
                    state.DemoPassword = result.Password.Trim();

                state.AccountStatus = result.AccountStatus;
                if (state.AccountStatus is "غير متصل" or "موقوف" or "منتهي")
                    SetDemoConnectionDown(state);

                RefreshAfterDemoAction(row);
                var extensionText = result.ValidityExtension > TimeSpan.Zero
                    ? $"\nالتمديد الإضافي: {result.ValidityExtension.TotalHours:0.##} ساعة"
                    : string.Empty;
                var addedDataText = result.AdditionalDataGb > 0m
                    ? $"\nالرصيد المضاف: {result.AdditionalDataGb.ToString("0.##", CultureInfo.InvariantCulture)} GB"
                    : string.Empty;
                ShowDemoMessage(
                    "تم تحديث بيانات الكرت في المعاينة",
                    $"الكرت: {row.CardNumber}\nالباقة: {result.Package.Name}\nالحالة: {state.AccountStatus}\nتاريخ البداية: {state.PackageStartDate ?? row.PackageStartDate}\nتاريخ الانتهاء: {state.EndDate ?? row.EndDate}{extensionText}{addedDataText}\n\nالتعديل خاص بهذا المستخدم وحده، ولا يغيّر بقية كروت الدفعة.");
                break;
            }

            case "renew-package":
            {
                var package = DemoPackageOptions.FirstOrDefault(option => string.Equals(option.Name, row.PackageName, StringComparison.OrdinalIgnoreCase))
                    ?? DemoPackageOptions.First(option => option.Price == 500m);
                ApplyDemoPackage(state, package);
                state.AccountStatus = "مفعل";
                RefreshAfterDemoAction(row);
                ShowDemoMessage("تم تجديد الباقة في المعاينة", $"تم تجديد {row.CardNumber} على باقة {package.Name}.\nتاريخ الانتهاء التجريبي: {state.EndDate}");
                break;
            }

            case "reset-password":
            {
                state.DemoPassword = "TC-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
                RefreshAfterDemoAction(row);
                ShowDemoMessage("كلمة مرور تجريبية جديدة", $"الكرت: {row.CardNumber}\nكلمة مرور العرض: {state.DemoPassword}\n\nهذه كلمة مرور للمعاينة فقط؛ لم يتم إرسالها إلى الراوتر أو حفظها.");
                break;
            }

            case "delete":
            {
                var owner = Window.GetWindow(this);
                var prompt = $"حذف {row.CardNumber} من عرض الديمو الحالي؟\n\nسيختفي من الجدول داخل هذه الشاشة فقط، ولن يُحذف من بيانات الإصدار أو من أي راوتر.";
                var answer = owner is null
                    ? MessageBox.Show(prompt, "تأكيد حذف تجريبي", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                    : MessageBox.Show(owner, prompt, "تأكيد حذف تجريبي", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (answer != MessageBoxResult.Yes) return;
                state.IsDeleted = true;
                GeneratedCardsGrid.UnselectAll();
                _rowsView.Refresh();
                UpdateEmptyState();
                UpdateSelectedDetails();
                ShowDemoMessage("تم إخفاء الحساب في المعاينة", $"تم إخفاء {row.CardNumber} من هذه الشاشة. لم يتم حذف سجل الكرت أو بيانات الراوتر.");
                break;
            }

            default:
                ShowDemoMessage("إجراء غير معروف", "الإجراء المحدد غير موجود في نموذج العرض الحالي.");
                break;
        }
    }

    private void SetDemoConnectionDown(DemoCardState state)
    {
        state.ConnectionStatus = "غير متصل";
        state.IpAddress = "—";
        state.MacAddress = "غير متاح";
        state.SessionDuration = "غير نشطة";
        state.Download = "—";
        state.Upload = "—";
        state.LastSeen = "الآن";
    }

    private void ApplyDemoPackage(DemoCardState state, DemoPackageOption package)
    {
        var now = DateTime.Now;
        state.PackageName = package.Name;
        state.UnitPrice = package.Price;
        state.PackageStartDate = now.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);
        state.EndDate = now.Add(package.Duration).ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);
        state.RemainingTime = package.RemainingLabel;
        state.PackageProgress = 0;
    }

    private void ExtendDemoValidity(NetworkUserDisplayRow row, DemoCardState state, TimeSpan extension)
    {
        var currentEndText = state.EndDate ?? row.EndDate;
        var formats = new[] { "yyyy/MM/dd HH:mm", "yyyy/M/d HH:mm", "yyyy/MM/dd H:mm" };
        var now = DateTime.Now;
        var effectiveEnd = DateTime.TryParseExact(currentEndText, formats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsedEnd) && parsedEnd > now
                ? parsedEnd
                : now;
        var newEnd = effectiveEnd.Add(extension);
        state.EndDate = newEnd.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);
        state.RemainingTime = FormatRemainingTime(newEnd - now);
    }

    private static string FormatRemainingTime(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero) return "انتهت الصلاحية";
        if (remaining.TotalDays >= 1)
            return $"{(int)Math.Floor(remaining.TotalDays)} يوم و {remaining.Hours} ساعة";
        if (remaining.TotalHours >= 1)
            return $"{(int)Math.Floor(remaining.TotalHours)} ساعة و {remaining.Minutes} دقيقة";
        return $"{Math.Max(1, remaining.Minutes)} دقيقة";
    }

    private void RefreshAfterDemoAction(NetworkUserDisplayRow previousRow)
    {
        // Reveal the changed state even when it no longer matches the old activity/status selection.
        _activityFilter = "all";
        UpdateActivityButtonStyles();
        var allStatus = CardStatusFilterComboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == "all");
        if (allStatus is not null && !ReferenceEquals(CardStatusFilterComboBox.SelectedItem, allStatus))
            CardStatusFilterComboBox.SelectedItem = allStatus;

        RebuildRows(selectFirst: false);
        var updated = _rowsView.Cast<object>().OfType<NetworkUserDisplayRow>()
            .FirstOrDefault(item => string.Equals(item.CardNumber, previousRow.CardNumber, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.BatchId, previousRow.BatchId, StringComparison.OrdinalIgnoreCase));
        if (updated is null && !string.IsNullOrWhiteSpace(CardSearchTextBox.Text))
        {
            CardSearchTextBox.Clear();
            _rowsView.Refresh();
            UpdateEmptyState();
            updated = _rowsView.Cast<object>().OfType<NetworkUserDisplayRow>()
                .FirstOrDefault(item => string.Equals(item.CardNumber, previousRow.CardNumber, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(item.BatchId, previousRow.BatchId, StringComparison.OrdinalIgnoreCase));
        }
        if (updated is not null)
        {
            GeneratedCardsGrid.SelectedItem = updated;
            GeneratedCardsGrid.ScrollIntoView(updated);
            UpdateSelectedDetails(updated);
        }
    }


    private DemoEditResult? ShowCardEditorDialog(NetworkUserDisplayRow row, string title, string applyLabel)
    {
        var owner = Window.GetWindow(this);
        var dialog = new Window
        {
            Title = title,
            Width = 540,
            Height = 650,
            MinWidth = 540,
            MinHeight = 650,
            MaxWidth = 540,
            MaxHeight = 650,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(243, 245, 249)),
            FlowDirection = FlowDirection.RightToLeft,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI")
        };
        if (owner is not null) dialog.Owner = owner;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(183, 19, 46)),
            Padding = new Thickness(18, 14, 18, 13)
        };
        var headerStack = new StackPanel();
        headerStack.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Brushes.White });
        headerStack.Children.Add(new TextBlock
        {
            Text = "إعدادات هذا الكرت فقط — التعديلات مؤقتة داخل المعاينة",
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 225, 232)),
            Margin = new Thickness(0, 4, 0, 0)
        });
        header.Child = headerStack;
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var content = new StackPanel { Margin = new Thickness(18, 11, 18, 8) };
        var summary = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(232, 197, 207)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 9)
        };
        var summaryStack = new StackPanel();
        summaryStack.Children.Add(new TextBlock
        {
            Text = $"الكرت {row.CardNumber}  ·  الرقم التسلسلي {row.SerialNumber}  ·  الدفعة {row.BatchId}",
            FontSize = 11, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(23, 40, 73))
        });
        summaryStack.Children.Add(new TextBlock
        {
            Text = $"الباقة: {row.PackageName}  ·  الحالة: {row.AccountStatus}  ·  الانتهاء: {row.EndDate}",
            FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(99, 113, 137)),
            Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis
        });
        summary.Child = summaryStack;
        content.Children.Add(summary);

        void AddLabel(string text) => content.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 10.5,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(23, 40, 73)),
            Margin = new Thickness(0, 0, 0, 4)
        });

        ComboBox CreateEditorCombo()
        {
            return new ComboBox
            {
                Height = 36,
                FlowDirection = FlowDirection.RightToLeft,
                FontSize = 11.5,
                Style = FindResource("NuCombo") as Style,
                Margin = new Thickness(0, 0, 0, 8)
            };
        }

        TextBox CreateEditorTextBox(string text, FlowDirection flowDirection = FlowDirection.LeftToRight)
        {
            return new TextBox
            {
                Text = text,
                Height = 36,
                Padding = new Thickness(10, 3, 10, 3),
                VerticalContentAlignment = VerticalAlignment.Center,
                FlowDirection = flowDirection,
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(206, 217, 232)),
                BorderThickness = new Thickness(1),
                Foreground = new SolidColorBrush(Color.FromRgb(23, 40, 73)),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 8)
            };
        }

        AddLabel("الباقة");
        var packageBox = CreateEditorCombo();
        foreach (var option in DemoPackageOptions)
            packageBox.Items.Add(new ComboBoxItem { Content = option.Name, Tag = option });
        packageBox.SelectedItem = packageBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals((item.Tag as DemoPackageOption)?.Name, row.PackageName, StringComparison.OrdinalIgnoreCase))
            ?? packageBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
        content.Children.Add(packageBox);

        AddLabel("تمديد الصلاحية الحالية");
        var extensionOptions = new[]
        {
            new ValidityExtensionOption("بدون تغيير الصلاحية", TimeSpan.Zero),
            new ValidityExtensionOption("إضافة ساعة واحدة", TimeSpan.FromHours(1)),
            new ValidityExtensionOption("إضافة 6 ساعات", TimeSpan.FromHours(6)),
            new ValidityExtensionOption("إضافة 24 ساعة", TimeSpan.FromHours(24)),
            new ValidityExtensionOption("إضافة أسبوع (7 أيام)", TimeSpan.FromDays(7)),
            new ValidityExtensionOption("إضافة شهر (30 يومًا)", TimeSpan.FromDays(30))
        };
        var extensionBox = CreateEditorCombo();
        foreach (var option in extensionOptions)
            extensionBox.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option });
        extensionBox.SelectedIndex = 0;
        content.Children.Add(extensionBox);

        AddLabel($"إضافة رصيد بيانات (GB) — المضاف سابقًا {row.AddedDataGb.ToString("0.##", CultureInfo.InvariantCulture)} GB");
        var extraDataBox = CreateEditorTextBox("0");
        extraDataBox.ToolTip = "أدخل كمية البيانات الإضافية التي تريد إضافتها لهذا المستخدم؛ الصفر يعني عدم الإضافة.";
        content.Children.Add(extraDataBox);

        AddLabel("كلمة المرور");
        var passwordBox = CreateEditorTextBox(row.PasswordValue);
        passwordBox.ToolTip = "كلمة مرور هذا الكرت. اتركها كما هي إذا لم ترغب بتغييرها.";
        content.Children.Add(passwordBox);

        AddLabel("حالة الحساب");
        var statusBox = CreateEditorCombo();
        foreach (var status in new[] { "مفعل", "غير متصل", "موقوف", "منتهي" })
            statusBox.Items.Add(new ComboBoxItem { Content = status, Tag = status });
        statusBox.SelectedItem = statusBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), row.AccountStatus, StringComparison.OrdinalIgnoreCase))
            ?? statusBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
        content.Children.Add(statusBox);

        content.Children.Add(new TextBlock
        {
            Text = "التمديد يُضاف إلى الصلاحية المتبقية، وإذا كانت منتهية يبدأ من الآن. رصيد البيانات المضاف يتراكم لهذا المستخدم فقط. سجل التغييرات التفصيلي سيُنفذ لاحقًا في شاشة مستقلة.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 9.5,
            Foreground = new SolidColorBrush(Color.FromRgb(99, 113, 137)),
            Margin = new Thickness(0, 0, 0, 0)
        });
        Grid.SetRow(content, 1);
        root.Children.Add(content);

        var footer = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 228, 239)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(16, 9, 16, 9)
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            FlowDirection = FlowDirection.LeftToRight,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        buttons.Children.Add(new Button
        {
            Content = "إلغاء", Width = 92, Height = 34, Margin = new Thickness(0, 0, 8, 0),
            Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(210, 220, 233)),
            BorderThickness = new Thickness(1), Foreground = new SolidColorBrush(Color.FromRgb(23, 40, 73)),
            FontWeight = FontWeights.SemiBold, IsCancel = true
        });
        var applyButton = new Button
        {
            Content = applyLabel, Width = 140, Height = 34,
            Background = new SolidColorBrush(Color.FromRgb(199, 25, 50)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(199, 25, 50)), BorderThickness = new Thickness(1),
            Foreground = Brushes.White, FontWeight = FontWeights.Bold, IsDefault = true
        };
        applyButton.Click += (_, _) =>
        {
            var rawExtraData = extraDataBox.Text.Trim();
            decimal extraData;
            if (string.IsNullOrWhiteSpace(rawExtraData))
                extraData = 0m;
            else if (!decimal.TryParse(rawExtraData, NumberStyles.Number, CultureInfo.CurrentCulture, out extraData)
                && !decimal.TryParse(rawExtraData, NumberStyles.Number, CultureInfo.InvariantCulture, out extraData))
            {
                MessageBox.Show(dialog, "أدخل رصيد بيانات صحيحًا بالأرقام، مثل 2 أو 1.5.", "قيمة غير صحيحة", MessageBoxButton.OK, MessageBoxImage.Warning);
                extraDataBox.Focus();
                extraDataBox.SelectAll();
                return;
            }

            if (extraData < 0m)
            {
                MessageBox.Show(dialog, "رصيد البيانات لا يمكن أن يكون سالبًا.", "قيمة غير صحيحة", MessageBoxButton.OK, MessageBoxImage.Warning);
                extraDataBox.Focus();
                extraDataBox.SelectAll();
                return;
            }
            if (packageBox.SelectedItem is not ComboBoxItem || extensionBox.SelectedItem is not ComboBoxItem || statusBox.SelectedItem is not ComboBoxItem)
                return;

            dialog.DialogResult = true;
        };
        buttons.Children.Add(applyButton);
        footer.Child = buttons;
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        dialog.Content = root;

        if (dialog.ShowDialog() != true)
            return null;

        var selectedPackage = (packageBox.SelectedItem as ComboBoxItem)?.Tag as DemoPackageOption;
        var selectedStatus = (statusBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var selectedExtension = (extensionBox.SelectedItem as ComboBoxItem)?.Tag as ValidityExtensionOption;
        var enteredExtraData = decimal.TryParse(extraDataBox.Text.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out var parsedExtraData)
            ? parsedExtraData
            : decimal.TryParse(extraDataBox.Text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out parsedExtraData) ? parsedExtraData : 0m;
        return selectedPackage is null || selectedExtension is null || selectedStatus is null
            ? null
            : new DemoEditResult(selectedPackage, selectedStatus, selectedExtension.Duration, enteredExtraData, passwordBox.Text.Trim());
    }

    private void ShowDemoMessage(string title, string message)
    {
        var fullMessage = message + "\n\nمحاكاة عرض محلية فقط — لا يوجد اتصال بالراوتر ولا حفظ دائم.";
        var owner = Window.GetWindow(this);
        if (owner is null)
            MessageBox.Show(fullMessage, title, MessageBoxButton.OK, MessageBoxImage.Information);
        else
            MessageBox.Show(owner, fullMessage, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void RefreshRowsForActivity(string key) => SetActivityFilter(key);

    private static string NormalizeDateLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "—";
        var match = Regex.Match(value, @"(?<time>\d{1,2}:\d{2})\s+(?<date>\d{4}/\d{2}/\d{2})");
        return match.Success ? $"{match.Groups["date"].Value} {match.Groups["time"].Value}" : value;
    }

    #endregion

    #region Demo-only presentation data

    private sealed class DemoCardState
    {
        public string? AccountStatus { get; set; }
        public string? ConnectionStatus { get; set; }
        public string? IpAddress { get; set; }
        public string? LastSeen { get; set; }
        public string? MacAddress { get; set; }
        public string? SessionDuration { get; set; }
        public string? Download { get; set; }
        public string? Upload { get; set; }
        public string? TotalDownload { get; set; }
        public string? TotalUpload { get; set; }
        public string? PackageName { get; set; }
        public decimal? UnitPrice { get; set; }
        public string? PackageStartDate { get; set; }
        public string? EndDate { get; set; }
        public string? RemainingTime { get; set; }
        public double? PackageProgress { get; set; }
        public double? UsageProgress { get; set; }
        public string? DemoPassword { get; set; }
        public decimal AddedDataGb { get; set; }
        public bool IsDeleted { get; set; }
    }

    private sealed record DemoPackageOption(string Name, decimal Price, TimeSpan Duration, string RemainingLabel);
    private sealed record ValidityExtensionOption(string Label, TimeSpan Duration);
    private sealed record DemoEditResult(DemoPackageOption Package, string AccountStatus, TimeSpan ValidityExtension, decimal AdditionalDataGb, string Password);

    private sealed record DemoSessionData(
        string AccountStatus,
        string ConnectionStatus,
        string IpAddress,
        string LastSeen,
        string MacAddress,
        string SessionDuration,
        string Download,
        string Upload,
        string TotalDownload,
        string TotalUpload,
        string EndDate,
        string RemainingTime,
        double PackageProgress,
        double UsageProgress);

    #endregion
}

#region Screen row projection

/// <summary>Presentation row wrapper for the existing demo-card model.</summary>
public sealed class NetworkUserDisplayRow
{
    public GeneratedDemoCard Card { get; }
    public string BatchId { get; }
    public string CardNumber { get; }
    public int SerialNumber { get; }
    public string BatchMarkerToolTip => $"فتح كروت الدفعة: {BatchId}";
    public string Username { get; }
    public string PasswordValue { get; }
    public string PasswordDisplay => string.IsNullOrWhiteSpace(PasswordValue) ? "بدون كلمة مرور" : PasswordValue;
    public string PackageName { get; }
    public string CreatedAtLabel { get; }
    public string PackageStartDate { get; }
    public decimal AddedDataGb { get; }
    public string AccountStatus { get; }
    public string StateToolTip { get; }
    public string ConnectionStatus { get; }
    public string IpAddress { get; }
    public string LastSeenLabel { get; }
    public string MacAddress { get; }
    public string SessionDuration { get; }
    public string Download { get; }
    public string Upload { get; }
    public string TotalDownload { get; }
    public string TotalUpload { get; }
    public string EndDate { get; }
    public string RemainingTime { get; }
    public double PackageProgress { get; }
    public double UsageProgress { get; }
    public bool IsConnected => ConnectionStatus == "متصل الآن";
    public bool HasKnownConnectionState { get; }

    public NetworkUserDisplayRow(
        GeneratedDemoCard card,
        int serialNumber,
        string accountStatus,
        string connectionStatus,
        string ipAddress,
        string lastSeenLabel,
        string macAddress,
        string sessionDuration,
        string download,
        string upload,
        string totalDownload,
        string totalUpload,
        string createdAtLabel,
        string endDate,
        string remainingTime,
        double packageProgress,
        double usageProgress,
        bool hasKnownConnectionState,
        string? passwordValue,
        string packageStartDate,
        decimal addedDataGb)
    {
        Card = card;
        BatchId = card.BatchId;
        CardNumber = card.CardNumber;
        SerialNumber = serialNumber;
        // In this product's network-card workflow, the RouterOS username is the card number.
        Username = CardNumber;
        PasswordValue = passwordValue ?? string.Empty;
        PackageName = card.PackageName;
        CreatedAtLabel = createdAtLabel;
        PackageStartDate = packageStartDate;
        AddedDataGb = addedDataGb;
        AccountStatus = accountStatus;
        StateToolTip = $"حالة الحساب: {accountStatus}";
        ConnectionStatus = connectionStatus;
        IpAddress = ipAddress;
        LastSeenLabel = lastSeenLabel;
        MacAddress = macAddress;
        SessionDuration = sessionDuration;
        Download = download;
        Upload = upload;
        TotalDownload = totalDownload;
        TotalUpload = totalUpload;
        EndDate = endDate;
        RemainingTime = remainingTime;
        PackageProgress = packageProgress;
        UsageProgress = usageProgress;
        HasKnownConnectionState = hasKnownConnectionState;
    }
}

#endregion // Screen row projection
