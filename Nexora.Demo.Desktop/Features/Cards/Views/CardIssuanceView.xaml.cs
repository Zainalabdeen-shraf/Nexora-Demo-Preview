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


public partial class CardIssuanceView : UserControl

{

    private readonly DemoCardIssuanceService _issuanceService;

    private readonly DemoAccountSystem _targetSystem;

    private readonly DemoCardWorkflowStore _workflowStore = new();
    private string _designerTemplateName = "القالب الافتراضي";
    private string _designerTemplateStyle = "classic";
    private string _designerColorTheme = "red";
    private readonly Dictionary<string, bool> _templateVisibleElements = CreateDefaultTemplateVisibility();

    private static string TemplateDesignStoragePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexoraProductDemo", "templates", "tabcard-template-design.json");


    private ListCollectionView? _recentBatchesView;

    private bool _isApplyingPreferences;

    private string? _lastGeneratedBatchId;

    private List<GeneratedDemoCard> _lastGeneratedCards = [];

    private GeneratedDemoCard? _previewCard;

    private bool _isSubscribed;

    private bool _workflowRunning;
    private Nexora.Demo.Desktop.Features.Printing.Views.TemplateDesignerView? _selectedTemplateRenderer;
    private string? _selectedTemplateRendererPath;
    private DateTime _selectedTemplateRendererLastWriteUtc = DateTime.MinValue;
    private string? _activeTemplatePath;
    private bool _isApplyingPreviousBatch;

    private string _printPaperSize = "A4";

    private int _printColumns = 3;

    private bool _printCutMarks = true;


    public CardIssuanceView(DemoCardIssuanceService issuanceService, DemoAccountSystem targetSystem)

    {

        InitializeComponent();
        LoadDesignerTemplateDefinition();

        _issuanceService = issuanceService;

        _targetSystem = targetSystem;
        CardNumberPatternLabelTextBlock.Text = "طول التسلسل ونمط اسم المستخدم";
        PreviewSourceTypeTextBlock.Text = targetSystem == DemoAccountSystem.UserManager
            ? "مدير المستخدمين"
            : "نقطة الاتصال";
        CardNumberLengthTextBox.ToolTip = "يحدد طول الرقم التسلسلي؛ يُنشأ اسم المستخدم منفصلًا وبشكل فريد.";

        _recentBatchesView = new ListCollectionView(_issuanceService.Batches) { Filter = MatchRecentBatch };

        RecentBatchesGrid.ItemsSource = _recentBatchesView;


        ManagerUserPanel.Visibility = targetSystem == DemoAccountSystem.UserManager

            ? Visibility.Visible : Visibility.Collapsed;
        try

        {

            RefreshPackageCatalog(preserveSelection: false);

        }

        catch (Exception ex)

        {

            PackageComboBox.ItemsSource = Array.Empty<DemoCardPackage>();

            SetStatus("تعذر تحميل الباقات: " + ex.Message, true);

        }


        LoadPersistedOptions();

        LoadPersistedPreferences();

        if (string.IsNullOrWhiteSpace(CardPrefixTextBox.Text))
            CardPrefixTextBox.Text = targetSystem == DemoAccountSystem.HotSpot ? "HP-" : "UM-";

        ManagerUserPanel.Visibility = targetSystem == DemoAccountSystem.UserManager ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetRow(PasswordGenerationPanel, 1);
        Grid.SetRow(CredentialIdentityPanel, 2);
        PasswordModeLabelTextBlock.Text = targetSystem == DemoAccountSystem.UserManager ? "صيغة الكرت" : "صيغة الدخول";

        UpdatePasswordControls();

        RefreshExistingBatches();

        RefreshRecentBatchesEmptyState();

        ApplyPrintTemplate();


        Loaded += CardIssuanceView_Loaded;

        Unloaded += CardIssuanceView_Unloaded;

    }


    public event EventHandler<FeatureNavigationRequestedEventArgs>? NavigationRequested;


    private void SetStatus(string message, bool isError)

    {

        if (IssuanceStatusTextBlock is null)

            return;

        IssuanceStatusTextBlock.Text = message;

        IssuanceStatusTextBlock.Foreground = new SolidColorBrush(isError ? Color.FromRgb(180, 35, 24) : Color.FromRgb(22, 138, 91));

    }

}

/// <summary>Displays a one-based row number in the local batch history table.</summary>
public sealed class OneBasedAlternationIndexConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int index ? index + 1 : 1;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
