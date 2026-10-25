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


    private static string StoragePath => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexoraProductDemo", "templates", "tabcard-template-design.json");


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
