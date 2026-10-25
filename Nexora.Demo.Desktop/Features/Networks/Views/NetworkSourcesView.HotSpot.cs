using System.Windows;
using System.Windows.Controls;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Shared;
using Nexora.Demo.Desktop.Features.Packages.Services;
using Nexora.Demo.Desktop.Features.Networks.Models;
using Nexora.Demo.Desktop.Features.Networks.Services;

namespace Nexora.Demo.Desktop.Features.Networks.Views;

// Responsibility: HotSpot authentication settings, profiles, portal preview, and diagnostics.
public partial class NetworkSourcesView
{
private void LoadHotSpotSettingsControls()
{
    _isLoadingHotSpotControls = true;

    try
    {
        var settings = _settings.HotSpot;

        HotSpotEnabledCheckBox.IsChecked = settings.IsEnabled;
        HotSpotServerNameTextBox.Text = settings.ServerName;
        HotSpotServerProfileNameTextBox.Text = settings.ServerProfileName;
        HotSpotInterfaceNameTextBox.Text = settings.InterfaceName;
        HotSpotAddressPoolNameTextBox.Text = settings.AddressPoolName;
        HotSpotDnsNameTextBox.Text = settings.DnsName;

        HotSpotAuthenticationModeComboBox.SelectedItem =
            HotSpotAuthenticationModeComboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item =>
                    item.Tag is HotSpotAuthenticationMode mode &&
                    mode == settings.AuthenticationMode);

        HotSpotRadiusAccountingCheckBox.IsChecked =
            settings.RadiusAccounting;

        HotSpotRadiusInterimUpdateTextBox.Text =
            settings.RadiusInterimUpdateSeconds.ToString();

        HotSpotUseCookieCheckBox.IsChecked = settings.UseCookie;
        HotSpotCookieTimeoutTextBox.Text =
            settings.CookieTimeoutMinutes.ToString();

        HotSpotClientIdleTimeoutTextBox.Text =
            settings.ClientIdleTimeoutSeconds.ToString();

        HotSpotKeepaliveTimeoutTextBox.Text =
            settings.KeepaliveTimeoutSeconds.ToString();

        HotSpotCommentTextBox.Text = settings.Comment;

        HotSpotRouterComboBox.ItemsSource = null;
        HotSpotRouterComboBox.ItemsSource = _settings.Routers;
        HotSpotRouterComboBox.DisplayMemberPath =
            nameof(RouterConnectionProfile.Name);
        HotSpotRouterComboBox.SelectedValuePath =
            nameof(RouterConnectionProfile.Id);
        HotSpotRouterComboBox.SelectedValue = settings.RouterId;

        HotSpotLoginMethodsListBox.SelectedItems.Clear();

        foreach (var option in HotSpotLoginMethodOptions.Where(
                     option => settings.LoginMethods.Contains(option.Key)))
        {
            HotSpotLoginMethodsListBox.SelectedItems.Add(option);
        }

        HotSpotProfilesListBox.ItemsSource = null;
        HotSpotProfilesListBox.ItemsSource = settings.Profiles;

        var profile = settings.Profiles.FirstOrDefault(
                          item => item.Id == _selectedHotSpotProfileId)
                      ?? settings.Profiles.FirstOrDefault();

        _selectedHotSpotProfileId = profile?.Id;
        HotSpotProfilesListBox.SelectedItem = profile;

        PopulateHotSpotProfileEditor(profile);

        HotSpotStatusSummaryTextBlock.Text =
            settings.IsEnabled ? "مفعّلة في النموذج" : "غير مفعّلة";

        HotSpotProfileCountTextBlock.Text =
            settings.Profiles.Count.ToString("N0");

        HotSpotBindingCountTextBlock.Text =
            HotSpotPackageBindingsTextBlock.Text;

        HotSpotPackageBindingSummaryTextBlock.Text =
            HotSpotPackageBindingsTextBlock.Text;

        HotSpotPortalTitleTextBox.Text = settings.PortalTitle;
        HotSpotPortalWelcomeTextBox.Text =
            settings.PortalWelcomeMessage;

        UpdateHotSpotPortalPreview();
    }
    finally
    {
        _isLoadingHotSpotControls = false;
    }
}
private List<string> GetSelectedHotSpotLoginMethods(ListBox listBox)
{
    return listBox.SelectedItems
        .OfType<HotSpotLoginMethodOption>()
        .Select(option => option.Key)
        .ToList();
}
private void SaveHotSpotSettingsButton_Click(
    object sender,
    RoutedEventArgs e)
{
    var settings = _settings.HotSpot;

    var serverName = HotSpotServerNameTextBox.Text.Trim();
    var serverProfileName =
        HotSpotServerProfileNameTextBox.Text.Trim();

    if (HotSpotEnabledCheckBox.IsChecked == true &&
        (string.IsNullOrWhiteSpace(serverName) ||
         string.IsNullOrWhiteSpace(serverProfileName)))
    {
        HotSpotSettingsStatusTextBlock.Text =
            "أدخل اسم خادم HotSpot واسم بروفايل الخادم.";
        return;
    }

    if (!int.TryParse(
            HotSpotRadiusInterimUpdateTextBox.Text,
            out var interimUpdate) ||
        interimUpdate < 0)
    {
        HotSpotSettingsStatusTextBlock.Text =
            "فترة تحديث RADIUS يجب أن تكون صفرًا أو أكبر.";
        return;
    }

    if (!int.TryParse(
            HotSpotCookieTimeoutTextBox.Text,
            out var cookieTimeout) ||
        cookieTimeout < 1)
    {
        HotSpotSettingsStatusTextBlock.Text =
            "مدة Cookie يجب أن تكون دقيقة واحدة على الأقل.";
        return;
    }

    if (!int.TryParse(
            HotSpotClientIdleTimeoutTextBox.Text,
            out var idleTimeout) ||
        idleTimeout < 0)
    {
        HotSpotSettingsStatusTextBlock.Text =
            "مدة خمول العميل يجب أن تكون صفرًا أو أكبر.";
        return;
    }

    if (!int.TryParse(
            HotSpotKeepaliveTimeoutTextBox.Text,
            out var keepaliveTimeout) ||
        keepaliveTimeout < 0)
    {
        HotSpotSettingsStatusTextBlock.Text =
            "مدة Keepalive يجب أن تكون صفرًا أو أكبر.";
        return;
    }

    if (HotSpotAuthenticationModeComboBox.SelectedItem
        is not ComboBoxItem
        {
            Tag: HotSpotAuthenticationMode mode
        })
    {
        HotSpotSettingsStatusTextBlock.Text =
            "اختر مصدر المصادقة.";
        return;
    }

    var selectedMethods =
        GetSelectedHotSpotLoginMethods(HotSpotLoginMethodsListBox);

    if (HotSpotEnabledCheckBox.IsChecked == true &&
        selectedMethods.Count == 0)
    {
        HotSpotSettingsStatusTextBlock.Text =
            "اختر طريقة دخول واحدة على الأقل.";
        return;
    }

    settings.IsEnabled =
        HotSpotEnabledCheckBox.IsChecked == true;

    settings.ServerName = serverName;
    settings.ServerProfileName = serverProfileName;
    settings.RouterId = HotSpotRouterComboBox.SelectedValue as string;

    settings.InterfaceName = HotSpotInterfaceNameTextBox.Text.Trim();
    settings.AddressPoolName = HotSpotAddressPoolNameTextBox.Text.Trim();
    settings.DnsName = HotSpotDnsNameTextBox.Text.Trim();

    settings.AuthenticationMode = mode;
    settings.UseRadius = mode == HotSpotAuthenticationMode.Radius;

    settings.LoginMethods = selectedMethods;

    settings.RadiusAccounting =
        HotSpotRadiusAccountingCheckBox.IsChecked == true;

    settings.RadiusInterimUpdateSeconds = interimUpdate;

    settings.UseCookie =
        HotSpotUseCookieCheckBox.IsChecked == true;

    settings.CookieTimeoutMinutes = cookieTimeout;
    settings.ClientIdleTimeoutSeconds = idleTimeout;
    settings.KeepaliveTimeoutSeconds = keepaliveTimeout;

    settings.PortalTitle = HotSpotPortalTitleTextBox.Text.Trim();
    settings.PortalWelcomeMessage =
        HotSpotPortalWelcomeTextBox.Text.Trim();

    settings.Comment = HotSpotCommentTextBox.Text.Trim();

    try
    {
        _settingsStore.Save(_settings);

        LoadHotSpotSettingsControls();

        HotSpotSettingsStatusTextBlock.Text =
            "تم حفظ إعدادات HotSpot محليًا. لم يتم تعديل أي راوتر.";
    }
    catch (Exception ex)
    {
        HotSpotSettingsStatusTextBlock.Text =
            $"تعذر حفظ إعدادات HotSpot: {ex.Message}";
    }
}
private void ReloadHotSpotSettingsButton_Click(
    object sender,
    RoutedEventArgs e)
{
    try
    {
        _settings = _settingsStore.Load();

        RouterConnectionsListBox.ItemsSource = null;
        RouterConnectionsListBox.ItemsSource = _settings.Routers;

        LoadUserManagerSettingsControls();
        LoadHotSpotSettingsControls();

        HotSpotSettingsStatusTextBlock.Text =
            "تمت إعادة تحميل إعدادات HotSpot المحفوظة.";
    }
    catch (Exception ex)
    {
        HotSpotSettingsStatusTextBlock.Text =
            $"تعذر تحميل إعدادات HotSpot: {ex.Message}";
    }
}
private void PopulateHotSpotProfileEditor(
    HotSpotProfileSettings? profile)
{
    HotSpotProfileNameTextBox.Text = profile?.Name ?? string.Empty;

    HotSpotProfileEnabledCheckBox.IsChecked =
        profile?.IsEnabled ?? true;

    HotSpotProfileUseRadiusCheckBox.IsChecked =
        profile?.UseRadius ?? false;

    HotSpotProfileRadiusAccountingCheckBox.IsChecked =
        profile?.RadiusAccounting ?? true;

    HotSpotProfileUseCookieCheckBox.IsChecked =
        profile?.UseCookie ?? true;

    HotSpotProfileInterimUpdateTextBox.Text =
        (profile?.RadiusInterimUpdateSeconds ?? 300).ToString();

    HotSpotProfileSharedUsersTextBox.Text =
        (profile?.SharedUsers ?? 1).ToString();

    HotSpotProfileCookieTimeoutTextBox.Text =
        (profile?.CookieTimeoutMinutes ?? 1440).ToString();

    HotSpotProfileIdleTimeoutTextBox.Text =
        (profile?.ClientIdleTimeoutSeconds ?? 300).ToString();

    HotSpotProfileKeepaliveTimeoutTextBox.Text =
        (profile?.KeepaliveTimeoutSeconds ?? 30).ToString();

    HotSpotProfileCommentTextBox.Text =
        profile?.Comment ?? string.Empty;

    HotSpotProfileLoginMethodsListBox.SelectedItems.Clear();

    foreach (var option in HotSpotLoginMethodOptions.Where(
                 option => profile?.LoginMethods.Contains(option.Key) == true))
    {
        HotSpotProfileLoginMethodsListBox.SelectedItems.Add(option);
    }
}
private void HotSpotProfilesListBox_SelectionChanged(
    object sender,
    SelectionChangedEventArgs e)
{
    if (_isLoadingHotSpotControls)
        return;

    if (HotSpotProfilesListBox.SelectedItem
        is HotSpotProfileSettings profile)
    {
        _selectedHotSpotProfileId = profile.Id;
        PopulateHotSpotProfileEditor(profile);
    }
    else
    {
        _selectedHotSpotProfileId = null;
        PopulateHotSpotProfileEditor(null);
    }
}
private void NewHotSpotProfileButton_Click(
    object sender,
    RoutedEventArgs e)
{
    _selectedHotSpotProfileId = null;
    HotSpotProfilesListBox.SelectedItem = null;

    PopulateHotSpotProfileEditor(null);
    HotSpotProfileNameTextBox.Clear();

    HotSpotProfileStatusTextBlock.Text =
        "أدخل بيانات البروفايل الجديد ثم احفظه.";
}
private void SaveHotSpotProfileButton_Click(
    object sender,
    RoutedEventArgs e)
{
    var name = HotSpotProfileNameTextBox.Text.Trim();

    if (string.IsNullOrWhiteSpace(name))
    {
        HotSpotProfileStatusTextBlock.Text =
            "أدخل اسم البروفايل.";
        return;
    }

    if (!int.TryParse(
            HotSpotProfileInterimUpdateTextBox.Text,
            out var interimUpdate) ||
        interimUpdate < 0)
    {
        HotSpotProfileStatusTextBlock.Text =
            "فترة تحديث RADIUS يجب أن تكون صفرًا أو أكبر.";
        return;
    }

    if (!int.TryParse(
            HotSpotProfileSharedUsersTextBox.Text,
            out var sharedUsers) ||
        sharedUsers < 1)
    {
        HotSpotProfileStatusTextBlock.Text =
            "عدد المستخدمين المتزامنين يجب أن يكون واحدًا أو أكثر.";
        return;
    }

    if (!int.TryParse(
            HotSpotProfileCookieTimeoutTextBox.Text,
            out var cookieTimeout) ||
        cookieTimeout < 1)
    {
        HotSpotProfileStatusTextBlock.Text =
            "مدة Cookie يجب أن تكون دقيقة واحدة على الأقل.";
        return;
    }

    if (!int.TryParse(
            HotSpotProfileIdleTimeoutTextBox.Text,
            out var idleTimeout) ||
        idleTimeout < 0)
    {
        HotSpotProfileStatusTextBlock.Text =
            "مدة خمول العميل يجب أن تكون صفرًا أو أكبر.";
        return;
    }

    if (!int.TryParse(
            HotSpotProfileKeepaliveTimeoutTextBox.Text,
            out var keepaliveTimeout) ||
        keepaliveTimeout < 0)
    {
        HotSpotProfileStatusTextBlock.Text =
            "مدة Keepalive يجب أن تكون صفرًا أو أكبر.";
        return;
    }

    var profile = _settings.HotSpot.Profiles.FirstOrDefault(
        item => item.Id == _selectedHotSpotProfileId);

    var isNew = profile is null;
    profile ??= new HotSpotProfileSettings();

    profile.Name = name;
    profile.IsEnabled =
        HotSpotProfileEnabledCheckBox.IsChecked == true;

    profile.UseRadius =
        HotSpotProfileUseRadiusCheckBox.IsChecked == true;

    profile.RadiusAccounting =
        HotSpotProfileRadiusAccountingCheckBox.IsChecked == true;

    profile.UseCookie =
        HotSpotProfileUseCookieCheckBox.IsChecked == true;

    profile.RadiusInterimUpdateSeconds = interimUpdate;
    profile.SharedUsers = sharedUsers;
    profile.CookieTimeoutMinutes = cookieTimeout;
    profile.ClientIdleTimeoutSeconds = idleTimeout;
    profile.KeepaliveTimeoutSeconds = keepaliveTimeout;

    profile.LoginMethods =
        GetSelectedHotSpotLoginMethods(
            HotSpotProfileLoginMethodsListBox);

    profile.Comment = HotSpotProfileCommentTextBox.Text.Trim();

    if (profile.IsEnabled && profile.LoginMethods.Count == 0)
    {
        HotSpotProfileStatusTextBlock.Text =
            "اختر طريقة دخول واحدة على الأقل للبروفايل المفعّل.";
        return;
    }

    try
    {
        if (isNew)
            _settings.HotSpot.Profiles.Add(profile);

        _settingsStore.Save(_settings);

        _selectedHotSpotProfileId = profile.Id;
        LoadHotSpotSettingsControls();

        HotSpotProfileStatusTextBlock.Text =
            $"تم حفظ البروفايل «{profile.Name}» محليًا.";
    }
    catch (Exception ex)
    {
        if (isNew)
            _settings.HotSpot.Profiles.Remove(profile);

        HotSpotProfileStatusTextBlock.Text =
            $"تعذر حفظ البروفايل: {ex.Message}";
    }
}
private void DeleteHotSpotProfileButton_Click(
    object sender,
    RoutedEventArgs e)
{
    var profile = _settings.HotSpot.Profiles.FirstOrDefault(
        item => item.Id == _selectedHotSpotProfileId);

    if (profile is null)
    {
        HotSpotProfileStatusTextBlock.Text =
            "اختر بروفايلًا لحذفه.";
        return;
    }

    var originalIndex =
        _settings.HotSpot.Profiles.IndexOf(profile);

    _settings.HotSpot.Profiles.Remove(profile);

    try
    {
        _settingsStore.Save(_settings);

        _selectedHotSpotProfileId = null;
        LoadHotSpotSettingsControls();

        HotSpotProfileStatusTextBlock.Text =
            $"تم حذف البروفايل «{profile.Name}» من الديمو.";
    }
    catch (Exception ex)
    {
        _settings.HotSpot.Profiles.Insert(
            Math.Clamp(
                originalIndex,
                0,
                _settings.HotSpot.Profiles.Count),
            profile);

        LoadHotSpotSettingsControls();

        HotSpotProfileStatusTextBlock.Text =
            $"تعذر حذف البروفايل: {ex.Message}";
    }
}
private void UpdateHotSpotPortalPreview()
{
    HotSpotPortalPreviewTitleTextBlock.Text =
        string.IsNullOrWhiteSpace(HotSpotPortalTitleTextBox.Text)
            ? "مرحبًا بك في الشبكة"
            : HotSpotPortalTitleTextBox.Text.Trim();

    HotSpotPortalPreviewMessageTextBlock.Text =
        string.IsNullOrWhiteSpace(HotSpotPortalWelcomeTextBox.Text)
            ? "أدخل بيانات كرت الإنترنت للاتصال."
            : HotSpotPortalWelcomeTextBox.Text.Trim();
}
private void PreviewHotSpotPortalButton_Click(
    object sender,
    RoutedEventArgs e)
{
    UpdateHotSpotPortalPreview();

    HotSpotSettingsStatusTextBlock.Text =
        "تم تحديث معاينة بوابة الدخول. احفظ الإعدادات للاحتفاظ بالنصوص.";
}
private void RefreshHotSpotDiagnosticsButton_Click(
    object sender,
    RoutedEventArgs e)
{
    var settings = _settings.HotSpot;
    var messages = new List<string>();

    if (!settings.IsEnabled)
        messages.Add("خدمة HotSpot معطّلة في النموذج.");
    else
        messages.Add("خدمة HotSpot مفعّلة في النموذج.");

    if (string.IsNullOrWhiteSpace(settings.ServerName))
        messages.Add("اسم الخادم غير محدد.");

    if (string.IsNullOrWhiteSpace(settings.ServerProfileName))
        messages.Add("بروفايل الخادم غير محدد.");

    var router = _settings.Routers.FirstOrDefault(
        item => item.Id == settings.RouterId);

    if (router is null)
        messages.Add("لم يتم ربط HotSpot بملف راوتر محفوظ.");
    else if (!router.IsEnabled)
        messages.Add($"ملف الراوتر «{router.Name}» معطّل.");
    else
        messages.Add($"ملف الراوتر المحدد: {router.Name}. لم يُختبر الاتصال.");

    if (settings.AuthenticationMode == HotSpotAuthenticationMode.Radius &&
        !_settings.UserManager.IsEnabled)
    {
        messages.Add(
            "تنبيه: المصادقة محددة عبر RADIUS، لكن User Manager غير مفعّل في إعدادات الديمو.");
    }

    if (settings.LoginMethods.Count == 0)
        messages.Add("لم تُحدد طرق الدخول.");

    if (!settings.Profiles.Any(profile => profile.IsEnabled))
        messages.Add("لا يوجد بروفايل HotSpot مفعّل في النموذج.");

    messages.Add(
        "هذه مراجعة محلية للإعدادات وليست اختبار اتصال أو تحققًا من جهاز حقيقي.");

    HotSpotDiagnosticsStatusTextBlock.Text =
        string.Join(Environment.NewLine, messages);
}
}
