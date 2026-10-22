using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Nexora.Demo.Desktop.Features.Packages.Services;

namespace Nexora.Demo.Desktop.Features.Packages.Views;

public partial class PackageManagementView
{
    private bool _isCardIssuanceOverlay;
    private HashSet<string> _overlayPackageIdsBefore = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _overlayBindingKeysBefore = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Prepare the existing package editor as a create-only modal for card issuance.</summary>
    public void PrepareForCardIssuanceOverlay()
    {
        var catalogBefore = DemoPackageStore.Load();
        _overlayPackageIdsBefore = catalogBefore.Packages
            .Select(package => package.Id?.ToString() ?? string.Empty)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _overlayBindingKeysBefore = catalogBefore.Bindings
            .Select(binding => string.Join("|", binding.Id, binding.PackageId, binding.ProviderKey))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _isCardIssuanceOverlay = true;

        ClearForm();
        if (CreatePackageTab is not null)
        {
            CreatePackageTab.Visibility = Visibility.Visible;
            CreatePackageTab.Header = "باقة جديدة";
        }
        if (SavedPackagesTab is not null)
            SavedPackagesTab.Visibility = Visibility.Collapsed;
        if (PackageTabs is not null && CreatePackageTab is not null)
            PackageTabs.SelectedItem = CreatePackageTab;

        // Register after the XAML click handler so the save has already completed when this verifies the catalog.
        if (SavePackageButton is not null)
            SavePackageButton.AddHandler(ButtonBase.ClickEvent, new RoutedEventHandler(CardIssuanceOverlaySaveClicked), true);
    }

    private void CardIssuanceOverlaySaveClicked(object sender, RoutedEventArgs e)
    {
        if (!_isCardIssuanceOverlay) return;

        Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
        {
            try
            {
                var catalog = DemoPackageStore.Load();
                var newPackageExists = catalog.Packages.Any(package =>
                    !package.IsArchived && !_overlayPackageIdsBefore.Contains(package.Id?.ToString() ?? string.Empty));
                var newProviderBindingExists = catalog.Bindings.Any(binding =>
                    string.Equals(binding.ProviderKey, _providerKey, StringComparison.OrdinalIgnoreCase) &&
                    !_overlayBindingKeysBefore.Contains(string.Join("|", binding.Id, binding.PackageId, binding.ProviderKey)));

                if (!newPackageExists && !newProviderBindingExists) return; // Validation failed or save was cancelled.

                var dialog = Window.GetWindow(this);
                if (dialog is null || !dialog.IsVisible) return;
                dialog.DialogResult = true; // ShowDialog window closes when DialogResult is assigned.
            }
            catch
            {
                // Keep the editor open if the save result cannot be verified.
            }
        }));
    }
}
