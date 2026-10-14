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

    private void BrowseFileButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "اختيار ملف أرقام الكروت",
            Filter = "ملفات الكروت (*.xlsx;*.csv;*.txt)|*.xlsx;*.csv;*.txt",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;

        FilePathTextBox.Text = dialog.FileName;
        LoadPreview();
    }

    private void PreviewFileButton_Click(object sender, RoutedEventArgs e)
        => LoadPreview();

    private void PackageComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (PackageComboBox.SelectedItem is not DemoCardPackage package)
            return;

        RouterProfileTextBox.Text = _targetSystem == DemoAccountSystem.HotSpot
            ? package.HotSpotProfile
            : package.UserManagerProfile;
    }

    private void LoadPreview()
    {
        var path = FilePathTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            ClearPreview();
            SetStatus("اختر ملفًا موجودًا أولًا.", true);
            return;
        }

        try
        {
            var numbers = ReadCardNumbersFromFile(path);

            if (numbers.Count == 0)
            {
                ClearPreview();
                SetStatus("لم يتم العثور على أرقام كروت في العمود الأول.", true);
                return;
            }

            if (numbers.Count > MaxImportedCards)
            {
                ClearPreview();
                SetStatus(
                    $"الحد الأعلى للاستيراد في العملية الواحدة هو {MaxImportedCards:N0} كرت.",
                    true);
                return;
            }

            BuildPreview(numbers);
        }
        catch (Exception ex)
        {
            ClearPreview();
            SetStatus("تعذر قراءة الملف: " + ex.Message, true);
        }
    }

    private void BuildPreview(IReadOnlyList<string> numbers)
    {
        var targetName = _targetSystem == DemoAccountSystem.HotSpot
            ? "مستخدمو الهوتسبوت"
            : "مدير المستخدمين";

        var duplicateNumbers = numbers
            .GroupBy(number => number, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existingNumbers = _issuanceService.Cards
            .Where(card => string.Equals(
                card.TargetSystem, targetName, StringComparison.Ordinal))
            .Select(card => card.CardNumber)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (_targetSystem == DemoAccountSystem.UserManager)
        {
            existingNumbers.UnionWith(
                _issuanceService.Cards
                    .Where(card => string.Equals(
                        card.TargetSystem, targetName, StringComparison.Ordinal))
                    .Select(card => card.Username));
        }

        _previewRows = numbers.Select((number, index) =>
        {
            var status = duplicateNumbers.Contains(number)
                ? "مكرر في الملف"
                : existingNumbers.Contains(number)
                    ? "موجود مسبقًا"
                    : "جاهز";

            return new ImportPreviewRow(index + 1, number, status);
        }).ToList();

        PreviewGrid.ItemsSource = _previewRows;

        var readyCount = _previewRows.Count(row => row.ValidationStatus == "جاهز");
        var problemCount = _previewRows.Count - readyCount;

        PreviewCountTextBlock.Text =
            $"{_previewRows.Count:N0} رقم — جاهز: {readyCount:N0} — يحتاج معالجة: {problemCount:N0}";

        if (problemCount == 0)
            SetStatus("اكتملت المعاينة. راجع البيانات ثم اعتمد الاستيراد.", false);
        else
            SetStatus(
                "توجد أرقام مكررة أو مستخدمة مسبقًا. صحح الملف قبل الاعتماد.",
                true);
    }

    private void ImportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_previewRows.Count == 0)
        {
            SetStatus("حمّل ملفًا ونفّذ المعاينة أولًا.", true);
            return;
        }

        var invalidRows = _previewRows
            .Where(row => row.ValidationStatus != "جاهز")
            .ToList();

        if (invalidRows.Count > 0)
        {
            SetStatus(
                "لا يمكن الاعتماد قبل معالجة الأرقام المكررة أو الموجودة مسبقًا.",
                true);
            return;
        }

        if (PackageComboBox.SelectedItem is not DemoCardPackage package)
        {
            SetStatus("اختر الباقة أولًا.", true);
            return;
        }

        var batchId = BatchIdTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(batchId))
        {
            SetStatus("أدخل اسم الدفعة أو رقمها.", true);
            return;
        }

        if (!int.TryParse(
                SharedUsersTextBox.Text.Trim(),
                NumberStyles.Integer,
                CultureInfo.CurrentCulture,
                out var sharedUsers) ||
            sharedUsers < 0 ||
            sharedUsers > 1000)
        {
            SetStatus("عدد المستخدمين يجب أن يكون رقمًا بين 0 و1000؛ الصفر يعني غير محدود.", true);
            return;
        }

        try
        {
            var numbers = _previewRows
                .Select(row => row.CardNumber)
                .ToList();

            var batch = _issuanceService.ImportCards(
                _targetSystem,
                numbers,
                package,
                batchId,
                RouterProfileTextBox.Text.Trim(),
                PointOfSaleTextBox.Text.Trim(),
                "استيراد من ملف: " + Path.GetFileName(FilePathTextBox.Text),
                ManagerAccountTextBox.Text.Trim(),
                sharedUsers);

            PreviewCountTextBlock.Text = $"تم استيراد {batch.Quantity:N0} كرت";
            SetStatus(
                $"تم حفظ الدفعة {batch.BatchId} محليًا. الأرقام محفوظة كما استُوردت؛ لم يحدث اتصال براوتر أو طابعة حقيقيين.",
                false);
        }
        catch (Exception ex)
        {
            SetStatus("تعذر اعتماد الاستيراد: " + ex.Message, true);
        }
    }

    private List<string> ReadCardNumbersFromFile(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();

        List<string> values = extension switch
        {
            ".txt" => File.ReadAllLines(path).ToList(),
            ".csv" => ReadCsvFirstColumn(path),
            ".xlsx" => ReadExcelFirstColumn(path),
            _ => throw new InvalidDataException(
                "نوع الملف غير مدعوم. استخدم XLSX أو CSV أو TXT.")
        };

        values = values
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .ToList();

        if (values.Count > 0 && LooksLikeHeader(values[0]))
            values.RemoveAt(0);

        if (values.Any(value => value.Length > 128 || value.Any(char.IsControl)))
            throw new InvalidDataException(
                "يوجد رقم يتجاوز 128 محرفًا أو يحتوي على محارف تحكم.");

        return values;
    }

    private static bool LooksLikeHeader(string value)
    {
        var normalized = value
            .Trim()
            .Replace(" ", "")
            .Replace("_", "")
            .Replace("-", "")
            .ToLowerInvariant();

        return normalized is
            "cardnumber" or "card" or "username" or "number" or
            "رقمالکرت" or "رقمالكرت" or "رقمالكرت" or "رقمالكارت";
    }

    private static List<string> ReadCsvFirstColumn(string path)
    {
        var lines = File.ReadAllLines(path);

        var sample = lines.Take(10).ToList();
        var tabCount = sample.Sum(line => line.Count(ch => ch == '\t'));
        var semicolonCount = sample.Sum(line => line.Count(ch => ch == ';'));
        var commaCount = sample.Sum(line => line.Count(ch => ch == ','));

        var delimiter = tabCount > semicolonCount && tabCount > commaCount
            ? '\t'
            : semicolonCount > commaCount
                ? ';'
                : ',';

        return lines
            .Select(line => ReadFirstDelimitedField(line, delimiter))
            .ToList();
    }

    private static string ReadFirstDelimitedField(string line, char delimiter)
    {
        var result = new StringBuilder();
        var inQuotes = false;

        for (var index = 0; index < line.Length; index++)
        {
            var character = line[index];

            if (character == '"')
            {
                if (inQuotes && index + 1 < line.Length && line[index + 1] == '"')
                {
                    result.Append('"');
                    index++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (character == delimiter && !inQuotes)
                break;

            result.Append(character);
        }

        return result.ToString();
    }

    private static List<string> ReadExcelFirstColumn(string path)
    {
        using var stream = File.OpenRead(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        static XDocument ReadXml(ZipArchive archive, string entryName)
        {
            var entry = archive.GetEntry(entryName)
                ?? throw new InvalidDataException(
                    $"ملف Excel لا يحتوي على الجزء المطلوب: {entryName}");

            using var entryStream = entry.Open();
            return XDocument.Load(entryStream);
        }

        static string NormalizeZipPath(string path)
        {
            var stack = new List<string>();

            foreach (var part in path.Replace('\\', '/').Split('/'))
            {
                if (part.Length == 0 || part == ".")
                    continue;

                if (part == "..")
                {
                    if (stack.Count > 0)
                        stack.RemoveAt(stack.Count - 1);
                    continue;
                }

                stack.Add(part);
            }

            return string.Join("/", stack);
        }

        XNamespace main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace officeRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRel = "http://schemas.openxmlformats.org/package/2006/relationships";

        var workbook = ReadXml(archive, "xl/workbook.xml");
        var firstSheet = workbook.Root?
            .Element(main + "sheets")?
            .Elements(main + "sheet")
            .FirstOrDefault()
            ?? throw new InvalidDataException("لا توجد ورقة عمل داخل ملف Excel.");

        var relationshipId = (string?)firstSheet.Attribute(officeRel + "id");

        var relationships = ReadXml(archive, "xl/_rels/workbook.xml.rels");
        var target = relationships.Root?
            .Elements(packageRel + "Relationship")
            .FirstOrDefault(item =>
                string.Equals(
                    (string?)item.Attribute("Id"),
                    relationshipId,
                    StringComparison.Ordinal))?
            .Attribute("Target")?.Value
            ?? throw new InvalidDataException("تعذر تحديد ورقة العمل الأولى.");

        var worksheetPath = target.StartsWith("/", StringComparison.Ordinal)
            ? NormalizeZipPath(target.TrimStart('/'))
            : NormalizeZipPath(
                target.StartsWith("xl/", StringComparison.Ordinal)
                    ? target
                    : "xl/" + target);

        var worksheet = ReadXml(archive, worksheetPath);

        var sharedStrings = new List<string>();
        var stringsEntry = archive.GetEntry("xl/sharedStrings.xml");

        if (stringsEntry is not null)
        {
            using var stringsStream = stringsEntry.Open();
            var stringsDocument = XDocument.Load(stringsStream);

            sharedStrings = stringsDocument.Root?
                .Elements(main + "si")
                .Select(item => string.Concat(
                    item.Descendants(main + "t").Select(text => text.Value)))
                .ToList() ?? [];
        }

        var numberFormats = new Dictionary<int, string>();
        var stylesEntry = archive.GetEntry("xl/styles.xml");

        if (stylesEntry is not null)
        {
            using var stylesStream = stylesEntry.Open();
            var stylesDocument = XDocument.Load(stylesStream);

            numberFormats = stylesDocument.Root?
                .Element(main + "numFmts")?
                .Elements(main + "numFmt")
                .Where(item =>
                    int.TryParse((string?)item.Attribute("numFmtId"), out _))
                .ToDictionary(
                    item => int.Parse(
                        (string)item.Attribute("numFmtId")!,
                        CultureInfo.InvariantCulture),
                    item => (string?)item.Attribute("formatCode") ?? string.Empty)
                ?? [];
        }

        var cellFormatIds = new List<int>();
        var styleDocumentEntry = archive.GetEntry("xl/styles.xml");

        if (styleDocumentEntry is not null)
        {
            using var styleStream = styleDocumentEntry.Open();
            var styleDocument = XDocument.Load(styleStream);

            cellFormatIds = styleDocument.Root?
                .Element(main + "cellXfs")?
                .Elements(main + "xf")
                .Select(item =>
                    int.TryParse((string?)item.Attribute("numFmtId"), out var id)
                        ? id
                        : 0)
                .ToList() ?? [];
        }

        var results = new List<string>();

        foreach (var row in worksheet.Descendants(main + "sheetData")
                     .Elements(main + "row"))
        {
            var cell = row.Elements(main + "c")
                .FirstOrDefault(item => IsFirstColumnCell((string?)item.Attribute("r")));

            if (cell is null)
                continue;

            var type = (string?)cell.Attribute("t");
            var value = cell.Element(main + "v")?.Value ?? string.Empty;

            if (type == "inlineStr")
            {
                value = string.Concat(
                    cell.Descendants(main + "t").Select(item => item.Value));
            }
            else if (type == "s" &&
                     int.TryParse(value, out var sharedIndex) &&
                     sharedIndex >= 0 &&
                     sharedIndex < sharedStrings.Count)
            {
                value = sharedStrings[sharedIndex];
            }
            else if (type is null &&
                     int.TryParse((string?)cell.Attribute("s"), out var styleIndex) &&
                     styleIndex >= 0 &&
                     styleIndex < cellFormatIds.Count &&
                     numberFormats.TryGetValue(cellFormatIds[styleIndex], out var formatCode))
            {
                var integerFormat = formatCode.Split(';')[0].Trim();

                if (integerFormat.Length > 1 &&
                    integerFormat.All(character => character == '0') &&
                    decimal.TryParse(
                        value,
                        NumberStyles.Number,
                        CultureInfo.InvariantCulture,
                        out var numericValue) &&
                    numericValue >= 0 &&
                    numericValue == decimal.Truncate(numericValue))
                {
                    value = numericValue.ToString("0", CultureInfo.InvariantCulture)
                        .PadLeft(integerFormat.Length, '0');
                }
            }

            results.Add(value);
        }

        return results;
    }

    private static bool IsFirstColumnCell(string? reference)
    {
        if (string.IsNullOrWhiteSpace(reference))
            return false;

        var letters = new string(
            reference.TakeWhile(char.IsLetter).ToArray());

        return string.Equals(letters, "A", StringComparison.OrdinalIgnoreCase);
    }

    private void ClearPreview()
    {
        _previewRows = [];
        PreviewGrid.ItemsSource = _previewRows;
        PreviewCountTextBlock.Text = "لا توجد أرقام للمعاينة";
    }

    private void SetStatus(string message, bool isError)
    {
        StatusTextBlock.Text = message;
        StatusTextBlock.Foreground =
            (Brush)FindResource(isError ? "DangerBrush" : "SuccessBrush");
    }

    private sealed record ImportPreviewRow(
        int RowNumber,
        string CardNumber,
        string ValidationStatus);
}