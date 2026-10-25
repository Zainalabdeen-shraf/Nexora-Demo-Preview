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

// Responsibility: System, status, activity, batch, and text filtering.
public partial class GeneratedCardsView
{
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
}
