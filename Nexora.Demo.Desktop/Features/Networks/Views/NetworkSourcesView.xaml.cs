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


}
