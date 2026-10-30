using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Printing.Views;
using Nexora.Demo.Desktop.Features.Shared;

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
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "تعذرت العملية على الدفعة", ex.Message,
                WorkflowFeedbackKind.Error, [new("رقم الدفعة", row.BatchId)],
                new WorkflowFeedbackAction("close", "إغلاق", true));
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
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "لا يمكن تنفيذ الإجراء", row.SyncToolTip,
                WorkflowFeedbackKind.Warning, [new("الدفعة", row.DisplayName)], new WorkflowFeedbackAction("close", "حسنًا", true));
            return;
        }

        if (_metadataStore.StorageWarning is not null)
        {
            SetOperationStatus(_metadataStore.StorageWarning, isError: true);
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "تخزين البيانات يحتاج مراجعة", _metadataStore.StorageWarning,
                WorkflowFeedbackKind.Error, null, new WorkflowFeedbackAction("close", "إغلاق", true));
            return;
        }

        var confirm = WorkflowFeedbackWindow.ShowNotice(
            Window.GetWindow(this),
            "تسجيل طلب متابعة المزامنة",
            $"سيسجل Nexora طلب متابعة محليًا لـ {row.Quantity:N0} حسابًا في الدفعة «{row.DisplayName}». لن تُرسل بيانات إلى الراوتر ولن تتغير حالة الحسابات.",
            WorkflowFeedbackKind.Warning,
            [new("الدفعة", row.DisplayName), new("عدد الحسابات", row.Quantity.ToString("N0")), new("النطاق", "تجريبي محلي فقط")],
            new WorkflowFeedbackAction("confirm", "تسجيل الطلب المحلي", true),
            new WorkflowFeedbackAction("cancel", "إلغاء"));
        if (confirm != "confirm") return;

        _metadataStore.SetSyncState(row.BatchId, row.TargetSystem, "طلب محلي غير منفذ", "لا يوجد موصل كتابة للراوتر في هذه النسخة؛ لم تتغير حالة أي حساب فعلي.");
        RefreshFromSource(preserveSelection: true);
        SetOperationStatus($"جارٍ تسجيل طلب محلي للدفعة {row.DisplayName} دون إرسال بيانات إلى الراوتر…", isError: false);
        var owner = Window.GetWindow(this);
        var progress = WorkflowFeedbackWindow.ShowProgress(owner, "تسجيل طلب المزامنة",
            "يجري حفظ نتيجة التحقق وسجل المتابعة المحلي. لا يوجد اتصال كتابة بالراوتر في هذا المسار.", "التحقق من حالة الدفعة…");
        try
        {
            await Task.Delay(650);
            progress.UpdateStage("حفظ الطلب المحلي دون تغيير حالة الحسابات…");
            var count = row.Cards.Count;
            var message = $"تم تسجيل طلب متابعة محلي للدفعة {row.DisplayName} التي تضم {count:N0} حسابًا. لم تُرسل أي بيانات إلى الراوتر ولم تتغير حالة الحسابات.";
            _metadataStore.SetSyncState(row.BatchId, row.TargetSystem, "طلب محلي غير منفذ", message);
            RefreshFromSource(preserveSelection: true);
            SetOperationStatus(message, isError: false);
            progress.Close();
            WorkflowFeedbackWindow.ShowNotice(owner, "تم تسجيل طلب المتابعة المحلي", message, WorkflowFeedbackKind.Warning,
                [new("الدفعة", row.DisplayName), new("عدد الحسابات", count.ToString("N0")), new("حالة الراوتر", "لم تتغير؛ المزامنة الفعلية غير مدعومة في هذا المشروع")],
                new WorkflowFeedbackAction("close", "إغلاق", true));
        }
        finally
        {
            if (progress.IsVisible) progress.Close();
        }
    }

    private async Task ToggleBatchDisabledAsync(BatchManagementRow row)
    {
        if (row.IsArchived)
        {
            const string text = "استعد الدفعة من الأرشيف قبل تغيير حالة التعطيل.";
            SetOperationStatus(text, isError: true);
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "لا يمكن تغيير حالة الدفعة", text,
                WorkflowFeedbackKind.Warning, null, new WorkflowFeedbackAction("close", "حسنًا", true));
            return;
        }
        if (_metadataStore.StorageWarning is not null)
        {
            SetOperationStatus(_metadataStore.StorageWarning, isError: true);
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "تخزين البيانات يحتاج مراجعة", _metadataStore.StorageWarning,
                WorkflowFeedbackKind.Error, null, new WorkflowFeedbackAction("close", "إغلاق", true));
            return;
        }

        var disable = !row.IsDisabled;
        var verb = disable ? "تعطيل" : "إعادة تفعيل";
        var message = $"سيتم {verb} الدفعة «{row.DisplayName}» محليًا داخل Nexora، مع الاحتفاظ بسجل المزامنة والطباعة. لن يتغير أي حساب على الراوتر. هل تريد المتابعة؟";
        var confirm = WorkflowFeedbackWindow.ShowNotice(
            Window.GetWindow(this), $"تأكيد {verb} الدفعة", message, WorkflowFeedbackKind.Warning,
            [new("الدفعة", row.DisplayName), new("الأثر", "تغيير حالة محلية فقط")],
            new WorkflowFeedbackAction("confirm", $"تأكيد {verb}", true), new WorkflowFeedbackAction("cancel", "إلغاء"));
        if (confirm != "confirm") return;

        var progress = WorkflowFeedbackWindow.ShowProgress(Window.GetWindow(this), $"جارٍ {verb} الدفعة", message, "حفظ التغيير…");
        try
        {
            await Task.Delay(180);
            _metadataStore.SetDisabled(row.BatchId, row.TargetSystem, disable, $"تم {verb} الدفعة محليًا ضمن الديمو دون اتصال خارجي.");
            RefreshFromSource(preserveSelection: true);
            SetOperationStatus($"تم {verb} الدفعة {row.DisplayName} محليًا ضمن الديمو.", isError: false);
            progress.Close();
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), $"تم {verb} الدفعة", $"تم {verb} الدفعة محليًا. لم يتم تغيير أي إعداد على الراوتر.",
                WorkflowFeedbackKind.Success, [new("الدفعة", row.DisplayName), new("الحالة", disable ? "معطلة محليًا" : "مفعّلة محليًا")],
                new WorkflowFeedbackAction("close", "إغلاق", true));
        }
        finally
        {
            if (progress.IsVisible) progress.Close();
        }
    }

    private void ArchiveOrRestoreBatch(BatchManagementRow row)
    {
        if (_metadataStore.StorageWarning is not null)
        {
            SetOperationStatus(_metadataStore.StorageWarning, isError: true);
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "تخزين البيانات يحتاج مراجعة", _metadataStore.StorageWarning,
                WorkflowFeedbackKind.Error, null, new WorkflowFeedbackAction("close", "إغلاق", true));
            return;
        }

        if (row.IsArchived)
        {
            var confirmRestore = WorkflowFeedbackWindow.ShowNotice(
                Window.GetWindow(this), "استعادة الدفعة من الأرشيف",
                $"سيتم استعادة الدفعة «{row.DisplayName}». الاستعادة لا تعني إعادة التفعيل؛ إذا كانت معطلة فستبقى معطلة حتى تفعيلها صراحةً.",
                WorkflowFeedbackKind.Warning,
                [new("الدفعة", row.DisplayName), new("تغيير التفعيل", "لن يتم تلقائيًا")],
                new WorkflowFeedbackAction("restore", "استعادة الدفعة", true), new WorkflowFeedbackAction("cancel", "إلغاء"));
            if (confirmRestore != "restore") return;

            _issuanceService.SetBatchCardsArchiveStatePreservingSyncStatus(row.BatchId, row.TargetSystem, archived: false);
            _metadataStore.SetArchived(row.BatchId, row.TargetSystem, archived: false, disableWhenArchiving: false, message: "تمت استعادة الدفعة من الأرشيف.");
            RefreshFromSource(preserveSelection: true);
            SetOperationStatus($"تمت استعادة الدفعة {row.DisplayName}. لم تتغير حالة المزامنة.", isError: false);
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "تمت استعادة الدفعة", "استُعيدت سجلات الدفعة دون تغيير حالة المزامنة أو التفعيل.",
                WorkflowFeedbackKind.Success, [new("الدفعة", row.DisplayName), new("حالة التفعيل", row.IsDisabled ? "تبقى معطلة" : "كما كانت")],
                new WorkflowFeedbackAction("close", "إغلاق", true));
            return;
        }

        if (row.SyncedAccountsCount > 0 && !row.AllCardsExpired)
        {
            var text = "لا يمكن أرشفة دفعة لها حسابات مسجلة كمتزامنة ولم تنتهِ صلاحيتها. عطّلها الآن، وأرشفها بعد انتهاء جميع كروتها.";
            SetOperationStatus(text, isError: true);
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "الأرشفة غير مسموحة", text,
                WorkflowFeedbackKind.Warning, [new("الدفعة", row.DisplayName), new("السبب", "توجد حسابات غير منتهية الصلاحية")],
                new WorkflowFeedbackAction("close", "فهمت", true));
            return;
        }

        var expired = row.AllCardsExpired;
        var archiveLabel = expired ? "منتهية ومؤرشفة" : "معطلة ومؤرشفة";
        var prompt = expired
            ? $"جميع كروت الدفعة «{row.DisplayName}» منتهية. سيتم أرشفة سجلاتها محليًا مع الاحتفاظ بسجل المزامنة."
            : $"هذه الدفعة غير مكتملة المزامنة. سيتم تعطيلها وأرشفتها محليًا. لن تتم محاكاة مزامنتها حتى استعادتها وإعادة تفعيلها.";
        var confirmArchive = WorkflowFeedbackWindow.ShowNotice(
            Window.GetWindow(this), "تأكيد أرشفة الدفعة", prompt, WorkflowFeedbackKind.Warning,
            [new("الدفعة", row.DisplayName), new("النتيجة", archiveLabel)],
            new WorkflowFeedbackAction("archive", "تأكيد الأرشفة", true), new WorkflowFeedbackAction("cancel", "إلغاء"));
        if (confirmArchive != "archive") return;

        _issuanceService.SetBatchCardsArchiveStatePreservingSyncStatus(row.BatchId, row.TargetSystem, archived: true,
            reason: expired ? "أرشفة بعد انتهاء جميع كروت الدفعة" : "تعطيل وأرشفة دفعة لم تتم مزامنتها");
        _metadataStore.SetArchived(row.BatchId, row.TargetSystem, archived: true, disableWhenArchiving: !expired,
            message: $"تمت أرشفة الدفعة بالحالة {archiveLabel}.");
        RefreshFromSource(preserveSelection: true);
        SetOperationStatus($"تمت أرشفة الدفعة {row.DisplayName}: {archiveLabel}.", isError: false);
        WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "تمت أرشفة الدفعة", "تم تحديث حالة الدفعة داخل Nexora فقط.",
            WorkflowFeedbackKind.Success, [new("الدفعة", row.DisplayName), new("الحالة", archiveLabel)],
            new WorkflowFeedbackAction("close", "إغلاق", true));
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
