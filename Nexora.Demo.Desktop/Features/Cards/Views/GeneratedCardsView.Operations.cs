using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Shared;

namespace Nexora.Demo.Desktop.Features.Cards.Views;

// Responsibility: Workflow status, upload/print/sale actions, and summary updates.
public partial class GeneratedCardsView
{
    private List<GeneratedDemoCard> GetSelectedCards() => GeneratedCardsGrid.SelectedItems.OfType<GeneratedDemoCard>().ToList();
    private void UpdateWorkflowStatus()
    {
        if (WorkflowStatusTextBlock is null || GeneratedCardsGrid is null) return;
        var selected = GetSelectedCards();
        if (selected.Count == 0)
        {
            WorkflowStatusTextBlock.Text = "حدد كرتًا لعرض تفاصيل حالته التشغيلية.";
            return;
        }
        if (selected.Count > 1)
        {
            WorkflowStatusTextBlock.Text = $"تم تحديد {selected.Count:N0} كرت؛ الإجراءات ستطبق على المحدد فقط.";
            return;
        }
        var card = selected[0];
        WorkflowStatusTextBlock.Text = $"{card.CardNumber} · {card.BatchId} · الحالة: {_workflowStore.GetCardStatusLabel(card.BatchId, card.CardNumber)}";
    }
    private void UploadSelectedToRouterButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedCards();
        if (selected.Count == 0)
        {
            CardsScopeTextBlock.Text = "حدد كرتًا أو أكثر من القائمة أولًا.";
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "لا توجد كروت محددة", "حدد كرتًا أو أكثر قبل طلب المزامنة.", WorkflowFeedbackKind.Warning, null, new WorkflowFeedbackAction("close", "العودة للقائمة", true));
            return;
        }
        try
        {
            _workflowStore.RecordRouterUpload(selected);
            SetPreferredBatchesForSelection(selected);
            CardsScopeTextBlock.Text = $"تم تسجيل طلب مزامنة محلي لـ {selected.Count:N0} كرت؛ لم يتم الاتصال بالراوتر.";
            RefreshFilteredCards();
            WorkflowFeedbackWindow.ShowNotice(
                Window.GetWindow(this), "تم تسجيل طلب المزامنة محليًا",
                "حُفظت حالة سير العمل في Nexora فقط. موصل RouterOS الموجود في المشروع للقراءة فقط، لذلك لم يتم إنشاء الحسابات على الراوتر ولم تتغير إعداداته.",
                WorkflowFeedbackKind.Warning,
                [new("عدد الكروت", selected.Count.ToString("N0")), new("النتيجة", "طلب محلي فقط — بانتظار موصل كتابة معتمد")],
                new WorkflowFeedbackAction("close", "مفهوم", true));
        }
        catch (Exception ex)
        {
            CardsScopeTextBlock.Text = "تعذر تسجيل الإجراء: " + ex.Message;
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "تعذر تسجيل طلب المزامنة", ex.Message, WorkflowFeedbackKind.Error, null, new WorkflowFeedbackAction("close", "إغلاق", true));
        }
    }

    private void UploadSelectedAndPrintButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedCards();
        if (selected.Count == 0)
        {
            CardsScopeTextBlock.Text = "حدد كرتًا أو أكثر قبل الرفع والطباعة.";
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "لا توجد كروت محددة", "حدد الكروت التي تريد تجهيزها للطباعة أولًا.", WorkflowFeedbackKind.Warning, null, new WorkflowFeedbackAction("close", "العودة للقائمة", true));
            return;
        }
        try
        {
            _workflowStore.RecordRouterUpload(selected);
            _workflowStore.QueuePrintSelection(selected);
            SetPreferredBatchesForSelection(selected);
            CardsScopeTextBlock.Text = $"تم تسجيل طلب مزامنة محلي لـ {selected.Count:N0} كرت وتجهيزها في قائمة الطباعة؛ لم يتم الاتصال بالراوتر.";
            RefreshFilteredCards();
            var choice = WorkflowFeedbackWindow.ShowNotice(
                Window.GetWindow(this), "تم تجهيز الكروت للطباعة",
                "أُضيفت الكروت إلى قائمة الطباعة. طلب المزامنة المسجل محلي فقط ولم يرسل حسابات إلى الراوتر.",
                WorkflowFeedbackKind.Success,
                [new("عدد الكروت", selected.Count.ToString("N0")), new("الطباعة", "لم تُرسل للطابعة بعد")],
                new WorkflowFeedbackAction("print", "فتح قائمة الطباعة", true),
                new WorkflowFeedbackAction("close", "لاحقًا"));
            if (choice == "print")
                NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("printing", "قائمة الطباعة"));
        }
        catch (Exception ex)
        {
            CardsScopeTextBlock.Text = "تعذر تجهيز مسار الطباعة: " + ex.Message;
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "تعذر تجهيز مسار الطباعة", ex.Message, WorkflowFeedbackKind.Error, null, new WorkflowFeedbackAction("close", "إغلاق", true));
        }
    }

    private void PrepareSelectedForOnlineSaleButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedCards();
        if (selected.Count == 0)
        {
            CardsScopeTextBlock.Text = "حدد كرتًا أو أكثر قبل تجهيز البيع أونلاين.";
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "لا توجد كروت محددة", "حدد الكروت التي تريد تجهيزها للبيع الإلكتروني أولًا.", WorkflowFeedbackKind.Warning, null, new WorkflowFeedbackAction("close", "العودة للقائمة", true));
            return;
        }
        try
        {
            _workflowStore.RecordOnlineSalePreparation(selected);
            SetPreferredBatchesForSelection(selected);
            CardsScopeTextBlock.Text = $"تم تجهيز {selected.Count:N0} كرت للبيع الإلكتروني محليًا؛ لم يتم نشرها على متجر خارجي.";
            RefreshFilteredCards();
            WorkflowFeedbackWindow.ShowNotice(
                Window.GetWindow(this), "تم تجهيز الكروت للبيع الإلكتروني محليًا",
                "حُفظت علامة التجهيز في سجل Nexora. هذا الإجراء لا ينشر منتجات على متجر إلكتروني ولا ينفذ مزامنة مع الراوتر.",
                WorkflowFeedbackKind.Info,
                [new("عدد الكروت", selected.Count.ToString("N0")), new("الحالة", "جاهز ضمن سجل العمل المحلي")],
                new WorkflowFeedbackAction("close", "حسنًا", true));
        }
        catch (Exception ex)
        {
            CardsScopeTextBlock.Text = "تعذر تجهيز البيع الإلكتروني: " + ex.Message;
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "تعذر تجهيز البيع الإلكتروني", ex.Message, WorkflowFeedbackKind.Error, null, new WorkflowFeedbackAction("close", "إغلاق", true));
        }
    }

    private void SetPreferredBatchesForSelection(IEnumerable<GeneratedDemoCard> cards)
    {
        foreach (var group in cards.GroupBy(card => SystemKeyForCard(card)))
        {
            var batchIds = group.Select(card => card.BatchId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (batchIds.Count == 1)
                _workflowStore.SetPreferredBatchFilter(group.Key, batchIds[0]);
        }
    }
    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshFilteredCards();
        CardsScopeTextBlock.Text = "تم تحديث قائمة الكروت والدفعات.";
    }
    private void IssuanceService_ScenarioChanged(object? sender, EventArgs e)
    {
        Dispatcher.InvokeAsync(RefreshFilteredCards);
    }
    private void OpenPrintQueueButton_Click(object sender, RoutedEventArgs e) =>
        NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("printing", "قائمة الطباعة"));
    private void UpdateSummary()
    {
        if (GeneratedCardsGrid is null || CardsCountSummaryTextBlock is null) return;

        var displayed = GeneratedCardsGrid.Items.Count;
        var totalSystemCards = GetCardsInCurrentSystemScope().Count();
        var totalBatches = GetCardsInCurrentSystemScope().Select(card => card.BatchId).Distinct(StringComparer.OrdinalIgnoreCase).Count();

        CardsCountSummaryTextBlock.Text = $"المعروض: {displayed:N0} سجل معلق · إجمالي السجلات ضمن النطاق: {totalSystemCards:N0} · إجمالي الدفعات: {totalBatches:N0}";
        EmptyCardsPanel.Visibility = displayed == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyCardsDescriptionTextBlock.Text = _issuanceService.Cards.Count == 0
            ? "لا توجد حسابات معلقة. أنشئ دفعة جديدة."
            : "لا توجد سجلات تطابق البحث والمرشحات الحالية.";
        UpdateWorkflowStatus();
    }
    private IEnumerable<GeneratedDemoCard> GetCardsInCurrentSystemScope()
    {
        var systemFilter = (CardSystemFilterComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var cards = _issuanceService.Cards.AsEnumerable();
        if (systemFilter == "hotspot")
            cards = cards.Where(card => card.TargetSystem == "مستخدمو الهوتسبوت");
        else if (systemFilter == "user-manager")
            cards = cards.Where(card => card.TargetSystem == "مدير المستخدمين");
        return cards;
    }
    private void RowSyncIcon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not GeneratedDemoCard card)
            return;
        try
        {
            _workflowStore.RecordRouterUpload(new[] { card });
            RefreshFilteredCards();
            WorkflowFeedbackWindow.ShowNotice(
                Window.GetWindow(this), "تم تسجيل طلب المزامنة محليًا",
                $"تم حفظ طلب مزامنة للكرت {card.CardNumber} داخل سجل Nexora فقط. لم يتم إرسال الحساب إلى الراوتر لأن موصل RouterOS المتاح للقراءة فقط.",
                WorkflowFeedbackKind.Warning,
                [new("الكرت", card.CardNumber), new("النتيجة", "لم تتم مزامنة فعلية")],
                new WorkflowFeedbackAction("close", "إغلاق", true));
        }
        catch (Exception ex)
        {
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "تعذر تسجيل المزامنة", ex.Message, WorkflowFeedbackKind.Error, null, new WorkflowFeedbackAction("close", "إغلاق", true));
        }
    }
    private void RowPrintIcon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not GeneratedDemoCard card)
            return;
        try
        {
            _workflowStore.QueuePrintSelection(new[] { card });
            RefreshFilteredCards();
            WorkflowFeedbackWindow.ShowNotice(
                Window.GetWindow(this), "أُضيف الكرت إلى قائمة الطباعة",
                "تم تجهيز سجل الكرت للطباعة. لم يتم إرسال مستند إلى الطابعة بعد.",
                WorkflowFeedbackKind.Success,
                [new("الكرت", card.CardNumber), new("الخطوة التالية", "افتح قائمة الطباعة لمراجعة المعاينة ثم الإرسال")],
                new WorkflowFeedbackAction("close", "حسنًا", true));
        }
        catch (Exception ex)
        {
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "تعذر تجهيز الطباعة", ex.Message, WorkflowFeedbackKind.Error, null, new WorkflowFeedbackAction("close", "إغلاق", true));
        }
    }
    private void RowSaleIcon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not GeneratedDemoCard card)
            return;
        try
        {
            _workflowStore.RecordOnlineSalePreparation(new[] { card });
            RefreshFilteredCards();
            WorkflowFeedbackWindow.ShowNotice(
                Window.GetWindow(this), "تم تجهيز الكرت للبيع الإلكتروني محليًا",
                "تم تسجيل حالة التجهيز في Nexora فقط. لم يُنشر الكرت في متجر خارجي.",
                WorkflowFeedbackKind.Info,
                [new("الكرت", card.CardNumber), new("الحالة", "تجهيز محلي")],
                new WorkflowFeedbackAction("close", "حسنًا", true));
        }
        catch (Exception ex)
        {
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "تعذر تجهيز البيع الإلكتروني", ex.Message, WorkflowFeedbackKind.Error, null, new WorkflowFeedbackAction("close", "إغلاق", true));
        }
    }

}
