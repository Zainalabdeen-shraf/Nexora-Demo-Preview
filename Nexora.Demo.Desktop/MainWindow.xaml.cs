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
        ["sales"] = ["ملخص المبيعات", "معاملات البيع", "نقاط البيع"],
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
        catch (Exception ex) { MessageBox.Show("تعذر فتح رابط التسجيل. " + ex.Message, "تاب كارد", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }


    private void ConfigureWorkspaceWindowChrome()
    {
        // Custom title bar is enabled only for the authenticated workspace.
        WindowState = WindowState.Normal;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 50,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        UpdateWorkspaceWindowControls();
    }

    private void RestoreLoginWindowChrome()
    {
        WindowState = WindowState.Normal;
        WindowChrome.SetWindowChrome(this, null);
        WindowStyle = WindowStyle.SingleBorderWindow;
        ResizeMode = ResizeMode.CanResize;
    }

    private void UpdateWorkspaceWindowControls()
    {
        if (WindowMaximizeRestoreButton is not null)
            WindowMaximizeRestoreButton.Content = WindowState == WindowState.Maximized ? "❐" : "□";
    }

    private void LoadSavedRouterConnectionProfile()
    {
        _savedRouterProfile = null;
        if (string.IsNullOrWhiteSpace(_currentAccountKey))
        {
            RefreshRouterConnectionHeaderStatus();
            return;
        }

        try
        {
            _savedRouterProfile = RouterConnectionProfileStore.Load(_currentAccountKey);
            if (_savedRouterProfile is not null)
                RouterConnectionDialogStatusTextBlock.Text = "تم تحميل بيانات الاتصال المحفوظة من Windows. اضغط اختبار الاتصال لاستخدامها.";
        }
        catch (Exception)
        {
            _savedRouterProfile = null;
            RouterConnectionDialogStatusTextBlock.Text = "تعذر قراءة بيانات الاتصال المحفوظة من Windows. يمكنك إدخال البيانات واختبارها من جديد.";
        }
        RefreshRouterConnectionHeaderStatus();
    }

    private void RefreshRouterConnectionHeaderStatus()
    {
        if (RouterConnectionHeaderStatusTextBlock is null) return;
        if (RouterConnectionSession.CurrentSnapshot is { } snapshot)
        {
            RouterConnectionHeaderStatusTextBlock.Text = "متصل · " + snapshot.Identity;
            RouterConnectionHeaderStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(187, 247, 208));
        }
        else if (_savedRouterProfile is not null)
        {
            RouterConnectionHeaderStatusTextBlock.Text = "اتصال محفوظ";
            RouterConnectionHeaderStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(254, 202, 202));
        }
        else
        {
            RouterConnectionHeaderStatusTextBlock.Text = "غير متصل";
            RouterConnectionHeaderStatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(254, 202, 202));
        }
    }

    private void OpenRouterConnectionOverlay_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentAccountKey)) return;
        LoadSavedRouterConnectionProfile();

        var active = RouterConnectionSession.CurrentOptions;
        if (active is not null)
        {
            RouterConnectionAddressTextBox.Text = active.BaseAddress.GetLeftPart(UriPartial.Authority);
            RouterConnectionUsernameTextBox.Text = active.Username;
            RouterConnectionPasswordBox.Password = active.Password;
            SaveRouterConnectionCheckBox.IsChecked = _savedRouterProfile is not null;
        }
        else if (_savedRouterProfile is not null)
        {
            RouterConnectionAddressTextBox.Text = _savedRouterProfile.Address;
            RouterConnectionUsernameTextBox.Text = _savedRouterProfile.Username;
            RouterConnectionPasswordBox.Password = _savedRouterProfile.Password;
            SaveRouterConnectionCheckBox.IsChecked = true;
        }
        else
        {
            RouterConnectionAddressTextBox.Clear();
            RouterConnectionUsernameTextBox.Clear();
            RouterConnectionPasswordBox.Clear();
            SaveRouterConnectionCheckBox.IsChecked = true;
            RouterConnectionDialogStatusTextBlock.Text = "أدخل عنوان HTTPS وحساب القراءة. ستُستخدم هذه البيانات للاختبار ولن تُحفظ إلا بعد نجاحه.";
        }

        RefreshRouterConnectionHeaderStatus();
        UpdateDashboardRouterStatus();
        RouterConnectionOverlay.Visibility = Visibility.Visible;
        RouterConnectionAddressTextBox.Focus();
    }

    private void CloseRouterConnectionOverlay_Click(object sender, RoutedEventArgs e)
    {
        RouterConnectionOverlay.Visibility = Visibility.Collapsed;
        RefreshRouterConnectionHeaderStatus();
        UpdateDashboardRouterStatus();
    }

    private async void ConnectRouterButton_Click(object sender, RoutedEventArgs e)
    {
        if (_routerConnectionRequestInProgress) return;
        if (string.IsNullOrWhiteSpace(_currentAccountKey))
        {
            RouterConnectionDialogStatusTextBlock.Text = "افتح مساحة الشبكة أولًا.";
            return;
        }

        _routerConnectionRequestInProgress = true;
        ConnectRouterButton.IsEnabled = false;
        RouterConnectionDialogStatusTextBlock.Text = "جارٍ اختبار HTTPS وقراءة هوية الراوتر والموارد والواجهات والجيران وARP...";
        try
        {
            var options = RouterOsConnectionOptions.Create(
                RouterConnectionAddressTextBox.Text,
                RouterConnectionUsernameTextBox.Text,
                RouterConnectionPasswordBox.Password);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var client = new RouterOsRestClient(options);
            var snapshot = await client.ReadDiagnosticsAsync(timeout.Token);

            // Publish the active connection only after a successful real read.
            RouterConnectionSession.SetConnected(options, snapshot);
            var normalizedAddress = options.BaseAddress.GetLeftPart(UriPartial.Authority);
            if (SaveRouterConnectionCheckBox.IsChecked == true)
            {
                var profile = new RouterConnectionProfile(normalizedAddress, options.Username, options.Password, DateTimeOffset.Now);
                RouterConnectionProfileStore.Save(_currentAccountKey, profile);
                _savedRouterProfile = profile;
            }
            else
            {
                RouterConnectionProfileStore.Delete(_currentAccountKey);
                _savedRouterProfile = null;
            }

            RouterConnectionDialogStatusTextBlock.Text = $"نجح الاتصال بالراوتر «{snapshot.Identity}» وقراءة {snapshot.Interfaces.Count:N0} واجهة عند {snapshot.CollectedAt:HH:mm:ss}.";
            RefreshRouterConnectionHeaderStatus();
            UpdateDashboardMetrics();
            RouterConnectionOverlay.Visibility = Visibility.Collapsed;
        }
        catch (RouterOsConnectionException ex)
        {
            RouterConnectionDialogStatusTextBlock.Text = ex.UserMessage;
        }
        catch (ArgumentException ex)
        {
            RouterConnectionDialogStatusTextBlock.Text = ex.Message;
        }
        catch (OperationCanceledException)
        {
            RouterConnectionDialogStatusTextBlock.Text = "انتهت مهلة اختبار الاتصال. تحقق من العنوان وإمكانية الوصول إلى الراوتر.";
        }
        catch (HttpRequestException)
        {
            RouterConnectionDialogStatusTextBlock.Text = "تعذر إنشاء اتصال HTTPS موثوق. تحقق من الشبكة وخدمة HTTPS وشهادة TLS.";
        }
        catch (Exception ex)
        {
            // A successful read can precede a local credential-store error; keep the live session and explain the save failure.
            if (RouterConnectionSession.CurrentSnapshot is { } live)
            {
                RouterConnectionDialogStatusTextBlock.Text = $"الاتصال بالراوتر «{live.Identity}» ناجح، لكن تعذر حفظ بيانات الاعتماد محليًا. {ex.Message}";
                RefreshRouterConnectionHeaderStatus();
            }
            else
            {
                RouterConnectionDialogStatusTextBlock.Text = "تعذر إكمال الاتصال أو حفظ الإعداد. لم تُرسل أوامر تغيير إلى الراوتر.";
            }
        }
        finally
        {
            _routerConnectionRequestInProgress = false;
            ConnectRouterButton.IsEnabled = true;
        }
    }

    private void DisconnectRouterButton_Click(object sender, RoutedEventArgs e)
    {
        RouterConnectionSession.Disconnect();
        RouterConnectionDialogStatusTextBlock.Text = _savedRouterProfile is null
            ? "تم قطع الاتصال. لا توجد بيانات اتصال محفوظة."
            : "تم قطع الاتصال. بيانات الاتصال محفوظة ويمكن استخدامها مجددًا.";
        RefreshRouterConnectionHeaderStatus();
        UpdateDashboardMetrics();
    }

    private void ClearSavedRouterConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentAccountKey)) return;
        try
        {
            RouterConnectionProfileStore.Delete(_currentAccountKey);
            _savedRouterProfile = null;
            RouterConnectionDialogStatusTextBlock.Text = "حُذفت بيانات الاتصال المحفوظة من Windows. الاتصال الحالي لم يُقطع.";
            RefreshRouterConnectionHeaderStatus();
            UpdateDashboardMetrics();
        }
        catch (Exception)
        {
            RouterConnectionDialogStatusTextBlock.Text = "تعذر حذف بيانات الاتصال المحفوظة من Windows.";
        }
    }

    private void WindowCloseButton_Click(object sender, RoutedEventArgs e) => Close();
    private void WindowMinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void WindowMaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }
    private void OpenWorkspace(string header, string accountKey, bool requireLocation)
    {
        _currentAccountKey = accountKey;
        LoadSavedRouterConnectionProfile();
        UpdateDashboardMetrics();
        _locationRequiredForCurrentAccount = requireLocation;
        LoadSavedLocation(accountKey);
        SidebarNetworkTextBlock.Text = _networkDisplayName;
        Title = "تاب كارد | إدارة شبكات الإنترنت";
        LoginView.Visibility = Visibility.Collapsed;
        WorkspaceView.Visibility = Visibility.Visible;
        ConfigureWorkspaceWindowChrome();
        MaxWidth = double.PositiveInfinity;
        MaxHeight = double.PositiveInfinity;
        var workArea = SystemParameters.WorkArea;
        MinWidth = Math.Min(1050, Math.Max(560, workArea.Width - 48));
        MinHeight = Math.Min(680, Math.Max(420, workArea.Height - 48));

        // Keep the workspace inside the usable desktop area (above the taskbar).
        // Use Normal state with explicit work-area bounds to avoid inconsistent custom maximize sizing.
        WindowState = WindowState.Normal;
        Left = workArea.Left + 4;
        Top = workArea.Top + 4;
        Width = Math.Min(1400, Math.Max(560, workArea.Width - 8));
        Height = Math.Min(900, Math.Max(420, workArea.Height - 8));
        LocationBadgeTextBlock.Visibility = requireLocation ? Visibility.Visible : Visibility.Collapsed;
        LocationBadgeTextBlock.Text = _locationSaved ? (_savedLocationSummary ?? "الموقع مكتمل") : "أكمل موقع الشبكة";
        WorkspaceView.IsEnabled = !(requireLocation && !_locationSaved);
        LocationModalOverlay.Visibility = requireLocation && !_locationSaved ? Visibility.Visible : Visibility.Collapsed;
        CancelLocationEditButton.Visibility = Visibility.Collapsed;

        if (requireLocation && !_locationSaved)
        {
            _locationEditWasOnboarding = true;
            _sectionBeforeLocationEdit = "dashboard";
            _tabBeforeLocationEdit = string.Empty;
            _currentSection = "dashboard";
            PageTitleTextBlock.Text = "الرئيسية";
            PageSubtitleTextBlock.Text = "أكمل موقع شبكتك لبدء العمل";
            DashboardPage.Visibility = Visibility.Visible;
            SectionPage.Visibility = Visibility.Collapsed;
            LocationStatusTextBlock.Text = string.Empty;
            GovernorateComboBox.Focus();
            return;
        }

        NavigateTo(DemoSystemSettingsStore.Current.StartupSection);
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
        if (_featureViewCache.TryGetValue("settings|الموقع والهوية", out var cachedIdentity) && cachedIdentity is WorkspaceIdentityView identityView)
            identityView.UpdateSummary(_networkDisplayName, _locationSaved, _savedLocationSummary);
        LocationBadgeTextBlock.Text = _savedLocationSummary;
        LocationBadgeTextBlock.Visibility = _locationRequiredForCurrentAccount ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceView.IsEnabled = true;
        LocationModalOverlay.Visibility = Visibility.Collapsed;
        CancelLocationEditButton.Visibility = Visibility.Collapsed;
        var wasOnboarding = _locationEditWasOnboarding;
        _locationEditWasOnboarding = false;
        if (wasOnboarding)
            NavigateTo(DemoSystemSettingsStore.Current.StartupSection);
        else
            ReturnFromLocationEdit();
    }

    private void ReturnFromLocationEdit()
    {
        NavigateTo(_sectionBeforeLocationEdit);
        if (_sectionBeforeLocationEdit != "dashboard" &&
            _sectionTabs.TryGetValue(_sectionBeforeLocationEdit, out var availableTabs) &&
            availableTabs.Contains(_tabBeforeLocationEdit))
        {
            ActivateSectionTab(_tabBeforeLocationEdit);
        }
    }

    private void ShowLocationModal()
    {
        _sectionBeforeLocationEdit = _currentSection;
        _tabBeforeLocationEdit = SectionContentTitleTextBlock.Text;
        _locationEditWasOnboarding = _locationRequiredForCurrentAccount && !_locationSaved;
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
        ReturnFromLocationEdit();
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
        CardSystemSelectorPanel.Visibility =
        key == "cards" || key == "packages" || key == "network-users"
            ? Visibility.Visible
            : Visibility.Collapsed;
            UpdatePageBreadcrumb(key);
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
                Style = (Style)FindResource("SectionTabButtonStyle"),
                Margin = new Thickness(0),
                Padding = new Thickness(12, 0, 12, 0),
                MinWidth = 0,
                Height = 34,
                FontSize = 12,
                BorderThickness = new Thickness(0),
                BorderBrush = Brushes.Transparent,
                Background = Brushes.Transparent,
                FlowDirection = FlowDirection.RightToLeft
            };
                        btn.Click += SectionTabButton_Click;
                        btn.Click += (_, _) => RefreshSectionTabButtonStyles();
            btn.MouseEnter += (_, _) => RefreshSectionTabButtonStyles();
            btn.MouseLeave += (_, _) => RefreshSectionTabButtonStyles();
            SectionTabsPanel.Children.Add(btn);
                        }
            ApplyShellDisplayPreferences();
            ActivateSectionTab(tabs[0]);
            RefreshSectionTabButtonStyles();
        }
    private void UpdatePageBreadcrumb(string key, string? tabName = null)
{
    var currentTitle = key switch
    {
        "dashboard" => "لوحة التحكم",
        "network-users" => "مستخدمو الشبكة",
        "cards" => "الكروت والدفعات",
        "packages" => "إدارة الباقات",
        "sales" => "المبيعات ونقاط البيع",
        "printing" => "الطباعة والتصميم",
        "networks" => "الشبكات ومصادر المصادقة",
        "network-center" => "مركز الشبكة",
        "reports" => "مركز التقارير",
        "settings" => "إعدادات النظام",
        _ => "تاب كارد"
    };

    PageTitleIconTextBlock.Visibility = Visibility.Collapsed;
    PageSubtitleTextBlock.Visibility = Visibility.Collapsed;

    PageTitleTextBlock.Inlines.Clear();
    PageTitleTextBlock.FontSize = 12;
    PageTitleTextBlock.FontWeight = FontWeights.Normal;
    PageTitleTextBlock.TextWrapping = TextWrapping.NoWrap;
    PageTitleTextBlock.VerticalAlignment = VerticalAlignment.Center;
    PageTitleTextBlock.TextAlignment = TextAlignment.Right;
    PageTitleTextBlock.Foreground =
        (Brush)FindResource("TextBrush");

    var homeIcon = new Run("\uE80F ")
    {
        FontFamily = new FontFamily("Segoe MDL2 Assets"),
        Foreground = (Brush)FindResource("PrimaryBrush")
    };

    if (key == "dashboard")
    {
        PageTitleTextBlock.Inlines.Add(homeIcon);

        PageTitleTextBlock.Inlines.Add(
            new Run("لوحة التحكم")
            {
                FontWeight = FontWeights.SemiBold
            });

        return;
    }

    var homeLink = new Hyperlink
    {
        Foreground = (Brush)FindResource("PrimaryBrush"),
        TextDecorations = new TextDecorationCollection()
    };

    homeLink.Inlines.Add(homeIcon);
    homeLink.Inlines.Add(new Run("لوحة التحكم"));

    homeLink.Click += (_, _) => NavigateTo("dashboard");

    PageTitleTextBlock.Inlines.Add(homeLink);

    PageTitleTextBlock.Inlines.Add(
        new Run("  ‹  ")
        {
            Foreground = Brushes.Gray
        });

    PageTitleTextBlock.Inlines.Add(
        new Run(currentTitle)
        {
            Foreground = (Brush)FindResource("TextBrush"),
            FontWeight = FontWeights.SemiBold
        });

    if (!string.IsNullOrWhiteSpace(tabName) &&
        !string.Equals(tabName, currentTitle, StringComparison.Ordinal))
    {
        PageTitleTextBlock.Inlines.Add(new Run("  ‹  ")
        {
            Foreground = Brushes.Gray
        });
        PageTitleTextBlock.Inlines.Add(new Run(tabName)
        {
            Foreground = (Brush)FindResource("PrimaryBrush"),
            FontWeight = FontWeights.SemiBold
        });
    }
}
private void RefreshSectionTabButtonStyles()
{
    if (SectionTabsPanel is null || SectionContentTitleTextBlock is null)
        return;

    var activeBackground = (Brush)FindResource("PrimaryLightBrush");
    var activeForeground = (Brush)FindResource("PrimaryBrush");
    var inactiveForeground = (Brush)FindResource("TextBrush");
    var inactiveBorder = (Brush)FindResource("BorderBrushSoft");

    var hoverBackground =
        new SolidColorBrush(Color.FromRgb(255, 244, 245));

    var hoverBorder =
        new SolidColorBrush(Color.FromRgb(240, 184, 190));

    foreach (var button in SectionTabsPanel.Children.OfType<Button>())
    {
        var isSelected = string.Equals(
            button.Tag?.ToString(),
            SectionContentTitleTextBlock.Text,
            StringComparison.Ordinal);

        var isHovered = button.IsMouseOver && !isSelected;

        button.Background = isSelected
            ? activeBackground
            : isHovered ? hoverBackground : Brushes.White;

        button.Foreground = isSelected
            ? activeForeground
            : inactiveForeground;

        button.BorderBrush = isSelected
            ? activeForeground
            : isHovered ? hoverBorder : inactiveBorder;

        button.BorderThickness = new Thickness(1);
        button.FontWeight = isSelected
            ? FontWeights.SemiBold
            : FontWeights.Normal;

        button.Cursor = System.Windows.Input.Cursors.Hand;
    }
}
    private void ApplyShellDisplayPreferences()
    {
        var compact = DemoSystemSettingsStore.Current.NavigationDensity == "مدمجة";
        foreach (var button in SidebarItemsPanel.Children.OfType<Button>())
        {
            button.Height = compact ? 31 : 35;
            button.Margin = compact ? new Thickness(0) : new Thickness(0, 1, 0, 1);
            button.Padding = compact ? new Thickness(7, 2, 7, 2) : new Thickness(8, 3, 8, 3);
            if (button.Content is Panel panel)
            {
                foreach (var label in panel.Children.OfType<TextBlock>())
                {
                    if (Grid.GetColumn(label) == 1)
                        label.FontSize = compact ? 11.5 : 13;
                    else
                        label.FontSize = compact ? 14.5 : 16;
                }
            }
        }

        foreach (var button in SectionTabsPanel.Children.OfType<Button>())
        {
            button.Height = compact ? 32 : 38;
            button.FontSize = compact ? 11.5 : 12.5;
            button.Padding = compact ? new Thickness(12, 0, 12, 0) : new Thickness(16, 0, 16, 0);
        }
    }

    private void UpdateSidebarSelection(string sectionKey)
    {
        var activeBackground = Brushes.White;
        var activeForeground = (Brush)FindResource("PrimaryBrush");
        var activeBorder = new SolidColorBrush(Color.FromRgb(246, 195, 199));

        foreach (var button in SidebarItemsPanel.Children.OfType<Button>())
        {
            var selected = string.Equals(button.Tag?.ToString(), sectionKey, StringComparison.Ordinal);

            button.Background = selected ? activeBackground : Brushes.Transparent;
            button.Foreground = selected ? activeForeground : Brushes.White;
            button.BorderBrush = selected ? activeBorder : Brushes.Transparent;
            button.BorderThickness = new Thickness(1);

            if (button.Content is Panel contentPanel)
            {
                foreach (var text in contentPanel.Children.OfType<TextBlock>())
                {
                    text.Foreground = selected ? activeForeground : Brushes.White;
                }
            }
        }
    }

    private void CardSystemTab_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton tab || tab.Tag is not string systemKey)
            return;

        var targetSystem = systemKey == "hotspot" ? DemoAccountSystem.HotSpot : DemoAccountSystem.UserManager;
        if (_selectedCardSystem == targetSystem)
            return;

        _selectedCardSystem = targetSystem;
        if (_currentSection == "cards")
        {
            var tabName = SectionContentTitleTextBlock.Text;
            if (!_sectionTabs["cards"].Any(name => string.Equals(name, tabName, StringComparison.Ordinal)))
                tabName = _sectionTabs["cards"][0];
            ActivateSectionTab(tabName);
        }
        else if (_currentSection == "network-users")
        {
            var tabName = SectionContentTitleTextBlock.Text;

            if (!_sectionTabs["network-users"].Any(name =>
                string.Equals(name, tabName, StringComparison.Ordinal)))
            {
                tabName = "كل الحسابات";
            }

            ActivateSectionTab(tabName);
        }
        else if (_currentSection == "packages")
        {
            ActivateSectionTab("كتالوج الباقات");
        }
    }
    private void SectionTabButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string tab) return;
        ActivateSectionTab(tab);
    }

    private void ActivateSectionTab(string tab)
    {
        if (_currentSection == "cards" && PageTitleTextBlock.Text != "الكروت والدفعات")
            PageTitleTextBlock.Text = "الكروت والدفعات";
        SectionContentTitleTextBlock.Text = tab;
        var unifiedIssuanceLayout = _currentSection == "cards" && string.Equals(tab, "إصدار الكروت", StringComparison.Ordinal);
        SectionTabsScrollViewer.Visibility = unifiedIssuanceLayout ? Visibility.Collapsed : Visibility.Visible;
        PageSubtitleTextBlock.FontSize = unifiedIssuanceLayout ? 10.5 : 11.5;
        if (unifiedIssuanceLayout)
        {
            PageTitleTextBlock.Text = "إصدار الكروت";
            PageSubtitleTextBlock.Text = "إنشاء دفعة جديدة ومعاينة الكرت والطباعة من شاشة واحدة";
            PageTitleIconTextBlock.Text = "\uE8C7";
        }
        else
        {
            PageTitleTextBlock.Text = _currentSection switch
            {
                "dashboard" => "الرئيسية",
                "network-users" => "مستخدمو الشبكة",
                "cards" => "الكروت والدفعات",
                "packages" => "إدارة الباقات",
                "sales" => "المبيعات ونقاط البيع",
                "printing" => "الطباعة والتصميم",
                "networks" => "الشبكات ومصادر المصادقة",
                "network-center" => "مركز الشبكة",
                "reports" => "مركز التقارير",
                "settings" => "الإعدادات والصيانة",
                _ => "تاب كارد"
            };
            PageTitleIconTextBlock.Text = _currentSection switch
            {
                "dashboard" => "\uE80F",
                "network-users" => "\uE77B",
                "cards" => "\uE8A5",
                "packages" => "\uE8D4",
                "sales" => "\uE7BF",
                "printing" => "\uE749",
                "networks" => "\uE701",
                "network-center" => "\uE968",
                "reports" => "\uE9D2",
                "settings" => "\uE713",
                _ => "\uE80F"
            };
            PageSubtitleTextBlock.Text = _currentSection switch
            {
                "network-center" => "متابعة المواقع والأجهزة والتنبيهات ومهام الصيانة",
                "dashboard" => "نظرة تشغيلية على الكروت والدفعات ومصدر الشبكة",
                "cards" => "إصدار الكروت وإدارة الدفعات",
                _ => string.Empty
            };
            PageSubtitleTextBlock.Visibility = (_currentSection == "dashboard" || _currentSection == "cards" || _currentSection == "network-center" || _currentSection == "network-users") ? Visibility.Collapsed : Visibility.Visible;
        }
        SectionContentDescriptionTextBlock.Text = GetSectionDescription(_currentSection, tab);
        SectionTabsScrollViewer.Visibility = (_currentSection == "packages" || _currentSection == "network-users") ? Visibility.Collapsed : Visibility.Visible;
        SectionPlaceholderTextBlock.Text = GetSectionPlaceholder(_currentSection, tab);
        RenderFeatureContent(tab);
        if (string.Equals(_currentSection, "cards", StringComparison.Ordinal) &&
            string.Equals(tab, "إصدار الكروت", StringComparison.Ordinal))
        {
            PageTitleTextBlock.FontSize = 19;
        }
        else
        {
            PageTitleTextBlock.ClearValue(System.Windows.Controls.TextBlock.FontSizeProperty);
        }

        // Restore the compact breadcrumb after section/tab title state is refreshed.
        // Keep the heading small and show the active cards sub-page in the path.
        UpdatePageBreadcrumb(_currentSection, string.Equals(_currentSection, "cards", StringComparison.Ordinal) ? tab : null);

        if (SectionFeatureContentHost.Content is INetworkOperationsRefreshable refreshable)
            refreshable.RefreshFromSharedState();

        if (SectionFeatureContentHost.Content is GeneratedCardsView cardsView)
        {
            if (_currentSection == "network-users")
            {
                var systemKey = _selectedCardSystem == DemoAccountSystem.HotSpot
                    ? "hotspot"
                    : "user-manager";
                cardsView.SetSystemFilter(systemKey);
                cardsView.SetBatchFilter("all");
                cardsView.SetStatusFilter("all");
                cardsView.SetActivityFilter(tab == "النشطون الآن" ? "active" : tab == "غير النشطين" ? "inactive" : "all");
                if (tab == "كل الحسابات") cardsView.SetSearchQuery(string.Empty);
            }
            else if (_currentSection == "cards" && tab == "إدارة الكروت")
            {
                cardsView.SetSystemFilter(_selectedCardSystem == DemoAccountSystem.HotSpot ? "hotspot" : "user-manager");
                cardsView.SetBatchFilter("all");
                cardsView.SetStatusFilter("all");
                cardsView.SetActivityFilter("all");
            }
        }

foreach (var child in SectionTabsPanel.Children.OfType<Button>())
    {
        var selected = string.Equals(
            child.Tag?.ToString(),
            tab,
            StringComparison.Ordinal);

        child.Background = selected
            ? (Brush)FindResource("PrimaryBrush")
            : Brushes.Transparent;

        child.Foreground = selected
            ? (Brush)FindResource("SurfaceBrush")
            : (Brush)FindResource("TextBrush");

        child.BorderBrush = Brushes.Transparent;
        child.BorderThickness = new Thickness(0);
    }

        var featureOwnsWorkspace = SectionFeatureContentHost.Content is
            CardIssuanceView or IssuanceWorkspaceView or CardManagementView or GeneratedCardsView or NetworkUsersView or CardBatchesView or PendingCardsView or ImportCardsView or
            PackageManagementView or SalesReportView or SalesTransactionsView or SalesPointsView or PrintQueueView or TemplateDesignerView or ReportsCenterView or NetworkSourcesView or NetworkCenterView or NetworkSitesDevicesView or NetworkClientsView or NetworkMonitoringView or NetworkMaintenanceView or NetworkAnalyticsView or SystemSettingsView or ScreenAppearanceSettingsView or WorkspaceIdentityView or BackupSettingsView;
        var compactIssuanceWorkspace = SectionFeatureContentHost.Content is IssuanceWorkspaceView;
        SectionContentBorder.Padding = compactIssuanceWorkspace ? new Thickness(6) : new Thickness(14);
        SectionTabsScrollViewer.Margin = compactIssuanceWorkspace
            ? new Thickness(0, 0, 0, 6)
            : new Thickness(0, 0, 0, 12);
        var isCardWorkspace = _currentSection == "cards" || _currentSection == "network-users";
        SectionContentBorder.Background = isCardWorkspace ? (Brush)FindResource("AppBgBrush") : Brushes.White;
        SectionContentBorder.BorderBrush = isCardWorkspace ? Brushes.Transparent : (Brush)FindResource("BorderBrushSoft");
        SectionContentBorder.BorderThickness = isCardWorkspace ? new Thickness(0) : new Thickness(1);
        SectionContentBorder.CornerRadius = isCardWorkspace ? new CornerRadius(0) : new CornerRadius(12);
        SectionContentBorder.Padding = isCardWorkspace ? new Thickness(0) : new Thickness(14);
        SectionContentPanel.Visibility = featureOwnsWorkspace ? Visibility.Collapsed : Visibility.Visible;
        SectionBodyHost.Visibility = Visibility.Collapsed;
        SectionBodyHost.Content = null;
    }

    private void RenderFeatureContent(string tab)
    {
        var cardSystemKey = _selectedCardSystem == DemoAccountSystem.HotSpot ? "hotspot" : "user-manager";
        var cacheKey = _currentSection switch
        {
            "network-center" => $"network-center|{tab}",
            "network-users" => $"network-users|{cardSystemKey}|accounts",
            "cards" when tab == "إصدار الكروت" => $"cards|{cardSystemKey}|issuance",
            "cards" when tab == "إدارة الكروت" => $"cards|{cardSystemKey}|cards-list",
            "cards" when tab == "إدارة الدفعات" => $"cards|{cardSystemKey}|batches",
            "cards" when tab == "المعلقات" => $"cards|{cardSystemKey}|pending",
            "cards" when tab == "استيراد الكروت" => $"cards|{cardSystemKey}|import",
            "packages" when tab == "كتالوج الباقات" => $"packages|{cardSystemKey}",
            "sales" when tab == "ملخص المبيعات" => "sales|summary",
            "sales" when tab == "معاملات البيع" => "sales|transactions",
            "sales" when tab == "نقاط البيع" => "sales|points",
            "printing" when tab == "تصميم القالب" => "printing|template-designer",
            "printing" => "printing|workspace",
            "networks" when tab == "مدير المستخدمين" => "networks|user-manager",
            "networks" when tab == "الهوتسبوت" => "networks|hotspot",
            "reports" => "reports|center",
            _ => $"{_currentSection}|{tab}"
        };

        if (!_featureViewCache.TryGetValue(cacheKey, out var featureView))
        {
            try
            {
                featureView = CreateFeatureView(_currentSection, tab);
                if (featureView is not null)
                    _featureViewCache[cacheKey] = featureView;
            }
            catch (Exception ex)
            {
                var logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NexoraProductDemo", "logs", "ui-runtime-errors.log");
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                    File.AppendAllText(
                        logPath,
                        $"{DateTimeOffset.Now:O} | Section={_currentSection} | Tab={tab}{Environment.NewLine}{ex}{Environment.NewLine}{new string('-', 80)}{Environment.NewLine}",
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                }
                catch
                {
                    // Keep a view-construction failure from closing the entire demo if logging is unavailable.
                }

                SectionFeatureContentHost.Content = null;
                SectionFeatureContentHost.Visibility = Visibility.Collapsed;
                SectionPlaceholderCard.Visibility = Visibility.Visible;
                SectionPlaceholderTextBlock.Text = "تعذر تحميل هذه الشاشة. بقي النظام مفتوحًا، وسُجلت تفاصيل المشكلة للتشخيص.";
                MessageBox.Show(
                    $"تعذر فتح شاشة «{tab}». لم يتم إغلاق النظام.\nتفاصيل الخطأ محفوظة في:\n{logPath}",
                    "تاب كارد", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
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
        if (section == "settings")
        {
            if (tab == "إعدادات النظام") return new SystemSettingsView();
            if (tab == "الشاشات والمظهر")
            {
                var appearanceView = new ScreenAppearanceSettingsView();
                appearanceView.PreferencesApplied += (_, _) => ApplyShellDisplayPreferences();
                return appearanceView;
            }
            if (tab == "الموقع والهوية")
            {
                var identityView = new WorkspaceIdentityView(_networkDisplayName, _locationSaved, _savedLocationSummary);
                identityView.EditLocationRequested += (_, _) => ShowLocationModal();
                return identityView;
            }
            if (tab == "النسخ الاحتياطي") return new BackupSettingsView();
        }

        if (section == "network-center")
        {
            UserControl networkView = tab switch
            {
                "المواقع والأجهزة" => new NetworkSitesDevicesView(),
                "المتصلون" => new NetworkClientsView(),
                "المراقبة" => new NetworkMonitoringView(),
                "الصيانة" => new NetworkMaintenanceView(),
                "التحليلات" => new NetworkAnalyticsView(),
                _ => new NetworkCenterView()
            };

            if (networkView is INetworkOperationsNavigationSource navigationSource)
            {
                navigationSource.NavigationRequested += (_, args) => ActivateSectionTab(args.TabName);
            }

            return networkView;
        }
        if (section == "network-users")
        {
            var view = new NetworkUsersView(_demoCardIssuanceService, _selectedCardSystem);
            return view;
        }

        if (section == "cards" && tab == "إصدار الكروت")
        {
            var view = new IssuanceWorkspaceView(_demoCardIssuanceService, _selectedCardSystem);
            view.NavigationRequested += FeatureView_NavigationRequested;
            return view;
        }

        if (section == "cards" && tab == "إدارة الكروت")
        {
            var view = new CardManagementView(_demoCardIssuanceService, _selectedCardSystem);
            view.NavigationRequested += FeatureView_NavigationRequested;
            return view;
        }

        if (section == "cards" && tab == "إدارة الدفعات")
        {
            var view = new CardBatchesView(_demoCardIssuanceService);
            view.SetSystemFilter(_selectedCardSystem == DemoAccountSystem.HotSpot ? "hotspot" : "user-manager");
            view.NavigationRequested += FeatureView_NavigationRequested;
            return view;
        }

        if (section == "cards" && tab == "المعلقات")
{
            var view = new PendingCardsView(
                _demoCardIssuanceService,
                _selectedCardSystem);

            view.NavigationRequested += FeatureView_NavigationRequested;
            return view;
        }

        if (section == "cards" && tab == "استيراد الكروت")
            {
                return new ImportCardsView(
                    _demoCardIssuanceService,
                    _selectedCardSystem);
            }

        if (section == "packages")
        {
            var provider = _selectedCardSystem == DemoAccountSystem.HotSpot
                ? "hotspot"
                : "user-manager";

            return new PackageManagementView(provider);
        }

        if (section == "sales" && tab == "ملخص المبيعات")
            return new SalesReportView(_demoSalesService);

        if (section == "sales" && tab == "معاملات البيع")
        {
            return new SalesTransactionsView(_demoSalesService);
        }

        if (section == "sales" && tab == "نقاط البيع")
        {
            var view = new SalesPointsView(_demoSalesService);
            view.NavigationRequested += FeatureView_NavigationRequested;
            return view;
        }

        if (section == "printing" && tab == "تصميم القالب")
            return new TemplateDesignerView();

        if (section == "printing")
        {
            var view = new PrintQueueView(_demoCardIssuanceService);
            view.NavigationRequested += FeatureView_NavigationRequested;
            return view;
        }

        if (section == "networks")
        {
            var area = tab == "مدير المستخدمين"
        ? "user-manager"
    : tab == "الهوتسبوت"
    ? "hotspot"
        : "sources";
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

            if (e.SectionKey == "hotspot")
                HotSpotCardSystemTab.IsChecked = true;
            else
                UserManagerCardSystemTab.IsChecked = true;

            ActivateSectionTab("كل الحسابات");
            }
        else
        {
            NavigateTo(e.SectionKey);
            ActivateSectionTab(e.TabName);
        }
        if (e.ContextKind == "card" && SectionFeatureContentHost.Content is NetworkUsersView networkUsersView)
        {
            if (!string.IsNullOrWhiteSpace(e.SystemKey)) networkUsersView.SetSystemFilter(e.SystemKey);
            networkUsersView.SetBatchFilter("all");
            networkUsersView.SetActivityFilter("all");
            networkUsersView.SetStatusFilter("all");
            networkUsersView.OpenCardDetails(e.ContextValue ?? string.Empty);
        }
        else if (e.ContextKind == "card" && SectionFeatureContentHost.Content is GeneratedCardsView cardsView)
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
        else if (e.ContextKind == "batch" && SectionFeatureContentHost.Content is NetworkUsersView usersInBatchView)
        {
            if (!string.IsNullOrWhiteSpace(e.SystemKey)) usersInBatchView.SetSystemFilter(e.SystemKey);
            usersInBatchView.SetActivityFilter("all");
            usersInBatchView.SetStatusFilter("all");
            usersInBatchView.SetBatchFilter(e.ContextValue ?? "all");
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


    private void DashboardRefreshButton_Click(object sender, RoutedEventArgs e) =>
        UpdateDashboardMetrics();

    private void DashboardBatchOpen_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string batchId ||
            string.IsNullOrWhiteSpace(batchId))
            return;

        if (_locationRequiredForCurrentAccount && !_locationSaved)
        {
            ShowLocationModal();
            return;
        }

        NavigateTo("cards");
        ActivateSectionTab("إدارة الدفعات");

        if (SectionFeatureContentHost.Content is CardBatchesView batchesView)
            batchesView.FocusBatch(batchId);
    }

    private void UpdateDashboardMetrics()
    {
        if (DashboardAvailableCardsMetricTextBlock is null ||
        DashboardIssuanceDaysItemsControl is null ||
        DashboardIssuanceChartFooterTextBlock is null ||
        DashboardIssuanceTotalMetricTextBlock is null ||
        DashboardIssuanceAverageMetricTextBlock is null ||
        DashboardIssuancePeakMetricTextBlock is null ||
        DashboardIssuancePeakDayTextBlock is null ||
        DashboardSalesDaysItemsControl is null ||
        DashboardSalesChartFooterTextBlock is null ||
        DashboardRecentBatchesGrid is null)
    {
        return;
    }

        var cards = _demoCardIssuanceService.Cards.ToArray();
        var total = cards.Length;
        var available = cards.Count(card => string.Equals(
            card.DemoStatus, "متاحة", StringComparison.Ordinal));
        var inUse = cards.Count(card => string.Equals(
            card.DemoStatus, "قيد الاستخدام", StringComparison.Ordinal));
        var expired = cards.Count(card => string.Equals(
            card.DemoStatus, "منتهية", StringComparison.Ordinal));
        var suspended = cards.Count(card => string.Equals(
            card.DemoStatus, "موقوفة", StringComparison.Ordinal));

        var batchRows = cards
            .GroupBy(card => Convert.ToString(card.BatchId, CultureInfo.CurrentCulture) ?? "—",
                StringComparer.Ordinal)
            .Select(group =>
            {
                var latestCard = group
                    .OrderByDescending(card => ToDashboardDateTime(card.CreatedAt))
                    .First();

                var syncStates = group
                    .Select(card => ClassifyDashboardSyncState(card.LocalStatus))
                    .ToArray();

                var attentionCards = syncStates.Count(state =>
                    state is DashboardSyncState.Failed or DashboardSyncState.Review);

                var hasPendingSync = syncStates.Any(state => state == DashboardSyncState.Pending);
                var statusLabel = attentionCards > 0
                    ? "تحتاج متابعة"
                    : hasPendingSync ? "قيد الإجراء" : "مسجلة";

                var packageName = group
                    .Select(card => Convert.ToString(card.PackageName, CultureInfo.CurrentCulture))
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "—";

                return new DashboardBatchRow(
                    group.Key,
                    packageName,
                    group.Count(),
                    FormatDashboardDate(latestCard.CreatedAt),
                    statusLabel,
                    ToDashboardDateTime(latestCard.CreatedAt),
                    attentionCards);
            })
            .OrderByDescending(batch => batch.SortAt)
            .ToArray();

        var attentionBatches = batchRows.Count(batch => batch.AttentionCardCount > 0);
        var attentionCardsCount = batchRows.Sum(batch => batch.AttentionCardCount);

        DashboardAvailableCardsMetricTextBlock.Text = available.ToString("N0", CultureInfo.CurrentCulture);
        DashboardAvailableCardsMetricDetailTextBlock.Text = total == 0
            ? "لا توجد كروت مسجلة"
            : $"{Percent(available, total):0.#}% من سجل الكروت";

        DashboardAttentionBatchesMetricTextBlock.Text =
            attentionBatches.ToString("N0", CultureInfo.CurrentCulture);
        DashboardAttentionBatchesMetricDetailTextBlock.Text = attentionBatches == 0
            ? "لا توجد دفعات تحتاج مراجعة"
            : $"{attentionCardsCount:N0} كرت ضمن الدفعات المتأثرة";

        DashboardInUseCardsMetricTextBlock.Text = inUse.ToString("N0", CultureInfo.CurrentCulture);
        DashboardInUseCardsMetricDetailTextBlock.Text = "وفق حالة الكرت المسجلة";
        DashboardRevenueMetricTextBlock.Text =
        $"{_demoSalesService.TotalRevenue:N0} ر.ي";

        DashboardCardsSoldMetricTextBlock.Text =
        _demoSalesService.TotalCardsSold.ToString("N0");


        // Daily sales chart sourced from the demo sales service.
        var salesDays = _demoSalesService.GetDailySummaries();

        DashboardSalesDaysItemsControl.ItemsSource = salesDays;

        DashboardSalesChartFooterTextBlock.Text = salesDays.Count == 0
            ? "لا توجد معاملات مبيعات في السيناريو الحالي."
            : $"سيناريو تجريبي · {salesDays[0].Day:dd/MM/yyyy} – " +
        $"{salesDays[salesDays.Count - 1].Day:dd/MM/yyyy} · " +
        $"إجمالي الفترة: {_demoSalesService.TotalRevenue.ToString("N0", CultureInfo.CurrentCulture)} ر.ي · بيانات غير حية.";

        // Card status panel: retain the existing status drill-down behavior.
        CardStatusTotalTextBlock.Text = $"إجمالي الكروت: {total:N0}";
        AvailableCardCountTextBlock.Text = available.ToString("N0", CultureInfo.CurrentCulture);
        InUseCardCountTextBlock.Text = inUse.ToString("N0", CultureInfo.CurrentCulture);
        ExpiredCardCountTextBlock.Text = expired.ToString("N0", CultureInfo.CurrentCulture);
        SuspendedCardCountTextBlock.Text = suspended.ToString("N0", CultureInfo.CurrentCulture);
        AvailableCardsProgressBar.Value = Percent(available, total);
        InUseCardsProgressBar.Value = Percent(inUse, total);
        ExpiredCardsProgressBar.Value = Percent(expired, total);
        SuspendedCardsProgressBar.Value = Percent(suspended, total);

        // Classify only records that explicitly mention sync/provisioning; never treat every local card as pending.
        var syncStatesForCards = cards
            .Select(card => ClassifyDashboardSyncState(card.LocalStatus))
            .Where(state => state != DashboardSyncState.Unknown)
            .ToArray();

        var syncSuccess = syncStatesForCards.Count(state => state == DashboardSyncState.Succeeded);
        var syncPending = syncStatesForCards.Count(state => state == DashboardSyncState.Pending);
        var syncFailed = syncStatesForCards.Count(state => state == DashboardSyncState.Failed);
        var syncReview = syncStatesForCards.Count(state => state == DashboardSyncState.Review);
        var trackedSyncCount = syncStatesForCards.Length;
        var syncSuccessPercent = trackedSyncCount == 0 ? 0 : syncSuccess * 100d / trackedSyncCount;

        DashboardSyncSuccessCountTextBlock.Text = syncSuccess.ToString("N0", CultureInfo.CurrentCulture);
        DashboardSyncPendingCountTextBlock.Text = syncPending.ToString("N0", CultureInfo.CurrentCulture);
        DashboardSyncFailedCountTextBlock.Text = syncFailed.ToString("N0", CultureInfo.CurrentCulture);
        DashboardSyncReviewCountTextBlock.Text = syncReview.ToString("N0", CultureInfo.CurrentCulture);
        DashboardSyncSuccessPercentTextBlock.Text = trackedSyncCount == 0
            ? "—"
            : $"{syncSuccessPercent:0}%";
        DashboardSyncProgressArcPath.Data = trackedSyncCount == 0
            ? Geometry.Empty
            : CreateDashboardArcGeometry(syncSuccessPercent);
        DashboardSyncFooterTextBlock.Text = trackedSyncCount == 0
            ? "لا توجد حالات مزامنة قابلة للتصنيف ضمن السجلات الحالية."
            : $"تم التصنيف من {trackedSyncCount:N0} سجلًا يحمل حالة تزويد أو مزامنة.";

        // Issuance analytics: current seven days versus the preceding seven days.
        var today = DateTime.Now.Date;
        var arabicCulture = CultureInfo.GetCultureInfo("ar");

        var dailyCounts = Enumerable.Range(0, 7)
            .Select(offset =>
            {
                var day = today.AddDays(offset - 6);
                var count = cards.Count(card =>
                    ToDashboardDateTime(card.CreatedAt).Date == day);

                return (Day: day, Count: count);
            })
            .ToArray();

// Previous seven calendar days: from today - 13 through today - 7.
        var previousWeekCounts = Enumerable.Range(0, 7)
            .Select(offset =>
            {
                var day = today.AddDays(offset - 13);
                return cards.Count(card =>
                    ToDashboardDateTime(card.CreatedAt).Date == day);
            })
            .ToArray();

        var currentTotal = dailyCounts.Sum(item => item.Count);
        var previousTotal = previousWeekCounts.Sum();
        var dailyAverage = currentTotal / 7d;

        var peakDay = dailyCounts
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Day)
            .First();

        // Summary metrics use locally recorded card creation dates.
        DashboardIssuanceTotalMetricTextBlock.Text =
            currentTotal.ToString("N0", CultureInfo.CurrentCulture);

        DashboardIssuanceAverageMetricTextBlock.Text =
            dailyAverage.ToString("N1", CultureInfo.CurrentCulture);

        DashboardIssuancePeakMetricTextBlock.Text =
            peakDay.Count.ToString("N0", CultureInfo.CurrentCulture);

        DashboardIssuancePeakDayTextBlock.Text = peakDay.Count == 0
            ? "لا يوجد إصدار مسجل"
            : peakDay.Day.ToString("dddd dd/MM", arabicCulture);

        // Keep the existing seven-day chart data contract.
        var maxDailyCount = Math.Max(1, dailyCounts.Max(item => item.Count));

        var chartRows = dailyCounts
            .Select(item => new DashboardIssuanceDay(
                item.Day.Date == today
                ? "اليوم"
                : item.Day.ToString("ddd", arabicCulture),
                item.Day.ToString("dd/MM", CultureInfo.CurrentCulture),
                item.Count,
                item.Count.ToString("N0", CultureInfo.CurrentCulture),
                item.Count == 0
                    ? 4
                    : Math.Max(6, 68d * item.Count / maxDailyCount)))
            .ToArray();
        DashboardIssuanceDaysItemsControl.ItemsSource = chartRows;

        // Compare with the preceding week without inventing a growth percentage
        // when that preceding period had no recorded issuance.
        if (previousTotal == 0)
        {
            DashboardIssuanceChartFooterTextBlock.Text = currentTotal == 0
                ? "آخر 7 أيام: لا يوجد إصدار مسجل · الأسبوع السابق: لا يوجد إصدار مسجل."
                : $"إجمالي آخر 7 أيام: {currentTotal:N0} كرت · " +
                "لم يُسجل إصدار في الأسبوع السابق للمقارنة.";
        }
        else
        {
            var difference = currentTotal - previousTotal;
            var changePercent = difference * 100d / previousTotal;

            var differenceText = difference.ToString(
                "+#,0;-#,0;0",
                CultureInfo.CurrentCulture);

            var percentageText = changePercent.ToString(
                "+0.0;-0.0;0.0",
                CultureInfo.CurrentCulture);

            DashboardIssuanceChartFooterTextBlock.Text =
                $"آخر 7 أيام: {currentTotal:N0} كرت · " +
                $"مقارنة بالأسبوع السابق: {differenceText} كرت " +
                $"({percentageText}%).";
        }

        // Recent batches and activity are derived from saved card records and the last verified router read.
        DashboardRecentBatchesGrid.ItemsSource = batchRows.Take(5).ToArray();
        DashboardRecentBatchesEmptyPanel.Visibility = batchRows.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        var activity = batchRows
            .Take(5)
            .Select(batch => new DashboardActivityRow(
                $"دفعة إصدار · {batch.BatchId}",
                $"{batch.CardCount:N0} كرت · {batch.PackageName}",
                batch.SortAt == DateTime.MinValue ? "—" : batch.SortAt.ToString("HH:mm", CultureInfo.CurrentCulture),
                "\uE8A5",
                batch.SortAt))
            .ToList();

        if (RouterConnectionSession.CurrentSnapshot is { } currentSnapshot)
        {
            var readAt = ToDashboardDateTime(currentSnapshot.CollectedAt);
            activity.Add(new DashboardActivityRow(
                "آخر قراءة للراوتر",
                $"{currentSnapshot.Identity} · {currentSnapshot.Interfaces.Count:N0} واجهة",
                readAt == DateTime.MinValue ? "—" : readAt.ToString("HH:mm", CultureInfo.CurrentCulture),
                "\uE968",
                readAt));
        }

        var recentActivity = activity
        .OrderByDescending(item => item.SortAt)
        .Take(3)
        .ToArray();

        DashboardRecentActivityItemsControl.ItemsSource = recentActivity;
        DashboardRecentActivityEmptyPanel.Visibility = recentActivity.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        UpdateDashboardRouterStatus();
        var viewUpdatedAt = DateTime.Now.ToString("HH:mm:ss", CultureInfo.CurrentCulture);
        DashboardLastUpdatedTextBlock.Text = $"تحديث العرض {viewUpdatedAt}";
    }

    private void UpdateDashboardRouterStatus()
    {
        if (DashboardRouterStatusBadgeTextBlock is null ||
            DashboardNetworkStatusBadgeTextBlock is null)
            return;

        string statusLabel;
        string identity;
        string interfaceSummary;
        string activeInterfaceSummary;
        string lastRead;
        string helpText;
        Brush badgeBackground;
        Brush badgeForeground;
        Brush statusDot;

        if (RouterConnectionSession.CurrentSnapshot is { } snapshot)
        {
            statusLabel = "متصل";
            identity = string.IsNullOrWhiteSpace(snapshot.Identity) ? "راوتر متصل" : snapshot.Identity;
            interfaceSummary = snapshot.Interfaces.Count.ToString("N0", CultureInfo.CurrentCulture);
            activeInterfaceSummary = snapshot.Interfaces.Count(item => item.Running)
                .ToString("N0", CultureInfo.CurrentCulture);
            lastRead = $"آخر قراءة ناجحة: {snapshot.CollectedAt:HH:mm:ss}";
            helpText = "الحالة مبنية على آخر قراءة HTTPS ناجحة، وليست فحصًا لحظيًا مستمرًا.";
            badgeBackground = new SolidColorBrush(Color.FromRgb(220, 252, 231));
            badgeForeground = new SolidColorBrush(Color.FromRgb(22, 132, 91));
            statusDot = new SolidColorBrush(Color.FromRgb(22, 163, 106));
        }
        else if (_savedRouterProfile is not null)
        {
            statusLabel = "اتصال محفوظ";
            identity = _savedRouterProfile.Address;
            interfaceSummary = "—";
            activeInterfaceSummary = "—";
            lastRead = "لا توجد قراءة حالية";
            helpText = "بيانات الاتصال محفوظة، لكن لم يتم التحقق من حالة الراوتر في هذه الجلسة.";
            badgeBackground = new SolidColorBrush(Color.FromRgb(255, 244, 220));
            badgeForeground = new SolidColorBrush(Color.FromRgb(154, 103, 0));
            statusDot = new SolidColorBrush(Color.FromRgb(183, 121, 8));
        }
        else
        {
            statusLabel = "غير متصل";
            identity = "لم تُسجل قراءة ناجحة للراوتر";
            interfaceSummary = "—";
            activeInterfaceSummary = "—";
            lastRead = "آخر قراءة: غير متاحة";
            helpText = "اختبر اتصال HTTPS للراوتر لعرض بياناته الحقيقية هنا.";
            badgeBackground = new SolidColorBrush(Color.FromRgb(254, 226, 226));
            badgeForeground = new SolidColorBrush(Color.FromRgb(185, 28, 28));
            statusDot = new SolidColorBrush(Color.FromRgb(220, 38, 38));
        }

        // Detailed middle-row network panel.
        DashboardNetworkStatusBadge.Background = badgeBackground;
        DashboardNetworkStatusBadgeTextBlock.Text = statusLabel;
        DashboardNetworkStatusBadgeTextBlock.Foreground = badgeForeground;
        DashboardNetworkStatusDot.Fill = statusDot;
        DashboardNetworkIdentityTextBlock.Text = identity;
        DashboardNetworkInterfacesTextBlock.Text = interfaceSummary;
        DashboardNetworkActiveInterfacesTextBlock.Text = activeInterfaceSummary;
        DashboardNetworkLastReadTextBlock.Text = lastRead;
        DashboardNetworkStatusHelpTextBlock.Text = helpText;

        // Compact lower-row router connection summary, using the same source of truth.
        DashboardRouterStatusBadge.Background = badgeBackground;
        DashboardRouterStatusBadgeTextBlock.Text = statusLabel;
        DashboardRouterStatusBadgeTextBlock.Foreground = badgeForeground;
        DashboardRouterStatusDot.Fill = statusDot;
        DashboardRouterIdentityTextBlock.Text = identity;
        DashboardRouterInterfacesTextBlock.Text = statusLabel == "متصل"
            ? $"الواجهات المقروءة: {interfaceSummary}"
            : "الواجهات المقروءة: غير متاحة";
        DashboardRouterLastReadTextBlock.Text = lastRead;
        DashboardRouterStatusHelpTextBlock.Text = helpText;
    }

    private static DashboardSyncState ClassifyDashboardSyncState(object? rawStatus)
    {
        var status = Convert.ToString(rawStatus, CultureInfo.CurrentCulture) ?? string.Empty;
        if (status.Length == 0)
            return DashboardSyncState.Unknown;

        var mentionsProvisioning = status.Contains("مزامنة", StringComparison.OrdinalIgnoreCase)
            || status.Contains("تزويد", StringComparison.OrdinalIgnoreCase)
            || status.Contains("sync", StringComparison.OrdinalIgnoreCase)
            || status.Contains("provision", StringComparison.OrdinalIgnoreCase);

        if (!mentionsProvisioning)
            return DashboardSyncState.Unknown;

        if (status.Contains("فشل", StringComparison.OrdinalIgnoreCase)
            || status.Contains("تعذر", StringComparison.OrdinalIgnoreCase)
            || status.Contains("خطأ", StringComparison.OrdinalIgnoreCase))
            return DashboardSyncState.Failed;

        if (status.Contains("مراجعة", StringComparison.OrdinalIgnoreCase)
            || status.Contains("تعارض", StringComparison.OrdinalIgnoreCase)
            || status.Contains("غير مؤكد", StringComparison.OrdinalIgnoreCase))
            return DashboardSyncState.Review;

        if (status.Contains("نجح", StringComparison.OrdinalIgnoreCase)
            || status.Contains("ناجح", StringComparison.OrdinalIgnoreCase)
            || status.Contains("مكتملة", StringComparison.OrdinalIgnoreCase)
            || status.Contains("تمت مزامنة", StringComparison.OrdinalIgnoreCase))
            return DashboardSyncState.Succeeded;

        if (status.Contains("انتظار", StringComparison.OrdinalIgnoreCase)
            || status.Contains("معلق", StringComparison.OrdinalIgnoreCase)
            || status.Contains("قيد التنفيذ", StringComparison.OrdinalIgnoreCase)
            || status.Contains("مسجل", StringComparison.OrdinalIgnoreCase)
            || status.Contains("مطلوب", StringComparison.OrdinalIgnoreCase)
            || status.Contains("طلب", StringComparison.OrdinalIgnoreCase))
            return DashboardSyncState.Pending;

        // A status explicitly about provisioning but not understood must not be shown as successful.
        return DashboardSyncState.Pending;
    }

    private static DateTime ToDashboardDateTime(object? value)
    {
        if (value is DateTime dateTime)
            return dateTime;

        if (value is DateTimeOffset dateTimeOffset)
            return dateTimeOffset.LocalDateTime;

        var text = Convert.ToString(value, CultureInfo.CurrentCulture);
        return DateTime.TryParse(text, CultureInfo.CurrentCulture,
            DateTimeStyles.AssumeLocal, out var parsed)
            ? parsed
            : DateTime.MinValue;
    }

    private static string FormatDashboardDate(object? value)
    {
        var parsed = ToDashboardDateTime(value);
        return parsed == DateTime.MinValue
            ? "—"
            : parsed.ToString("dd/MM HH:mm", CultureInfo.CurrentCulture);
    }

    private static Geometry CreateDashboardArcGeometry(double percent)
    {
        var sweepAngle = Math.Clamp(percent, 0, 100) * 3.6;
        if (sweepAngle <= 0.1)
            return Geometry.Empty;

        // Avoid a zero-length end segment when the percentage is 100%.
        sweepAngle = Math.Min(359.8, sweepAngle);
        const double center = 47;
        const double radius = 37;

        static Point PointOnCircle(double cx, double cy, double r, double angleDegrees)
        {
            var radians = angleDegrees * Math.PI / 180d;
            return new Point(cx + r * Math.Cos(radians), cy + r * Math.Sin(radians));
        }

        var start = PointOnCircle(center, center, radius, -90);
        var end = PointOnCircle(center, center, radius, -90 + sweepAngle);
        var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment(
            end,
            new Size(radius, radius),
            0,
            sweepAngle > 180,
            SweepDirection.Clockwise,
            true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
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
        "packages" => "تعريف باقات الاتصال التجارية وربطها بإعدادات المصدر.",
        "sales" => "معاملات البيع وتوزيع المبيعات وفق السجلات المتاحة.",
        "printing" => tab == "تصميم القالب" ? "تصميم قوالب الكروت وإدارة ظهور الحقول وحفظ إعدادات القالب محليًا." : "إدارة قائمة الكروت ومراجعة المعاينة وإرسال مهمة الطباعة.",
        "networks" => "إعداد مصادر المصادقة وربط وظائفها بوحدات Nexora.",
        "reports" => "التقارير التجارية والإدارية والتشغيلية مجمّعة حسب مصدرها.",
        "network-center" => tab switch
        {
            "نظرة عامة" => "ملخص تشغيل موحد للأجهزة والمواقع والتنبيهات والمهام المفتوحة.",
            "المواقع والأجهزة" => "حصر الأجهزة بحسب الدور والموقع وعلاقة الاتصال؛ الحالات التمثيلية موضحة داخل الشاشة.",
            "المتصلون" => "عرض جلسات HotSpot وPPPoE منفصلة عن روابط CPE والأجهزة النهائية.",
            "المراقبة" => "مراجعة التنبيهات التجريبية وربطها بإجراء صيانة واضح.",
            "الصيانة" => "إدارة مهام الفحص والمتابعة المرتبطة بالأجهزة والتنبيهات.",
            _ => "تحليل بيانات السيناريو التجريبي حسب الفترة والموقع، دون الادعاء بوجود قياس مباشر."
        },
        "settings" => tab switch
        {
            "إعدادات النظام" => "تحديد صفحة البداية وتفضيلات الإجراءات والبيانات المحلية.",
            "الشاشات والمظهر" => "تطبيق كثافة مريحة أو مدمجة على القائمة الجانبية وتبويبات الأقسام.",
            "الموقع والهوية" => "مراجعة اسم مساحة الشبكة وموقعها وتحديث المعلومات المحفوظة محليًا.",
            "النسخ الاحتياطي" => "إنشاء حزمة محلية من ملفات بيانات الديمو المحفوظة ومراجعة سجل النسخ.",
            _ => "إعدادات النظام والواجهة."
        },
        _ => $"{tab} ضمن إدارة Nexora."
    };

    private static string GetSectionPlaceholder(string section, string tab) => (section, tab) switch
    {
        ("settings", "الموقع والهوية") => "إدارة معلومات الشبكة وموقعها والعناصر المرتبطة بمساحة العمل.",
        ("settings", "النسخ الاحتياطي") => "مراجعة خيارات الاحتفاظ بنسخة احتياطية من بيانات التطبيق.",
        ("settings", "الشاشات والمظهر") => "ضبط كثافة التنقل وطريقة عرض عناصر الواجهة.",
        ("settings", _) => "إعدادات النظام والواجهة والبيانات المحلية.",
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

    private void ToggleSidebarButton_Click(object sender, RoutedEventArgs e)
    {
        _isSidebarCollapsed = !_isSidebarCollapsed;
        bool collapsed = _isSidebarCollapsed;

        WorkspaceView.ColumnDefinitions[0].Width = new GridLength(collapsed ? 64 : 224);

        SidebarBrandTopPanel.Margin = collapsed
            ? new Thickness(2,  12, 2, 12)
            : new Thickness(13, 12, 13, 12);

        SidebarBrandGrid.ColumnDefinitions[0].Width = new GridLength(collapsed ? 40 : 50);
        SidebarBrandGrid.ColumnDefinitions[1].Width = new GridLength(collapsed ? 0 : 8);
        SidebarBrandMarkBorder.Width = collapsed ? 40 : 48;
        SidebarBrandMarkBorder.Height = collapsed ? 40 : 46;
        SidebarBrandDetails.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;

        SidebarItemsPanel.Margin = collapsed
            ? new Thickness(3, 0, 3, 8)
            : new Thickness(9, 0, 9, 8);

        foreach (UIElement child in SidebarItemsPanel.Children)
        {
            if (child is Button button)
            {
                button.Padding = collapsed ? new Thickness(0) : new Thickness(10, 3, 10, 3);
                SetSidebarNavigationLayout(button, collapsed);
                SetSidebarLabelsVisibility(button, collapsed ? Visibility.Collapsed : Visibility.Visible);
            }
            else if (child is TextBlock heading)
            {
                heading.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            }
            else if (child is Border separator && !double.IsNaN(separator.Height) && separator.Height <= 1)
            {
                separator.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        SidebarNetworkFooterBorder.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        SidebarBottomPanel.Margin = collapsed
            ? new Thickness(4, 12, 4, 12)
            : new Thickness(14, 12, 14, 12);

        SignOutButton.Content = collapsed ? "↪" : "تسجيل الخروج";
        SignOutButton.Width = collapsed ? 44 : double.NaN;
        SignOutButton.Padding = collapsed ? new Thickness(0) : new Thickness(14, 0, 14, 0);
        SidebarToggleButton.ToolTip = collapsed ? "توسيع القائمة الجانبية" : "طي القائمة الجانبية";
    }

    private static void SetSidebarLabelsVisibility(DependencyObject parent, Visibility visibility)
    {
        if (parent is TextBlock textBlock)
        {
            string family = textBlock.FontFamily?.Source ?? string.Empty;
            if (!string.Equals(family, "Segoe MDL2 Assets", StringComparison.OrdinalIgnoreCase))
            {
                textBlock.Visibility = visibility;
            }
            return;
        }

        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            SetSidebarLabelsVisibility(VisualTreeHelper.GetChild(parent, i), visibility);
        }
    }

    private static void SetSidebarNavigationLayout(DependencyObject parent, bool collapsed)
    {
        if (parent is Grid grid && grid.ColumnDefinitions.Count == 2)
        {
            grid.ColumnDefinitions[0].Width = collapsed
                ? new GridLength(1, GridUnitType.Star)
                : new GridLength(30, GridUnitType.Pixel);
            grid.ColumnDefinitions[1].Width = collapsed
                ? new GridLength(0, GridUnitType.Pixel)
                : new GridLength(1, GridUnitType.Star);
        }

        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            SetSidebarNavigationLayout(VisualTreeHelper.GetChild(parent, i), collapsed);
        }
    }

    private void SignOutButton_Click(object sender, RoutedEventArgs e)
    {
        RouterConnectionSession.Disconnect();
        _savedRouterProfile = null;
        LocationModalOverlay.Visibility = Visibility.Collapsed;
        WorkspaceView.IsEnabled = true;
        WorkspaceView.Visibility = Visibility.Collapsed;
        LoginView.Visibility = Visibility.Visible;
        _currentAccountKey = null;
        _locationRequiredForCurrentAccount = false;
        _locationEditWasOnboarding = false;
        _locationSaved = false;
        _savedLocationSummary = null;
        LocationBadgeTextBlock.Visibility = Visibility.Collapsed;
        RestoreLoginWindowChrome();
        WindowState = WindowState.Normal;
        MinWidth = 520; MinHeight = 480; MaxWidth = 920; MaxHeight = 820;
        Width = Math.Min(660, SystemParameters.WorkArea.Width - 40);
        Height = Math.Min(630, SystemParameters.WorkArea.Height - 40);
        Title = "تاب كارد | كروت الشبكات";
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
