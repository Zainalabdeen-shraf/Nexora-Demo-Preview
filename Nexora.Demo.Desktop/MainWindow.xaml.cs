using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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
using Nexora.Demo.Desktop.Features.Packages.Views;
namespace Nexora.Demo.Desktop;

public partial class MainWindow : Window
{
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
        ["network-users"] = ["كل الحسابات", "النشطون الآن", "غير النشطين", "مستخدمو HotSpot", "مستخدمو User Manager"],
        ["cards"] = ["إصدار الكروت", "إدارة الكروت", "إدارة الدفعات"],
        ["packages"] = ["كتالوج الباقات", "باقات HotSpot", "باقات User Manager"],
        ["sales"] = ["ملخص المبيعات", "معاملات البيع", "نقاط البيع"],
        ["printing"] = ["قائمة الطباعة"],
        ["networks"] = ["مصادر المصادقة", "HotSpot", "User Manager"],
        ["reports"] = ["مركز التقارير"],
        ["settings"] = ["إعدادات النظام", "الموقع والهوية", "النسخ الاحتياطي", "الصيانة"]
    };
    private string _currentSection = "dashboard";
    private readonly DemoCardIssuanceService _demoCardIssuanceService = new();
    private readonly DemoSalesService _demoSalesService = new();
    private readonly Dictionary<string, UserControl> _featureViewCache = new(StringComparer.Ordinal);
    private string _sectionBeforeLocationEdit = "dashboard";
    private string _networkDisplayName = "مساحة الشبكة";
    private string? _createdTrialPhone;
    private string? _createdTrialPassword;
    private string? _createdTrialNetworkName;
    private bool _locationSaved;
    private string? _savedLocationSummary;
    private string? _currentAccountKey;
    private bool _locationRequiredForCurrentAccount;

    public MainWindow()
    {
        InitializeComponent();
        SystemPasswordBox.Password = DemoPassword;
        TrialPasswordBox.Password = DemoTrialPassword;
        GovernorateComboBox.ItemsSource = _directorates.Keys.OrderBy(x => x).ToList();
        GovernorateComboBox.SelectedIndex = -1;
        _demoCardIssuanceService.ScenarioChanged += (_, _) => UpdateDashboardMetrics();
        UpdateDashboardMetrics();
        if (!string.IsNullOrWhiteSpace(_demoCardIssuanceService.StorageWarning))
            SetStatus(LocationStatusTextBlock, _demoCardIssuanceService.StorageWarning, true);
    }

    private void RegisteredModeButton_Click(object sender, RoutedEventArgs e) => SetLoginMode(false);
    private void TrialModeButton_Click(object sender, RoutedEventArgs e) => SetLoginMode(true);

    private void SetLoginMode(bool trial)
    {
        RegisteredLoginPanel.Visibility = trial ? Visibility.Collapsed : Visibility.Visible;
        TrialLoginPanel.Visibility = trial ? Visibility.Visible : Visibility.Collapsed;
        TrialRegistrationPanel.Visibility = Visibility.Collapsed;
        SetModeButton(RegisteredModeButton, !trial);
        SetModeButton(TrialModeButton, trial);
        LoginStatusTextBlock.Text = string.Empty;
        TrialLoginStatusTextBlock.Text = string.Empty;
        TrialCreateStatusTextBlock.Text = string.Empty;
    }

    private void SetModeButton(Button button, bool selected)
    {
        button.Background = (Brush)FindResource(selected ? "PrimaryBrush" : "SurfaceBrush");
        button.Foreground = (Brush)FindResource(selected ? "SurfaceBrush" : "TextBrush");
        button.BorderBrush = (Brush)FindResource(selected ? "PrimaryBrush" : "BorderBrushSoft");
    }

    private void SignInButton_Click(object sender, RoutedEventArgs e)
    {
        var code = NetworkCodeTextBox.Text.Trim();
        var phone = OwnerPhoneTextBox.Text.Trim();
        if (code != DemoNetworkCode || phone != DemoOwnerPhone || SystemPasswordBox.Password != DemoPassword)
        {
            SetStatus(LoginStatusTextBlock, "تعذر تسجيل الدخول. تحقق من كود الشبكة ورقم الهاتف وكلمة المرور.", true);
            return;
        }
        _networkDisplayName = "مساحة الشبكة";
        OpenWorkspace($"الشبكة المسجلة · {DemoNetworkCode}", $"registered|{code}|{phone}", requireLocation: false);
    }

    private void TrialSignInButton_Click(object sender, RoutedEventArgs e)
    {
        var phone = TrialPhoneNumberTextBox.Text.Trim();
        var password = TrialPasswordBox.Password;
        var builtIn = phone == DemoTrialPhone && password == DemoTrialPassword;
        var created = !string.IsNullOrWhiteSpace(_createdTrialPhone) && phone == _createdTrialPhone && password == _createdTrialPassword;
        if (!builtIn && !created)
        {
            SetStatus(TrialLoginStatusTextBlock, "رقم الهاتف أو كلمة المرور غير صحيحين.", true);
            return;
        }
        _networkDisplayName = created ? (_createdTrialNetworkName ?? "شبكة جديدة") : "شبكة التجربة";
        OpenWorkspace("حساب التجربة · " + phone, created ? $"trial-created|{phone}" : $"trial-default|{phone}", requireLocation: created);
    }

    private void CreateTrialAccountButton_Click(object sender, RoutedEventArgs e)
    {
        RegisteredLoginPanel.Visibility = Visibility.Collapsed;
        TrialLoginPanel.Visibility = Visibility.Collapsed;
        TrialRegistrationPanel.Visibility = Visibility.Visible;
        TrialCreateStatusTextBlock.Text = string.Empty;
        TrialNetworkNameTextBox.Text = string.Empty;
        TrialNewOwnerPhoneTextBox.Text = TrialPhoneNumberTextBox.Text;
        TrialNewPasswordBox.Password = string.Empty;
        TrialConfirmPasswordBox.Password = string.Empty;
    }

    private void SaveTrialAccountButton_Click(object sender, RoutedEventArgs e)
    {
        var name = TrialNetworkNameTextBox.Text.Trim();
        var phone = TrialNewOwnerPhoneTextBox.Text.Trim();
        var password = TrialNewPasswordBox.Password;
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(phone)) { SetStatus(TrialCreateStatusTextBlock, "اسم الشبكة ورقم الهاتف مطلوبان.", true); return; }
        if (password.Length < 6) { SetStatus(TrialCreateStatusTextBlock, "كلمة المرور يجب ألا تقل عن 6 أحرف.", true); return; }
        if (password != TrialConfirmPasswordBox.Password) { SetStatus(TrialCreateStatusTextBlock, "تأكيد كلمة المرور غير مطابق.", true); return; }
        _createdTrialNetworkName = name; _createdTrialPhone = phone; _createdTrialPassword = password;
        TrialPhoneNumberTextBox.Text = phone; TrialPasswordBox.Password = password;
        SetLoginMode(true);
        SetStatus(TrialLoginStatusTextBlock, "تم إنشاء الحساب. سجّل الدخول لإكمال إعداد مساحة شبكتك.", false);
    }

    private void CancelTrialAccountButton_Click(object sender, RoutedEventArgs e) => SetLoginMode(true);

    private void OpenRegistrationLinkButton_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo { FileName = NetworkRegistrationUrl, UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show("تعذر فتح رابط التسجيل. " + ex.Message, "Nexora", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void OpenWorkspace(string header, string accountKey, bool requireLocation)
    {
        _currentAccountKey = accountKey;
        _locationRequiredForCurrentAccount = requireLocation;
        LoadSavedLocation(accountKey);
        SidebarNetworkTextBlock.Text = _networkDisplayName;
        Title = "Nexora | إدارة شبكات الإنترنت";
        LoginView.Visibility = Visibility.Collapsed;
        WorkspaceView.Visibility = Visibility.Visible;
        MaxWidth = double.PositiveInfinity;
        MaxHeight = double.PositiveInfinity;
        var workArea = SystemParameters.WorkArea;
        MinWidth = Math.Min(1050, Math.Max(560, workArea.Width - 48));
        MinHeight = Math.Min(680, Math.Max(420, workArea.Height - 48));
        Width = Math.Min(1400, workArea.Width);
        Height = Math.Min(900, workArea.Height);
        WindowState = WindowState.Maximized;

        LocationBadgeTextBlock.Visibility = requireLocation ? Visibility.Visible : Visibility.Collapsed;
        LocationBadgeTextBlock.Text = _locationSaved ? (_savedLocationSummary ?? "الموقع مكتمل") : "أكمل موقع الشبكة";
        WorkspaceView.IsEnabled = !(requireLocation && !_locationSaved);
        LocationModalOverlay.Visibility = requireLocation && !_locationSaved ? Visibility.Visible : Visibility.Collapsed;
        CancelLocationEditButton.Visibility = Visibility.Collapsed;

        if (requireLocation && !_locationSaved)
        {
            _currentSection = "dashboard";
            PageTitleTextBlock.Text = "الرئيسية";
            PageSubtitleTextBlock.Text = "أكمل موقع شبكتك لبدء العمل";
            DashboardPage.Visibility = Visibility.Visible;
            SectionPage.Visibility = Visibility.Collapsed;
            LocationStatusTextBlock.Text = string.Empty;
            GovernorateComboBox.Focus();
            return;
        }

        NavigateTo("dashboard");
    }

    private void GovernorateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var governorate = GovernorateComboBox.SelectedItem as string;
        DirectorateComboBox.ItemsSource = governorate is not null && _directorates.TryGetValue(governorate, out var values) ? values : null;
        DirectorateComboBox.SelectedIndex = -1; DirectorateComboBox.IsEnabled = !string.IsNullOrWhiteSpace(governorate);
        AreaComboBox.ItemsSource = null; AreaComboBox.SelectedIndex = -1; AreaComboBox.IsEnabled = false;
    }

    private void DirectorateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var directorate = DirectorateComboBox.SelectedItem as string;
        AreaComboBox.ItemsSource = directorate is not null && _areas.TryGetValue(directorate, out var values) ? values : null;
        AreaComboBox.SelectedIndex = -1; AreaComboBox.IsEnabled = !string.IsNullOrWhiteSpace(directorate);
    }

    private void SaveLocationButton_Click(object sender, RoutedEventArgs e)
    {
        var governorate = GovernorateComboBox.SelectedItem as string;
        var directorate = DirectorateComboBox.SelectedItem as string;
        var area = AreaComboBox.SelectedItem as string;
        var landmark = LandmarkTextBox.Text.Trim();
        if (governorate is null || directorate is null || area is null || string.IsNullOrWhiteSpace(landmark))
        {
            SetStatus(LocationStatusTextBlock, "اختر المحافظة ثم المديرية ثم المنطقة، واكتب أقرب معلم بارز.", true);
            return;
        }

        var location = new SavedLocation(governorate, directorate, area, landmark, DateTimeOffset.Now);
        try
        {
            var path = GetLocationFilePath(_currentAccountKey ?? "unassigned");
            var directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonSerializer.Serialize(location, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            SetStatus(LocationStatusTextBlock, "تعذر حفظ موقع الشبكة: " + ex.Message, true);
            return;
        }

        _locationSaved = true;
        _savedLocationSummary = $"{governorate} · {directorate} · {area}";
        LocationBadgeTextBlock.Text = _savedLocationSummary;
        LocationBadgeTextBlock.Visibility = _locationRequiredForCurrentAccount ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceView.IsEnabled = true;
        LocationModalOverlay.Visibility = Visibility.Collapsed;
        CancelLocationEditButton.Visibility = Visibility.Collapsed;
        NavigateTo(_locationRequiredForCurrentAccount ? "dashboard" : _sectionBeforeLocationEdit);
    }

    private void ShowLocationModal()
    {
        _sectionBeforeLocationEdit = _currentSection;
        LocationStatusTextBlock.Text = string.Empty;
        CancelLocationEditButton.Visibility = (_locationSaved || !_locationRequiredForCurrentAccount) ? Visibility.Visible : Visibility.Collapsed;
        var workArea = SystemParameters.WorkArea;
        LocationModalBorder.Width = Math.Min(760, Math.Max(520, workArea.Width - 48));
        LocationModalBorder.MaxWidth = Math.Max(520, workArea.Width - 32);
        LocationModalBorder.MaxHeight = Math.Max(340, Math.Min(470, workArea.Height - 48));
        WorkspaceView.IsEnabled = false;
        LocationModalOverlay.Visibility = Visibility.Visible;
        GovernorateComboBox.Focus();
    }

    private void CancelLocationEditButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_locationSaved && _locationRequiredForCurrentAccount) return;
        LocationModalOverlay.Visibility = Visibility.Collapsed;
        WorkspaceView.IsEnabled = true;
        NavigateTo(_sectionBeforeLocationEdit);
    }

    private void NavigationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string key) return;
        if (_locationRequiredForCurrentAccount && !_locationSaved)
        {
            ShowLocationModal();
            SetStatus(LocationStatusTextBlock, "أكمل موقع الشبكة قبل متابعة العمل.", true);
            return;
        }
        NavigateTo(key);
    }

    private void DashboardMetric_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string route) return;
        var parts = route.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0])) return;
        if (_locationRequiredForCurrentAccount && !_locationSaved) { ShowLocationModal(); return; }
        NavigateTo(parts[0]);
        if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1])) ActivateSectionTab(parts[1]);
        if (parts.Length > 2 && string.Equals(parts[2], "all", StringComparison.OrdinalIgnoreCase) &&
            SectionFeatureContentHost.Content is GeneratedCardsView generatedCardsView)
            generatedCardsView.ShowAllSystems();
    }

    private void NavigateTo(string key)
    {
        if (_locationRequiredForCurrentAccount && !_locationSaved && key != "dashboard")
        {
            ShowLocationModal();
            return;
        }

        _currentSection = key;
        SectionBodyHost.Content = null;
        SectionBodyHost.Visibility = Visibility.Collapsed;
        SectionContentPanel.Visibility = Visibility.Visible;
        DashboardPage.Visibility = key == "dashboard" ? Visibility.Visible : Visibility.Collapsed;
        SectionPage.Visibility = key == "dashboard" ? Visibility.Collapsed : Visibility.Visible;
        PageTitleTextBlock.Text = key switch
        {
            "dashboard" => "الرئيسية",
            "network-users" => "مستخدمو الشبكة",
            "cards" => "الكروت والدفعات",
            "packages" => "الباقات",
            "sales" => "المبيعات ونقاط البيع",
            "printing" => "الطباعة والتصميم",
            "networks" => "الشبكات ومصادر المصادقة",
            "reports" => "مركز التقارير",
            "settings" => "الإعدادات والصيانة",
            _ => "Nexora"
        };
        PageSubtitleTextBlock.Text = key == "dashboard"
            ? "نظرة موحدة على الكروت والمبيعات ومصادر المصادقة"
            : "كل وظيفة في مكانها، مع ربط واضح بين الإصدار والإدارة والتقارير";
        UpdateSidebarSelection(key);
        if (key == "dashboard")
        {
            SectionTabsPanel.Children.Clear();
            return;
        }

        SectionTabsPanel.Children.Clear();
        var tabs = _sectionTabs.TryGetValue(key, out var list) ? list : ["نظرة عامة"];
        foreach (var tab in tabs)
        {
            var btn = new Button
            {
                Content = tab,
                Tag = tab,
                Style = (Style)FindResource("SecondaryButton"),
                Margin = new Thickness(0, 0, 8, 0),
                MinWidth = 110
            };
            btn.Click += SectionTabButton_Click;
            SectionTabsPanel.Children.Add(btn);
        }
        ActivateSectionTab(tabs[0]);
    }

    private void UpdateSidebarSelection(string sectionKey)
    {
        foreach (var button in SidebarItemsPanel.Children.OfType<Button>())
        {
            var selected = string.Equals(button.Tag?.ToString(), sectionKey, StringComparison.Ordinal);
            button.Background = selected ? (Brush)FindResource("PrimaryDarkBrush") : Brushes.Transparent;
            button.Foreground = Brushes.White;
        }
    }

    private void SectionTabButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string tab) return;
        ActivateSectionTab(tab);
        if (_currentSection == "settings" && tab == "الموقع والهوية") ShowLocationModal();
    }

    private void ActivateSectionTab(string tab)
    {
        SectionContentTitleTextBlock.Text = tab;
        SectionContentDescriptionTextBlock.Text = GetSectionDescription(_currentSection, tab);
        SectionPlaceholderTextBlock.Text = GetSectionPlaceholder(_currentSection, tab);
        RenderFeatureContent(tab);

        if (SectionFeatureContentHost.Content is GeneratedCardsView cardsView)
        {
            if (_currentSection == "network-users")
            {
                var systemKey = tab switch
                {
                    "مستخدمو HotSpot" => "hotspot",
                    "مستخدمو User Manager" => "user-manager",
                    _ => "all"
                };
                cardsView.SetSystemFilter(systemKey);
                cardsView.SetBatchFilter("all");
                cardsView.SetStatusFilter("all");
                cardsView.SetActivityFilter(tab == "النشطون الآن" ? "active" : tab == "غير النشطين" ? "inactive" : "all");
                if (tab == "كل الحسابات") cardsView.SetSearchQuery(string.Empty);
            }
            else if (_currentSection == "cards" && tab == "إدارة الكروت")
            {
                cardsView.ShowAllSystems();
                cardsView.SetBatchFilter("all");
                cardsView.SetStatusFilter("all");
                cardsView.SetActivityFilter("all");
            }
        }

        foreach (var child in SectionTabsPanel.Children.OfType<Button>())
        {
            var selected = string.Equals(child.Tag?.ToString(), tab, StringComparison.Ordinal);
            child.Background = (Brush)FindResource(selected ? "PrimaryBrush" : "SurfaceBrush");
            child.Foreground = (Brush)FindResource(selected ? "SurfaceBrush" : "TextBrush");
            child.BorderBrush = (Brush)FindResource(selected ? "PrimaryBrush" : "BorderBrushSoft");
        }

        var featureOwnsWorkspace = SectionFeatureContentHost.Content is
            CardIssuanceView or IssuanceWorkspaceView or GeneratedCardsView or CardBatchesView or
            PackageManagementView or SalesReportView or SalesTransactionsView or SalesPointsView or PrintQueueView or ReportsCenterView or NetworkSourcesView;
        SectionContentPanel.Visibility = featureOwnsWorkspace ? Visibility.Collapsed : Visibility.Visible;
        SectionBodyHost.Visibility = Visibility.Collapsed;
        SectionBodyHost.Content = null;
    }

    private void RenderFeatureContent(string tab)
    {
        var cacheKey = _currentSection switch
        {
            "network-users" => "network-users|accounts",
            "cards" when tab == "إصدار الكروت" => "cards|issuance",
            "cards" when tab == "إدارة الكروت" => "cards|cards-list",
            "cards" when tab == "إدارة الدفعات" => "cards|batches",
            "packages" when tab == "كتالوج الباقات" => "packages|all",
            "packages" when tab == "باقات HotSpot" => "packages|hotspot",
            "packages" when tab == "باقات User Manager" => "packages|user-manager",
            "sales" when tab == "ملخص المبيعات" => "sales|summary",
            "sales" when tab == "معاملات البيع" => "sales|transactions",
            "sales" when tab == "نقاط البيع" => "sales|points",
            "printing" => "printing|workspace",
            "networks" when tab == "مصادر المصادقة" => "networks|sources",
            "networks" when tab == "HotSpot" => "networks|hotspot",
            "networks" when tab == "User Manager" => "networks|user-manager",
            "reports" => "reports|center",
            _ => $"{_currentSection}|{tab}"
        };

        if (!_featureViewCache.TryGetValue(cacheKey, out var featureView))
        {
            featureView = CreateFeatureView(_currentSection, tab);
            if (featureView is not null)
                _featureViewCache[cacheKey] = featureView;
        }

        if (featureView is null)
        {
            SectionFeatureContentHost.Content = null;
            SectionFeatureContentHost.Visibility = Visibility.Collapsed;
            SectionPlaceholderCard.Visibility = Visibility.Visible;
            return;
        }

        SectionPlaceholderCard.Visibility = Visibility.Collapsed;
        SectionFeatureContentHost.Visibility = Visibility.Visible;
        SectionFeatureContentHost.Content = featureView;
    }

    private UserControl? CreateFeatureView(string section, string tab)
    {
        if (section == "network-users")
        {
            var view = new GeneratedCardsView(_demoCardIssuanceService, DemoAccountSystem.UserManager);
            view.NavigationRequested += FeatureView_NavigationRequested;
            view.ShowAllSystems();
            return view;
        }

        if (section == "cards" && tab == "إصدار الكروت")
        {
            var view = new IssuanceWorkspaceView(_demoCardIssuanceService);
            view.NavigationRequested += FeatureView_NavigationRequested;
            return view;
        }

        if (section == "cards" && tab == "إدارة الكروت")
        {
            var view = new GeneratedCardsView(_demoCardIssuanceService, DemoAccountSystem.UserManager);
            view.NavigationRequested += FeatureView_NavigationRequested;
            view.ShowAllSystems();
            return view;
        }

        if (section == "cards" && tab == "إدارة الدفعات")
        {
            var view = new CardBatchesView(_demoCardIssuanceService);
            view.NavigationRequested += FeatureView_NavigationRequested;
            return view;
        }

        if (section == "packages")
        {
            var provider = tab == "باقات HotSpot" ? "hotspot" : tab == "باقات User Manager" ? "user-manager" : "all";
            return new PackageManagementView(provider);
        }

        if (section == "sales" && tab == "ملخص المبيعات")
            return new SalesReportView(_demoSalesService);

        if (section == "sales" && tab == "معاملات البيع")
        {
            var view = new SalesTransactionsView(_demoSalesService);
            view.NavigationRequested += FeatureView_NavigationRequested;
            return view;
        }

        if (section == "sales" && tab == "نقاط البيع")
        {
            var view = new SalesPointsView(_demoSalesService);
            view.NavigationRequested += FeatureView_NavigationRequested;
            return view;
        }

        if (section == "printing")
        {
            var view = new PrintQueueView(_demoCardIssuanceService);
            view.NavigationRequested += FeatureView_NavigationRequested;
            return view;
        }

        if (section == "networks")
        {
            var area = tab == "HotSpot" ? "hotspot" : tab == "User Manager" ? "user-manager" : "sources";
            var view = new NetworkSourcesView(area, _demoCardIssuanceService);
            view.NavigationRequested += FeatureView_NavigationRequested;
            return view;
        }

        if (section == "reports")
        {
            var view = new ReportsCenterView(_demoCardIssuanceService, _demoSalesService);
            view.NavigationRequested += FeatureView_NavigationRequested;
            return view;
        }

        return null;
    }

    private void FeatureView_NavigationRequested(object? sender, FeatureNavigationRequestedEventArgs e)
    {
        if (_locationRequiredForCurrentAccount && !_locationSaved)
        {
            ShowLocationModal();
            return;
        }

        if (e.SectionKey is "hotspot" or "user-manager")
        {
            if (e.TabName == "إضافة الكروت")
            {
                NavigateTo("cards");
                ActivateSectionTab("إصدار الكروت");
                if (SectionFeatureContentHost.Content is IssuanceWorkspaceView issuanceView && (e.SectionKey is "hotspot" or "user-manager"))
                    issuanceView.SelectTargetSystem(e.SectionKey);
                return;
            }
            NavigateTo("network-users");
            ActivateSectionTab(e.SectionKey == "hotspot" ? "مستخدمو HotSpot" : "مستخدمو User Manager");
        }
        else
        {
            NavigateTo(e.SectionKey);
            ActivateSectionTab(e.TabName);
        }

        if (e.ContextKind == "card" && SectionFeatureContentHost.Content is GeneratedCardsView cardsView)
        {
            if (!string.IsNullOrWhiteSpace(e.SystemKey)) cardsView.SetSystemFilter(e.SystemKey);
            cardsView.SetBatchFilter("all");
            cardsView.SetActivityFilter("all");
            cardsView.SetStatusFilter("all");
            cardsView.OpenCardDetails(e.ContextValue ?? string.Empty);
        }
        else if (e.ContextKind == "batch-details" && SectionFeatureContentHost.Content is CardBatchesView reportBatchesView)
        {
            if (!string.IsNullOrWhiteSpace(e.SystemKey)) reportBatchesView.SetSystemFilter(e.SystemKey);
            reportBatchesView.FocusBatch(e.ContextValue ?? string.Empty);
        }
        else if (e.ContextKind == "batch" && SectionFeatureContentHost.Content is CardBatchesView batchesView)
        {
            batchesView.FocusBatch(e.ContextValue ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(e.SystemKey)) batchesView.SetSystemFilter(e.SystemKey);
        }
        else if (e.ContextKind == "batch" && SectionFeatureContentHost.Content is GeneratedCardsView cardsInBatchView)
        {
            if (!string.IsNullOrWhiteSpace(e.SystemKey)) cardsInBatchView.SetSystemFilter(e.SystemKey);
            cardsInBatchView.SetActivityFilter("all");
            cardsInBatchView.SetStatusFilter("all");
            cardsInBatchView.SetBatchFilter(e.ContextValue ?? "all");
        }
        else if (e.ContextKind == "sale" && SectionFeatureContentHost.Content is SalesTransactionsView transactionsView)
        {
            transactionsView.FocusTransaction(e.ContextValue ?? string.Empty, showDetails: true);
        }
        else if (e.ContextKind == "sale" && SectionFeatureContentHost.Content is SalesReportView salesView)
        {
            salesView.ShowTransaction(e.ContextValue ?? string.Empty);
        }
        else if (e.ContextKind == "point" && SectionFeatureContentHost.Content is SalesTransactionsView pointTransactionsView)
        {
            pointTransactionsView.FocusPointOfSale(e.ContextValue ?? string.Empty);
        }

        if (!string.IsNullOrWhiteSpace(e.SystemKey) && SectionFeatureContentHost.Content is IssuanceWorkspaceView targetIssuance)
            targetIssuance.SelectTargetSystem(e.SystemKey);
    }

    private void SalesChartDay_Click(object sender, RoutedEventArgs e)
    {
        if (_locationRequiredForCurrentAccount && !_locationSaved)
        {
            ShowLocationModal();
            return;
        }

        if (sender is not Button button)
            return;

        DateOnly day;
        if (button.Tag is DateOnly selectedDate)
            day = selectedDate;
        else if (button.Tag is string rawDate &&
                 DateOnly.TryParseExact(rawDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            day = parsedDate;
        else
            return;

        NavigateTo("sales");
        ActivateSectionTab("ملخص المبيعات");
        if (SectionFeatureContentHost.Content is SalesReportView report)
            report.ShowDay(day);
    }

    private void UpdateDashboardMetrics()
    {
        if (TotalCardsMetricTextBlock is null || DashboardSalesDaysItemsControl is null)
            return;

        var cards = _demoCardIssuanceService.Cards;
        var total = cards.Count;
        var available = cards.Count(card => card.DemoStatus == "متاحة");
        var inUse = cards.Count(card => card.DemoStatus == "قيد الاستخدام");
        var expired = cards.Count(card => card.DemoStatus == "منتهية");
        var suspended = cards.Count(card => card.DemoStatus == "موقوفة");

        TotalCardsMetricTextBlock.Text = total.ToString("N0", CultureInfo.CurrentCulture);
        AvailableCardsMetricTextBlock.Text = available.ToString("N0", CultureInfo.CurrentCulture);
        InUseCardsMetricTextBlock.Text = inUse.ToString("N0", CultureInfo.CurrentCulture);
        AvailableCardsMetricDetailTextBlock.Text = total == 0 ? "لا توجد كروت" : $"{available * 100d / total:0.#}% من الكروت";
        CardStatusTotalTextBlock.Text = $"إجمالي الكروت: {total:N0}";
        AvailableCardCountTextBlock.Text = available.ToString("N0", CultureInfo.CurrentCulture);
        InUseCardCountTextBlock.Text = inUse.ToString("N0", CultureInfo.CurrentCulture);
        ExpiredCardCountTextBlock.Text = expired.ToString("N0", CultureInfo.CurrentCulture);
        SuspendedCardCountTextBlock.Text = suspended.ToString("N0", CultureInfo.CurrentCulture);
        AvailableCardsProgressBar.Value = Percent(available, total);
        InUseCardsProgressBar.Value = Percent(inUse, total);
        ExpiredCardsProgressBar.Value = Percent(expired, total);
        SuspendedCardsProgressBar.Value = Percent(suspended, total);

        var dailySales = _demoSalesService.GetDailySummaries();
        DashboardSalesDaysItemsControl.ItemsSource = dailySales;
        var salesTotal = dailySales.Sum(item => item.Amount);
        DashboardSalesTotalTextBlock.Text = $"إجمالي السبعة أيام: {salesTotal:N0} ر.ي";
        LastDaySalesMetricTextBlock.Text = dailySales.Count == 0 ? "—" : dailySales[^1].Amount.ToString("N0", CultureInfo.CurrentCulture);
    }

    private static double Percent(int value, int total) => total <= 0 ? 0 : value * 100d / total;

    private static string GetSectionDescription(string section, string tab) => section switch
    {
        "network-users" => tab switch
        {
            "النشطون الآن" => "الحسابات التي تحمل حالة استخدام نشطة في سجلات المصدر.",
            "غير النشطين" => "الحسابات التي لا تحمل حالة استخدام نشطة حاليًا.",
            "مستخدمو HotSpot" => "حسابات جهة HotSpot ضمن قائمة المستخدمين الموحدة.",
            "مستخدمو User Manager" => "حسابات جهة User Manager ضمن قائمة المستخدمين الموحدة.",
            _ => "إدارة موحدة لحسابات الدخول مع إمكانية التصفية حسب المصدر والحالة والدفعة."
        },
        "cards" => "إصدار الكروت ومتابعة الدفعات والمخزون في مساحة واحدة.",
        "packages" => "كتالوج تجاري موحّد وربط تقني مستقل لكل جهة إصدار.",
        "sales" => "معاملات البيع وتوزيع المبيعات وفق السجلات المتاحة.",
        "printing" => "إدارة قائمة الكروت والتصاميم ومعاينة الطباعة وتنفيذها.",
        "networks" => "إعداد مصادر المصادقة وربط وظائفها بوحدات Nexora.",
        "reports" => "التقارير التجارية والإدارية والتشغيلية مجمّعة حسب مصدرها.",
        _ => $"{tab} ضمن إدارة Nexora."
    };

    private static string GetSectionPlaceholder(string section, string tab) => (section, tab) switch
    {
        ("settings", "الموقع والهوية") => "إدارة معلومات الشبكة وموقعها والعناصر المرتبطة بمساحة العمل.",
        ("settings", "النسخ الاحتياطي") => "مراجعة خيارات الاحتفاظ بنسخة احتياطية من بيانات التطبيق.",
        ("settings", "الصيانة") => "أدوات الصيانة والتحقق من بيانات النظام.",
        ("settings", _) => "إعدادات مساحة العمل والصلاحيات والمظهر.",
        ("networks", "مصادر المصادقة") => "إدارة مصادر المصادقة وطرق الربط المستخدمة لحسابات الشبكة.",
        ("networks", "صفحات الدخول") => "إدارة مظهر ومحتوى صفحات الدخول بحسب خدمة الشبكة.",
        _ => "اختر وظيفة من التبويبات لفتح مساحة العمل المرتبطة بها."
    };

    private void LoadSavedLocation(string accountKey)
    {
        _locationSaved = false;
        _savedLocationSummary = null;
        GovernorateComboBox.SelectedIndex = -1;
        DirectorateComboBox.ItemsSource = null;
        DirectorateComboBox.SelectedIndex = -1;
        DirectorateComboBox.IsEnabled = false;
        AreaComboBox.ItemsSource = null;
        AreaComboBox.SelectedIndex = -1;
        AreaComboBox.IsEnabled = false;
        LandmarkTextBox.Text = string.Empty;

        try
        {
            var path = GetLocationFilePath(accountKey);
            if (!File.Exists(path)) return;
            var saved = JsonSerializer.Deserialize<SavedLocation>(File.ReadAllText(path));
            if (saved is null || string.IsNullOrWhiteSpace(saved.Governorate) ||
                string.IsNullOrWhiteSpace(saved.Directorate) || string.IsNullOrWhiteSpace(saved.Area) ||
                string.IsNullOrWhiteSpace(saved.Landmark)) return;
            if (!_directorates.TryGetValue(saved.Governorate, out var directorates) || !directorates.Contains(saved.Directorate)) return;
            if (!_areas.TryGetValue(saved.Directorate, out var areas) || !areas.Contains(saved.Area)) return;

            GovernorateComboBox.SelectedItem = saved.Governorate;
            DirectorateComboBox.SelectedItem = saved.Directorate;
            AreaComboBox.SelectedItem = saved.Area;
            LandmarkTextBox.Text = saved.Landmark;
            _locationSaved = true;
            _savedLocationSummary = $"{saved.Governorate} · {saved.Directorate} · {saved.Area}";
        }
        catch
        {
            _locationSaved = false;
            _savedLocationSummary = null;
        }
    }

    private static string GetLocationFilePath(string accountKey)
    {
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(accountKey))).Substring(0, 20);
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NexoraProductDemo", $"network-location-{digest}.json");
    }

    private void SignOutButton_Click(object sender, RoutedEventArgs e)
    {
        LocationModalOverlay.Visibility = Visibility.Collapsed;
        WorkspaceView.IsEnabled = true;
        WorkspaceView.Visibility = Visibility.Collapsed;
        LoginView.Visibility = Visibility.Visible;
        _currentAccountKey = null;
        _locationRequiredForCurrentAccount = false;
        _locationSaved = false;
        _savedLocationSummary = null;
        LocationBadgeTextBlock.Visibility = Visibility.Collapsed;
        WindowState = WindowState.Normal;
        MinWidth = 520; MinHeight = 480; MaxWidth = 920; MaxHeight = 820;
        Width = Math.Min(660, SystemParameters.WorkArea.Width - 40);
        Height = Math.Min(630, SystemParameters.WorkArea.Height - 40);
        Title = "Nexora | كروت الشبكات";
        Left = Math.Max(0, (SystemParameters.WorkArea.Width - Width) / 2);
        Top = Math.Max(0, (SystemParameters.WorkArea.Height - Height) / 2);
        NetworkCodeTextBox.Text = DemoNetworkCode;
        OwnerPhoneTextBox.Text = DemoOwnerPhone;
        SystemPasswordBox.Password = DemoPassword;
        SetLoginMode(false);
    }

    private void SetStatus(TextBlock target, string message, bool error) { target.Text = message; target.Foreground = (Brush)FindResource(error ? "DangerBrush" : "SuccessBrush"); }
    private sealed record SavedLocation(string Governorate, string Directorate, string Area, string Landmark, DateTimeOffset SavedAt);

    private void CardStatusDrilldown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string selectedStatus) return;
        if (_locationRequiredForCurrentAccount && !_locationSaved)
        {
            ShowLocationModal();
            return;
        }

        var matchedCards = _demoCardIssuanceService.Cards
            .Where(card => string.Equals(card.DemoStatus, selectedStatus, StringComparison.Ordinal))
            .OrderByDescending(card => card.CreatedAt)
            .ToArray();

        CardStatusDrilldownTitleTextBlock.Text = $"سجلات الكروت: {selectedStatus}";
        CardStatusDrilldownSummaryTextBlock.Text =
            $"عدد الكروت بهذه الحالة: {matchedCards.Length:N0}.";
        var workArea = SystemParameters.WorkArea;
        CardStatusDrilldownDialogBorder.Width = Math.Min(1240, Math.Max(420, workArea.Width - 48));
        CardStatusDrilldownDialogBorder.Height = Math.Min(680, Math.Max(350, workArea.Height - 48));
        CardStatusDrilldownGrid.ItemsSource = matchedCards;
        CardStatusDrilldownOverlay.Visibility = Visibility.Visible;
        CardStatusDrilldownGrid.Focus();
    }

    private void CloseCardStatusDrilldown_Click(object sender, RoutedEventArgs e)
    {
        CardStatusDrilldownOverlay.Visibility = Visibility.Collapsed;
        CardStatusDrilldownGrid.ItemsSource = null;
    }

}
