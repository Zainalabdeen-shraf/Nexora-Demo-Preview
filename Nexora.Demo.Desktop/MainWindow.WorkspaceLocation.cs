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

// Responsibility: Workspace window lifecycle and required location workflow.
public partial class MainWindow
{
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
}
