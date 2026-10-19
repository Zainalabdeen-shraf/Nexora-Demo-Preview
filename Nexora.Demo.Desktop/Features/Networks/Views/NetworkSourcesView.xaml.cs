using System.Windows;
using System.Windows.Controls;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Shared;
using Nexora.Demo.Desktop.Features.Packages.Services;
using Nexora.Demo.Desktop.Features.Networks.Models;
using Nexora.Demo.Desktop.Features.Networks.Services;

namespace Nexora.Demo.Desktop.Features.Networks.Views;

/// <summary>Source-aware navigation page. It does not imply an active RouterOS connection.</summary>
public partial class NetworkSourcesView : UserControl
{
    private readonly string _area;
    private readonly DemoCardIssuanceService _issuanceService;

    private readonly NetworkSourceSettingsStore _settingsStore = new();
    private NetworkSourceSettingsSnapshot _settings = new();
    private string? _selectedRouterId;
    private bool _isLoadingRouterSettings;

    private bool _isLoadingUserManagerControls;
    private string? _selectedUserManagerProfileId;
    private string? _selectedUserManagerClientId;

    private bool _isLoadingHotSpotControls;
    private string? _selectedHotSpotProfileId;




    private static readonly HotSpotLoginMethodOption[]
        HotSpotLoginMethodOptions =
    [
        new("http-chap", "CHAP عبر HTTP"),
        new("http-pap", "كلمة المرور عبر HTTP"),
        new("https", "HTTPS"),
        new("cookie", "Cookie"),
        new("mac", "مصادقة MAC"),
        new("mac-cookie", "MAC Cookie"),
        new("trial", "دخول تجريبي")
    ];

    private sealed record HotSpotLoginMethodOption(
        string Key,
        string Label);


    public NetworkSourcesView(string area, DemoCardIssuanceService issuanceService)
    {
        InitializeComponent();
        RouterMethodComboBox.ItemsSource =
        Enum.GetValues<RouterConnectionMethod>();
        UserManagerClientProtocolComboBox.ItemsSource =
        Enum.GetValues<RadiusTransportProtocol>();

        UserManagerClientProtocolComboBox.SelectedItem =
        RadiusTransportProtocol.Udp;
        RouterMethodComboBox.SelectedItem =
        RouterConnectionMethod.RouterOsApiSsl;
        _area = area;
        _issuanceService = issuanceService;
        RefreshSummary();
        HotSpotAuthenticationModeComboBox.Items.Add(
    new ComboBoxItem
    {
        Content = "حسابات HotSpot المحلية",
        Tag = HotSpotAuthenticationMode.LocalUsers
    });

    HotSpotAuthenticationModeComboBox.Items.Add(
        new ComboBoxItem
        {
            Content = "المصادقة عبر User Manager / RADIUS",
            Tag = HotSpotAuthenticationMode.Radius
        });

    HotSpotLoginMethodsListBox.ItemsSource =
        HotSpotLoginMethodOptions;

    HotSpotLoginMethodsListBox.DisplayMemberPath =
        nameof(HotSpotLoginMethodOption.Label);

    HotSpotProfileLoginMethodsListBox.ItemsSource =
        HotSpotLoginMethodOptions;

    HotSpotProfileLoginMethodsListBox.DisplayMemberPath =
        nameof(HotSpotLoginMethodOption.Label);
        ConfigureArea();
        LoadRouterSettings();
    }

    public event EventHandler<FeatureNavigationRequestedEventArgs>? NavigationRequested;

    private void RefreshSummary()
    {
        HotSpotAccountsTextBlock.Text = _issuanceService.Cards.Count(card => card.TargetSystem.Contains("هوتسبوت", StringComparison.OrdinalIgnoreCase)).ToString("N0");
        UserManagerAccountsTextBlock.Text = _issuanceService.Cards.Count(card => card.TargetSystem.Contains("مدير المستخدمين", StringComparison.OrdinalIgnoreCase)).ToString("N0");
        try
        {
            var catalog = DemoPackageStore.Load();
            HotSpotPackageBindingsTextBlock.Text = catalog.Bindings.Count(binding => binding.ProviderKey == "hotspot" && binding.IsEnabled).ToString("N0");
            UserManagerPackageBindingsTextBlock.Text = catalog.Bindings.Count(binding => binding.ProviderKey == "user-manager" && binding.IsEnabled).ToString("N0");
        }
        catch
        {
            HotSpotPackageBindingsTextBlock.Text = "—";
            UserManagerPackageBindingsTextBlock.Text = "—";
        }
    }

    private void ConfigureArea()
    {
        RouterSettingsPanel.Visibility = Visibility.Collapsed;
        UserManagerSettingsPanel.Visibility = Visibility.Collapsed;
        HotSpotSettingsPanel.Visibility = Visibility.Collapsed;

        if (_area == "hotspot")
                {
                NetworkPageTitleTextBlock.Text = "HotSpot";
                NetworkPageSubtitleTextBlock.Text = "إعدادات مصدر المصادقة الخاص بـ HotSpot، مع إدارة الحسابات في مساحة مستخدمي الشبكة الموحدة.";
                UserManagerCard.Visibility = Visibility.Collapsed;
                Grid.SetColumn(HotSpotCard, 0);
                HotSpotCard.SetValue(Grid.ColumnSpanProperty, 3);
                ProviderDetailPanel.Visibility = Visibility.Visible;
                ProviderDetailTitleTextBlock.Text = "إعداد HotSpot";
                ProviderDetailDescriptionTextBlock.Text = "يُحدد إعداد هذا المصدر ما إذا كانت الحسابات محلية في HotSpot أو تستخدم RADIUS. اختر الباقة المتوافقة مع طريقة المصادقة المستخدمة.";
                ProviderCardsGrid.Visibility = Visibility.Collapsed;
                ProviderDetailPanel.Visibility = Visibility.Collapsed;
                HotSpotSettingsPanel.Visibility = Visibility.Visible;
            }
            else if (_area == "user-manager")
            {
                NetworkPageTitleTextBlock.Text = "User Manager";
                NetworkPageSubtitleTextBlock.Text = "إعدادات المصدر المسؤول عن الحسابات وملفات الاستخدام وقيود RADIUS.";
                HotSpotCard.Visibility = Visibility.Collapsed;
                Grid.SetColumn(UserManagerCard, 0);
                UserManagerCard.SetValue(Grid.ColumnSpanProperty, 3);
                ProviderDetailPanel.Visibility = Visibility.Visible;
                ProviderDetailTitleTextBlock.Text = "إعداد User Manager";
                ProviderDetailDescriptionTextBlock.Text = "تكوين الباقة مرتبط بملفات المستخدم والحدود والمجموعات المتوافقة. تظهر الحسابات في مساحة المستخدمين الموحدة مع علامة المصدر.";
                ProviderCardsGrid.Visibility = Visibility.Collapsed;
                ProviderDetailPanel.Visibility = Visibility.Collapsed;

                NetworkPageTitleTextBlock.Text = "مدير المستخدمين";
                NetworkPageSubtitleTextBlock.Text =
                    "إدارة إعدادات الخدمة العامة ومنافذ RADIUS.";

                UserManagerSettingsPanel.Visibility = Visibility.Visible;
            }
            else
            {
                NetworkPageTitleTextBlock.Text = "الشبكات ومصادر المصادقة";
                NetworkPageSubtitleTextBlock.Text = "افصل طريقة إدارة الحسابات حسب المصدر، مع مشاركة واجهة إدارة المستخدمين والإصدار والتقارير.";
                HotSpotCard.Visibility = Visibility.Visible;
                UserManagerCard.Visibility = Visibility.Visible;
                ProviderDetailPanel.Visibility = Visibility.Collapsed;
            }
        }

    private void IssueForSystemButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string systemKey }) return;
        NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("cards", "إصدار الكروت", null, null, systemKey));
    }

    private void HotSpotPackagesButton_Click(object sender, RoutedEventArgs e) =>
        NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("packages", "باقات HotSpot"));

    private void UserManagerPackagesButton_Click(object sender, RoutedEventArgs e) =>
        NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("packages", "باقات User Manager"));

    private void OpenUsersButton_Click(object sender, RoutedEventArgs e) =>
        NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("network-users", _area == "hotspot" ? "مستخدمو HotSpot" : "مستخدمو User Manager"));

    private void OpenIssuanceButton_Click(object sender, RoutedEventArgs e) =>
        NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("cards", "إصدار الكروت", null, null, _area));

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
