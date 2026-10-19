using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Nexora.Demo.Desktop.Features.NetworkOperations.Connections;
using Nexora.Demo.Desktop.Features.NetworkOperations.DemoData;
using Nexora.Demo.Desktop.Features.NetworkOperations.Models;
using Nexora.Demo.Desktop.Features.NetworkOperations.Monitoring;

namespace Nexora.Demo.Desktop.Features.NetworkOperations.Views;

public sealed class NetworkSiteSummaryRow
{
    public string Name { get; init; } = string.Empty;
    public string Area { get; init; } = string.Empty;
    public int DeviceCount { get; init; }
    public string HealthLabel { get; init; } = string.Empty;
}

public partial class NetworkCenterView : UserControl,
    INetworkOperationsNavigationSource, INetworkOperationsRefreshable
{
    private const string SimulationMarker = "[NEXORA-SIM]";

    public event EventHandler<NetworkOperationsNavigationRequestedEventArgs>? NavigationRequested;

    public ObservableCollection<NetworkAlertDemo> Alerts => NetworkOperationsDemoStore.Alerts;
    public ObservableCollection<NetworkInterfaceIncident> InterfaceIncidents => NetworkInterfaceIncidentStore.Incidents;
    public ObservableCollection<NetworkMaintenanceTaskDemo> Tasks => NetworkOperationsDemoStore.MaintenanceTasks;
    public ObservableCollection<NetworkSiteSummaryRow> SiteSummaries { get; } = new();

    public NetworkCenterView()
    {
        InitializeComponent();
        InitializeRouterConnectionSession();
        DataContext = this;

        SimulatorDeviceComboBox.ItemsSource = NetworkOperationsDemoStore.Devices;
        SimulatorScenarioComboBox.Items.Add("انقطاع الجهاز");
        SimulatorScenarioComboBox.Items.Add("تدهور الإشارة");
        SimulatorScenarioComboBox.SelectedIndex = 0;
        SimulatorDeviceComboBox.SelectedItem = NetworkOperationsDemoStore.Devices.FirstOrDefault();

        RefreshFromSharedState();
        StatusTextBlock.Text = "البيانات محلية تجريبية. لم يتم الاتصال براوتر أو جهاز حقيقي.";
    }

    public void RefreshFromSharedState() => RefreshDashboard();

    private void RefreshDashboard()
    {
        var devices = NetworkOperationsDemoStore.Devices;
        var online = devices.Count(device => device.Status.StartsWith("متصل", StringComparison.Ordinal));
        var openAlerts = NetworkOperationsDemoStore.Alerts.Count(alert => alert.Status != "تمت المراجعة");
        var openTasks = NetworkOperationsDemoStore.MaintenanceTasks.Count(task => task.Status != "مكتملة");

if (!TryRenderLiveRouterDashboard())
        {
            DeviceCountTextBlock.Text = devices.Count.ToString("N0");
            OnlineCountTextBlock.Text = online.ToString("N0");
            AlertCountTextBlock.Text = openAlerts.ToString("N0");
            TaskCountTextBlock.Text = openTasks.ToString("N0");
        }

        RebuildSiteSummaries();
        RebuildTopology();
        AlertsItemsControl.Items.Refresh();
        TasksItemsControl.Items.Refresh();
    }

    private void RebuildSiteSummaries()
    {
        SiteSummaries.Clear();
        foreach (var site in NetworkOperationsDemoStore.Sites)
        {
            var matched = NetworkOperationsDemoStore.Devices.Where(device =>
                string.Equals(device.SiteName, site.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(device.SiteName, site.Area, StringComparison.OrdinalIgnoreCase)).ToList();

            var online = matched.Count(device => device.Status.StartsWith("متصل", StringComparison.Ordinal));
            var review = matched.Count(device => device.Status.Contains("مراجعة", StringComparison.Ordinal) ||
                                                   device.Status.Contains("غير متصل", StringComparison.Ordinal));
            var health = matched.Count == 0
                ? "لا توجد سجلات أجهزة مطابقة"
                : review > 0
                    ? $"{online:N0} متصلة في السيناريو · {review:N0} تحتاج مراجعة"
                    : $"{online:N0} متصلة في السيناريو";

            SiteSummaries.Add(new NetworkSiteSummaryRow
            {
                Name = site.Name,
                Area = site.Area,
                DeviceCount = matched.Count,
                HealthLabel = health
            });
        }
    }

    private static int GetDepth(
        NetworkDeviceDemo device,
        IReadOnlyDictionary<string, NetworkDeviceDemo> byName,
        Dictionary<string, int> cache,
        HashSet<string>? trail = null)
    {
        if (cache.TryGetValue(device.Id, out var cached)) return cached;
        var parentName = device.ParentDevice;
        if (string.IsNullOrWhiteSpace(parentName) || parentName == "—" ||
            !byName.TryGetValue(parentName, out var parent))
        {
            cache[device.Id] = 0;
            return 0;
        }

        trail ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!trail.Add(device.Name)) return 0;
        var depth = GetDepth(parent, byName, cache, trail) + 1;
        trail.Remove(device.Name);
        cache[device.Id] = depth;
        return depth;
    }

    private sealed class RouterMapNode
    {
        public string Name { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string MacAddress { get; set; } = string.Empty;
        public string InterfaceName { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
    }

    private void RebuildTopology()
    {
        NetworkTopologyCanvas.Children.Clear();
        var snapshot = _lastLiveRouterSnapshot;
        if (snapshot is null)
        {
            NetworkTopologyCanvas.Width = 780;
            NetworkTopologyCanvas.Height = 170;
            NetworkTopologyCanvas.Children.Add(new TextBlock
            {
                Text = "لا توجد خريطة فعلية بعد. افتح زر «اتصال الراوتر» في رأس التطبيق واختبر الاتصال.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 700,
                Margin = new Thickness(18, 65, 18, 0)
            });
            RouterMapSummaryTextBlock.Text = "لا توجد قراءة فعّالة من راوتر في هذه الجلسة.";
            return;
        }

        var nodes = new List<RouterMapNode>();
        foreach (var neighbor in snapshot.Neighbors)
        {
            var node = new RouterMapNode
            {
                Name = string.IsNullOrWhiteSpace(neighbor.Identity)
                    ? (!string.IsNullOrWhiteSpace(neighbor.Address) ? neighbor.Address : neighbor.MacAddress)
                    : neighbor.Identity,
                Address = neighbor.Address,
                MacAddress = neighbor.MacAddress,
                InterfaceName = neighbor.InterfaceName,
                Source = "Neighbor Discovery" + (string.IsNullOrWhiteSpace(neighbor.DiscoveredBy) ? string.Empty : " · " + neighbor.DiscoveredBy)
            };
            nodes.Add(node);
        }

        foreach (var arp in snapshot.ArpEntries)
        {
            var node = nodes.FirstOrDefault(existing =>
                (!string.IsNullOrWhiteSpace(arp.MacAddress) && string.Equals(existing.MacAddress, arp.MacAddress, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrWhiteSpace(arp.Address) && string.Equals(existing.Address, arp.Address, StringComparison.OrdinalIgnoreCase)));
            if (node is null)
            {
                nodes.Add(new RouterMapNode
                {
                    Name = string.IsNullOrWhiteSpace(arp.Address) ? arp.MacAddress : arp.Address,
                    Address = arp.Address,
                    MacAddress = arp.MacAddress,
                    InterfaceName = arp.InterfaceName,
                    Source = "سجل ARP · " + arp.State
                });
            }
            else
            {
                if (string.IsNullOrWhiteSpace(node.Address)) node.Address = arp.Address;
                if (string.IsNullOrWhiteSpace(node.MacAddress)) node.MacAddress = arp.MacAddress;
                if (string.IsNullOrWhiteSpace(node.InterfaceName)) node.InterfaceName = arp.InterfaceName;
                if (!node.Source.Contains("ARP", StringComparison.OrdinalIgnoreCase)) node.Source += " · ARP (" + arp.State + ")";
            }
        }

        var routerAddress = RouterConnectionSession.CurrentOptions?.BaseAddress.GetLeftPart(UriPartial.Authority) ?? "عنوان غير متاح";
        const double nodeWidth = 224;
        const double nodeHeight = 84;
        const double stepX = 246;
        const double stepY = 108;
        const int columns = 3;
        var rows = Math.Max(1, (int)Math.Ceiling(nodes.Count / (double)columns));
        var canvasWidth = Math.Max(780, columns * stepX + 20);
        var canvasHeight = nodes.Count == 0 ? 230 : 145 + rows * stepY;
        NetworkTopologyCanvas.Width = canvasWidth;
        NetworkTopologyCanvas.Height = canvasHeight;

        var rootX = (canvasWidth - nodeWidth) / 2d;
        var rootCard = new Border
        {
            Width = nodeWidth,
            Height = 82,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(166, 25, 31)),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10)
        };
        var rootPanel = new StackPanel();
        rootPanel.Children.Add(new TextBlock
        {
            Text = snapshot.Identity + " · الراوتر المتصل",
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        rootPanel.Children.Add(new TextBlock
        {
            Text = routerAddress,
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            Margin = new Thickness(0, 4, 0, 0)
        });
        rootPanel.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(snapshot.BoardName) ? snapshot.Platform : snapshot.BoardName,
            FontSize = 9.5,
            Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 3, 0, 0)
        });
        rootCard.Child = rootPanel;
        Canvas.SetLeft(rootCard, rootX);
        Canvas.SetTop(rootCard, 16);
        Panel.SetZIndex(rootCard, 2);
        NetworkTopologyCanvas.Children.Add(rootCard);

        if (nodes.Count == 0)
        {
            NetworkTopologyCanvas.Children.Add(new TextBlock
            {
                Text = "لم يعرض الراوتر جيرانًا أو سجلات ARP لهذه القراءة. هذا لا يثبت خلو الشبكة من الأجهزة.",
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 700,
                Margin = new Thickness(18, 125, 18, 0)
            });
            RouterMapSummaryTextBlock.Text = $"الراوتر: {snapshot.Identity} · جيران مكتشفون: {snapshot.Neighbors.Count:N0} · سجلات ARP: {snapshot.ArpEntries.Count:N0} · لا توجد عُقد إضافية في مصادر الاكتشاف الحالية.";
            return;
        }

        for (var index = 0; index < nodes.Count; index++)
        {
            var node = nodes[index];
            var column = index % columns;
            var row = index / columns;
            var rowWidth = columns * nodeWidth + (columns - 1) * (stepX - nodeWidth);
            var rowStartX = (canvasWidth - rowWidth) / 2d;
            var x = rowStartX + column * stepX;
            var y = 142 + row * stepY;

            var link = new Line
            {
                X1 = rootX + nodeWidth / 2,
                Y1 = 98,
                X2 = x + nodeWidth / 2,
                Y2 = y,
                Stroke = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection { 4, 3 },
                IsHitTestVisible = false
            };
            NetworkTopologyCanvas.Children.Add(link);

            var card = new Border
            {
                Width = nodeWidth,
                Height = nodeHeight,
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(9),
                ToolTip = node.Source
            };
            var body = new StackPanel();
            body.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(node.Name) ? "جهاز مكتشف" : node.Name,
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            body.Children.Add(new TextBlock
            {
                Text = string.Join(" · ", new[] { node.Address, node.MacAddress }.Where(value => !string.IsNullOrWhiteSpace(value))),
                FontSize = 9.5,
                Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 4, 0, 0)
            });
            body.Children.Add(new TextBlock
            {
                Text = "الواجهة: " + (string.IsNullOrWhiteSpace(node.InterfaceName) ? "غير محددة" : node.InterfaceName),
                FontSize = 9.5,
                Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                Margin = new Thickness(0, 3, 0, 0)
            });
            body.Children.Add(new TextBlock
            {
                Text = node.Source,
                FontSize = 9,
                Foreground = new SolidColorBrush(Color.FromRgb(166, 25, 31)),
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 3, 0, 0)
            });
            card.Child = body;
            Canvas.SetLeft(card, x);
            Canvas.SetTop(card, y);
            Panel.SetZIndex(card, 2);
            NetworkTopologyCanvas.Children.Add(card);
        }

        RouterMapSummaryTextBlock.Text = $"الراوتر: {snapshot.Identity} · {nodes.Count:N0} عنوان/جهاز جرى دمجه من جداول الاكتشاف · الجيران: {snapshot.Neighbors.Count:N0} · ARP: {snapshot.ArpEntries.Count:N0}. هذه خريطة رصد، وليست ضمانًا لاكتمال الأجهزة أو إثباتًا للتوصيلات المادية.";
    }


    private static (Color Color, string Label) GetStatusStyle(string status)
    {
        if (status.StartsWith("متصل", StringComparison.Ordinal))
            return (Color.FromRgb(22, 132, 87), status.Contains("محاكاة", StringComparison.Ordinal) ? "متصل · محاكاة" : "متصل في السيناريو");
        if (status.Contains("مراجعة", StringComparison.Ordinal))
            return (Color.FromRgb(194, 89, 24), "يحتاج مراجعة");
        if (status.Contains("غير متصل", StringComparison.Ordinal))
            return (Color.FromRgb(185, 28, 28), "غير متصل");
        return (Color.FromRgb(100, 116, 139), "غير معروف / غير مختبر");
    }

private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshDashboard();
        if (_lastLiveRouterSnapshot is null)
        {
            StatusTextBlock.Text = $"تم تحديث ملخص بيانات الديمو عند {DateTime.Now:HH:mm:ss}. لا يوجد اتصال فعلي بالشبكة.";
        }
        else
        {
            StatusTextBlock.Text = $"تم تحديث قوائم الديمو عند {DateTime.Now:HH:mm:ss}. المؤشرات الأربع بالأعلى من آخر قراءة فعلية لـ RouterOS عند {_lastLiveRouterSnapshot.CollectedAt:HH:mm:ss}؛ بقية الخريطة والتنبيهات والمهام ما زالت تجريبية.";
        }
    }

    private void RunScenarioButton_Click(object sender, RoutedEventArgs e)
    {
        if (SimulatorDeviceComboBox.SelectedItem is not NetworkDeviceDemo device)
        {
            SimulatorResultTextBlock.Text = "اختر جهازًا من القائمة أو اضغط على جهاز في الخريطة أولًا.";
            return;
        }

        var scenario = SimulatorScenarioComboBox.SelectedItem as string;
        if (scenario == "انقطاع الجهاز")
        {
            device.Status = "غير متصل (محاكاة)";
            device.LastSeen = "الآن (محاكاة)";
            AddOrUpdateSimulationAlert(device, "انقطاع اتصال محاكى", "مرتفع",
                $"تم تغيير حالة {device.Name} إلى غير متصل داخل السيناريو. هذه ليست نتيجة فحص فعلي.");
            SimulatorResultTextBlock.Text = $"تمت محاكاة انقطاع {device.Name}. تغيّرت حالة السجل وأُضيف تنبيه مفتوح مشترك مع شاشة المراقبة.";
        }
        else if (scenario == "تدهور الإشارة")
        {
            device.Status = "يحتاج مراجعة (محاكاة)";
            device.Signal = "-82 dBm (محاكاة)";
            device.LastSeen = "الآن (محاكاة)";
            AddOrUpdateSimulationAlert(device, "تدهور إشارة محاكى", "تحذير",
                $"تم تمثيل تدهور إشارة {device.Name} داخل الديمو. قيمة الإشارة محاكاة وليست قراءة راديوية فعلية.");
            SimulatorResultTextBlock.Text = $"تمت محاكاة تدهور الإشارة للجهاز {device.Name}. تغيّرت الحالة والإشارة التجريبية وأُنشئ تنبيه.";
        }
        else
        {
            SimulatorResultTextBlock.Text = "اختر انقطاع الجهاز أو تدهور الإشارة.";
            return;
        }

        LastCheckTextBlock.Text = $"آخر سيناريو: {scenario} · الجهاز: {device.Name} · {DateTime.Now:HH:mm:ss}";
        StatusTextBlock.Text = "تم تغيير بيانات السيناريو محليًا. لم يتم إرسال أي طلب أو أمر إلى جهاز شبكة.";
        RefreshDashboard();
    }

    private void RestoreDeviceButton_Click(object sender, RoutedEventArgs e)
    {
        if (SimulatorDeviceComboBox.SelectedItem is not NetworkDeviceDemo device)
        {
            SimulatorResultTextBlock.Text = "اختر الجهاز الذي تريد استعادته أولًا.";
            return;
        }

        device.Status = "متصل (محاكاة)";
        device.LastSeen = "الآن (محاكاة)";
        if (device.Signal.Contains("محاكاة", StringComparison.Ordinal)) device.Signal = "-60 dBm (محاكاة)";

        foreach (var alert in NetworkOperationsDemoStore.Alerts.Where(alert =>
                     alert.DeviceName == device.Name &&
                     alert.Description.Contains(SimulationMarker, StringComparison.Ordinal) &&
                     alert.Status != "تمت المراجعة"))
        {
            alert.Status = "تمت المراجعة";
            alert.Age = "تمت المراجعة الآن (محاكاة)";
            alert.Description += " تمت الاستعادة في السيناريو.";
        }

        SimulatorResultTextBlock.Text = $"تمت محاكاة استعادة {device.Name}. أُغلقت تنبيهات المحاكاة النشطة المرتبطة به، وستنعكس الحالة في الملخص والخريطة.";
        LastCheckTextBlock.Text = $"آخر سيناريو: استعادة الاتصال · الجهاز: {device.Name} · {DateTime.Now:HH:mm:ss}";
        StatusTextBlock.Text = "استعادة محاكاة محلية فقط. لم يتم اختبار اتصال الجهاز الحقيقي.";
        RefreshDashboard();
    }

    private static void AddOrUpdateSimulationAlert(NetworkDeviceDemo device, string title, string severity, string description)
    {
        var active = NetworkOperationsDemoStore.Alerts.FirstOrDefault(alert =>
            alert.DeviceName == device.Name &&
            alert.Description.Contains(SimulationMarker, StringComparison.Ordinal) &&
            alert.Status != "تمت المراجعة");

        if (active is not null)
        {
            active.Title = $"{title} — {device.Name}";
            active.Severity = severity;
            active.Age = "الآن (محاكاة)";
            active.Description = $"{SimulationMarker} {description}";
            active.Status = "مفتوح";
            return;
        }

        NetworkOperationsDemoStore.Alerts.Insert(0, new NetworkAlertDemo
        {
            Id = NetworkOperationsDemoStore.NewId("ALT"),
            Title = $"{title} — {device.Name}",
            DeviceName = device.Name,
            SiteName = device.SiteName,
            Severity = severity,
            Age = "الآن (محاكاة)",
            Description = $"{SimulationMarker} {description}",
            Status = "مفتوح"
        });
    }

    private void OpenAlertButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string alertId } &&
            NetworkOperationsDemoStore.Alerts.Any(alert => alert.Id == alertId))
        {
            NetworkOperationsDemoStore.RequestedAlertId = alertId;
            NavigateTo("المراقبة");
        }
    }

    private void OpenMonitoringButton_Click(object sender, RoutedEventArgs e) => NavigateTo("المراقبة");
    private void OpenDevicesButton_Click(object sender, RoutedEventArgs e) => NavigateTo("المواقع والأجهزة");
    private void OpenClientsButton_Click(object sender, RoutedEventArgs e) => NavigateTo("المتصلون");
    private void OpenMaintenanceButton_Click(object sender, RoutedEventArgs e) => NavigateTo("الصيانة");
    private void OpenAnalyticsButton_Click(object sender, RoutedEventArgs e) => NavigateTo("التحليلات");

    private void NavigateTo(string tabName) => NavigationRequested?.Invoke(
        this, new NetworkOperationsNavigationRequestedEventArgs(tabName));
}

