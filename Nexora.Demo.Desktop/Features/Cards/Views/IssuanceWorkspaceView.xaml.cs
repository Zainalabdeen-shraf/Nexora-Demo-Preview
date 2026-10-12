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

    public IssuanceWorkspaceView(DemoCardIssuanceService issuanceService)
    {
        ArgumentNullException.ThrowIfNull(issuanceService);
        InitializeComponent();
        _issuanceService = issuanceService;
    }

    public event EventHandler<FeatureNavigationRequestedEventArgs>? NavigationRequested;

    public void SelectTargetSystem(string systemKey)
    {
        var item = TargetSystemComboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(option => option.Tag?.ToString() == systemKey);
        if (item is not null) TargetSystemComboBox.SelectedItem = item;
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
