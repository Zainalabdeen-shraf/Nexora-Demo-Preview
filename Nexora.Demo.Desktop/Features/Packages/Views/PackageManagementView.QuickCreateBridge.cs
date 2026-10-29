using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Nexora.Demo.Desktop.Features.Packages.Models;
using Nexora.Demo.Desktop.Features.Packages.Services;

namespace Nexora.Demo.Desktop.Features.Packages.Views;

public partial class PackageManagementView
{
    private static readonly string[] QuickCreateControlNames =
    {
        "PackageNameTextBox",
        "DataQuotaAmountTextBox", "DataQuotaUnitComboBox", "UnlimitedDataCheckBox",
        "SalePriceTextBox", "CardDisplayPriceTextBox", "CommissionModeComboBox", "CommissionValueTextBox", "CommissionEnabledCheckBox",
        "ValidityDaysTextBox", "UptimeHoursTextBox", "UptimeMinutesTextBox", "UnlimitedUptimeCheckBox",
        "DownloadMbpsTextBox", "UploadMbpsTextBox", "HotSpotSharedUsersComboBox", "UserManagerSharedOverrideComboBox",
        "HotSpotProfileComboBox", "HotSpotServerComboBox", "PreserveUsageOnRouterInterruptionCheckBox",
        "PreserveTimeOnRouterInterruptionCheckBox", "RecordHotSpotSessionsCheckBox",
        "UserManagerProfileComboBox", "UserManagerGroupComboBox", "UserManagerLimitationComboBox", "UserManagerStartsWhenComboBox",
        "DaysPolicyComboBox", "SaturdayCheckBox", "SundayCheckBox", "MondayCheckBox", "TuesdayCheckBox",
        "WednesdayCheckBox", "ThursdayCheckBox", "FridayCheckBox", "PeriodPolicyComboBox",
        "AccessFromTextBox", "AccessUntilTextBox"
    };

    /// <summary>
    /// Loads the existing form state into the compact dialog without introducing a second
    /// persistence implementation. The existing PackageManagementView editor remains the
    /// single place where values are validated and saved.
    /// </summary>
    public void PrepareQuickPackageDialog(
        FrameworkElement quickForm,
        PackageManagementRow? sourceRow,
        QuickPackageCreateMode mode)
    {
        ArgumentNullException.ThrowIfNull(quickForm);

        if (mode == QuickPackageCreateMode.Create)
        {
            ClearForm();
            CopyQuickCreateControls(this, quickForm);
            return;
        }

        if (sourceRow is null)
            throw new ArgumentException("يجب تحديد الباقة في وضع التعديل أو النسخ.", nameof(sourceRow));

        _catalog = DemoPackageStore.Load();
        var package = _catalog.Packages.FirstOrDefault(p =>
            string.Equals(p.Id, sourceRow.Package.Id, StringComparison.Ordinal));

        if (package is null)
            throw new InvalidOperationException("لم تعد الباقة المحددة موجودة في البيانات المحفوظة. حدّث قائمة الباقات ثم أعد المحاولة.");

        var binding = _catalog.Bindings.FirstOrDefault(b =>
            string.Equals(b.PackageId, package.Id, StringComparison.Ordinal) &&
            string.Equals(b.ProviderKey, _providerKey, StringComparison.OrdinalIgnoreCase));

        var freshRow = new PackageManagementRow(package, binding);
        LoadPackageRow(freshRow);

        if (mode == QuickPackageCreateMode.Duplicate)
        {
            // A duplicate is a new package definition. Keep the selected binding's settings
            // as initial values, but clear the edit identity so the existing save path creates
            // a fresh package and binding rather than modifying the source.
            _editingPackage = null;
            _editingBinding = binding?.Copy() ?? new DemoPackageBinding
            {
                ProviderKey = _providerKey,
                PackageId = package.Id
            };
            PackageNameTextBox.Text = CreateUniqueCopyName(package.Name, _catalog.Packages);
        }

        CopyQuickCreateControls(this, quickForm);
    }

    /// <summary>
    /// Copies dialog values into the existing form and calls its normal validation/save path.
    /// Returns a success flag and the resulting package ID so the card list can select the saved row.
    /// </summary>
    public string SaveQuickPackageDialog(
        FrameworkElement quickForm,
        PackageManagementRow? sourceRow,
        QuickPackageCreateMode mode,
        bool bindingEnabled,
        out bool saved,
        out string? savedPackageId)
    {
        ArgumentNullException.ThrowIfNull(quickForm);
        saved = false;
        savedPackageId = null;

        var before = DemoPackageStore.Load();
        var beforeIds = before.Packages
            .Select(p => p.Id ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var sourceId = sourceRow?.Package.Id;

        // The hidden host supplies the established validation and persistence path.
        if (mode == QuickPackageCreateMode.Create)
            ClearForm();
        else if (mode == QuickPackageCreateMode.Duplicate)
            _editingPackage = null;

        CopyQuickCreateControls(quickForm, this);
        SavePackage(addAnother: false);

        var message = StatusTextBlock?.Text ?? string.Empty;
        if (!message.StartsWith("تم حفظ", StringComparison.CurrentCulture))
        {
            return string.IsNullOrWhiteSpace(message)
                ? "لم تُحفظ الباقة. راجع الحقول المطلوبة وحاول مرة أخرى."
                : message;
        }

        var after = DemoPackageStore.Load();
        var createdPackage = after.Packages
            .Where(p => !beforeIds.Contains(p.Id ?? string.Empty))
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefault();

        savedPackageId = createdPackage?.Id ??
            (mode == QuickPackageCreateMode.Edit ? sourceId : null);
        saved = true;

        // Enabled/paused belongs to this provider binding, not the shared commercial package.
        // Apply it only after the established save path succeeds.
        var packageIdForBinding = savedPackageId;
        if (mode == QuickPackageCreateMode.Edit && !string.IsNullOrWhiteSpace(packageIdForBinding))
        {
            var targetBinding = after.Bindings.FirstOrDefault(b =>
                string.Equals(b.PackageId, packageIdForBinding, StringComparison.Ordinal) &&
                string.Equals(b.ProviderKey, _providerKey, StringComparison.OrdinalIgnoreCase));

            if (targetBinding is null)
            {
                RefreshPackages(savedPackageId);
                return "تم حفظ بيانات الباقة، لكن تعذر العثور على ربط النظام الحالي لتحديث حالته.";
            }

            if (targetBinding.IsEnabled != bindingEnabled)
            {
                targetBinding.IsEnabled = bindingEnabled;
                targetBinding.UpdatedAt = DateTimeOffset.Now;
                try
                {
                    DemoPackageStore.Save(after);
                }
                catch (Exception ex)
                {
                    RefreshPackages(savedPackageId);
                    return "تم حفظ بيانات الباقة، لكن تعذر تحديث حالة التفعيل: " + ex.Message;
                }
            }
        }

        RefreshPackages(savedPackageId);
        SetStatus(message, false);
        return message;
    }

    /// <summary>Compatibility path retained for the earlier create-only dialog workflow.</summary>
    public string SaveQuickCreateFrom(FrameworkElement quickForm, bool addAnother, out bool saved)
    {
        ArgumentNullException.ThrowIfNull(quickForm);

        var before = DemoPackageStore.Load();
        var beforeIds = before.Packages
            .Select(p => p.Id?.ToString() ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        CopyQuickCreateControls(quickForm, this);
        var button = addAnother ? SaveAndNewButton : SavePackageButton;
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));

        var after = DemoPackageStore.Load();
        saved = after.Packages.Any(p =>
            !p.IsArchived && !beforeIds.Contains(p.Id?.ToString() ?? string.Empty));

        if (addAnother && saved)
            CopyQuickCreateControls(this, quickForm);

        return StatusTextBlock?.Text ?? string.Empty;
    }

    public void CopyQuickCreateValuesTo(FrameworkElement target)
    {
        ArgumentNullException.ThrowIfNull(target);
        CopyQuickCreateControls(this, target);
    }

    private static string CreateUniqueCopyName(string sourceName, IEnumerable<DemoServicePackage> packages)
    {
        var existingNames = new HashSet<string>(
            packages.Select(p => p.Name?.Trim() ?? string.Empty),
            StringComparer.OrdinalIgnoreCase);

        var baseName = $"{sourceName.Trim()} - نسخة";
        var candidate = baseName;
        var suffix = 2;
        while (existingNames.Contains(candidate))
            candidate = $"{baseName} ({suffix++})";

        return candidate;
    }

    private static void CopyQuickCreateControls(FrameworkElement sourceRoot, FrameworkElement targetRoot)
    {
        foreach (string name in QuickCreateControlNames)
        {
            var source = FindQuickCreateControlByName(sourceRoot, name);
            var target = FindQuickCreateControlByName(targetRoot, name);
            if (source is null || target is null)
                continue;

            if (source is TextBox sourceText && target is TextBox targetText)
            {
                targetText.Text = sourceText.Text;
            }
            else if (source is CheckBox sourceCheck && target is CheckBox targetCheck)
            {
                targetCheck.IsChecked = sourceCheck.IsChecked;
            }
            else if (source is ComboBox sourceCombo && target is ComboBox targetCombo)
            {
                var selectedTag = (sourceCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
                ComboBoxItem? match = null;
                if (!string.IsNullOrEmpty(selectedTag))
                {
                    match = targetCombo.Items.OfType<ComboBoxItem>()
                        .FirstOrDefault(item => string.Equals(
                            item.Tag?.ToString(), selectedTag, StringComparison.OrdinalIgnoreCase));
                }

                if (match is not null)
                    targetCombo.SelectedItem = match;
                else if (sourceCombo.SelectedIndex >= 0 && sourceCombo.SelectedIndex < targetCombo.Items.Count)
                    targetCombo.SelectedIndex = sourceCombo.SelectedIndex;
            }
        }
    }

    private static FrameworkElement? FindQuickCreateControlByName(FrameworkElement root, string name)
    {
        if (root.FindName(name) is FrameworkElement named)
            return named;

        return FindQuickCreateControlRecursive(root, name);
    }

    private static FrameworkElement? FindQuickCreateControlRecursive(DependencyObject parent, string name)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement element && string.Equals(element.Name, name, StringComparison.Ordinal))
                return element;

            var nested = FindQuickCreateControlRecursive(child, name);
            if (nested is not null)
                return nested;
        }

        return null;
    }
}
