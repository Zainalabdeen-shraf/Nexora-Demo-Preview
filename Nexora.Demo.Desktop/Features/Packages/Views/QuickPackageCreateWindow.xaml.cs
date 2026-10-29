using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Nexora.Demo.Desktop.Features.Packages.Views;

namespace Nexora.Demo.Desktop.Features.Packages.Views;

/// <summary>
/// Shared compact package dialog for creation, editing, and creating a separate copy.
/// Data validation and persistence remain in PackageManagementView's existing workflow.
/// </summary>
public partial class QuickPackageCreateWindow : Window
{
    private readonly string _providerKey;
    private readonly PackageManagementView _logicHost;
    private readonly PackageManagementRow? _sourceRow;
    private readonly QuickPackageCreateMode _mode;
    private bool _isDirty;
    private bool _initializing = true;

    public string? SavedPackageId { get; private set; }
    public string? ResultStatusMessage { get; private set; }

    public QuickPackageCreateWindow(
        string providerKey,
        PackageManagementRow? row = null,
        QuickPackageCreateMode mode = QuickPackageCreateMode.Create)
    {
        InitializeComponent();

        _providerKey = string.Equals(providerKey, "user-manager", StringComparison.OrdinalIgnoreCase)
            ? "user-manager"
            : "hotspot";
        _sourceRow = row;
        _mode = mode;

        if (_mode != QuickPackageCreateMode.Create && _sourceRow is null)
            throw new ArgumentException("يجب تحديد الباقة عند التعديل أو إنشاء نسخة.", nameof(row));

        _logicHost = new PackageManagementView(_providerKey)
        {
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };
        LogicHostContainer.Children.Add(_logicHost);

        ConfigureProviderFields();

        // This helper loads the selected row into the existing form state and copies
        // the values into this compact form. It is implemented in the bridge partial.
        _logicHost.PrepareQuickPackageDialog(this, _sourceRow, _mode);

        UpdateLimitControlStates();
        UpdateCommissionControlStates();
        UpdatePolicyVisibility();
        ConfigureDialogMode();
        RegisterDirtyHandlers();

        _initializing = false;
        _isDirty = false;
        UpdateSaveAvailability();
    }

    private void ConfigureDialogMode()
    {
        string title = _mode switch
        {
            QuickPackageCreateMode.Edit => "تعديل الباقة",
            QuickPackageCreateMode.Duplicate => "إنشاء نسخة مستقلة",
            _ => "إنشاء باقة جديدة"
        };

        Title = title;
        SetHeaderTitle(title);

        bool isEditing = _mode == QuickPackageCreateMode.Edit;
        BindingEnabledCheckBox.Visibility = isEditing ? Visibility.Visible : Visibility.Collapsed;
        BindingEnabledCheckBox.IsChecked = isEditing && _sourceRow?.Binding?.IsEnabled == true;
        BindingEnabledCheckBox.Content = BindingEnabledCheckBox.IsChecked == true ? "إيقاف" : "تشغيل";

        SavePackageButton.Content = _mode switch
        {
            QuickPackageCreateMode.Edit => "حفظ التعديلات",
            QuickPackageCreateMode.Duplicate => "إنشاء النسخة",
            _ => "حفظ الباقة"
        };

        QuickCreateStatusTextBlock.Text = _mode switch
        {
            QuickPackageCreateMode.Edit => "عدّل البيانات المطلوبة ثم احفظ التعديلات.",
            QuickPackageCreateMode.Duplicate => "غيّر اسم الباقة أو بياناتها لإنشاء نسخة مستقلة.",
            _ => "أدخل بيانات الباقة ثم احفظها."
        };
        QuickCreateStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x65, 0x7A, 0x99));
    }

    private void SetHeaderTitle(string title)
    {
        var header = FindHeaderTitleTextBlock(RootGrid);
        if (header is not null)
            header.Text = title;
    }

    private static TextBlock? FindHeaderTitleTextBlock(DependencyObject parent)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is TextBlock textBlock &&
                string.Equals(textBlock.Text, "إنشاء باقة جديدة", StringComparison.Ordinal))
            {
                return textBlock;
            }

            var nested = FindHeaderTitleTextBlock(child);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private void ConfigureProviderFields()
    {
        bool isHotSpot = _providerKey == "hotspot";
        HotSpotSourcePanel.Visibility = isHotSpot ? Visibility.Visible : Visibility.Collapsed;
        UserManagerSourcePanel.Visibility = isHotSpot ? Visibility.Collapsed : Visibility.Visible;
        HotSpotServerPanel.Visibility = isHotSpot ? Visibility.Visible : Visibility.Collapsed;
        UserManagerStartsWhenPanel.Visibility = isHotSpot ? Visibility.Collapsed : Visibility.Visible;
        HotSpotSharedUsersComboBox.Visibility = isHotSpot ? Visibility.Visible : Visibility.Collapsed;
        UserManagerSharedOverrideComboBox.Visibility = isHotSpot ? Visibility.Collapsed : Visibility.Visible;
        ConcurrentUsersLabel.Text = isHotSpot ? "عدد المستخدمين المتزامنين" : "تجاوز عدد الجلسات";
    }

    private void LimitOption_Changed(object sender, RoutedEventArgs e) => UpdateLimitControlStates();

    private void UpdateLimitControlStates()
    {
        if (DataQuotaAmountTextBox is null || DataQuotaUnitComboBox is null ||
            UptimeHoursTextBox is null || UptimeMinutesTextBox is null)
        {
            return;
        }

        bool unlimitedData = UnlimitedDataCheckBox.IsChecked == true;
        DataQuotaAmountTextBox.IsEnabled = !unlimitedData;
        DataQuotaUnitComboBox.IsEnabled = !unlimitedData;

        bool unlimitedUptime = UnlimitedUptimeCheckBox.IsChecked == true;
        UptimeHoursTextBox.IsEnabled = !unlimitedUptime;
        UptimeMinutesTextBox.IsEnabled = !unlimitedUptime;
    }

    private void CommissionEnabled_Changed(object sender, RoutedEventArgs e) => UpdateCommissionControlStates();

    private void UpdateCommissionControlStates()
    {
        if (CommissionModeComboBox is null || CommissionValueTextBox is null)
            return;

        bool enabled = CommissionEnabledCheckBox.IsChecked == true;
        CommissionModeComboBox.IsEnabled = enabled;
        CommissionValueTextBox.IsEnabled = enabled;
    }

    private void PolicySelection_Changed(object sender, SelectionChangedEventArgs e) => UpdatePolicyVisibility();

    private void UpdatePolicyVisibility()
    {
        // SelectionChanged can fire while InitializeComponent is building the XAML tree.
        if (DaysPolicyComboBox is null || PeriodPolicyComboBox is null ||
            CustomDaysPanel is null || AccessTimeFieldsPanel is null)
        {
            return;
        }

        string? daysTag = (DaysPolicyComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        string? periodTag = (PeriodPolicyComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        CustomDaysPanel.Visibility = daysTag == "custom" ? Visibility.Visible : Visibility.Collapsed;
        AccessTimeFieldsPanel.Visibility = periodTag == "custom" ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RegisterDirtyHandlers()
    {
        // Register only after values have been loaded, so initialization is not mistaken for editing.
        foreach (TextBox textBox in new[]
        {
            PackageNameTextBox, DataQuotaAmountTextBox, SalePriceTextBox, CardDisplayPriceTextBox,
            CommissionValueTextBox, ValidityDaysTextBox, UptimeHoursTextBox, UptimeMinutesTextBox,
            DownloadMbpsTextBox, UploadMbpsTextBox, AccessFromTextBox, AccessUntilTextBox
        })
        {
            textBox.TextChanged += FormField_TextChanged;
        }

        foreach (ComboBox comboBox in new[]
        {
            DataQuotaUnitComboBox, ValidityUnitComboBox, CommissionModeComboBox,
            HotSpotSharedUsersComboBox, UserManagerSharedOverrideComboBox,
            HotSpotProfileComboBox, HotSpotServerComboBox, UserManagerProfileComboBox,
            UserManagerGroupComboBox, UserManagerLimitationComboBox, UserManagerStartsWhenComboBox,
            DaysPolicyComboBox, PeriodPolicyComboBox
        })
        {
            comboBox.SelectionChanged += FormField_SelectionChanged;
        }

        foreach (CheckBox checkBox in new[]
        {
            UnlimitedDataCheckBox, UnlimitedUptimeCheckBox, CommissionEnabledCheckBox,
            PreserveUsageOnRouterInterruptionCheckBox, PreserveTimeOnRouterInterruptionCheckBox,
            RecordHotSpotSessionsCheckBox, BindingEnabledCheckBox, SaturdayCheckBox, SundayCheckBox, MondayCheckBox,
            TuesdayCheckBox, WednesdayCheckBox, ThursdayCheckBox, FridayCheckBox
        })
        {
            checkBox.Checked += FormField_CheckedChanged;
            checkBox.Unchecked += FormField_CheckedChanged;
        }
    }

    private void FormField_TextChanged(object sender, TextChangedEventArgs e) => MarkDirty();
    private void FormField_SelectionChanged(object sender, SelectionChangedEventArgs e) => MarkDirty();
    private void FormField_CheckedChanged(object sender, RoutedEventArgs e) => MarkDirty();

    private void MarkDirty()
    {
        if (_initializing)
            return;

        _isDirty = true;
        UpdateSaveAvailability();

        if (_mode == QuickPackageCreateMode.Duplicate)
        {
            QuickCreateStatusTextBlock.Text = "تم تعديل البيانات. تأكد من إدخال اسم جديد للنسخة قبل الحفظ.";
            QuickCreateStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x65, 0x7A, 0x99));
        }
    }

    private void UpdateSaveAvailability()
    {
        SavePackageButton.IsEnabled = _mode != QuickPackageCreateMode.Duplicate || _isDirty;
        SavePackageButton.Opacity = SavePackageButton.IsEnabled ? 1.0 : 0.65;
    }

    private void SavePackageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mode == QuickPackageCreateMode.Duplicate && !_isDirty)
        {
            QuickCreateStatusTextBlock.Text = "غيّر اسم الباقة أو أحد بياناتها قبل إنشاء النسخة.";
            return;
        }

        // The current persisted model represents validity as whole days. Do not silently
        // save a selected hours value as days until the persistence contract is extended.
        if (string.Equals(
            (ValidityUnitComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(),
            "hours",
            StringComparison.OrdinalIgnoreCase))
        {
            QuickCreateStatusTextBlock.Text = "الصلاحية بالساعات غير مدعومة في نموذج الحفظ الحالي؛ لم تُحفظ القيمة.";
            return;
        }

        try
        {
            string message = _logicHost.SaveQuickPackageDialog(
                this,
                _sourceRow,
                _mode,
                BindingEnabledCheckBox.IsChecked == true,
                out bool saved,
                out string? savedPackageId);

            QuickCreateStatusTextBlock.Text = message;
            if (!saved)
            {
                QuickCreateStatusTextBlock.Foreground = System.Windows.Media.Brushes.Firebrick;
                return;
            }

            SavedPackageId = savedPackageId;
            ResultStatusMessage = message;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            QuickCreateStatusTextBlock.Text = "تعذر حفظ الباقة: " + ex.Message;
            QuickCreateStatusTextBlock.Foreground = System.Windows.Media.Brushes.Firebrick;
        }
    }

    private void BindingEnabledCheckBox_StateChanged(object sender, RoutedEventArgs e)
    {
        if (BindingEnabledCheckBox is null)
            return;

        // The switch shows the action available to the user, not a technical binding label.
        BindingEnabledCheckBox.Content = BindingEnabledCheckBox.IsChecked == true ? "إيقاف" : "تشغيل";
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try
            {
                DragMove();
            }
            catch (InvalidOperationException)
            {
                // The window might be closing while the drag event is being delivered.
            }
        }
    }
}
