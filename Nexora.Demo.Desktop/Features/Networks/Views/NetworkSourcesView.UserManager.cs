using System.Windows;
using System.Windows.Controls;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Shared;
using Nexora.Demo.Desktop.Features.Packages.Services;
using Nexora.Demo.Desktop.Features.Networks.Models;
using Nexora.Demo.Desktop.Features.Networks.Services;

namespace Nexora.Demo.Desktop.Features.Networks.Views;

// Responsibility: User Manager settings, profiles, clients, and diagnostics.
public partial class NetworkSourcesView
{
private void LoadUserManagerSettingsControls()
{
    _isLoadingUserManagerControls = true;

    try
    {
        var userManager = _settings.UserManager;

        UserManagerEnabledCheckBox.IsChecked = userManager.IsEnabled;
        UserManagerUseProfilesCheckBox.IsChecked = userManager.UseProfiles;
        UserManagerAccountingEnabledCheckBox.IsChecked =
            userManager.AccountingEnabled;

        UserManagerAuthenticationPortTextBox.Text =
            userManager.AuthenticationPort.ToString();

        UserManagerAccountingPortTextBox.Text =
            userManager.AccountingPort.ToString();

        UserManagerInterimUpdateTextBox.Text =
            userManager.InterimUpdateSeconds.ToString();

        UserManagerCommentTextBox.Text = userManager.Comment;

        UserManagerRouterComboBox.ItemsSource = null;
        UserManagerRouterComboBox.ItemsSource = _settings.Routers;
        UserManagerRouterComboBox.DisplayMemberPath =
            nameof(RouterConnectionProfile.Name);
        UserManagerRouterComboBox.SelectedValuePath =
            nameof(RouterConnectionProfile.Id);
        UserManagerRouterComboBox.SelectedValue = userManager.RouterId;

        UserManagerProfilesListBox.ItemsSource = null;
        UserManagerProfilesListBox.ItemsSource = userManager.Profiles;

        var profile =
            userManager.Profiles.FirstOrDefault(
                item => item.Id == _selectedUserManagerProfileId)
            ?? userManager.Profiles.FirstOrDefault();

        _selectedUserManagerProfileId = profile?.Id;
        UserManagerProfilesListBox.SelectedItem = profile;
        PopulateUserManagerProfileEditor(profile);

        UserManagerClientsListBox.ItemsSource = null;
        UserManagerClientsListBox.ItemsSource = userManager.RouterClients;

        var client =
            userManager.RouterClients.FirstOrDefault(
                item => item.Id == _selectedUserManagerClientId)
            ?? userManager.RouterClients.FirstOrDefault();

        _selectedUserManagerClientId = client?.Id;
        UserManagerClientsListBox.SelectedItem = client;
        PopulateUserManagerClientEditor(client);

        UserManagerStatusSummaryTextBlock.Text =
            userManager.IsEnabled ? "مفعّلة في النموذج" : "غير مفعّلة";

        UserManagerProfileCountTextBlock.Text =
            userManager.Profiles.Count.ToString("N0");

        UserManagerRadiusClientCountTextBlock.Text =
            userManager.RouterClients.Count.ToString("N0");

        UserManagerPackageBindingSummaryTextBlock.Text =
            UserManagerPackageBindingsTextBlock.Text;
    }
    finally
    {
        _isLoadingUserManagerControls = false;
    }
}
private void SaveUserManagerSettingsButton_Click(
    object sender,
    RoutedEventArgs e)
{
    if (!int.TryParse(
            UserManagerAuthenticationPortTextBox.Text,
            out var authenticationPort) ||
        authenticationPort is < 1 or > 65535)
    {
        UserManagerSettingsStatusTextBlock.Text =
            "منفذ المصادقة يجب أن يكون رقمًا بين 1 و65535.";
        return;
    }

    if (!int.TryParse(
            UserManagerAccountingPortTextBox.Text,
            out var accountingPort) ||
        accountingPort is < 1 or > 65535)
    {
        UserManagerSettingsStatusTextBlock.Text =
            "منفذ المحاسبة يجب أن يكون رقمًا بين 1 و65535.";
        return;
    }

    if (!int.TryParse(
            UserManagerInterimUpdateTextBox.Text,
            out var interimUpdateSeconds) ||
        interimUpdateSeconds < 0)
    {
        UserManagerSettingsStatusTextBlock.Text =
            "فترة تحديث المحاسبة يجب أن تكون صفرًا أو أكبر.";
        return;
    }

    var settings = _settings.UserManager;

    settings.IsEnabled =
        UserManagerEnabledCheckBox.IsChecked == true;

    settings.UseProfiles =
        UserManagerUseProfilesCheckBox.IsChecked == true;

    settings.AccountingEnabled =
        UserManagerAccountingEnabledCheckBox.IsChecked == true;

    settings.AuthenticationPort = authenticationPort;
    settings.AccountingPort = accountingPort;
    settings.InterimUpdateSeconds = interimUpdateSeconds;
    settings.RouterId = UserManagerRouterComboBox.SelectedValue as string;
    settings.Comment = UserManagerCommentTextBox.Text.Trim();

    try
    {
        _settingsStore.Save(_settings);

        UserManagerSettingsStatusTextBlock.Text =
            "حُفظت إعدادات مدير المستخدمين محليًا في الديمو. لم يُجرَ اتصال براوتر.";
    }
    catch (Exception ex)
    {
        UserManagerSettingsStatusTextBlock.Text =
            $"تعذر حفظ الإعدادات: {ex.Message}";
    }
}
private void ReloadUserManagerSettingsButton_Click(
    object sender,
    RoutedEventArgs e)
{
    try
    {
        _settings = _settingsStore.Load();

        RouterConnectionsListBox.ItemsSource = null;
        RouterConnectionsListBox.ItemsSource = _settings.Routers;

        LoadUserManagerSettingsControls();

        UserManagerSettingsStatusTextBlock.Text =
            "أُعيد تحميل الإعدادات المحفوظة محليًا.";
    }
    catch (Exception ex)
    {
        UserManagerSettingsStatusTextBlock.Text =
            $"تعذر تحميل الإعدادات: {ex.Message}";
    }
}
private void PopulateUserManagerProfileEditor(
    UserManagerProfileSettings? profile)
{
    UserManagerProfileNameTextBox.Text = profile?.Name ?? "default";

    UserManagerProfileEnabledCheckBox.IsChecked =
        profile?.IsEnabled ?? true;

    UserManagerProfileDownloadLimitTextBox.Text =
        (profile?.DownloadLimitKbps ?? 0).ToString();

    UserManagerProfileUploadLimitTextBox.Text =
        (profile?.UploadLimitKbps ?? 0).ToString();

    UserManagerProfileSharedUsersTextBox.Text =
        (profile?.SharedUsers ?? 1).ToString();

    UserManagerProfileCommentTextBox.Text =
        profile?.Comment ?? string.Empty;
}
private void UserManagerProfilesListBox_SelectionChanged(
    object sender,
    SelectionChangedEventArgs e)
{
    if (_isLoadingUserManagerControls)
        return;

    if (UserManagerProfilesListBox.SelectedItem
        is UserManagerProfileSettings profile)
    {
        _selectedUserManagerProfileId = profile.Id;
        PopulateUserManagerProfileEditor(profile);
    }
    else
    {
        _selectedUserManagerProfileId = null;
        PopulateUserManagerProfileEditor(null);
    }
}
private void NewUserManagerProfileButton_Click(
    object sender,
    RoutedEventArgs e)
{
    _selectedUserManagerProfileId = null;
    UserManagerProfilesListBox.SelectedItem = null;
    PopulateUserManagerProfileEditor(null);

    UserManagerProfileNameTextBox.Text = string.Empty;
    UserManagerProfileStatusTextBlock.Text =
        "أدخل بيانات البروفايل الجديد ثم احفظه.";
}
private void SaveUserManagerProfileButton_Click(
    object sender,
    RoutedEventArgs e)
{
    var name = UserManagerProfileNameTextBox.Text.Trim();

    if (string.IsNullOrWhiteSpace(name))
    {
        UserManagerProfileStatusTextBlock.Text =
            "أدخل اسم البروفايل.";
        return;
    }

    if (!int.TryParse(
            UserManagerProfileDownloadLimitTextBox.Text,
            out var downloadLimit) ||
        downloadLimit < 0)
    {
        UserManagerProfileStatusTextBlock.Text =
            "حد التنزيل يجب أن يكون صفرًا أو أكبر، وبوحدة Kbps.";
        return;
    }

    if (!int.TryParse(
            UserManagerProfileUploadLimitTextBox.Text,
            out var uploadLimit) ||
        uploadLimit < 0)
    {
        UserManagerProfileStatusTextBlock.Text =
            "حد الرفع يجب أن يكون صفرًا أو أكبر، وبوحدة Kbps.";
        return;
    }

    if (!int.TryParse(
            UserManagerProfileSharedUsersTextBox.Text,
            out var sharedUsers) ||
        sharedUsers < 1)
    {
        UserManagerProfileStatusTextBlock.Text =
            "عدد المستخدمين المتزامنين يجب أن يكون واحدًا أو أكثر.";
        return;
    }

    var profile = _settings.UserManager.Profiles.FirstOrDefault(
        item => item.Id == _selectedUserManagerProfileId);

    var isNew = profile is null;

    profile ??= new UserManagerProfileSettings();

    profile.Name = name;
    profile.IsEnabled =
        UserManagerProfileEnabledCheckBox.IsChecked == true;

    profile.DownloadLimitKbps = downloadLimit;
    profile.UploadLimitKbps = uploadLimit;
    profile.SharedUsers = sharedUsers;
    profile.Comment = UserManagerProfileCommentTextBox.Text.Trim();

    try
    {
        if (isNew)
            _settings.UserManager.Profiles.Add(profile);

        _settingsStore.Save(_settings);

        _selectedUserManagerProfileId = profile.Id;
        LoadUserManagerSettingsControls();

        UserManagerProfileStatusTextBlock.Text =
            $"حُفظ البروفايل «{profile.Name}» محليًا.";
    }
    catch (Exception ex)
    {
        if (isNew)
            _settings.UserManager.Profiles.Remove(profile);

        UserManagerProfileStatusTextBlock.Text =
            $"تعذر حفظ البروفايل: {ex.Message}";
    }
}
private void DeleteUserManagerProfileButton_Click(
    object sender,
    RoutedEventArgs e)
{
    var profile = _settings.UserManager.Profiles.FirstOrDefault(
        item => item.Id == _selectedUserManagerProfileId);

    if (profile is null)
    {
        UserManagerProfileStatusTextBlock.Text =
            "اختر بروفايلًا لحذفه.";
        return;
    }

    var originalIndex = _settings.UserManager.Profiles.IndexOf(profile);
    _settings.UserManager.Profiles.Remove(profile);

    try
    {
        _settingsStore.Save(_settings);

        _selectedUserManagerProfileId = null;
        LoadUserManagerSettingsControls();

        UserManagerProfileStatusTextBlock.Text =
            $"حُذف البروفايل «{profile.Name}» من بيانات الديمو.";
    }
    catch (Exception ex)
    {
        _settings.UserManager.Profiles.Insert(
            Math.Clamp(originalIndex, 0, _settings.UserManager.Profiles.Count),
            profile);

        LoadUserManagerSettingsControls();

        UserManagerProfileStatusTextBlock.Text =
            $"تعذر حذف البروفايل: {ex.Message}";
    }
}
private void PopulateUserManagerClientEditor(
    UserManagerRouterClientSettings? client)
{
    UserManagerClientNameTextBox.Text =
        client?.Name ?? string.Empty;

    UserManagerClientAddressTextBox.Text =
        client?.Address ?? string.Empty;

    UserManagerClientEnabledCheckBox.IsChecked =
        client?.IsEnabled ?? true;

    UserManagerClientProtocolComboBox.SelectedItem =
        client?.Protocol ?? RadiusTransportProtocol.Udp;

    UserManagerClientCoaPortTextBox.Text =
        (client?.CoaPort ?? 3799).ToString();

    UserManagerClientSecretConfiguredCheckBox.IsChecked =
        client?.SharedSecretConfigured ?? false;

    UserManagerClientCommentTextBox.Text =
        client?.Comment ?? string.Empty;
}
private void UserManagerClientsListBox_SelectionChanged(
    object sender,
    SelectionChangedEventArgs e)
{
    if (_isLoadingUserManagerControls)
        return;

    if (UserManagerClientsListBox.SelectedItem
        is UserManagerRouterClientSettings client)
    {
        _selectedUserManagerClientId = client.Id;
        PopulateUserManagerClientEditor(client);
    }
    else
    {
        _selectedUserManagerClientId = null;
        PopulateUserManagerClientEditor(null);
    }
}
private void NewUserManagerClientButton_Click(
    object sender,
    RoutedEventArgs e)
{
    _selectedUserManagerClientId = null;
    UserManagerClientsListBox.SelectedItem = null;
    PopulateUserManagerClientEditor(null);

    UserManagerClientNameTextBox.Clear();
    UserManagerClientAddressTextBox.Clear();

    UserManagerClientStatusTextBlock.Text =
        "أدخل اسم الجهاز وعنوانه ثم احفظه.";
}
private void SaveUserManagerClientButton_Click(
    object sender,
    RoutedEventArgs e)
{
    var name = UserManagerClientNameTextBox.Text.Trim();
    var address = UserManagerClientAddressTextBox.Text.Trim();

    if (string.IsNullOrWhiteSpace(name))
    {
        UserManagerClientStatusTextBlock.Text =
            "أدخل اسمًا لعميل RADIUS.";
        return;
    }

    if (UserManagerClientEnabledCheckBox.IsChecked == true &&
        string.IsNullOrWhiteSpace(address))
    {
        UserManagerClientStatusTextBlock.Text =
            "أدخل عنوان الجهاز أو عطّل العميل في النموذج.";
        return;
    }

    if (!int.TryParse(
            UserManagerClientCoaPortTextBox.Text,
            out var coaPort) ||
        coaPort is < 1 or > 65535)
    {
        UserManagerClientStatusTextBlock.Text =
            "منفذ CoA يجب أن يكون رقمًا بين 1 و65535.";
        return;
    }

    if (UserManagerClientProtocolComboBox.SelectedItem
        is not RadiusTransportProtocol protocol)
    {
        UserManagerClientStatusTextBlock.Text =
            "اختر بروتوكول النقل.";
        return;
    }

    var client = _settings.UserManager.RouterClients.FirstOrDefault(
        item => item.Id == _selectedUserManagerClientId);

    var isNew = client is null;
    client ??= new UserManagerRouterClientSettings();

    client.Name = name;
    client.Address = address;
    client.IsEnabled =
        UserManagerClientEnabledCheckBox.IsChecked == true;
    client.Protocol = protocol;
    client.CoaPort = coaPort;

    client.SharedSecretConfigured =
        UserManagerClientSecretConfiguredCheckBox.IsChecked == true;

    client.Comment = UserManagerClientCommentTextBox.Text.Trim();

    try
    {
        if (isNew)
            _settings.UserManager.RouterClients.Add(client);

        _settingsStore.Save(_settings);

        _selectedUserManagerClientId = client.Id;
        LoadUserManagerSettingsControls();

        UserManagerClientStatusTextBlock.Text =
            $"حُفظ عميل RADIUS «{client.Name}» محليًا.";
    }
    catch (Exception ex)
    {
        if (isNew)
            _settings.UserManager.RouterClients.Remove(client);

        UserManagerClientStatusTextBlock.Text =
            $"تعذر حفظ العميل: {ex.Message}";
    }
}
private void DeleteUserManagerClientButton_Click(
    object sender,
    RoutedEventArgs e)
{
    var client = _settings.UserManager.RouterClients.FirstOrDefault(
        item => item.Id == _selectedUserManagerClientId);

    if (client is null)
    {
        UserManagerClientStatusTextBlock.Text =
            "اختر عميل RADIUS لحذفه.";
        return;
    }

    var originalIndex =
        _settings.UserManager.RouterClients.IndexOf(client);

    _settings.UserManager.RouterClients.Remove(client);

    try
    {
        _settingsStore.Save(_settings);

        _selectedUserManagerClientId = null;
        LoadUserManagerSettingsControls();

        UserManagerClientStatusTextBlock.Text =
            $"حُذف العميل «{client.Name}» من بيانات الديمو.";
    }
    catch (Exception ex)
    {
        _settings.UserManager.RouterClients.Insert(
            Math.Clamp(
                originalIndex,
                0,
                _settings.UserManager.RouterClients.Count),
            client);

        LoadUserManagerSettingsControls();

        UserManagerClientStatusTextBlock.Text =
            $"تعذر حذف العميل: {ex.Message}";
    }
}
private void ManageRouterConnectionsButton_Click(
    object sender,
    RoutedEventArgs e)
{
    RouterSettingsPanel.Visibility = Visibility.Visible;
    RouterSettingsPanel.BringIntoView();

    RouterSettingsStatusTextBlock.Text =
        "تعديل اتصالات الراوتر هنا محلي في الديمو؛ لا يوجد اتصال فعلي.";
}
private void RefreshUserManagerDiagnosticsButton_Click(
    object sender,
    RoutedEventArgs e)
{
    UserManagerDiagnosticsStatusTextBlock.Text =
        $"تم تحديث عرض التشخيص في {DateTime.Now:HH:mm:ss}. " +
        "لم يُرسل أي طلب إلى راوتر أو خادم RADIUS.";
}
}
