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
        ["رمز الدخول"] = false,
        ["اسم المستخدم"] = true,
        ["كلمة المرور"] = true,
        ["الصلاحية"] = false,
        ["الوقت"] = false,
        ["السعر"] = false,
        ["التحميل"] = false,
        ["الرقم التسلسلي"] = false,
        ["تاريخ الطباعة"] = false,
        ["رقم الدفعة"] = false,
        ["نقطة البيع"] = false,
        ["معلومات إضافية"] = false,
        ["أرقام التواصل"] = false,
        ["كود المكافأة"] = false,
        ["رمز QR"] = false,
        ["الباركود"] = false,
        ["شعار الشبكة"] = false,
        ["الباقة"] = false,
        ["الشريط العلوي"] = false,
        ["الفاصل"] = false
    };
    private static Dictionary<string, ElementDesignProperties> CreateDefaultElementProperties()
    {
        // Keep authored positions/sizes proportional to the old canvas, but convert all
        // geometry to physical WPF DIPs. FontSize intentionally is NOT scaled.
        var properties = new Dictionary<string, ElementDesignProperties>(StringComparer.Ordinal)
        {
            ["login"] = new() { X = 285, Y = 52, Width = 145, Height = 40, Text = "50723015", IsBold = true, FontSize = 11, FontFamily = "Segoe UI", TextAlignment = "Right" },
            ["username"] = new() { X = 285, Y = 52, Width = 145, Height = 40, Text = "50723015", IsBold = true, FontSize = 11.5, FontFamily = "Segoe UI", TextAlignment = "Right" },
            ["password"] = new() { X = 285, Y = 122, Width = 145, Height = 40, Text = "1432", IsBold = true, FontSize = 10.5, FontFamily = "Segoe UI", TextAlignment = "Right" },
            ["validity"] = new() { X = 145, Y = 89, Width = 68, Height = 16, Text = "الصلاحية: 7 أيام", IsBold = true, FontSize = 6.5, FontFamily = "Segoe UI" },
            ["time"] = new() { X = 215, Y = 89, Width = 76, Height = 16, Text = "الوقت: 1 س", FontSize = 6.5, FontFamily = "Segoe UI" },
            ["price"] = new() { X = 225, Y = 151, Width = 76, Height = 15, Text = "السعر: 100 ريال", IsBold = true, FontSize = 5.8, FontFamily = "Segoe UI" },
            ["package"] = new() { X = 228, Y = 119, Width = 70, Height = 15, Text = "الباقة: —", FontSize = 6, FontFamily = "Segoe UI" },
            ["download"] = new() { X = 145, Y = 119, Width = 80, Height = 15, Text = "التحميل: —", FontSize = 6, FontFamily = "Segoe UI" },
            ["contact"] = new() { X = 145, Y = 151, Width = 152, Height = 15, Text = "", FontSize = 5.8, FontFamily = "Segoe UI" },
            ["serial"] = new() { X = 98, Y = 95, Width = 145, Height = 12, Text = "الرقم التسلسلي: 0001", FontSize = 9, FontFamily = "Segoe UI" },
            ["printDate"] = new() { X = 98, Y = 95, Width = 145, Height = 12, Text = "تاريخ الطباعة", FontSize = 9, FontFamily = "Segoe UI" },
            ["batch"] = new() { X = 277, Y = 95, Width = 174, Height = 12, Text = "رقم الدفعة: B-0001", FontSize = 9, FontFamily = "Segoe UI" },
            ["pointOfSale"] = new() { X = 98, Y = 95, Width = 145, Height = 12, Text = "نقطة البيع", FontSize = 9, FontFamily = "Segoe UI" },
            ["extra"] = new() { X = 98, Y = 95, Width = 350, Height = 13, Text = "", FontSize = 9, FontFamily = "Segoe UI" },
            ["reward"] = new() { X = 277, Y = 95, Width = 174, Height = 12, Text = "كود المكافأة", FontSize = 9, FontFamily = "Segoe UI" },
            ["qr"] = new() { X = 373, Y = 3, Width = 32, Height = 32 },
            ["barcode"] = new() { X = 98, Y = 94, Width = 145, Height = 13 },
            ["brand"] = new() { X = 350, Y = 104, Width = 54, Height = 15, Text = "TAB CARD", IsBold = true, FontSize = 6.5, FontFamily = "Segoe UI" },
            ["networkName"] = new() { X = 350, Y = 136, Width = 54, Height = 26, Text = "اسم الشبكة", FontSize = 5.5, FontFamily = "Segoe UI" },
            ["logoBadge"] = new() { X = 365, Y = 12, Width = 38, Height = 38 },
            ["logoMark"] = new() { X = 371, Y = 20, Width = 26, Height = 20, Text = "TC", IsBold = true, FontSize = 8.5, FontFamily = "Segoe UI" },
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
            ("username", "اسم المستخدم"), ("password", "كلمة المرور"),
            ("validity", "الصلاحية"), ("time", "الوقت"), ("price", "السعر"),
            ("package", "الباقة"), ("download", "التحميل"), ("contact", "أرقام التواصل"),
            ("serial", "الرقم التسلسلي"), ("printDate", "تاريخ الطباعة"), ("batch", "رقم الدفعة"),
            ("pointOfSale", "نقطة البيع"), ("extra", "معلومات إضافية"), ("reward", "كود المكافأة"),
            ("qr", "رمز QR"), ("barcode", "الباركود"), ("brand", "اسم العلامة"),
            ("networkName", "اسم الشبكة"), ("logoBadge", "خلفية الشعار"), ("logoMark", "رمز الشعار"),
            ("header", "الشريط العلوي"), ("divider", "الفاصل")
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
        SelectComboTag(ColorThemeComboBox, style switch
        {
            "dark" => "dark",
            "light" => "white",
            _ => "blue"
        });
        UpdatePreview();
    }
    private void ColorThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isReady) return;
        ApplyTheme();
        RefreshStudioPreviews();
    }
    private void FontFamilyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isReady) return;
        var font = GetSelectedTag(FontFamilyComboBox) ?? "Segoe UI";
        if (GetSelectedElement() is not TextBlock selectedText) return;
        selectedText.FontFamily = new FontFamily(font);
        if (ReferenceEquals(selectedText, PreviewUsernameTextBlock)) PreviewUsernameCaptionTextBlock.FontFamily = selectedText.FontFamily;
        if (ReferenceEquals(selectedText, PreviewPasswordTextBlock)) PreviewPasswordCaptionTextBlock.FontFamily = selectedText.FontFamily;
        UpdateSelectionFrame();
        RefreshStudioPreviews();
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
        _studioSelectedFieldTag = GetSelectedTag(SelectedElementComboBox);
        SyncFieldToggleButtons();
        SyncSelectedElementEditor();
    }
    private void SyncSelectedElementEditor()
    {
        if (SelectedElementComboBox is not null)
        {
            SelectedElementComboBox.Visibility = Visibility.Collapsed;
            SelectedElementComboBox.Width = 0;
            SelectedElementComboBox.Height = 0;
        }
        if (SelectedFieldNameTextBlock is not null) SelectedFieldNameTextBlock.Visibility = Visibility.Visible;
        var element = GetSelectedElement();
        if (element is null)
        {
            if (SelectedFieldNameTextBlock is not null) SelectedFieldNameTextBlock.Text = "لم يتم تحديد حقل";
            if (ToggleSelectedFieldVisibilityButton is not null) ToggleSelectedFieldVisibilityButton.Content = "اختر حقلًا";
            UpdateSelectionFrame();
            return;
        }
        if (SelectedFieldNameTextBlock is not null)
        {
            SelectedFieldNameTextBlock.Text = GetFieldDisplayName(_studioSelectedFieldTag ?? (SelectedElementComboBox is { } selectedCombo ? GetSelectedTag(selectedCombo) : null));
            SelectedFieldNameTextBlock.Visibility = Visibility.Visible;
        }

        var left = Canvas.GetLeft(element);
        var top = Canvas.GetTop(element);
        if (!double.IsFinite(left)) left = 0;
        if (!double.IsFinite(top)) top = 0;
        ElementXTextBox.Text = left.ToString("0.#", CultureInfo.InvariantCulture);
        ElementYTextBox.Text = top.ToString("0.#", CultureInfo.InvariantCulture);
        ElementWidthTextBox.Text = GetElementWidth(element).ToString("0.#", CultureInfo.InvariantCulture);
        ElementHeightTextBox.Text = GetElementHeight(element).ToString("0.#", CultureInfo.InvariantCulture);
        ElementAngleTextBox.Text = GetElementRotation(element).ToString("0.#", CultureInfo.InvariantCulture);

        SetDesignerTextBoxText("ElementForegroundHexTextBox", GetElementForegroundHex(element));
        SetDesignerTextBoxText("ElementBackgroundHexTextBox", GetElementBackgroundHex(element));
        UpdateElementColorSwatches(GetElementForegroundHex(element), GetElementBackgroundHex(element));
        if (element is TextBlock textBlock)
        {
            if (ReferenceEquals(element, PreviewUsernameTextBlock))
            {
                textBlock.Text = NormalizeUsernameText(textBlock.Text);
                textBlock.FlowDirection = FlowDirection.LeftToRight;
            }
            else if (ReferenceEquals(element, PreviewPasswordTextBlock))
            {
                textBlock.Text = NormalizePasswordText(textBlock.Text);
                textBlock.FlowDirection = FlowDirection.LeftToRight;
            }
            SelectedElementTextBox.FlowDirection = ReferenceEquals(textBlock, PreviewUsernameTextBlock) || ReferenceEquals(textBlock, PreviewPasswordTextBlock)
                ? FlowDirection.LeftToRight
                : FlowDirection.RightToLeft;
            SelectedElementTextBox.Text = textBlock.Text;
            ElementFontSizeTextBox.Text = textBlock.FontSize.ToString("0.#", CultureInfo.InvariantCulture);
            BoldElementCheckBox.IsChecked = textBlock.FontWeight == FontWeights.Bold || textBlock.FontWeight == FontWeights.SemiBold;
            SelectedElementTextBox.IsEnabled = true;
            ElementFontSizeTextBox.IsEnabled = true;
            BoldElementCheckBox.IsEnabled = true;
            ElementTextAlignLeftButton.IsEnabled = true;
            ElementTextAlignCenterButton.IsEnabled = true;
            ElementTextAlignRightButton.IsEnabled = true;
            SyncTextAlignmentButtons(textBlock.TextAlignment);
            SelectComboTag(FontFamilyComboBox, textBlock.FontFamily.Source);
        }
        else
        {
            SelectedElementTextBox.Text = string.Empty;
            SelectedElementTextBox.IsEnabled = false;
            ElementFontSizeTextBox.Text = "11";
            ElementFontSizeTextBox.IsEnabled = false;
            BoldElementCheckBox.IsChecked = false;
            BoldElementCheckBox.IsEnabled = false;
            ElementTextAlignLeftButton.IsChecked = false;
            ElementTextAlignCenterButton.IsChecked = false;
            ElementTextAlignRightButton.IsChecked = false;
            ElementTextAlignLeftButton.IsEnabled = false;
            ElementTextAlignCenterButton.IsEnabled = false;
            ElementTextAlignRightButton.IsEnabled = false;
        }
        UpdateSelectionFrame();
    }
    private void ElementVisibility_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isReady || sender is not CheckBox checkBox || checkBox.Tag is not string key) return;

        // Legacy "login code" and username are the same account identifier.
        // Never allow the old checkbox path to create a duplicate printed value.
        if (string.Equals(key, "رمز الدخول", StringComparison.Ordinal))
        {
            _visibleElements["رمز الدخول"] = false;
            if (checkBox.IsChecked == true)
            {
                _visibleElements["اسم المستخدم"] = true;
                // The legacy checkbox is hidden, but normalize it if old UI state sets it.
                if (checkBox.IsChecked == true) checkBox.IsChecked = false;
            }
        }
        else
        {
            _visibleElements[key] = checkBox.IsChecked == true;
        }
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
    private void ElementTextAlignLeftButton_Click(object sender, RoutedEventArgs e) => SetSelectedTextAlignment(TextAlignment.Left);
    private void ElementTextAlignCenterButton_Click(object sender, RoutedEventArgs e) => SetSelectedTextAlignment(TextAlignment.Center);
    private void ElementTextAlignRightButton_Click(object sender, RoutedEventArgs e) => SetSelectedTextAlignment(TextAlignment.Right);

    private void SetSelectedTextAlignment(TextAlignment alignment)
    {
        if (!_isReady) return;
        if (GetSelectedElement() is not TextBlock textBlock)
        {
            SetStatus("اختر عنصرًا نصيًا لتغيير المحاذاة.", isSuccess: false);
            return;
        }
        textBlock.TextAlignment = alignment;
        SyncTextAlignmentButtons(alignment);
        UpdateSelectionFrame();
        RefreshStudioPreviews();
        var label = alignment switch
        {
            TextAlignment.Left => "اليسار",
            TextAlignment.Center => "الوسط",
            _ => "اليمين"
        };
        SetStatus($"تمت محاذاة النص إلى {label}. احفظ القالب للاحتفاظ بالتغيير.", isSuccess: true);
    }

    private void SyncTextAlignmentButtons(TextAlignment alignment)
    {
        ElementTextAlignLeftButton.IsChecked = alignment == TextAlignment.Left;
        ElementTextAlignCenterButton.IsChecked = alignment == TextAlignment.Center;
        ElementTextAlignRightButton.IsChecked = alignment == TextAlignment.Right;
    }

    private void ApplyPropertiesButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isReady) return;
        var font = GetSelectedTag(FontFamilyComboBox) ?? "Segoe UI";
        var family = new FontFamily(font);
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
                textBlock.Text = ReferenceEquals(textBlock, PreviewUsernameTextBlock)
                    ? NormalizeUsernameText(SelectedElementTextBox.Text)
                    : ReferenceEquals(textBlock, PreviewPasswordTextBlock)
                        ? NormalizePasswordText(SelectedElementTextBox.Text)
                        : SelectedElementTextBox.Text;
                if (ReferenceEquals(textBlock, PreviewUsernameTextBlock) || ReferenceEquals(textBlock, PreviewPasswordTextBlock))
                    textBlock.FlowDirection = FlowDirection.LeftToRight;
                textBlock.FontFamily = family;
                textBlock.FontWeight = BoldElementCheckBox.IsChecked == true ? FontWeights.Bold : FontWeights.Normal;
                textBlock.FontSize = ParseDouble(ElementFontSizeTextBox.Text, textBlock.FontSize, 4, 60);
                if (TryParseBrush(GetDesignerTextBoxText("ElementForegroundHexTextBox", GetElementForegroundHex(textBlock)), out var foreground)) textBlock.Foreground = foreground;
                if (TryParseBrush(GetDesignerTextBoxText("ElementBackgroundHexTextBox", GetElementBackgroundHex(textBlock)), out var background)) textBlock.Background = background;
            }
            else if (selected is Shape shape && TryParseBrush(GetDesignerTextBoxText("ElementBackgroundHexTextBox", GetElementBackgroundHex(shape)), out var shapeFill))
            {
                shape.Fill = shapeFill;
            }
            else if (selected is Border border && TryParseBrush(GetDesignerTextBoxText("ElementBackgroundHexTextBox", GetElementBackgroundHex(border)), out var borderFill))
            {
                border.Background = borderFill;
            }
            ElementXTextBox.Text = clampedPosition.X.ToString("0.#", CultureInfo.InvariantCulture);
            ElementYTextBox.Text = clampedPosition.Y.ToString("0.#", CultureInfo.InvariantCulture);
            ElementWidthTextBox.Text = width.ToString("0.#", CultureInfo.InvariantCulture);
            ElementHeightTextBox.Text = height.ToString("0.#", CultureInfo.InvariantCulture);
            ElementAngleTextBox.Text = angle.ToString("0.#", CultureInfo.InvariantCulture);
        }

        var cardWidth = ParseDouble(CardWidthTextBox.Text, 92.1, 10, 200);
        var cardHeight = ParseDouble(CardHeightTextBox.Text, 52, 10, 200);
        var cardAngle = ParseDouble(CardRotationTextBox.Text, 0, -180, 180);
        CardWidthTextBox.Text = cardWidth.ToString("0.#", CultureInfo.InvariantCulture);
        CardHeightTextBox.Text = cardHeight.ToString("0.#", CultureInfo.InvariantCulture);
        CardRotationTextBox.Text = cardAngle.ToString("0.#", CultureInfo.InvariantCulture);
        ApplyCardDimensionTransform();
        if (_isAutoFitZoom) FitCardToWorkspace();
        ApplyTheme();
        ApplyElementVisibility();
        UpdateSelectionFrame();
        ElementCountTextBlock.Text = $"{_visibleElements.Count(pair => pair.Value && pair.Key != "شعار الشبكة")} عناصر ظاهرة";
        SyncFieldToggleButtons();
        SyncSelectedElementEditor();
        RefreshStudioPreviews();
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
        if (_isRasterTemplateMode) ApplyRasterTemplateModeVisibility();
        ApplyCardDimensionTransform();
        var count = _visibleElements.Count(pair => pair.Value && pair.Key != "شعار الشبكة" && pair.Key != "رمز الدخول");
        ElementCountTextBlock.Text = $"{count} عناصر ظاهرة";
        KeepUsernameCaptionAligned();
        KeepPasswordCaptionAligned();
        EnsureElementBorderOverlays();
        UpdateElementBorderOverlays();
        UpdateSelectionFrame();
        RefreshDesignerLayers();
        SyncFieldToggleButtons();
        RefreshStudioPreviews();
    }
    private void ApplyTheme()
    {
        if (CardSurfaceBorder is null) return;
        var theme = GetSelectedTag(ColorThemeComboBox) ?? "red";
        switch (theme)
        {
            case "blue":
                CardSurfaceBorder.Background = Brushes.White;
                CardSurfaceBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x0B, 0x6E, 0xA8));
                CardHeaderBorder.Background = new SolidColorBrush(Color.FromRgb(0x08, 0x4D, 0x8C));
                PreviewBrandTextBlock.Foreground = Brushes.White;
                PreviewNetworkTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xD8, 0xF5, 0xFF));
                PreviewLoginCodeTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x07, 0x3D, 0x82));
                PreviewUsernameTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x13, 0x3B, 0x5A));
                PreviewPasswordTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x13, 0x3B, 0x5A));
                PreviewValidityTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x8F, 0xC8));
                PreviewTimeBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x8F, 0xC8));
                PreviewPriceTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x8F, 0xC8));
                PreviewPackageTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x13, 0x3B, 0x5A));
                PreviewDownloadTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x13, 0x3B, 0x5A));
                PreviewContactBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x13, 0x3B, 0x5A));
                break;
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
        // Product rule: card number and username refer to one value.
        // Normalize legacy templates before applying visibility to the actual visuals.
        if (IsTemplateElementVisible("رمز الدخول")) _visibleElements["اسم المستخدم"] = true;
        _visibleElements["رمز الدخول"] = false;
        PreviewLoginCodeTextBlock.Visibility = Visibility.Collapsed;
        PreviewUsernameTextBlock.Visibility = IsTemplateElementVisible("اسم المستخدم") ? Visibility.Visible : Visibility.Collapsed;
        if (PreviewUsernameCaptionTextBlock is not null)
            PreviewUsernameCaptionTextBlock.Visibility = PreviewUsernameTextBlock.Visibility;
        PreviewPasswordTextBlock.Visibility = IsTemplateElementVisible("كلمة المرور") ? Visibility.Visible : Visibility.Collapsed;
        PreviewPasswordCaptionTextBlock.Visibility = PreviewPasswordTextBlock.Visibility;
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
    private static string VisibilityKeyForLayerTag(string tag) => tag switch
    {
        "login" => "رمز الدخول",
        "username" => "اسم المستخدم",
        "password" => "كلمة المرور",
        "validity" => "الصلاحية",
        "time" => "الوقت",
        "price" => "السعر",
        "package" => "الباقة",
        "download" => "التحميل",
        "contact" => "أرقام التواصل",
        "serial" => "الرقم التسلسلي",
        "printDate" => "تاريخ الطباعة",
        "batch" => "رقم الدفعة",
        "pointOfSale" => "نقطة البيع",
        "extra" => "معلومات إضافية",
        "reward" => "كود المكافأة",
        "qr" => "رمز QR",
        "barcode" => "الباركود",
        "brand" or "networkName" or "logoBadge" or "logoMark" => "شعار الشبكة",
        "header" => "الشريط العلوي",
        "divider" => "الفاصل",
        _ => string.Empty
    };

    private bool IsLayerVisible(string tag)
    {
        if (tag.StartsWith("custom-", StringComparison.Ordinal))
            return GetElementByTag(tag)?.Visibility == Visibility.Visible;
        var key = VisibilityKeyForLayerTag(tag);
        return !string.IsNullOrEmpty(key) && IsTemplateElementVisible(key);
    }

    private static string GetLayerGlyph(string tag) => tag switch
    {
        "price" => "▤",
        "time" => "◷",
        "qr" => "▦",
        "barcode" => "▥",
        "contact" => "☎",
        "brand" or "networkName" or "logoBadge" or "logoMark" => "◈",
        "header" => "▬",
        "divider" => "—",
        _ => "T"
    };

    private void RefreshDesignerLayers()
    {
        if (DesignerLayersPanel is null || !_isReady) return;
        DesignerLayersPanel.Children.Clear();

        // Show all editable layers, even hidden ones, so the user can restore them.
        var tags = GetAllEditableElementTags()
            .Where(tag => !string.Equals(tag, "login", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (tags.Count == 0)
        {
            DesignerLayersPanel.Children.Add(new TextBlock
            {
                Text = "لا توجد عناصر مضافة بعد.",
                FontSize = 10.5,
                Foreground = (Brush)FindResource("MutedBrush"),
                Margin = new Thickness(4, 8, 4, 8),
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        var selectedTag = _studioSelectedFieldTag ?? GetSelectedTag(SelectedElementComboBox);
        foreach (var tag in tags)
        {
            var isVisible = IsLayerVisible(tag);
            var selected = string.Equals(selectedTag, tag, StringComparison.Ordinal);
            var row = new Border
            {
                Background = selected ? new SolidColorBrush(Color.FromRgb(0xFC, 0xEA, 0xED)) : Brushes.White,
                BorderBrush = selected ? new SolidColorBrush(Color.FromRgb(0xE7, 0xA8, 0xB0)) : new SolidColorBrush(Color.FromRgb(0xE4, 0xE9, 0xF0)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(3),
                Margin = new Thickness(0, 0, 0, 4)
            };
            var line = new Grid { FlowDirection = FlowDirection.RightToLeft, Height = 31 };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(29) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(25) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var eye = new Button
            {
                Content = isVisible ? "◉" : "○",
                Tag = tag,
                FontSize = 15,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = isVisible ? new SolidColorBrush(Color.FromRgb(0xC5, 0x1D, 0x2E)) : new SolidColorBrush(Color.FromRgb(0x94, 0x9D, 0xAA)),
                ToolTip = isVisible ? "إخفاء العنصر" : "إظهار العنصر",
                VerticalAlignment = VerticalAlignment.Stretch
            };
            eye.Click += LayerVisibilityButton_Click;
            Grid.SetColumn(eye, 0);
            line.Children.Add(eye);

            var glyph = new TextBlock
            {
                Text = GetLayerGlyph(tag),
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = selected ? new SolidColorBrush(Color.FromRgb(0xA9, 0x15, 0x25)) : new SolidColorBrush(Color.FromRgb(0x42, 0x54, 0x6B)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(glyph, 1);
            line.Children.Add(glyph);

            var select = new Button
            {
                Content = GetFieldDisplayName(tag),
                Tag = tag,
                HorizontalContentAlignment = HorizontalAlignment.Right,
                VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(4, 2, 7, 2),
                Margin = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = selected ? new SolidColorBrush(Color.FromRgb(0xA9, 0x15, 0x25)) : (Brush)FindResource("TextBrush"),
                FontSize = 10.5,
                FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal,
                FlowDirection = FlowDirection.RightToLeft,
                ToolTip = "تحديد العنصر وتعديل خصائصه"
            };
            select.Click += LayerSelectButton_Click;
            Grid.SetColumn(select, 2);
            line.Children.Add(select);
            row.Child = line;
            DesignerLayersPanel.Children.Add(row);
        }
    }

    private void LayerSelectButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isReady || sender is not Button button || button.Tag is not string tag) return;
        if (string.Equals(tag, "login", StringComparison.Ordinal)) tag = "username";

        if (!SelectedElementComboBox.Items.OfType<ComboBoxItem>().Any(item => string.Equals(item.Tag as string, tag, StringComparison.Ordinal)))
            SelectedElementComboBox.Items.Add(new ComboBoxItem { Content = GetFieldDisplayName(tag), Tag = tag });
        SelectComboTag(SelectedElementComboBox, tag);
        _studioSelectedFieldTag = tag;
        SyncFieldToggleButtons();
        SyncSelectedElementEditor();
        UpdateSelectionFrame();
        RefreshDesignerLayers();
    }

    private void LayerVisibilityButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isReady || sender is not Button button || button.Tag is not string tag) return;
        if (tag.StartsWith("custom-", StringComparison.Ordinal))
        {
            if (GetElementByTag(tag) is FrameworkElement custom)
                custom.Visibility = custom.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        }
        else
        {
            var key = VisibilityKeyForLayerTag(tag);
            if (string.IsNullOrEmpty(key)) return;
            if (key == "رمز الدخول")
            {
                _visibleElements["رمز الدخول"] = false;
                _visibleElements["اسم المستخدم"] = true;
            }
            else
            {
                _visibleElements[key] = !IsTemplateElementVisible(key);
            }
        }
        SyncMainElementChecks();
        UpdatePreview();
        SyncSelectedElementEditor();
        SetStatus(IsLayerVisible(tag) ? $"تم إظهار العنصر: {GetFieldDisplayName(tag)}." : $"تم إخفاء العنصر: {GetFieldDisplayName(tag)}.", isSuccess: true);
    }

    private void KeepUsernameCaptionAligned()
    {
        if (PreviewUsernameCaptionTextBlock is null || PreviewUsernameTextBlock is null) return;
        var left = Canvas.GetLeft(PreviewUsernameTextBlock);
        var top = Canvas.GetTop(PreviewUsernameTextBlock);
        if (!double.IsFinite(left)) left = 0;
        if (!double.IsFinite(top)) top = 0;
        Canvas.SetLeft(PreviewUsernameCaptionTextBlock, left);
        Canvas.SetTop(PreviewUsernameCaptionTextBlock, Math.Max(0, top - Math.Max(7, PreviewUsernameCaptionTextBlock.Height)));
        PreviewUsernameCaptionTextBlock.Width = GetElementWidth(PreviewUsernameTextBlock);
        PreviewUsernameCaptionTextBlock.Visibility = PreviewUsernameTextBlock.Visibility;
    }

    private void KeepPasswordCaptionAligned()
    {
        if (PreviewPasswordCaptionTextBlock is null || PreviewPasswordTextBlock is null) return;
        var left = Canvas.GetLeft(PreviewPasswordTextBlock);
        var top = Canvas.GetTop(PreviewPasswordTextBlock);
        if (!double.IsFinite(left)) left = 0;
        if (!double.IsFinite(top)) top = 0;
        Canvas.SetLeft(PreviewPasswordCaptionTextBlock, left);
        Canvas.SetTop(PreviewPasswordCaptionTextBlock, Math.Max(0, top - Math.Max(7, PreviewPasswordCaptionTextBlock.Height)));
        PreviewPasswordCaptionTextBlock.Width = GetElementWidth(PreviewPasswordTextBlock);
        PreviewPasswordCaptionTextBlock.Visibility = PreviewPasswordTextBlock.Visibility;
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
        // Hidden compatibility control only; the duplicate field is never enabled.
        ShowLoginCodeCheckBox.IsChecked = false;
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
    private FrameworkElement? GetSelectedElement()
    {
        var tag = _studioSelectedFieldTag ?? GetSelectedTag(SelectedElementComboBox);
        return string.IsNullOrWhiteSpace(tag) ? null : GetElementByTag(tag);
    }

    private static string GetFieldDisplayName(string? tag) => tag switch
    {
        "login" or "username" => "اسم المستخدم",
        "password" => "كلمة المرور",
        "qr" => "رمز QR",
        "barcode" => "الباركود",
        "validity" => "الصلاحية",
        "time" => "الوقت",
        "price" => "السعر",
        "package" => "الباقة",
        "download" => "التحميل",
        "contact" => "أرقام التواصل",
        "serial" => "الرقم التسلسلي",
        "printDate" => "تاريخ الطباعة",
        "batch" => "رقم الدفعة",
        "pointOfSale" => "نقطة البيع",
        "extra" => "معلومات إضافية",
        "reward" => "كود المكافأة",
        "brand" => "اسم العلامة",
        "networkName" => "اسم الشبكة",
        "logoBadge" => "خلفية الشعار",
        "logoMark" => "رمز الشعار",
        "header" => "الشريط العلوي",
        "divider" => "الفاصل",
        _ when tag is not null && tag.StartsWith("custom-", StringComparison.Ordinal) => "حقل مخصص " + tag[7..],
        _ => tag ?? "لم يتم تحديد حقل"
    };

    private void SyncFieldToggleButtons()
    {
        // Chip state represents visibility, not selection. A click selects the field
        // and toggles whether it is rendered on the card.
        if (FieldLoginToggleButton is not null) FieldLoginToggleButton.IsChecked = false;
        if (FieldUsernameToggleButton is not null) FieldUsernameToggleButton.IsChecked = IsTemplateElementVisible("اسم المستخدم");
        if (FieldPasswordToggleButton is not null) FieldPasswordToggleButton.IsChecked = IsTemplateElementVisible("كلمة المرور");
        if (FieldValidityToggleButton is not null) FieldValidityToggleButton.IsChecked = IsTemplateElementVisible("الصلاحية");
        if (FieldTimeToggleButton is not null) FieldTimeToggleButton.IsChecked = IsTemplateElementVisible("الوقت");
        if (FieldPriceToggleButton is not null) FieldPriceToggleButton.IsChecked = IsTemplateElementVisible("السعر");
        if (FieldDownloadToggleButton is not null) FieldDownloadToggleButton.IsChecked = IsTemplateElementVisible("التحميل");
        if (FieldContactToggleButton is not null) FieldContactToggleButton.IsChecked = IsTemplateElementVisible("أرقام التواصل");
        if (FieldPackageToggleButton is not null) FieldPackageToggleButton.IsChecked = IsTemplateElementVisible("الباقة");
        if (FieldNetworkLogoToggleButton is not null) FieldNetworkLogoToggleButton.IsChecked = IsTemplateElementVisible("شعار الشبكة");
        if (ToggleSelectedFieldVisibilityButton is not null && !string.IsNullOrWhiteSpace(_studioSelectedFieldTag))
        {
            var key = VisibilityKeyForLayerTag(_studioSelectedFieldTag);
            ToggleSelectedFieldVisibilityButton.Content = !string.IsNullOrEmpty(key) && IsTemplateElementVisible(key) ? "إخفاء الحقل" : "إظهار الحقل";
        }
    }

    private void StudioFieldToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isReady || sender is not ToggleButton button || button.Tag is not string tag) return;
        var key = VisibilityKeyForLayerTag(tag);
        if (string.IsNullOrWhiteSpace(key)) return;

        // Checked means visible; unchecking removes the field from the live card
        // without deleting its saved position or styling.
        _visibleElements[key] = button.IsChecked == true;
        if (string.Equals(key, "اسم المستخدم", StringComparison.Ordinal))
            _visibleElements["رمز الدخول"] = false;

        _studioSelectedFieldTag = tag;
        if (!SelectedElementComboBox.Items.OfType<ComboBoxItem>().Any(item => string.Equals(item.Tag as string, tag, StringComparison.Ordinal)))
            SelectedElementComboBox.Items.Add(new ComboBoxItem { Content = GetFieldDisplayName(tag), Tag = tag });
        SelectComboTag(SelectedElementComboBox, tag);
        UpdatePreview();
        SyncFieldToggleButtons();
        SyncSelectedElementEditor();
        SetStatus(IsTemplateElementVisible(key) ? $"تم إظهار العنصر: {GetFieldDisplayName(tag)}." : $"تم إخفاء العنصر: {GetFieldDisplayName(tag)}.", isSuccess: true);
    }

    private void ToggleSelectedFieldVisibilityButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isReady) return;
        var tag = _studioSelectedFieldTag ?? GetSelectedTag(SelectedElementComboBox);
        var key = tag switch
        {
            "login" or "username" => "اسم المستخدم",
            "password" => "كلمة المرور",
            _ => string.Empty
        };
        if (string.IsNullOrEmpty(key))
        {
            SetStatus("حدد اسم المستخدم أو كلمة المرور أولًا.", isSuccess: false);
            return;
        }
        _visibleElements[key] = !IsTemplateElementVisible(key);
        SyncMainElementChecks();
        UpdatePreview();
        SyncFieldToggleButtons();
        SyncSelectedElementEditor();
    }

    private void RotateSelectedFieldButton_Click(object sender, RoutedEventArgs e)
    {
        var element = GetSelectedElement();
        if (element is null) return;
        var nextAngle = GetElementRotation(element) + 90d;
        if (nextAngle > 180d) nextAngle -= 360d;
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = new RotateTransform(nextAngle);
        ElementAngleTextBox.Text = nextAngle.ToString("0.#", CultureInfo.InvariantCulture);
        UpdateSelectionFrame();
        SyncSelectedElementEditor();
        RefreshStudioPreviews();
    }
    private FrameworkElement? GetElementByTag(string tag)
    {
        var element = tag switch
        {
            "login" => (FrameworkElement?)PreviewLoginCodeTextBlock,
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
        if (element is not null) return element;
        return _customElements.TryGetValue(tag, out var custom) ? custom : null;
    }
    private static string BrushToHex(Brush? brush)
    {
        if (brush is SolidColorBrush solid) return solid.Color.ToString();
        return "#00000000";
    }
    // Resolve the two color inputs through the control tree instead of generated XAML fields.
    // This avoids a WPF namescope/code-generation mismatch while keeping the editor controls editable.
    private void SetDesignerTextBoxText(string name, string value)
    {
        var input = FindDesignerTextBox(name);
        if (input is not null)
            input.Text = value;
    }

    private string GetDesignerTextBoxText(string name, string fallback)
    {
        var input = FindDesignerTextBox(name);
        return input?.Text ?? fallback;
    }

    private TextBox? FindDesignerTextBox(string name)
    {
        if (FindName(name) is TextBox named)
            return named;

        return FindDesignerTextBoxLogical(this, name)
            ?? FindDesignerTextBoxVisual(this, name);
    }

    private static TextBox? FindDesignerTextBoxLogical(DependencyObject parent, string name)
    {
        if (parent is TextBox textBox && string.Equals(textBox.Name, name, StringComparison.Ordinal))
            return textBox;

        foreach (var child in LogicalTreeHelper.GetChildren(parent))
        {
            if (child is not DependencyObject dependencyChild)
                continue;

            var match = FindDesignerTextBoxLogical(dependencyChild, name);
            if (match is not null)
                return match;
        }

        return null;
    }

    private static TextBox? FindDesignerTextBoxVisual(DependencyObject parent, string name)
    {
        if (parent is TextBox textBox && string.Equals(textBox.Name, name, StringComparison.Ordinal))
            return textBox;

        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < childCount; index++)
        {
            var match = FindDesignerTextBoxVisual(VisualTreeHelper.GetChild(parent, index), name);
            if (match is not null)
                return match;
        }

        return null;
    }
    private static bool TryParseBrush(string? value, out Brush brush)
    {
        brush = Brushes.Transparent;
        if (string.IsNullOrWhiteSpace(value)) return false;
        try
        {
            var parsed = new BrushConverter().ConvertFromString(value.Trim());
            if (parsed is not Brush converted) return false;
            converted.Freeze();
            brush = converted;
            return true;
        }
        catch { return false; }
    }
    private static string NormalizeUsernameText(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        foreach (var prefix in new[] { "اسم المستخدم", "رقم الكرت", "رقم الكارت", "رقم الدخول", "رمز الدخول" })
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                text = text[prefix.Length..].TrimStart(' ', ':', '：', '-', '–', '—');
                break;
            }
        }
        return string.IsNullOrWhiteSpace(text) ? "50723015" : text;
    }

    private static string NormalizePasswordText(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        foreach (var prefix in new[] { "كلمة المرور", "كلمة السر", "Password", "Pass" })
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                text = text[prefix.Length..].TrimStart(' ', ':', '：', '-', '–', '—');
                break;
            }
        }
        return string.IsNullOrWhiteSpace(text) ? "1432" : text;
    }

    private void SelectedElementTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isReady || GetSelectedElement() is not TextBlock selectedText) return;
        var value = ReferenceEquals(selectedText, PreviewUsernameTextBlock)
            ? NormalizeUsernameText(SelectedElementTextBox.Text)
            : ReferenceEquals(selectedText, PreviewPasswordTextBlock)
                ? NormalizePasswordText(SelectedElementTextBox.Text)
                : SelectedElementTextBox.Text;
        if (string.Equals(selectedText.Text, value, StringComparison.Ordinal)) return;
        selectedText.Text = value;
        if (ReferenceEquals(selectedText, PreviewUsernameTextBlock))
        {
            selectedText.FlowDirection = FlowDirection.LeftToRight;
            KeepUsernameCaptionAligned();
        }
        if (ReferenceEquals(selectedText, PreviewPasswordTextBlock))
        {
            selectedText.FlowDirection = FlowDirection.LeftToRight;
            KeepPasswordCaptionAligned();
        }
        UpdateSelectionFrame();
        RefreshStudioPreviews();
    }

    private void SelectedElementTextBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_isReady || GetSelectedElement() is not TextBlock textBlock) return;
        if (ReferenceEquals(textBlock, PreviewUsernameTextBlock)) textBlock.Text = NormalizeUsernameText(textBlock.Text);
        if (ReferenceEquals(textBlock, PreviewPasswordTextBlock)) textBlock.Text = NormalizePasswordText(textBlock.Text);
        SyncSelectedElementEditor();
        RefreshStudioPreviews();
        SetStatus("تم تحديث نص العنصر. احفظ القالب للاحتفاظ بالتغيير.", isSuccess: true);
    }

    private void ElementProperty_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_isReady) return;
        ApplyPropertiesButton_Click(this, new RoutedEventArgs());
    }

    private void ElementProperty_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        ApplyPropertiesButton_Click(this, new RoutedEventArgs());
        Keyboard.ClearFocus();
    }

    private void ElementStyleControl_Click(object sender, RoutedEventArgs e)
    {
        if (_isReady) ApplyPropertiesButton_Click(this, new RoutedEventArgs());
    }

    private void ElementForegroundColorButton_Click(object sender, RoutedEventArgs e)
    {
        ElementForegroundColorPopup.IsOpen = true;
    }

    private void ElementBackgroundColorButton_Click(object sender, RoutedEventArgs e)
    {
        ElementBackgroundColorPopup.IsOpen = true;
    }

    private void ForegroundColorSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string hex) return;
        SetDesignerTextBoxText("ElementForegroundHexTextBox", hex);
        UpdateElementColorSwatches(hex, GetDesignerTextBoxText("ElementBackgroundHexTextBox", "#00000000"));
        ElementForegroundColorPopup.IsOpen = false;
        ApplyPropertiesButton_Click(this, new RoutedEventArgs());
    }

    private void BackgroundColorSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string hex) return;
        SetDesignerTextBoxText("ElementBackgroundHexTextBox", hex);
        UpdateElementColorSwatches(GetDesignerTextBoxText("ElementForegroundHexTextBox", "#22252A"), hex);
        ElementBackgroundColorPopup.IsOpen = false;
        ApplyPropertiesButton_Click(this, new RoutedEventArgs());
    }

    private void UpdateElementColorSwatches(string foregroundHex, string backgroundHex)
    {
        if (ElementForegroundColorSwatch is not null && TryParseBrush(foregroundHex, out var foreground))
            ElementForegroundColorSwatch.Background = foreground;
        if (ElementBackgroundColorSwatch is not null && TryParseBrush(backgroundHex, out var background))
            ElementBackgroundColorSwatch.Background = background;
    }

    private static string GetElementForegroundHex(FrameworkElement element) => element switch
    {
        TextBlock text => BrushToHex(text.Foreground),
        Shape shape => BrushToHex(shape.Fill),
        _ => "#22252A"
    };
    private static string GetElementBackgroundHex(FrameworkElement element) => element switch
    {
        TextBlock text => BrushToHex(text.Background),
        Border border => BrushToHex(border.Background),
        _ => "#00000000"
    };

    private readonly Dictionary<string, Border> _elementBorderOverlays = new(StringComparer.Ordinal);

    private void EnsureElementBorderOverlays()
    {
        if (CardDesignCanvas is null || SelectionFrameBorder is null) return;
        foreach (var tag in GetAllEditableElementTags())
        {
            if (_elementBorderOverlays.ContainsKey(tag)) continue;
            var overlay = new Border
            {
                Tag = "element-border:" + tag,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(2),
                IsHitTestVisible = false,
                Visibility = Visibility.Collapsed,
                SnapsToDevicePixels = true
            };
            _elementBorderOverlays[tag] = overlay;
            var selectionIndex = CardDesignCanvas.Children.IndexOf(SelectionFrameBorder);
            if (selectionIndex >= 0) CardDesignCanvas.Children.Insert(selectionIndex, overlay);
            else CardDesignCanvas.Children.Add(overlay);
        }
    }

    private void UpdateElementBorderOverlays()
    {
        if (CardDesignCanvas is null) return;
        EnsureElementBorderOverlays();
        foreach (var pair in _elementBorderOverlays)
        {
            var element = GetElementByTag(pair.Key);
            var overlay = pair.Value;
            if (element is null) { overlay.Visibility = Visibility.Collapsed; continue; }
            var left = Canvas.GetLeft(element);
            var top = Canvas.GetTop(element);
            if (!double.IsFinite(left)) left = 0;
            if (!double.IsFinite(top)) top = 0;
            overlay.Width = GetElementWidth(element);
            overlay.Height = GetElementHeight(element);
            Canvas.SetLeft(overlay, left);
            Canvas.SetTop(overlay, top);
            overlay.RenderTransformOrigin = new Point(0.5, 0.5);
            overlay.RenderTransform = new RotateTransform(GetElementRotation(element));
            var enabled = overlay.BorderThickness.Left > 0 && overlay.BorderBrush is not SolidColorBrush { Color.A: 0 };
            overlay.Visibility = enabled && element.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void ElementBorderButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetSelectedElement() is null)
        {
            SetStatus("اختر عنصرًا من الشرائح العلوية أولًا.", isSuccess: false);
            return;
        }
        ElementBorderColorPopup.IsOpen = true;
    }

    private void DisableElementBorderButton_Click(object sender, RoutedEventArgs e)
    {
        var tag = _studioSelectedFieldTag ?? GetSelectedTag(SelectedElementComboBox);
        if (string.IsNullOrWhiteSpace(tag) || GetElementByTag(tag) is null) return;
        EnsureElementBorderOverlays();
        var overlay = _elementBorderOverlays[tag];
        overlay.BorderThickness = new Thickness(0);
        overlay.BorderBrush = Brushes.Transparent;
        UpdateElementBorderOverlays();
        ElementBorderColorPopup.IsOpen = false;
        RefreshStudioPreviews();
        SetStatus("أزيل إطار العنصر. احفظ القالب للاحتفاظ بالتغيير.", isSuccess: true);
    }

    private void BorderColorSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string hex) return;
        var tag = _studioSelectedFieldTag ?? GetSelectedTag(SelectedElementComboBox);
        if (string.IsNullOrWhiteSpace(tag) || GetElementByTag(tag) is null) return;
        if (!TryParseBrush(hex, out var brush))
        {
            SetStatus("تعذر قراءة اللون المحدد.", isSuccess: false);
            return;
        }
        EnsureElementBorderOverlays();
        var overlay = _elementBorderOverlays[tag];
        overlay.BorderBrush = brush;
        overlay.BorderThickness = new Thickness(1.2);
        UpdateElementBorderOverlays();
        ElementBorderColorPopup.IsOpen = false;
        RefreshStudioPreviews();
        SetStatus("تم تغيير إطار العنصر. احفظ القالب للاحتفاظ بالتغيير.", isSuccess: true);
    }

    private void ApplySavedElementBorder(string tag, ElementDesignProperties design)
    {
        EnsureElementBorderOverlays();
        if (!_elementBorderOverlays.TryGetValue(tag, out var overlay)) return;
        if (design.HasBorder && TryParseBrush(design.BorderHex, out var brush))
        {
            overlay.BorderBrush = brush;
            var thickness = design.BorderThickness is > 0 and <= 8 ? design.BorderThickness : 1.2;
            overlay.BorderThickness = new Thickness(thickness);
        }
        else
        {
            overlay.BorderBrush = Brushes.Transparent;
            overlay.BorderThickness = new Thickness(0);
        }
    }

    private void AddBlankFieldButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isReady) return;
        var tag = "custom-" + (++_customElementCounter).ToString(CultureInfo.InvariantCulture);
        var field = new TextBlock
        {
            Tag = tag,
            Text = string.Empty,
            Width = 70,
            Height = 16,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 8,
            Foreground = new SolidColorBrush(Color.FromRgb(34, 36, 42)),
            Background = new SolidColorBrush(Color.FromRgb(255, 231, 234)),
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Thickness(2),
            IsHitTestVisible = true
        };
        _customElements[tag] = field;
        Canvas.SetLeft(field, Math.Max(0, CardDesignCanvas.Width * 0.42));
        Canvas.SetTop(field, Math.Max(0, CardDesignCanvas.Height * 0.48));
        CardDesignCanvas.Children.Add(field);
        SelectedElementComboBox.Items.Add(new ComboBoxItem { Content = "حقل مخصص " + _customElementCounter.ToString(CultureInfo.InvariantCulture), Tag = tag });
        SelectComboTag(SelectedElementComboBox, tag);
        _studioSelectedFieldTag = tag;
        InspectorTabs.SelectedIndex = 0;
        SyncFieldToggleButtons();
        SyncSelectedElementEditor();
        ApplyPropertiesButton_Click(this, new RoutedEventArgs());
        SetStatus("أُضيف حقل فارغ مستقل. عدّل النص والألوان والحجم أو اترك النص فارغًا ليعمل كمساحة ملونة.", isSuccess: true);
    }

    private bool IsTemplateElementVisible(string key) => _visibleElements.TryGetValue(key, out var visible) && visible;
    private Dictionary<string, ElementDesignProperties> CaptureElementProperties()
    {
        EnsureElementBorderOverlays();
        var result = new Dictionary<string, ElementDesignProperties>(StringComparer.Ordinal);
        foreach (var tag in GetAllEditableElementTags())
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
                IsVisible = element.Visibility == Visibility.Visible,
                Text = (element as TextBlock)?.Text,
                IsBold = element is TextBlock textBlock && (textBlock.FontWeight == FontWeights.Bold || textBlock.FontWeight == FontWeights.SemiBold),
                ForegroundHex = GetElementForegroundHex(element),
                BackgroundHex = GetElementBackgroundHex(element),
                FontSize = element is TextBlock tb ? tb.FontSize : 11,
                FontFamily = element is TextBlock tbf ? tbf.FontFamily.Source : "Segoe UI",
                TextAlignment = element is TextBlock alignedText ? alignedText.TextAlignment.ToString() : null,
                HasBorder = _elementBorderOverlays.TryGetValue(tag, out var elementBorder) && elementBorder.BorderThickness.Left > 0 && elementBorder.BorderBrush is not SolidColorBrush { Color.A: 0 },
                BorderHex = _elementBorderOverlays.TryGetValue(tag, out var savedBorder) ? BrushToHex(savedBorder.BorderBrush) : "#C51D2E",
                BorderThickness = _elementBorderOverlays.TryGetValue(tag, out var savedThickness) ? savedThickness.BorderThickness.Left : 1.2
            };
        }
        return result;
    }
    private void ApplyElementProperties(Dictionary<string, ElementDesignProperties> properties)
    {
        EnsureElementBorderOverlays();
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
            // Standard field visibility is stored in VisibleElements; custom layer
            // visibility lives with its element properties and defaults true in old saves.
            if (pair.Key.StartsWith("custom-", StringComparison.Ordinal))
                element.Visibility = design.IsVisible ? Visibility.Visible : Visibility.Collapsed;
            if (element is TextBlock textBlock)
            {
                if (design.Text is not null)
                    textBlock.Text = string.Equals(pair.Key, "username", StringComparison.Ordinal)
                        ? NormalizeUsernameText(design.Text)
                        : string.Equals(pair.Key, "password", StringComparison.Ordinal)
                            ? NormalizePasswordText(design.Text)
                            : design.Text;
                if (pair.Key is "username" or "password") textBlock.FlowDirection = FlowDirection.LeftToRight;
                textBlock.FontWeight = design.IsBold ? FontWeights.Bold : FontWeights.Normal;
                if (design.FontSize is >= 4 and <= 60) textBlock.FontSize = design.FontSize;
                if (!string.IsNullOrWhiteSpace(design.FontFamily)) textBlock.FontFamily = new FontFamily(design.FontFamily);
                if (!string.IsNullOrWhiteSpace(design.TextAlignment) && Enum.TryParse<TextAlignment>(design.TextAlignment, true, out var textAlignment))
                    textBlock.TextAlignment = textAlignment;
                if (TryParseBrush(design.ForegroundHex, out var foreground)) textBlock.Foreground = foreground;
                if (TryParseBrush(design.BackgroundHex, out var background)) textBlock.Background = background;
            }
            else if (element is Shape shape && TryParseBrush(design.BackgroundHex, out var shapeFill)) shape.Fill = shapeFill;
            else if (element is Border border && TryParseBrush(design.BackgroundHex, out var borderFill)) border.Background = borderFill;
            ApplySavedElementBorder(pair.Key, design);
        }
        UpdateElementBorderOverlays();
    }
}

