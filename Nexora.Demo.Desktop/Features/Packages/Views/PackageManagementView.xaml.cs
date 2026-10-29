using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Nexora.Demo.Desktop.Features.Packages.Models;
using Nexora.Demo.Desktop.Features.Packages.Services;
using System.Windows.Media;

namespace Nexora.Demo.Desktop.Features.Packages.Views;

/// <summary>Defines how the shared quick-package dialog is opened.</summary>
public enum QuickPackageCreateMode
{
    Create,
    Edit,
    Duplicate
}

public partial class PackageManagementView : UserControl
{
    private readonly string _providerKey;
    private DemoPackageCatalog _catalog = new();
    private readonly ObservableCollection<PackageManagementRow> _rows = new();
    private DemoServicePackage? _editingPackage;
    private DemoPackageBinding? _editingBinding;
    private PackageManagementRow? _selectedPackageRow;
    private bool _loadingForm;

    public PackageManagementView(string providerKey)
    {
        _providerKey = providerKey == "user-manager" ? "user-manager" : "hotspot";
        InitializeComponent();

        PackageCardsItemsControl.ItemsSource = _rows;
        SearchTextBox.TextChanged += SearchTextBox_TextChanged;
        StatusFilterComboBox.SelectionChanged += StatusFilterComboBox_SelectionChanged;

        ConfigureProviderFields();
        ClearForm();
        RefreshPackages();
        PackageTabs.SelectedItem = SavedPackagesTab;
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

        return _selectedPackageRow;
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
                : Convert.ChangeType(Guid.NewGuid().ToString("N"), type, CultureInfo.InvariantCulture);
        property.SetValue(target, value);
    }

    private void CopyRowPackage_Click(object sender, RoutedEventArgs e)
    {
        var row = GetPackageRowFromAction(sender);
        if (row == null) return;
        _selectedPackageRow = row;

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
        _selectedPackageRow = row;

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
            _selectedPackageRow = null;
            RefreshPackages();
            SetStatus("تمت الأرشفة دون تغيير ارتباطات النظام الآخر.", false);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"تعذرت أرشفة الباقة:\n{ex.Message}", "تعذر الأرشفة",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CreateNewPackageButton_Click(object sender, RoutedEventArgs e)
        => OpenQuickPackageDialog(null, QuickPackageCreateMode.Create);

    private void EditRowPackage_Click(object sender, RoutedEventArgs e)
        => EditPackageFromCardAction(sender);

    private void EditIcon_Click(object sender, RoutedEventArgs e)
        => EditPackageFromCardAction(sender);

    private void EditPackageRowIcon_Click(object sender, RoutedEventArgs e)
        => EditPackageFromCardAction(sender);

    private void DuplicatePackageCard_Click(object sender, RoutedEventArgs e)
    {
        var row = GetPackageRowFromAction(sender);
        if (row is null)
        {
            SetStatus("تعذر تحديد الباقة المراد نسخها.", true);
            return;
        }

        _selectedPackageRow = row;
        OpenQuickPackageDialog(row, QuickPackageCreateMode.Duplicate);
    }

    private void EditPackageFromCardAction(object sender)
    {
        var row = GetPackageRowFromAction(sender);
        if (row is null)
        {
            SetStatus("تعذر تحديد الباقة المطلوبة للتعديل.", true);
            return;
        }

        _selectedPackageRow = row;
        OpenQuickPackageDialog(row, QuickPackageCreateMode.Edit);
    }

    private enum PackageNoticeKind
    {
        Success,
        Info,
        Warning,
        Error
    }

    private void ShowPackageNotice(
        string title,
        string message,
        PackageNoticeKind kind)
    {
        Color badgeColor;
        Color foregroundColor;
        Color accentColor;
        Color borderColor;
        string icon;

        switch (kind)
        {
            case PackageNoticeKind.Success:
                badgeColor = Color.FromRgb(220, 252, 231);
                foregroundColor = Color.FromRgb(22, 101, 52);
                accentColor = Color.FromRgb(22, 163, 74);
                borderColor = Color.FromRgb(187, 247, 208);
                icon = "✓";
                break;

            case PackageNoticeKind.Warning:
                badgeColor = Color.FromRgb(254, 243, 199);
                foregroundColor = Color.FromRgb(146, 64, 14);
                accentColor = Color.FromRgb(217, 119, 6);
                borderColor = Color.FromRgb(253, 230, 138);
                icon = "!";
                break;

            case PackageNoticeKind.Error:
                badgeColor = Color.FromRgb(254, 226, 226);
                foregroundColor = Color.FromRgb(153, 27, 27);
                accentColor = Color.FromRgb(220, 38, 38);
                borderColor = Color.FromRgb(254, 202, 202);
                icon = "×";
                break;

            default:
                badgeColor = Color.FromRgb(219, 234, 254);
                foregroundColor = Color.FromRgb(30, 64, 175);
                accentColor = Color.FromRgb(37, 99, 235);
                borderColor = Color.FromRgb(191, 219, 254);
                icon = "i";
                break;
        }

        PackageNoticeTitleTextBlock.Text = title;
        PackageNoticeMessageTextBlock.Text = message;
        PackageNoticeIconTextBlock.Text = icon;

        PackageNoticeIconBadge.Background =
            new SolidColorBrush(badgeColor);

        PackageNoticeIconTextBlock.Foreground =
            new SolidColorBrush(foregroundColor);

        PackageNoticeTitleTextBlock.Foreground =
            new SolidColorBrush(foregroundColor);

        PackageNoticeCard.BorderBrush =
            new SolidColorBrush(borderColor);

        PackageNoticeCloseButton.Background =
            new SolidColorBrush(accentColor);

        PackageNoticeCloseButton.BorderBrush =
            new SolidColorBrush(accentColor);

        PackageNoticeOverlay.Visibility = Visibility.Visible;
        PackageNoticeCloseButton.Focus();
    }

    private void PackageNoticeCloseButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        PackageNoticeOverlay.Visibility = Visibility.Collapsed;
    }

    private void OpenQuickPackageDialog(
    PackageManagementRow? row,
    QuickPackageCreateMode mode)
    {
        var owner = Window.GetWindow(this);

        try
        {
            var dialog = new QuickPackageCreateWindow(
                _providerKey,
                row,
                mode)
            {
                Owner = owner,
                WindowStartupLocation = owner is null
                    ? WindowStartupLocation.CenterScreen
                    : WindowStartupLocation.CenterOwner
            };

            var result = dialog.ShowDialog();

            if (result == true)
            {
                RefreshPackages(dialog.SavedPackageId);

                _selectedPackageRow =
                    string.IsNullOrWhiteSpace(dialog.SavedPackageId)
                        ? null
                        : _rows.FirstOrDefault(x =>
                            string.Equals(
                                x.Package.Id,
                                dialog.SavedPackageId,
                                StringComparison.Ordinal));

                var message = mode switch
                {
                    QuickPackageCreateMode.Edit =>
                        "تم حفظ تعديلات الباقة بنجاح.",

                    QuickPackageCreateMode.Duplicate =>
                        "تم إنشاء نسخة مستقلة من الباقة بنجاح.",

                    _ => "تم إنشاء الباقة بنجاح."
                };

                SetStatus(message, false);

                ShowPackageNotice(
                    "تمت العملية بنجاح",
                    message,
                    PackageNoticeKind.Success);
            }
            else
            {
                var message = mode switch
                {
                    QuickPackageCreateMode.Edit =>
                        "تم إلغاء تعديل الباقة دون حفظ.",

                    QuickPackageCreateMode.Duplicate =>
                        "تم إلغاء إنشاء النسخة دون حفظ.",

                    _ => "تم إلغاء إنشاء الباقة دون حفظ."
                };

                SetStatus(message, false);

                ShowPackageNotice(
                    "لم يتم حفظ التغييرات",
                    message,
                    PackageNoticeKind.Info);
            }
        }
        catch (Exception ex)
        {
            var message = "تعذر فتح نافذة الباقة: " + ex.Message;

            SetStatus(message, true);

            ShowPackageNotice(
                "تعذر إكمال العملية",
                ex.Message,
                PackageNoticeKind.Error);
        }
    }

    private void ToggleRowBinding_Click(object sender, RoutedEventArgs e)
    {
        var row = GetPackageRowFromAction(sender);
        if (row is null)
        {
            SetStatus("تعذر تحديد الباقة المطلوبة.", true);
            return;
        }

        _selectedPackageRow = row;
        ToggleBinding_Click(sender, e);
    }

    private void EditSelectedPackage_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPackageRow is PackageManagementRow row)
        {
            PackageTabs.SelectedItem = CreatePackageTab;
            LoadPackageRow(row);
        }
        else
        {
            SetStatus("اختر باقة من البطاقات أولًا.", true);
        }
    }
}

public sealed class PackageManagementRow
{
    public int Number { get; set; }
    public DemoServicePackage Package { get; }
    public DemoPackageBinding? Binding { get; }
    public string Name => Package.Name;
    public string ValidityDisplay => Package.UnlimitedValidity
        ? "بلا انتهاء"
        : Package.ValidityDays > 0 ? $"{Package.ValidityDays} يوم" : "—";
    public string UptimeDisplay => Package.UptimeHours == 0 && Package.UptimeMinutes == 0
        ? "ساعات مفتوحة"
        : Package.UptimeHours >= 24 && Package.UptimeHours % 24 == 0 && Package.UptimeMinutes == 0
            ? $"{Package.UptimeHours / 24} يوم"
            : $"{Package.UptimeHours} س {Package.UptimeMinutes} د";
    public string DataQuotaDisplay => Package.UnlimitedData
        ? "بلا حد للبيانات"
        : $"{Package.DataQuotaAmount:0.##} {(Package.DataQuotaUnit == "GB" ? "جيجابايت" : "ميجابايت")}";
    public string DownloadDisplay => $"{Package.DownloadMbps:0.##} م.ب/ث";
    public string UploadDisplay => $"{Package.UploadMbps:0.##} م.ب/ث";
    public string SalePriceDisplay => $"{Package.SalePriceYer:0.##} ريال";
    public string ConcurrentUsersDisplay => Binding is null
        ? "غير مرتبط"
        : string.Equals(Binding.ProviderKey, "hotspot", StringComparison.OrdinalIgnoreCase)
            ? Binding.HotSpotSharedUsers <= 0 ? "غير محدود" : $"{Binding.HotSpotSharedUsers} جلسة"
            : string.Equals(Binding.UserManagerSharedOverride, "unlimited", StringComparison.OrdinalIgnoreCase)
                ? "غير محدود"
                : "حسب إعداد المصدر";
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
