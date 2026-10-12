using System.Globalization;
using Nexora.Demo.Desktop.Features.Packages.Services;

namespace Nexora.Demo.Desktop.Features.Cards.Models;

/// <summary>
/// Projects the shared local commercial package catalog into the target account system.
/// The technical profile is taken from that system's provider binding.
/// </summary>
public static class DemoCardPackageCatalog
{
    public static IReadOnlyList<DemoCardPackage> All => Load(DemoAccountSystem.HotSpot);

    public static IReadOnlyList<DemoCardPackage> Load(DemoAccountSystem targetSystem)
    {
        var providerKey = targetSystem == DemoAccountSystem.HotSpot ? "hotspot" : "user-manager";
        var catalog = DemoPackageStore.Load();

        var result = new List<DemoCardPackage>();
        foreach (var package in catalog.Packages.Where(item => !item.IsArchived))
        {
            var binding = catalog.Bindings.FirstOrDefault(item =>
                item.PackageId == package.Id &&
                string.Equals(item.ProviderKey, providerKey, StringComparison.OrdinalIgnoreCase) &&
                item.IsEnabled);

            if (binding is null)
                continue;

            var displayName = $"{package.Name} — {package.SalePriceYer.ToString("N0", CultureInfo.CurrentCulture)} ريال";
            var rate = $"{package.DownloadMbps:0.##}M/{package.UploadMbps:0.##}M";
            var userManagerProfile = string.IsNullOrWhiteSpace(binding.UserManagerProfile)
                ? binding.UserManagerLimitation : binding.UserManagerProfile;
            var hotSpotProfile = string.IsNullOrWhiteSpace(binding.HotSpotProfile)
                ? "default" : binding.HotSpotProfile;

            result.Add(new DemoCardPackage(
                package.Id,
                displayName,
                package.SalePriceYer,
                package.UptimeDisplay,
                package.DataQuotaDisplay,
                rate,
                userManagerProfile,
                hotSpotProfile,
                package.CardDisplayPriceYer > 0 ? package.CardDisplayPriceYer : null,
                package.ValidityDisplay));
        }

        return result
            .OrderBy(item => item.DisplayName, StringComparer.CurrentCulture)
            .ToList();
    }
}
