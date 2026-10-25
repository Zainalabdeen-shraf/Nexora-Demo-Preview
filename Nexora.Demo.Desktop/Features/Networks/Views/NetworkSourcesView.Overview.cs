using System.Windows;
using System.Windows.Controls;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Shared;
using Nexora.Demo.Desktop.Features.Packages.Services;
using Nexora.Demo.Desktop.Features.Networks.Models;
using Nexora.Demo.Desktop.Features.Networks.Services;

namespace Nexora.Demo.Desktop.Features.Networks.Views;

// Responsibility: Source selection summary and screen-level navigation actions.
public partial class NetworkSourcesView
{
    private void RefreshSummary()
    {
        HotSpotAccountsTextBlock.Text = _issuanceService.Cards.Count(card => card.TargetSystem.Contains("هوتسبوت", StringComparison.OrdinalIgnoreCase)).ToString("N0");
        UserManagerAccountsTextBlock.Text = _issuanceService.Cards.Count(card => card.TargetSystem.Contains("مدير المستخدمين", StringComparison.OrdinalIgnoreCase)).ToString("N0");
        try
        {
            var catalog = DemoPackageStore.Load();
            HotSpotPackageBindingsTextBlock.Text = catalog.Bindings.Count(binding => binding.ProviderKey == "hotspot" && binding.IsEnabled).ToString("N0");
            UserManagerPackageBindingsTextBlock.Text = catalog.Bindings.Count(binding => binding.ProviderKey == "user-manager" && binding.IsEnabled).ToString("N0");
        }
        catch
        {
            HotSpotPackageBindingsTextBlock.Text = "—";
            UserManagerPackageBindingsTextBlock.Text = "—";
        }
    }
    private void ConfigureArea()
    {
        RouterSettingsPanel.Visibility = Visibility.Collapsed;
        UserManagerSettingsPanel.Visibility = Visibility.Collapsed;
        HotSpotSettingsPanel.Visibility = Visibility.Collapsed;

        if (_area == "hotspot")
                {
                NetworkPageTitleTextBlock.Text = "HotSpot";
                NetworkPageSubtitleTextBlock.Text = "إعدادات مصدر المصادقة الخاص بـ HotSpot، مع إدارة الحسابات في مساحة مستخدمي الشبكة الموحدة.";
                UserManagerCard.Visibility = Visibility.Collapsed;
                Grid.SetColumn(HotSpotCard, 0);
                HotSpotCard.SetValue(Grid.ColumnSpanProperty, 3);
                ProviderDetailPanel.Visibility = Visibility.Visible;
                ProviderDetailTitleTextBlock.Text = "إعداد HotSpot";
                ProviderDetailDescriptionTextBlock.Text = "يُحدد إعداد هذا المصدر ما إذا كانت الحسابات محلية في HotSpot أو تستخدم RADIUS. اختر الباقة المتوافقة مع طريقة المصادقة المستخدمة.";
                ProviderCardsGrid.Visibility = Visibility.Collapsed;
                ProviderDetailPanel.Visibility = Visibility.Collapsed;
                HotSpotSettingsPanel.Visibility = Visibility.Visible;
            }
            else if (_area == "user-manager")
            {
                NetworkPageTitleTextBlock.Text = "User Manager";
                NetworkPageSubtitleTextBlock.Text = "إعدادات المصدر المسؤول عن الحسابات وملفات الاستخدام وقيود RADIUS.";
                HotSpotCard.Visibility = Visibility.Collapsed;
                Grid.SetColumn(UserManagerCard, 0);
                UserManagerCard.SetValue(Grid.ColumnSpanProperty, 3);
                ProviderDetailPanel.Visibility = Visibility.Visible;
                ProviderDetailTitleTextBlock.Text = "إعداد User Manager";
                ProviderDetailDescriptionTextBlock.Text = "تكوين الباقة مرتبط بملفات المستخدم والحدود والمجموعات المتوافقة. تظهر الحسابات في مساحة المستخدمين الموحدة مع علامة المصدر.";
                ProviderCardsGrid.Visibility = Visibility.Collapsed;
                ProviderDetailPanel.Visibility = Visibility.Collapsed;

                NetworkPageTitleTextBlock.Text = "مدير المستخدمين";
                NetworkPageSubtitleTextBlock.Text =
                    "إدارة إعدادات الخدمة العامة ومنافذ RADIUS.";

                UserManagerSettingsPanel.Visibility = Visibility.Visible;
            }
            else
            {
                NetworkPageTitleTextBlock.Text = "الشبكات ومصادر المصادقة";
                NetworkPageSubtitleTextBlock.Text = "افصل طريقة إدارة الحسابات حسب المصدر، مع مشاركة واجهة إدارة المستخدمين والإصدار والتقارير.";
                HotSpotCard.Visibility = Visibility.Visible;
                UserManagerCard.Visibility = Visibility.Visible;
                ProviderDetailPanel.Visibility = Visibility.Collapsed;
            }
        }
    private void IssueForSystemButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string systemKey }) return;
        NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("cards", "إصدار الكروت", null, null, systemKey));
    }
    private void HotSpotPackagesButton_Click(object sender, RoutedEventArgs e) =>
        NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("packages", "باقات HotSpot"));
    private void UserManagerPackagesButton_Click(object sender, RoutedEventArgs e) =>
        NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("packages", "باقات User Manager"));
    private void OpenUsersButton_Click(object sender, RoutedEventArgs e) =>
        NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("network-users", _area == "hotspot" ? "مستخدمو HotSpot" : "مستخدمو User Manager"));
    private void OpenIssuanceButton_Click(object sender, RoutedEventArgs e) =>
        NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("cards", "إصدار الكروت", null, null, _area));
}
