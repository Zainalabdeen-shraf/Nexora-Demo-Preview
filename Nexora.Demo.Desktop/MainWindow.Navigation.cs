using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;
using System.Globalization;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Cards.Views;
using Nexora.Demo.Desktop.Features.Printing.Views;
using Nexora.Demo.Desktop.Features.Sales.Services;
using Nexora.Demo.Desktop.Features.Sales.Views;
using Nexora.Demo.Desktop.Features.Shared;
using Nexora.Demo.Desktop.Features.Reports.Views;
using Nexora.Demo.Desktop.Features.Networks.Views;
using Nexora.Demo.Desktop.Features.NetworkOperations.Views;
using Nexora.Demo.Desktop.Features.NetworkOperations.Models;
using Nexora.Demo.Desktop.Features.Settings.Views;
using Nexora.Demo.Desktop.Features.Settings.Services;
using Nexora.Demo.Desktop.Features.Packages.Views;
using Nexora.Demo.Desktop.Features.NetworkOperations.Connections;
using System;
using System.Windows.Documents;

namespace Nexora.Demo.Desktop;

// Responsibility: Section navigation, shell state, and sidebar layout.
public partial class MainWindow
{
    private void NavigationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string key) return;
        if (_locationRequiredForCurrentAccount && !_locationSaved)
        {
            ShowLocationModal();
            SetStatus(LocationStatusTextBlock, "أكمل موقع الشبكة قبل متابعة العمل.", true);
            return;
        }
        NavigateTo(key);
    }
    private void DashboardMetric_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string route) return;
        var parts = route.Split('|', StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || string.IsNullOrWhiteSpace(parts[0])) return;
        if (_locationRequiredForCurrentAccount && !_locationSaved) { ShowLocationModal(); return; }
        NavigateTo(parts[0]);
        if (parts.Length > 1 && !string.IsNullOrWhiteSpace(parts[1])) ActivateSectionTab(parts[1]);
        if (parts.Length > 2 && string.Equals(parts[2], "all", StringComparison.OrdinalIgnoreCase) &&
            SectionFeatureContentHost.Content is GeneratedCardsView generatedCardsView)
            generatedCardsView.ShowAllSystems();
    }
    private void NavigateTo(string key)
    {
        if (_locationRequiredForCurrentAccount && !_locationSaved && key != "dashboard")
        {
            ShowLocationModal();
            return;
        }

        _currentSection = key;
        SectionBodyHost.Content = null;
        SectionBodyHost.Visibility = Visibility.Collapsed;
        SectionContentPanel.Visibility = Visibility.Visible;
        DashboardPage.Visibility = key == "dashboard" ? Visibility.Visible : Visibility.Collapsed;
        SectionPage.Visibility = key == "dashboard" ? Visibility.Collapsed : Visibility.Visible;
        CardSystemSelectorPanel.Visibility =
        key == "cards" || key == "packages" || key == "network-users"
            ? Visibility.Visible
            : Visibility.Collapsed;
            UpdatePageBreadcrumb(key);
        UpdateSidebarSelection(key);
        if (key == "dashboard")
        {
            SectionTabsPanel.Children.Clear();
            return;
        }

        SectionTabsPanel.Children.Clear();
        var tabs = _sectionTabs.TryGetValue(key, out var list) ? list : ["نظرة عامة"];
        if (string.Equals(key, "printing", StringComparison.Ordinal))
            tabs = ["تصميم القالب"]; // One dedicated design workspace; printing actions live in its toolbar.
        foreach (var tab in tabs)
        {
            var btn = new Button
            {
                Content = tab,
                Tag = tab,
                Style = (Style)FindResource("SectionTabButtonStyle"),
                Margin = new Thickness(0),
                Padding = new Thickness(12, 0, 12, 0),
                MinWidth = 0,
                Height = 34,
                FontSize = 12,
                BorderThickness = new Thickness(0),
                BorderBrush = Brushes.Transparent,
                Background = Brushes.Transparent,
                FlowDirection = FlowDirection.RightToLeft
            };
                        btn.Click += SectionTabButton_Click;
                        btn.Click += (_, _) => RefreshSectionTabButtonStyles();
            btn.MouseEnter += (_, _) => RefreshSectionTabButtonStyles();
            btn.MouseLeave += (_, _) => RefreshSectionTabButtonStyles();
            SectionTabsPanel.Children.Add(btn);
                        }
            ApplyShellDisplayPreferences();
            ActivateSectionTab(tabs[0]);
            RefreshSectionTabButtonStyles();
        }
    private void UpdatePageBreadcrumb(string key, string? tabName = null)
{
    var currentTitle = key switch
    {
        "dashboard" => "لوحة التحكم",
        "network-users" => "مستخدمو الشبكة",
        "cards" => "الكروت والدفعات",
        "packages" => "إدارة الباقات",
        "sales" => "المبيعات ونقاط البيع",
        "printing" => "الطباعة والتصميم",
        "networks" => "الشبكات ومصادر المصادقة",
        "network-center" => "مركز الشبكة",
        "reports" => "مركز التقارير",
        "settings" => "إعدادات النظام",
        _ => "تاب كارد"
    };

    // The template designer uses the full workspace width/height; hide only its breadcrumb row.
    // Other application sections keep the same breadcrumb behavior.
    if (PageTitleTextBlock.Parent is FrameworkElement titlePanel &&
        titlePanel.Parent is FrameworkElement breadcrumbRow)
    {
        breadcrumbRow.Visibility = string.Equals(key, "printing", StringComparison.Ordinal)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    PageTitleIconTextBlock.Visibility = Visibility.Collapsed;
    PageTitleTextBlock.Visibility = string.Equals(key, "printing", StringComparison.Ordinal)
        ? Visibility.Collapsed
        : Visibility.Visible;
    if (PrintingStudioShellTitlePanel is not null)
        PrintingStudioShellTitlePanel.Visibility = string.Equals(key, "printing", StringComparison.Ordinal)
            ? Visibility.Visible
            : Visibility.Collapsed;
    PageSubtitleTextBlock.Visibility = Visibility.Collapsed;

    PageTitleTextBlock.Inlines.Clear();
    PageTitleTextBlock.FontSize = 12;
    PageTitleTextBlock.FontWeight = FontWeights.Normal;
    PageTitleTextBlock.TextWrapping = TextWrapping.NoWrap;
    PageTitleTextBlock.VerticalAlignment = VerticalAlignment.Center;
    PageTitleTextBlock.TextAlignment = TextAlignment.Right;
    PageTitleTextBlock.Foreground =
        (Brush)FindResource("TextBrush");

    var homeIcon = new Run("\uE80F ")
    {
        FontFamily = new FontFamily("Segoe MDL2 Assets"),
        Foreground = (Brush)FindResource("PrimaryBrush")
    };

    if (key == "dashboard")
    {
        PageTitleTextBlock.Inlines.Add(homeIcon);

        PageTitleTextBlock.Inlines.Add(
            new Run("لوحة التحكم")
            {
                FontWeight = FontWeights.SemiBold
            });

        return;
    }

    var homeLink = new Hyperlink
    {
        Foreground = (Brush)FindResource("PrimaryBrush"),
        TextDecorations = new TextDecorationCollection()
    };

    homeLink.Inlines.Add(homeIcon);
    homeLink.Inlines.Add(new Run("لوحة التحكم"));

    homeLink.Click += (_, _) => NavigateTo("dashboard");

    PageTitleTextBlock.Inlines.Add(homeLink);

    PageTitleTextBlock.Inlines.Add(
        new Run("  ‹  ")
        {
            Foreground = Brushes.Gray
        });

    PageTitleTextBlock.Inlines.Add(
        new Run(currentTitle)
        {
            Foreground = (Brush)FindResource("TextBrush"),
            FontWeight = FontWeights.SemiBold
        });

    if (!string.IsNullOrWhiteSpace(tabName) &&
        !string.Equals(tabName, currentTitle, StringComparison.Ordinal))
    {
        PageTitleTextBlock.Inlines.Add(new Run("  ‹  ")
        {
            Foreground = Brushes.Gray
        });
        PageTitleTextBlock.Inlines.Add(new Run(tabName)
        {
            Foreground = (Brush)FindResource("PrimaryBrush"),
            FontWeight = FontWeights.SemiBold
        });
    }
}
private void RefreshSectionTabButtonStyles()
{
    if (SectionTabsPanel is null || SectionContentTitleTextBlock is null)
        return;

    var activeBackground = (Brush)FindResource("PrimaryLightBrush");
    var activeForeground = (Brush)FindResource("PrimaryBrush");
    var inactiveForeground = (Brush)FindResource("TextBrush");
    var inactiveBorder = (Brush)FindResource("BorderBrushSoft");

    var hoverBackground =
        new SolidColorBrush(Color.FromRgb(255, 244, 245));

    var hoverBorder =
        new SolidColorBrush(Color.FromRgb(240, 184, 190));

    foreach (var button in SectionTabsPanel.Children.OfType<Button>())
    {
        var isSelected = string.Equals(
            button.Tag?.ToString(),
            SectionContentTitleTextBlock.Text,
            StringComparison.Ordinal);

        var isHovered = button.IsMouseOver && !isSelected;

        button.Background = isSelected
            ? activeBackground
            : isHovered ? hoverBackground : Brushes.White;

        button.Foreground = isSelected
            ? activeForeground
            : inactiveForeground;

        button.BorderBrush = isSelected
            ? activeForeground
            : isHovered ? hoverBorder : inactiveBorder;

        button.BorderThickness = new Thickness(1);
        button.FontWeight = isSelected
            ? FontWeights.SemiBold
            : FontWeights.Normal;

        button.Cursor = System.Windows.Input.Cursors.Hand;
    }
}
    private void ApplyShellDisplayPreferences()
    {
        var compact = DemoSystemSettingsStore.Current.NavigationDensity == "مدمجة";
        foreach (var button in SidebarItemsPanel.Children.OfType<Button>())
        {
            button.Height = compact ? 31 : 35;
            button.Margin = compact ? new Thickness(0) : new Thickness(0, 1, 0, 1);
            button.Padding = compact ? new Thickness(7, 2, 7, 2) : new Thickness(8, 3, 8, 3);
            if (button.Content is Panel panel)
            {
                foreach (var label in panel.Children.OfType<TextBlock>())
                {
                    if (Grid.GetColumn(label) == 1)
                        label.FontSize = compact ? 11.5 : 13;
                    else
                        label.FontSize = compact ? 14.5 : 16;
                }
            }
        }

        foreach (var button in SectionTabsPanel.Children.OfType<Button>())
        {
            button.Height = compact ? 32 : 38;
            button.FontSize = compact ? 11.5 : 12.5;
            button.Padding = compact ? new Thickness(12, 0, 12, 0) : new Thickness(16, 0, 16, 0);
        }
    }
    private void UpdateSidebarSelection(string sectionKey)
    {
        var activeBorder = new SolidColorBrush(
            Color.FromRgb(252, 235, 237));

        foreach (var button in SidebarItemsPanel.Children.OfType<Button>())
        {
            bool selected = string.Equals(
                button.Tag?.ToString(),
                sectionKey,
                StringComparison.Ordinal);

            button.Background = Brushes.Transparent;
            button.Foreground = Brushes.White;
            button.BorderBrush = selected
                ? activeBorder
                : Brushes.Transparent;
            button.BorderThickness = selected
                ? new Thickness(1.5)
                : new Thickness(1);

            if (button.Content is Panel contentPanel)
            {
                foreach (var text in contentPanel.Children.OfType<TextBlock>())
                {
                    text.Foreground = Brushes.White;
                }
            }
        }
    }
    private void CardSystemTab_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton tab || tab.Tag is not string systemKey)
            return;

        var targetSystem = systemKey == "hotspot" ? DemoAccountSystem.HotSpot : DemoAccountSystem.UserManager;
        if (_selectedCardSystem == targetSystem)
            return;

        _selectedCardSystem = targetSystem;
        if (_currentSection == "cards")
        {
            var tabName = SectionContentTitleTextBlock.Text;
            if (!_sectionTabs["cards"].Any(name => string.Equals(name, tabName, StringComparison.Ordinal)))
                tabName = _sectionTabs["cards"][0];
            ActivateSectionTab(tabName);
        }
        else if (_currentSection == "network-users")
        {
            var tabName = SectionContentTitleTextBlock.Text;

            if (!_sectionTabs["network-users"].Any(name =>
                string.Equals(name, tabName, StringComparison.Ordinal)))
            {
                tabName = "كل الحسابات";
            }

            ActivateSectionTab(tabName);
        }
        else if (_currentSection == "packages")
        {
            ActivateSectionTab("كتالوج الباقات");
        }
    }
    private void SectionTabButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string tab) return;
        ActivateSectionTab(tab);
    }
    private void ActivateSectionTab(string tab)
    {
        if (_currentSection == "cards" && PageTitleTextBlock.Text != "الكروت والدفعات")
            PageTitleTextBlock.Text = "الكروت والدفعات";
        SectionContentTitleTextBlock.Text = tab;
        var unifiedIssuanceLayout = _currentSection == "cards" && string.Equals(tab, "إصدار الكروت", StringComparison.Ordinal);
        SectionTabsScrollViewer.Visibility = unifiedIssuanceLayout ? Visibility.Collapsed : Visibility.Visible;
        PageSubtitleTextBlock.FontSize = unifiedIssuanceLayout ? 10.5 : 11.5;
        if (unifiedIssuanceLayout)
        {
            PageTitleTextBlock.Text = "إصدار الكروت";
            PageSubtitleTextBlock.Text = "إنشاء دفعة جديدة ومعاينة الكرت والطباعة من شاشة واحدة";
            PageTitleIconTextBlock.Text = "\uE8C7";
        }
        else
        {
            PageTitleTextBlock.Text = _currentSection switch
            {
                "dashboard" => "الرئيسية",
                "network-users" => "مستخدمو الشبكة",
                "cards" => "الكروت والدفعات",
                "packages" => "إدارة الباقات",
                "sales" => "المبيعات ونقاط البيع",
                "printing" => "الطباعة والتصميم",
                "networks" => "الشبكات ومصادر المصادقة",
                "network-center" => "مركز الشبكة",
                "reports" => "مركز التقارير",
                "settings" => "الإعدادات والصيانة",
                _ => "تاب كارد"
            };
            PageTitleIconTextBlock.Text = _currentSection switch
            {
                "dashboard" => "\uE80F",
                "network-users" => "\uE77B",
                "cards" => "\uE8A5",
                "packages" => "\uE8D4",
                "sales" => "\uE7BF",
                "printing" => "\uE749",
                "networks" => "\uE701",
                "network-center" => "\uE968",
                "reports" => "\uE9D2",
                "settings" => "\uE713",
                _ => "\uE80F"
            };
            PageSubtitleTextBlock.Text = _currentSection switch
            {
                "network-center" => "متابعة المواقع والأجهزة والتنبيهات ومهام الصيانة",
                "dashboard" => "نظرة تشغيلية على الكروت والدفعات ومصدر الشبكة",
                "cards" => "إصدار الكروت وإدارة الدفعات",
                _ => string.Empty
            };
            PageSubtitleTextBlock.Visibility = (_currentSection == "dashboard" || _currentSection == "cards" || _currentSection == "network-center" || _currentSection == "network-users") ? Visibility.Collapsed : Visibility.Visible;
        }
        SectionContentDescriptionTextBlock.Text = GetSectionDescription(_currentSection, tab);
        SectionTabsScrollViewer.Visibility = (_currentSection == "packages" || _currentSection == "network-users" || _currentSection == "printing") ? Visibility.Collapsed : Visibility.Visible;
        SectionPlaceholderTextBlock.Text = GetSectionPlaceholder(_currentSection, tab);
        RenderFeatureContent(tab);
        if (string.Equals(_currentSection, "cards", StringComparison.Ordinal) &&
            string.Equals(tab, "إصدار الكروت", StringComparison.Ordinal))
        {
            PageTitleTextBlock.FontSize = 19;
        }
        else
        {
            PageTitleTextBlock.ClearValue(System.Windows.Controls.TextBlock.FontSizeProperty);
        }

        // Restore the compact breadcrumb after section/tab title state is refreshed.
        // Keep the heading small and show the active cards sub-page in the path.
        UpdatePageBreadcrumb(_currentSection, string.Equals(_currentSection, "cards", StringComparison.Ordinal) ? tab : null);

        if (SectionFeatureContentHost.Content is INetworkOperationsRefreshable refreshable)
            refreshable.RefreshFromSharedState();

        if (SectionFeatureContentHost.Content is GeneratedCardsView cardsView)
        {
            if (_currentSection == "network-users")
            {
                var systemKey = _selectedCardSystem == DemoAccountSystem.HotSpot
                    ? "hotspot"
                    : "user-manager";
                cardsView.SetSystemFilter(systemKey);
                cardsView.SetBatchFilter("all");
                cardsView.SetStatusFilter("all");
                cardsView.SetActivityFilter(tab == "النشطون الآن" ? "active" : tab == "غير النشطين" ? "inactive" : "all");
                if (tab == "كل الحسابات") cardsView.SetSearchQuery(string.Empty);
            }
            else if (_currentSection == "cards" && tab == "إدارة الكروت")
            {
                cardsView.SetSystemFilter(_selectedCardSystem == DemoAccountSystem.HotSpot ? "hotspot" : "user-manager");
                cardsView.SetBatchFilter("all");
                cardsView.SetStatusFilter("all");
                cardsView.SetActivityFilter("all");
            }
        }

foreach (var child in SectionTabsPanel.Children.OfType<Button>())
    {
        var selected = string.Equals(
            child.Tag?.ToString(),
            tab,
            StringComparison.Ordinal);

        child.Background = selected
            ? (Brush)FindResource("PrimaryBrush")
            : Brushes.Transparent;

        child.Foreground = selected
            ? (Brush)FindResource("SurfaceBrush")
            : (Brush)FindResource("TextBrush");

        child.BorderBrush = Brushes.Transparent;
        child.BorderThickness = new Thickness(0);
    }

        var featureOwnsWorkspace = SectionFeatureContentHost.Content is
            CardIssuanceView or IssuanceWorkspaceView or CardManagementView or GeneratedCardsView or NetworkUsersView or CardBatchesView or PendingCardsView or ImportCardsView or
            PackageManagementView or SalesOperationsDashboardView or SalesOperationEntryView or SalesStockMovementView or SalesAccountsView or SalesOperationsLedgerView or SalesReportView or SalesTransactionsView or SalesPointManagementView or SalesPointsView or PrintQueueView or TemplateDesignerView or ReportsCenterView or NetworkSourcesView or NetworkCenterView or NetworkSitesDevicesView or NetworkClientsView or NetworkMonitoringView or NetworkMaintenanceView or NetworkAnalyticsView or SystemSettingsView or ScreenAppearanceSettingsView or WorkspaceIdentityView or BackupSettingsView;
        var compactIssuanceWorkspace = SectionFeatureContentHost.Content is IssuanceWorkspaceView;
        SectionContentBorder.Padding = compactIssuanceWorkspace ? new Thickness(6) : new Thickness(14);
        SectionTabsScrollViewer.Margin = compactIssuanceWorkspace
            ? new Thickness(0, 0, 0, 6)
            : new Thickness(0, 0, 0, 12);
        var isCardWorkspace = _currentSection == "cards" || _currentSection == "network-users";
        SectionContentBorder.Background = isCardWorkspace ? (Brush)FindResource("AppBgBrush") : Brushes.White;
        SectionContentBorder.BorderBrush = isCardWorkspace ? Brushes.Transparent : (Brush)FindResource("BorderBrushSoft");
        SectionContentBorder.BorderThickness = isCardWorkspace ? new Thickness(0) : new Thickness(1);
        SectionContentBorder.CornerRadius = isCardWorkspace ? new CornerRadius(0) : new CornerRadius(12);
        SectionContentBorder.Padding = isCardWorkspace ? new Thickness(0) : new Thickness(14);
        SectionContentPanel.Visibility = featureOwnsWorkspace ? Visibility.Collapsed : Visibility.Visible;
        SectionBodyHost.Visibility = Visibility.Collapsed;
        SectionBodyHost.Content = null;
    }

    private readonly Dictionary<
    Grid,
    (GridLength First, GridLength Second, HorizontalAlignment Alignment)>
    _sidebarExpandedGridLayouts = new();
    private void ToggleSidebarButton_Click(object sender, RoutedEventArgs e)
    {
        _isSidebarCollapsed = !_isSidebarCollapsed;
        bool collapsed = _isSidebarCollapsed;

        WorkspaceView.ColumnDefinitions[0].Width = new GridLength(collapsed ? 64 : 224);

        SidebarBrandTopPanel.Margin = collapsed
            ? new Thickness(2,  12, 2, 12)
            : new Thickness(13, 12, 13, 12);

        SidebarBrandGrid.ColumnDefinitions[0].Width = new GridLength(collapsed ? 40 : 50);
        SidebarBrandGrid.ColumnDefinitions[1].Width = new GridLength(collapsed ? 0 : 8);
        SidebarBrandMarkBorder.Width = collapsed ? 40 : 48;
        SidebarBrandMarkBorder.Height = collapsed ? 40 : 46;
        SidebarBrandDetails.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;

        SidebarItemsPanel.Margin = collapsed
            ? new Thickness(3, 0, 3, 8)
            : new Thickness(9, 0, 9, 8);

        foreach (UIElement child in SidebarItemsPanel.Children)
        {
            if (child is Button button)
                {
                    button.ToolTip = collapsed
                        ? FindSidebarLabel(button)
                        : null;

                    button.Padding = collapsed
                        ? new Thickness(0)
                        : new Thickness(10, 3, 10, 3);

                    SetSidebarNavigationLayout(button, collapsed);

                    SetSidebarLabelsVisibility(
                        button,
                        collapsed ? Visibility.Collapsed : Visibility.Visible);
                }
                else if (child is StackPanel headingPanel &&
                        headingPanel.Orientation == Orientation.Horizontal)
                {
                    headingPanel.Visibility = collapsed
                        ? Visibility.Collapsed
                        : Visibility.Visible;
                }
                else if (child is TextBlock heading)
                {
                    heading.Visibility = collapsed
                        ? Visibility.Collapsed
                        : Visibility.Visible;
                }
                else if (child is Border separator &&
                        !double.IsNaN(separator.Height) &&
                        separator.Height <= 1)
                {
                    separator.Visibility = collapsed
                        ? Visibility.Collapsed
                        : Visibility.Visible;
                }
        }

        SidebarNetworkFooterBorder.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        SidebarBottomPanel.Margin = collapsed
            ? new Thickness(4, 12, 4, 12)
            : new Thickness(14, 12, 14, 12);

        SignOutButton.Content = collapsed ? "↪" : "تسجيل الخروج";
        SignOutButton.ToolTip = collapsed
            ? "تسجيل الخروج"
            : null;
        SignOutButton.Width = collapsed ? 44 : double.NaN;
        SignOutButton.Padding = collapsed ? new Thickness(0) : new Thickness(14, 0, 14, 0);
        SidebarToggleButton.ToolTip = collapsed ? "توسيع القائمة الجانبية" : "طي القائمة الجانبية";
    }
    private static void SetSidebarLabelsVisibility(DependencyObject parent, Visibility visibility)
    {
        if (parent is TextBlock textBlock)
        {
            string family = textBlock.FontFamily?.Source ?? string.Empty;
            if (!string.Equals(family, "Segoe MDL2 Assets", StringComparison.OrdinalIgnoreCase))
            {
                textBlock.Visibility = visibility;
            }
            return;
        }

        int childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            SetSidebarLabelsVisibility(VisualTreeHelper.GetChild(parent, i), visibility);
        }
    }

    private static string? FindSidebarLabel(DependencyObject parent)
    {
        if (parent is TextBlock textBlock)
        {
            if (string.Equals(
                textBlock.FontFamily?.Source,
                "Segoe MDL2 Assets",
                StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            return string.IsNullOrWhiteSpace(textBlock.Text)
                ? null
                : textBlock.Text.Trim();
        }

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var label = FindSidebarLabel(
                VisualTreeHelper.GetChild(parent, i));

            if (!string.IsNullOrWhiteSpace(label))
                return label;
        }

        return null;
    }
    private void SetSidebarNavigationLayout(
        DependencyObject parent,
        bool collapsed)
    {
        if (parent is Grid grid && grid.ColumnDefinitions.Count == 2)
        {
            // احفظ التخطيط الأصلي مرة واحدة قبل أول عملية طي.
            if (!_sidebarExpandedGridLayouts.ContainsKey(grid))
            {
                _sidebarExpandedGridLayouts[grid] = (
                    grid.ColumnDefinitions[0].Width,
                    grid.ColumnDefinitions[1].Width,
                    grid.HorizontalAlignment);
            }

            if (collapsed)
            {
                // وضع الطي: تخصيص المساحة للأيقونة فقط.
                grid.ColumnDefinitions[0].Width =
                    new GridLength(1, GridUnitType.Star);

                grid.ColumnDefinitions[1].Width =
                    new GridLength(0, GridUnitType.Pixel);

                grid.HorizontalAlignment =
                    HorizontalAlignment.Stretch;
            }
            else
            {
                // استعادة تصميم XAML الأصلي دون فرض Auto على الأعمدة.
                var original = _sidebarExpandedGridLayouts[grid];

                grid.ColumnDefinitions[0].Width = original.First;
                grid.ColumnDefinitions[1].Width = original.Second;
                grid.HorizontalAlignment = original.Alignment;
            }
        }

        int childCount = VisualTreeHelper.GetChildrenCount(parent);

        for (int i = 0; i < childCount; i++)
        {
            SetSidebarNavigationLayout(
                VisualTreeHelper.GetChild(parent, i),
                collapsed);
        }
    }
}
