using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;

namespace Nexora.Demo.Desktop.Features.Cards.Views;

/// <summary>
/// Dedicated network-user screen. It projects persisted local card records without inventing router telemetry;
/// RouterOS session fields remain unavailable until a live read provider supplies them.
/// </summary>
public partial class NetworkUsersView : UserControl
{
    private readonly DemoCardIssuanceService _issuanceService;
    private readonly ObservableCollection<NetworkUserDisplayRow> _rows = new();
    private readonly ListCollectionView _rowsView;
    private bool _viewReady;
    private bool _refreshingFilterOptions;
    private bool _isSubscribed;
    private string _activityFilter = "all";
    private string _systemKey = "user-manager";

    // Temporary state for the presentation demo only. Nothing is written to the issuance service,
    // production storage, or RouterOS.
    private readonly Dictionary<string, DemoCardState> _demoStates = new(StringComparer.OrdinalIgnoreCase);

    private static readonly DemoPackageOption[] DemoPackageOptions =
    {
        new("6 ساعات — 300 ريال", 300m, TimeSpan.FromHours(6), "6 ساعات تقريبًا"),
        new("24 ساعة — 500 ريال", 500m, TimeSpan.FromHours(24), "24 ساعة تقريبًا"),
        new("أسبوع — 1,500 ريال", 1500m, TimeSpan.FromDays(7), "7 أيام تقريبًا"),
        new("شهر — 2,500 ريال", 2500m, TimeSpan.FromDays(30), "30 يومًا تقريبًا")
    };

    private static readonly IReadOnlyDictionary<string, DemoSessionData> DemoSessions =
        new Dictionary<string, DemoSessionData>(StringComparer.OrdinalIgnoreCase)
        {
            ["26001001"] = new(
                "مفعل", "متصل الآن", "192.168.10.5", "منذ 3 دقائق", "4C:5E:0C:12:34:56",
                "01:24:16", "482.1 MB", "125.4 MB", "12.8 GB", "3.4 GB",
                "2026/10/06 07:34", "9 ساعة و 12 دقيقة", 44, 47),
            ["26001003"] = new(
                "غير متصل", "غير متصل", "—", "منذ 2 ساعة", "غير متاح",
                "غير متاحة", "—", "—", "—", "—", "غير متاح", "غير متاح", 0, 0),
            ["26001004"] = new(
                "موقوف", "غير متصل", "—", "منذ 5 ساعات", "غير متاح",
                "غير متاحة", "—", "—", "—", "—", "غير متاح", "غير متاح", 0, 0),
            ["26001005"] = new(
                "مفعل", "متصل الآن", "192.168.20.12", "منذ دقيقة", "غير متاح",
                "غير متاحة", "—", "—", "—", "—", "غير متاح", "غير متاح", 0, 0),
            ["26001006"] = new(
                "منتهي", "غير متصل", "—", "منذ يوم", "غير متاح",
                "غير متاحة", "—", "—", "—", "—", "غير متاح", "انتهت الصلاحية", 0, 0),

            // Separate User Manager examples: account status is known, but HotSpot-style
            // session telemetry (IP/MAC/traffic) is not reported by this demo for these rows.
            ["26001009"] = new(
                "غير متصل", "غير متصل", "—", "غير متاح", "غير متاح",
                "غير متاحة", "—", "—", "—", "—", "غير متاح", "غير متاح", 0, 0),
            ["26001010"] = new(
                "مفعل", "غير متصل", "—", "غير متاح", "غير متاح",
                "غير متاحة", "—", "—", "—", "—", "غير متاح", "غير متاح", 0, 0),
            ["26001011"] = new(
                "منتهي", "غير متصل", "—", "غير متاح", "غير متاح",
                "غير متاحة", "—", "—", "—", "—", "غير متاح", "غير متاح", 0, 0)
        };

    public NetworkUsersView(DemoCardIssuanceService issuanceService, DemoAccountSystem targetSystem)
    {
        InitializeComponent();
        _issuanceService = issuanceService ?? throw new ArgumentNullException(nameof(issuanceService));
        _rowsView = new ListCollectionView(_rows) { Filter = MatchesRow };
        GeneratedCardsGrid.ItemsSource = _rowsView;

        // InitializeComponent may raise SelectionChanged from ComboBox items before the service is set.
        // The guard in FilterSelectionChanged prevents access to uninitialized fields during XAML loading.
        _viewReady = true;
        _systemKey = targetSystem == DemoAccountSystem.HotSpot ? "hotspot" : "user-manager";
        SelectSystemFilter(_systemKey);
        RefreshBatchFilterOptions();
        UpdateActivityButtonStyles();
        UpdateSearchPlaceholder();
        RebuildRows(selectFirst: true);
        Loaded += NetworkUsersView_Loaded;
        Unloaded += NetworkUsersView_Unloaded;
    }

    // Kept as a no-op compatibility event for the shared shell. The dedicated screen no longer exposes a router-open button.
    public event EventHandler? RouterConnectionRequested { add { } remove { } }


    private void NetworkUsersView_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_isSubscribed)
        {
            _issuanceService.ScenarioChanged += IssuanceService_ScenarioChanged;
            _isSubscribed = true;
        }
        RefreshBatchFilterOptions();
        RefreshRows(keepSelection: true);
        if (GeneratedCardsGrid.SelectedItem is null && GeneratedCardsGrid.Items.Count > 0)
            GeneratedCardsGrid.SelectedIndex = 0;
    }

    private void NetworkUsersView_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_isSubscribed)
        {
            _issuanceService.ScenarioChanged -= IssuanceService_ScenarioChanged;
            _isSubscribed = false;
        }
    }

    private void IssuanceService_ScenarioChanged(object? sender, EventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (IsLoaded)
            {
                RefreshBatchFilterOptions();
                RebuildRows(selectFirst: GeneratedCardsGrid.SelectedItem is null);
            }
        });
    }

    private sealed class DemoCardState
    {
        public string? AccountStatus { get; set; }
        public string? ConnectionStatus { get; set; }
        public string? IpAddress { get; set; }
        public string? LastSeen { get; set; }
        public string? MacAddress { get; set; }
        public string? SessionDuration { get; set; }
        public string? Download { get; set; }
        public string? Upload { get; set; }
        public string? TotalDownload { get; set; }
        public string? TotalUpload { get; set; }
        public string? PackageName { get; set; }
        public decimal? UnitPrice { get; set; }
        public string? PackageStartDate { get; set; }
        public string? EndDate { get; set; }
        public string? RemainingTime { get; set; }
        public double? PackageProgress { get; set; }
        public double? UsageProgress { get; set; }
        public string? DemoPassword { get; set; }
        public decimal AddedDataGb { get; set; }
        public bool IsDeleted { get; set; }
    }

    private sealed record DemoPackageOption(string Name, decimal Price, TimeSpan Duration, string RemainingLabel);
    private sealed record ValidityExtensionOption(string Label, TimeSpan Duration);
    private sealed record DemoEditResult(DemoPackageOption Package, string AccountStatus, TimeSpan ValidityExtension, decimal AdditionalDataGb, string Password);

    private sealed record DemoSessionData(
        string AccountStatus,
        string ConnectionStatus,
        string IpAddress,
        string LastSeen,
        string MacAddress,
        string SessionDuration,
        string Download,
        string Upload,
        string TotalDownload,
        string TotalUpload,
        string EndDate,
        string RemainingTime,
        double PackageProgress,
        double UsageProgress);
}

/// <summary>Presentation row wrapper for the existing demo-card model.</summary>
public sealed class NetworkUserDisplayRow
{
    public GeneratedDemoCard Card { get; }
    public string BatchId { get; }
    public string CardNumber { get; }
    public string SerialNumber { get; }
    public string BatchMarkerToolTip => $"فتح كروت الدفعة: {BatchId}";
    public string Username { get; }
    public string PasswordValue { get; }
    public string PasswordDisplay => string.IsNullOrWhiteSpace(PasswordValue) ? "بدون كلمة مرور" : PasswordValue;
    public string PackageName { get; }
    public string CreatedAtLabel { get; }
    public string PackageStartDate { get; }
    public decimal AddedDataGb { get; }
    public string AccountStatus { get; }
    public string StateToolTip { get; }
    public string ConnectionStatus { get; }
    public string IpAddress { get; }
    public string LastSeenLabel { get; }
    public string MacAddress { get; }
    public string SessionDuration { get; }
    public string Download { get; }
    public string Upload { get; }
    public string TotalDownload { get; }
    public string TotalUpload { get; }
    public string EndDate { get; }
    public string RemainingTime { get; }
    public double PackageProgress { get; }
    public double UsageProgress { get; }
    public bool IsConnected => ConnectionStatus == "متصل الآن";
    public bool HasKnownConnectionState { get; }

    public NetworkUserDisplayRow(
        GeneratedDemoCard card,
        string serialNumber,
        string accountStatus,
        string connectionStatus,
        string ipAddress,
        string lastSeenLabel,
        string macAddress,
        string sessionDuration,
        string download,
        string upload,
        string totalDownload,
        string totalUpload,
        string createdAtLabel,
        string endDate,
        string remainingTime,
        double packageProgress,
        double usageProgress,
        bool hasKnownConnectionState,
        string? passwordValue,
        string packageStartDate,
        decimal addedDataGb)
    {
        Card = card;
        BatchId = card.BatchId;
        CardNumber = card.CardNumber;
        SerialNumber = serialNumber;
        // In this product's network-card workflow, the RouterOS username is the card number.
        Username = card.Username;
        PasswordValue = passwordValue ?? string.Empty;
        PackageName = card.PackageName;
        CreatedAtLabel = createdAtLabel;
        PackageStartDate = packageStartDate;
        AddedDataGb = addedDataGb;
        AccountStatus = accountStatus;
        StateToolTip = $"حالة الحساب: {accountStatus}";
        ConnectionStatus = connectionStatus;
        IpAddress = ipAddress;
        LastSeenLabel = lastSeenLabel;
        MacAddress = macAddress;
        SessionDuration = sessionDuration;
        Download = download;
        Upload = upload;
        TotalDownload = totalDownload;
        TotalUpload = totalUpload;
        EndDate = endDate;
        RemainingTime = remainingTime;
        PackageProgress = packageProgress;
        UsageProgress = usageProgress;
        HasKnownConnectionState = hasKnownConnectionState;
    }
}
