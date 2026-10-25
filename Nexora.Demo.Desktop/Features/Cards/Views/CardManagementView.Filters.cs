using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Shared;

namespace Nexora.Demo.Desktop.Features.Cards.Views;

// Responsibility: Source refresh, search, advanced filters, and filter option construction.
public partial class CardManagementView
{
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
            if (!string.IsNullOrWhiteSpace(_sortMemberPath))
                _view.SortDescriptions.Add(new SortDescription(_sortMemberPath, _sortDirection));
            _view.Refresh();
            ApplyPaging();
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
                OperationStatusTextBlock.Visibility = Visibility.Visible;
            }
            else
            {
                OperationStatusTextBlock.Text = string.Empty;
                OperationStatusTextBlock.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception)
        {
            OperationStatusTextBlock.Text = "تعذر تحديث سجل الكروت. حاول التحديث مرة أخرى.";
            OperationStatusTextBlock.Foreground = (Brush)FindResource("DangerBrush");
            OperationStatusTextBlock.Visibility = Visibility.Visible;
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
    private static string SelectedTag(ComboBox combo)
    {
        var selectedValue = combo.SelectedValue?.ToString();
        if (!string.IsNullOrWhiteSpace(selectedValue)) return selectedValue;
        if (combo.SelectedItem is ComboBoxItem item)
        {
            var tag = item.Tag?.ToString();
            if (!string.IsNullOrWhiteSpace(tag)) return tag;
            var content = item.Content?.ToString();
            if (!string.IsNullOrWhiteSpace(content)) return content;
        }
        return "all";
    }
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

        var createdPeriod = SelectedTag(CreatedPeriodFilterComboBox);
        var createdDate = card.CreatedAt.ToLocalTime().Date;
        var today = DateTime.Today;
        if (createdPeriod == "today" && createdDate != today) return false;
        if (createdPeriod == "7-days" && createdDate < today.AddDays(-6)) return false;
        if (createdPeriod == "30-days" && createdDate < today.AddDays(-29)) return false;

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
        UpdateSearchPlaceholder();
        if (!_filtersReady || _suppressFilters || _view is null) return;
        _view.Refresh();
        _currentPage = 1;
        ApplyPaging();
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
            SetComboTag(CreatedPeriodFilterComboBox, "all");
            SetComboTag(SourceFilterComboBox, "all");
            SetComboTag(PrintFilterComboBox, "all");
        }
        finally
        {
            _suppressFilters = false;
        }
        _view.Refresh();
        _currentPage = 1;
        ApplyPaging();
        UpdateFooter();
        SetOperationStatus("تمت إعادة الفلاتر إلى القيم الافتراضية.", false);
        if (AdvancedFiltersPopup is not null) AdvancedFiltersPopup.IsOpen = false;
    }
    private void UpdateSearchPlaceholder()
    {
        if (CardSearchPlaceholderTextBlock is null || CardSearchTextBox is null) return;
        CardSearchPlaceholderTextBlock.Visibility = string.IsNullOrEmpty(CardSearchTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }
    private void SearchButton_Click(object sender, RoutedEventArgs e) => Filter_Changed(sender, e);
    private void AdvancedFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        if (AdvancedFiltersPopup is not null)
            AdvancedFiltersPopup.IsOpen = !AdvancedFiltersPopup.IsOpen;
    }
    private void AdvancedFiltersCloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (AdvancedFiltersPopup is not null) AdvancedFiltersPopup.IsOpen = false;
    }
    private void CardsDataGrid_Loaded(object sender, RoutedEventArgs e) => RebuildPageSizeOptions();
    private void CardsDataGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsLoaded) return;
        Dispatcher.BeginInvoke(new Action(RebuildPageSizeOptions));
    }
    private void RebuildPageSizeOptions()
    {
        if (CardsDataGrid is null || PageSizeComboBox is null || CardsDataGrid.ActualHeight < 40) return;
        var rowHeight = CardsDataGrid.RowHeight > 0 ? CardsDataGrid.RowHeight : 36;
        var headerHeight = CardsDataGrid.ColumnHeaderHeight > 0 ? CardsDataGrid.ColumnHeaderHeight : 36;
        var visibleCapacity = Math.Max(1, (int)Math.Floor((CardsDataGrid.ActualHeight - headerHeight - 2) / rowHeight));
        visibleCapacity = Math.Min(50, visibleCapacity);
        if (visibleCapacity == _lastVisibleRowCapacity && PageSizeComboBox.Items.Count > 0) return;
        _lastVisibleRowCapacity = visibleCapacity;

        var previousSize = _pageSize;
        _pageSize = Math.Clamp(_pageSize, 1, visibleCapacity);
        var choices = new SortedSet<int> { _pageSize, visibleCapacity };
        foreach (var choice in new[] { 5, 7, 10, 20, 50 })
            if (choice <= visibleCapacity) choices.Add(choice);

        _updatingPageSizeOptions = true;
        try
        {
            PageSizeComboBox.Items.Clear();
            foreach (var size in choices)
                PageSizeComboBox.Items.Add(new ComboBoxItem { Content = size.ToString(CultureInfo.CurrentCulture), Tag = size.ToString(CultureInfo.InvariantCulture) });
            PageSizeComboBox.SelectedItem = PageSizeComboBox.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), _pageSize.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
        }
        finally
        {
            _updatingPageSizeOptions = false;
        }

        if (previousSize != _pageSize)
        {
            _currentPage = 1;
            ApplyPaging();
            UpdateFooter();
        }
    }
    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshFromSource(preserveSelection: true);
        SetOperationStatus("تم تحديث السجل.", false);
    }
}
