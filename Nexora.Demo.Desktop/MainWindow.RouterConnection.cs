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

// Responsibility: Router connection and connection profile interactions.
public partial class MainWindow
{
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
}
