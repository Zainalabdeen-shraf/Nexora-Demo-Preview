using System.Globalization;
using System.Windows;
using Nexora.Demo.Desktop.Features.NetworkOperations.Connections;

namespace Nexora.Demo.Desktop.Features.NetworkOperations.Views;

public partial class NetworkCenterView
{
    private RouterOsConnectionSnapshot? _lastLiveRouterSnapshot;

    private void InitializeRouterConnectionSession()
    {
        RouterConnectionSession.Changed += RouterConnectionSession_Changed;
        Loaded += NetworkCenterView_Loaded;
        Unloaded += NetworkCenterView_Unloaded;
        SynchronizeRouterConnectionSession();
    }

    private void NetworkCenterView_Loaded(object sender, RoutedEventArgs e)
    {
        if (RouterConnectionSession.CurrentOptions is { } options && RouterConnectionSession.CurrentSnapshot is { } snapshot)
        {
            if (_liveTrafficClient is null)
                StartLiveTrafficPolling(options, snapshot);
            else
                _trafficTimer?.Start();
        }
    }

    private void NetworkCenterView_Unloaded(object sender, RoutedEventArgs e)
    {
        // Pause while another tab is visible; retain the client and in-memory chart history for return.
        _trafficTimer?.Stop();
    }

    private void RouterConnectionSession_Changed(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.BeginInvoke(new Action(SynchronizeRouterConnectionSession));
            return;
        }
        SynchronizeRouterConnectionSession();
    }

    private void SynchronizeRouterConnectionSession()
    {
        var options = RouterConnectionSession.CurrentOptions;
        var snapshot = RouterConnectionSession.CurrentSnapshot;
        if (options is null || snapshot is null)
        {
            _lastLiveRouterSnapshot = null;
            StopLiveTrafficPolling(clearDisplay: true);
            RouterInterfacesDataGrid.ItemsSource = null;
            RouterInterfacesDataGrid.Visibility = Visibility.Collapsed;
            RouterNeighborsDataGrid.ItemsSource = null;
            RouterNeighborsDataGrid.Visibility = Visibility.Collapsed;
            RouterArpDataGrid.ItemsSource = null;
            RouterArpDataGrid.Visibility = Visibility.Collapsed;
            RouterDiscoverySummaryTextBlock.Text = "لا يوجد اتصال فعّال بالراوتر. استخدم زر الاتصال في رأس التطبيق.";
            RouterSystemSummaryTextBlock.Text = "لا يوجد اتصال فعّال بالراوتر.";
            ConnectionModeBadgeTextBlock.Text = "ديمو محلي";
            RefreshDashboard();
            StatusTextBlock.Text = "لا يوجد اتصال فعّال بالراوتر. تبقى بيانات الخريطة والتنبيهات والمحاكاة تجريبية.";
            return;
        }

        var connectionChanged = _lastLiveRouterSnapshot is null ||
            !string.Equals(_lastLiveRouterSnapshot.Identity, snapshot.Identity, StringComparison.Ordinal) ||
            _lastLiveRouterSnapshot.CollectedAt != snapshot.CollectedAt;
        if (connectionChanged && _liveTrafficClient is not null)
            StopLiveTrafficPolling(clearDisplay: false);

        _lastLiveRouterSnapshot = snapshot;
        var memoryText = snapshot.TotalMemoryBytes.HasValue && snapshot.FreeMemoryBytes.HasValue
            ? $"الذاكرة الحرة: {FormatBytes(snapshot.FreeMemoryBytes.Value)} من {FormatBytes(snapshot.TotalMemoryBytes.Value)}"
            : "الذاكرة: غير متاحة";
        var board = string.IsNullOrWhiteSpace(snapshot.BoardName) ? snapshot.Platform : snapshot.BoardName;
        RouterSystemSummaryTextBlock.Text =
            $"الراوتر: {snapshot.Identity} · {board} · RouterOS {Fallback(snapshot.Version)} · التشغيل {Fallback(snapshot.Uptime)} · CPU {Fallback(snapshot.CpuLoad)}% · {memoryText} · آخر قراءة {snapshot.CollectedAt:HH:mm:ss}.";

        RouterInterfacesDataGrid.ItemsSource = snapshot.Interfaces;
        RouterInterfacesDataGrid.Visibility = snapshot.Interfaces.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RouterNeighborsDataGrid.ItemsSource = snapshot.Neighbors;
        RouterNeighborsDataGrid.Visibility = snapshot.Neighbors.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        RouterArpDataGrid.ItemsSource = snapshot.ArpEntries;
        RouterArpDataGrid.Visibility = snapshot.ArpEntries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        var neighborSummary = snapshot.Neighbors.Count == 0
            ? "لم يعرض الراوتر جيرانًا متوافقين في هذه القراءة؛ هذا لا يثبت خلو الشبكة من الأجهزة."
            : $"جيران Neighbor Discovery: {snapshot.Neighbors.Count:N0}.";
        var arpSummary = snapshot.ArpEntries.Count == 0
            ? "لا توجد سجلات ARP في هذه القراءة."
            : $"سجلات ARP: {snapshot.ArpEntries.Count:N0}.";
        RouterDiscoverySummaryTextBlock.Text = neighborSummary + " " + arpSummary + " قد لا تمثل هذه المصادر جميع الأجهزة.";
        RefreshDashboard();
        StatusTextBlock.Text = $"اتصال فعلي ناجح بالراوتر «{snapshot.Identity}» عند {snapshot.CollectedAt:HH:mm:ss}. معلومات الواجهات والجيران وARP وقراءات حركة المرور فعلية؛ الخريطة تمثل ما رُصد فقط، فيما تبقى سيناريوهات الأعطال والتنبيهات والصيانة محاكاة ديمو.";
        LastCheckTextBlock.Text = $"آخر قراءة فعلية من RouterOS: {snapshot.CollectedAt:HH:mm:ss} · {snapshot.Interfaces.Count:N0} واجهة.";

        if (IsLoaded)
        {
            if (_liveTrafficClient is null)
                StartLiveTrafficPolling(options, snapshot);
            else
                _trafficTimer?.Start();
        }
    }

    private bool TryRenderLiveRouterDashboard()
    {
        var snapshot = _lastLiveRouterSnapshot;
        if (snapshot is null)
        {
            ConnectionModeBadgeTextBlock.Text = "ديمو محلي";
            NetworkDataModeHeadingTextBlock.Text = "حالة مصدر البيانات";
            NetworkMetricInterfacesLabelTextBlock.Text = "أجهزة مسجلة (ديمو)";
            NetworkMetricInterfacesDetailTextBlock.Text = "لا يوجد اتصال فعّال";
            NetworkMetricRunningLabelTextBlock.Text = "واجهات تعمل (ديمو)";
            NetworkMetricRunningDetailTextBlock.Text = "ليست قراءة مباشرة";
            NetworkMetricInactiveLabelTextBlock.Text = "تنبيهات تجريبية";
            NetworkMetricInactiveDetailTextBlock.Text = "تحتاج متابعة في الديمو";
            NetworkMetricCpuLabelTextBlock.Text = "مهام مفتوحة (ديمو)";
            NetworkMetricCpuDetailTextBlock.Text = "قائمة الصيانة التجريبية";
            return false;
        }

        ConnectionModeBadgeTextBlock.Text = "راوتر متصل · بيانات مختلطة";
        NetworkDataModeHeadingTextBlock.Text = "مصادر البيانات في مركز الشبكة";
        NetworkMetricInterfacesLabelTextBlock.Text = "واجهات الراوتر";
        NetworkMetricInterfacesDetailTextBlock.Text = "قراءة فعلية من RouterOS";
        NetworkMetricRunningLabelTextBlock.Text = "واجهات تعمل";
        NetworkMetricRunningDetailTextBlock.Text = "حالة فعلية للواجهات";
        NetworkMetricInactiveLabelTextBlock.Text = "واجهات لا تعمل";
        NetworkMetricInactiveDetailTextBlock.Text = "قد تكون معطلة أو غير موصولة";
        NetworkMetricCpuLabelTextBlock.Text = "حمل CPU";
        NetworkMetricCpuDetailTextBlock.Text = $"آخر قراءة {snapshot.CollectedAt:HH:mm:ss}";
        DeviceCountTextBlock.Text = snapshot.Interfaces.Count.ToString("N0");
        OnlineCountTextBlock.Text = snapshot.Interfaces.Count(item => item.Running).ToString("N0");
        AlertCountTextBlock.Text = snapshot.Interfaces.Count(item => !item.Running).ToString("N0");
        TaskCountTextBlock.Text = string.IsNullOrWhiteSpace(snapshot.CpuLoad) ? "—" : $"{snapshot.CpuLoad}%";
        return true;
    }

    private static string Fallback(string value) => string.IsNullOrWhiteSpace(value) ? "غير متاح" : value;

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0, bytes);
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:N1} {units[unit]}";
    }
}