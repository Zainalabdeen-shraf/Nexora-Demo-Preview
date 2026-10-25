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

// Responsibility: Package editing form state, validation, save workflow, and value conversion.
public partial class PackageManagementView
{
    private void LoadPackageRow(PackageManagementRow row)
    {
        _loadingForm = true;
        _editingPackage = row.Package.Copy();
        _editingBinding = row.Binding?.Copy() ?? new DemoPackageBinding { ProviderKey = _providerKey, PackageId = row.Package.Id };

        PackageNameTextBox.Text = row.Package.Name;
        SalePriceTextBox.Text = FormatNumber(row.Package.SalePriceYer);
        CardDisplayPriceTextBox.Text = FormatNumber(row.Package.CardDisplayPriceYer);
        ValidityDaysTextBox.Text = row.Package.ValidityDays.ToString(CultureInfo.InvariantCulture);
        UnlimitedUptimeCheckBox.IsChecked = row.Package.UptimeHours == 0 && row.Package.UptimeMinutes == 0;
        DataQuotaAmountTextBox.Text = FormatNumber(row.Package.DataQuotaAmount);
        SelectTag(DataQuotaUnitComboBox, row.Package.DataQuotaUnit);
        UnlimitedDataCheckBox.IsChecked = row.Package.UnlimitedData;
        DownloadMbpsTextBox.Text = FormatNumber(row.Package.DownloadMbps);
        UploadMbpsTextBox.Text = FormatNumber(row.Package.UploadMbps);
        UptimeHoursTextBox.Text = row.Package.UptimeHours.ToString(CultureInfo.InvariantCulture);
        UptimeMinutesTextBox.Text = row.Package.UptimeMinutes.ToString(CultureInfo.InvariantCulture);
        CommissionEnabledCheckBox.IsChecked = row.Package.CommissionEnabled;
        SelectTag(CommissionModeComboBox, row.Package.CommissionMode);
        CommissionValueTextBox.Text = FormatNumber(row.Package.CommissionValue);
        PreserveUsageOnRouterInterruptionCheckBox.IsChecked = row.Package.PreserveUsageOnRouterInterruption;
        PreserveTimeOnRouterInterruptionCheckBox.IsChecked = row.Package.PreserveTimeOnRouterInterruption;
        RecordHotSpotSessionsCheckBox.IsChecked = row.Package.RecordHotSpotSessions;

        var binding = _editingBinding;
        SelectTag(HotSpotProfileComboBox, binding.HotSpotProfile);
        SelectTag(HotSpotServerComboBox, binding.HotSpotServer);
        SelectTag(HotSpotSharedUsersComboBox, binding.HotSpotSharedUsers.ToString(CultureInfo.InvariantCulture));
        SelectTag(UserManagerProfileComboBox, binding.UserManagerProfile);
        SelectTag(UserManagerGroupComboBox, binding.UserManagerGroup);
        SelectTag(UserManagerLimitationComboBox, binding.UserManagerLimitation);
        SelectTag(UserManagerStartsWhenComboBox, binding.UserManagerStartsWhen);
        SelectTag(UserManagerSharedOverrideComboBox, binding.UserManagerSharedOverride);

        var days = binding.AccessDays ?? new List<string>();
        if (days.Count == 7 && !binding.UseSchedule)
            SelectTag(DaysPolicyComboBox, "all");
        else
            SelectTag(DaysPolicyComboBox, "custom");

        SaturdayCheckBox.IsChecked = days.Contains("السبت");
        SundayCheckBox.IsChecked = days.Contains("الأحد");
        MondayCheckBox.IsChecked = days.Contains("الإثنين");
        TuesdayCheckBox.IsChecked = days.Contains("الثلاثاء");
        WednesdayCheckBox.IsChecked = days.Contains("الأربعاء");
        ThursdayCheckBox.IsChecked = days.Contains("الخميس");
        FridayCheckBox.IsChecked = days.Contains("الجمعة");
        var customPeriod = binding.AccessFrom != "00:00" || binding.AccessUntil != "23:59";
        SelectTag(PeriodPolicyComboBox, customPeriod ? "custom" : "full-day");
        AccessFromTextBox.Text = string.IsNullOrWhiteSpace(binding.AccessFrom) ? "08:00" : binding.AccessFrom;
        AccessUntilTextBox.Text = string.IsNullOrWhiteSpace(binding.AccessUntil) ? "18:00" : binding.AccessUntil;
        _loadingForm = false;
        ApplyControlStates();
        UpdatePackageEditorMode();
        SetStatus(row.Binding is null
            ? "تعريف تجاري محفوظ؛ سيُنشأ له ربط جديد عند حفظه في النظام الحالي."
            : "تم تحميل إعدادات الباقة المحددة للتعديل.", false);
    }
    private void ClearForm()
    {
        _loadingForm = true;
        _editingPackage = null;
        _editingBinding = new DemoPackageBinding { ProviderKey = _providerKey };
        PackageNameTextBox.Text = string.Empty;
        SalePriceTextBox.Text = "100";
        CardDisplayPriceTextBox.Text = "100";
        ValidityDaysTextBox.Text = "7";
        UnlimitedUptimeCheckBox.IsChecked = false;
        DataQuotaAmountTextBox.Text = "500";
        SelectTag(DataQuotaUnitComboBox, "MB");
        UnlimitedDataCheckBox.IsChecked = false;
        DownloadMbpsTextBox.Text = "2";
        UploadMbpsTextBox.Text = "0.5";
        UptimeHoursTextBox.Text = "0";
        UptimeMinutesTextBox.Text = "0";
        CommissionEnabledCheckBox.IsChecked = false;
        SelectTag(CommissionModeComboBox, "Percentage");
        CommissionValueTextBox.Text = "0";
        PreserveUsageOnRouterInterruptionCheckBox.IsChecked = true;
        PreserveTimeOnRouterInterruptionCheckBox.IsChecked = true;
        RecordHotSpotSessionsCheckBox.IsChecked = true;
        SelectTag(HotSpotProfileComboBox, "default");
        SelectTag(HotSpotServerComboBox, "hotspot1");
        SelectTag(HotSpotSharedUsersComboBox, "1");
        SelectTag(UserManagerProfileComboBox, "UM-1H-500M");
        SelectTag(UserManagerGroupComboBox, "default");
        SelectTag(UserManagerLimitationComboBox, "limit-hour-500mb-2m");
        SelectTag(UserManagerStartsWhenComboBox, "first-auth");
        SelectTag(UserManagerSharedOverrideComboBox, "off");
        SelectTag(DaysPolicyComboBox, "all");
        SaturdayCheckBox.IsChecked = false;
        SundayCheckBox.IsChecked = false;
        MondayCheckBox.IsChecked = false;
        TuesdayCheckBox.IsChecked = false;
        WednesdayCheckBox.IsChecked = false;
        ThursdayCheckBox.IsChecked = false;
        FridayCheckBox.IsChecked = false;
        SelectTag(PeriodPolicyComboBox, "full-day");
        AccessFromTextBox.Text = "08:00";
        AccessUntilTextBox.Text = "18:00";
        _suppressSelection = true;
        PackagesDataGrid.SelectedItem = null;
        _suppressSelection = false;
        _loadingForm = false;
        ApplyControlStates();
        UpdatePackageEditorMode();
    }
    private void LimitOption_Changed(object sender, RoutedEventArgs e) => ApplyControlStates();
    private void CommissionEnabled_Changed(object sender, RoutedEventArgs e) => ApplyControlStates();
    private void PolicySelection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loadingForm) ApplyControlStates();
    }
    private void ApplyControlStates()
    {
        if (DataQuotaAmountTextBox is not null) DataQuotaAmountTextBox.IsEnabled = UnlimitedDataCheckBox.IsChecked != true;
        if (DataQuotaUnitComboBox is not null) DataQuotaUnitComboBox.IsEnabled = UnlimitedDataCheckBox.IsChecked != true;
        if (ValidityDaysTextBox is not null) ValidityDaysTextBox.IsEnabled = true;
        if (UptimeHoursTextBox is not null) UptimeHoursTextBox.IsEnabled = UnlimitedUptimeCheckBox.IsChecked != true;
        if (UptimeMinutesTextBox is not null) UptimeMinutesTextBox.IsEnabled = UnlimitedUptimeCheckBox.IsChecked != true;
        if (CommissionModeComboBox is not null) CommissionModeComboBox.IsEnabled = CommissionEnabledCheckBox.IsChecked == true;
        if (CommissionValueTextBox is not null) CommissionValueTextBox.IsEnabled = CommissionEnabledCheckBox.IsChecked == true;
        if (AccessTimeFieldsPanel is not null)
            AccessTimeFieldsPanel.Visibility = SelectedTag(PeriodPolicyComboBox, "full-day") == "custom" ? Visibility.Visible : Visibility.Collapsed;
        if (CustomDaysPanel is not null)
            CustomDaysPanel.Visibility = SelectedTag(DaysPolicyComboBox, "all") == "custom" ? Visibility.Visible : Visibility.Collapsed;
    }
    private List<string> GetSelectedAccessDays()
    {
        var days = new List<string>();
        if (SaturdayCheckBox.IsChecked == true) days.Add("السبت");
        if (SundayCheckBox.IsChecked == true) days.Add("الأحد");
        if (MondayCheckBox.IsChecked == true) days.Add("الإثنين");
        if (TuesdayCheckBox.IsChecked == true) days.Add("الثلاثاء");
        if (WednesdayCheckBox.IsChecked == true) days.Add("الأربعاء");
        if (ThursdayCheckBox.IsChecked == true) days.Add("الخميس");
        if (FridayCheckBox.IsChecked == true) days.Add("الجمعة");
        return days;
    }
    private void UpdatePackageEditorMode()
    {
        var editing = _editingPackage is not null;
        if (SavePackageButton is not null)
            SavePackageButton.Content = editing ? "حفظ التعديلات" : "حفظ الباقة";
        if (SaveAndNewButton is not null)
            SaveAndNewButton.IsEnabled = !editing;
        if (CreatePackageTab is not null)
            CreatePackageTab.Header = editing ? "تعديل الباقة" : "باقة جديدة";
    }
    private void SavePackageButton_Click(object sender, RoutedEventArgs e) => SavePackage(addAnother: false);
    private void SaveAndNewButton_Click(object sender, RoutedEventArgs e) => SavePackage(addAnother: true);
    private void SavePackage(bool addAnother)
    {
        try
        {
            var catalog = DemoPackageStore.Load();
            var name = PackageNameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(name)) { SetStatus("اسم الباقة مطلوب.", true); PackageNameTextBox.Focus(); return; }
            var idToExclude = _editingPackage?.Id;
            if (catalog.Packages.Any(p => p.Id != idToExclude && string.Equals(p.Name.Trim(), name, StringComparison.CurrentCultureIgnoreCase)))
            { SetStatus("يوجد تعريف باقة بهذا الاسم. اختر التعريف المحفوظ من الجدول لربطه بهذا النظام بدل إنشاء اسم مكرر.", true); return; }

            var unlimitedUptime = UnlimitedUptimeCheckBox.IsChecked == true;
            if (!TryInt(ValidityDaysTextBox, out var validityDaysInput) || validityDaysInput < 1)
            { SetStatus("أدخل مدة صلاحية يومًا واحدًا على الأقل.", true); return; }
            var validityDays = validityDaysInput;

            var uptimeHours = 0;
            var uptimeMinutes = 0;
            if (!unlimitedUptime)
            {
                if (!TryInt(UptimeHoursTextBox, out uptimeHours) || uptimeHours < 0 ||
                    !TryInt(UptimeMinutesTextBox, out uptimeMinutes) || uptimeMinutes < 0 || uptimeMinutes > 59)
                { SetStatus("أدخل عدد الساعات بصورة صحيحة، والدقائق بين 0 و59.", true); UptimeHoursTextBox.Focus(); return; }

                if (uptimeHours == 0 && uptimeMinutes == 0)
                { SetStatus("أدخل مدة استخدام أكبر من صفر أو فعّل «ساعات مفتوحة».", true); UptimeHoursTextBox.Focus(); return; }
            }
            else
            {
                // The package model stores unlimited uptime as zero hours and zero minutes.
                uptimeHours = 0;
                uptimeMinutes = 0;
            }
            var unlimitedData = UnlimitedDataCheckBox.IsChecked == true;
            if (!unlimitedData && (!TryDecimal(DataQuotaAmountTextBox, out var quotaInput) || quotaInput <= 0))
            { SetStatus("حصة البيانات يجب أن تكون أكبر من صفر أو اختر بلا حد للبيانات.", true); return; }
            var quotaAmount = unlimitedData ? 0 : ParseDecimal(DataQuotaAmountTextBox, 500);
            var quotaUnit = SelectedTag(DataQuotaUnitComboBox, "MB");
            if (!TryDecimal(DownloadMbpsTextBox, out var download) || download < 0 || !TryDecimal(UploadMbpsTextBox, out var upload) || upload < 0)
            { SetStatus("سرعتا التنزيل والرفع يجب أن تكونا صفرًا أو قيمة موجبة.", true); return; }
            if (!TryDecimal(SalePriceTextBox, out var salePrice) || salePrice < 0 || !TryDecimal(CardDisplayPriceTextBox, out var cardPrice) || cardPrice < 0)
            { SetStatus("سعر البيع والسعر المطبوع يجب أن يكونا صفرًا أو قيمة موجبة.", true); return; }
            if (!TryDecimal(CommissionValueTextBox, out var commissionValue) || commissionValue < 0)
            { SetStatus("قيمة العمولة غير صحيحة.", true); return; }
            var commissionEnabled = CommissionEnabledCheckBox.IsChecked == true;
            var commissionMode = SelectedTag(CommissionModeComboBox, "Percentage");
            if (commissionEnabled && commissionMode == "Percentage" && (commissionValue <= 0 || commissionValue > 100))
            { SetStatus("نسبة العمولة يجب أن تكون أكبر من صفر ولا تتجاوز 100%.", true); return; }

            var catalogPackage = _editingPackage is null ? null : catalog.Packages.FirstOrDefault(p => p.Id == _editingPackage.Id);
            if (_editingPackage is not null && catalogPackage is null)
            {
                SetStatus("لم يعد تعريف الباقة المحدد موجودًا في البيانات المحفوظة. حدّث الجدول ثم أعد المحاولة.", true);
                return;
            }

            var isolateSharedDefinition = _editingPackage is not null &&
                catalogPackage is not null &&
                catalog.Bindings.Any(b => b.PackageId == catalogPackage.Id && b.ProviderKey != _providerKey);

            var package = catalogPackage?.Copy() ?? new DemoServicePackage();
            var creatingDefinition = catalogPackage is null || isolateSharedDefinition;

            if (creatingDefinition)
            {
                package.Id = Guid.NewGuid().ToString("N");
                package.CreatedAt = DateTimeOffset.Now;
                package.IsArchived = false;
            }

            if (isolateSharedDefinition && catalogPackage is not null)
            {
                // انقل ارتباط النظام الجاري تعديله إلى تعريف مستقل، واترك النظام الآخر على تعريفه الأصلي.
                catalog.Bindings.RemoveAll(b =>
                    b.PackageId == catalogPackage.Id && b.ProviderKey == _providerKey);
            }
            package.Name = name;
            package.ValidityDays = validityDays;
            package.UnlimitedValidity = false;
            package.UptimeHours = uptimeHours;
            package.UptimeMinutes = uptimeMinutes;
            package.DataQuotaAmount = quotaAmount;
            package.DataQuotaUnit = quotaUnit;
            package.UnlimitedData = unlimitedData;
            package.DownloadMbps = download;
            package.UploadMbps = upload;
            package.SalePriceYer = salePrice;
            package.CardDisplayPriceYer = cardPrice;
            package.CommissionEnabled = commissionEnabled;
            package.CommissionMode = commissionMode;
            package.CommissionValue = commissionEnabled ? commissionValue : 0;
            package.PreserveUsageOnRouterInterruption = PreserveUsageOnRouterInterruptionCheckBox.IsChecked == true;
            package.PreserveTimeOnRouterInterruption = PreserveTimeOnRouterInterruptionCheckBox.IsChecked == true;
            package.RecordHotSpotSessions = RecordHotSpotSessionsCheckBox.IsChecked == true;
            package.UpdatedAt = DateTimeOffset.Now;
            if (creatingDefinition) catalog.Packages.Add(package);
            else
            {
                var packageIndex = catalog.Packages.FindIndex(p => p.Id == package.Id);
                catalog.Packages[packageIndex] = package;
            }

            var existingBinding = catalog.Bindings.FirstOrDefault(b => b.PackageId == package.Id && b.ProviderKey == _providerKey);
            var binding = existingBinding?.Copy() ?? (_editingBinding is not null && _editingBinding.ProviderKey == _providerKey ? _editingBinding.Copy() : new DemoPackageBinding { PackageId = package.Id, ProviderKey = _providerKey });
            var creatingBinding = existingBinding is null;
            binding.PackageId = package.Id;
            binding.ProviderKey = _providerKey;
            if (creatingBinding)
            {
                binding.Id = Guid.NewGuid().ToString("N");
                binding.CreatedAt = DateTimeOffset.Now;
                binding.IsEnabled = true;
            }

            if (_providerKey == "hotspot")
            {
                binding.HotSpotProfile = SelectedTag(HotSpotProfileComboBox, "default");
                binding.HotSpotServer = SelectedTag(HotSpotServerComboBox, "hotspot1");
                if (!int.TryParse(SelectedTag(HotSpotSharedUsersComboBox, "1"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sharedUsers) || sharedUsers < 0)
                { SetStatus("عدد المستخدمين المتزامنين غير صحيح.", true); return; }
                binding.HotSpotSharedUsers = sharedUsers;
            }
            else
            {
                binding.UserManagerProfile = SelectedTag(UserManagerProfileComboBox, "UM-1H-500M");
                binding.UserManagerGroup = SelectedTag(UserManagerGroupComboBox, "default");
                binding.UserManagerLimitation = SelectedTag(UserManagerLimitationComboBox, "limit-hour-500mb-2m");
                binding.UserManagerStartsWhen = SelectedTag(UserManagerStartsWhenComboBox, "first-auth");
                binding.UserManagerSharedOverride = SelectedTag(UserManagerSharedOverrideComboBox, "off");
            }

            var dayPolicy = SelectedTag(DaysPolicyComboBox, "all");
            var periodPolicy = SelectedTag(PeriodPolicyComboBox, "full-day");
            var allDays = new List<string> { "السبت", "الأحد", "الإثنين", "الثلاثاء", "الأربعاء", "الخميس", "الجمعة" };
            if (dayPolicy == "custom" && GetSelectedAccessDays().Count == 0)
            {
                SetStatus("اختر يومًا واحدًا على الأقل، أو اختر كل الأيام.", true);
                return;
            }

            binding.AccessDays = dayPolicy == "custom"
                ? GetSelectedAccessDays()
                : allDays;
            binding.UseSchedule = dayPolicy != "all" || periodPolicy == "custom";
            binding.AccessFrom = periodPolicy == "custom" ? AccessFromTextBox.Text.Trim() : "00:00";
            binding.AccessUntil = periodPolicy == "custom" ? AccessUntilTextBox.Text.Trim() : "23:59";
            if (periodPolicy == "custom")
            {
                if (!TimeSpan.TryParse(binding.AccessFrom, CultureInfo.InvariantCulture, out var from) ||
                    !TimeSpan.TryParse(binding.AccessUntil, CultureInfo.InvariantCulture, out var until) ||
                    from < TimeSpan.Zero || until > TimeSpan.FromDays(1) || from >= until)
                { SetStatus("وقت البداية والنهاية غير صحيح؛ استخدم صيغة 24 ساعة واجعل البداية قبل النهاية.", true); return; }
            }
            binding.UpdatedAt = DateTimeOffset.Now;
            if (creatingBinding) catalog.Bindings.Add(binding);
            else
            {
                var bindingIndex = catalog.Bindings.FindIndex(b => b.Id == binding.Id && b.ProviderKey == _providerKey);
                if (bindingIndex < 0) throw new InvalidOperationException("تعذر العثور على ربط الباقة الحالي.");
                catalog.Bindings[bindingIndex] = binding;
            }

            if (_editingPackage is not null)
            {
                var confirmation = MessageBox.Show(
                    "هل تريد حفظ التعديلات على الباقة المحددة؟ سيتم تحديث الباقة الحالية ولن تُنشأ باقة جديدة.",
                    "تأكيد تعديل الباقة",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question,
                    MessageBoxResult.No);

                if (confirmation != MessageBoxResult.Yes)
                {
                    SetStatus("تم إلغاء الحفظ؛ لم تتغير البيانات المحفوظة.", false);
                    return;
                }
            }

            var successMessage = creatingDefinition
                ? "تم حفظ تعريف الباقة وربطه بالنظام المحدد."
                : creatingBinding
                    ? "تم حفظ التعريف التجاري وإنشاء الربط للنظام المحدد."
                    : "تم حفظ تعديلات الباقة والربط.";

            DemoPackageStore.Save(catalog);
            var savedPackageId = package.Id;

            if (addAnother)
            {
                ClearForm();
                RefreshPackages(savedPackageId);
                PackageTabs.SelectedItem = CreatePackageTab;
                SetStatus("تم الحفظ. أدخل بيانات الباقة التالية.", false);
                return;
            }

            // End create/edit mode, then select the saved package in the saved-packages tab.
            ClearForm();
            RefreshPackages(savedPackageId);
            PackageTabs.SelectedItem = SavedPackagesTab;
            SetStatus(successMessage, false);
        }
        catch (Exception ex)
        {
            SetStatus("تعذر حفظ الباقة: " + ex.Message, true);
        }
    }
    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        ClearForm();
        SetStatus("تم إلغاء التعديل الحالي دون حفظ تغييرات جديدة.", false);
    }
    private void SelectTag(ComboBox combo, string? tag)
    {
        foreach (var item in combo.Items.OfType<ComboBoxItem>())
        {
            if (string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            { combo.SelectedItem = item; return; }
        }
        if (!string.IsNullOrWhiteSpace(tag))
        {
            var compatibleItem = new ComboBoxItem { Tag = tag, Content = tag };
            combo.Items.Add(compatibleItem);
            combo.SelectedItem = compatibleItem;
            return;
        }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
    }
    private static string SelectedTag(ComboBox combo, string fallback)
        => (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;
    private static string FormatNumber(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    private static bool TryInt(TextBox box, out int value)
        => int.TryParse(NormalizeDigits(box.Text), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    private static int ParseInt(TextBox box, int fallback) => TryInt(box, out var value) ? value : fallback;
    private static decimal ParseDecimal(TextBox box, decimal fallback) => TryDecimal(box, out var value) ? value : fallback;
    private static bool TryDecimal(TextBox box, out decimal value)
    {
        var text = NormalizeDigits(box.Text).Trim().Replace('٫', '.').Replace(',', '.');
        return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out value) ||
               decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out value);
    }
    private static string NormalizeDigits(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] >= '٠' && chars[i] <= '٩') chars[i] = (char)('0' + chars[i] - '٠');
            else if (chars[i] >= '۰' && chars[i] <= '۹') chars[i] = (char)('0' + chars[i] - '۰');
        }
        return new string(chars);
    }
    private void SetStatus(string message, bool isError)
    {
        StatusTextBlock.Text = message;
        StatusTextBlock.Foreground = isError
            ? System.Windows.Media.Brushes.Firebrick
            : (System.Windows.Media.Brush)FindResource("PkgMutedBrush");
    }
}
