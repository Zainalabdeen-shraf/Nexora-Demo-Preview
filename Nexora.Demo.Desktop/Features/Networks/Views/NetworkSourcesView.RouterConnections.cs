using System.Windows;
using System.Windows.Controls;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Shared;
using Nexora.Demo.Desktop.Features.Packages.Services;
using Nexora.Demo.Desktop.Features.Networks.Models;
using Nexora.Demo.Desktop.Features.Networks.Services;

namespace Nexora.Demo.Desktop.Features.Networks.Views;

// Responsibility: Router connection list and local profile management.
public partial class NetworkSourcesView
{
private void LoadRouterSettings()
{
    try
    {
        _isLoadingRouterSettings = true;

        _settings = _settingsStore.Load();

        RouterConnectionsListBox.ItemsSource = null;
        RouterConnectionsListBox.ItemsSource = _settings.Routers;
        LoadUserManagerSettingsControls();
        LoadHotSpotSettingsControls();

        RouterSettingsStatusTextBlock.Text =
            _settingsStore.StorageDescription;
    }
    catch (Exception ex)
    {
        RouterSettingsStatusTextBlock.Text =
            $"تعذر تحميل الإعدادات: {ex.Message}";
    }
    finally
    {
        _isLoadingRouterSettings = false;
    }
}
private void RouterConnectionsListBox_SelectionChanged(
    object sender,
    SelectionChangedEventArgs e)
{
    if (_isLoadingRouterSettings ||
        RouterConnectionsListBox.SelectedItem is not RouterConnectionProfile router)
    {
        return;
    }

    _selectedRouterId = router.Id;

    RouterNameTextBox.Text = router.Name;
    RouterAddressTextBox.Text = router.HostAddress;
    RouterPortTextBox.Text = router.Port.ToString();
    RouterUsernameTextBox.Text = router.Username;
    RouterCommentTextBox.Text = router.Comment;
    RouterMethodComboBox.SelectedItem = router.Method;
    RouterEnabledCheckBox.IsChecked = router.IsEnabled;

    RouterSettingsStatusTextBlock.Text =
        $"تم تحميل إعدادات «{router.Name}» إلى النموذج. لم يتم الاتصال بالراوتر.";
}
private void NewRouterConnectionButton_Click(
    object sender,
    RoutedEventArgs e)
{
    _selectedRouterId = null;
    RouterConnectionsListBox.SelectedItem = null;

    RouterNameTextBox.Text = "الراوتر الرئيسي";
    RouterAddressTextBox.Clear();
    RouterPortTextBox.Text = "8729";
    RouterUsernameTextBox.Clear();
    RouterCommentTextBox.Clear();
    RouterMethodComboBox.SelectedItem =
        RouterConnectionMethod.RouterOsApiSsl;
    RouterEnabledCheckBox.IsChecked = true;

    RouterSettingsStatusTextBlock.Text =
        "أدخل بيانات الاتصال ثم اضغط حفظ الاتصال.";
}
private void ReloadRouterConnectionsButton_Click(
    object sender,
    RoutedEventArgs e)
{
    _selectedRouterId = null;
    LoadRouterSettings();
}
private void SaveRouterConnectionButton_Click(
    object sender,
    RoutedEventArgs e)
{
    if (!int.TryParse(RouterPortTextBox.Text, out var port))
    {
        RouterSettingsStatusTextBlock.Text =
            "أدخل رقم منفذ صحيحًا بين 1 و65535.";
        return;
    }

    var router = _settings.Routers.FirstOrDefault(
        item => item.Id == _selectedRouterId);

    var isNew = router is null;

    router ??= new RouterConnectionProfile();

    router.Name = RouterNameTextBox.Text.Trim();
    router.HostAddress = RouterAddressTextBox.Text.Trim();
    router.Port = port;
    router.Username = RouterUsernameTextBox.Text.Trim();
    router.Comment = RouterCommentTextBox.Text.Trim();
    router.IsEnabled = RouterEnabledCheckBox.IsChecked == true;

    if (RouterMethodComboBox.SelectedItem is not RouterConnectionMethod method)
    {
        RouterSettingsStatusTextBlock.Text =
            "اختر طريقة اتصال صحيحة.";
        return;
    }

    router.Method = method;

    try
    {
        if (isNew)
            _settings.Routers.Add(router);

        _settingsStore.Save(_settings);

        _selectedRouterId = router.Id;

        _isLoadingRouterSettings = true;
        RouterConnectionsListBox.ItemsSource = null;
        RouterConnectionsListBox.ItemsSource = _settings.Routers;
        RouterConnectionsListBox.SelectedItem = router;
        _isLoadingRouterSettings = false;

        RouterSettingsStatusTextBlock.Text =
            $"تم حفظ «{router.Name}» محليًا في الديمو. لم يتم الاتصال بالراوتر.";
    }
    catch (Exception ex)
    {
        _isLoadingRouterSettings = false;

        if (isNew)
            _settings.Routers.Remove(router);

        RouterSettingsStatusTextBlock.Text =
            $"تعذر حفظ الإعدادات: {ex.Message}";
    }
}
}
