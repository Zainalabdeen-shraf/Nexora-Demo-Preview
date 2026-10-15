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

public partial class TemplateDesignerView : UserControl
{
    // These controls have x:Name declarations in TemplateDesignerView.xaml.
    // Use the WPF-generated fields directly; runtime visual-tree lookup can fail
    // while this UserControl is still being constructed and is not yet connected.
    private TextBlock PreviewTimeBlock => PreviewTimeTextBlock;
    private TextBlock PreviewContactBlock => PreviewContactTextBlock;
    private CheckBox ShowContactOption => ShowContactCheckBox;
    private const string DefaultTemplateName = "Default_Design";
    private const string DefaultLoginUrl = "https://login";
    private const string TemplateFormatId = "TabCard.TemplateDesigner.v3";
    // Legacy designer coordinates used seven canvas units per millimeter. The new
    // coordinate system uses WPF DIPs (96 per inch), so font sizes now retain their
    // real physical size when the card is printed instead of shrinking with the canvas.
    private const double LegacyDesignBaseWidth = 420d;
    private const double LegacyDesignBaseHeight = 245d;
    private const double BaseWidthMm = 60d;
    private const double BaseHeightMm = 35d;
    private const double DipPerMillimeter = 96d / 25.4d;
    private const double LegacyToDipScale = DipPerMillimeter / 7d;
    private const double DesignBaseWidth = LegacyDesignBaseWidth * LegacyToDipScale;
    private const double DesignBaseHeight = LegacyDesignBaseHeight * LegacyToDipScale;
    private const long MaxImportedImageBytes = 15L * 1024 * 1024;
    private readonly Dictionary<string, bool> _visibleElements = CreateDefaultVisibility();
    private bool _isReady;
    private string _loginPageUrl = DefaultLoginUrl;
    private string _barcodeText = "50723015";
    private string _activePackageSource = "hotspot";
    private string? _packageCatalogLoadError;
    private string? _importedTemplateImagePath;
    private FrameworkElement? _draggingElement;
    private Point _dragStartPoint;
    private double _dragStartLeft;
    private double _dragStartTop;
    private double _zoom = 1d;
    // Logical canvas dimensions track the card's physical dimensions. Element sizes and
    // font sizes remain independent; resizing the card only remaps element positions.
    private double _lastCanvasWidth = DesignBaseWidth;
    private double _lastCanvasHeight = DesignBaseHeight;
    private bool _isAutoFitZoom = true;
    private bool _isApplyingFitZoom;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static readonly string[] EditableElementTags =
    [
        "login", "username", "password", "validity", "time", "price", "package", "download", "contact", "serial",
        "printDate", "batch", "pointOfSale", "extra", "reward", "qr", "barcode", "brand", "networkName",
        "logoBadge", "logoMark", "header", "divider"
    ];

    private static readonly string[] ElementLabels =
    [
        "رمز الدخول", "اسم المستخدم", "كلمة المرور", "الصلاحية", "الوقت", "السعر",
        "التحميل", "الباقة", "الرقم التسلسلي", "تاريخ الطباعة", "رقم الدفعة", "نقطة البيع",
        "معلومات إضافية", "أرقام التواصل", "كود المكافأة", "رمز QR", "الباركود", "شعار الشبكة", "الشريط العلوي", "الفاصل"
    ];

    public TemplateDesignerView()
    {
        InitializeComponent();
        InitializeElementOptions();
        LoadPackageOptions();
        _isReady = true;
        LoadSavedTemplate();
        if (!File.Exists(StoragePath))
        {
            _lastCanvasWidth = DesignBaseWidth * ParseDouble(CardWidthTextBox.Text, 66, 10, 200) / BaseWidthMm;
            _lastCanvasHeight = DesignBaseHeight * ParseDouble(CardHeightTextBox.Text, 15.5, 10, 200) / BaseHeightMm;
            ApplyElementProperties(CreateDefaultElementProperties());
        }
        UpdateSamplePackageFields();
        SyncMainElementChecks();
        UpdatePreview();
        SyncSelectedElementEditor();
        SetStatus(
            !string.IsNullOrWhiteSpace(_packageCatalogLoadError)
                ? _packageCatalogLoadError
                : (File.Exists(StoragePath) ? "تم تحميل القالب المحفوظ وربط الباقات الفعلية." : "قالب افتراضي جاهز للتصميم ومربوط بكتالوج الباقات."),
            isSuccess: string.IsNullOrWhiteSpace(_packageCatalogLoadError));
    }

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

    private static string StoragePath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexoraProductDemo", "templates", "tabcard-template-design.json");

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

    private void ImportTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "استيراد قالب TabCard أو صورة لقالب",
            Filter = "قالب TabCard (*.json)|*.json|صور القوالب (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|كل الملفات المدعومة (*.json;*.png;*.jpg;*.jpeg;*.bmp)|*.json;*.png;*.jpg;*.jpeg;*.bmp",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        try
        {
            if (string.Equals(System.IO.Path.GetExtension(dialog.FileName), ".json", StringComparison.OrdinalIgnoreCase))
            {
                var imported = JsonSerializer.Deserialize<TemplateDesignState>(File.ReadAllText(dialog.FileName));
                if (imported is null || (imported.Format != TemplateFormatId && imported.ElementProperties is not { Count: > 0 }))
                {
                    MessageBox.Show("هذا الملف ليس قالب TabCard قابلًا للاستيراد. يمكن استيراد قالب مُصدّر من TabCard بصيغة JSON أو صورة PNG/JPG/BMP كخلفية.", "صيغة غير مدعومة", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                ApplyTemplateState(imported);
                SetStatus($"تم استيراد القالب «{imported.TemplateName}». احفظه لحفظ النسخة محليًا.", isSuccess: true);
            }
            else
            {
                ImportTemplateImage(dialog.FileName);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("تعذر استيراد القالب: " + ex.Message, "تعذر الاستيراد", MessageBoxButton.OK, MessageBoxImage.Warning);
            SetStatus("فشل استيراد القالب: " + ex.Message, isSuccess: false);
        }
    }

    private void ImportBackgroundImageButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "اختيار صورة خلفية القالب",
            Filter = "صور القالب (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            try { ImportTemplateImage(dialog.FileName); }
            catch (Exception ex) { MessageBox.Show("تعذر استيراد الصورة: " + ex.Message, "تعذر الاستيراد", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
    }

    private void ExportTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ApplyPropertiesButton_Click(this, new RoutedEventArgs());
            var dialog = new SaveFileDialog
            {
                Title = "تصدير قالب TabCard",
                Filter = "قالب TabCard (*.json)|*.json",
                FileName = (string.IsNullOrWhiteSpace(TemplateNameTextBox.Text) ? DefaultTemplateName : TemplateNameTextBox.Text.Trim()) + ".json",
                AddExtension = true,
                DefaultExt = ".json",
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(CaptureState(), JsonOptions));
            SetStatus("تم تصدير القالب. ملف JSON يتضمن خصائص العناصر وصورة الخلفية المستوردة إن وجدت.", isSuccess: true);
        }
        catch (Exception ex) { SetStatus("تعذر تصدير القالب: " + ex.Message, isSuccess: false); }
    }

    private void ImportTemplateImage(string sourcePath)
    {
        var extension = System.IO.Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extension != ".png" && extension != ".jpg" && extension != ".jpeg" && extension != ".bmp")
            throw new InvalidDataException("امتداد الصورة غير مدعوم. استخدم PNG أو JPG أو BMP.");
        var info = new FileInfo(sourcePath);
        if (info.Length > MaxImportedImageBytes)
            throw new InvalidDataException("حجم الصورة أكبر من 15 ميجابايت.");

        var assetsDirectory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexoraProductDemo", "templates", "assets");
        Directory.CreateDirectory(assetsDirectory);
        var destination = System.IO.Path.Combine(assetsDirectory, "template-background-" + Guid.NewGuid().ToString("N") + extension);
        File.Copy(sourcePath, destination, overwrite: false);
        _importedTemplateImagePath = destination;
        LoadBackgroundImageFromPath(destination);
        _visibleElements["شعار الشبكة"] = false;
        _visibleElements["الشريط العلوي"] = false;
        _visibleElements["الفاصل"] = false;
        SyncMainElementChecks();
        TemplateNameTextBox.Text = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
        ApplyImportedBackgroundAppearance();
        UpdatePreview();
        SetStatus("تم استيراد صورة القالب كخلفية. يمكن تحريك وتغيير حجم وتدوير كل عنصر بيانات فوقها.", isSuccess: true);
    }

    private void LoadBackgroundImageFromPath(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.DecodePixelWidth = 1600;
        bitmap.EndInit();
        bitmap.Freeze();
        TemplateBackgroundImage.Source = bitmap;
        TemplateBackgroundImage.Visibility = Visibility.Visible;
    }

    private void ApplyImportedBackgroundAppearance()
    {
        var hasImage = !string.IsNullOrWhiteSpace(_importedTemplateImagePath) && File.Exists(_importedTemplateImagePath);
        TemplateBackgroundImage.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
        if (!hasImage) TemplateBackgroundImage.Source = null;
    }

    private string? GetImportedImageBase64()
    {
        if (string.IsNullOrWhiteSpace(_importedTemplateImagePath) || !File.Exists(_importedTemplateImagePath)) return null;
        var info = new FileInfo(_importedTemplateImagePath);
        return info.Length <= MaxImportedImageBytes ? Convert.ToBase64String(File.ReadAllBytes(_importedTemplateImagePath)) : null;
    }

    private string? RestoreBackgroundFromState(TemplateDesignState state)
    {
        if (!string.IsNullOrWhiteSpace(state.ImportedBackgroundImagePath) && File.Exists(state.ImportedBackgroundImagePath))
            return state.ImportedBackgroundImagePath;
        if (string.IsNullOrWhiteSpace(state.ImportedBackgroundImageBase64)) return null;

        var bytes = Convert.FromBase64String(state.ImportedBackgroundImageBase64);
        if (bytes.LongLength > MaxImportedImageBytes) throw new InvalidDataException("صورة الخلفية داخل القالب أكبر من 15 ميجابايت.");
        var extension = (state.ImportedBackgroundImageExtension ?? ".png").ToLowerInvariant();
        if (extension != ".png" && extension != ".jpg" && extension != ".jpeg" && extension != ".bmp") extension = ".png";
        var assetsDirectory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexoraProductDemo", "templates", "assets");
        Directory.CreateDirectory(assetsDirectory);
        var path = System.IO.Path.Combine(assetsDirectory, "template-background-" + Guid.NewGuid().ToString("N") + extension);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private void TemplateDesignerView_Loaded(object sender, RoutedEventArgs e)
    {
        FitCardToWorkspace();
    }

    private void DesignPreviewHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_isReady || !_isAutoFitZoom || !IsLoaded) return;
        FitCardToWorkspace();
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isReady) return;
        if (!_isApplyingFitZoom) _isAutoFitZoom = false;
        _zoom = Math.Clamp(e.NewValue, 0.1, 2.0);
        ApplyCardDimensionTransform();
    }

    private void ZoomInButton_Click(object sender, RoutedEventArgs e) => ZoomSlider.Value = Math.Min(2.0, ZoomSlider.Value + 0.1);
    private void ZoomOutButton_Click(object sender, RoutedEventArgs e) => ZoomSlider.Value = Math.Max(0.1, ZoomSlider.Value - 0.1);

    private void ZoomFitButton_Click(object sender, RoutedEventArgs e) => FitCardToWorkspace();

    private void FitCardToWorkspace()
    {
        if (DesignPreviewHost is null || ZoomSlider is null || DesignPreviewHost.ActualWidth < 40 || DesignPreviewHost.ActualHeight < 40)
            return;

        _isApplyingFitZoom = true;
        try
        {
            ApplyCardDimensionTransform();
            var availableWidth = Math.Max(40, DesignPreviewHost.ActualWidth - 8);
            var availableHeight = Math.Max(40, DesignPreviewHost.ActualHeight - 8);
            var baseWidth = Math.Max(1, DesignViewportCanvas.Width);
            var baseHeight = Math.Max(1, DesignViewportCanvas.Height);
            var fittedZoom = Math.Clamp(Math.Min(availableWidth / baseWidth, availableHeight / baseHeight), 0.1, 2.0);
            _zoom = fittedZoom;
            ZoomSlider.Value = fittedZoom;
            _isAutoFitZoom = true;
            ApplyCardDimensionTransform();
        }
        finally
        {
            _isApplyingFitZoom = false;
        }
    }

    private void CardDesignCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isReady) return;
        var element = FindEditableCanvasElement(e.OriginalSource as DependencyObject);
        var tag = element is null ? null : GetTagByElement(element);
        if (element is null || tag is null) return;

        SelectComboTag(SelectedElementComboBox, tag);
        SyncSelectedElementEditor();
        _draggingElement = element;
        _dragStartPoint = e.GetPosition(CardDesignCanvas);
        _dragStartLeft = Canvas.GetLeft(element);
        _dragStartTop = Canvas.GetTop(element);
        if (!double.IsFinite(_dragStartLeft)) _dragStartLeft = 0;
        if (!double.IsFinite(_dragStartTop)) _dragStartTop = 0;
        CardDesignCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void CardDesignCanvas_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_draggingElement is null || e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(CardDesignCanvas);
        var desiredX = _dragStartLeft + current.X - _dragStartPoint.X;
        var desiredY = _dragStartTop + current.Y - _dragStartPoint.Y;
        var position = ClampElementPosition(_draggingElement, desiredX, desiredY);
        Canvas.SetLeft(_draggingElement, position.X);
        Canvas.SetTop(_draggingElement, position.Y);
        SyncSelectedElementEditor();
        UpdateSelectionFrame();
    }

    private void CardDesignCanvas_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggingElement is null) return;
        _draggingElement = null;
        CardDesignCanvas.ReleaseMouseCapture();
        SyncSelectedElementEditor();
        UpdateSelectionFrame();
    }

    private Point ClampElementPosition(FrameworkElement element, double desiredX, double desiredY)
    {
        var width = Math.Min(GetElementWidth(element), Math.Max(1, CardDesignCanvas.Width));
        var height = Math.Min(GetElementHeight(element), Math.Max(1, CardDesignCanvas.Height));
        var radians = GetElementRotation(element) * Math.PI / 180d;
        var rotatedWidth = Math.Abs(width * Math.Cos(radians)) + Math.Abs(height * Math.Sin(radians));
        var rotatedHeight = Math.Abs(width * Math.Sin(radians)) + Math.Abs(height * Math.Cos(radians));
        var minX = Math.Max(0, (rotatedWidth - width) / 2d);
        var minY = Math.Max(0, (rotatedHeight - height) / 2d);
        var maxX = Math.Max(minX, CardDesignCanvas.Width - (rotatedWidth + width) / 2d);
        var maxY = Math.Max(minY, CardDesignCanvas.Height - (rotatedHeight + height) / 2d);
        return new Point(Math.Clamp(desiredX, minX, maxX), Math.Clamp(desiredY, minY, maxY));
    }

    private FrameworkElement? FindEditableCanvasElement(DependencyObject? source)
    {
        if (source is null) return null;
        DependencyObject? current = source;
        FrameworkElement? directChild = null;
        while (current is not null && !ReferenceEquals(current, CardDesignCanvas))
        {
            DependencyObject? parent;
            if (current is Visual || current is System.Windows.Media.Media3D.Visual3D)
                parent = VisualTreeHelper.GetParent(current);
            else
                parent = LogicalTreeHelper.GetParent(current);
            if (current is FrameworkElement element && ReferenceEquals(parent, CardDesignCanvas))
                directChild = element;
            current = parent;
        }
        if (directChild is null || directChild == SelectionFrameBorder || directChild == CardSurfaceBorder || directChild == TemplateBackgroundImage)
            return null;
        return GetTagByElement(directChild) is not null ? directChild : null;
    }

    private string? GetTagByElement(FrameworkElement element)
    {
        foreach (var tag in EditableElementTags)
            if (ReferenceEquals(GetElementByTag(tag), element)) return tag;
        return null;
    }

    private void UpdateSelectionFrame()
    {
        var selected = GetSelectedElement();
        if (selected is null || SelectionFrameBorder is null) return;
        var left = Canvas.GetLeft(selected);
        var top = Canvas.GetTop(selected);
        if (!double.IsFinite(left)) left = 0;
        if (!double.IsFinite(top)) top = 0;
        Canvas.SetLeft(SelectionFrameBorder, left - 2);
        Canvas.SetTop(SelectionFrameBorder, top - 2);
        SelectionFrameBorder.Width = Math.Max(6, GetElementWidth(selected) + 4);
        SelectionFrameBorder.Height = Math.Max(6, GetElementHeight(selected) + 4);
        SelectionFrameBorder.RenderTransformOrigin = new Point(0.5, 0.5);
        SelectionFrameBorder.RenderTransform = new RotateTransform(GetElementRotation(selected));
        SelectionFrameBorder.Visibility = Visibility.Visible;
    }

    private static double GetElementWidth(FrameworkElement element) => double.IsFinite(element.Width) && element.Width > 0 ? element.Width : Math.Max(6, element.ActualWidth);
    private static double GetElementHeight(FrameworkElement element) => double.IsFinite(element.Height) && element.Height > 0 ? element.Height : Math.Max(6, element.ActualHeight);
    private static double GetElementRotation(FrameworkElement element) => element.RenderTransform is RotateTransform rotation ? rotation.Angle : 0;

    private void PreviewTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyPropertiesButton_Click(this, new RoutedEventArgs());
        ShowPrintPreviewDialog();
    }

    private void ShowPrintPreviewDialog()
    {
        const double pageWidthUnits = 210d;
        const double pageHeightUnits = 297d; // A4 portrait dimensions, represented in millimetre-like page units
        var dialog = CreateDialog("معاينة صفحة الطباعة", 1180, 820);
        dialog.MinWidth = 900;
        dialog.MinHeight = 650;
        dialog.WindowState = WindowState.Normal;
        dialog.ResizeMode = ResizeMode.CanResize;
        dialog.Owner = Window.GetWindow(this);
        dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        dialog.Topmost = false;
        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "معاينة صفحة الطباعة — الصفحة كاملة",
            FontSize = 19,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("PrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        var settingsBorder = new Border
        {
            Background = Brushes.White,
            BorderBrush = (Brush)FindResource("BorderBrushSoft"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 8)
        };
        var settingsPanel = new UniformGrid { Columns = 7 };
        var selectedPackage = GetActivePackage();
        var hours = AddTextField(settingsPanel, "الوقت / مدة الاستخدام", selectedPackage?.TimeLimit ?? "—");
        var validity = AddTextField(settingsPanel, "الصلاحية بالأيام", selectedPackage?.Validity ?? "—");
        var price = AddTextField(settingsPanel, "السعر على الكرت", selectedPackage is null ? "—" : selectedPackage.PrintPrice.ToString("0.##", CultureInfo.InvariantCulture));
        var upload = AddTextField(settingsPanel, "حد التحميل", selectedPackage?.DataLimit ?? "—");
        var cardCount = AddTextField(settingsPanel, "إجمالي الكروت", "51");
        var columns = AddTextField(settingsPanel, "عدد الأعمدة", "3");
        var rows = AddTextField(settingsPanel, "حد الصفوف/صفحة", "17");
        var numberPrefix = AddTextField(settingsPanel, "بداية الرقم", "50723001");
        var usernamePrefix = AddTextField(settingsPanel, "بادئة اسم المستخدم", "");
        var passwordPrefix = AddTextField(settingsPanel, "بادئة كلمة المرور", "");
        var packageName = AddTextField(settingsPanel, "اسم الباقة", selectedPackage is not null ? GetPackageName(selectedPackage) : "—");
        var networkName = AddTextField(settingsPanel, "اسم الشبكة", PreviewNetworkTextBlock.Text);
        var contact = AddTextField(settingsPanel, "أرقام التواصل", PreviewContactBlock.Text);
        networkName.ToolTip = "اكتب اسم الشبكة الحقيقي؛ سيتم تحديث المصمم. احفظ القالب لتثبيت التغيير للطباعة.";
        contact.ToolTip = "أدخل أرقام التواصل الصحيحة؛ لا توجد أرقام مثبتة في كتالوج الباقات. احفظ القالب بعد التعديل.";
        networkName.TextChanged += (_, _) =>
        {
            if (PreviewNetworkTextBlock.Text == networkName.Text) return;
            PreviewNetworkTextBlock.Text = networkName.Text;
            UpdatePreview();
            SetStatus("تم تحديث اسم الشبكة في المصمم؛ احفظ القالب لتثبيته للطباعة.", isSuccess: true);
        };
        contact.TextChanged += (_, _) =>
        {
            if (PreviewContactBlock.Text == contact.Text) return;
            PreviewContactBlock.Text = contact.Text;
            UpdatePreview();
            SetStatus("تم تحديث أرقام التواصل في المصمم؛ احفظ القالب لتثبيته للطباعة.", isSuccess: true);
        };
        var previewPage = AddTextField(settingsPanel, "رقم الصفحة", "1");
        hours.IsReadOnly = validity.IsReadOnly = price.IsReadOnly = upload.IsReadOnly = packageName.IsReadOnly = true;
        hours.ToolTip = validity.ToolTip = price.ToolTip = upload.ToolTip = packageName.ToolTip = "تُقرأ هذه البيانات من الباقة المختارة؛ اختر الباقة من شريط المصمم لتغييرها.";
        settingsBorder.Child = settingsPanel;
        Grid.SetRow(settingsBorder, 1);
        root.Children.Add(settingsBorder);

        var previewBorder = new Border
        {
            Background = (Brush)FindResource("AppBgBrush"),
            BorderBrush = (Brush)FindResource("BorderBrushSoft"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(5)
        };
        var previewRoot = new Grid();
        previewRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        previewRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var previewSummary = new TextBlock
        {
            Text = "ورقة A4 عمودية (210 × 297 مم) — افتراضيًا 3 أعمدة × 17 صفًا = 51 كرتًا.",
            Foreground = (Brush)FindResource("MutedBrush"),
            FontSize = 10.5,
            Margin = new Thickness(3, 0, 3, 4)
        };
        Grid.SetRow(previewSummary, 0);
        previewRoot.Children.Add(previewSummary);

        var pageHost = new Grid { ClipToBounds = true, Background = (Brush)FindResource("AppBgBrush") };
        var pageCards = new Canvas { Width = pageWidthUnits, Height = pageHeightUnits, ClipToBounds = true };
        var pageSurface = new Border
        {
            Width = pageWidthUnits,
            Height = pageHeightUnits,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xCF, 0xD4, 0xDB)),
            BorderThickness = new Thickness(0.5),
            Padding = new Thickness(0),
            Child = pageCards,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 9, ShadowDepth = 2, Opacity = 0.16 }
        };
        var pageViewbox = new Viewbox { Stretch = Stretch.Uniform, Child = pageSurface, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        pageHost.Children.Add(pageViewbox);
        pageHost.SizeChanged += (_, _) => FitPrintPage();
        Grid.SetRow(pageHost, 1);
        previewRoot.Children.Add(pageHost);
        previewBorder.Child = previewRoot;
        Grid.SetRow(previewBorder, 2);
        root.Children.Add(previewBorder);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 7, 0, 0) };
        var updatePreviewButton = MakeButton("معاينة الصفحة", primary: true);
        updatePreviewButton.MinWidth = 130;
        var closeButton = MakeButton("إغلاق", primary: false);
        closeButton.MinWidth = 90;
        closeButton.Margin = new Thickness(8, 0, 0, 0);
        updatePreviewButton.Click += (_, _) => RenderSampleCards();
        closeButton.Click += (_, _) => dialog.Close();
        actions.Children.Add(updatePreviewButton);
        actions.Children.Add(closeButton);
        Grid.SetRow(actions, 3);
        root.Children.Add(actions);
        dialog.Content = root;

        void FitPrintPage()
        {
            if (pageHost.ActualWidth < 50 || pageHost.ActualHeight < 50) return;
            var availableWidth = Math.Max(1, pageHost.ActualWidth - 14);
            var availableHeight = Math.Max(1, pageHost.ActualHeight - 10);
            var scale = Math.Min(availableWidth / pageWidthUnits, availableHeight / pageHeightUnits);
            pageViewbox.Width = pageWidthUnits * scale;
            pageViewbox.Height = pageHeightUnits * scale;
        }

        void RenderSampleCards()
        {
            const double pageMarginMm = 5d;
            const double cardGapMm = 1d;
            var total = ParseInt(cardCount.Text, 51, 1, 10000);
            var requestedColumns = ParseInt(columns.Text, 3, 1, 8);
            var cardWidthMm = ParseDouble(CardWidthTextBox.Text, 66, 10, 200);
            var cardHeightMm = ParseDouble(CardHeightTextBox.Text, 15.5, 10, 200);
            var usableWidthMm = pageWidthUnits - pageMarginMm * 2d;
            var usableHeightMm = pageHeightUnits - pageMarginMm * 2d;
            var maxColumns = Math.Max(1, (int)Math.Floor((usableWidthMm + cardGapMm) / (cardWidthMm + cardGapMm)));
            var maxRows = Math.Max(1, (int)Math.Floor((usableHeightMm + cardGapMm) / (cardHeightMm + cardGapMm)));
            var colCount = Math.Min(requestedColumns, maxColumns);
            var requestedRows = ParseInt(rows.Text, maxRows, 1, 100);
            var rowCapacity = Math.Min(requestedRows, maxRows);
            var perPage = Math.Max(1, rowCapacity * colCount);
            var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)perPage));
            var pageNumber = ParseInt(previewPage.Text, 1, 1, totalPages);
            previewPage.Text = pageNumber.ToString(CultureInfo.InvariantCulture);
            columns.Text = colCount.ToString(CultureInfo.InvariantCulture);
            rows.Text = rowCapacity.ToString(CultureInfo.InvariantCulture);
            cardCount.Text = total.ToString(CultureInfo.InvariantCulture);
            if (colCount != requestedColumns || rowCapacity != requestedRows)
                SetStatus($"تم ضبط التخطيط حسب مقاس الكرت الفعلي: {colCount} أعمدة × {rowCapacity} صفوف كحد أقصى؛ لم يتم تصغير الخطوط أو الكروت.", isSuccess: false);

            pageCards.Children.Clear();
            var firstIndex = (pageNumber - 1) * perPage;
            var cardsOnPage = Math.Min(perPage, Math.Max(0, total - firstIndex));
            var first = ParseLong(numberPrefix.Text, 50723001);
            var priceText = string.IsNullOrWhiteSpace(price.Text) ? "—" : price.Text.Trim();
            var uploadText = string.IsNullOrWhiteSpace(upload.Text) ? "—" : upload.Text.Trim();
            previewSummary.Text = $"A4 عمودي · الصفحة {pageNumber} من {totalPages} · {colCount} أعمدة · السعة {perPage} كرت/صفحة · {cardsOnPage} كرت في هذه الصفحة · إجمالي {total:N0} كرت · المقاس {cardWidthMm:0.#} × {cardHeightMm:0.#} مم";

            for (var i = 0; i < cardsOnPage; i++)
            {
                var globalIndex = firstIndex + i;
                var cardNumber = (first + globalIndex).ToString(CultureInfo.InvariantCulture);
                var sampleUsername = string.IsNullOrWhiteSpace(usernamePrefix.Text)
                    ? cardNumber
                    : usernamePrefix.Text.Trim() + (globalIndex + 1).ToString(CultureInfo.InvariantCulture);
                var samplePassword = string.IsNullOrWhiteSpace(passwordPrefix.Text)
                    ? "143" + (globalIndex % 10).ToString(CultureInfo.InvariantCulture)
                    : passwordPrefix.Text.Trim() + (globalIndex + 1).ToString(CultureInfo.InvariantCulture);
                var sampleCard = BuildDesignedPrintPreviewCard(
                    cardNumber, sampleUsername, samplePassword, hours.Text.Trim(), validity.Text.Trim(),
                    priceText, uploadText, networkName.Text.Trim(), packageName.Text.Trim(), contact.Text.Trim(), globalIndex);

                var cardViewbox = new Viewbox
                {
                    Width = cardWidthMm,
                    Height = cardHeightMm,
                    Stretch = Stretch.Uniform,
                    StretchDirection = StretchDirection.DownOnly,
                    ClipToBounds = true,
                    Child = sampleCard
                };
                var rowIndex = i / colCount;
                var columnIndexFromRight = i % colCount;
                var x = pageWidthUnits - pageMarginMm - cardWidthMm - columnIndexFromRight * (cardWidthMm + cardGapMm);
                var y = pageMarginMm + rowIndex * (cardHeightMm + cardGapMm);
                Canvas.SetLeft(cardViewbox, x);
                Canvas.SetTop(cardViewbox, y);
                cardViewbox.Cursor = Cursors.Hand;
                cardViewbox.ToolTip = "انقر لمعاينة البطاقة بحجم مكبّر";
                cardViewbox.MouseLeftButtonUp += (_, _) => ShowSampleCardDetailDialog(
                    cardNumber, sampleUsername, samplePassword, hours.Text.Trim(), validity.Text.Trim(),
                    priceText, uploadText, networkName.Text.Trim(), packageName.Text.Trim(), contact.Text.Trim(), globalIndex);
                pageCards.Children.Add(cardViewbox);
            }
            FitPrintPage();
        }

        RenderSampleCards();
        dialog.Loaded += (_, _) => FitPrintPage();
        dialog.ShowDialog();
    }

    private void ShowSampleCardDetailDialog(string number, string username, string password, string hours, string validity, string price, string upload, string network, string packageName, string contact, int cardIndex)
    {
        var dialog = CreateDialog("معاينة بطاقة مكبّرة", 940, 360);
        dialog.MinWidth = 720;
        dialog.MinHeight = 300;

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var title = new TextBlock
        {
            Text = "معاينة مكبّرة — انقر إغلاق للعودة إلى ورقة A4",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("PrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 14)
        };
        root.Children.Add(title);

        var cardSurface = new Border
        {
            Background = Brushes.White,
            BorderBrush = (Brush)FindResource("BorderBrushSoft"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new Viewbox
            {
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.UpOnly,
                Width = 820,
                Height = 195,
                Child = BuildDesignedPrintPreviewCard(number, username, password, hours, validity, price, upload, network, packageName, contact, cardIndex)
            }
        };
        Grid.SetRow(cardSurface, 1);
        root.Children.Add(cardSurface);

        var close = MakeButton("إغلاق", primary: false);
        close.MinWidth = 100;
        close.Margin = new Thickness(0, 14, 0, 0);
        close.HorizontalAlignment = HorizontalAlignment.Left;
        close.Click += (_, _) => dialog.Close();
        Grid.SetRow(close, 2);
        root.Children.Add(close);

        dialog.Content = root;
        dialog.ShowDialog();
    }

    private Canvas BuildDesignedPrintCard(string number, string username, string password, string hours, string validity, string price, string upload, string network, string packageName, string contact, int cardIndex)
    {
        // Preserve the original print-path surface and coordinates. Page-preview layout
        // has its own builder below so preview-only scaling never alters actual printing.
        var viewport = new Canvas
        {
            Width = Math.Max(1, DesignViewportCanvas.Width),
            Height = Math.Max(1, DesignViewportCanvas.Height),
            Background = Brushes.Transparent,
            ClipToBounds = true
        };
        var cardCanvas = new Canvas
        {
            Width = Math.Max(1, CardDesignCanvas.Width),
            Height = Math.Max(1, CardDesignCanvas.Height),
            Background = Brushes.Transparent,
            RenderTransformOrigin = CardDesignCanvas.RenderTransformOrigin,
            RenderTransform = CardDesignCanvas.RenderTransform?.CloneCurrentValue(),
            ClipToBounds = true
        };
        Canvas.SetLeft(cardCanvas, double.IsFinite(Canvas.GetLeft(CardDesignCanvas)) ? Canvas.GetLeft(CardDesignCanvas) : 0);
        Canvas.SetTop(cardCanvas, double.IsFinite(Canvas.GetTop(CardDesignCanvas)) ? Canvas.GetTop(CardDesignCanvas) : 0);
        viewport.Children.Add(cardCanvas);

        foreach (UIElement child in CardDesignCanvas.Children)
        {
            if (ReferenceEquals(child, SelectionFrameBorder) || child is not FrameworkElement source) continue;
            var clone = CloneTemplateVisual(source, number, username, password, hours, validity, price, upload, network, packageName, contact, cardIndex);
            if (clone is null) continue;
            var left = Canvas.GetLeft(source);
            var top = Canvas.GetTop(source);
            Canvas.SetLeft(clone, double.IsFinite(left) ? left : 0);
            Canvas.SetTop(clone, double.IsFinite(top) ? top : 0);
            Panel.SetZIndex(clone, Panel.GetZIndex(source));
            cardCanvas.Children.Add(clone);
        }
        return viewport;
    }

    private Canvas BuildDesignedPrintPreviewCard(string number, string username, string password, string hours, string validity, string price, string upload, string network, string packageName, string contact, int cardIndex)
    {
        // This builder is intentionally preview-only. The editor surface is 420 × 245,
        // but the physical card defaults to 66 × 15.5 mm. Most visible template content
        // occupies only the upper part of the editor canvas; crop that unused workspace
        // and compact Y coordinates so each preview slot uses the full physical card area.
        var sourceWidth = Math.Max(1, CardDesignCanvas.Width);
        var sourceHeight = Math.Max(1, CardDesignCanvas.Height);
        var physicalWidth = ParseDouble(CardWidthTextBox.Text, 66, 10, 200);
        var physicalHeight = ParseDouble(CardHeightTextBox.Text, 15.5, 10, 200);

        // Some default fields deliberately extend slightly past the editor canvas's
        // right edge (for example the password/contact labels). Include their bounds so
        // they do not get clipped in the preview-only surface.
        var contentRight = (double)sourceWidth;
        var contentBottom = 1d;
        foreach (UIElement child in CardDesignCanvas.Children)
        {
            if (child is not FrameworkElement source ||
                ReferenceEquals(source, SelectionFrameBorder) ||
                source.Visibility != Visibility.Visible ||
                IsPreviewFullCanvasBackground(source, sourceHeight))
                continue;

            var left = Canvas.GetLeft(source);
            var top = Canvas.GetTop(source);
            var safeLeft = double.IsFinite(left) ? Math.Max(0, left) : 0;
            var safeTop = double.IsFinite(top) ? Math.Max(0, top) : 0;
            contentRight = Math.Max(contentRight, safeLeft + GetElementWidth(source));
            contentBottom = Math.Max(contentBottom, safeTop + GetElementHeight(source));
        }

        var previewWidth = Math.Max(sourceWidth, contentRight);
        var previewHeight = Math.Max(1, previewWidth * physicalHeight / physicalWidth);
        var verticalScale = Math.Min(1d, previewHeight / contentBottom);
        var previewCanvas = new Canvas
        {
            Width = previewWidth,
            Height = previewHeight,
            Background = Brushes.Transparent,
            ClipToBounds = true
        };

        foreach (UIElement child in CardDesignCanvas.Children)
        {
            if (ReferenceEquals(child, SelectionFrameBorder) || child is not FrameworkElement source) continue;
            var clone = CloneTemplateVisual(source, number, username, password, hours, validity, price, upload, network, packageName, contact, cardIndex);
            if (clone is null) continue;

            var left = Canvas.GetLeft(source);
            var top = Canvas.GetTop(source);
            Canvas.SetLeft(clone, double.IsFinite(left) ? left : 0);
            Canvas.SetTop(clone, (double.IsFinite(top) ? top : 0) * verticalScale);
            ApplyPreviewVerticalScale(clone, verticalScale, previewWidth, previewHeight, sourceHeight);
            Panel.SetZIndex(clone, Panel.GetZIndex(source));
            previewCanvas.Children.Add(clone);
        }

        return previewCanvas;
    }

    private static bool IsPreviewFullCanvasBackground(FrameworkElement element, double sourceCanvasHeight)
    {
        var elementName = element.Name.Length > 0 ? element.Name : element.Tag?.ToString();
        return elementName is nameof(CardSurfaceBorder) or nameof(TemplateBackgroundImage) or nameof(CardHeaderBorder) ||
               (element.Height >= 0.9 * sourceCanvasHeight && Canvas.GetTop(element) <= 1d);
    }

    private static void ApplyPreviewVerticalScale(FrameworkElement element, double scale, double previewWidth, double previewHeight, double sourceCanvasHeight)
    {
        // Background surfaces must span the preview card; ordinary elements are fitted
        // to the occupied design area. Text is scaled by the same Y-layout factor as its
        // line box, while all X positions, widths, and the original print builder remain intact.
        if (IsPreviewFullCanvasBackground(element, sourceCanvasHeight))
        {
            element.Height = previewHeight;
            var elementName = element.Name.Length > 0 ? element.Name : element.Tag?.ToString();
            if (elementName is nameof(CardSurfaceBorder) or nameof(TemplateBackgroundImage))
                element.Width = previewWidth;
        }
        else if (double.IsFinite(element.Height) && element.Height > 0)
        {
            element.Height *= scale;
        }

        element.Margin = new Thickness(element.Margin.Left, element.Margin.Top * scale, element.Margin.Right, element.Margin.Bottom * scale);

        if (element is TextBlock text)
            text.FontSize = Math.Max(1, text.FontSize * scale);

        if (element is Grid grid)
        {
            foreach (var row in grid.RowDefinitions)
            {
                if (row.Height.IsAbsolute) row.Height = new GridLength(row.Height.Value * scale, GridUnitType.Pixel);
                if (double.IsFinite(row.MinHeight) && row.MinHeight > 0) row.MinHeight *= scale;
                if (double.IsFinite(row.MaxHeight) && row.MaxHeight > 0 && row.MaxHeight < double.MaxValue) row.MaxHeight *= scale;
            }
            foreach (UIElement child in grid.Children)
            {
                if (child is FrameworkElement nested)
                    ApplyPreviewVerticalScale(nested, scale, previewWidth, previewHeight, sourceCanvasHeight);
            }
        }
        else if (element is Border border && border.Child is FrameworkElement nestedChild)
        {
            ApplyPreviewVerticalScale(nestedChild, scale, previewWidth, previewHeight, sourceCanvasHeight);
        }
    }

    private FrameworkElement? CloneTemplateVisual(FrameworkElement source, string number, string username, string password, string hours, string validity, string price, string upload, string network, string packageName, string contact, int cardIndex)
    {
        FrameworkElement? clone = source switch
        {
            TextBlock text => new TextBlock
            {
                Text = GetPrintPreviewText(text, number, username, password, hours, validity, price, upload, network, packageName, contact, cardIndex),
                FontFamily = text.FontFamily,
                FontSize = text.FontSize,
                FontWeight = text.FontWeight,
                FontStyle = text.FontStyle,
                FontStretch = text.FontStretch,
                Foreground = text.Foreground,
                Background = text.Background,
                TextAlignment = text.TextAlignment,
                TextWrapping = text.TextWrapping,
                TextTrimming = text.TextTrimming,
                FlowDirection = text.FlowDirection,
                Padding = text.Padding,
                VerticalAlignment = text.VerticalAlignment,
                HorizontalAlignment = text.HorizontalAlignment
            },
            Border border => CloneBorder(border, number, username, password, hours, validity, price, upload, network, packageName, contact, cardIndex),
            Ellipse ellipse => new Ellipse { Fill = ellipse.Fill, Stroke = ellipse.Stroke, StrokeThickness = ellipse.StrokeThickness },
            Image image => new Image { Source = image.Source, Stretch = image.Stretch, StretchDirection = image.StretchDirection },
            Grid grid => CloneGrid(grid, number, username, password, hours, validity, price, upload, network, packageName, contact, cardIndex),
            _ => null
        };

        if (clone is null) return null;
        CopyCommonElementProperties(source, clone);
        clone.Tag = source.Name;
        return clone;
    }

    private Border CloneBorder(Border source, string number, string username, string password, string hours, string validity, string price, string upload, string network, string packageName, string contact, int cardIndex)
    {
        var clone = new Border
        {
            Background = source.Background,
            BorderBrush = source.BorderBrush,
            BorderThickness = source.BorderThickness,
            CornerRadius = source.CornerRadius,
            Padding = source.Padding
        };
        if (source.Child is FrameworkElement child)
            clone.Child = CloneTemplateVisual(child, number, username, password, hours, validity, price, upload, network, packageName, contact, cardIndex);
        return clone;
    }

    private Grid CloneGrid(Grid source, string number, string username, string password, string hours, string validity, string price, string upload, string network, string packageName, string contact, int cardIndex)
    {
        var clone = new Grid { Background = source.Background };
        foreach (var row in source.RowDefinitions)
            clone.RowDefinitions.Add(new RowDefinition { Height = row.Height, MinHeight = row.MinHeight, MaxHeight = row.MaxHeight });
        foreach (var column in source.ColumnDefinitions)
            clone.ColumnDefinitions.Add(new ColumnDefinition { Width = column.Width, MinWidth = column.MinWidth, MaxWidth = column.MaxWidth });
        foreach (UIElement child in source.Children)
        {
            if (child is not FrameworkElement childElement) continue;
            var childClone = CloneTemplateVisual(childElement, number, username, password, hours, validity, price, upload, network, packageName, contact, cardIndex);
            if (childClone is null) continue;
            Grid.SetRow(childClone, Grid.GetRow(childElement));
            Grid.SetColumn(childClone, Grid.GetColumn(childElement));
            Grid.SetRowSpan(childClone, Grid.GetRowSpan(childElement));
            Grid.SetColumnSpan(childClone, Grid.GetColumnSpan(childElement));
            clone.Children.Add(childClone);
        }
        return clone;
    }

    private static void CopyCommonElementProperties(FrameworkElement source, FrameworkElement target)
    {
        target.Width = source.Width;
        target.Height = source.Height;
        target.Margin = source.Margin;
        target.Opacity = source.Opacity;
        target.Visibility = source.Visibility;
        target.HorizontalAlignment = source.HorizontalAlignment;
        target.VerticalAlignment = source.VerticalAlignment;
        target.FlowDirection = source.FlowDirection;
        target.RenderTransformOrigin = source.RenderTransformOrigin;
        target.RenderTransform = source.RenderTransform?.CloneCurrentValue();
        target.ClipToBounds = source.ClipToBounds;
        target.IsHitTestVisible = false;
    }

    private string GetPrintPreviewText(TextBlock source, string number, string username, string password, string hours, string validity, string price, string upload, string network, string packageName, string contact, int cardIndex)
    {
        return source.Name switch
        {
            nameof(PreviewLoginCodeTextBlock) => number,
            nameof(PreviewUsernameTextBlock) => "اسم المستخدم: " + username,
            nameof(PreviewPasswordTextBlock) => string.IsNullOrWhiteSpace(password) ? string.Empty : "كلمة المرور: " + password,
            nameof(PreviewValidityTextBlock) => "الصلاحية: " + validity,
            "PreviewTimeTextBlock" => "الوقت: " + hours,
            nameof(PreviewPriceTextBlock) => string.IsNullOrWhiteSpace(price) || price.Trim() == "—"
                ? "السعر: —"
                : "السعر: " + price + (price.Contains("ريال", StringComparison.Ordinal) ? string.Empty : " ريال"),
            nameof(PreviewPackageTextBlock) => "الباقة: " + packageName,
            nameof(PreviewDownloadTextBlock) => "التحميل: " + upload,
            "PreviewContactTextBlock" => string.IsNullOrWhiteSpace(contact) ? source.Text : contact,
            nameof(PreviewPrintDateTextBlock) => "تاريخ الطباعة: " + DateTime.Now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture),
            nameof(PreviewSerialTextBlock) => "الرقم التسلسلي: " + number,
            nameof(PreviewBatchTextBlock) => "رقم الدفعة: B-" + (cardIndex + 1).ToString("D4", CultureInfo.InvariantCulture),
            nameof(PreviewNetworkTextBlock) => string.IsNullOrWhiteSpace(network) ? source.Text : network,
            _ => source.Text
        };
    }

    public Size GetPhysicalCardSizeMillimeters()
    {
        var width = ParseDouble(CardWidthTextBox.Text, 66, 10, 200);
        var height = ParseDouble(CardHeightTextBox.Text, 15.5, 10, 200);
        var radians = ParseDouble(CardRotationTextBox.Text, 0, -180, 180) * Math.PI / 180d;
        return new Size(
            Math.Abs(width * Math.Cos(radians)) + Math.Abs(height * Math.Sin(radians)),
            Math.Abs(width * Math.Sin(radians)) + Math.Abs(height * Math.Cos(radians)));
    }

    public FrameworkElement CreatePrintableCard(
        GeneratedDemoCard card,
        int cardIndex,
        bool showNumber = true,
        bool showUsername = true,
        bool showPassword = true,
        bool showPackage = true,
        bool showPrice = true)
    {
        ArgumentNullException.ThrowIfNull(card);
        var originalVisibility = new Dictionary<FrameworkElement, Visibility>
        {
            [PreviewLoginCodeTextBlock] = PreviewLoginCodeTextBlock.Visibility,
            [PreviewUsernameTextBlock] = PreviewUsernameTextBlock.Visibility,
            [PreviewPasswordTextBlock] = PreviewPasswordTextBlock.Visibility,
            [PreviewPackageTextBlock] = PreviewPackageTextBlock.Visibility,
            [PreviewPriceTextBlock] = PreviewPriceTextBlock.Visibility
        };

        try
        {
            if (!showNumber) PreviewLoginCodeTextBlock.Visibility = Visibility.Collapsed;
            if (!showUsername) PreviewUsernameTextBlock.Visibility = Visibility.Collapsed;
            if (!showPassword || string.IsNullOrWhiteSpace(card.Password) ||
                string.Equals(card.Password.Trim(), card.Username?.Trim(), StringComparison.Ordinal))
                PreviewPasswordTextBlock.Visibility = Visibility.Collapsed;
            if (!showPackage) PreviewPackageTextBlock.Visibility = Visibility.Collapsed;
            if (!showPrice) PreviewPriceTextBlock.Visibility = Visibility.Collapsed;

            var packageName = card.PackageName;
            var separator = packageName.LastIndexOf(" — ", StringComparison.Ordinal);
            if (separator > 0) packageName = packageName[..separator].Trim();
            var network = string.IsNullOrWhiteSpace(PreviewNetworkTextBlock.Text) ? "" : PreviewNetworkTextBlock.Text;
            var cardCanvas = BuildDesignedPrintCard(
                card.CardNumber,
                card.Username ?? string.Empty,
                card.Password,
                card.TimeLimit,
                card.PackageValidity,
                card.UnitPrice.ToString("0.##", CultureInfo.CurrentCulture),
                card.DataLimit,
                network,
                packageName,
                PreviewContactBlock.Text,
                cardIndex);
            return FlattenCardKeepingCredentialsSelectable(cardCanvas);
        }
        finally
        {
            foreach (var pair in originalVisibility) pair.Key.Visibility = pair.Value;
        }
    }

    private Canvas FlattenCardKeepingCredentialsSelectable(Canvas viewport)
    {
        if (viewport.Children.OfType<Canvas>().FirstOrDefault() is not Canvas cardCanvas)
            return viewport;

        var username = cardCanvas.Children.OfType<FrameworkElement>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), nameof(PreviewUsernameTextBlock), StringComparison.Ordinal));
        var password = cardCanvas.Children.OfType<FrameworkElement>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), nameof(PreviewPasswordTextBlock), StringComparison.Ordinal));
        var usernameWasVisible = username?.Visibility == Visibility.Visible;
        var passwordWasVisible = password?.Visibility == Visibility.Visible;
        if (username is not null) username.Visibility = Visibility.Collapsed;
        if (password is not null) password.Visibility = Visibility.Collapsed;

        var width = Math.Max(1, viewport.Width);
        var height = Math.Max(1, viewport.Height);
        viewport.Measure(new Size(width, height));
        viewport.Arrange(new Rect(0, 0, width, height));
        viewport.UpdateLayout();
        const int renderScale = 4;
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(width * renderScale)),
            Math.Max(1, (int)Math.Ceiling(height * renderScale)),
            96d * renderScale, 96d * renderScale, PixelFormats.Pbgra32);
        bitmap.Render(viewport);

        var flattened = new Canvas { Width = width, Height = height, Background = Brushes.Transparent, ClipToBounds = true };
        flattened.Children.Add(new Image { Source = bitmap, Width = width, Height = height, Stretch = Stretch.Fill, IsHitTestVisible = false });
        var credentialLayer = new Canvas
        {
            Width = cardCanvas.Width,
            Height = cardCanvas.Height,
            Background = Brushes.Transparent,
            ClipToBounds = cardCanvas.ClipToBounds,
            RenderTransformOrigin = cardCanvas.RenderTransformOrigin,
            RenderTransform = cardCanvas.RenderTransform?.CloneCurrentValue(),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(credentialLayer, double.IsFinite(Canvas.GetLeft(cardCanvas)) ? Canvas.GetLeft(cardCanvas) : 0);
        Canvas.SetTop(credentialLayer, double.IsFinite(Canvas.GetTop(cardCanvas)) ? Canvas.GetTop(cardCanvas) : 0);

        if (username is not null)
        {
            cardCanvas.Children.Remove(username);
            if (usernameWasVisible) credentialLayer.Children.Add(username);
        }
        if (password is not null)
        {
            cardCanvas.Children.Remove(password);
            if (passwordWasVisible) credentialLayer.Children.Add(password);
        }
        flattened.Children.Add(credentialLayer);
        return flattened;
    }

    private void SaveTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ApplyPropertiesButton_Click(this, new RoutedEventArgs());
            var state = CaptureState();
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StoragePath)!);
            File.WriteAllText(StoragePath, JsonSerializer.Serialize(state, JsonOptions));
            SetStatus($"تم حفظ القالب «{state.TemplateName}» محليًا.", isSuccess: true);
        }
        catch (Exception ex)
        {
            SetStatus("تعذر حفظ القالب: " + ex.Message, isSuccess: false);
        }
    }

    private void NewTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        ResetToDefaults();
        SetStatus("قالب جديد غير محفوظ. لن يتغير القالب المحفوظ إلا عند الضغط على حفظ القالب.", isSuccess: true);
    }

    private void DeleteTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "سيتم حذف نسخة القالب المحفوظة محليًا وإرجاع الشاشة إلى الإعدادات الافتراضية. لا تتأثر الكروت أو بيانات المشروع الأخرى. هل تريد المتابعة؟",
            "حذف القالب المحفوظ",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            if (File.Exists(StoragePath)) File.Delete(StoragePath);
            ResetToDefaults();
            SetStatus("تم حذف القالب المحفوظ وإعادة الإعدادات الافتراضية.", isSuccess: true);
        }
        catch (Exception ex)
        {
            SetStatus("تعذر حذف القالب: " + ex.Message, isSuccess: false);
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

    private void ApplyCardDimensionTransform()
    {
        if (CardDesignCanvas is null || DesignViewportCanvas is null || CardDesignViewbox is null) return;

        var widthMm = ParseDouble(CardWidthTextBox.Text, 66, 10, 200);
        var heightMm = ParseDouble(CardHeightTextBox.Text, 15.5, 10, 200);
        var angle = ParseDouble(CardRotationTextBox.Text, 0, -180, 180);
        var canvasWidth = Math.Max(1, DesignBaseWidth * widthMm / BaseWidthMm);
        var canvasHeight = Math.Max(1, DesignBaseHeight * heightMm / BaseHeightMm);

        // When the physical card changes, remap X/Y positions only. Do not scale each
        // control, border, or font: the user can resize every element independently.
        var oldWidth = Math.Max(1, _lastCanvasWidth);
        var oldHeight = Math.Max(1, _lastCanvasHeight);
        if (Math.Abs(canvasWidth - oldWidth) > 0.1 || Math.Abs(canvasHeight - oldHeight) > 0.1)
        {
            var positionScaleX = canvasWidth / oldWidth;
            var positionScaleY = canvasHeight / oldHeight;
            foreach (var tag in EditableElementTags)
            {
                if (tag is "header" or "divider") continue;
                var element = GetElementByTag(tag);
                if (element is null) continue;
                var left = Canvas.GetLeft(element);
                var top = Canvas.GetTop(element);
                if (!double.IsFinite(left)) left = 0;
                if (!double.IsFinite(top)) top = 0;
                Canvas.SetLeft(element, left * positionScaleX);
                Canvas.SetTop(element, top * positionScaleY);
            }
        }

        CardDesignCanvas.Width = canvasWidth;
        CardDesignCanvas.Height = canvasHeight;
        CardSurfaceBorder.Width = canvasWidth;
        CardSurfaceBorder.Height = canvasHeight;
        TemplateBackgroundImage.Width = canvasWidth;
        TemplateBackgroundImage.Height = canvasHeight;
        // The default card is a long horizontal voucher: a narrow brand panel on the left,
        // with the credentials and package facts arranged in readable rows on the right.
        CardHeaderBorder.Width = Math.Min(canvasWidth, canvasWidth * 86d / LegacyDesignBaseWidth);
        CardHeaderBorder.Height = canvasHeight;
        Canvas.SetLeft(CardHeaderBorder, 0);
        Canvas.SetTop(CardHeaderBorder, 0);
        CardDividerBorder.Width = Math.Max(1, canvasWidth * 310d / LegacyDesignBaseWidth);
        Canvas.SetLeft(CardDividerBorder, canvasWidth * 94d / LegacyDesignBaseWidth);
        var legacyLogicalHeight = LegacyDesignBaseHeight * ParseDouble(CardHeightTextBox.Text, 15.5, 10, 200) / BaseHeightMm;
        Canvas.SetTop(CardDividerBorder, Math.Min(canvasHeight - 2d, canvasHeight * 34d / legacyLogicalHeight));

        var radians = angle * Math.PI / 180d;
        var boundsWidth = Math.Abs(canvasWidth * Math.Cos(radians)) + Math.Abs(canvasHeight * Math.Sin(radians));
        var boundsHeight = Math.Abs(canvasWidth * Math.Sin(radians)) + Math.Abs(canvasHeight * Math.Cos(radians));
        // Keep the preview viewport at the exact physical bounds. The old +4 canvas-unit
        // safety padding made a 66 × 15.5 mm card shrink inside its measured print size.
        boundsWidth = Math.Max(1, boundsWidth);
        boundsHeight = Math.Max(1, boundsHeight);

        // Only rotate the canvas. There is intentionally no ScaleTransform on content.
        CardDesignCanvas.RenderTransformOrigin = new Point(0.5, 0.5);
        CardDesignCanvas.RenderTransform = new RotateTransform(angle);
        DesignViewportCanvas.Width = boundsWidth;
        DesignViewportCanvas.Height = boundsHeight;
        Canvas.SetLeft(CardDesignCanvas, (boundsWidth - canvasWidth) / 2d);
        Canvas.SetTop(CardDesignCanvas, (boundsHeight - canvasHeight) / 2d);

        // Keep editable items inside the resized card without changing their own size.
        foreach (var tag in EditableElementTags)
        {
            var element = GetElementByTag(tag);
            if (element is null) continue;
            var left = Canvas.GetLeft(element);
            var top = Canvas.GetTop(element);
            if (!double.IsFinite(left)) left = 0;
            if (!double.IsFinite(top)) top = 0;
            var clamped = ClampElementPosition(element, left, top);
            Canvas.SetLeft(element, clamped.X);
            Canvas.SetTop(element, clamped.Y);
        }

        _lastCanvasWidth = canvasWidth;
        _lastCanvasHeight = canvasHeight;
        CardDesignViewbox.Width = Math.Max(1, boundsWidth * _zoom);
        CardDesignViewbox.Height = Math.Max(1, boundsHeight * _zoom);
        CardDesignCanvas.ToolTip = $"مقاس القالب: {widthMm:0.#} × {heightMm:0.#} مم؛ الدوران: {angle:0.#}°";
        CanvasSizeTextBlock.Text = $"{widthMm:0.#} × {heightMm:0.#} مم · دوران {angle:0.#}°";
        ZoomPercentageTextBlock.Text = $"{_zoom * 100:0}%";
        UpdateSelectionFrame();
    }

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

    private TemplateDesignState CaptureState() => new()
    {
        Format = TemplateFormatId,
        TemplateName = string.IsNullOrWhiteSpace(TemplateNameTextBox.Text) ? DefaultTemplateName : TemplateNameTextBox.Text.Trim(),
        TemplateStyle = GetSelectedTag(TemplateStyleComboBox) ?? "classic",
        ColorTheme = GetSelectedTag(ColorThemeComboBox) ?? "red",
        FontFamily = GetSelectedTag(FontFamilyComboBox) ?? "Segoe UI",
        FontSize = ParseDouble(ElementFontSizeTextBox.Text, 11, 8, 28),
        CardWidthMm = ParseDouble(CardWidthTextBox.Text, 66, 10, 200),
        CardHeightMm = ParseDouble(CardHeightTextBox.Text, 15.5, 10, 200),
        CardRotationDegrees = ParseDouble(CardRotationTextBox.Text, 0, -180, 180),
        ImportedBackgroundImagePath = _importedTemplateImagePath,
        ImportedBackgroundImageBase64 = GetImportedImageBase64(),
        ImportedBackgroundImageExtension = string.IsNullOrWhiteSpace(_importedTemplateImagePath) ? ".png" : System.IO.Path.GetExtension(_importedTemplateImagePath),
        HotspotPackageIndex = HotspotPackageComboBox.SelectedIndex,
        UserManagerPackageIndex = UserManagerPackageComboBox.SelectedIndex,
        HotspotPackageKey = (HotspotPackageComboBox.SelectedItem as DemoCardPackage)?.Key ?? string.Empty,
        UserManagerPackageKey = (UserManagerPackageComboBox.SelectedItem as DemoCardPackage)?.Key ?? string.Empty,
        ActivePackageSource = _activePackageSource,
        LoginPageUrl = _loginPageUrl,
        BarcodeText = _barcodeText,
        SelectedElementTag = GetSelectedTag(SelectedElementComboBox) ?? "login",
        SelectedElementX = ParseDouble(ElementXTextBox.Text, 20, 0, Math.Max(DesignBaseWidth, CardDesignCanvas.Width)),
        SelectedElementY = ParseDouble(ElementYTextBox.Text, 73, 0, Math.Max(DesignBaseHeight, CardDesignCanvas.Height)),
        SelectedElementText = SelectedElementTextBox.Text,
        SelectedElementBold = BoldElementCheckBox.IsChecked == true,
        ElementProperties = CaptureElementProperties(),
        VisibleElements = new Dictionary<string, bool>(_visibleElements, StringComparer.Ordinal)
    };

    private void LoadSavedTemplate()
    {
        if (!File.Exists(StoragePath)) return;
        try
        {
            var state = JsonSerializer.Deserialize<TemplateDesignState>(File.ReadAllText(StoragePath));
            if (state is not null) ApplyTemplateState(state);
        }
        catch (Exception ex)
        {
            SetStatus("تعذر قراءة القالب المحفوظ؛ سيتم استخدام الإعدادات الافتراضية. " + ex.Message, isSuccess: false);
        }
    }

    private void ApplyTemplateState(TemplateDesignState state)
    {
        // Migrate only the old built-in 60x35 mm Default_Design to the new voucher geometry.
        // Custom named templates keep their dimensions; the existing JSON is not rewritten
        // until the user deliberately saves the updated default template.
        var isOldBuiltInDefault = string.Equals(state.TemplateName, DefaultTemplateName, StringComparison.OrdinalIgnoreCase)
            && Math.Abs(state.CardWidthMm - 60d) < 0.1d
            && Math.Abs(state.CardHeightMm - 35d) < 0.1d;
        var requiresDipCoordinateMigration = !string.Equals(state.Format, TemplateFormatId, StringComparison.Ordinal);
        if (isOldBuiltInDefault)
        {
            var newProperties = CreateDefaultElementProperties();
            if (state.ElementProperties is not null)
            {
                foreach (var pair in newProperties)
                {
                    if (!state.ElementProperties.TryGetValue(pair.Key, out var oldProperty) || oldProperty is null)
                        continue;
                    // Preserve user-authored text/font family/bold preference while replacing
                    // the old card geometry and small legacy placements with the readable voucher layout.
                    if (oldProperty.Text is not null) pair.Value.Text = oldProperty.Text;
                    if (!string.IsNullOrWhiteSpace(oldProperty.FontFamily)) pair.Value.FontFamily = oldProperty.FontFamily;
                    if (pair.Key is "login" or "price" or "validity") pair.Value.IsBold = oldProperty.IsBold || pair.Value.IsBold;
                }
            }
            state.ElementProperties = newProperties;
            if (state.VisibleElements is not null)
                state.VisibleElements["رمز QR"] = false;
            state.CardWidthMm = 66d;
            state.CardHeightMm = 15.5d;
            state.SelectedElementX *= LegacyToDipScale;
            state.SelectedElementY *= LegacyToDipScale;
            state.Format = TemplateFormatId;
        }
        else if (requiresDipCoordinateMigration)
        {
            // V1 stored base-canvas positions; V2 stored positions after sizing the canvas.
            // Convert the legacy geometry and font sizes to DIPs exactly once, keeping the
            // old card-size position ratios for V1 while preventing the whole card shrinking text.
            var isV2PhysicalCanvas = string.Equals(state.Format, "TabCard.TemplateDesigner.v2", StringComparison.Ordinal);
            var legacyPositionScaleX = LegacyToDipScale * (isV2PhysicalCanvas ? 1d : state.CardWidthMm / BaseWidthMm);
            var legacyPositionScaleY = LegacyToDipScale * (isV2PhysicalCanvas ? 1d : state.CardHeightMm / BaseHeightMm);
            if (state.ElementProperties is not null)
            {
                foreach (var element in state.ElementProperties.Values.Where(value => value is not null))
                {
                    element.X *= legacyPositionScaleX;
                    element.Y *= legacyPositionScaleY;
                    element.Width *= LegacyToDipScale;
                    element.Height *= LegacyToDipScale;
                    element.FontSize *= LegacyToDipScale;
                }
            }
            state.SelectedElementX *= legacyPositionScaleX;
            state.SelectedElementY *= legacyPositionScaleY;
            state.Format = TemplateFormatId;
        }

        var isLegacyCoordinateState = !string.Equals(state.Format, TemplateFormatId, StringComparison.Ordinal);
        TemplateNameTextBox.Text = string.IsNullOrWhiteSpace(state.TemplateName) ? DefaultTemplateName : state.TemplateName;
        SelectComboTag(TemplateStyleComboBox, state.TemplateStyle);
        SelectComboTag(ColorThemeComboBox, state.ColorTheme);
        SelectComboTag(FontFamilyComboBox, state.FontFamily);
        ElementFontSizeTextBox.Text = state.FontSize.ToString("0.#", CultureInfo.InvariantCulture);
        CardWidthTextBox.Text = state.CardWidthMm.ToString("0.#", CultureInfo.InvariantCulture);
        CardHeightTextBox.Text = state.CardHeightMm.ToString("0.#", CultureInfo.InvariantCulture);
        CardRotationTextBox.Text = state.CardRotationDegrees.ToString("0.#", CultureInfo.InvariantCulture);
        var savedHotSpotPackage = HotspotPackageComboBox.Items.OfType<DemoCardPackage>()
            .FirstOrDefault(item => string.Equals(item.Key, state.HotspotPackageKey, StringComparison.OrdinalIgnoreCase));
        if (savedHotSpotPackage is not null)
            HotspotPackageComboBox.SelectedItem = savedHotSpotPackage;
        else if (state.HotspotPackageIndex >= 0 && state.HotspotPackageIndex < HotspotPackageComboBox.Items.Count)
            HotspotPackageComboBox.SelectedIndex = state.HotspotPackageIndex;

        var savedUserManagerPackage = UserManagerPackageComboBox.Items.OfType<DemoCardPackage>()
            .FirstOrDefault(item => string.Equals(item.Key, state.UserManagerPackageKey, StringComparison.OrdinalIgnoreCase));
        if (savedUserManagerPackage is not null)
            UserManagerPackageComboBox.SelectedItem = savedUserManagerPackage;
        else if (state.UserManagerPackageIndex >= 0 && state.UserManagerPackageIndex < UserManagerPackageComboBox.Items.Count)
            UserManagerPackageComboBox.SelectedIndex = state.UserManagerPackageIndex;
        _activePackageSource = state.ActivePackageSource == "user-manager" ? "user-manager" : "hotspot";
        _loginPageUrl = string.IsNullOrWhiteSpace(state.LoginPageUrl) ? DefaultLoginUrl : state.LoginPageUrl;
        _barcodeText = state.BarcodeText ?? "50723015";

        if (state.VisibleElements is not null)
        {
            _visibleElements.Clear();
            foreach (var item in CreateDefaultVisibility()) _visibleElements[item.Key] = item.Value;
            foreach (var item in state.VisibleElements) _visibleElements[item.Key] = item.Value;
        }
        else
        {
            _visibleElements.Clear();
            foreach (var item in CreateDefaultVisibility()) _visibleElements[item.Key] = item.Value;
        }

        _importedTemplateImagePath = RestoreBackgroundFromState(state);
        if (!string.IsNullOrWhiteSpace(_importedTemplateImagePath) && File.Exists(_importedTemplateImagePath))
            LoadBackgroundImageFromPath(_importedTemplateImagePath);
        else
            TemplateBackgroundImage.Source = null;
        ApplyImportedBackgroundAppearance();

        ApplyElementProperties(CreateDefaultElementProperties());
        if (state.ElementProperties is { Count: > 0 }) ApplyElementProperties(state.ElementProperties);
        SelectComboTag(SelectedElementComboBox, state.SelectedElementTag ?? "login");
        if (state.ElementProperties is not { Count: > 0 })
        {
            if (GetSelectedElement() is FrameworkElement selected)
            {
                Canvas.SetLeft(selected, Math.Clamp(state.SelectedElementX, 0, DesignBaseWidth * 200d / BaseWidthMm));
                Canvas.SetTop(selected, Math.Clamp(state.SelectedElementY, 0, DesignBaseHeight * 200d / BaseHeightMm));
                if (selected is TextBlock tb && state.SelectedElementText is not null) tb.Text = state.SelectedElementText;
                if (selected is TextBlock textBlock) textBlock.FontWeight = state.SelectedElementBold ? FontWeights.Bold : FontWeights.Normal;
            }
        }
        UpdateSamplePackageFields();
        SyncMainElementChecks();
        var targetCanvasWidth = DesignBaseWidth * ParseDouble(CardWidthTextBox.Text, 66, 10, 200) / BaseWidthMm;
        var targetCanvasHeight = DesignBaseHeight * ParseDouble(CardHeightTextBox.Text, 15.5, 10, 200) / BaseHeightMm;
        // V2 state stores coordinates in the actual canvas. Legacy V1 state stores base-canvas
        // coordinates, so let ApplyCardDimensionTransform map those positions once on migration.
        if (!isLegacyCoordinateState)
        {
            _lastCanvasWidth = targetCanvasWidth;
            _lastCanvasHeight = targetCanvasHeight;
        }
        ApplyCardDimensionTransform();
        UpdatePreview();
        SyncSelectedElementEditor();
    }

    private void ResetToDefaults()
    {
        _visibleElements.Clear();
        foreach (var pair in CreateDefaultVisibility()) _visibleElements[pair.Key] = pair.Value;
        _importedTemplateImagePath = null;
        TemplateBackgroundImage.Source = null;
        TemplateBackgroundImage.Visibility = Visibility.Collapsed;
        TemplateNameTextBox.Text = DefaultTemplateName;
        SelectComboTag(TemplateStyleComboBox, "classic");
        SelectComboTag(ColorThemeComboBox, "red");
        SelectComboTag(FontFamilyComboBox, "Segoe UI");
        ElementFontSizeTextBox.Text = "11";
        CardWidthTextBox.Text = "66";
        CardHeightTextBox.Text = "15.5";
        CardRotationTextBox.Text = "0";
        _lastCanvasWidth = DesignBaseWidth * 66d / BaseWidthMm;
        _lastCanvasHeight = DesignBaseHeight * 15.5d / BaseHeightMm;
        ApplyElementProperties(CreateDefaultElementProperties());
        ApplyCardDimensionTransform();
        _zoom = 1d;
        ZoomSlider.Value = 1d;
        HotspotPackageComboBox.SelectedIndex = 0;
        UserManagerPackageComboBox.SelectedIndex = 0;
        _activePackageSource = "hotspot";
        _loginPageUrl = DefaultLoginUrl;
        _barcodeText = "50723015";
        ApplyElementProperties(CreateDefaultElementProperties());
        SelectComboTag(SelectedElementComboBox, "login");
        UpdateSamplePackageFields();
        SyncSelectedElementEditor();
        SyncMainElementChecks();
        ApplyImportedBackgroundAppearance();
        ApplyCardDimensionTransform();
        UpdatePreview();
    }

    private void SetStatus(string message, bool isSuccess)
    {
        if (TemplateStatusTextBlock is null) return;
        TemplateStatusTextBlock.Text = message;
        TemplateStatusTextBlock.Foreground = (Brush)FindResource(isSuccess ? "SuccessBrush" : "DangerBrush");
    }

    private Window CreateDialog(string title, double width, double height)
    {
        var window = new Window
        {
            Title = title,
            Width = width,
            Height = height,
            MinWidth = Math.Min(width, 620),
            MinHeight = Math.Min(height, 460),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            FlowDirection = FlowDirection.RightToLeft,
            FontFamily = new FontFamily("Segoe UI"),
            Background = (Brush)FindResource("AppBgBrush"),
            Owner = Window.GetWindow(this)
        };
        return window;
    }

    private StackPanel BuildLabeledField(string caption, Control control)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = caption, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(control);
        return panel;
    }

    private StackPanel BuildDialogActions(Window dialog, string confirmText, string cancelText)
    {
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        var confirm = MakeButton(confirmText, primary: true);
        confirm.MinWidth = 140;
        confirm.Click += (_, _) => dialog.DialogResult = true;
        var cancel = MakeButton(cancelText, primary: false);
        cancel.MinWidth = 100;
        cancel.Margin = new Thickness(8, 0, 0, 0);
        cancel.Click += (_, _) => dialog.DialogResult = false;
        actions.Children.Add(confirm);
        actions.Children.Add(cancel);
        return actions;
    }

    private Button MakeButton(string text, bool primary)
    {
        var button = new Button
        {
            Content = text,
            Style = (Style)FindResource(primary ? "PrimaryButton" : "SecondaryButton"),
            Height = 36,
            Padding = new Thickness(14, 0, 14, 0),
            MinWidth = 90
        };
        return button;
    }

    private TextBox AddTextField(Panel host, string label, string initialValue)
    {
        var panel = new StackPanel { Margin = new Thickness(5, 4, 5, 7) };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        var textBox = new TextBox { Text = initialValue, Height = 32, Padding = new Thickness(7, 4, 7, 4), FontSize = 11.5 };
        panel.Children.Add(textBox);
        host.Children.Add(panel);
        return textBox;
    }

    private static int ParseInt(string? text, int fallback, int min, int max)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value);
        if (value == 0 && string.IsNullOrWhiteSpace(text)) value = fallback;
        return Math.Clamp(value, min, max);
    }

    private static long ParseLong(string? text, long fallback)
    {
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) return value;
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value)) return value;
        return fallback;
    }

    private static double ParseDouble(string? text, double fallback, double min, double max)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
            !double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            value = fallback;
        if (!double.IsFinite(value)) value = fallback;
        return Math.Clamp(value, min, max);
    }

    private static string? GetSelectedTag(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString();

    private static void SelectComboTag(ComboBox combo, string? tag)
    {
        for (var i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem item && string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = i;
                return;
            }
        }
    }

    private sealed class TemplateDesignState
    {
        public TemplateDesignState() { }

        public string Format { get; set; } = TemplateFormatId;
        public string? ImportedBackgroundImagePath { get; set; }
        public string? ImportedBackgroundImageBase64 { get; set; }
        public string ImportedBackgroundImageExtension { get; set; } = ".png";
        public double CardRotationDegrees { get; set; }
        public string TemplateName { get; set; } = DefaultTemplateName;
        public string TemplateStyle { get; set; } = "classic";
        public string ColorTheme { get; set; } = "red";
        public string FontFamily { get; set; } = "Segoe UI";
        public double FontSize { get; set; } = 11;
        public double CardWidthMm { get; set; } = 66;
        public double CardHeightMm { get; set; } = 15.5;
        public int HotspotPackageIndex { get; set; } = 0;
        public int UserManagerPackageIndex { get; set; } = 0;
        public string HotspotPackageKey { get; set; } = string.Empty;
        public string UserManagerPackageKey { get; set; } = string.Empty;
        public string ActivePackageSource { get; set; } = "hotspot";
        public string LoginPageUrl { get; set; } = DefaultLoginUrl;
        public string BarcodeText { get; set; } = "50723015";
        public string SelectedElementTag { get; set; } = "login";
        public double SelectedElementX { get; set; } = 20;
        public double SelectedElementY { get; set; } = 73;
        public string? SelectedElementText { get; set; } = "50723015";
        public bool SelectedElementBold { get; set; } = true;
        public Dictionary<string, ElementDesignProperties> ElementProperties { get; set; } = new(StringComparer.Ordinal);
        public Dictionary<string, bool> VisibleElements { get; set; } = CreateDefaultVisibility();
    }

    private sealed class ElementDesignProperties
    {
        public ElementDesignProperties() { }

        public double X { get; set; }
        public double Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double RotationDegrees { get; set; }
        public string? Text { get; set; }
        public bool IsBold { get; set; }
        public double FontSize { get; set; } = 11;
        public string FontFamily { get; set; } = "Segoe UI";
    }
}
