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

// Responsibility: Feature-screen creation and cross-screen navigation requests.
public partial class MainWindow
{
    private void RenderFeatureContent(string tab)
    {
        var cardSystemKey = _selectedCardSystem == DemoAccountSystem.HotSpot ? "hotspot" : "user-manager";
        var cacheKey = _currentSection switch
        {
            "network-center" => $"network-center|{tab}",
            "network-users" => $"network-users|{cardSystemKey}|accounts",
            "cards" when tab == "إصدار الكروت" => $"cards|{cardSystemKey}|issuance",
            "cards" when tab == "إدارة الكروت" => $"cards|{cardSystemKey}|cards-list",
            "cards" when tab == "إدارة الدفعات" => $"cards|{cardSystemKey}|batches",
            "cards" when tab == "المعلقات" => $"cards|{cardSystemKey}|pending",
            "cards" when tab == "استيراد الكروت" => $"cards|{cardSystemKey}|import",
            "packages" when tab == "كتالوج الباقات" => $"packages|{cardSystemKey}",
            "sales" when tab == "لوحة المبيعات" => "sales|operations-dashboard",
            "sales" when tab == "تسجيل البيع" => "sales|operation-entry",
            "sales" when tab == "حركة الكروت" => "sales|card-movements",
            "sales" when tab == "حسابات نقاط البيع" => "sales|accounts",
            "sales" when tab == "سجل العمليات" => "sales|operations-ledger",
            "sales" when tab == "تقارير المبيعات" => "sales|sales-report",
            "sales" when tab == "ملخص المبيعات" => "sales|summary",
            "sales" when tab == "معاملات البيع" => "sales|transactions",
            "sales" when tab == "نقاط البيع" => "sales|point-management",
            "sales" when tab == "أداء نقاط البيع" => "sales|points",
            "printing" when tab == "تصميم القالب" => "printing|template-designer",
            "printing" => "printing|workspace",
            "networks" when tab == "مدير المستخدمين" => "networks|user-manager",
            "networks" when tab == "الهوتسبوت" => "networks|hotspot",
            "reports" => "reports|center",
            _ => $"{_currentSection}|{tab}"
        };

        if (!_featureViewCache.TryGetValue(cacheKey, out var featureView))
        {
            try
            {
                featureView = CreateFeatureView(_currentSection, tab);
                if (featureView is not null)
                    _featureViewCache[cacheKey] = featureView;
            }
            catch (Exception ex)
            {
                var logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "NexoraProductDemo", "logs", "ui-runtime-errors.log");
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                    File.AppendAllText(
                        logPath,
                        $"{DateTimeOffset.Now:O} | Section={_currentSection} | Tab={tab}{Environment.NewLine}{ex}{Environment.NewLine}{new string('-', 80)}{Environment.NewLine}",
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                }
                catch
                {
                    // Keep a view-construction failure from closing the entire demo if logging is unavailable.
                }

                SectionFeatureContentHost.Content = null;
                SectionFeatureContentHost.Visibility = Visibility.Collapsed;
                SectionPlaceholderCard.Visibility = Visibility.Visible;
                SectionPlaceholderTextBlock.Text = "تعذر تحميل هذه الشاشة. بقي النظام مفتوحًا، وسُجلت تفاصيل المشكلة للتشخيص.";
                MessageBox.Show(
                    $"تعذر فتح شاشة «{tab}». لم يتم إغلاق النظام.\nالسبب: {ex.GetBaseException().Message}\nالتفاصيل الكاملة محفوظة في:\n{logPath}",
                    "تاب كارد", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
        }

        if (featureView is null)
        {
            SectionFeatureContentHost.Content = null;
            SectionFeatureContentHost.Visibility = Visibility.Collapsed;
            SectionPlaceholderCard.Visibility = Visibility.Visible;
            return;
        }

        SectionPlaceholderCard.Visibility = Visibility.Collapsed;
        SectionFeatureContentHost.Visibility = Visibility.Visible;
        SectionFeatureContentHost.Content = featureView;
    }
    private UserControl? CreateFeatureView(string section, string tab)
    {
        if (section == "settings")
        {
            if (tab == "إعدادات النظام") return new SystemSettingsView();
            if (tab == "الشاشات والمظهر")
            {
                var appearanceView = new ScreenAppearanceSettingsView();
                appearanceView.PreferencesApplied += (_, _) => ApplyShellDisplayPreferences();
                return appearanceView;
            }
            if (tab == "الموقع والهوية")
            {
                var identityView = new WorkspaceIdentityView(_networkDisplayName, _locationSaved, _savedLocationSummary);
                identityView.EditLocationRequested += (_, _) => ShowLocationModal();
                return identityView;
            }
            if (tab == "النسخ الاحتياطي") return new BackupSettingsView();
        }

        if (section == "network-center")
        {
            UserControl networkView = tab switch
            {
                "المواقع والأجهزة" => new NetworkSitesDevicesView(),
                "المتصلون" => new NetworkClientsView(),
                "المراقبة" => new NetworkMonitoringView(),
                "الصيانة" => new NetworkMaintenanceView(),
                "التحليلات" => new NetworkAnalyticsView(),
                _ => new NetworkCenterView()
            };

            if (networkView is INetworkOperationsNavigationSource navigationSource)
            {
                navigationSource.NavigationRequested += (_, args) => ActivateSectionTab(args.TabName);
            }

            return networkView;
        }
        if (section == "network-users")
        {
            var view = new NetworkUsersView(_demoCardIssuanceService, _selectedCardSystem);
            return view;
        }

        if (section == "cards" && tab == "إصدار الكروت")
        {
            var view = new IssuanceWorkspaceView(_demoCardIssuanceService, _selectedCardSystem);
            view.NavigationRequested += FeatureView_NavigationRequested;
            return view;
        }

        if (section == "cards" && tab == "إدارة الكروت")
        {
            var view = new CardManagementView(_demoCardIssuanceService, _selectedCardSystem);
            view.NavigationRequested += FeatureView_NavigationRequested;
            return view;
        }

        if (section == "cards" && tab == "إدارة الدفعات")
        {
            var view = new CardBatchesView(_demoCardIssuanceService);
            view.SetSystemFilter(_selectedCardSystem == DemoAccountSystem.HotSpot ? "hotspot" : "user-manager");
            view.NavigationRequested += FeatureView_NavigationRequested;
            return view;
        }

        if (section == "cards" && tab == "المعلقات")
{
            var view = new PendingCardsView(
                _demoCardIssuanceService,
                _selectedCardSystem);

            view.NavigationRequested += FeatureView_NavigationRequested;
            return view;
        }

        if (section == "cards" && tab == "استيراد الكروت")
            {
                return new ImportCardsView(
                    _demoCardIssuanceService,
                    _selectedCardSystem);
            }

        if (section == "packages")
        {
            var provider = _selectedCardSystem == DemoAccountSystem.HotSpot
                ? "hotspot"
                : "user-manager";

            return new PackageManagementView(provider);
        }

        if (section == "sales" && tab == "لوحة المبيعات")
        {
            var view = new SalesOperationsDashboardView(_salesOperationsService, _salesPointDirectoryService);
            view.NavigationRequested += FeatureView_NavigationRequested;
            return view;
        }

        if (section == "sales" && tab == "تسجيل البيع")
        {
            var view = new SalesOperationEntryView(_salesOperationsService, _salesPointDirectoryService, _demoCardIssuanceService);
            view.NavigationRequested += FeatureView_NavigationRequested;
            return view;
        }

        if (section == "sales" && tab == "حركة الكروت")
            return new SalesCardMovementView(_demoCardIssuanceService, _salesPointDirectoryService, _salesOperationsService);

        if (section == "sales" && tab == "حسابات نقاط البيع")
            return new SalesAccountsView(_salesPointDirectoryService, _salesOperationsService);

        if (section == "sales" && tab == "تقارير المبيعات")
            return new SalesReportView(_demoSalesService);

        // Keep legacy demo views available for the global demo dashboard's existing drill-down;
        // they are intentionally no longer exposed as sales-section tabs.
        if (section == "sales" && tab == "ملخص المبيعات")
            return new SalesReportView(_demoSalesService);

        if (section == "sales" && tab == "معاملات البيع")
            return new SalesTransactionsView(_demoSalesService);

        if (section == "sales" && tab == "سجل العمليات")
            return new SalesOperationsLedgerView(_salesOperationsService);

        if (section == "sales" && tab == "نقاط البيع")
        {
            var view = new SalesPointManagementView(_salesPointDirectoryService, _demoSalesService, _salesOperationsService);
            view.NavigationRequested += FeatureView_NavigationRequested;
            return view;
        }

        if (section == "sales" && tab == "أداء نقاط البيع")
        {
            var view = new SalesPointsView(_demoSalesService);
            view.NavigationRequested += FeatureView_NavigationRequested;
            return view;
        }

        if (section == "printing" && tab == "تصميم القالب")
            return new TemplateDesignerView();

        if (section == "printing")
        {
            var view = new PrintQueueView(_demoCardIssuanceService);
            view.NavigationRequested += FeatureView_NavigationRequested;
            return view;
        }

        if (section == "networks")
        {
            var area = tab == "مدير المستخدمين"
        ? "user-manager"
    : tab == "الهوتسبوت"
    ? "hotspot"
        : "sources";
            var view = new NetworkSourcesView(area, _demoCardIssuanceService);
            view.NavigationRequested += FeatureView_NavigationRequested;
            return view;
        }

        if (section == "reports")
        {
            var view = new ReportsCenterView(_demoCardIssuanceService, _demoSalesService);
            view.NavigationRequested += FeatureView_NavigationRequested;
            return view;
        }

        return null;
    }
    private void FeatureView_NavigationRequested(object? sender, FeatureNavigationRequestedEventArgs e)
    {
        if (_locationRequiredForCurrentAccount && !_locationSaved)
        {
            ShowLocationModal();
            return;
        }

        if (e.SectionKey is "hotspot" or "user-manager")
        {
            if (e.TabName == "إضافة الكروت")
            {
                NavigateTo("cards");
                ActivateSectionTab("إصدار الكروت");
                if (SectionFeatureContentHost.Content is IssuanceWorkspaceView issuanceView && (e.SectionKey is "hotspot" or "user-manager"))
                    issuanceView.SelectTargetSystem(e.SectionKey);
                return;
            }
            NavigateTo("network-users");

            if (e.SectionKey == "hotspot")
                HotSpotCardSystemTab.IsChecked = true;
            else
                UserManagerCardSystemTab.IsChecked = true;

            ActivateSectionTab("كل الحسابات");
            }
        else
        {
            NavigateTo(e.SectionKey);
            ActivateSectionTab(e.TabName);
        }
        if (e.ContextKind == "card" && SectionFeatureContentHost.Content is NetworkUsersView networkUsersView)
        {
            if (!string.IsNullOrWhiteSpace(e.SystemKey)) networkUsersView.SetSystemFilter(e.SystemKey);
            networkUsersView.SetBatchFilter("all");
            networkUsersView.SetActivityFilter("all");
            networkUsersView.SetStatusFilter("all");
            networkUsersView.OpenCardDetails(e.ContextValue ?? string.Empty);
        }
        else if (e.ContextKind == "card" && SectionFeatureContentHost.Content is GeneratedCardsView cardsView)
        {
            if (!string.IsNullOrWhiteSpace(e.SystemKey)) cardsView.SetSystemFilter(e.SystemKey);
            cardsView.SetBatchFilter("all");
            cardsView.SetActivityFilter("all");
            cardsView.SetStatusFilter("all");
            cardsView.OpenCardDetails(e.ContextValue ?? string.Empty);
        }
        else if (e.ContextKind == "batch-details" && SectionFeatureContentHost.Content is CardBatchesView reportBatchesView)
        {
            if (!string.IsNullOrWhiteSpace(e.SystemKey)) reportBatchesView.SetSystemFilter(e.SystemKey);
            reportBatchesView.FocusBatch(e.ContextValue ?? string.Empty);
        }
        else if (e.ContextKind == "batch" && SectionFeatureContentHost.Content is CardBatchesView batchesView)
        {
            batchesView.FocusBatch(e.ContextValue ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(e.SystemKey)) batchesView.SetSystemFilter(e.SystemKey);
        }
        else if (e.ContextKind == "batch" && SectionFeatureContentHost.Content is NetworkUsersView usersInBatchView)
        {
            if (!string.IsNullOrWhiteSpace(e.SystemKey)) usersInBatchView.SetSystemFilter(e.SystemKey);
            usersInBatchView.SetActivityFilter("all");
            usersInBatchView.SetStatusFilter("all");
            usersInBatchView.SetBatchFilter(e.ContextValue ?? "all");
        }
        else if (e.ContextKind == "batch" && SectionFeatureContentHost.Content is GeneratedCardsView cardsInBatchView)
        {
            if (!string.IsNullOrWhiteSpace(e.SystemKey)) cardsInBatchView.SetSystemFilter(e.SystemKey);
            cardsInBatchView.SetActivityFilter("all");
            cardsInBatchView.SetStatusFilter("all");
            cardsInBatchView.SetBatchFilter(e.ContextValue ?? "all");
        }
        else if (e.ContextKind == "sale" && SectionFeatureContentHost.Content is SalesTransactionsView transactionsView)
        {
            transactionsView.FocusTransaction(e.ContextValue ?? string.Empty, showDetails: true);
        }
        else if (e.ContextKind == "sale" && SectionFeatureContentHost.Content is SalesReportView salesView)
        {
            salesView.ShowTransaction(e.ContextValue ?? string.Empty);
        }
        else if (e.ContextKind == "point" && SectionFeatureContentHost.Content is SalesTransactionsView pointTransactionsView)
        {
            pointTransactionsView.FocusPointOfSale(e.ContextValue ?? string.Empty);
        }
        else if (e.ContextKind == "point" && SectionFeatureContentHost.Content is SalesOperationsLedgerView pointLedgerView)
        {
            pointLedgerView.FocusPointOfSale(e.ContextValue ?? string.Empty);
        }

        if (!string.IsNullOrWhiteSpace(e.SystemKey) && SectionFeatureContentHost.Content is IssuanceWorkspaceView targetIssuance)
            targetIssuance.SelectTargetSystem(e.SystemKey);
    }
    private static string GetSectionDescription(string section, string tab) => section switch
    {
        "network-users" => tab switch
        {
            "النشطون الآن" => "الحسابات التي تحمل حالة استخدام نشطة في سجلات المصدر.",
            "غير النشطين" => "الحسابات التي لا تحمل حالة استخدام نشطة حاليًا.",
            "مستخدمو HotSpot" => "حسابات جهة HotSpot ضمن قائمة المستخدمين الموحدة.",
            "مستخدمو User Manager" => "حسابات جهة User Manager ضمن قائمة المستخدمين الموحدة.",
            _ => "إدارة موحدة لحسابات الدخول مع إمكانية التصفية حسب المصدر والحالة والدفعة."
        },
        "cards" => "إصدار الكروت ومتابعة الدفعات والمخزون في مساحة واحدة.",
        "packages" => "تعريف باقات الاتصال التجارية وربطها بإعدادات المصدر.",
        "sales" => tab switch
        {
            "لوحة المبيعات" => "لوحة تشغيلية لدفتر المبيعات المحلي: إجمالي البيع، ذمم البقالات، عمولات القنوات، التجميع الإلكتروني ومصاريف تجهيز الكروت.",
            "تسجيل البيع" => "تسجيل بيع صاحب الشبكة أو قناة كروت الشبكات أو المحافظ الإلكترونية مع معاينة صافي المستحق والعمولة ومصاريف التجهيز.",
            "حركة الكروت" => "مراجعة حركات إصدار الدفعات والتسليم والبيع من السجلات المتاحة، مع فتح نموذج التوريد الحالي عند الحاجة.",
            "حسابات نقاط البيع" => "متابعة أرصدة البقالات والفواتير والتسويات الجزئية والكاملة ومنع تحصيل أكثر من الرصيد.",
            "سجل العمليات" => "سجل تشغيل موحد لعمليات البيع والتوريد والتسويات والمصروفات مع البحث والتصفية والتصدير وتفاصيل السجل المحدد.",
            "تقارير المبيعات" => "تحليل المبيعات المسجلة حسب الفترة والقناة ونقطة البيع والباقات، باستخدام بيانات التقرير الحالية.",
            "نقاط البيع" => "إدارة دليل نقاط البيع والعمولات وسياسة الآجل وبيانات التواصل والحالة.",
            _ => "إدارة المبيعات ونقاط البيع ضمن السجلات المحلية المتاحة."
        },
        "printing" => tab == "تصميم القالب" ? "تصميم قوالب الكروت وإدارة ظهور الحقول وحفظ إعدادات القالب محليًا." : "إدارة قائمة الكروت ومراجعة المعاينة وإرسال مهمة الطباعة.",
        "networks" => "إعداد مصادر المصادقة وربط وظائفها بوحدات Nexora.",
        "reports" => "التقارير التجارية والإدارية والتشغيلية مجمّعة حسب مصدرها.",
        "network-center" => tab switch
        {
            "نظرة عامة" => "ملخص تشغيل موحد للأجهزة والمواقع والتنبيهات والمهام المفتوحة.",
            "المواقع والأجهزة" => "حصر الأجهزة بحسب الدور والموقع وعلاقة الاتصال؛ الحالات التمثيلية موضحة داخل الشاشة.",
            "المتصلون" => "عرض جلسات HotSpot وPPPoE منفصلة عن روابط CPE والأجهزة النهائية.",
            "المراقبة" => "مراجعة التنبيهات التجريبية وربطها بإجراء صيانة واضح.",
            "الصيانة" => "إدارة مهام الفحص والمتابعة المرتبطة بالأجهزة والتنبيهات.",
            _ => "تحليل بيانات السيناريو التجريبي حسب الفترة والموقع، دون الادعاء بوجود قياس مباشر."
        },
        "settings" => tab switch
        {
            "إعدادات النظام" => "تحديد صفحة البداية وتفضيلات الإجراءات والبيانات المحلية.",
            "الشاشات والمظهر" => "تطبيق كثافة مريحة أو مدمجة على القائمة الجانبية وتبويبات الأقسام.",
            "الموقع والهوية" => "مراجعة اسم مساحة الشبكة وموقعها وتحديث المعلومات المحفوظة محليًا.",
            "النسخ الاحتياطي" => "إنشاء حزمة محلية من ملفات بيانات الديمو المحفوظة ومراجعة سجل النسخ.",
            _ => "إعدادات النظام والواجهة."
        },
        _ => $"{tab} ضمن إدارة Nexora."
    };
    private static string GetSectionPlaceholder(string section, string tab) => (section, tab) switch
    {
        ("settings", "الموقع والهوية") => "إدارة معلومات الشبكة وموقعها والعناصر المرتبطة بمساحة العمل.",
        ("settings", "النسخ الاحتياطي") => "مراجعة خيارات الاحتفاظ بنسخة احتياطية من بيانات التطبيق.",
        ("settings", "الشاشات والمظهر") => "ضبط كثافة التنقل وطريقة عرض عناصر الواجهة.",
        ("settings", _) => "إعدادات النظام والواجهة والبيانات المحلية.",
        ("networks", "مصادر المصادقة") => "إدارة مصادر المصادقة وطرق الربط المستخدمة لحسابات الشبكة.",
        ("networks", "صفحات الدخول") => "إدارة مظهر ومحتوى صفحات الدخول بحسب خدمة الشبكة.",
        _ => "اختر وظيفة من التبويبات لفتح مساحة العمل المرتبطة بها."
    };
}
