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

// Responsibility: Selection metrics, page-size adaptation, sorting, and pager controls.
public partial class CardManagementView
{
    private void SelectVisibleButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _pageRows)
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
        TotalCardsCountTextBlock.Text = _rows.Count.ToString("N0", CultureInfo.CurrentCulture);
        AvailableCardsCountTextBlock.Text = _rows.Count(row => !row.Card.IsArchived && string.Equals(row.Card.DemoStatus, "متاحة", StringComparison.Ordinal)).ToString("N0", CultureInfo.CurrentCulture);
        InUseCardsCountTextBlock.Text = _rows.Count(row => !row.Card.IsArchived && string.Equals(row.Card.DemoStatus, "قيد الاستخدام", StringComparison.Ordinal)).ToString("N0", CultureInfo.CurrentCulture);
        ExpiredCardsCountTextBlock.Text = _rows.Count(row => !row.Card.IsArchived && string.Equals(row.Card.DemoStatus, "منتهية", StringComparison.Ordinal)).ToString("N0", CultureInfo.CurrentCulture);
        ReviewCardsCountTextBlock.Text = _rows.Count(row => row.NeedsReview).ToString("N0", CultureInfo.CurrentCulture);
    }
    private void ApplyPaging()
    {
        if (_view is null || PagerButtonsPanel is null) return;
        var filtered = _view.Cast<object>().OfType<CardManagementRow>().ToList();
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
                PagerButtonsPanel.Children.Add(new TextBlock { Text = "…", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5, 0, 5, 0), Foreground = (Brush)FindResource("MutedBrush") });
            AddPagerButton(page.ToString(CultureInfo.CurrentCulture), page.ToString(CultureInfo.InvariantCulture), true, page == _currentPage, $"الصفحة {page}");
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
            Height = 32,
            MinWidth = 34,
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
        else if (int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out var page)) _currentPage = Math.Clamp(page, 1, pageCount);
        ApplyPaging();
        UpdateFooter();
    }
    private void PageSizeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingPageSizeOptions) return;
        if (!_filtersReady || _view is null || PageSizeComboBox.SelectedItem is not ComboBoxItem item) return;
        if (!int.TryParse(item.Tag?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var size)) return;
        if (size == _pageSize) return;
        _pageSize = size;
        _currentPage = 1;
        ApplyPaging();
        UpdateFooter();
    }
    private void CardsDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        if (_view is null || string.IsNullOrWhiteSpace(e.Column.SortMemberPath)) return;
        e.Handled = true;
        var member = e.Column.SortMemberPath;
        _sortDirection = string.Equals(_sortMemberPath, member, StringComparison.Ordinal)
            && _sortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending
            : ListSortDirection.Ascending;
        _sortMemberPath = member;
        _view.SortDescriptions.Clear();
        _view.SortDescriptions.Add(new SortDescription(member, _sortDirection));
        _view.Refresh();
        foreach (var column in CardsDataGrid.Columns)
            column.SortDirection = ReferenceEquals(column, e.Column) ? _sortDirection : null;
        _currentPage = 1;
        ApplyPaging();
        UpdateFooter();
    }
    private void UpdateFooter()
    {
        if (ResultSummaryTextBlock is null || CardsDataGrid is null) return;
        var visibleCount = FilteredCount;
        var start = visibleCount == 0 ? 0 : ((_currentPage - 1) * _pageSize) + 1;
        var end = Math.Min(_currentPage * _pageSize, visibleCount);
        ResultSummaryTextBlock.Text = visibleCount == 0
            ? "لا توجد نتائج مطابقة"
            : $"عرض {start:N0}–{end:N0} من {visibleCount:N0} كرت";
        EmptyStatePanel.Visibility = visibleCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyStateDescriptionTextBlock.Text = _rows.Count == 0
            ? "لا توجد كروت مسجلة لهذا النظام."
            : "لا توجد سجلات تطابق البحث والفلاتر الحالية.";
        CardsDataGrid.Visibility = visibleCount == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
}
