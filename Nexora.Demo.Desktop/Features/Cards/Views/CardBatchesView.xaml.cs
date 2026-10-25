using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Shared;

namespace Nexora.Demo.Desktop.Features.Cards.Views;

/// <summary>
/// Batch-management screen coordinator. The XAML, filters, paging, status projection and
/// row operations are split into screen-local partial files so changes stay scoped here.
/// The demo issuance service remains the source of card/batch records.
/// </summary>
public partial class CardBatchesView : UserControl
{
    private readonly DemoCardIssuanceService _issuanceService;
    private readonly BatchManagementMetadataStore _metadataStore = new();
    private readonly DemoCardWorkflowStore _workflowStore = new();
    private List<BatchManagementRow> _rows = [];
    private ListCollectionView? _view;
    private readonly ObservableCollection<BatchManagementRow> _pageRows = [];
    private readonly HashSet<string> _batchOperationsInProgress = new(StringComparer.OrdinalIgnoreCase);
    private int _currentPage = 1;
    private int _pageSize = 9;
    private bool _filtersReady;
    private bool _suppressFilters;
    private bool _isSubscribed;
    private string _systemFilterKey = "all";
    private string? _sortMemberPath;
    private string? _focusedBatchId;
    private ListSortDirection _sortDirection = ListSortDirection.Descending;

    public CardBatchesView(DemoCardIssuanceService issuanceService)
    {
        ArgumentNullException.ThrowIfNull(issuanceService);
        _issuanceService = issuanceService;

        InitializeComponent();
        BatchesGrid.ItemsSource = _pageRows;
        _view = new ListCollectionView(_rows) { Filter = MatchesBatch };
        _filtersReady = true;
        RefreshFromSource(preserveSelection: false);

        Loaded += CardBatchesView_Loaded;
        Unloaded += CardBatchesView_Unloaded;
    }

    public event EventHandler<FeatureNavigationRequestedEventArgs>? NavigationRequested;

    private void CardBatchesView_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_isSubscribed)
        {
            _issuanceService.ScenarioChanged += IssuanceService_ScenarioChanged;
            _isSubscribed = true;
        }
        RefreshFromSource(preserveSelection: true);
    }

    private void CardBatchesView_Unloaded(object sender, RoutedEventArgs e)
    {
        if (!_isSubscribed) return;
        _issuanceService.ScenarioChanged -= IssuanceService_ScenarioChanged;
        _isSubscribed = false;
    }

    private void IssuanceService_ScenarioChanged(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(() => RefreshFromSource(preserveSelection: true));

    public void SetSystemFilter(string systemKey)
    {
        _systemFilterKey = string.IsNullOrWhiteSpace(systemKey) ? "all" : systemKey.Trim().ToLowerInvariant();
        if (_systemFilterKey is not ("all" or "hotspot" or "user-manager"))
            _systemFilterKey = "all";

        _suppressFilters = true;
        try { RefreshDynamicFilterOptions(); }
        finally { _suppressFilters = false; }
        _view?.Refresh();
        _currentPage = 1;
        ApplyPaging();
        UpdateMetrics();
        UpdateFooter();
    }

    public void FocusBatch(string batchId)
    {
        if (string.IsNullOrWhiteSpace(batchId)) return;
        _focusedBatchId = batchId;
        SearchTextBox.Text = batchId;
        _view?.Refresh();
        var filteredRows = _view?.Cast<object>().OfType<BatchManagementRow>().ToList() ?? [];
        var rowIndex = filteredRows.FindIndex(item =>
            string.Equals(item.BatchId, batchId, StringComparison.OrdinalIgnoreCase));
        if (rowIndex < 0) return;
        _currentPage = (rowIndex / Math.Max(1, _pageSize)) + 1;
        ApplyPaging();
        SelectVisibleBatch(batchId);
        UpdateFooter();
    }

    private void RefreshFromSource(bool preserveSelection)
    {
        try
        {
            var selectedBatchId = preserveSelection && BatchesGrid.SelectedItem is BatchManagementRow selected
                ? selected.BatchId
                : null;

            var cardsByBatch = _issuanceService.Cards
                .GroupBy(card => BatchKey(card.TargetSystem, card.BatchId), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => (IReadOnlyList<GeneratedDemoCard>)group.ToList(), StringComparer.OrdinalIgnoreCase);

            var printedCountByBatch = _workflowStore.GetActionHistory()
                .Where(action => string.Equals(action.ActionType, "PrintSubmitted", StringComparison.OrdinalIgnoreCase))
                .GroupBy(action => BatchKey(action.TargetSystem, action.BatchId), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Sum(action => Math.Max(0, action.Count)), StringComparer.OrdinalIgnoreCase);

            _rows = _issuanceService.Batches
                .OrderByDescending(batch => batch.CreatedAt)
                .Select(batch =>
                {
                    cardsByBatch.TryGetValue(BatchKey(batch.TargetSystem, batch.BatchId), out var relatedCards);
                    printedCountByBatch.TryGetValue(BatchKey(batch.TargetSystem, batch.BatchId), out var printedCount);
                    var metadata = _metadataStore.Get(batch.BatchId, batch.TargetSystem);
                    return new BatchManagementRow(batch, relatedCards ?? Array.Empty<GeneratedDemoCard>(), metadata,
                        printedCount, _metadataStore.StorageWarning is not null);
                })
                .ToList();

            _suppressFilters = true;
            try { RefreshDynamicFilterOptions(); }
            finally { _suppressFilters = false; }

            _view = new ListCollectionView(_rows) { Filter = MatchesBatch };
            if (!string.IsNullOrWhiteSpace(_sortMemberPath))
                _view.SortDescriptions.Add(new SortDescription(_sortMemberPath, _sortDirection));
            _view.Refresh();

            _currentPage = 1;
            ApplyPaging();
            UpdateMetrics();
            UpdateFooter();

            if (!string.IsNullOrWhiteSpace(selectedBatchId))
                SelectVisibleBatch(selectedBatchId);
            else if (!string.IsNullOrWhiteSpace(_focusedBatchId))
                SelectVisibleBatch(_focusedBatchId);

            if (!string.IsNullOrWhiteSpace(_issuanceService.StorageWarning))
                SetOperationStatus("تحذير في سجل الكروت: " + _issuanceService.StorageWarning, isError: true);
            else if (!string.IsNullOrWhiteSpace(_metadataStore.StorageWarning))
                SetOperationStatus("تحذير في سجل عمليات الدفعات: " + _metadataStore.StorageWarning, isError: true);
            else
                SetOperationStatus(string.Empty, isError: false);
        }
        catch (Exception ex)
        {
            SetOperationStatus("تعذر تحديث سجل الدفعات: " + ex.Message, isError: true);
            ResultSummaryTextBlock.Text = "لم يكتمل تحديث السجل.";
        }
    }

    private static string NormalizeSystemKey(string? targetSystem) =>
        targetSystem?.Contains("هوتسبوت", StringComparison.OrdinalIgnoreCase) == true ||
        targetSystem?.Contains("HotSpot", StringComparison.OrdinalIgnoreCase) == true
            ? "hotspot"
            : "user-manager";

    private static string BatchKey(string targetSystem, string batchId) =>
        NormalizeSystemKey(targetSystem) + "\u001F" + batchId.Trim();

    private bool MatchesCurrentSystem(BatchManagementRow row) =>
        _systemFilterKey == "all" || string.Equals(row.SystemKey, _systemFilterKey, StringComparison.OrdinalIgnoreCase);

    private void NewBatchButton_Click(object sender, RoutedEventArgs e) =>
        NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("cards", "إصدار الكروت"));

    private void BatchesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e) => OpenSelectedBatch();

    private void OpenSelectedBatch()
    {
        if (BatchesGrid.SelectedItem is not BatchManagementRow row) return;
        NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs(
            "network-users", "كل الحسابات", row.BatchId, "batch", row.SystemKey));
    }
}
