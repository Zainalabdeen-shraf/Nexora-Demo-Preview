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

// Responsibility: File selection and import-preview construction.
public partial class ImportCardsView
{
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
    private void ClearPreview()
    {
        _previewRows = [];
        PreviewGrid.ItemsSource = _previewRows;
        PreviewCountTextBlock.Text = "لا توجد أرقام للمعاينة";
    }
}
