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

public partial class PackageManagementView : UserControl
{
    private readonly string _providerKey;
    private DemoPackageCatalog _catalog = new();
    private readonly ObservableCollection<PackageManagementRow> _rows = new();
    private DemoServicePackage? _editingPackage;
    private DemoPackageBinding? _editingBinding;
    private bool _suppressSelection;
    private bool _loadingForm;

    public PackageManagementView(string providerKey)
    {
        _providerKey = providerKey == "user-manager" ? "user-manager" : "hotspot";
        InitializeComponent();
        PackagesDataGrid.ItemsSource = _rows;
        SearchTextBox.TextChanged += SearchTextBox_TextChanged;
        StatusFilterComboBox.SelectionChanged += StatusFilterComboBox_SelectionChanged;
        ConfigureProviderFields();
        ClearForm();
        RefreshPackages();
    }


private PackageManagementRow? GetPackageRowFromAction(object sender)
{
    if (sender is FrameworkElement element &&
        element.DataContext is PackageManagementRow directRow)
    {
        return directRow;
    }

    if (sender is MenuItem menuItem)
    {
        var contextMenu = menuItem.Parent as ContextMenu;
        if (contextMenu?.DataContext is PackageManagementRow contextRow)
            return contextRow;

        if (contextMenu?.PlacementTarget is FrameworkElement target &&
            target.DataContext is PackageManagementRow targetRow)
        {
            return targetRow;
        }
    }

    return PackagesDataGrid.SelectedItem as PackageManagementRow;
}

private static void SetFreshIdentifier(object target, string propertyName)
{
    var property = target.GetType().GetProperty(propertyName);
    if (property == null || !property.CanWrite)
        throw new InvalidOperationException($"تعذر إنشاء معرّف جديد للحقل {propertyName}.");

    var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
    object value = type == typeof(Guid)
        ? Guid.NewGuid()
        : type == typeof(string)
            ? Guid.NewGuid().ToString("N")
            : Convert.ChangeType(Guid.NewGuid().ToString("N"), type, System.Globalization.CultureInfo.InvariantCulture);
    property.SetValue(target, value);
}

private void CopyRowPackage_Click(object sender, RoutedEventArgs e)
{
    var row = GetPackageRowFromAction(sender);
    if (row == null) return;

    if (MessageBox.Show($"إنشاء نسخة مستقلة من الباقة «{row.Package.Name}» ضمن النظام الحالي؟",
        "نسخ الباقة", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

    try
    {
        var catalog = DemoPackageStore.Load();
        var source = catalog.Packages.FirstOrDefault(p => p.Id == row.Package.Id);
        if (source == null) throw new InvalidOperationException("لم تعد الباقة موجودة في السجل.");

        var clone = source.Copy();
        SetFreshIdentifier(clone, "Id");

        var baseName = $"{source.Name} - نسخة";
        var name = baseName;
        var suffix = 2;
        while (catalog.Packages.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            name = $"{baseName} ({suffix++})";

        clone.Name = name;
        clone.IsArchived = false;
        catalog.Packages.Add(clone);

        var sourceBinding = catalog.Bindings.FirstOrDefault(
            b => b.PackageId == source.Id && b.ProviderKey == _providerKey);

        if (sourceBinding != null)
        {
            var bindingClone = sourceBinding.Copy();
            SetFreshIdentifier(bindingClone, "Id");
            bindingClone.PackageId = clone.Id;
            bindingClone.ProviderKey = _providerKey;
            bindingClone.CreatedAt = DateTimeOffset.Now;
            bindingClone.UpdatedAt = DateTimeOffset.Now;
            catalog.Bindings.Add(bindingClone);
        }

        DemoPackageStore.Save(catalog);
        RefreshPackages(clone.Id);
        MessageBox.Show("تم نسخ تعريف الباقة وإعدادات النظام الحالي فقط.",
            "نسخ الباقة", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    catch (Exception ex)
    {
        MessageBox.Show($"تعذر نسخ الباقة:\n{ex.Message}", "تعذر النسخ",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
}


private void ArchiveRowPackage_Click(object sender, RoutedEventArgs e)
{
    var row = GetPackageRowFromAction(sender);
    if (row == null) return;

    if (MessageBox.Show($"أرشفة الباقة «{row.Package.Name}» في النظام الحالي؟",
        "أرشفة الباقة", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

    try
    {
        var catalog = DemoPackageStore.Load();
        var package = catalog.Packages.FirstOrDefault(p => p.Id == row.Package.Id);
        if (package == null) throw new InvalidOperationException("لم تعد الباقة موجودة في السجل.");

        var currentBinding = catalog.Bindings.FirstOrDefault(
            b => b.PackageId == package.Id && b.ProviderKey == _providerKey);

        var otherProviderBindings = catalog.Bindings.Where(
            b => b.PackageId == package.Id && b.ProviderKey != _providerKey).ToList();

        if (otherProviderBindings.Count > 0)
        {
            if (currentBinding == null)
                throw new InvalidOperationException(
                    "هذه الباقة غير مرتبطة بالنظام الحالي. لم تُؤرشف حتى لا يتأثر النظام الآخر.");

            var archivedCopy = package.Copy();
            SetFreshIdentifier(archivedCopy, "Id");

            var baseName = $"{package.Name} - مؤرشفة";
            var archivedName = baseName;
            var suffix = 2;
            while (catalog.Packages.Any(p => string.Equals(
                p.Name, archivedName, StringComparison.OrdinalIgnoreCase)))
                archivedName = $"{baseName} ({suffix++})";

            archivedCopy.Name = archivedName;
            archivedCopy.IsArchived = true;
            archivedCopy.CreatedAt = DateTimeOffset.Now;
            archivedCopy.UpdatedAt = DateTimeOffset.Now;
            catalog.Packages.Add(archivedCopy);

            currentBinding.PackageId = archivedCopy.Id;
            currentBinding.IsEnabled = false;
            currentBinding.UpdatedAt = DateTimeOffset.Now;
        }
        else
        {
            package.IsArchived = true;
            package.UpdatedAt = DateTimeOffset.Now;
            if (currentBinding != null)
            {
                currentBinding.IsEnabled = false;
                currentBinding.UpdatedAt = DateTimeOffset.Now;
            }
        }

        DemoPackageStore.Save(catalog);
        RefreshPackages();
        SetStatus("تمت الأرشفة دون تغيير ارتباطات النظام الآخر.", false);
    }
    catch (Exception ex)
    {
        MessageBox.Show($"تعذرت أرشفة الباقة:\n{ex.Message}", "تعذر الأرشفة",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

private void EditRowPackage_Click(object sender, RoutedEventArgs e)
{
    var row = GetPackageRowFromAction(sender);
    if (row is null)
    {
        SetStatus("اختر باقة صالحة لتعديلها.", true);
        return;
    }

    PackagesDataGrid.SelectedItem = row;
    PackagesDataGrid.ScrollIntoView(row);
    PackageTabs.SelectedItem = CreatePackageTab;
    LoadPackageRow(row);
}

private void ToggleRowBinding_Click(object sender, RoutedEventArgs e)
{
    var row = GetPackageRowFromAction(sender);
    if (row is null)
    {
        SetStatus("تعذر تحديد الباقة المطلوبة.", true);
        return;
    }

    PackagesDataGrid.SelectedItem = row;
    ToggleBinding_Click(sender, e);
}
private void EditIcon_Click(object sender, RoutedEventArgs e)
{
    var row = GetPackageRowFromAction(sender);
    if (row == null)
    {
        MessageBox.Show("تعذر تحديد الباقة من صف الإجراءات.", "تعديل الباقة",
            MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
    }

    PackagesDataGrid.SelectedItem = row;
    PackagesDataGrid.ScrollIntoView(row);
    EditSelectedPackage_Click(sender, e);
}
private void EditPackageRowIcon_Click(object sender, RoutedEventArgs e)
{
    if (sender is not FrameworkElement element ||
        element.DataContext is not PackageManagementRow row)
    {
        SetStatus("تعذر تحديد الباقة من صف الإجراءات.", true);
        return;
    }

    PackagesDataGrid.SelectedItem = row;
    PackagesDataGrid.CurrentItem = row;
    PackagesDataGrid.ScrollIntoView(row);

    PackageTabs.SelectedItem = CreatePackageTab;
    LoadPackageRow(row);
}
private void EditSelectedPackage_Click(object sender, RoutedEventArgs e)
{
    if (PackagesDataGrid.SelectedItem is PackageManagementRow row)
    {
        PackageTabs.SelectedItem = CreatePackageTab;
        LoadPackageRow(row);
    }
    else SetStatus("اختر باقة من الجدول أولًا.", true);
}


}

public sealed class PackageManagementRow
{
    public int Number { get; set; }
    public DemoServicePackage Package { get; }
    public DemoPackageBinding? Binding { get; }
    public string Name => Package.Name;
    public string ValidityDisplay => Package.ValidityDays > 0 ? $"{Package.ValidityDays} يوم" : "—";
    public string UptimeDisplay => Package.UptimeHours == 0 && Package.UptimeMinutes == 0
        ? "ساعات مفتوحة"
        : Package.UptimeHours >= 24 && Package.UptimeHours % 24 == 0 && Package.UptimeMinutes == 0
            ? $"{Package.UptimeHours / 24} يوم"
            : $"{Package.UptimeHours} س {Package.UptimeMinutes} د";
    public string DataQuotaDisplay => Package.UnlimitedData ? "بلا حد للبيانات" : $"{Package.DataQuotaAmount:0.##} {(Package.DataQuotaUnit == "GB" ? "جيجابايت" : "ميجابايت")}";
    public string DownloadDisplay => $"{Package.DownloadMbps:0.##} م.ب/ث";
    public string UploadDisplay => $"{Package.UploadMbps:0.##} م.ب/ث";
    public string SalePriceDisplay => $"{Package.SalePriceYer:0.##} ريال";
    public string ProviderDisplay => Binding is null ? "غير مرتبطة بهذا النظام" : Binding.ProviderKey == "hotspot" ? "هوتسبوت" : "يوزر منجر";
    public string TechnicalTargetDisplay => Binding is null ? string.Empty : Binding.ProviderKey == "hotspot"
        ? $"{Binding.HotSpotProfile} {Binding.HotSpotServer}"
        : $"{Binding.UserManagerProfile} {Binding.UserManagerGroup} {Binding.UserManagerLimitation}";
    public string StatusDisplay => Package.IsArchived ? "مؤرشفة" : Binding is null ? "غير مرتبطة" : Binding.IsEnabled ? "نشطة" : "متوقفة";
    public string CreatedAtDisplay => Package.CreatedAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);

    public PackageManagementRow(DemoServicePackage package, DemoPackageBinding? binding)
    {
        Package = package;
        Binding = binding;
    }
}
