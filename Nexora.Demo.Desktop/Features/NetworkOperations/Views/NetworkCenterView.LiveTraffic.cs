using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Nexora.Demo.Desktop.Features.NetworkOperations.Connections;
using Nexora.Demo.Desktop.Features.NetworkOperations.Monitoring;

namespace Nexora.Demo.Desktop.Features.NetworkOperations.Views;

public sealed class RouterTrafficRow
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string RunningText { get; set; } = string.Empty;
    public string ReceiveRate { get; set; } = "بانتظار عينة";
    public string TransmitRate { get; set; } = "بانتظار عينة";
    public string RxBytes { get; set; } = "—";
    public string TxBytes { get; set; } = "—";
    public string LinkDowns { get; set; } = "—";
}

public partial class NetworkCenterView
{
    private sealed record TrafficCounter(long? RxBytes, long? TxBytes, DateTimeOffset CollectedAt);
    private sealed record TrafficPoint(DateTimeOffset CollectedAt, double ReceiveMbps, double TransmitMbps);

    private sealed class InterfaceHealthCounter
    {
        public int ConsecutiveDownSamples { get; set; }
        public int ConsecutiveUpSamples { get; set; }
        public DateTimeOffset? FirstDownAt { get; set; }
    }

    private readonly Dictionary<string, InterfaceHealthCounter> _interfaceHealthCounters = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<RouterTrafficRow> LiveTrafficRows { get; } = new();

    private RouterOsRestClient? _liveTrafficClient;
    private DispatcherTimer? _trafficTimer;
    private CancellationTokenSource? _trafficPollCancellation;
    private readonly Dictionary<string, TrafficCounter> _previousTrafficCounters = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<TrafficPoint>> _trafficHistory = new(StringComparer.OrdinalIgnoreCase);
    private bool _trafficPollInProgress;
    private bool _settingTrafficInterface;
    private bool _trafficExitHookAttached;
    private const int MaxTrafficHistorySamples = 60;

    private void StartLiveTrafficPolling(RouterOsConnectionOptions options, RouterOsConnectionSnapshot snapshot)
    {
        StopLiveTrafficPolling(clearDisplay: false);
        _liveTrafficClient = new RouterOsRestClient(options);
        _trafficPollCancellation = new CancellationTokenSource();
        LiveTrafficRows.Clear();
        _previousTrafficCounters.Clear();
        _trafficHistory.Clear();

        foreach (var item in snapshot.Interfaces)
        {
            _previousTrafficCounters[item.Name] = new TrafficCounter(item.RxBytes, item.TxBytes, snapshot.CollectedAt);
            _trafficHistory[item.Name] = new List<TrafficPoint>();
            LiveTrafficRows.Add(CreateTrafficRow(item, null, null));
        }

        ProcessInterfaceHealthSamples(snapshot.Interfaces, snapshot.CollectedAt);
        RouterTrafficDataGrid.ItemsSource = LiveTrafficRows;
        _settingTrafficInterface = true;
        var names = snapshot.Interfaces.Select(item => item.Name).Where(name => !string.IsNullOrWhiteSpace(name)).ToList();
        TrafficInterfaceComboBox.ItemsSource = names;
        var initialInterface = names.FirstOrDefault(name => string.Equals(name, "ether2", StringComparison.OrdinalIgnoreCase))
            ?? names.FirstOrDefault();
        TrafficInterfaceComboBox.SelectedItem = initialInterface;
        _settingTrafficInterface = false;

        TrafficSamplingStatusTextBlock.Text =
            $"بدأ جمع العينات من {snapshot.Interfaces.Count:N0} واجهة عند {snapshot.CollectedAt:HH:mm:ss}. سيظهر معدل Mbps بعد القراءة التالية، ويحتاج الخط إلى عينتين محسوبتين.";
        DrawTrafficChart();

        if (Application.Current is not null && !_trafficExitHookAttached)
        {
            Application.Current.Exit += LiveTraffic_ApplicationExit;
            _trafficExitHookAttached = true;
        }

        if (_trafficTimer is null)
        {
            _trafficTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _trafficTimer.Tick += TrafficTimer_Tick;
        }
        _trafficTimer.Start();
    }

    private void StopLiveTrafficPolling(bool clearDisplay)
    {
        _trafficTimer?.Stop();
        if (_trafficPollCancellation is not null)
        {
            try { _trafficPollCancellation.Cancel(); } catch (ObjectDisposedException) { }
            _trafficPollCancellation.Dispose();
            _trafficPollCancellation = null;
        }
        _liveTrafficClient?.Dispose();
        _liveTrafficClient = null;
        _previousTrafficCounters.Clear();
        _trafficHistory.Clear();
        _interfaceHealthCounters.Clear();

        if (clearDisplay)
        {
            LiveTrafficRows.Clear();
            _settingTrafficInterface = true;
            TrafficInterfaceComboBox.ItemsSource = null;
            TrafficInterfaceComboBox.SelectedItem = null;
            _settingTrafficInterface = false;
            TrafficHistoryCanvas.Children.Clear();
            TrafficSamplingStatusTextBlock.Text = "توقف جمع العينات ومسح سجل حركة المرور لهذه الجلسة.";
        }
    }

    private void LiveTraffic_ApplicationExit(object? sender, ExitEventArgs e)
    {
        StopLiveTrafficPolling(clearDisplay: false);
    }

    private async void TrafficTimer_Tick(object? sender, EventArgs e)
    {
        if (_trafficPollInProgress || _liveTrafficClient is null || _trafficPollCancellation is null)
            return;

        var client = _liveTrafficClient;
        var sessionCancellation = _trafficPollCancellation;
        _trafficPollInProgress = true;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(sessionCancellation.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            var interfaces = await client.ReadInterfaceStatsAsync(timeout.Token);
            if (sessionCancellation.IsCancellationRequested || !ReferenceEquals(client, _liveTrafficClient))
                return;

            ApplyLiveTrafficRead(interfaces, DateTimeOffset.Now);
        }
        catch (OperationCanceledException) when (sessionCancellation.IsCancellationRequested)
        {
            // The user cleared/replaced the connection or the application is closing.
        }
        catch (OperationCanceledException)
        {
            MarkInterfaceIncidentReadFailed("انتهت مهلة قراءة الواجهات.");
            TrafficSamplingStatusTextBlock.Text =
                $"انتهت مهلة قراءة الواجهات عند {DateTimeOffset.Now:HH:mm:ss}. ستبقى آخر عينة معروضة إلى أن تنجح قراءة جديدة.";
        }
        catch (RouterOsConnectionException ex)
        {
            MarkInterfaceIncidentReadFailed(ex.UserMessage);
            TrafficSamplingStatusTextBlock.Text =
                $"تعذر تحديث عينات المرور عند {DateTimeOffset.Now:HH:mm:ss}: {ex.UserMessage} آخر عينة محفوظة في الذاكرة لم تتغير.";
        }
        catch (HttpRequestException)
        {
            MarkInterfaceIncidentReadFailed("انقطع اتصال HTTPS بالراوتر.");
            TrafficSamplingStatusTextBlock.Text =
                $"انقطع اتصال HTTPS عند {DateTimeOffset.Now:HH:mm:ss}. آخر عينة معروضة قديمة حتى تنجح قراءة جديدة.";
        }
        catch (Exception ex)
        {
            MarkInterfaceIncidentReadFailed(ex.Message);
            TrafficSamplingStatusTextBlock.Text =
                $"حدث خطأ غير متوقع أثناء قراءة الواجهات عند {DateTimeOffset.Now:HH:mm:ss}. لم تُرسل أوامر تغيير.";
        }
        finally
        {
            _trafficPollInProgress = false;
        }
    }

    private void ApplyLiveTrafficRead(IReadOnlyList<RouterOsInterfaceSnapshot> interfaces, DateTimeOffset collectedAt)
    {
        ProcessInterfaceHealthSamples(interfaces, collectedAt);
        foreach (var item in interfaces)
        {
            double? receiveMbps = null;
            double? transmitMbps = null;
            if (_previousTrafficCounters.TryGetValue(item.Name, out var previous))
            {
                var seconds = (collectedAt - previous.CollectedAt).TotalSeconds;
                receiveMbps = CalculateMbps(item.RxBytes, previous.RxBytes, seconds);
                transmitMbps = CalculateMbps(item.TxBytes, previous.TxBytes, seconds);

                if (receiveMbps.HasValue && transmitMbps.HasValue)
                {
                    if (!_trafficHistory.TryGetValue(item.Name, out var history))
                    {
                        history = new List<TrafficPoint>();
                        _trafficHistory[item.Name] = history;
                    }
                    history.Add(new TrafficPoint(collectedAt, receiveMbps.Value, transmitMbps.Value));
                    if (history.Count > MaxTrafficHistorySamples)
                        history.RemoveRange(0, history.Count - MaxTrafficHistorySamples);
                }
            }

            _previousTrafficCounters[item.Name] = new TrafficCounter(item.RxBytes, item.TxBytes, collectedAt);
            var row = LiveTrafficRows.FirstOrDefault(current =>
                string.Equals(current.Name, item.Name, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                row = CreateTrafficRow(item, receiveMbps, transmitMbps);
                LiveTrafficRows.Add(row);
            }
            else
            {
                CopyTrafficRow(row, item, receiveMbps, transmitMbps);
            }
        }

        // Counters shown in the original interface table are also refreshed from the latest REST response.
        RouterInterfacesDataGrid.ItemsSource = interfaces;
        RouterInterfacesDataGrid.Items.Refresh();
        RouterTrafficDataGrid.Items.Refresh();

        DeviceCountTextBlock.Text = interfaces.Count.ToString("N0");
        OnlineCountTextBlock.Text = interfaces.Count(item => item.Running).ToString("N0");
        AlertCountTextBlock.Text = interfaces.Count(item => !item.Running).ToString("N0");
        LastCheckTextBlock.Text = $"آخر قراءة فعلية لعدادات الواجهات: {collectedAt:HH:mm:ss} · {interfaces.Count:N0} واجهة.";

        var selectedName = TrafficInterfaceComboBox.SelectedItem as string;
        var sampleCount = selectedName is not null && _trafficHistory.TryGetValue(selectedName, out var selectedHistory)
            ? selectedHistory.Count
            : 0;
        var rates = selectedName is not null ? LiveTrafficRows.FirstOrDefault(row => row.Name == selectedName) : null;
        var rateText = rates is null
            ? string.Empty
            : $" · RX {rates.ReceiveRate} · TX {rates.TransmitRate}";
        TrafficSamplingStatusTextBlock.Text =
            $"قراءة RouterOS فعلية عند {collectedAt:HH:mm:ss} · {interfaces.Count:N0} واجهة · تاريخ {sampleCount:N0} عينة للواجهة المختارة{rateText}.";
        DrawTrafficChart();
    }

    private static double? CalculateMbps(long? current, long? previous, double seconds)
    {
        if (!current.HasValue || !previous.HasValue || seconds <= 0)
            return null;
        var delta = current.Value - previous.Value;
        if (delta < 0)
            return null; // The router counter was reset or wrapped; establish a new baseline.
        return (delta * 8d) / seconds / 1_000_000d;
    }

    private static RouterTrafficRow CreateTrafficRow(
        RouterOsInterfaceSnapshot item, double? receiveMbps, double? transmitMbps)
    {
        var row = new RouterTrafficRow();
        CopyTrafficRow(row, item, receiveMbps, transmitMbps);
        return row;
    }

    private static void CopyTrafficRow(
        RouterTrafficRow row, RouterOsInterfaceSnapshot item, double? receiveMbps, double? transmitMbps)
    {
        row.Name = item.Name;
        row.Type = item.Type;
        row.RunningText = item.Running ? "تعمل" : item.Disabled ? "معطلة" : "لا تعمل";
        row.ReceiveRate = receiveMbps.HasValue ? receiveMbps.Value.ToString("N3", System.Globalization.CultureInfo.InvariantCulture) : "بانتظار عينة";
        row.TransmitRate = transmitMbps.HasValue ? transmitMbps.Value.ToString("N3", System.Globalization.CultureInfo.InvariantCulture) : "بانتظار عينة";
        row.RxBytes = item.RxBytes?.ToString("N0") ?? "غير متاح";
        row.TxBytes = item.TxBytes?.ToString("N0") ?? "غير متاح";
        row.LinkDowns = item.LinkDowns?.ToString("N0") ?? "غير متاح";
    }

    private void TrafficInterfaceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_settingTrafficInterface)
            DrawTrafficChart();
    }

    private void TrafficHistoryCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        DrawTrafficChart();
    }

    private void DrawTrafficChart()
    {
        if (TrafficHistoryCanvas is null) return;
        TrafficHistoryCanvas.Children.Clear();
        var selectedName = TrafficInterfaceComboBox.SelectedItem as string;
        var width = TrafficHistoryCanvas.ActualWidth;
        var height = TrafficHistoryCanvas.ActualHeight;
        if (width < 240) width = 620;
        if (height < 120) height = 190;

        if (string.IsNullOrWhiteSpace(selectedName) ||
            !_trafficHistory.TryGetValue(selectedName, out var history) || history.Count == 0)
        {
            AddChartMessage("اتصل بالراوتر لبدء جمع عينات فعلية من حركة المرور.", 14, Math.Max(12, height / 2 - 10));
            return;
        }

        if (history.Count < 2)
        {
            AddChartMessage("تم جمع أول عينة محسوبة. سيظهر المنحنى بعد العينة الزمنية التالية.", 14, Math.Max(12, height / 2 - 10));
            AddChartMessage($"RX {history[^1].ReceiveMbps:N3} Mbps · TX {history[^1].TransmitMbps:N3} Mbps", 14, Math.Max(30, height / 2 + 12));
            return;
        }

        const double left = 62;
        const double right = 14;
        const double top = 14;
        const double bottom = 28;
        var plotWidth = Math.Max(160, width - left - right);
        var plotHeight = Math.Max(80, height - top - bottom);
        var maxRate = Math.Max(0.01, history.Max(point => Math.Max(point.ReceiveMbps, point.TransmitMbps)) * 1.2);

        for (var step = 0; step <= 4; step++)
        {
            var y = top + plotHeight * step / 4d;
            var value = maxRate * (4 - step) / 4d;
            var line = new Line
            {
                X1 = left, Y1 = y, X2 = left + plotWidth, Y2 = y,
                Stroke = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                StrokeThickness = 1,
                IsHitTestVisible = false
            };
            TrafficHistoryCanvas.Children.Add(line);
            var label = new TextBlock { Text = $"{value:N2}", FontSize = 9, Foreground = Brushes.Gray };
            Canvas.SetLeft(label, 4);
            Canvas.SetTop(label, y - 7);
            TrafficHistoryCanvas.Children.Add(label);
        }

        var receiveLine = new Polyline
        {
            Stroke = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
            StrokeThickness = 2.2,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false
        };
        var transmitLine = new Polyline
        {
            Stroke = new SolidColorBrush(Color.FromRgb(220, 38, 38)),
            StrokeThickness = 2.2,
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false
        };

        for (var index = 0; index < history.Count; index++)
        {
            var x = left + plotWidth * index / (double)(history.Count - 1);
            var receiveY = top + plotHeight - (history[index].ReceiveMbps / maxRate * plotHeight);
            var transmitY = top + plotHeight - (history[index].TransmitMbps / maxRate * plotHeight);
            receiveLine.Points.Add(new Point(x, Math.Clamp(receiveY, top, top + plotHeight)));
            transmitLine.Points.Add(new Point(x, Math.Clamp(transmitY, top, top + plotHeight)));
        }

        TrafficHistoryCanvas.Children.Add(receiveLine);
        TrafficHistoryCanvas.Children.Add(transmitLine);
        var first = new TextBlock { Text = history[0].CollectedAt.LocalDateTime.ToString("HH:mm:ss"), FontSize = 9, Foreground = Brushes.Gray };
        Canvas.SetLeft(first, left);
        Canvas.SetTop(first, height - 19);
        TrafficHistoryCanvas.Children.Add(first);
        var lastText = new TextBlock { Text = history[^1].CollectedAt.LocalDateTime.ToString("HH:mm:ss"), FontSize = 9, Foreground = Brushes.Gray };
        Canvas.SetLeft(lastText, Math.Max(left, left + plotWidth - 56));
        Canvas.SetTop(lastText, height - 19);
        TrafficHistoryCanvas.Children.Add(lastText);
    }

    private void ProcessInterfaceHealthSamples(IReadOnlyList<RouterOsInterfaceSnapshot> interfaces, DateTimeOffset collectedAt)
    {
        foreach (var item in interfaces)
        {
            if (string.IsNullOrWhiteSpace(item.Name)) continue;
            if (!_interfaceHealthCounters.TryGetValue(item.Name, out var counter))
            {
                counter = new InterfaceHealthCounter();
                _interfaceHealthCounters[item.Name] = counter;
            }

            if (item.Disabled)
            {
                counter.ConsecutiveDownSamples = 0;
                counter.ConsecutiveUpSamples = 0;
                counter.FirstDownAt = null;
                NetworkInterfaceIncidentStore.ObserveAdministrativelyDisabled(item.Name, collectedAt);
                continue;
            }

            if (item.Running)
            {
                counter.ConsecutiveDownSamples = 0;
                counter.FirstDownAt = null;
                counter.ConsecutiveUpSamples++;
                NetworkInterfaceIncidentStore.ObserveUp(item.Name, collectedAt, counter.ConsecutiveUpSamples);
            }
            else
            {
                counter.ConsecutiveUpSamples = 0;
                if (counter.ConsecutiveDownSamples == 0) counter.FirstDownAt = collectedAt;
                counter.ConsecutiveDownSamples++;
                NetworkInterfaceIncidentStore.ObserveDown(item.Name, collectedAt, counter.ConsecutiveDownSamples, counter.FirstDownAt ?? collectedAt);
            }
        }

        InterfaceIncidentsDataGrid.Items.Refresh();
        var openCount = NetworkInterfaceIncidentStore.Incidents.Count(item => item.Status == "مفتوحة");
        var readText = $"آخر معالجة لقراءة واجهات ناجحة: {collectedAt:yyyy-MM-dd HH:mm:ss} · {interfaces.Count:N0} واجهة · {openCount:N0} حادثة مفتوحة. يتوقف أخذ العينات عند مغادرة مركز الشبكة أو انقطاع الاتصال.";
        InterfaceIncidentMonitorStatusTextBlock.Text = string.IsNullOrWhiteSpace(NetworkInterfaceIncidentStore.PersistenceError)
            ? readText + " سجل الحوادث محفوظ محليًا."
            : readText + " تحذير: " + NetworkInterfaceIncidentStore.PersistenceError;
    }

    private void MarkInterfaceIncidentReadFailed(string reason)
    {
        // A failed poll gives us no evidence for device state; break consecutive-sample streaks.
        foreach (var counter in _interfaceHealthCounters.Values)
        {
            counter.ConsecutiveDownSamples = 0;
            counter.ConsecutiveUpSamples = 0;
            counter.FirstDownAt = null;
        }
        InterfaceIncidentMonitorStatusTextBlock.Text = $"تعذرت قراءة الواجهات عند {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}: {reason} الحوادث لم تُحدّث، وتبقى آخر قراءة ناجحة هي المرجع.";
    }
    private void AddChartMessage(string text, double left, double top)
    {
        var message = new TextBlock
        {
            Text = text,
            Foreground = Brushes.Gray,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = Math.Max(200, TrafficHistoryCanvas.ActualWidth - 30)
        };
        Canvas.SetLeft(message, left);
        Canvas.SetTop(message, top);
        TrafficHistoryCanvas.Children.Add(message);
    }
}
