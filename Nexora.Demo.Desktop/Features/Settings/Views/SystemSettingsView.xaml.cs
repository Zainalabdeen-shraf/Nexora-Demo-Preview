using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Nexora.Demo.Desktop.Features.Settings.Models;
using Nexora.Demo.Desktop.Features.Settings.Services;

namespace Nexora.Demo.Desktop.Features.Settings.Views;

public partial class SystemSettingsView : UserControl
{
    public SystemSettingsView()
    {
        InitializeComponent();
        StartupSectionComboBox.Items.Add("الرئيسية");
        StartupSectionComboBox.Items.Add("مركز الشبكة");
        StartupSectionComboBox.Items.Add("الكروت والدفعات");
        StartupSectionComboBox.SelectedItem = DemoSystemSettingsStore.Current.StartupSection switch
        {
            "network-center" => "مركز الشبكة",
            "cards" => "الكروت والدفعات",
            _ => "الرئيسية"
        };
        ConfirmBeforeBackupCheckBox.IsChecked = DemoSystemSettingsStore.Current.ConfirmBeforeBackup;
        ShowSuccessMessagesCheckBox.IsChecked = DemoSystemSettingsStore.Current.ShowSuccessMessages;
        DataDirectoryTextBlock.Text = "مجلد البيانات المحلي: " + DemoSystemSettingsStore.DataDirectory;
        if (DemoSystemSettingsStore.LoadWarning is not null)
            SettingsStatusTextBlock.Text = DemoSystemSettingsStore.LoadWarning;
    }

    private void SavePreferencesButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedSection = StartupSectionComboBox.SelectedItem as string;
        var startupSection = selectedSection switch
        {
            "مركز الشبكة" => "network-center",
            "الكروت والدفعات" => "cards",
            _ => "dashboard"
        };
        var current = DemoSystemSettingsStore.Current;
        var updated = new DemoSystemPreferences
        {
            StartupSection = startupSection,
            NavigationDensity = current.NavigationDensity,
            ConfirmBeforeBackup = ConfirmBeforeBackupCheckBox.IsChecked == true,
            ShowSuccessMessages = ShowSuccessMessagesCheckBox.IsChecked == true
        };
        try
        {
            DemoSystemSettingsStore.Save(updated);
            SettingsStatusTextBlock.Text = "حُفظت التفضيلات محليًا. ستُستخدم صفحة البداية في تسجيل الدخول التالي.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            SettingsStatusTextBlock.Text = "تعذر حفظ التفضيلات: " + ex.Message;
        }
    }

    private void OpenDataFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(DemoSystemSettingsStore.DataDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + DemoSystemSettingsStore.DataDirectory + "\"") { UseShellExecute = true });
            SettingsStatusTextBlock.Text = "فُتح مجلد بيانات الديمو المحلي.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            SettingsStatusTextBlock.Text = "تعذر فتح المجلد: " + ex.Message;
        }
    }
}