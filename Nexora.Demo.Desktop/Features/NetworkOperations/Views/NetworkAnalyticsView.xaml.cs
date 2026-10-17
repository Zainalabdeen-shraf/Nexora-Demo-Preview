using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Nexora.Demo.Desktop.Features.NetworkOperations.DemoData;
using Nexora.Demo.Desktop.Features.NetworkOperations.Models;

namespace Nexora.Demo.Desktop.Features.NetworkOperations.Views;

public partial class NetworkAnalyticsView : UserControl, INetworkOperationsNavigationSource, INetworkOperationsRefreshable
{
    private bool _initializing = true;
    public event EventHandler<NetworkOperationsNavigationRequestedEventArgs>? NavigationRequested;
    public ObservableCollection<NetworkUsageBreakdownDemo> UsageBySite { get; } = new();

    public NetworkAnalyticsView()
    {
        InitializeComponent();
        DataContext = this;
        PeriodComboBox.Items.Add("اليوم");
        PeriodComboBox.Items.Add("آخر 7 أيام");
        PeriodComboBox.Items.Add("آخر 30 يومًا");
        PeriodComboBox.SelectedIndex = 1;
        _initializing = false;
        RefreshFromSharedState();
    }

    private void PeriodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing) RefreshFromSharedState();
    }

    public void RefreshFromSharedState()
    {
        var source = NetworkOperationsDemoStore.UsageBySite.ToList();
        var sourceTotal = source.Sum(item => item.DownloadGb);
        var period = PeriodComboBox.SelectedItem as string ?? "آخر 7 أيام";
        var days = period switch
        {
            "اليوم" => 1m,
            "آخر 30 يومًا" => 30m,
            _ => 7m
        };
        // The current sample is treated as a seven-day reference window. Other periods are estimates derived from that shared sample.
        var totalForPeriod = sourceTotal * days / 7m;

        UsageBySite.Clear();
        foreach (var item in source)
        {
            var scaled = sourceTotal <= 0 ? 0m : Math.Round(totalForPeriod * item.DownloadGb / sourceTotal, 1);
            var share = totalForPeriod <= 0 ? 0 : (int)Math.Round((scaled / totalForPeriod) * 100m);
            UsageBySite.Add(new NetworkUsageBreakdownDemo
            {
                Name = item.Name,
                DownloadGb = scaled,
                SharePercent = share,
                Caption = FormatVolume(scaled)
            });
        }

        UsageTotalTextBlock.Text = FormatVolume(totalForPeriod);
        TopSiteTextBlock.Text = UsageBySite.OrderByDescending(item => item.DownloadGb).FirstOrDefault()?.Name ?? "—";

        var devices = NetworkOperationsDemoStore.Devices;
        var online = devices.Count(item => item.Status.StartsWith("متصل", StringComparison.Ordinal));
        var review = devices.Count(item => item.Status.Contains("مراجعة", StringComparison.Ordinal) || item.Status.Contains("غير متصل", StringComparison.Ordinal));
        var unknown = devices.Count(item => item.Status.Contains("غير معروف", StringComparison.Ordinal) || item.Status.Contains("غير مختبر", StringComparison.Ordinal));
        DevicesCountTextBlock.Text = $"من {devices.Count:N0} سجلًا";
        DevicesNeedReviewTextBlock.Text = review.ToString("N0");
        ScenarioOnlineTextBlock.Text = online.ToString("N0");
        ScenarioReviewTextBlock.Text = review.ToString("N0");
        ScenarioUnknownTextBlock.Text = unknown.ToString("N0");
        AnalyticsStatusTextBlock.Text = $"تم اشتقاق عرض «{period}» من مرجع السيناريو البالغ {FormatVolume(sourceTotal)} لسبعة أيام، عند {DateTime.Now:HH:mm:ss}.";
    }

    private static string FormatVolume(decimal gigabytes) => gigabytes >= 1000m
        ? $"{gigabytes / 1000m:N1} TB"
        : $"{gigabytes:N0} GB";

    private void OpenDevicesButton_Click(object sender, RoutedEventArgs e) =>
        NavigationRequested?.Invoke(this, new NetworkOperationsNavigationRequestedEventArgs("المواقع والأجهزة"));

    private void OpenClientsButton_Click(object sender, RoutedEventArgs e) =>
        NavigationRequested?.Invoke(this, new NetworkOperationsNavigationRequestedEventArgs("المتصلون"));
}