using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Nexora.Demo.Desktop.Features.Packages.Models;
using Nexora.Demo.Desktop.Features.Packages.Services;

namespace Nexora.Demo.Desktop.Features.Packages.Views;

// Responsibility: Package catalog loading, filtering, selection, and binding actions.
public partial class PackageManagementView
{
    private void ConfigureProviderFields()
    {
        var hotspot = _providerKey == "hotspot";
        HotSpotSourcePanel.Visibility = hotspot ? Visibility.Visible : Visibility.Collapsed;
        UserManagerSourcePanel.Visibility = hotspot ? Visibility.Collapsed : Visibility.Visible;
        HotSpotServerPanel.Visibility = hotspot ? Visibility.Visible : Visibility.Collapsed;
        UserManagerStartsWhenPanel.Visibility = hotspot ? Visibility.Collapsed : Visibility.Visible;
        HotSpotSharedUsersComboBox.Visibility = hotspot ? Visibility.Visible : Visibility.Collapsed;
        UserManagerSharedOverrideComboBox.Visibility = hotspot ? Visibility.Collapsed : Visibility.Visible;
        ConcurrentUsersLabel.Text = hotspot ? "عدد المستخدمين المتزامنين" : "تجاوز عدد الجلسات";
        SourceSettingsTitleTextBlock.Text = hotspot ? "إعدادات المصدر (هوتسبوت)" : "إعدادات المصدر (يوزر منجر)";
        SourceSettingsIconTextBlock.Text = hotspot ? "⌁" : "⚙";
    }
    private void RefreshPackages(string? selectPackageId = null)
    {
        try
        {
            _catalog = DemoPackageStore.Load();
            ApplyFilter(selectPackageId);
        }
        catch (Exception ex)
        {
            _rows.Clear();
            EmptyStatePanel.Visibility = Visibility.Visible;
            EmptyStateTextBlock.Text = "تعذر تحميل الباقات المحفوظة. لم يتم تغيير بياناتها.";
            StatusTextBlock.Text = "تعذر تحميل بيانات الباقات.";
            MessageBox.Show(Window.GetWindow(this), "تعذر تحميل بيانات الباقات:\n" + ex.Message,
                "TabCard", MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
        }
    }
    private void ApplyFilter(string? selectPackageId = null)
    {
        if (PackagesDataGrid is null || _catalog.Packages is null || _catalog.Bindings is null) return;
        var query = SearchTextBox.Text?.Trim() ?? string.Empty;
        var filter = (StatusFilterComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";
        var bindingMap = _catalog.Bindings
            .Where(b => b.ProviderKey == _providerKey)
            .GroupBy(b => b.PackageId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.IsEnabled).First(), StringComparer.Ordinal);

        var list = _catalog.Packages
            .Select(p => new PackageManagementRow(p, bindingMap.TryGetValue(p.Id, out var binding) ? binding : null))
            .Where(row => filter switch
            {
                "active" => row.Binding is not null && row.Binding.IsEnabled && !row.Package.IsArchived,
                "paused" => row.Binding is not null && !row.Binding.IsEnabled && !row.Package.IsArchived,
                "unlinked" => row.Binding is null && !row.Package.IsArchived,
                "archived" => row.Package.IsArchived,
                _ => true
            })
            .Where(row => query.Length == 0 ||
                row.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                row.TechnicalTargetDisplay.Contains(query, StringComparison.CurrentCultureIgnoreCase))
            .OrderByDescending(row => row.Package.CreatedAt)
            .Select((row, index) => { row.Number = index + 1; return row; })
            .ToList();

        _suppressSelection = true;
        _rows.Clear();
        foreach (var row in list) _rows.Add(row);
        EmptyStatePanel.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyStateTextBlock.Text = query.Length > 0
            ? "لا توجد باقات تطابق عبارة البحث."
            : "لا توجد باقات مطابقة لهذه الحالة في النظام المحدد.";

        if (!string.IsNullOrWhiteSpace(selectPackageId))
            PackagesDataGrid.SelectedItem = _rows.FirstOrDefault(x => x.Package.Id == selectPackageId);
        _suppressSelection = false;

        StatusTextBlock.Text = $"تم عرض {_rows.Count:N0} باقة في النظام المحدد.";
    }
    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void StatusFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsInitialized) ApplyFilter();
    }
    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedId = (PackagesDataGrid.SelectedItem as PackageManagementRow)?.Package.Id;
        RefreshPackages(selectedId);
    }
    private void PackagesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection || PackagesDataGrid.SelectedItem is not PackageManagementRow row) return;
        SetStatus($"تم تحديد الباقة «{row.Package.Name}». استخدم أيقونة التعديل لفتحها.", false);
    }
    private void PackagesDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (PackagesDataGrid.SelectedItem is PackageManagementRow)
            SetStatus("لتعديل الباقة، استخدم أيقونة التعديل في صف الإجراءات.", false);
    }
    private void RowActionsButton_Click(object sender, RoutedEventArgs e)
{
    if (sender is Button button && button.ContextMenu is ContextMenu menu)
    {
        menu.PlacementTarget = button;
        menu.DataContext = button.DataContext;
        menu.IsOpen = true;
    }
}
    private void ToggleBinding_Click(object sender, RoutedEventArgs e)
    {
        if (PackagesDataGrid.SelectedItem is not PackageManagementRow row) return;
        if (row.Binding is null)
        {
            SetStatus("هذه الباقة غير مرتبطة بالنظام الحالي. اخترها ثم احفظها لإنشاء الربط.", true);
            return;
        }
        var enable = !row.Binding.IsEnabled;
        var action = enable ? "إعادة تفعيل" : "إيقاف";
        var answer = MessageBox.Show(Window.GetWindow(this),
            $"هل تريد {action} ربط الباقة «{row.Package.Name}»؟ سيبقى التعريف التجاري محفوظًا.",
            "تأكيد الإجراء", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No, MessageBoxOptions.RtlReading);
        if (answer != MessageBoxResult.Yes) return;
        try
        {
            var fresh = DemoPackageStore.Load();
            var target = fresh.Bindings.FirstOrDefault(b => b.Id == row.Binding.Id && b.ProviderKey == _providerKey);
            if (target is null) throw new InvalidOperationException("لم يعد ربط الباقة موجودًا في البيانات المحفوظة.");
            target.IsEnabled = enable;
            target.UpdatedAt = DateTimeOffset.Now;
            DemoPackageStore.Save(fresh);
            RefreshPackages(row.Package.Id);
            SetStatus(enable ? "تمت إعادة تفعيل ربط الباقة." : "تم إيقاف ربط الباقة في النظام الحالي.", false);
        }
        catch (Exception ex) { SetStatus("تعذر تحديث حالة الربط: " + ex.Message, true); }
    }
}
