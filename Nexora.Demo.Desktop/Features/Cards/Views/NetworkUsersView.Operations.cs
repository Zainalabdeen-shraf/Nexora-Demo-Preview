using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Shared;

namespace Nexora.Demo.Desktop.Features.Cards.Views;

// Responsibility: Local demo actions and editor interactions.
public partial class NetworkUsersView
{
    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = GeneratedCardsGrid.SelectedItems.OfType<NetworkUserDisplayRow>().ToList();
        if (selected.Count == 0 && GeneratedCardsGrid.SelectedItem is NetworkUserDisplayRow single)
            selected.Add(single);

        ExecuteAction((sender as FrameworkElement)?.Tag?.ToString() ?? string.Empty, selected);
    }
    private void ExecuteAction(string action, IReadOnlyList<NetworkUserDisplayRow> selected)
    {
        if (selected.Count == 0)
        {
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "حدد حسابًا", "اختر كرتًا من القائمة أولًا.",
                WorkflowFeedbackKind.Warning, null, new WorkflowFeedbackAction("close", "إغلاق", true));
            return;
        }

        WorkflowFeedbackWindow.ShowNotice(
            Window.GetWindow(this),
            "أمر الراوتر غير متاح حاليًا",
            "لم يتم تنفيذ أي تغيير. الاتصال المتاح في هذه النسخة للقراءة فقط، لذلك لا يمكن تفعيل الحساب أو إيقافه أو تعديل باقته أو كلمة مروره أو حذفه. لن يعرض Nexora نجاحًا وهميًا لهذه الأوامر.",
            WorkflowFeedbackKind.Warning,
            [new("الكرت", selected[0].CardNumber), new("الرقم التسلسلي", selected[0].SerialNumber), new("النتيجة", "لم يُرسل أي أمر إلى الراوتر")],
            new WorkflowFeedbackAction("close", "إغلاق", true));
    }
    private static string NormalizeDateLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "—";
        var match = Regex.Match(value, @"(?<time>\d{1,2}:\d{2})\s+(?<date>\d{4}/\d{2}/\d{2})");
        return match.Success ? $"{match.Groups["date"].Value} {match.Groups["time"].Value}" : value;
    }
}
