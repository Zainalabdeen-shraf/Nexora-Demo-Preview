using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Nexora.Demo.Desktop.Features.Cards.Views;

public partial class CardBatchesView
{
    private void RefreshDynamicFilterOptions()
    {
        var selectedPackage = SelectedTag(PackageFilterComboBox);
        var selectedPointOfSale = SelectedTag(PointOfSaleFilterComboBox);
        var selectedRouterProfile = SelectedTag(RouterProfileFilterComboBox);
        var scopedRows = _rows.Where(MatchesCurrentSystem).ToList();
        PopulateCombo(PackageFilterComboBox, "كل الباقات", scopedRows.Select(row => row.PackageName), selectedPackage);
        PopulateCombo(PointOfSaleFilterComboBox, "كل نقاط البيع", scopedRows.Select(row => row.PointOfSale), selectedPointOfSale);
        PopulateCombo(RouterProfileFilterComboBox, "كل ملفات الراوتر", scopedRows.Select(row => row.RouterProfile), selectedRouterProfile);
    }

    private static void PopulateCombo(ComboBox combo, string allLabel, IEnumerable<string> sourceValues, string selectedValue)
    {
        combo.Items.Clear();
        combo.Items.Add(new ComboBoxItem { Content = allLabel, Tag = "all" });
        foreach (var value in sourceValues
                     .Where(value => !string.IsNullOrWhiteSpace(value) && value != "—")
                     .Distinct(StringComparer.CurrentCultureIgnoreCase)
                     .OrderBy(value => value, StringComparer.CurrentCulture))
        {
            combo.Items.Add(new ComboBoxItem { Content = value, Tag = value });
        }
        SetComboTag(combo, selectedValue);
    }

    private static string SelectedTag(ComboBox combo)
    {
        if (combo.SelectedValue is not null)
        {
            var selectedValue = combo.SelectedValue.ToString();
            if (!string.IsNullOrWhiteSpace(selectedValue)) return selectedValue;
        }
        if (combo.SelectedItem is ComboBoxItem item && !string.IsNullOrWhiteSpace(item.Tag?.ToString()))
            return item.Tag!.ToString()!;
        return "all";
    }

    private static void SetComboTag(ComboBox combo, string tag)
    {
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
            string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            ?? combo.Items.OfType<ComboBoxItem>().FirstOrDefault();
    }

    private bool MatchesBatch(object item)
    {
        if (item is not BatchManagementRow row || !MatchesCurrentSystem(row)) return false;

        var query = SearchTextBox?.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(query) &&
            !Contains(row.BatchId, query) &&
            !Contains(row.PackageName, query) &&
            !Contains(row.PointOfSale, query) &&
            !Contains(row.RouterProfile, query) &&
            !Contains(row.StatusLabel, query) &&
            !row.Quantity.ToString(CultureInfo.CurrentCulture).Contains(query, StringComparison.CurrentCultureIgnoreCase) &&
            !row.Total.ToString("N0", CultureInfo.CurrentCulture).Contains(query, StringComparison.CurrentCultureIgnoreCase))
            return false;

        var status = SelectedTag(BatchStatusFilterComboBox);
        if (status != "all" && !MatchesStatusFilter(row, status)) return false;

        var package = SelectedTag(PackageFilterComboBox);
        if (package != "all" && !string.Equals(row.PackageName, package, StringComparison.OrdinalIgnoreCase)) return false;

        var createdPeriod = SelectedTag(CreatedPeriodFilterComboBox);
        var createdDate = row.CreatedAt.ToLocalTime().Date;
        var today = DateTime.Today;
        if (createdPeriod == "today" && createdDate != today) return false;
        if (createdPeriod == "7-days" && createdDate < today.AddDays(-6)) return false;
        if (createdPeriod == "30-days" && createdDate < today.AddDays(-29)) return false;

        var pointOfSale = SelectedTag(PointOfSaleFilterComboBox);
        if (pointOfSale != "all" && !string.Equals(row.PointOfSale, pointOfSale, StringComparison.OrdinalIgnoreCase)) return false;

        var routerProfile = SelectedTag(RouterProfileFilterComboBox);
        if (routerProfile != "all" && !string.Equals(row.RouterProfile, routerProfile, StringComparison.OrdinalIgnoreCase)) return false;

        return true;
    }

    private static bool MatchesStatusFilter(BatchManagementRow row, string filter) => filter switch
    {
        "waiting" => row.StatusLabel == "بانتظار المزامنة",
        "unsynced" => row.IsNotSyncedAndNotDisabledOrArchived,
        "syncing" => row.StatusLabel == "جارٍ المزامنة",
        "partial" => row.StatusLabel == "مكتملة جزئياً",
        "synced" => row.StatusLabel == "مكتملة المزامنة",
        "failed" => row.StatusLabel == "فشلت المزامنة",
        "review" => row.StatusLabel == "تحتاج مراجعة",
        "disabled" => row.StatusLabel == "معطلة",
        "disabled-archived" => row.StatusLabel == "معطلة ومؤرشفة",
        "expired-archived" => row.StatusLabel == "منتهية ومؤرشفة",
        _ => true
    };

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
        _focusedBatchId = null;
        try
        {
            SearchTextBox.Clear();
            SetComboTag(BatchStatusFilterComboBox, "all");
            SetComboTag(PackageFilterComboBox, "all");
            SetComboTag(CreatedPeriodFilterComboBox, "all");
            SetComboTag(PointOfSaleFilterComboBox, "all");
            SetComboTag(RouterProfileFilterComboBox, "all");
        }
        finally { _suppressFilters = false; }

        _view?.Refresh();
        _currentPage = 1;
        ApplyPaging();
        UpdateMetrics();
        UpdateFooter();
        SetOperationStatus("تمت إعادة الفلاتر إلى القيم الافتراضية.", isError: false);
        if (AdvancedFiltersPopup is not null) AdvancedFiltersPopup.IsOpen = false;
    }

    private void UpdateSearchPlaceholder()
    {
        if (SearchPlaceholderTextBlock is null || SearchTextBox is null) return;
        SearchPlaceholderTextBlock.Visibility = string.IsNullOrEmpty(SearchTextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void AdvancedFiltersButton_Click(object sender, RoutedEventArgs e)
    {
        if (AdvancedFiltersPopup is not null)
            AdvancedFiltersPopup.IsOpen = !AdvancedFiltersPopup.IsOpen;
    }

    private void AdvancedFiltersCloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (AdvancedFiltersPopup is not null) AdvancedFiltersPopup.IsOpen = false;
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshFromSource(preserveSelection: true);
        if (string.IsNullOrWhiteSpace(_issuanceService.StorageWarning) && string.IsNullOrWhiteSpace(_metadataStore.StorageWarning))
            SetOperationStatus("تم تحديث سجل الدفعات.", isError: false);
    }
}
