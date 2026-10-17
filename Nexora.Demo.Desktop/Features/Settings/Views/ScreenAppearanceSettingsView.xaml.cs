using System.Windows;
using System.Windows.Controls;
using Nexora.Demo.Desktop.Features.Settings.Models;
using Nexora.Demo.Desktop.Features.Settings.Services;

namespace Nexora.Demo.Desktop.Features.Settings.Views;

public partial class ScreenAppearanceSettingsView : UserControl
{
    private bool _initializing = true;
    public event EventHandler? PreferencesApplied;

    public ScreenAppearanceSettingsView()
    {
        InitializeComponent();
        DensityComboBox.Items.Add("مريحة");
        DensityComboBox.Items.Add("مدمجة");
        DensityComboBox.SelectedItem = DemoSystemSettingsStore.Current.NavigationDensity;
        _initializing = false;
        UpdatePreview();
    }

    private void DensityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing) UpdatePreview();
    }

    private void UpdatePreview()
    {
        var compact = DensityComboBox.SelectedItem as string == "مدمجة";
        PreviewItemOne.Padding = compact ? new Thickness(8, 4, 8, 4) : new Thickness(10, 7, 10, 7);
        PreviewItemTwo.Padding = compact ? new Thickness(8, 4, 8, 4) : new Thickness(10, 7, 10, 7);
        PreviewItemOne.Margin = compact ? new Thickness(0, 0, 0, 3) : new Thickness(0, 0, 0, 6);
    }

    private void ApplyAppearanceButton_Click(object sender, RoutedEventArgs e)
    {
        var current = DemoSystemSettingsStore.Current;
        var updated = new DemoSystemPreferences
        {
            StartupSection = current.StartupSection,
            NavigationDensity = DensityComboBox.SelectedItem as string ?? "مريحة",
            ConfirmBeforeBackup = current.ConfirmBeforeBackup,
            ShowSuccessMessages = current.ShowSuccessMessages
        };
        try
        {
            DemoSystemSettingsStore.Save(updated);
            PreferencesApplied?.Invoke(this, EventArgs.Empty);
            AppearanceStatusTextBlock.Text = $"تم تطبيق نمط «{updated.NavigationDensity}» على القائمة الجانبية وتبويبات الأقسام.";
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            AppearanceStatusTextBlock.Text = "تعذر حفظ المظهر: " + ex.Message;
        }
    }
}