using System.Windows;
using System.Windows.Controls;

namespace Nexora.Demo.Desktop.Features.Settings.Views;

public partial class WorkspaceIdentityView : UserControl
{
    public event EventHandler? EditLocationRequested;

    public WorkspaceIdentityView(string? workspaceName, bool locationSaved, string? locationSummary)
    {
        InitializeComponent();
        UpdateSummary(workspaceName, locationSaved, locationSummary);
    }

    public void UpdateSummary(string? workspaceName, bool locationSaved, string? locationSummary)
    {
        WorkspaceNameTextBlock.Text = string.IsNullOrWhiteSpace(workspaceName) ? "مساحة الشبكة" : workspaceName;
        LocationStatusTextBlock.Text = locationSaved ? "مكتمل" : "غير مكتمل";
        LocationSummaryTextBlock.Text = locationSaved && !string.IsNullOrWhiteSpace(locationSummary)
            ? locationSummary
            : "لم يتم حفظ موقع بعد.";
        LocationStatusBadge.Background = locationSaved
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(224, 244, 235))
            : (System.Windows.Media.Brush)FindResource("PrimaryLightBrush");
        LocationStatusTextBlock.Foreground = locationSaved
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(20, 112, 78))
            : (System.Windows.Media.Brush)FindResource("PrimaryBrush");
    }

    private void EditLocationButton_Click(object sender, RoutedEventArgs e) => EditLocationRequested?.Invoke(this, EventArgs.Empty);
}