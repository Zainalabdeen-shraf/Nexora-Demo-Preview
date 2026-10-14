using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Shared;

namespace Nexora.Demo.Desktop.Features.Cards.Views;

public partial class CardBatchesView : UserControl
{
    private readonly DemoCardIssuanceService _issuanceService;
    private readonly ListCollectionView _view;
    private bool _updatingFilters;
    private bool _isSubscribed;

    public CardBatchesView(DemoCardIssuanceService issuanceService)
    {
        ArgumentNullException.ThrowIfNull(issuanceService);
        InitializeComponent();
        _issuanceService = issuanceService;
        _view = new ListCollectionView(_issuanceService.Batches) { Filter = MatchesBatch };
        BatchesGrid.ItemsSource = _view;
        Loaded += CardBatchesView_Loaded;
        Unloaded += CardBatchesView_Unloaded;
        RefreshSummary();
    }

    public event EventHandler<FeatureNavigationRequestedEventArgs>? NavigationRequested;

    private void CardBatchesView_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_isSubscribed)
        {
            _issuanceService.Batches.CollectionChanged += Batches_CollectionChanged;
            _isSubscribed = true;
        }
        _view.Refresh();
        RefreshSummary();
    }

    private void CardBatchesView_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_isSubscribed)
        {
            _issuanceService.Batches.CollectionChanged -= Batches_CollectionChanged;
            _isSubscribed = false;
        }
    }

    public void FocusBatch(string batchId)
    {
        if (string.IsNullOrWhiteSpace(batchId)) return;
        SearchTextBox.Text = batchId;
        BatchesGrid.SelectedItem = _view.Cast<object>().OfType<GeneratedCardBatch>()
            .FirstOrDefault(batch => string.Equals(batch.BatchId, batchId, StringComparison.OrdinalIgnoreCase));
        if (BatchesGrid.SelectedItem is not null)
            BatchesGrid.ScrollIntoView(BatchesGrid.SelectedItem);
    }

    public void SetSystemFilter(string systemKey)
    {
        var desired = SystemFilterComboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == systemKey);
        if (desired is not null && !ReferenceEquals(SystemFilterComboBox.SelectedItem, desired))
        {
            _updatingFilters = true;
            try
            {
                SystemFilterComboBox.SelectedItem = desired;
            }
            finally
            {
                _updatingFilters = false;
            }
        }
        _view.Refresh();
        RefreshSummary();
    }

    private void Batches_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        Dispatcher.InvokeAsync(() => { _view.Refresh(); RefreshSummary(); });

    private bool MatchesBatch(object item)
    {
        if (item is not GeneratedCardBatch batch) return false;
        var source = (SystemFilterComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";
        if (source == "hotspot" && !batch.TargetSystem.Contains("هوتسبوت", StringComparison.OrdinalIgnoreCase)) return false;
        if (source == "user-manager" && !batch.TargetSystem.Contains("مدير المستخدمين", StringComparison.OrdinalIgnoreCase)) return false;
        var query = SearchTextBox?.Text?.Trim() ?? string.Empty;
        return query.Length == 0 || batch.BatchId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               batch.PackageName.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
               batch.TargetSystem.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
               batch.PointOfSale.Contains(query, StringComparison.CurrentCultureIgnoreCase);
    }

    private void Filter_Changed(object sender, EventArgs e)
    {
        if (_view is null || _updatingFilters) return;
        _view.Refresh();
        RefreshSummary();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _view.Refresh();
        RefreshSummary();
    }

    private void RefreshSummary()
    {
        if (BatchesGrid is null) return;
        var filtered = _view.Cast<object>().OfType<GeneratedCardBatch>().ToArray();
        BatchCountTextBlock.Text = filtered.Length.ToString("N0", CultureInfo.CurrentCulture);
        CardCountTextBlock.Text = filtered.Sum(batch => batch.Quantity).ToString("N0", CultureInfo.CurrentCulture);
        IssuanceValueTextBlock.Text = $"{filtered.Sum(batch => batch.Total):N0} ريال";
        ResultSummaryTextBlock.Text = $"عرض {filtered.Length:N0} دفعة من أصل {_issuanceService.Batches.Count:N0} · انقر مرتين على الدفعة لعرض الكروت التابعة لها.";
    }

    private void OpenSelectedBatchButton_Click(object sender, RoutedEventArgs e) => OpenSelectedBatch();
    private void BatchesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelectedBatch();

    private void OpenSelectedBatch()
    {
        if (BatchesGrid.SelectedItem is not GeneratedCardBatch batch) return;
        var systemKey = batch.TargetSystem.Contains("هوتسبوت", StringComparison.OrdinalIgnoreCase) ? "hotspot" : "user-manager";
        NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("network-users", "كل الحسابات", batch.BatchId, "batch", systemKey));
    }

    private void NewBatchButton_Click(object sender, RoutedEventArgs e) =>
        NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("cards", "إصدار الكروت"));
}
