using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Shared;

namespace Nexora.Demo.Desktop.Features.Cards.Views;

// Responsibility: Search, filters, row sorting, and footer/selection summaries.
public partial class UserManagerCardsView
{
    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchPlaceholderTextBlock is not null)
            SearchPlaceholderTextBlock.Visibility = string.IsNullOrEmpty(SearchTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        _cardsView.Refresh();
        UpdateFooter();
    }
    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { FlowDirection = FlowDirection.RightToLeft };
        AddMenuItem(menu, "كل الحالات", (_, _) => { _statusFilter = null; _archiveFilter = ArchiveFilterMode.ActiveOnly; ApplyFilters(); });
        AddMenuItem(menu, "متاحة", (_, _) => SetStatusFilter("متاحة"));
        AddMenuItem(menu, "قيد الاستخدام", (_, _) => SetStatusFilter("قيد الاستخدام"));
        AddMenuItem(menu, "منتهية", (_, _) => SetStatusFilter("منتهية"));
        AddMenuItem(menu, "موقوفة", (_, _) => SetStatusFilter("موقوفة"));
        AddMenuItem(menu, "مفعّلة محليًا", (_, _) => SetStatusFilter("مفعّلة محليًا"));
        AddMenuItem(menu, "معطلة محليًا", (_, _) => SetStatusFilter("معطلة محليًا"));
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "عرض الأرشيف المحلي", (_, _) => { _archiveFilter = ArchiveFilterMode.ArchivedOnly; _statusFilter = null; ApplyFilters(); });
        AddMenuItem(menu, "عرض الكل بما فيه الأرشيف", (_, _) => { _archiveFilter = ArchiveFilterMode.All; _statusFilter = null; ApplyFilters(); });
        AddMenuItem(menu, "إلغاء تصفية العمود", (_, _) => { _columnFilterKey = null; _columnFilterValue = null; ApplyFilters(); });
        OpenMenu(menu, (Button)sender);
    }
    private void SetStatusFilter(string value)
    {
        _statusFilter = value;
        _archiveFilter = ArchiveFilterMode.All;
        ApplyFilters();
    }
    private void ApplyFilters()
    {
        _cardsView.Refresh();
        UpdateFooter();
    }
    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _columnFilterKey = null;
        _columnFilterValue = null;
        RebuildRows();
    }
    private void UpdateSelectionSummary()
    {
        var selected = GetSelectedRows();
        SelectionActionsPanel.Visibility = selected.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SelectionCountTextBlock.Text = $"تم تحديد {selected.Count:N0} كرت";
        SelectionDetailsButton.IsEnabled = selected.Count == 1;
    }
    private void UpdateFooter()
    {
        var visibleCount = _cardsView.Cast<ManagedCardRow>().Count();
        var totalCount = _allRows.Count(row => !row.State.IsDeleted);
        CardsCountSummaryTextBlock.Text = $"الظاهر {visibleCount:N0} من {totalCount:N0} سجل User Manager";
        var hasFilters = !string.IsNullOrWhiteSpace(SearchTextBox.Text) || _statusFilter is not null ||
                         _columnFilterKey is not null || _archiveFilter != ArchiveFilterMode.ActiveOnly;
        ClearFilterButton.Visibility = hasFilters ? Visibility.Visible : Visibility.Collapsed;
        UpdateFilterSummary();
        EmptyStateBorder.Visibility = visibleCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSelectionSummary();
    }
    private void UpdateFilterSummary()
    {
        var parts = new List<string>();
        if (_statusFilter is not null) parts.Add("الحالة: " + _statusFilter);
        if (_columnFilterKey is not null && _columnFilterValue is not null)
        {
            var title = ColumnDefinitions.FirstOrDefault(definition => definition.Key == _columnFilterKey)?.Title ?? _columnFilterKey;
            parts.Add($"{title}: {_columnFilterValue}");
        }
        if (_archiveFilter == ArchiveFilterMode.ArchivedOnly) parts.Add("الأرشيف المحلي");
        else if (_archiveFilter == ArchiveFilterMode.All) parts.Add("يشمل الأرشيف");
        CurrentFilterTextBlock.Text = string.Join(" · ", parts);
    }
    private void ClearFilterButton_Click(object sender, RoutedEventArgs e)
    {
        SearchTextBox.Clear();
        _statusFilter = null;
        _columnFilterKey = null;
        _columnFilterValue = null;
        _archiveFilter = ArchiveFilterMode.ActiveOnly;
        ApplyFilters();
    }
    private void CardsGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        if (!_definitionsByGridColumn.TryGetValue(e.Column, out var definition) || !definition.CanSort)
        {
            e.Handled = true;
            return;
        }
        var direction = e.Column.SortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending : ListSortDirection.Ascending;
        SortBy(definition, direction);
        e.Handled = true;
    }
    private void SortBy(CardColumnDefinition definition, ListSortDirection direction)
    {
        _sortKey = definition.Key;
        _sortDirection = direction;
        _cardsView.CustomSort = new ManagedCardComparer(definition, direction);
        foreach (var column in CardsGrid.Columns)
            column.SortDirection = _definitionsByGridColumn.TryGetValue(column, out var current) && current.Key == definition.Key
                ? direction : null;
        UpdateFooter();
    }
    private void FilterBySelectedValue(CardColumnDefinition definition)
    {
        var selected = GetSelectedRows();
        var row = selected.FirstOrDefault();
        if (row is null)
        {
            MessageBox.Show(Window.GetWindow(this), "حدد كرتًا أولًا، ثم افتح قائمة رأس العمود لتصفية القيمة.", "التصفية", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var value = definition.Display(row);
        if (string.IsNullOrWhiteSpace(value) || value == "—")
        {
            MessageBox.Show(Window.GetWindow(this), "لا توجد قيمة مسجلة في هذا الحقل لتطبيق التصفية.", "التصفية", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _columnFilterKey = definition.Key;
        _columnFilterValue = value;
        ApplyFilters();
    }
}
