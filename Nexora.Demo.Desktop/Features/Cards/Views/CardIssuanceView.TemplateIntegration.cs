using System.IO;
using System.Text.Json;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Packages.Services;
using Nexora.Demo.Desktop.Features.Packages.Views;
using Nexora.Demo.Desktop.Features.Shared;

namespace Nexora.Demo.Desktop.Features.Cards.Views;

// Responsibility: Designer template integration and package/offer navigation.
public partial class CardIssuanceView
{
    private static Dictionary<string, bool> CreateDefaultTemplateVisibility() => new(StringComparer.Ordinal)
    {
        ["رمز الدخول"] = true, ["اسم المستخدم"] = true, ["كلمة المرور"] = true,
        ["الصلاحية"] = true, ["الوقت"] = false, ["السعر"] = true, ["التحميل"] = false,
        ["الباقة"] = true, ["الرقم التسلسلي"] = false, ["تاريخ الطباعة"] = false,
        ["رقم الدفعة"] = false, ["نقطة البيع"] = false, ["معلومات إضافية"] = false,
        ["كود المكافأة"] = false, ["رمز QR"] = true, ["الباركود"] = false,
        ["شعار الشبكة"] = true, ["الشريط العلوي"] = true, ["الفاصل"] = true
    };
    private bool IsTemplateElementVisible(string key) =>
        _templateVisibleElements.TryGetValue(key, out var visible) && visible;
    private void LoadDesignerTemplateDefinition()
    {
        _templateVisibleElements.Clear();
        foreach (var pair in CreateDefaultTemplateVisibility()) _templateVisibleElements[pair.Key] = pair.Value;
        _designerTemplateName = "القالب الافتراضي";
        _designerTemplateStyle = "classic";
        _designerColorTheme = "red";

        try
        {
            if (File.Exists(TemplateDesignStoragePath))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(TemplateDesignStoragePath));
                var root = document.RootElement;
                if (root.TryGetProperty("TemplateName", out var name) && name.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(name.GetString()))
                    _designerTemplateName = name.GetString()!.Trim();
                if (root.TryGetProperty("TemplateStyle", out var style) && style.ValueKind == JsonValueKind.String)
                    _designerTemplateStyle = style.GetString() ?? "classic";
                if (root.TryGetProperty("ColorTheme", out var theme) && theme.ValueKind == JsonValueKind.String)
                    _designerColorTheme = theme.GetString() ?? "red";
                if (root.TryGetProperty("VisibleElements", out var visibleElements) && visibleElements.ValueKind == JsonValueKind.Object)
                {
                    foreach (var item in visibleElements.EnumerateObject())
                        if (item.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                            _templateVisibleElements[item.Name] = item.Value.GetBoolean();
                }
            }
        }
        catch (Exception ex)
        {
            SetStatus("تعذر قراءة إعدادات القالب المحفوظ؛ ستُستخدم الحقول الافتراضية. " + ex.Message, true);
        }

        if (PrintTemplateComboBox is not null)
        {
            PrintTemplateComboBox.Items.Clear();
            var currentTemplate = new ComboBoxItem { Content = _designerTemplateName, Tag = "designer" };
            PrintTemplateComboBox.Items.Add(currentTemplate);
            PrintTemplateComboBox.SelectedItem = currentTemplate;
            PrintTemplateComboBox.ToolTip = "القالب المحفوظ من شاشة الطباعة والتصميم";
        }
    }
    private void ApplyPrintTemplate()
    {
        if (CardPreviewBorder is null) return;

        // The saved Template Designer configuration supplies the current style and color theme.
        if (string.Equals(_designerTemplateStyle, "dark", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_designerColorTheme, "dark", StringComparison.OrdinalIgnoreCase))
        {
            CardPreviewBorder.Background = new SolidColorBrush(Color.FromRgb(55, 17, 24));
            CardPreviewBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(160, 24, 24));
            SetPreviewTextBrush(Brushes.White, new SolidColorBrush(Color.FromRgb(245, 218, 222)));
        }
        else if (string.Equals(_designerColorTheme, "white", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(_designerTemplateStyle, "light", StringComparison.OrdinalIgnoreCase))
        {
            CardPreviewBorder.Background = Brushes.White;
            CardPreviewBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(197, 27, 43));
            SetPreviewTextBrush(new SolidColorBrush(Color.FromRgb(32, 36, 42)), new SolidColorBrush(Color.FromRgb(105, 112, 120)));
        }
        else
        {
            CardPreviewBorder.Background = Brushes.White;
            CardPreviewBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(226, 199, 201));
            SetPreviewTextBrush(new SolidColorBrush(Color.FromRgb(32, 36, 42)), new SolidColorBrush(Color.FromRgb(109, 116, 128)));
        }
    }
    private void SetPreviewTextBrush(Brush primary, Brush secondary)
    {
        PreviewCredentialsCaptionTextBlock.Foreground = secondary;
        PreviewCardNumberTextBlock.Foreground = primary;
        PreviewUsernameTextBlock.Foreground = primary;
        PreviewPasswordTextBlock.Foreground = secondary;
        PreviewPackageTextBlock.Foreground = primary;
        PreviewBrandNameTextBlock.Foreground = primary;
        PreviewBrandSubtitleTextBlock.Foreground = secondary;
        PreviewCardTypeTextBlock.Foreground = primary;
        PreviewSourceTypeTextBlock.Foreground = secondary;
        PreviewHeaderDividerBorder.Background = secondary;
        PreviewFooterDividerBorder.Background = secondary;
    }
    private void OpenTemplateDesignerButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var owner = Window.GetWindow(this);
            var designer = new Nexora.Demo.Desktop.Features.Printing.Views.TemplateDesignerView();
            var ownerWidth = owner is not null && owner.ActualWidth > 700 ? owner.ActualWidth : 1180d;
            var ownerHeight = owner is not null && owner.ActualHeight > 500 ? owner.ActualHeight : 720d;
            var dialog = new Window
            {
                Title = "تصميم قالب الكرت",
                Content = designer,
                Owner = owner,
                WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.CanResize,
                ShowInTaskbar = false,
                FlowDirection = FlowDirection.RightToLeft,
                Background = new SolidColorBrush(Color.FromRgb(247, 248, 250)),
                Width = Math.Clamp(ownerWidth * 0.94, 900, 1320),
                Height = Math.Clamp(ownerHeight * 0.92, 620, 900),
                MinWidth = 880,
                MinHeight = 600
            };

            dialog.ShowDialog();

            // The designer persists to the same local definition consumed by the issuance preview.
            LoadDesignerTemplateDefinition();
            ApplyPrintTemplate();
            UpdatePreview();
            PersistCurrentPreferences();
            SetStatus("تم تحديث معاينة الإصدار وفق القالب المحفوظ.", false);
        }
        catch (Exception ex)
        {
            SetStatus("تعذر فتح شاشة تصميم القالب: " + ex.Message, true);
        }
    }
    private void AddPackageButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var providerKey = _targetSystem == DemoAccountSystem.HotSpot ? "hotspot" : "user-manager";
            var owner = Window.GetWindow(this);
            var before = DemoPackageStore.Load();
            var packageIdsBefore = new HashSet<string>(
                before.Packages.Select(package => package.Id?.ToString() ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);
            var bindingKeysBefore = new HashSet<string>(
                before.Bindings.Select(binding => string.Join("|", binding.Id, binding.PackageId, binding.ProviderKey)),
                StringComparer.OrdinalIgnoreCase);

            var packageView = new PackageManagementView(providerKey);
            packageView.PrepareForCardIssuanceOverlay();

            var ownerWidth = owner is not null && owner.ActualWidth > 600 ? owner.ActualWidth : 1120d;
            var ownerHeight = owner is not null && owner.ActualHeight > 450 ? owner.ActualHeight : 740d;
            var dialog = new Window
            {
                Title = "إنشاء باقة جديدة",
                Content = packageView,
                Owner = owner,
                WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.CanResize,
                ShowInTaskbar = false,
                FlowDirection = FlowDirection.RightToLeft,
                Background = new SolidColorBrush(Color.FromRgb(247, 248, 250)),
                Width = Math.Clamp(ownerWidth * 0.90, 850, 1240),
                Height = Math.Clamp(ownerHeight * 0.88, 540, 820),
                MinWidth = 820,
                MinHeight = 520
            };

            dialog.ShowDialog();

            var after = DemoPackageStore.Load();
            var createdPackage = after.Packages
                .Where(package => !package.IsArchived && !packageIdsBefore.Contains(package.Id?.ToString() ?? string.Empty))
                .OrderByDescending(package => package.CreatedAt)
                .FirstOrDefault();

            // Also support attaching an existing commercial definition to this system from the create screen.
            if (createdPackage is null)
            {
                var newBinding = after.Bindings
                    .Where(binding => string.Equals(binding.ProviderKey, providerKey, StringComparison.OrdinalIgnoreCase) &&
                        !bindingKeysBefore.Contains(string.Join("|", binding.Id, binding.PackageId, binding.ProviderKey)))
                    .OrderByDescending(binding => binding.UpdatedAt)
                    .FirstOrDefault();
                if (newBinding is not null)
                    createdPackage = after.Packages.FirstOrDefault(package =>
                        string.Equals(package.Id?.ToString(), newBinding.PackageId?.ToString(), StringComparison.OrdinalIgnoreCase));
            }

            if (createdPackage is not null)
            {
                RefreshPackageCatalog(preserveSelection: false, preferredKey: createdPackage.Id?.ToString());
                SetStatus("تم حفظ الباقة الجديدة وتحديث قائمة الباقات المتاحة للإصدار.", false);
            }
            else
            {
                RefreshPackageCatalog(preserveSelection: true);
                SetStatus("أُغلقت شاشة الباقات. تم تحديث القائمة إذا كانت البيانات قد تغيّرت.", false);
            }
        }
        catch (Exception ex)
        {
            SetStatus("تعذر فتح شاشة إنشاء الباقة: " + ex.Message, true);
        }
    }
    private void OffersButton_Click(object sender, RoutedEventArgs e)

    {

        var dialog = new IssuanceUtilityDialog(showOffers: true, currentPaperSize: _printPaperSize, currentColumns: _printColumns, showCutMarks: _printCutMarks);

        var owner = Window.GetWindow(this);

        if (owner is not null) dialog.Owner = owner;

        dialog.ShowDialog();

    }
}
