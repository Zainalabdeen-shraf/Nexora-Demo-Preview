using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using Microsoft.Win32;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Shared;

namespace Nexora.Demo.Desktop.Features.Cards.Views;

public partial class ImportCardsView : UserControl
{
    private const int MaxImportedCards = 5000;

    private readonly DemoCardIssuanceService _issuanceService;
    private readonly DemoAccountSystem _targetSystem;
    private List<ImportPreviewRow> _previewRows = [];

    public ImportCardsView(
        DemoCardIssuanceService issuanceService,
        DemoAccountSystem targetSystem)
    {
        ArgumentNullException.ThrowIfNull(issuanceService);

        _issuanceService = issuanceService;
        _targetSystem = targetSystem;

        InitializeComponent();

        TargetSystemTextBlock.Text = targetSystem == DemoAccountSystem.HotSpot
            ? "النظام المحدد: هوتسبوت"
            : "النظام المحدد: يوزر منجر";

        BatchIdTextBox.Text = $"IMP-{DateTime.Now:yyMMdd-HHmmss}";
        ManagerAccountTextBox.Text = targetSystem == DemoAccountSystem.UserManager
            ? "admin"
            : "غير منطبق";

        try
        {
            PackageComboBox.ItemsSource = DemoCardPackageCatalog.Load(targetSystem);
            PackageComboBox.SelectedIndex = 0;

            if (PackageComboBox.SelectedItem is null)
                SetStatus("لا توجد باقات متاحة للنظام المحدد.", true);
        }
        catch (Exception ex)
        {
            SetStatus("تعذر تحميل الباقات: " + ex.Message, true);
        }
    }


    private sealed record ImportPreviewRow(
        int RowNumber,
        string CardNumber,
        string ValidationStatus);
}