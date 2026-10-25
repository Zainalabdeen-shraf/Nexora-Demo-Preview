using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Data;

namespace Nexora.Demo.Desktop.Features.Cards.Views;

public partial class CardBatchesView
{
    private void UpdateMetrics()
    {
        var scoped = _rows.Where(MatchesCurrentSystem).ToList();
        BatchCountTextBlock.Text = scoped.Count.ToString("N0", CultureInfo.CurrentCulture);
        // Quantity is the batch's own issuance quantity, not a count inferred from linked account records.
        CardCountTextBlock.Text = scoped.Sum(row => row.Quantity).ToString("N0", CultureInfo.CurrentCulture);
        IssuanceValueTextBlock.Text = $"{scoped.Sum(row => row.Total):N0} ريال";
        PendingBatchesCountTextBlock.Text = scoped.Count(row => row.StatusLabel == "بانتظار المزامنة").ToString("N0", CultureInfo.CurrentCulture);
    }

    private int FilteredCount => _view?.Cast<object>().OfType<BatchManagementRow>().Count() ?? 0;
    private int PageCount => Math.Max(1, (FilteredCount + _pageSize - 1) / _pageSize);

    private void ApplyPaging()
    {
        if (_view is null || PagerButtonsPanel is null) return;
        var filtered = _view.Cast<object>().OfType<BatchManagementRow>().ToList();
        var pageCount = Math.Max(1, (filtered.Count + _pageSize - 1) / _pageSize);
        _currentPage = Math.Clamp(_currentPage, 1, pageCount);
        var pageRows = filtered.Skip((_currentPage - 1) * _pageSize).Take(_pageSize).ToList();
        _pageRows.Clear();
        foreach (var row in pageRows) _pageRows.Add(row);
        BuildPagerButtons(pageCount);
    }

    private void BuildPagerButtons(int pageCount)
    {
        PagerButtonsPanel.Children.Clear();
        AddPagerButton("«", "first", _currentPage > 1, false, "الصفحة الأولى");
        AddPagerButton("‹", "previous", _currentPage > 1, false, "الصفحة السابقة");
        var pages = new SortedSet<int> { 1, pageCount };
        for (var page = Math.Max(1, _currentPage - 1); page <= Math.Min(pageCount, _currentPage + 1); page++) pages.Add(page);
        var previousPage = 0;
        foreach (var page in pages)
        {
            if (previousPage > 0 && page - previousPage > 1)
                PagerButtonsPanel.Children.Add(new TextBlock
                {
                    Text = "…",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(5, 0, 5, 0),
                    Foreground = (Brush)FindResource("MutedBrush")
                });
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
            Style = (Style)FindResource(current ? "ManagementPagerButtonCurrent" : "ManagementPagerButton"),
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
        if (sender is not Button button || button.Tag is not string tag) return;
        var pageCount = PageCount;
        if (tag == "first") _currentPage = 1;
        else if (tag == "previous") _currentPage = Math.Max(1, _currentPage - 1);
        else if (tag == "next") _currentPage = Math.Min(pageCount, _currentPage + 1);
        else if (tag == "last") _currentPage = pageCount;
        else if (int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out var page))
            _currentPage = Math.Clamp(page, 1, pageCount);
        ApplyPaging();
        UpdateFooter();
    }

    private void PageSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_filtersReady || _view is null || PageSizeComboBox.SelectedItem is not ComboBoxItem item) return;
        if (!int.TryParse(item.Tag?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var size)) return;
        if (size is not (9 or 18 or 27 or 45 or 90) || size == _pageSize) return;
        _pageSize = size;
        _currentPage = 1;
        ApplyPaging();
        UpdateFooter();
    }

    private void BatchesGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        if (_view is null || string.IsNullOrWhiteSpace(e.Column.SortMemberPath)) return;
        e.Handled = true;
        var member = e.Column.SortMemberPath;
        _sortDirection = string.Equals(_sortMemberPath, member, StringComparison.Ordinal) &&
                         _sortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;
        _sortMemberPath = member;
        _view.SortDescriptions.Clear();
        _view.SortDescriptions.Add(new SortDescription(member, _sortDirection));
        _view.Refresh();
        foreach (var column in BatchesGrid.Columns)
            column.SortDirection = ReferenceEquals(column, e.Column) ? _sortDirection : null;
        _currentPage = 1;
        ApplyPaging();
        UpdateFooter();
    }

    private void UpdateFooter()
    {
        if (ResultSummaryTextBlock is null || BatchesGrid is null) return;
        var visibleCount = FilteredCount;
        var start = visibleCount == 0 ? 0 : ((_currentPage - 1) * _pageSize) + 1;
        var end = Math.Min(_currentPage * _pageSize, visibleCount);
        ResultSummaryTextBlock.Text = visibleCount == 0
            ? "لا توجد نتائج مطابقة"
            : $"عرض {start:N0}–{end:N0} من {visibleCount:N0} دفعة";
        EmptyStatePanel.Visibility = visibleCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyStateDescriptionTextBlock.Text = _rows.Count == 0 || !_rows.Any(MatchesCurrentSystem)
            ? "لا توجد دفعات مسجلة لهذا النظام حالياً."
            : "لا توجد دفعات تطابق البحث والفلاتر الحالية.";
        BatchesGrid.Visibility = visibleCount == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SetOperationStatus(string message, bool isError)
    {
        OperationStatusTextBlock.Text = message;
        OperationStatusTextBlock.Foreground = (Brush)FindResource(isError ? "DangerBrush" : "MutedBrush");
        OperationStatusTextBlock.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SelectVisibleBatch(string batchId)
    {
        var row = _pageRows.FirstOrDefault(item =>
            string.Equals(item.BatchId, batchId, StringComparison.OrdinalIgnoreCase));
        if (row is null) return;
        BatchesGrid.SelectedItem = row;
        BatchesGrid.ScrollIntoView(row);
    }
}
