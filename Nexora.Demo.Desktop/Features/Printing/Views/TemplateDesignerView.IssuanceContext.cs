// Restored from the pre-R4 backup to preserve Card Issuance integration.
// This file is intentionally a separate partial class so the R4 studio UI remains intact.
using System.Globalization;
using Microsoft.Win32;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Nexora.Demo.Desktop.Features.Cards.Models;

namespace Nexora.Demo.Desktop.Features.Printing.Views;

public partial class TemplateDesignerView
{
    // public void ApplyIssuanceContext(DemoAccountSystem targetSystem, string packageKey)
    // {
    //     _activePackageSource = targetSystem == DemoAccountSystem.UserManager ? "user-manager" : "hotspot";
    //     var combo = _activePackageSource == "user-manager" ? UserManagerPackageComboBox : HotspotPackageComboBox;
    //     if (!string.IsNullOrWhiteSpace(packageKey))
    //     {
    //         var matchingPackage = combo.Items.OfType<DemoCardPackage>()
    //             .FirstOrDefault(package => string.Equals(package.Key, packageKey, StringComparison.Ordinal));
    //         if (matchingPackage is not null)
    //             combo.SelectedItem = matchingPackage;
    //     }
    //     UpdateSamplePackageFields();
    //     SyncMainElementChecks();
    //     SyncSelectedElementEditor();
    //     UpdatePreview();
    // }
}
