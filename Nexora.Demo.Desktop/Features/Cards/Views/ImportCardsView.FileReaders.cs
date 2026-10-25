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

// Responsibility: CSV, delimited text, and Excel first-column readers.
public partial class ImportCardsView
{
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
}
