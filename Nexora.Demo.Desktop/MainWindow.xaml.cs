using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;

using System.Globalization;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Cards.Views;
using Nexora.Demo.Desktop.Features.Printing.Views;
using Nexora.Demo.Desktop.Features.Sales.Services;
using Nexora.Demo.Desktop.Features.Sales.Views;
using Nexora.Demo.Desktop.Features.Shared;
using Nexora.Demo.Desktop.Features.Reports.Views;
using Nexora.Demo.Desktop.Features.Networks.Views;

using Nexora.Demo.Desktop.Features.NetworkOperations.Views;
using Nexora.Demo.Desktop.Features.NetworkOperations.Models;
using Nexora.Demo.Desktop.Features.Settings.Views;
using Nexora.Demo.Desktop.Features.Settings.Services;
using Nexora.Demo.Desktop.Features.Packages.Views;
using Nexora.Demo.Desktop.Features.NetworkOperations.Connections;
using System;
using System.Windows.Documents;
namespace Nexora.Demo.Desktop;

public partial class MainWindow : Window
{
    private bool _isSidebarCollapsed;

    private const string DemoNetworkCode = "23400";
    private const string DemoOwnerPhone = "777000001";
    private const string DemoPassword = "Nexora@123";
    private const string DemoTrialPhone = "777000202";
    private const string DemoTrialPassword = "Trial@123";
    private const string NetworkRegistrationUrl = "https://kurootalshabakah.tabweeb.com.ye/referral/form";
    private readonly Dictionary<string, string[]> _directorates = new(StringComparer.Ordinal)
    {
        ["أمانة العاصمة"] = ["التحرير", "معين", "الوحدة", "السبعين", "شعوب"],
        ["صنعاء"] = ["سنحان وبني بهلول", "بني حشيش", "همدان", "أرحب", "الحيمة الداخلية"],
        ["عدن"] = ["المنصورة", "خور مكسر", "المعلا", "التواهي", "دار سعد"],
        ["تعز"] = ["القاهرة", "المظفر", "صالة", "المعافر", "التعزية"]
    };
    private readonly Dictionary<string, string[]> _areas = new(StringComparer.Ordinal)
    {
        ["التحرير"] = ["باب اليمن", "شارع جمال", "ميدان التحرير"], ["معين"] = ["حدة", "شميلة", "الستين"], ["الوحدة"] = ["الزبيري", "شارع بغداد"], ["السبعين"] = ["حي السبعين", "جولة المصباحي"], ["شعوب"] = ["الجراف", "الروضة"],
        ["سنحان وبني بهلول"] = ["سنحان", "بني بهلول"], ["بني حشيش"] = ["صرف", "بيت نعم"], ["همدان"] = ["ضلاع همدان", "الجائف"], ["أرحب"] = ["ريدة", "شعبان"], ["الحيمة الداخلية"] = ["المناخ", "الجميمة"],
        ["المنصورة"] = ["الشيخ عثمان الحدودي", "عبدالعزيز"], ["خور مكسر"] = ["العريش", "السعادة"], ["المعلا"] = ["الشارع الرئيسي", "حافون"], ["التواهي"] = ["القلوعة", "البنجسار"], ["دار سعد"] = ["بئر فضل", "دار سعد المركز"],
        ["القاهرة"] = ["جولة سنان", "شارع التحرير"], ["المظفر"] = ["باب موسى", "المدينة القديمة"], ["صالة"] = ["صالة", "الجحملية"], ["المعافر"] = ["النشمة", "الأعلوم"], ["التعزية"] = ["الحوبان", "الكمب"]
    };
    private readonly Dictionary<string, string[]> _sectionTabs = new(StringComparer.Ordinal)
    {
        ["network-users"] = ["كل الحسابات", "النشطون الآن", "غير النشطين"],        ["cards"] = ["إصدار الكروت", "إدارة الكروت", "إدارة الدفعات", "المعلقات", "استيراد الكروت"],
        ["packages"] = ["كتالوج الباقات"],
        ["sales"] = ["لوحة المبيعات", "تسجيل البيع", "حركة الكروت", "حسابات نقاط البيع", "سجل العمليات", "تقارير المبيعات", "نقاط البيع"],
        ["printing"] = ["تصميم القالب", "قائمة الطباعة"],
        ["networks"] = ["مدير المستخدمين", "الهوتسبوت", ],
        ["reports"] = ["مركز التقارير"],
        ["network-center"] = ["نظرة عامة", "المواقع والأجهزة", "المتصلون", "المراقبة", "الصيانة", "التحليلات"],
        ["settings"] = ["إعدادات النظام", "الشاشات والمظهر", "الموقع والهوية", "النسخ الاحتياطي"]
    };
    private string _currentSection = "dashboard";
    private DemoAccountSystem _selectedCardSystem = DemoAccountSystem.HotSpot;
    private readonly DemoCardIssuanceService _demoCardIssuanceService = new();
    private readonly DemoSalesService _demoSalesService = new();
    private readonly SalesOperationsService _salesOperationsService = new();
    private readonly SalesPointDirectoryService _salesPointDirectoryService;
    private readonly Dictionary<string, UserControl> _featureViewCache = new(StringComparer.Ordinal);
    private string _sectionBeforeLocationEdit = "dashboard";
    private string _tabBeforeLocationEdit = "إعدادات النظام";
    private bool _locationEditWasOnboarding;
    private string _networkDisplayName = "مساحة الشبكة";
    private string? _createdTrialPhone;
    private string? _createdTrialPassword;
    private string? _createdTrialNetworkName;
    private bool _locationSaved;
    private string? _savedLocationSummary;
    private string? _currentAccountKey;
    private bool _locationRequiredForCurrentAccount;
    private RouterConnectionProfile? _savedRouterProfile;
    private bool _routerConnectionRequestInProgress;

    public MainWindow()
    {
        InitializeComponent();
        _salesPointDirectoryService = new SalesPointDirectoryService(_demoSalesService.GetAll().Select(row => row.PointOfSale));
        StateChanged += (_, _) => UpdateWorkspaceWindowControls();
        ApplyShellDisplayPreferences();
        SystemPasswordBox.Password = DemoPassword;
        TrialPasswordBox.Password = DemoTrialPassword;
        GovernorateComboBox.ItemsSource = _directorates.Keys.OrderBy(x => x).ToList();
        GovernorateComboBox.SelectedIndex = -1;
        _demoCardIssuanceService.ScenarioChanged += (_, _) => UpdateDashboardMetrics();
        UpdateDashboardMetrics();
        if (!string.IsNullOrWhiteSpace(_demoCardIssuanceService.StorageWarning))
            SetStatus(LocationStatusTextBlock, _demoCardIssuanceService.StorageWarning, true);
    }


    private enum DashboardSyncState
    {
        Unknown,
        Succeeded,
        Pending,
        Failed,
        Review
    }

    private sealed record DashboardBatchRow(
        string BatchId,
        string PackageName,
        int CardCount,
        string CreatedAtLabel,
        string StatusLabel,
        DateTime SortAt,
        int AttentionCardCount);

    private sealed record DashboardIssuanceDay(
        string DayLabel,
        string DateLabel,
        int Count,
        string CountLabel,
        double BarHeight);

    private sealed record DashboardActivityRow(
        string Title,
        string Description,
        string TimeLabel,
        string Glyph,
        DateTime SortAt);


    private void SetStatus(TextBlock target, string message, bool error) { target.Text = message; target.Foreground = (Brush)FindResource(error ? "DangerBrush" : "SuccessBrush"); }
    private sealed record SavedLocation(string Governorate, string Directorate, string Area, string Landmark, DateTimeOffset SavedAt);


}
