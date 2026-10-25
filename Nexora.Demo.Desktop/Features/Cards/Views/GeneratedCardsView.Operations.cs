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
            return;
        }
        try
        {
            _workflowStore.RecordRouterUpload(selected);
            SetPreferredBatchesForSelection(selected);
            CardsScopeTextBlock.Text = $"تم تسجيل طلب مزامنة لـ {selected.Count:N0} كرت.";
            RefreshFilteredCards();
        }
        catch (Exception ex) { CardsScopeTextBlock.Text = "تعذر تسجيل الإجراء: " + ex.Message; }
    }
    private void UploadSelectedAndPrintButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedCards();
        if (selected.Count == 0)
        {
            CardsScopeTextBlock.Text = "حدد كرتًا أو أكثر قبل الرفع والطباعة.";
            return;
        }
        try
        {
            _workflowStore.RecordRouterUpload(selected);
            _workflowStore.QueuePrintSelection(selected);
            SetPreferredBatchesForSelection(selected);
            CardsScopeTextBlock.Text = $"تم تسجيل طلب مزامنة لـ {selected.Count:N0} كرت وإضافتها إلى قائمة الطباعة.";
            RefreshFilteredCards();
            NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("printing", "قائمة الطباعة"));
        }
        catch (Exception ex) { CardsScopeTextBlock.Text = "تعذر تجهيز مسار الطباعة: " + ex.Message; }
    }
    private void PrepareSelectedForOnlineSaleButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedCards();
        if (selected.Count == 0)
        {
            CardsScopeTextBlock.Text = "حدد كرتًا أو أكثر قبل تجهيز البيع أونلاين.";
            return;
        }
        try
        {
            _workflowStore.RecordOnlineSalePreparation(selected);
            SetPreferredBatchesForSelection(selected);
            CardsScopeTextBlock.Text = $"تم تسجيل تجهيز {selected.Count:N0} كرت للبيع الإلكتروني.";
            RefreshFilteredCards();
        }
        catch (Exception ex) { CardsScopeTextBlock.Text = "تعذر تجهيز البيع الإلكتروني: " + ex.Message; }
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
        if (sender is Button button && button.DataContext is GeneratedDemoCard card)
        {
            _workflowStore.RecordRouterUpload(new[] { card });
            RefreshFilteredCards();
            MessageBox.Show(Window.GetWindow(this),
                $"⚡ تم تسجيل طلب المزامنة للكرت ({card.CardNumber}) بنجاح إلى الراوتر.",
                "المزامنة الفورية", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
    private void RowPrintIcon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is GeneratedDemoCard card)
        {
            _workflowStore.QueuePrintSelection(new[] { card });
            RefreshFilteredCards();
            MessageBox.Show(Window.GetWindow(this),
                $"🖨️ تم تجهيز الكرت ({card.CardNumber}) وإضافته مباشرة إلى قائمة الطباعة.",
                "تجهيز الطباعة", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
    private void RowSaleIcon_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is GeneratedDemoCard card)
        {
            _workflowStore.RecordOnlineSalePreparation(new[] { card });
            RefreshFilteredCards();
            MessageBox.Show(Window.GetWindow(this),
                $"🛒 تم تسجيل الكرت ({card.CardNumber}) كمنتج جاهز للبيع الإلكتروني.",
                "البيع الإلكتروني", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
