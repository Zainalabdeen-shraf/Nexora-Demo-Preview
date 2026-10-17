using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Nexora.Demo.Desktop.Features.NetworkOperations.DemoData;
using Nexora.Demo.Desktop.Features.NetworkOperations.Models;

namespace Nexora.Demo.Desktop.Features.NetworkOperations.Views;

public partial class NetworkSitesDevicesView : UserControl, INetworkOperationsNavigationSource, INetworkOperationsRefreshable
{
    private bool _initializing = true;
    private NetworkDeviceDemo? _selectedDevice;
    public event EventHandler<NetworkOperationsNavigationRequestedEventArgs>? NavigationRequested;
    public ObservableCollection<NetworkDeviceDemo> VisibleDevices { get; } = new();

    private static readonly string[] DeviceTypes =
    ["راوتر رئيسي", "نقطة بث (AP)", "وصلة لاسلكية", "جهاز استقبال (CPE)", "راوتر منزلي", "سويتش / PoE", "أخرى"];

    public NetworkSitesDevicesView()
    {
        InitializeComponent();
        DataContext = this;
        DeviceTypeFilterComboBox.Items.Add("كل الأنواع");
        foreach (var item in DeviceTypes) DeviceTypeFilterComboBox.Items.Add(item);
        DeviceTypeFilterComboBox.SelectedIndex = 0;
        DeviceSiteFilterComboBox.Items.Add("كل المواقع");
        foreach (var siteName in NetworkOperationsDemoStore.Devices.Select(device => device.SiteName).Distinct()) DeviceSiteFilterComboBox.Items.Add(siteName);
        DeviceSiteFilterComboBox.SelectedIndex = 0;
        NewDeviceTypeComboBox.ItemsSource = DeviceTypes;
        NewDeviceTypeComboBox.SelectedIndex = 1;
        NewDeviceSiteComboBox.ItemsSource = NetworkOperationsDemoStore.Sites;
        NewDeviceSiteComboBox.SelectedIndex = 0;
        NewDeviceParentComboBox.ItemsSource = NetworkOperationsDemoStore.Devices;
        _initializing = false;
        ApplyFilters();
        UpdateCounters();
    }

    private void UpdateCounters()
    {
        TotalDevicesTextBlock.Text = NetworkOperationsDemoStore.Devices.Count.ToString("N0");
        TotalSitesTextBlock.Text = NetworkOperationsDemoStore.Sites.Count.ToString("N0");
        NeedsReviewTextBlock.Text = NetworkOperationsDemoStore.Devices.Count(device => device.Status.Contains("مراجعة", StringComparison.Ordinal) || device.Status.Contains("غير متصل", StringComparison.Ordinal)).ToString("N0");
    }

    public void RefreshFromSharedState()
    {
        var previousSite = DeviceSiteFilterComboBox.SelectedItem as string;
        DeviceSiteFilterComboBox.Items.Clear();
        DeviceSiteFilterComboBox.Items.Add("كل المواقع");
        foreach (var siteName in NetworkOperationsDemoStore.Devices.Select(device => device.SiteName).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct())
            DeviceSiteFilterComboBox.Items.Add(siteName);
        DeviceSiteFilterComboBox.SelectedItem = previousSite is not null && DeviceSiteFilterComboBox.Items.Contains(previousSite)
            ? previousSite
            : "كل المواقع";
        if (DeviceSiteFilterComboBox.SelectedIndex < 0) DeviceSiteFilterComboBox.SelectedIndex = 0;
        ApplyFilters();
        UpdateCounters();
    }
    private void DeviceFilters_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initializing) ApplyFilters();
    }

    private void ApplyFilters()
    {
        var search = DeviceSearchTextBox.Text.Trim();
        var selectedType = DeviceTypeFilterComboBox.SelectedItem as string;
        var selectedSite = DeviceSiteFilterComboBox.SelectedItem as string;
        var query = NetworkOperationsDemoStore.Devices.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(device => string.Join(" ", device.Name, device.Id, device.Type, device.SiteName, device.ParentDevice, device.ManagementAddress).Contains(search, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(selectedType) && selectedType != "كل الأنواع")
            query = query.Where(device => device.Type == selectedType);
        if (!string.IsNullOrWhiteSpace(selectedSite) && selectedSite != "كل المواقع")
            query = query.Where(device => device.SiteName == selectedSite);
        VisibleDevices.Clear();
        foreach (var device in query) VisibleDevices.Add(device);
        VisibleDevicesTextBlock.Text = $"{VisibleDevices.Count:N0} جهازًا";
        DevicesDataGrid.Items.Refresh();
    }

    private void ShowAddDeviceButton_Click(object sender, RoutedEventArgs e)
    {
        AddDevicePanel.Visibility = Visibility.Visible;
        NewDeviceNameTextBox.Focus();
        AddDeviceStatusTextBlock.Text = "أضف سجل جهاز إلى بيانات الديمو. لن يجري اكتشاف أو اتصال تلقائي.";
    }

    private void CancelNewDeviceButton_Click(object sender, RoutedEventArgs e) => AddDevicePanel.Visibility = Visibility.Collapsed;

    private void SaveNewDeviceButton_Click(object sender, RoutedEventArgs e)
    {
        var name = NewDeviceNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            AddDeviceStatusTextBlock.Text = "اكتب اسم الجهاز أولًا.";
            return;
        }
        if (NewDeviceTypeComboBox.SelectedItem is not string type || NewDeviceSiteComboBox.SelectedItem is not NetworkSiteDemo site)
        {
            AddDeviceStatusTextBlock.Text = "اختر نوع الجهاز والموقع.";
            return;
        }
        var parent = NewDeviceParentComboBox.SelectedItem as NetworkDeviceDemo;
        var device = new NetworkDeviceDemo
        {
            Id = NetworkOperationsDemoStore.NewId("DEV"),
            Name = name,
            Type = type,
            SiteName = site.Name,
            ParentDevice = parent?.Name ?? "غير محدد",
            ManagementAddress = NewDeviceAddressTextBox.Text.Trim(),
            Status = "غير مختبر",
            Signal = "غير متاح",
            ConnectedClients = 0,
            DownloadGb = 0m,
            LastSeen = "لم تتم قراءته",
            Notes = NewDeviceNotesTextBox.Text.Trim()
        };
        NetworkOperationsDemoStore.Devices.Add(device);
        NewDeviceNameTextBox.Clear();
        NewDeviceAddressTextBox.Clear();
        NewDeviceNotesTextBox.Clear();
        AddDevicePanel.Visibility = Visibility.Collapsed;
        ApplyFilters();
        UpdateCounters();
        DevicesDataGrid.SelectedItem = device;
        PageStatusTextBlock.Text = $"تمت إضافة «{device.Name}» إلى قائمة الديمو بحالة «غير مختبر».";
    }

    private void DevicesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedDevice = DevicesDataGrid.SelectedItem as NetworkDeviceDemo;
        if (_selectedDevice is null)
        {
            SelectedDevicePanel.Visibility = Visibility.Collapsed;
            return;
        }
        SelectedDevicePanel.Visibility = Visibility.Visible;
        SelectedDeviceDetailsTextBlock.Text =
            $"{_selectedDevice.Name} · {_selectedDevice.Type}\nالموقع: {_selectedDevice.SiteName}\nالجهاز الأب: {_selectedDevice.ParentDevice}\nالحالة: {_selectedDevice.Status}\nالإشارة: {_selectedDevice.Signal}\nعدد العملاء المعلن في بيانات العرض: {_selectedDevice.ConnectedClients:N0}\nآخر قراءة: {_selectedDevice.LastSeen}\nالملاحظات: {_selectedDevice.Notes}";
    }

    private void OpenClientsButton_Click(object sender, RoutedEventArgs e) =>
        NavigationRequested?.Invoke(this, new NetworkOperationsNavigationRequestedEventArgs("المتصلون"));

    private void CreateTaskForDeviceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedDevice is null) return;
        var task = new NetworkMaintenanceTaskDemo
        {
            Id = NetworkOperationsDemoStore.NewId("MNT"),
            Title = $"فحص الجهاز {_selectedDevice.Name}",
            DeviceName = _selectedDevice.Name,
            SiteName = _selectedDevice.SiteName,
            Priority = _selectedDevice.Status.Contains("غير متصل", StringComparison.Ordinal) ? "عالية" : "متوسطة",
            Status = "جديدة",
            AssignedTo = "غير معيّن",
            Notes = "تم إنشاؤها من صفحة حصر الأجهزة في الديمو."
        };
        NetworkOperationsDemoStore.MaintenanceTasks.Insert(0, task);
        PageStatusTextBlock.Text = $"تم إنشاء مهمة {task.Id} للجهاز {_selectedDevice.Name}.";
        NavigationRequested?.Invoke(this, new NetworkOperationsNavigationRequestedEventArgs("الصيانة"));
    }
}