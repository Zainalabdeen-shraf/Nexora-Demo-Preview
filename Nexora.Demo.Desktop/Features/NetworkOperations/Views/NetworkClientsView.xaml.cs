using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Nexora.Demo.Desktop.Features.NetworkOperations.DemoData;
using Nexora.Demo.Desktop.Features.NetworkOperations.Models;

namespace Nexora.Demo.Desktop.Features.NetworkOperations.Views;

public partial class NetworkClientsView : UserControl, INetworkOperationsNavigationSource, INetworkOperationsRefreshable
{
    private bool _initializing = true;
    public event EventHandler<NetworkOperationsNavigationRequestedEventArgs>? NavigationRequested;
    public ObservableCollection<NetworkClientDemo> VisibleClients { get; } = new();

    public NetworkClientsView()
    {
        InitializeComponent();
        DataContext = this;
        ClientLayerFilterComboBox.Items.Add("كل المصادر");
        foreach (var layer in NetworkOperationsDemoStore.Clients.Select(client => client.Layer).Distinct()) ClientLayerFilterComboBox.Items.Add(layer);
        ClientLayerFilterComboBox.SelectedIndex = 0;
        ClientSiteFilterComboBox.Items.Add("كل المواقع");
        foreach (var site in NetworkOperationsDemoStore.Sites) ClientSiteFilterComboBox.Items.Add(site.Area);
        ClientSiteFilterComboBox.Items.Add("الموقع الرئيسي");
        ClientSiteFilterComboBox.SelectedIndex = 0;
        _initializing = false;
        ApplyFilters();
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        ClientsTotalTextBlock.Text = NetworkOperationsDemoStore.Clients.Count.ToString("N0");
        ActiveSessionsTextBlock.Text = NetworkOperationsDemoStore.Clients.Count(client => client.Layer.StartsWith("جلسة", StringComparison.Ordinal) && client.Status.StartsWith("نشطة", StringComparison.Ordinal)).ToString("N0");
        TotalUsageTextBlock.Text = $"{NetworkOperationsDemoStore.Clients.Sum(client => client.DownloadGb):N1} GB";
    }

    public void RefreshFromSharedState()
    {
        ApplyFilters();
        UpdateSummary();
    }

    private void ClientFilters_Changed(object sender, RoutedEventArgs e)
    {
        if (!_initializing) ApplyFilters();
    }

    private void ApplyFilters()
    {
        var search = ClientSearchTextBox.Text.Trim();
        var selectedLayer = ClientLayerFilterComboBox.SelectedItem as string;
        var selectedSite = ClientSiteFilterComboBox.SelectedItem as string;
        var query = NetworkOperationsDemoStore.Clients.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(client => string.Join(" ", client.Id, client.Name, client.Layer, client.DeviceName, client.SiteName, client.IpAddress, client.MacAddress).Contains(search, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(selectedLayer) && selectedLayer != "كل المصادر")
            query = query.Where(client => client.Layer == selectedLayer);
        if (!string.IsNullOrWhiteSpace(selectedSite) && selectedSite != "كل المواقع")
            query = query.Where(client => client.SiteName.Contains(selectedSite, StringComparison.OrdinalIgnoreCase));
        VisibleClients.Clear();
        foreach (var client in query) VisibleClients.Add(client);
        ClientRowsCountTextBlock.Text = $"{VisibleClients.Count:N0} سجلًا";
        ClientsDataGrid.Items.Refresh();
    }

    private void ClientsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ClientsDataGrid.SelectedItem is not NetworkClientDemo client)
        {
            ClientDetailsPanel.Visibility = Visibility.Collapsed;
            return;
        }
        ClientDetailsPanel.Visibility = Visibility.Visible;
        ClientDetailsTextBlock.Text =
            $"{client.Name} ({client.Id})\nطبقة المصدر: {client.Layer}\nالجهاز: {client.DeviceName}\nالموقع: {client.SiteName}\nIP: {client.IpAddress}\nMAC: {client.MacAddress}\nالحالة: {client.Status}\nالتنزيل: {client.DownloadGb:N1} GB · الرفع: {client.UploadGb:N1} GB\nالباقة: {client.PackageName}\nآخر قراءة: {client.LastSeen}";
    }

    private void OpenDevicesButton_Click(object sender, RoutedEventArgs e) =>
        NavigationRequested?.Invoke(this, new NetworkOperationsNavigationRequestedEventArgs("المواقع والأجهزة"));
}