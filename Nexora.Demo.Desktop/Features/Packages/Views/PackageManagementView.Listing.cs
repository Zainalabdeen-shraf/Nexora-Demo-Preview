using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Nexora.Demo.Desktop.Features.Packages.Models;
using Nexora.Demo.Desktop.Features.Packages.Services;

namespace Nexora.Demo.Desktop.Features.Packages.Views;

// Responsibility: package catalog loading, filtering, card refresh, and binding actions.
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
                "Nexora", MessageBoxButton.OK, MessageBoxImage.Error,
                MessageBoxResult.OK, MessageBoxOptions.RtlReading);
        }
    }

    private void ApplyFilter(string? selectPackageId = null)
    {
        if (PackageCardsItemsControl is null || _catalog.Packages is null || _catalog.Bindings is null)
            return;

        var previousSelectedId = selectPackageId ?? _selectedPackageRow?.Package.Id;
        var query = SearchTextBox.Text?.Trim() ?? string.Empty;
        var filter = (StatusFilterComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "all";

        var bindingMap = _catalog.Bindings
            .Where(b => string.Equals(b.ProviderKey, _providerKey, StringComparison.OrdinalIgnoreCase))
            .GroupBy(b => b.PackageId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(x => x.IsEnabled).First(),
                StringComparer.Ordinal);

        var list = _catalog.Packages
            .Select(p => new PackageManagementRow(
                p,
                bindingMap.TryGetValue(p.Id, out var binding) ? binding : null))
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
            .Select((row, index) =>
            {
                row.Number = index + 1;
                return row;
            })
            .ToList();

        _rows.Clear();
        foreach (var row in list)
            _rows.Add(row);

        _selectedPackageRow = string.IsNullOrWhiteSpace(previousSelectedId)
            ? null
            : _rows.FirstOrDefault(row =>
                string.Equals(row.Package.Id, previousSelectedId, StringComparison.Ordinal));

        EmptyStatePanel.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyStateTextBlock.Text = query.Length > 0
            ? "لا توجد باقات تطابق عبارة البحث."
            : "لا توجد باقات مطابقة لهذه الحالة في النظام المحدد.";

        StatusTextBlock.Text = $"تم عرض {_rows.Count.ToString("N0", CultureInfo.CurrentCulture)} باقة في النظام المحدد.";
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        => ApplyFilter();

    private void StatusFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsInitialized)
            ApplyFilter();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedId = _selectedPackageRow?.Package.Id;
        RefreshPackages(selectedId);
    }

    private void RowActionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu is not ContextMenu menu)
            return;

        if (button.DataContext is PackageManagementRow row)
            _selectedPackageRow = row;

        menu.PlacementTarget = button;
        menu.DataContext = button.DataContext;
        menu.IsOpen = true;
    }

    private void ToggleBinding_Click(object sender, RoutedEventArgs e)
    {
        var row = GetPackageRowFromAction(sender) ?? _selectedPackageRow;
        if (row is null)
        {
            SetStatus("اختر باقة صالحة لتحديث الربط.", true);
            return;
        }

        _selectedPackageRow = row;
        if (row.Binding is null)
        {
            SetStatus("هذه الباقة غير مرتبطة بالنظام الحالي. افتح الباقة واحفظها لإنشاء الربط.", true);
            return;
        }

        var enable = !row.Binding.IsEnabled;
        var action = enable ? "إعادة تفعيل" : "إيقاف";
        var answer = MessageBox.Show(Window.GetWindow(this),
            $"هل تريد {action} ربط الباقة «{row.Package.Name}»؟ سيبقى التعريف التجاري محفوظًا.",
            "تأكيد الإجراء", MessageBoxButton.YesNo, MessageBoxImage.Question,
            MessageBoxResult.No, MessageBoxOptions.RtlReading);
        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            var fresh = DemoPackageStore.Load();
            var target = fresh.Bindings.FirstOrDefault(b =>
                b.Id == row.Binding.Id &&
                string.Equals(b.ProviderKey, _providerKey, StringComparison.OrdinalIgnoreCase));
            if (target is null)
                throw new InvalidOperationException("لم يعد ربط الباقة موجودًا في البيانات المحفوظة.");

            target.IsEnabled = enable;
            target.UpdatedAt = DateTimeOffset.Now;
            DemoPackageStore.Save(fresh);
            RefreshPackages(row.Package.Id);
            SetStatus(enable
                ? "تمت إعادة تفعيل ربط الباقة."
                : "تم إيقاف ربط الباقة في النظام الحالي.", false);
        }
        catch (Exception ex)
        {
            SetStatus("تعذر تحديث حالة الربط: " + ex.Message, true);
        }
    }
}
