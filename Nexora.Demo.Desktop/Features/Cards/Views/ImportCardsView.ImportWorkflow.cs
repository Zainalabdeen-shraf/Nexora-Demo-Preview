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

// Responsibility: Validated local import workflow.
public partial class ImportCardsView
{
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
}
