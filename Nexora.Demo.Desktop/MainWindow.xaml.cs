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
        RouterConnectionOverlay.Visibility = Visibility.Visible;
        RouterConnectionAddressTextBox.Focus();
    }

    private void CloseRouterConnectionOverlay_Click(object sender, RoutedEventArgs e)
    {
        RouterConnectionOverlay.Visibility = Visibility.Collapsed;
        RefreshRouterConnectionHeaderStatus();
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
            PageTitleTextBlock.Text = key switch
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
            "settings" => "إعدادات النظام",
            _ => "تاب كارد"
        };
        PageTitleIconTextBlock.Text = key switch
        {
            "dashboard" => "\uE80F",
            "network-users" => "\uE77B",
            "cards" => "\uE8C7",
            "packages" => "\uE8D4",
            "sales" => "\uE7BF",
            "printing" => "\uE749",
            "networks" => "\uE701",
            "network-center" => "\uE968",
            "reports" => "\uE9D2",
            "settings" => "\uE713",
            _ => "\uE80F"
        };
        PageSubtitleTextBlock.Text = key switch
        {
            "network-center" => "متابعة المواقع والأجهزة والتنبيهات ومهام الصيانة.",
            "settings" => "تهيئة خيارات النظام والواجهة والهوية وإدارة النسخ الاحتياطية المحلية.",
            "dashboard" => "نظرة موحدة على الكروت والمبيعات ومصادر المصادقة",
            "cards" => "إصدار الكروت وإدارة الدفعات",
            _ => "كل وظيفة في مكانها، مع ربط واضح بين الإصدار والإدارة والتقارير"
        };
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
            btn.MouseEnter += (_, _) =>
{
    var isSelected = string.Equals(
        btn.Tag?.ToString(),
        SectionContentTitleTextBlock.Text,
        StringComparison.Ordinal);

    if (!isSelected)
        btn.Background = new SolidColorBrush(Color.FromRgb(252, 237, 239));
};

btn.MouseLeave += (_, _) =>
{
    var isSelected = string.Equals(
        btn.Tag?.ToString(),
        SectionContentTitleTextBlock.Text,
        StringComparison.Ordinal);

    btn.Background = isSelected
        ? (Brush)FindResource("PrimaryBrush")
        : Brushes.Transparent;

    btn.Foreground = isSelected
        ? (Brush)FindResource("SurfaceBrush")
        : (Brush)FindResource("TextBrush");

    btn.BorderBrush = Brushes.Transparent;
    btn.BorderThickness = new Thickness(0);
};
SectionTabsPanel.Children.Add(btn);
            }
            ApplyShellDisplayPreferences();
            ActivateSectionTab(tabs[0]);
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
                "cards" => "\uE8C7",
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
                "dashboard" => "نظرة موحدة على الكروت والمبيعات ومصادر المصادقة",
                "cards" => "إصدار الكروت وإدارة الدفعات",
                _ => "كل وظيفة في مكانها، مع ربط واضح بين الإصدار والإدارة والتقارير"
            };
        }
        SectionContentDescriptionTextBlock.Text = GetSectionDescription(_currentSection, tab);
        SectionTabsScrollViewer.Visibility = _currentSection == "packages" ? Visibility.Collapsed : Visibility.Visible;
        SectionPlaceholderTextBlock.Text = GetSectionPlaceholder(_currentSection, tab);
        RenderFeatureContent(tab);
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
            CardIssuanceView or IssuanceWorkspaceView or GeneratedCardsView or CardBatchesView or CardBatchesView or PendingCardsView or ImportCardsView or
            PackageManagementView or SalesReportView or SalesTransactionsView or SalesPointsView or PrintQueueView or TemplateDesignerView or ReportsCenterView or NetworkSourcesView or NetworkCenterView or NetworkSitesDevicesView or NetworkClientsView or NetworkMonitoringView or NetworkMaintenanceView or NetworkAnalyticsView or SystemSettingsView or ScreenAppearanceSettingsView or WorkspaceIdentityView or BackupSettingsView;
        var compactIssuanceWorkspace = SectionFeatureContentHost.Content is IssuanceWorkspaceView;
        SectionContentBorder.Padding = compactIssuanceWorkspace ? new Thickness(6) : new Thickness(14);
        SectionTabsScrollViewer.Margin = compactIssuanceWorkspace
            ? new Thickness(0, 0, 0, 6)
            : new Thickness(0, 0, 0, 12);
        var isCardWorkspace = _currentSection == "cards";
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
            var view = new GeneratedCardsView(
                _demoCardIssuanceService,
                _selectedCardSystem);

            view.NavigationRequested += FeatureView_NavigationRequested;

            view.SetSystemFilter(
                _selectedCardSystem == DemoAccountSystem.HotSpot
                    ? "hotspot"
                    : "user-manager");

            view.SetSystemFilterControlVisible(false);

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
            var view = new GeneratedCardsView(_demoCardIssuanceService, _selectedCardSystem);
            view.NavigationRequested += FeatureView_NavigationRequested;
            view.SetSystemFilter(_selectedCardSystem == DemoAccountSystem.HotSpot ? "hotspot" : "user-manager");
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


