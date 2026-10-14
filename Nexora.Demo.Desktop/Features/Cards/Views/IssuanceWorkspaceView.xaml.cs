using System.Windows;
using System.Windows.Controls;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Shared;

namespace Nexora.Demo.Desktop.Features.Cards.Views;

/// <summary>Single issuance entry point that routes a batch to a selected account system.</summary>
public partial class IssuanceWorkspaceView : UserControl
{
    private readonly DemoCardIssuanceService _issuanceService;
    private readonly Dictionary<DemoAccountSystem, CardIssuanceView> _views = new();
    private bool _isSelectingTargetSystem;

    public IssuanceWorkspaceView(DemoCardIssuanceService issuanceService, DemoAccountSystem initialTargetSystem = DemoAccountSystem.UserManager)
    {
        ArgumentNullException.ThrowIfNull(issuanceService);
        InitializeComponent();
        _issuanceService = issuanceService;
        SelectTargetSystem(initialTargetSystem == DemoAccountSystem.HotSpot ? "hotspot" : "user-manager");
    }

    public event EventHandler<FeatureNavigationRequestedEventArgs>? NavigationRequested;

    public void SelectTargetSystem(string systemKey)
    {
        var item = TargetSystemComboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(option => string.Equals(option.Tag?.ToString(), systemKey, StringComparison.OrdinalIgnoreCase));
        if (item is null) return;

        _isSelectingTargetSystem = true;
        try
        {
            HotSpotTargetTabButton.IsChecked = string.Equals(systemKey, "hotspot", StringComparison.OrdinalIgnoreCase);
            UserManagerTargetTabButton.IsChecked = string.Equals(systemKey, "user-manager", StringComparison.OrdinalIgnoreCase);
            if (!ReferenceEquals(TargetSystemComboBox.SelectedItem, item))
                TargetSystemComboBox.SelectedItem = item;
        }
        finally
        {
            _isSelectingTargetSystem = false;
        }
    }

    private void TargetSystemTab_Checked(object sender, RoutedEventArgs e)
    {
        if (_isSelectingTargetSystem || sender is not RadioButton tab || tab.Tag is not string systemKey)
            return;
        SelectTargetSystem(systemKey);
    }

    private void TargetSystemComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TargetSystemComboBox.SelectedItem is not ComboBoxItem item)
            return;

        var system = item.Tag?.ToString() == "hotspot"
            ? DemoAccountSystem.HotSpot
            : DemoAccountSystem.UserManager;

        if (!_views.TryGetValue(system, out var view))
        {
            view = new CardIssuanceView(_issuanceService, system);
            view.NavigationRequested += ChildNavigationRequested;
            _views.Add(system, view);
        }

        ChooseTargetPanel.Visibility = Visibility.Collapsed;
        IssuanceContentHost.Visibility = Visibility.Visible;
        IssuanceContentHost.Content = view;
    }

    private void ChildNavigationRequested(object? sender, FeatureNavigationRequestedEventArgs e) =>
        NavigationRequested?.Invoke(this, e);
}
