using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Printing.Views;

namespace Nexora.Demo.Desktop.Features.Cards.Views;

public partial class CardBatchesView
{
    private async void BatchActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not BatchManagementRow row || button.Tag is not string action)
            return;

        var operationKey = row.TargetSystem.Trim() + "\u001F" + row.BatchId.Trim();
        if (!_batchOperationsInProgress.Add(operationKey))
        {
            SetOperationStatus($"توجد عملية أخرى جارية للدفعة {row.BatchId}. انتظر حتى تنتهي قبل بدء عملية أخرى.", isError: true);
            return;
        }

        try
        {
            switch (action)
            {
                case "sync":
                    await SynchronizeBatchAsync(row);
                    break;
                case "print":
                    PrintBatchCards(row);
                    break;
                case "toggle-disabled":
                    await ToggleBatchDisabledAsync(row);
                    break;
                case "archive":
                    ArchiveOrRestoreBatch(row);
                    break;
            }
        }
        catch (Exception ex)
        {
            SetOperationStatus(ex.Message, isError: true);
        }
        finally
        {
            _batchOperationsInProgress.Remove(operationKey);
        }
    }

    private async Task SynchronizeBatchAsync(BatchManagementRow row)
    {
        if (!row.CanSync)
        {
            SetOperationStatus(row.SyncToolTip, isError: true);
            return;
        }

        if (_metadataStore.StorageWarning is not null)
        {
            SetOperationStatus(_metadataStore.StorageWarning, isError: true);
            return;
        }

        var confirm = MessageBox.Show(
            $"ستتم محاكاة مزامنة {row.Quantity:N0} حساباً من الدفعة {row.BatchId} داخل الديمو.\n" +
            "ستُحدَّث حالات الحسابات محلياً فقط، ولن يتم الاتصال براوتر أو إرسال أي بيانات إلى شبكة خارجية.\n\nهل تريد المتابعة؟",
            "محاكاة مزامنة الدفعة",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes) return;

        _metadataStore.SetSyncState(row.BatchId, row.TargetSystem, "جارٍ المزامنة", "محاكاة محلية لعملية المزامنة ضمن الديمو.");
        RefreshFromSource(preserveSelection: true);
        SetOperationStatus($"جارٍ محاكاة مزامنة الدفعة {row.BatchId}...", isError: false);

        // Keep the operation visibly asynchronous so the demo communicates an in-progress state.
        await Task.Delay(650);
        var statuses = row.Cards.ToDictionary(
            card => card.CardNumber,
            _ => "مكتملة المزامنة",
            StringComparer.OrdinalIgnoreCase);
        _issuanceService.UpdateBatchSynchronizationResults(
            row.BatchId, row.TargetSystem, statuses, "مكتملة المزامنة");
        var message = $"اكتملت محاكاة مزامنة {statuses.Count:N0} من {row.Quantity:N0} حساباً محلياً. لم يتم الاتصال بالراوتر.";
        _metadataStore.SetSyncState(row.BatchId, row.TargetSystem, "مكتملة المزامنة", message);
        RefreshFromSource(preserveSelection: true);
        SetOperationStatus(message, isError: false);
    }

    private async Task ToggleBatchDisabledAsync(BatchManagementRow row)
    {
        if (row.IsArchived)
        {
            SetOperationStatus("استعد الدفعة من الأرشيف قبل تغيير حالة التعطيل.", isError: true);
            return;
        }
        if (_metadataStore.StorageWarning is not null)
        {
            SetOperationStatus(_metadataStore.StorageWarning, isError: true);
            return;
        }

        var disable = !row.IsDisabled;
        var verb = disable ? "تعطيل" : "إعادة تفعيل";
        var message = $"سيتم {verb} الدفعة {row.BatchId} محلياً ضمن الديمو، مع الاحتفاظ بحالة المزامنة وسجل الطباعة. لن يتم الاتصال بالراوتر أو تغيير أي حساب خارجي.\n\nهل تريد المتابعة؟";
        if (MessageBox.Show(message, $"تأكيد {verb} الدفعة", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        await Task.Delay(150);
        _metadataStore.SetDisabled(row.BatchId, row.TargetSystem, disable,
            $"تم {verb} الدفعة محلياً ضمن الديمو دون اتصال خارجي.");
        RefreshFromSource(preserveSelection: true);
        SetOperationStatus($"تم {verb} الدفعة {row.BatchId} محلياً ضمن الديمو.", isError: false);
    }

    private void ArchiveOrRestoreBatch(BatchManagementRow row)
    {
        if (_metadataStore.StorageWarning is not null)
        {
            SetOperationStatus(_metadataStore.StorageWarning, isError: true);
            return;
        }

        if (row.IsArchived)
        {
            if (MessageBox.Show(
                    $"سيتم استعادة الدفعة {row.BatchId} من الأرشيف. الاستعادة لا تعني إعادة تفعيل الدفعة؛ إذا كانت معطلة فستبقى معطلة حتى تفعيلها صراحةً.\n\nهل تريد المتابعة؟",
                    "استعادة الدفعة", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;

            _issuanceService.SetBatchCardsArchiveStatePreservingSyncStatus(row.BatchId, row.TargetSystem, archived: false);
            _metadataStore.SetArchived(row.BatchId, row.TargetSystem, archived: false, disableWhenArchiving: false, message: "تمت استعادة الدفعة من الأرشيف.");
            RefreshFromSource(preserveSelection: true);
            SetOperationStatus($"تمت استعادة الدفعة {row.BatchId}. لم تتغير حالة المزامنة.", isError: false);
            return;
        }

        if (row.SyncedAccountsCount > 0 && !row.AllCardsExpired)
        {
            SetOperationStatus("لا يمكن أرشفة دفعة لها حسابات متزامنة لم تنتهِ صلاحيتها. عطّلها الآن، وأرشفها بعد انتهاء جميع كروتها.", isError: true);
            return;
        }

        var expired = row.AllCardsExpired;
        var archiveLabel = expired ? "منتهية ومؤرشفة" : "معطلة ومؤرشفة";
        if (MessageBox.Show(
                expired
                    ? $"جميع كروت الدفعة {row.BatchId} منتهية. سيتم أرشفة سجلاتها محلياً مع الاحتفاظ بسجل المزامنة.\n\nهل تريد المتابعة؟"
                    : $"هذه الدفعة غير مكتملة المزامنة. سيتم تعطيلها وأرشفتها محلياً، ولن تتم محاكاة مزامنتها حتى استعادتها وإعادة تفعيلها.\n\nهل تريد المتابعة؟",
                "تأكيد أرشفة الدفعة", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        _issuanceService.SetBatchCardsArchiveStatePreservingSyncStatus(
            row.BatchId, row.TargetSystem, archived: true,
            reason: expired ? "أرشفة بعد انتهاء جميع كروت الدفعة" : "تعطيل وأرشفة دفعة لم تتم مزامنتها");
        _metadataStore.SetArchived(row.BatchId, row.TargetSystem, archived: true,
            disableWhenArchiving: !expired, message: $"تمت أرشفة الدفعة بالحالة {archiveLabel}.");
        RefreshFromSource(preserveSelection: true);
        SetOperationStatus($"تمت أرشفة الدفعة {row.BatchId}: {archiveLabel}.", isError: false);
    }

    private void PrintBatchCards(BatchManagementRow row)
    {
        if (!row.CanPrint)
        {
            SetOperationStatus(row.PrintToolTip, isError: true);
            return;
        }

        var selected = row.Cards.Where(card => !card.IsArchived &&
            !string.Equals(card.DemoStatus, "منتهية", StringComparison.OrdinalIgnoreCase)).ToList();
        if (selected.Count == 0)
        {
            SetOperationStatus("لا توجد كروت غير مؤرشفة وغير منتهية للطباعة في هذه الدفعة.", isError: true);
            return;
        }

        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true)
        {
            SetOperationStatus("تم إلغاء مهمة الطباعة.", isError: false);
            return;
        }

        try
        {
            var pageWidth = dialog.PrintableAreaWidth > 100 ? dialog.PrintableAreaWidth : 793.7;
            var pageHeight = dialog.PrintableAreaHeight > 100 ? dialog.PrintableAreaHeight : 1122.5;
            var document = new FixedDocument();
            document.DocumentPaginator.PageSize = new Size(pageWidth, pageHeight);

            // Reuse the saved card template and physical size used by the print queue screen.
            var designer = new TemplateDesignerView();
            var cardSizeMm = designer.GetPhysicalCardSizeMillimeters();
            var mmToDip = 96d / 25.4d;
            var cardWidth = cardSizeMm.Width * mmToDip;
            var cardHeight = cardSizeMm.Height * mmToDip;
            const double horizontalMarginDip = 7.6d;
            const double verticalMarginDip = 19d;
            const double gapDip = 3.8d;
            if (cardWidth > pageWidth - horizontalMarginDip * 2d || cardHeight > pageHeight - verticalMarginDip * 2d)
                throw new InvalidOperationException($"مقاس القالب {cardSizeMm.Width:0.#} × {cardSizeMm.Height:0.#} مم أكبر من المساحة القابلة للطباعة.");

            var maxColumns = Math.Max(1, (int)Math.Floor((pageWidth - horizontalMarginDip * 2d + gapDip) / (cardWidth + gapDip)));
            var maxRows = Math.Max(1, (int)Math.Floor((pageHeight - verticalMarginDip * 2d + gapDip) / (cardHeight + gapDip)));
            var columns = Math.Clamp(maxColumns, 1, 8);
            var perPage = Math.Max(1, columns * maxRows);
            var totalPages = (int)Math.Ceiling(selected.Count / (double)perPage);

            for (var pageIndex = 0; pageIndex < totalPages; pageIndex++)
            {
                var page = new FixedPage { Width = pageWidth, Height = pageHeight, FlowDirection = FlowDirection.RightToLeft };
                var firstCardIndex = pageIndex * perPage;
                var cardsOnThisPage = Math.Min(perPage, selected.Count - firstCardIndex);
                var gridWidth = columns * cardWidth + Math.Max(0, columns - 1) * gapDip;
                var horizontalStart = Math.Max(horizontalMarginDip, (pageWidth - gridWidth) / 2d);
                for (var localIndex = 0; localIndex < cardsOnThisPage; localIndex++)
                {
                    var globalIndex = firstCardIndex + localIndex;
                    var visual = designer.CreatePrintableCard(selected[globalIndex], globalIndex);
                    var printedCard = new Viewbox
                    {
                        Width = cardWidth,
                        Height = cardHeight,
                        Stretch = Stretch.Uniform,
                        StretchDirection = StretchDirection.DownOnly,
                        Child = visual
                    };
                    var rowIndex = localIndex / columns;
                    var columnFromRight = localIndex % columns;
                    var left = pageWidth - horizontalStart - cardWidth - columnFromRight * (cardWidth + gapDip);
                    var top = verticalMarginDip + rowIndex * (cardHeight + gapDip);
                    FixedPage.SetLeft(printedCard, left);
                    FixedPage.SetTop(printedCard, top);
                    page.Children.Add(printedCard);
                }

                var pageContent = new PageContent();
                ((IAddChild)pageContent).AddChild(page);
                document.Pages.Add(pageContent);
            }

            dialog.PrintDocument(document.DocumentPaginator, $"TabCard — {selected.Count:N0} بطاقة من الدفعة {row.BatchId}");
            var printerName = dialog.PrintQueue?.FullName ?? "الطابعة المحددة";
            try
            {
                _workflowStore.RecordPrintSubmitted(selected, printerName);
                _metadataStore.RecordPrintSubmission(row.BatchId, row.TargetSystem, printerName, selected.Count);
                RefreshFromSource(preserveSelection: true);
                SetOperationStatus($"أُرسلت {selected.Count:N0} بطاقة إلى الطابعة {printerName}. تم تسجيل طلب الإرسال؛ هذا لا يؤكد خروج الورق فعلياً.", isError: false);
            }
            catch (Exception recordException)
            {
                SetOperationStatus($"أُرسلت المهمة إلى {printerName}، لكن تعذر تحديث سجل الطباعة: {recordException.Message}", isError: true);
            }
        }
        catch (Exception ex)
        {
            SetOperationStatus("تعذر إرسال مهمة الطباعة: " + ex.Message, isError: true);
        }
    }

}
