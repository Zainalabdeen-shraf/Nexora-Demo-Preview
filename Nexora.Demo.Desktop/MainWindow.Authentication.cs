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

// Responsibility: Login, trial registration, and session entry/exit.
public partial class MainWindow
{
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
}
