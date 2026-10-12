// Shared printing workflow for queued card selections.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;

namespace Nexora.Demo.Desktop.Features.Printing.Views;

/// <summary>Restores local card selections sent to the print queue by the demo issuance workflow.</summary>
public partial class PrintQueueView : UserControl
{
    private readonly DemoCardWorkflowStore _tabCardWorkflowStore = new();

    private void TabCardWorkflow_PrintQueueLoaded(object sender, RoutedEventArgs e)
    {
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(ApplyPendingWorkflowSelection));
    }

    private void ApplyPendingWorkflowSelection()
    {
        if (!IsLoaded || PrintQueueGrid is null)
            return;

        var pending = _tabCardWorkflowStore.GetPendingPrintSelection();
        if (pending.Count == 0)
            return;

        var selected = _issuanceService.Cards.Where(card => pending.Any(item =>
            string.Equals(item.BatchId, card.BatchId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.CardNumber, card.CardNumber, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.TargetSystem, GetWorkflowSystemKey(card.TargetSystem), StringComparison.OrdinalIgnoreCase)))
            .ToList();

        PrintQueueGrid.UnselectAll();
        foreach (var card in selected)
            PrintQueueGrid.SelectedItems.Add(card);

        if (selected.Count > 0)
        {
            PrintQueueGrid.ScrollIntoView(selected[0]);
            UpdateSummary();
            UpdatePreview();
            PrintPreviewStatusTextBlock.Text = $"تم تحديد {selected.Count:N0} كرت من الدفعة المحددة.";
        }
        else
        {
            PrintPreviewStatusTextBlock.Text = "توجد عناصر في قائمة الطباعة لم تعد ضمن سجلات الكروت الحالية.";
        }
    }

    private static string GetWorkflowSystemKey(string targetSystem) =>
        targetSystem.Contains("هوتسبوت", StringComparison.OrdinalIgnoreCase) ? "hotspot" : "user-manager";

    private void TabCardWorkflow_PreparePreviewButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = PrintQueueGrid.SelectedItems.OfType<GeneratedDemoCard>().ToList();
        if (selected.Count == 0)
        {
            PrintPreviewStatusTextBlock.Text = "حدد كرتًا أو أكثر من القائمة لمراجعة المعاينة.";
            return;
        }

        try
        {
            _tabCardWorkflowStore.RecordPrintPreview(selected);
            PrintPreviewStatusTextBlock.Text = $"تم تسجيل مراجعة معاينة {selected.Count:N0} كرت.";
        }
        catch (Exception ex)
        {
            PrintPreviewStatusTextBlock.Text = "تعذر تسجيل مراجعة المعاينة: " + ex.Message;
        }
    }

    private void TabCardWorkflow_SimulatePrintButton_Click(object sender, RoutedEventArgs e) => PrintSelectedCards();

}
