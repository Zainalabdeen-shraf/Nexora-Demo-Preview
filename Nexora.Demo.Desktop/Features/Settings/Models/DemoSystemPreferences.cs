namespace Nexora.Demo.Desktop.Features.Settings.Models;

public sealed class DemoSystemPreferences
{
    public string StartupSection { get; set; } = "dashboard";
    public string NavigationDensity { get; set; } = "مريحة";
    public bool ConfirmBeforeBackup { get; set; } = true;
    public bool ShowSuccessMessages { get; set; } = true;
}