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
    public string? SelectedTemplatePath { get; private set; }
    private readonly Dictionary<string, bool> _visibleElements = CreateDefaultVisibility();
    private bool _isReady;
    private string _loginPageUrl = DefaultLoginUrl;
    private string _barcodeText = "50723015";
    private string _activePackageSource = "hotspot";
    private string? _packageCatalogLoadError;
    private string? _importedTemplateImagePath;
    private string? _rasterTemplateImagePath;
    private bool _isRasterTemplateMode;
    private bool _isLoadingTemplateLibrary;
    private string? _studioSelectedFieldTag = "username";
    private bool _isSyncingElementOpacity;
    private bool _isResizingElement;
    private FrameworkElement? _resizeTarget;
    private string? _activeResizeHandle;
    private double _resizeStartWidth;
    private double _resizeStartHeight;
    private double _resizeStartFontSize;
    private readonly List<Thumb> _resizeHandles = new();
    private readonly Dictionary<string, TextBlock> _customElements = new(StringComparer.Ordinal);
    private int _customElementCounter;
    private FrameworkElement? _draggingElement;
    private Point _dragStartPoint;
    private double _dragStartLeft;
    private double _dragStartTop;
    private double _zoom = 1d;
    private double _fitZoom = 1d;
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
        "اسم المستخدم", "كلمة المرور", "الصلاحية", "الوقت", "السعر",
        "التحميل", "الباقة", "الرقم التسلسلي", "تاريخ الطباعة", "رقم الدفعة", "نقطة البيع",
        "معلومات إضافية", "أرقام التواصل", "كود المكافأة", "رمز QR", "الباركود", "شعار الشبكة", "الشريط العلوي", "الفاصل"
    ];


    public void ApplyIssuanceContext(DemoAccountSystem targetSystem, string packageKey)
{
    _activePackageSource = targetSystem == DemoAccountSystem.UserManager
        ? "user-manager"
        : "hotspot";

    var combo = _activePackageSource == "user-manager"
        ? UserManagerPackageComboBox
        : HotspotPackageComboBox;

    if (!string.IsNullOrWhiteSpace(packageKey))
    {
        var matchingPackage = combo.Items.OfType<DemoCardPackage>()
            .FirstOrDefault(package =>
                string.Equals(package.Key, packageKey, StringComparison.Ordinal));

        if (matchingPackage is not null)
            combo.SelectedItem = matchingPackage;
    }

    UpdateSamplePackageFields();
    SyncMainElementChecks();
    SyncSelectedElementEditor();
    UpdatePreview();
}


    public TemplateDesignerView()
    {
        InitializeComponent();
        InitializeElementOptions();
        LoadPackageOptions();
        _isReady = true;
        // One complete set of previews is rendered after state and template-library restoration.
        _isStudioPreviewRendering = true;
        LoadSavedTemplate();
        if (!File.Exists(StoragePath))
            ResetToDefaults();
        LoadTemplateLibrary();
        UpdateSamplePackageFields();
        SyncMainElementChecks();
        _isStudioPreviewRendering = false;
        UpdatePreview();
        if (TemplateStatusPopup is not null) TemplateStatusPopup.IsOpen = false;
        var credentialLayoutRepaired = RepairCredentialLayoutIfNeeded();
        if (credentialLayoutRepaired) UpdatePreview();
        SyncSelectedElementEditor();
        SetStatus(
            !string.IsNullOrWhiteSpace(_packageCatalogLoadError)
                ? _packageCatalogLoadError
                : credentialLayoutRepaired
                    ? "تم فصل اسم المستخدم وكلمة المرور ومنع تداخل النصوص. احفظ القالب للاحتفاظ بالتخطيط."
                    : (File.Exists(StoragePath) ? "تم تحميل القالب المحفوظ وربط الباقات الفعلية." : "قالب افتراضي جاهز للتصميم ومربوط بكتالوج الباقات."),
            isSuccess: string.IsNullOrWhiteSpace(_packageCatalogLoadError));
    }



    private bool RepairCredentialLayoutIfNeeded()
    {
        if (CardDesignCanvas is null || PreviewUsernameTextBlock is null || PreviewPasswordTextBlock is null) return false;
        var username = PreviewUsernameTextBlock;
        var password = PreviewPasswordTextBlock;
        var ux = Canvas.GetLeft(username); var uy = Canvas.GetTop(username);
        var px = Canvas.GetLeft(password); var py = Canvas.GetTop(password);
        if (!double.IsFinite(ux)) ux = 0; if (!double.IsFinite(uy)) uy = 0;
        if (!double.IsFinite(px)) px = 0; if (!double.IsFinite(py)) py = 0;
        var uw = Math.Max(1, GetElementWidth(username)); var uh = Math.Max(1, GetElementHeight(username));
        var pw = Math.Max(1, GetElementWidth(password)); var ph = Math.Max(1, GetElementHeight(password));
        var canvasWidth = Math.Max(1, CardDesignCanvas.Width > 1 ? CardDesignCanvas.Width : DesignBaseWidth);
        var canvasHeight = Math.Max(1, CardDesignCanvas.Height > 1 ? CardDesignCanvas.Height : DesignBaseHeight);
        var overlaps = ux < px + pw && ux + uw > px && uy < py + ph && uy + uh > py;
        var rowsTooClose = Math.Abs(uy - py) < Math.Max(uh, ph) * 1.1;
        var credentialsClusteredOnLeft = ux < canvasWidth * 0.35 && px < canvasWidth * 0.35
            && uw < canvasWidth * 0.4 && pw < canvasWidth * 0.4;
        var duplicatePasswordLabel = password.Text.StartsWith("كلمة المرور", StringComparison.OrdinalIgnoreCase)
            || password.Text.StartsWith("كلمة السر", StringComparison.OrdinalIgnoreCase);
        var duplicateUsernameLabel = username.Text.StartsWith("اسم المستخدم", StringComparison.OrdinalIgnoreCase)
            || username.Text.StartsWith("رقم الكرت", StringComparison.OrdinalIgnoreCase);
        var directionWrong = username.FlowDirection != FlowDirection.LeftToRight || password.FlowDirection != FlowDirection.LeftToRight;
        if (!overlaps && !rowsTooClose && !credentialsClusteredOnLeft && !duplicatePasswordLabel && !duplicateUsernameLabel && !directionWrong) return false;

        var width = Math.Clamp(canvasWidth * 0.38, 54, canvasWidth * 0.44);
        var left = Math.Clamp(canvasWidth * 0.53, 0, Math.Max(0, canvasWidth - width - 4));
                var maxElementHeight = Math.Max(1d, canvasHeight * 0.23);
        var minElementHeight = Math.Min(16d, maxElementHeight);
        var height = Math.Clamp(canvasHeight * 0.18, minElementHeight, maxElementHeight);
        Canvas.SetLeft(username, left);
        Canvas.SetTop(username, canvasHeight * 0.20);
        username.Width = width; username.Height = height;
        username.Text = NormalizeUsernameText(username.Text);
        username.TextAlignment = TextAlignment.Right;
        username.FlowDirection = FlowDirection.LeftToRight;
        username.FontFamily = new FontFamily("Segoe UI");
        username.FontWeight = FontWeights.Bold;
        username.FontSize = Math.Max(username.FontSize, 11.5);

        Canvas.SetLeft(password, left);
        Canvas.SetTop(password, canvasHeight * 0.59);
        password.Width = width; password.Height = height;
        password.Text = NormalizePasswordText(password.Text);
        password.TextAlignment = TextAlignment.Right;
        password.FlowDirection = FlowDirection.LeftToRight;
        password.FontFamily = new FontFamily("Segoe UI");
        password.FontWeight = FontWeights.Bold;
        password.FontSize = Math.Max(password.FontSize, 10.5);
        PreviewUsernameCaptionTextBlock.FontSize = Math.Max(PreviewUsernameCaptionTextBlock.FontSize, 7.2);
        PreviewUsernameCaptionTextBlock.FontWeight = FontWeights.Bold;
        PreviewPasswordCaptionTextBlock.FontSize = Math.Max(PreviewPasswordCaptionTextBlock.FontSize, 7.2);
        PreviewPasswordCaptionTextBlock.FontWeight = FontWeights.Bold;
        KeepUsernameCaptionAligned();
        KeepPasswordCaptionAligned();
        return true;
    }

    private static string StoragePath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexoraProductDemo", "templates", "tabcard-template-design.json");

    private static string TemplateLibraryDirectory => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexoraProductDemo", "templates", "library");

    private static string TemplateAssetsDirectory => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexoraProductDemo", "templates", "assets");

    private static string DefaultStarterTemplateImagePath => System.IO.Path.Combine(
        AppContext.BaseDirectory, "Assets", "Printing", "TabCard-Modern-Blue-Wave-Background.png");

    // Every design object stays as an editable layer after saving. The background
    // renderer hides these layers temporarily, exporting only the reusable artwork.
    private static readonly string[] DynamicFieldTags = EditableElementTags;

    private sealed class SavedTemplateEntry
    {
        public string Name { get; init; } = string.Empty;
        public string Path { get; init; } = string.Empty;
        public override string ToString() => Name;
    }


    private sealed class TemplateDesignState
    {
        public TemplateDesignState() { }

        public string Format { get; set; } = TemplateFormatId;
        public string? ImportedBackgroundImagePath { get; set; }
        public string? ImportedBackgroundImageBase64 { get; set; }
        public string ImportedBackgroundImageExtension { get; set; } = ".png";
        public bool IsRasterTemplateMode { get; set; }
        public string? RasterTemplateImagePath { get; set; }
        public string? RasterTemplateImageBase64 { get; set; }
        public string LinkedPackageKey { get; set; } = string.Empty;
        public double CardRotationDegrees { get; set; }
        public string TemplateName { get; set; } = DefaultTemplateName;
        public string TemplateStyle { get; set; } = "classic";
        public string ColorTheme { get; set; } = "blue";
        public string FontFamily { get; set; } = "Segoe UI";
        public double FontSize { get; set; } = 11;
        public double CardWidthMm { get; set; } = 92.1;
        public double CardHeightMm { get; set; } = 52;
        public int PrintColumns { get; set; } = 2;
        public int PrintRows { get; set; } = 5;
        public double PageMarginTopMm { get; set; } = 10;
        public double PageMarginBottomMm { get; set; } = 10;
        public double PageMarginLeftMm { get; set; } = 10;
        public double PageMarginRightMm { get; set; } = 10;
        // Bumped when the built-in Default_Design receives a new readable card layout.
        // Missing values in older JSON intentionally deserialize as revision 0.
        public int LayoutRevision { get; set; }
        public int HotspotPackageIndex { get; set; } = 0;
        public int UserManagerPackageIndex { get; set; } = 0;
        public string HotspotPackageKey { get; set; } = string.Empty;
        public string UserManagerPackageKey { get; set; } = string.Empty;
        public string ActivePackageSource { get; set; } = "hotspot";
        public string LoginPageUrl { get; set; } = DefaultLoginUrl;
        public string BarcodeText { get; set; } = "50723015";
        public string SelectedElementTag { get; set; } = "username";
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
        // Missing in older saved templates defaults to true for backward compatibility.
        public bool IsVisible { get; set; } = true;
        public string? Text { get; set; }
        public bool IsBold { get; set; }
        public bool IsItalic { get; set; }
        public double Opacity { get; set; } = 1d;
        public string? ForegroundHex { get; set; }
        public string? BackgroundHex { get; set; }
        public double FontSize { get; set; } = 11;
        public string FontFamily { get; set; } = "Segoe UI";
        // Optional for backward compatibility: older templates omit this property.
        public string? TextAlignment { get; set; }
        public bool HasBorder { get; set; }
        public string BorderHex { get; set; } = "#C51D2E";
        public double BorderThickness { get; set; } = 1.2;
    }

    private string[] GetAllEditableElementTags() => EditableElementTags.Concat(_customElements.Keys).ToArray();

    private bool IsDynamicElementTag(string tag) =>
        DynamicFieldTags.Contains(tag, StringComparer.Ordinal) || tag.StartsWith("custom-", StringComparison.Ordinal);

    private void RestoreCustomElements(Dictionary<string, ElementDesignProperties>? properties)
    {
        foreach (var element in _customElements.Values)
            CardDesignCanvas.Children.Remove(element);
        _customElements.Clear();
        var elementSelector = SelectedElementComboBox;
        if (elementSelector is not null)
        {
            foreach (var item in elementSelector.Items.OfType<ComboBoxItem>()
                         .Where(item => item.Tag?.ToString()?.StartsWith("custom-", StringComparison.Ordinal) == true).ToList())
                elementSelector.Items.Remove(item);
        }
        _customElementCounter = 0;

        if (properties is null) return;
        foreach (var pair in properties.Where(pair => pair.Key.StartsWith("custom-", StringComparison.Ordinal)))
        {
            var tag = pair.Key;
            var element = new TextBlock
            {
                Tag = tag,
                Name = string.Empty,
                Text = pair.Value?.Text ?? string.Empty,
                FontFamily = new FontFamily(string.IsNullOrWhiteSpace(pair.Value?.FontFamily) ? "Segoe UI" : pair.Value!.FontFamily),
                FontSize = pair.Value?.FontSize is >= 4 and <= 60 ? pair.Value.FontSize : 8,
                Foreground = Brushes.Black,
                Background = new SolidColorBrush(Color.FromRgb(255, 231, 234)),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                FlowDirection = FlowDirection.RightToLeft,
                Padding = new Thickness(2),
                IsHitTestVisible = true
            };
            _customElements[tag] = element;
            CardDesignCanvas.Children.Add(element);
            SelectedElementComboBox.Items.Add(new ComboBoxItem
            {
                Content = "حقل مخصص " + tag["custom-".Length..],
                Tag = tag
            });
            if (int.TryParse(tag["custom-".Length..], out var number))
                _customElementCounter = Math.Max(_customElementCounter, number);
        }
    }

}
