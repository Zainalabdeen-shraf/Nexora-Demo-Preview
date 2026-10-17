using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Nexora.Demo.Desktop.Features.NetworkOperations.DemoData;
using Nexora.Demo.Desktop.Features.NetworkOperations.Models;

namespace Nexora.Demo.Desktop.Features.NetworkOperations.Views;

public partial class NetworkMonitoringView : UserControl, INetworkOperationsNavigationSource, INetworkOperationsRefreshable
{
    private bool _initializing = true;
    private NetworkAlertDemo? _selectedAlert;
    public event EventHandler<NetworkOperationsNavigationRequestedEventArgs>? NavigationRequested;
    public ObservableCollection<NetworkAlertDemo> VisibleAlerts { get; } = new();

    public NetworkMonitoringView()
    {
        InitializeComponent();
        DataContext = this;
        AlertFilterComboBox.Items.Add("كل التنبيهات");
        AlertFilterComboBox.Items.Add("المفتوحة فقط");
        AlertFilterComboBox.Items.Add("تمت مراجعتها");
        AlertFilterComboBox.SelectedIndex = 0;
        _initializing = false;
        ApplyFilter();
        if (!string.IsNullOrWhiteSpace(NetworkOperationsDemoStore.RequestedAlertId))
        {
            AlertsDataGrid.SelectedItem = VisibleAlerts.FirstOrDefault(alert => alert.Id == NetworkOperationsDemoStore.RequestedAlertId);
            NetworkOperationsDemoStore.RequestedAlertId = null;
        }
    }

    public void RefreshFromSharedState()
    {
        var currentId = (AlertsDataGrid.SelectedItem as NetworkAlertDemo)?.Id;
        var requestedId = NetworkOperationsDemoStore.RequestedAlertId;
        if (!string.IsNullOrWhiteSpace(requestedId))
        {
            NetworkOperationsDemoStore.RequestedAlertId = null;
            AlertFilterComboBox.SelectedItem = "كل التنبيهات";
        }
        ApplyFilter();
        var desiredId = requestedId ?? currentId;
        var desiredAlert = VisibleAlerts.FirstOrDefault(alert => alert.Id == desiredId);
        if (desiredAlert is not null) AlertsDataGrid.SelectedItem = desiredAlert;
    }
    private void AlertFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing) ApplyFilter();
    }

    private void ApplyFilter()
    {
        var selected = AlertFilterComboBox.SelectedItem as string;
        var query = NetworkOperationsDemoStore.Alerts.AsEnumerable();
        if (selected == "المفتوحة فقط") query = query.Where(alert => alert.Status != "تمت المراجعة");
        if (selected == "تمت مراجعتها") query = query.Where(alert => alert.Status == "تمت المراجعة");
        VisibleAlerts.Clear();
        foreach (var alert in query) VisibleAlerts.Add(alert);
        AlertsCountTextBlock.Text = $"{VisibleAlerts.Count:N0} تنبيهًا";
        AlertsDataGrid.Items.Refresh();
    }

    private void RefreshAlertsButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyFilter();
        AlertActionStatusTextBlock.Text = $"تم تحديث عرض البيانات عند {DateTime.Now:HH:mm:ss}. لم يُجرَ فحص شبكي فعلي.";
    }

    private void AlertsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedAlert = AlertsDataGrid.SelectedItem as NetworkAlertDemo;
        if (_selectedAlert is null)
        {
            AlertDetailsPanel.Visibility = Visibility.Collapsed;
            return;
        }
        AlertDetailsPanel.Visibility = Visibility.Visible;
        AlertDetailsTitleTextBlock.Text = _selectedAlert.Title;
        AlertDetailsContextTextBlock.Text = $"{_selectedAlert.Id} · {_selectedAlert.DeviceName} · {_selectedAlert.SiteName} · {_selectedAlert.Severity} · {_selectedAlert.Status}";
        AlertDetailsDescriptionTextBlock.Text = _selectedAlert.Description;
        AlertActionStatusTextBlock.Text = "اختر الإجراء المطلوب. إنشاء المهمة سيضيف سجلًا إلى قائمة الصيانة التجريبية.";
    }

    private void CreateMaintenanceTaskButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAlert is null) return;
        var task = NetworkOperationsDemoStore.CreateOrGetTaskFromAlert(_selectedAlert);
        AlertActionStatusTextBlock.Text = $"المهمة {task.Id} مرتبطة بالتنبيه {_selectedAlert.Id}.";
        NavigationRequested?.Invoke(this, new NetworkOperationsNavigationRequestedEventArgs("الصيانة"));
    }

    private void MarkAlertReviewedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedAlert is null) return;
        var alertId = _selectedAlert.Id;
        _selectedAlert.Status = "تمت المراجعة";
        AlertsDataGrid.Items.Refresh();
        ApplyFilter();
        AlertActionStatusTextBlock.Text = $"تم تحديث حالة التنبيه {alertId} داخل بيانات الديمو.";
    }

    private void OpenDevicesButton_Click(object sender, RoutedEventArgs e) =>
        NavigationRequested?.Invoke(this, new NetworkOperationsNavigationRequestedEventArgs("المواقع والأجهزة"));
}