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
            ShowImportFeedback("لا توجد معاينة", "اختر ملفًا ونفّذ المعاينة قبل اعتماد الاستيراد.", WorkflowFeedbackKind.Warning);
            SetStatus("حمّل ملفًا ونفّذ المعاينة أولًا.", true);
            return;
        }

        var invalidRows = _previewRows.Where(row => row.ValidationStatus != "جاهز").ToList();
        if (invalidRows.Count > 0)
        {
            ShowImportFeedback(
                "تعذر اعتماد الاستيراد",
                "عالج الأرقام المكررة أو الموجودة مسبقًا قبل الاعتماد.",
                WorkflowFeedbackKind.Warning,
                [new("عدد الأرقام", _previewRows.Count.ToString("N0", CultureInfo.CurrentCulture)),
                 new("الأرقام التي تحتاج معالجة", invalidRows.Count.ToString("N0", CultureInfo.CurrentCulture))]);
            SetStatus("لا يمكن الاعتماد قبل معالجة الأرقام المكررة أو الموجودة مسبقًا.", true);
            return;
        }

        if (PackageComboBox.SelectedItem is not DemoCardPackage package)
        {
            ShowImportFeedback("اختر الباقة", "حدد الباقة التي ستُربط بالكروت المستوردة.", WorkflowFeedbackKind.Warning);
            SetStatus("اختر الباقة أولًا.", true);
            return;
        }

        var batchId = BatchIdTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(batchId))
        {
            ShowImportFeedback("رقم الدفعة مطلوب", "أدخل رقمًا فريدًا للدفعة المستوردة.", WorkflowFeedbackKind.Warning);
            SetStatus("أدخل رقم الدفعة.", true);
            return;
        }

        if (!int.TryParse(SharedUsersTextBox.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var sharedUsers) ||
            sharedUsers < 0 || sharedUsers > 1000)
        {
            ShowImportFeedback("قيمة غير صحيحة", "عدد المستخدمين يجب أن يكون رقمًا بين 0 و1000؛ الصفر يعني غير محدود.", WorkflowFeedbackKind.Warning);
            SetStatus("عدد المستخدمين يجب أن يكون رقمًا بين 0 و1000؛ الصفر يعني غير محدود.", true);
            return;
        }

        try
        {
            var numbers = _previewRows.Select(row => row.CardNumber).ToList();
            var batch = _issuanceService.ImportCards(
                _targetSystem, numbers, package, batchId, RouterProfileTextBox.Text.Trim(),
                PointOfSaleTextBox.Text.Trim(), "استيراد من ملف: " + Path.GetFileName(FilePathTextBox.Text),
                ManagerAccountTextBox.Text.Trim(), sharedUsers);

            PreviewCountTextBlock.Text = $"تم استيراد {batch.Quantity:N0} كرت";
            SetStatus(
                $"تم حفظ الدفعة {batch.BatchId} محليًا. الأرقام محفوظة كما استُوردت؛ لم يحدث اتصال براوتر أو طابعة حقيقيين.",
                false);
            ShowImportFeedback(
                "تم استيراد الدفعة",
                $"تم حفظ {batch.Quantity:N0} كرت في سجل Nexora. لم تتم مزامنة الراوتر أو الطباعة.",
                WorkflowFeedbackKind.Success,
                [new("رقم الدفعة", batch.BatchId),
                 new("عدد الكروت", batch.Quantity.ToString("N0", CultureInfo.CurrentCulture)),
                 new("الباقة", batch.PackageName),
                 new("سعر الكرت", $"{batch.UnitPrice:N0} ريال"),
                 new("الإجمالي التقديري", $"{batch.Total:N0} ريال"),
                 new("الحالة", "محفوظ محليًا")]);
        }
        catch (Exception ex)
        {
            SetStatus("تعذر اعتماد الاستيراد: " + ex.Message, true);
            ShowImportFeedback("تعذر اعتماد الاستيراد", ex.Message, WorkflowFeedbackKind.Error);
        }
    }

    private void ShowImportFeedback(
        string title,
        string message,
        WorkflowFeedbackKind kind,
        IEnumerable<KeyValuePair<string, string>>? details = null)
    {
        WorkflowFeedbackWindow.ShowNotice(
            Window.GetWindow(this), title, message, kind, details,
            new WorkflowFeedbackAction("close", "إغلاق", true));
    }
}
