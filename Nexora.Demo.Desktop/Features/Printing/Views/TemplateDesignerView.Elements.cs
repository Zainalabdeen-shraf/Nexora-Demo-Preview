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

// Responsibility: Template element catalog, editor properties, and appearance state.
public partial class TemplateDesignerView
{
    private static Dictionary<string, bool> CreateDefaultVisibility() => new(StringComparer.Ordinal)
    {
        ["رمز الدخول"] = true,
        ["اسم المستخدم"] = true,
        ["كلمة المرور"] = true,
        ["الصلاحية"] = true,
        ["الوقت"] = true,
        ["السعر"] = true,
        ["التحميل"] = true,
        ["الرقم التسلسلي"] = false,
        ["تاريخ الطباعة"] = false,
        ["رقم الدفعة"] = false,
        ["نقطة البيع"] = false,
        ["معلومات إضافية"] = false,
        ["أرقام التواصل"] = true,
        ["كود المكافأة"] = false,
        ["رمز QR"] = false,
        ["الباركود"] = false,
        ["شعار الشبكة"] = true,
        ["الباقة"] = true,
        ["الشريط العلوي"] = true,
        ["الفاصل"] = true
    };
    private static Dictionary<string, ElementDesignProperties> CreateDefaultElementProperties()
    {
        // Keep authored positions/sizes proportional to the old canvas, but convert all
        // geometry to physical WPF DIPs. FontSize intentionally is NOT scaled.
        var properties = new Dictionary<string, ElementDesignProperties>(StringComparer.Ordinal)
        {
            ["login"] = new() { X = 98, Y = 5, Width = 245, Height = 28, Text = "50723015", IsBold = true, FontSize = 11.5, FontFamily = "Segoe UI" },
            ["username"] = new() { X = 98, Y = 39, Width = 174, Height = 18, Text = "اسم المستخدم: 50723015", FontSize = 7.7, FontFamily = "Segoe UI" },
            ["password"] = new() { X = 277, Y = 39, Width = 174, Height = 18, Text = "كلمة المرور: 1432", FontSize = 7.7, FontFamily = "Segoe UI" },
            ["validity"] = new() { X = 98, Y = 58, Width = 118, Height = 18, Text = "الصلاحية: 7 أيام", IsBold = true, FontSize = 7.4, FontFamily = "Segoe UI" },
            ["time"] = new() { X = 221, Y = 58, Width = 96, Height = 18, Text = "الوقت: 1 س 0 د", FontSize = 7.4, FontFamily = "Segoe UI" },
            ["price"] = new() { X = 320, Y = 58, Width = 124, Height = 18, Text = "السعر: 100 ريال", IsBold = true, FontSize = 7.4, FontFamily = "Segoe UI" },
            ["package"] = new() { X = 98, Y = 77, Width = 170, Height = 18, Text = "الباقة: —", FontSize = 7.7, FontFamily = "Segoe UI" },
            ["download"] = new() { X = 276, Y = 77, Width = 174, Height = 18, Text = "التحميل: —", FontSize = 7.7, FontFamily = "Segoe UI" },
            ["contact"] = new() { X = 98, Y = 94, Width = 350, Height = 14, Text = "", FontSize = 6, FontFamily = "Segoe UI" },
            ["serial"] = new() { X = 98, Y = 95, Width = 145, Height = 12, Text = "الرقم التسلسلي: 0001", FontSize = 9, FontFamily = "Segoe UI" },
            ["printDate"] = new() { X = 98, Y = 95, Width = 145, Height = 12, Text = "تاريخ الطباعة", FontSize = 9, FontFamily = "Segoe UI" },
            ["batch"] = new() { X = 277, Y = 95, Width = 174, Height = 12, Text = "رقم الدفعة: B-0001", FontSize = 9, FontFamily = "Segoe UI" },
            ["pointOfSale"] = new() { X = 98, Y = 95, Width = 145, Height = 12, Text = "نقطة البيع", FontSize = 9, FontFamily = "Segoe UI" },
            ["extra"] = new() { X = 98, Y = 95, Width = 350, Height = 13, Text = "", FontSize = 9, FontFamily = "Segoe UI" },
            ["reward"] = new() { X = 277, Y = 95, Width = 174, Height = 12, Text = "كود المكافأة", FontSize = 9, FontFamily = "Segoe UI" },
            ["qr"] = new() { X = 373, Y = 3, Width = 32, Height = 32 },
            ["barcode"] = new() { X = 98, Y = 94, Width = 145, Height = 13 },
            ["brand"] = new() { X = 7, Y = 48, Width = 72, Height = 17, Text = "TAB CARD", IsBold = true, FontSize = 7.4, FontFamily = "Segoe UI" },
            ["networkName"] = new() { X = 7, Y = 68, Width = 72, Height = 30, Text = "اسم الشبكة", FontSize = 6.5, FontFamily = "Segoe UI" },
            ["logoBadge"] = new() { X = 25, Y = 7, Width = 36, Height = 36 },
            ["logoMark"] = new() { X = 30, Y = 14, Width = 26, Height = 20, Text = "TC", IsBold = true, FontSize = 8.5, FontFamily = "Segoe UI" },
            ["header"] = new() { X = 0, Y = 0, Width = 86, Height = 112 },
            ["divider"] = new() { X = 94, Y = 34, Width = 310, Height = 1 }
        };

        foreach (var item in properties.Values)
        {
            item.X *= LegacyToDipScale;
            item.Y *= LegacyToDipScale;
            item.Width *= LegacyToDipScale;
            item.Height *= LegacyToDipScale;
        }
        return properties;
    }
    private void InitializeElementOptions()
    {
        var options = new (string Tag, string Label)[]
        {
            ("login", "رمز الدخول"), ("username", "اسم المستخدم"), ("password", "كلمة المرور"),
            ("validity", "الصلاحية"), ("time", "الوقت"), ("price", "السعر"),
            ("package", "الباقة"), ("download", "التحميل"), ("contact", "أرقام التواصل"),
            ("serial", "الرقم التسلسلي"), ("printDate", "تاريخ الطباعة"), ("batch", "رقم الدفعة"),
            ("pointOfSale", "نقطة البيع"), ("extra", "معلومات إضافية"), ("reward", "كود المكافأة"),
            ("qr", "رمز QR"), ("barcode", "الباركود"), ("brand", "اسم العلامة"),
            ("networkName", "اسم الشبكة"), ("logoBadge", "خلفية الشعار"), ("logoMark", "رمز الشعار"),
            ("header", "الشريط الجانبي"), ("divider", "الفاصل")
        };

        SelectedElementComboBox.Items.Clear();
        foreach (var option in options)
            SelectedElementComboBox.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Tag });
        SelectedElementComboBox.SelectedIndex = 0;
    }
    private void TemplateStyleComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isReady) return;
        var style = GetSelectedTag(TemplateStyleComboBox);
        if (style == "dark") ColorThemeComboBox.SelectedIndex = 1;
        else if (style == "light") ColorThemeComboBox.SelectedIndex = 2;
        else ColorThemeComboBox.SelectedIndex = 0;
        UpdatePreview();
    }
    private void ColorThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isReady) ApplyTheme();
    }
    private void FontFamilyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isReady) return;
        var font = GetSelectedTag(FontFamilyComboBox) ?? "Segoe UI";
        var family = new FontFamily(font);
        PreviewBrandTextBlock.FontFamily = family;
        PreviewLogoMarkTextBlock.FontFamily = family;
        PreviewNetworkTextBlock.FontFamily = family;
        PreviewLoginCodeTextBlock.FontFamily = family;
        PreviewUsernameTextBlock.FontFamily = family;
        PreviewPasswordTextBlock.FontFamily = family;
        PreviewValidityTextBlock.FontFamily = family;
        PreviewTimeBlock.FontFamily = family;
        PreviewPriceTextBlock.FontFamily = family;
        PreviewPackageTextBlock.FontFamily = family;
        PreviewDownloadTextBlock.FontFamily = family;
        PreviewSerialTextBlock.FontFamily = family;
        PreviewPointOfSaleTextBlock.FontFamily = family;
        PreviewBatchTextBlock.FontFamily = family;
        PreviewPrintDateTextBlock.FontFamily = family;
        PreviewExtraTextBlock.FontFamily = family;
        PreviewContactBlock.FontFamily = family;
        PreviewRewardTextBlock.FontFamily = family;
    }
    private static void ResetPackageComboForItemsSource(ComboBox combo)
    {
        // WPF cannot mix declarative ComboBoxItem children with ItemsSource.
        // Older/saved XAML variants may still contain explicit items, so remove
        // any binding and clear explicit items before binding the package catalog.
        if (combo.ItemsSource is not null)
            combo.ItemsSource = null;

        if (combo.Items.Count > 0)
            combo.Items.Clear();
    }
    private void LoadPackageOptions()
    {
        try
        {
            var hotSpotPackages = DemoCardPackageCatalog.Load(DemoAccountSystem.HotSpot);
            var userManagerPackages = DemoCardPackageCatalog.Load(DemoAccountSystem.UserManager);

            ResetPackageComboForItemsSource(HotspotPackageComboBox);
            ResetPackageComboForItemsSource(UserManagerPackageComboBox);

            HotspotPackageComboBox.DisplayMemberPath = nameof(DemoCardPackage.DisplayName);
            HotspotPackageComboBox.ItemsSource = hotSpotPackages;
            HotspotPackageComboBox.SelectedItem = hotSpotPackages.FirstOrDefault();

            UserManagerPackageComboBox.DisplayMemberPath = nameof(DemoCardPackage.DisplayName);
            UserManagerPackageComboBox.ItemsSource = userManagerPackages;
            UserManagerPackageComboBox.SelectedItem = userManagerPackages.FirstOrDefault();

            if (hotSpotPackages.Count == 0 && userManagerPackages.Count == 0)
                _packageCatalogLoadError = "لا توجد باقات نشطة مرتبطة بمصدري HotSpot أو User Manager. راجع كتالوج الباقات.";
            else if (hotSpotPackages.Count == 0 || userManagerPackages.Count == 0)
                _packageCatalogLoadError = hotSpotPackages.Count == 0
                    ? "لا توجد باقات نشطة مرتبطة بـ HotSpot؛ تحقق من إعدادات مصدر الشبكة."
                    : "لا توجد باقات نشطة مرتبطة بـ User Manager؛ تحقق من إعدادات مصدر الشبكة.";
        }
        catch (Exception ex)
        {
            _packageCatalogLoadError = "تعذر تحميل الباقات من كتالوج الديمو: " + ex.Message;

            // Make the fallback path safe too: clearing ItemsSource without
            // clearing declarative Items can throw the same WPF exception again.
            ResetPackageComboForItemsSource(HotspotPackageComboBox);
            ResetPackageComboForItemsSource(UserManagerPackageComboBox);
            HotspotPackageComboBox.ItemsSource = Array.Empty<DemoCardPackage>();
            UserManagerPackageComboBox.ItemsSource = Array.Empty<DemoCardPackage>();
        }
    }
    private DemoCardPackage? GetActivePackage()
    {
        var combo = _activePackageSource == "user-manager" ? UserManagerPackageComboBox : HotspotPackageComboBox;
        return combo.SelectedItem as DemoCardPackage;
    }
    private static string GetPackageName(DemoCardPackage package)
    {
        var separator = package.DisplayName.LastIndexOf(" — ", StringComparison.Ordinal);
        return separator > 0 ? package.DisplayName[..separator].Trim() : package.DisplayName.Trim();
    }
    private void PackageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isReady) return;
        if (ReferenceEquals(sender, UserManagerPackageComboBox)) _activePackageSource = "user-manager";
        else _activePackageSource = "hotspot";
        UpdateSamplePackageFields();
        SyncSelectedElementEditor();
        UpdatePreview();
    }
    private void SelectedElementComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isReady) return;
        SyncSelectedElementEditor();
    }
    private void SyncSelectedElementEditor()
    {
        var element = GetSelectedElement();
        if (element is null) return;

        var left = Canvas.GetLeft(element);
        var top = Canvas.GetTop(element);
        if (!double.IsFinite(left)) left = 0;
        if (!double.IsFinite(top)) top = 0;
        ElementXTextBox.Text = left.ToString("0.#", CultureInfo.InvariantCulture);
        ElementYTextBox.Text = top.ToString("0.#", CultureInfo.InvariantCulture);
        ElementWidthTextBox.Text = GetElementWidth(element).ToString("0.#", CultureInfo.InvariantCulture);
        ElementHeightTextBox.Text = GetElementHeight(element).ToString("0.#", CultureInfo.InvariantCulture);
        ElementAngleTextBox.Text = GetElementRotation(element).ToString("0.#", CultureInfo.InvariantCulture);

        if (element is TextBlock textBlock)
        {
            SelectedElementTextBox.Text = textBlock.Text;
            ElementFontSizeTextBox.Text = textBlock.FontSize.ToString("0.#", CultureInfo.InvariantCulture);
            BoldElementCheckBox.IsChecked = textBlock.FontWeight == FontWeights.Bold || textBlock.FontWeight == FontWeights.SemiBold;
            SelectedElementTextBox.IsEnabled = true;
            ElementFontSizeTextBox.IsEnabled = true;
            BoldElementCheckBox.IsEnabled = true;
        }
        else
        {
            SelectedElementTextBox.Text = string.Empty;
            SelectedElementTextBox.IsEnabled = false;
            ElementFontSizeTextBox.Text = "11";
            ElementFontSizeTextBox.IsEnabled = false;
            BoldElementCheckBox.IsChecked = false;
            BoldElementCheckBox.IsEnabled = false;
        }
        UpdateSelectionFrame();
    }
    private void ElementVisibility_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isReady || sender is not CheckBox checkBox || checkBox.Tag is not string key) return;
        _visibleElements[key] = checkBox.IsChecked == true;
        UpdatePreview();
    }
    private void OpenElementsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = CreateDialog("عناصر الكرت", 760, 590);
        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var heading = new TextBlock
        {
            Text = "حدد البيانات والعناصر التي تريد إظهارها على البطاقة",
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 12)
        };
        Grid.SetRow(heading, 0);
        root.Children.Add(heading);

        var checkPanel = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 12) };
        var checks = new Dictionary<string, CheckBox>(StringComparer.Ordinal);
        foreach (var label in ElementLabels)
        {
            var check = new CheckBox
            {
                Content = label,
                IsChecked = IsTemplateElementVisible(label),
                Margin = new Thickness(7, 9, 7, 9),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12,
                Tag = label
            };
            checks[label] = check;
            checkPanel.Children.Add(check);
        }
        Grid.SetRow(checkPanel, 1);
        root.Children.Add(checkPanel);

        var extraFields = new Grid { Margin = new Thickness(0, 4, 0, 12) };
        extraFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        extraFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        extraFields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var urlPanel = BuildLabeledField("رابط صفحة الدخول / رمز QR", new TextBox { Text = _loginPageUrl, Height = 35, FlowDirection = FlowDirection.LeftToRight });
        var barcodePanel = BuildLabeledField("نص الباركود", new TextBox { Text = _barcodeText, Height = 35, FlowDirection = FlowDirection.LeftToRight });
        Grid.SetColumn(urlPanel, 0);
        Grid.SetColumn(barcodePanel, 2);
        extraFields.Children.Add(urlPanel);
        extraFields.Children.Add(barcodePanel);
        Grid.SetRow(extraFields, 2);
        root.Children.Add(extraFields);

        var actions = BuildDialogActions(dialog, "تطبيق العناصر", "إلغاء");
        Grid.SetRow(actions, 3);
        root.Children.Add(actions);
        dialog.Content = root;

        if (dialog.ShowDialog() == true)
        {
            foreach (var pair in checks) _visibleElements[pair.Key] = pair.Value.IsChecked == true;
            _loginPageUrl = ((TextBox)((StackPanel)urlPanel).Children[1]).Text.Trim();
            _barcodeText = ((TextBox)((StackPanel)barcodePanel).Children[1]).Text.Trim();
            SyncMainElementChecks();
            UpdatePreview();
            SetStatus("تم تطبيق عناصر البطاقة على المعاينة. احفظ القالب للاحتفاظ بالتغييرات.", isSuccess: true);
        }
    }
    private void ApplyPropertiesButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isReady) return;
        var font = GetSelectedTag(FontFamilyComboBox) ?? "Segoe UI";
        var family = new FontFamily(font);
        foreach (var textBlock in new[]
        {
            PreviewBrandTextBlock, PreviewNetworkTextBlock, PreviewLoginCodeTextBlock, PreviewUsernameTextBlock,
            PreviewPasswordTextBlock, PreviewValidityTextBlock, PreviewPriceTextBlock, PreviewPackageTextBlock,
            PreviewDownloadTextBlock, PreviewSerialTextBlock, PreviewPointOfSaleTextBlock, PreviewBatchTextBlock,
            PreviewPrintDateTextBlock, PreviewExtraTextBlock, PreviewContactBlock, PreviewRewardTextBlock, PreviewLogoMarkTextBlock
        }) textBlock.FontFamily = family;

        var selected = GetSelectedElement();
        if (selected is not null)
        {
            var maxCanvasWidth = Math.Max(DesignBaseWidth, DesignBaseWidth * 200d / BaseWidthMm);
            var maxCanvasHeight = Math.Max(DesignBaseHeight, DesignBaseHeight * 200d / BaseHeightMm);
            var x = ParseDouble(ElementXTextBox.Text, Canvas.GetLeft(selected), 0, maxCanvasWidth);
            var y = ParseDouble(ElementYTextBox.Text, Canvas.GetTop(selected), 0, maxCanvasHeight);
            var width = ParseDouble(ElementWidthTextBox.Text, GetElementWidth(selected), 5, maxCanvasWidth);
            var height = ParseDouble(ElementHeightTextBox.Text, GetElementHeight(selected), 5, maxCanvasHeight);
            var angle = ParseDouble(ElementAngleTextBox.Text, GetElementRotation(selected), -180, 180);
            selected.Width = width;
            selected.Height = height;
            selected.RenderTransformOrigin = new Point(0.5, 0.5);
            selected.RenderTransform = new RotateTransform(angle);
            var clampedPosition = ClampElementPosition(selected, x, y);
            Canvas.SetLeft(selected, clampedPosition.X);
            Canvas.SetTop(selected, clampedPosition.Y);
            if (selected is TextBlock textBlock)
            {
                textBlock.Text = SelectedElementTextBox.Text;
                textBlock.FontWeight = BoldElementCheckBox.IsChecked == true ? FontWeights.Bold : FontWeights.Normal;
                textBlock.FontSize = ParseDouble(ElementFontSizeTextBox.Text, textBlock.FontSize, 6, 60);
            }
            ElementXTextBox.Text = clampedPosition.X.ToString("0.#", CultureInfo.InvariantCulture);
            ElementYTextBox.Text = clampedPosition.Y.ToString("0.#", CultureInfo.InvariantCulture);
            ElementWidthTextBox.Text = width.ToString("0.#", CultureInfo.InvariantCulture);
            ElementHeightTextBox.Text = height.ToString("0.#", CultureInfo.InvariantCulture);
            ElementAngleTextBox.Text = angle.ToString("0.#", CultureInfo.InvariantCulture);
        }

        var cardWidth = ParseDouble(CardWidthTextBox.Text, 66, 10, 200);
        var cardHeight = ParseDouble(CardHeightTextBox.Text, 15.5, 10, 200);
        var cardAngle = ParseDouble(CardRotationTextBox.Text, 0, -180, 180);
        CardWidthTextBox.Text = cardWidth.ToString("0.#", CultureInfo.InvariantCulture);
        CardHeightTextBox.Text = cardHeight.ToString("0.#", CultureInfo.InvariantCulture);
        CardRotationTextBox.Text = cardAngle.ToString("0.#", CultureInfo.InvariantCulture);
        ApplyCardDimensionTransform();
        if (_isAutoFitZoom) FitCardToWorkspace();
        ApplyTheme();
        ApplyElementVisibility();
        UpdateSelectionFrame();
        ElementCountTextBlock.Text = $"{_visibleElements.Count(pair => pair.Value && pair.Key != "شعار الشبكة")} عناصر";
        SyncSelectedElementEditor();
        SetStatus($"تم تطبيق الخصائص. القالب {cardWidth:0.#} × {cardHeight:0.#} مم والدوران {cardAngle:0.#}°.", isSuccess: true);
    }
    private void UpdatePreview()
    {
        if (!_isReady || CardSurfaceBorder is null) return;
        PreviewQrBorder.ToolTip = _loginPageUrl;
        PreviewBarcodeBorder.ToolTip = _barcodeText;
        ApplyImportedBackgroundAppearance();
        ApplyTheme();
        ApplyElementVisibility();
        ApplyCardDimensionTransform();
        var count = _visibleElements.Count(pair => pair.Value && pair.Key != "شعار الشبكة");
        ElementCountTextBlock.Text = $"{count} عناصر";
        UpdateSelectionFrame();
    }
    private void ApplyTheme()
    {
        if (CardSurfaceBorder is null) return;
        var theme = GetSelectedTag(ColorThemeComboBox) ?? "red";
        switch (theme)
        {
            case "dark":
                CardSurfaceBorder.Background = new SolidColorBrush(Color.FromRgb(0x8F, 0x11, 0x19));
                CardSurfaceBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x5E, 0x0B, 0x10));
                CardHeaderBorder.Background = new SolidColorBrush(Color.FromRgb(0x5E, 0x0B, 0x10));
                PreviewBrandTextBlock.Foreground = Brushes.White;
                PreviewNetworkTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xE5, 0xE5));
                PreviewLoginCodeTextBlock.Foreground = Brushes.White;
                PreviewUsernameTextBlock.Foreground = Brushes.White;
                PreviewPasswordTextBlock.Foreground = Brushes.White;
                PreviewValidityTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xBF, 0xC3));
                PreviewTimeBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xBF, 0xC3));
                PreviewPriceTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xBF, 0xC3));
                PreviewPackageTextBlock.Foreground = Brushes.White;
                PreviewDownloadTextBlock.Foreground = Brushes.White;
                PreviewContactBlock.Foreground = Brushes.White;
                break;
            case "white":
                CardSurfaceBorder.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF9, 0xF8));
                CardSurfaceBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xE7, 0xC5, 0xC8));
                CardHeaderBorder.Background = new SolidColorBrush(Color.FromRgb(0xF4, 0xE3, 0xE4));
                PreviewBrandTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0x18, 0x18));
                PreviewNetworkTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x26, 0x2B));
                PreviewLoginCodeTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x20, 0x24, 0x2A));
                PreviewUsernameTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0x39, 0x41));
                PreviewPasswordTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0x39, 0x41));
                PreviewValidityTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0x18, 0x18));
                PreviewTimeBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0x18, 0x18));
                PreviewPriceTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0x18, 0x18));
                PreviewPackageTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0x39, 0x41));
                PreviewDownloadTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0x39, 0x41));
                PreviewContactBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0x39, 0x41));
                break;
            default:
                CardSurfaceBorder.Background = Brushes.White;
                CardSurfaceBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x94, 0x15, 0x1A));
                CardHeaderBorder.Background = (Brush)FindResource("PrimaryBrush");
                PreviewBrandTextBlock.Foreground = Brushes.White;
                PreviewNetworkTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xE9, 0xE9));
                PreviewLoginCodeTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x22, 0x25, 0x2A));
                PreviewUsernameTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0x39, 0x41));
                PreviewPasswordTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0x39, 0x41));
                PreviewValidityTextBlock.Foreground = (Brush)FindResource("PrimaryBrush");
                PreviewTimeBlock.Foreground = (Brush)FindResource("PrimaryBrush");
                PreviewPriceTextBlock.Foreground = (Brush)FindResource("PrimaryBrush");
                PreviewPackageTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0x39, 0x41));
                PreviewDownloadTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0x39, 0x41));
                PreviewContactBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0x39, 0x41));
                break;
        }
    }
    private void ApplyElementVisibility()
    {
        PreviewLoginCodeTextBlock.Visibility = IsTemplateElementVisible("رمز الدخول") ? Visibility.Visible : Visibility.Collapsed;
        PreviewUsernameTextBlock.Visibility = IsTemplateElementVisible("اسم المستخدم") ? Visibility.Visible : Visibility.Collapsed;
        PreviewPasswordTextBlock.Visibility = IsTemplateElementVisible("كلمة المرور") ? Visibility.Visible : Visibility.Collapsed;
        PreviewValidityTextBlock.Visibility = IsTemplateElementVisible("الصلاحية") ? Visibility.Visible : Visibility.Collapsed;
        PreviewTimeBlock.Visibility = IsTemplateElementVisible("الوقت") ? Visibility.Visible : Visibility.Collapsed;
        PreviewPriceTextBlock.Visibility = IsTemplateElementVisible("السعر") ? Visibility.Visible : Visibility.Collapsed;
        PreviewPackageTextBlock.Visibility = IsTemplateElementVisible("الباقة") ? Visibility.Visible : Visibility.Collapsed;
        PreviewDownloadTextBlock.Visibility = IsTemplateElementVisible("التحميل") ? Visibility.Visible : Visibility.Collapsed;
        PreviewContactBlock.Visibility = IsTemplateElementVisible("أرقام التواصل") ? Visibility.Visible : Visibility.Collapsed;
        PreviewSerialTextBlock.Visibility = IsTemplateElementVisible("الرقم التسلسلي") ? Visibility.Visible : Visibility.Collapsed;
        PreviewPrintDateTextBlock.Visibility = IsTemplateElementVisible("تاريخ الطباعة") ? Visibility.Visible : Visibility.Collapsed;
        PreviewBatchTextBlock.Visibility = IsTemplateElementVisible("رقم الدفعة") ? Visibility.Visible : Visibility.Collapsed;
        PreviewPointOfSaleTextBlock.Visibility = IsTemplateElementVisible("نقطة البيع") ? Visibility.Visible : Visibility.Collapsed;
        PreviewExtraTextBlock.Visibility = IsTemplateElementVisible("معلومات إضافية") ? Visibility.Visible : Visibility.Collapsed;
        PreviewRewardTextBlock.Visibility = IsTemplateElementVisible("كود المكافأة") ? Visibility.Visible : Visibility.Collapsed;
        PreviewQrBorder.Visibility = IsTemplateElementVisible("رمز QR") ? Visibility.Visible : Visibility.Collapsed;
        PreviewBarcodeBorder.Visibility = IsTemplateElementVisible("الباركود") ? Visibility.Visible : Visibility.Collapsed;
        var logoVisible = IsTemplateElementVisible("شعار الشبكة");
        PreviewLogoBadgeEllipse.Visibility = logoVisible ? Visibility.Visible : Visibility.Collapsed;
        PreviewLogoMarkTextBlock.Visibility = logoVisible ? Visibility.Visible : Visibility.Collapsed;
        PreviewBrandTextBlock.Visibility = logoVisible ? Visibility.Visible : Visibility.Collapsed;
        PreviewNetworkTextBlock.Visibility = logoVisible ? Visibility.Visible : Visibility.Collapsed;
        CardHeaderBorder.Visibility = IsTemplateElementVisible("الشريط العلوي") ? Visibility.Visible : Visibility.Collapsed;
        CardDividerBorder.Visibility = IsTemplateElementVisible("الفاصل") ? Visibility.Visible : Visibility.Collapsed;
    }
    private void UpdateSamplePackageFields()
    {
        if (!_isReady || PreviewPackageTextBlock is null) return;

        var package = GetActivePackage();
        if (package is null)
        {
            PreviewPackageTextBlock.Text = "الباقة: لا توجد باقة فعالة";
            PreviewPriceTextBlock.Text = "السعر: —";
            PreviewValidityTextBlock.Text = "الصلاحية: —";
            PreviewTimeBlock.Text = "الوقت: —";
            PreviewDownloadTextBlock.Text = "التحميل: —";
            return;
        }

        PreviewPackageTextBlock.Text = "الباقة: " + GetPackageName(package);
        PreviewPriceTextBlock.Text = "السعر: " + package.PrintPriceLabel;
        PreviewValidityTextBlock.Text = "الصلاحية: " + package.Validity;
        PreviewTimeBlock.Text = "الوقت: " + package.TimeLimit;
        PreviewDownloadTextBlock.Text = "التحميل: " + package.DataLimit;
    }
    private void SyncMainElementChecks()
    {
        ShowLoginCodeCheckBox.IsChecked = IsTemplateElementVisible("رمز الدخول");
        ShowUsernameCheckBox.IsChecked = IsTemplateElementVisible("اسم المستخدم");
        ShowPasswordCheckBox.IsChecked = IsTemplateElementVisible("كلمة المرور");
        ShowValidityCheckBox.IsChecked = IsTemplateElementVisible("الصلاحية");
        ShowPriceCheckBox.IsChecked = IsTemplateElementVisible("السعر");
        ShowPackageCheckBox.IsChecked = IsTemplateElementVisible("الباقة");
        ShowTimeCheckBox.IsChecked = IsTemplateElementVisible("الوقت");
        ShowDownloadCheckBox.IsChecked = IsTemplateElementVisible("التحميل");
        ShowSerialCheckBox.IsChecked = IsTemplateElementVisible("الرقم التسلسلي");
        ShowPrintDateCheckBox.IsChecked = IsTemplateElementVisible("تاريخ الطباعة");
        ShowBatchCheckBox.IsChecked = IsTemplateElementVisible("رقم الدفعة");
        ShowPointOfSaleCheckBox.IsChecked = IsTemplateElementVisible("نقطة البيع");
        ShowExtraCheckBox.IsChecked = IsTemplateElementVisible("معلومات إضافية");
        ShowContactOption.IsChecked = IsTemplateElementVisible("أرقام التواصل");
        ShowRewardCheckBox.IsChecked = IsTemplateElementVisible("كود المكافأة");
        ShowQrCheckBox.IsChecked = IsTemplateElementVisible("رمز QR");
        ShowBarcodeCheckBox.IsChecked = IsTemplateElementVisible("الباركود");
        ShowNetworkLogoCheckBox.IsChecked = IsTemplateElementVisible("شعار الشبكة");
        ShowHeaderCheckBox.IsChecked = IsTemplateElementVisible("الشريط العلوي");
        ShowDividerCheckBox.IsChecked = IsTemplateElementVisible("الفاصل");
    }
    private FrameworkElement? GetSelectedElement() => GetElementByTag(GetSelectedTag(SelectedElementComboBox) ?? "login");
    private FrameworkElement? GetElementByTag(string tag) => tag switch
    {
        "login" => PreviewLoginCodeTextBlock,
        "username" => PreviewUsernameTextBlock,
        "password" => PreviewPasswordTextBlock,
        "validity" => PreviewValidityTextBlock,
        "time" => PreviewTimeBlock,
        "price" => PreviewPriceTextBlock,
        "package" => PreviewPackageTextBlock,
        "download" => PreviewDownloadTextBlock,
        "contact" => PreviewContactBlock,
        "serial" => PreviewSerialTextBlock,
        "printDate" => PreviewPrintDateTextBlock,
        "batch" => PreviewBatchTextBlock,
        "pointOfSale" => PreviewPointOfSaleTextBlock,
        "extra" => PreviewExtraTextBlock,
        "reward" => PreviewRewardTextBlock,
        "qr" => PreviewQrBorder,
        "barcode" => PreviewBarcodeBorder,
        "brand" => PreviewBrandTextBlock,
        "networkName" => PreviewNetworkTextBlock,
        "logoBadge" => PreviewLogoBadgeEllipse,
        "logoMark" => PreviewLogoMarkTextBlock,
        "header" => CardHeaderBorder,
        "divider" => CardDividerBorder,
        _ => null
    };
    private bool IsTemplateElementVisible(string key) => _visibleElements.TryGetValue(key, out var visible) && visible;
    private Dictionary<string, ElementDesignProperties> CaptureElementProperties()
    {
        var result = new Dictionary<string, ElementDesignProperties>(StringComparer.Ordinal);
        foreach (var tag in EditableElementTags)
        {
            var element = GetElementByTag(tag);
            if (element is null) continue;
            var x = Canvas.GetLeft(element);
            var y = Canvas.GetTop(element);
            result[tag] = new ElementDesignProperties
            {
                X = double.IsFinite(x) ? x : 0,
                Y = double.IsFinite(y) ? y : 0,
                Width = GetElementWidth(element),
                Height = GetElementHeight(element),
                RotationDegrees = GetElementRotation(element),
                Text = (element as TextBlock)?.Text,
                IsBold = element is TextBlock textBlock && (textBlock.FontWeight == FontWeights.Bold || textBlock.FontWeight == FontWeights.SemiBold),
                FontSize = element is TextBlock tb ? tb.FontSize : 11,
                FontFamily = element is TextBlock tbf ? tbf.FontFamily.Source : "Segoe UI"
            };
        }
        return result;
    }
    private void ApplyElementProperties(Dictionary<string, ElementDesignProperties> properties)
    {
        foreach (var pair in properties)
        {
            var element = GetElementByTag(pair.Key);
            var design = pair.Value;
            if (element is null || design is null) continue;
            var maxCanvasWidth = DesignBaseWidth * 200d / BaseWidthMm;
            var maxCanvasHeight = DesignBaseHeight * 200d / BaseHeightMm;
            Canvas.SetLeft(element, Math.Clamp(design.X, 0, maxCanvasWidth));
            Canvas.SetTop(element, Math.Clamp(design.Y, 0, maxCanvasHeight));
            if (double.IsFinite(design.Width) && design.Width >= 5) element.Width = Math.Clamp(design.Width, 5, maxCanvasWidth);
            if (double.IsFinite(design.Height) && design.Height >= 1) element.Height = Math.Clamp(design.Height, 1, maxCanvasHeight);
            element.RenderTransformOrigin = new Point(0.5, 0.5);
            element.RenderTransform = new RotateTransform(Math.Clamp(design.RotationDegrees, -180, 180));
            if (element is TextBlock textBlock)
            {
                if (design.Text is not null) textBlock.Text = design.Text;
                textBlock.FontWeight = design.IsBold ? FontWeights.Bold : FontWeights.Normal;
                if (design.FontSize is >= 4 and <= 60) textBlock.FontSize = design.FontSize;
                if (!string.IsNullOrWhiteSpace(design.FontFamily)) textBlock.FontFamily = new FontFamily(design.FontFamily);
            }
        }
    }
}
