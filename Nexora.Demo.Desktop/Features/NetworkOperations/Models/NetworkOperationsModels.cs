using System;
using System.Collections.ObjectModel;

namespace Nexora.Demo.Desktop.Features.NetworkOperations.Models;

public sealed class NetworkSiteDemo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Area { get; set; } = string.Empty;
    public string SiteType { get; set; } = "موقع بث";
    public string Notes { get; set; } = string.Empty;
}

public sealed class NetworkDeviceDemo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string SiteName { get; set; } = string.Empty;
    public string ParentDevice { get; set; } = "—";
    public string ManagementAddress { get; set; } = string.Empty;
    public string Status { get; set; } = "غير معروف";
    public string Signal { get; set; } = "غير متاح";
    public int ConnectedClients { get; set; }
    public decimal DownloadGb { get; set; }
    public string LastSeen { get; set; } = "غير متاح";
    public string Notes { get; set; } = string.Empty;
}

public sealed class NetworkClientDemo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Layer { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string SiteName { get; set; } = string.Empty;
    public string IpAddress { get; set; } = "—";
    public string MacAddress { get; set; } = "—";
    public string Status { get; set; } = string.Empty;
    public decimal DownloadGb { get; set; }
    public decimal UploadGb { get; set; }
    public string PackageName { get; set; } = "—";
    public string LastSeen { get; set; } = "غير متاح";
}

public sealed class NetworkAlertDemo
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string SiteName { get; set; } = string.Empty;
    public string Severity { get; set; } = "مراجعة";
    public string Age { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = "مفتوح";
}

public sealed class NetworkMaintenanceTaskDemo
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string SiteName { get; set; } = string.Empty;
    public string Priority { get; set; } = "متوسطة";
    public string Status { get; set; } = "جديدة";
    public string AssignedTo { get; set; } = "غير معيّن";
    public string Notes { get; set; } = string.Empty;
    public string? SourceAlertId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}

public sealed class NetworkUsageBreakdownDemo
{
    public string Name { get; set; } = string.Empty;
    public decimal DownloadGb { get; set; }
    public int SharePercent { get; set; }
    public string Caption { get; set; } = string.Empty;
}

public sealed class NetworkOperationsNavigationRequestedEventArgs : EventArgs
{
    public NetworkOperationsNavigationRequestedEventArgs(string tabName)
    {
        TabName = tabName;
    }

    public string TabName { get; }
}

public interface INetworkOperationsRefreshable
{
    void RefreshFromSharedState();
}
public interface INetworkOperationsNavigationSource
{
    event EventHandler<NetworkOperationsNavigationRequestedEventArgs>? NavigationRequested;
}