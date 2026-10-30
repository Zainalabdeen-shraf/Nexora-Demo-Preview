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

// Responsibility: Screen API, filtering, row projection, and list refresh.
public partial class NetworkUsersView
{
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
        foreach (var card in _issuanceService.Cards)
            _rows.Add(CreateDisplayRow(card, string.IsNullOrWhiteSpace(card.SerialNumber) ? "—" : card.SerialNumber));

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
    private NetworkUserDisplayRow CreateDisplayRow(GeneratedDemoCard card, string serialNumber)
    {
        // The current RouterOS connector is read-only. Never present sample IP/MAC/session
        // values or infer an online state from a locally generated card record.
        var localStatus = string.IsNullOrWhiteSpace(card.LocalStatus)
            ? "محفوظ محليًا"
            : $"محفوظ محليًا — {card.LocalStatus}";
        const string unavailable = "غير متاح — يتطلب قراءة فعلية من الراوتر";
        return new NetworkUserDisplayRow(
            card,
            serialNumber,
            localStatus,
            unavailable,
            "—",
            "غير متاح",
            "غير متاح",
            "غير متاح",
            "—",
            "—",
            "—",
            "—",
            NormalizeDateLabel(card.CreatedAtLabel),
            "غير متاح",
            "غير متاح",
            0,
            0,
            false,
            card.Password,
            NormalizeDateLabel(card.CreatedAtLabel),
            0m);
    }
    private bool MatchesRow(object item)
    {
        if (item is not NetworkUserDisplayRow row)
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

        return Contains(row.BatchId, query) || Contains(row.SerialNumber, query) ||
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
    private void RefreshRowsForActivity(string key) => SetActivityFilter(key);
}
