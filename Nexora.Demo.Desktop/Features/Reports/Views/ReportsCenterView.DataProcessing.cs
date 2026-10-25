using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Sales.Models;
using Nexora.Demo.Desktop.Features.Sales.Services;
using Nexora.Demo.Desktop.Features.Shared;

namespace Nexora.Demo.Desktop.Features.Reports.Views;

// Responsibility: Report projection, source refresh, metrics, and export conversion.
public partial class ReportsCenterView
{
    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshReport();
    private void RefreshReport()
    {
        if (ReportGrid is null || ReportTypeComboBox.SelectedItem is not ComboBoxItem reportItem)
            return;

        _reportKey = reportItem.Tag?.ToString() ?? "cards";
        var sourceKey = SelectedTag(SourceFilterComboBox);
        var statusKey = SelectedTag(StatusFilterComboBox);
        var search = SearchTextBox.Text?.Trim() ?? string.Empty;
        var from = FromDatePicker.SelectedDate?.Date;
        var to = ToDatePicker.SelectedDate?.Date;
        if (from.HasValue && to.HasValue && from.Value > to.Value)
        {
            EmptyStatePanel.Visibility = Visibility.Visible;
            ReportGrid.Visibility = Visibility.Collapsed;
            EmptyStateTitleTextBlock.Text = "نطاق التاريخ غير صحيح";
            EmptyStateDescriptionTextBlock.Text = "تأكد أن تاريخ البداية يسبق تاريخ النهاية.";
            _visibleRows = [];
            SetMetrics("عدد النتائج", "0", "نطاق التاريخ", "غير صحيح", "المصدر", "—");
            ResultSummaryTextBlock.Text = "لم يتم تنفيذ التصفية؛ تاريخ البداية بعد تاريخ النهاية.";
            return;
        }

        ConfigureReportColumns(_reportKey);
        SourceFilterLabelTextBlock.Visibility = _reportKey == "sales" ? Visibility.Collapsed : Visibility.Visible;
        SourceFilterComboBox.Visibility = _reportKey == "sales" ? Visibility.Collapsed : Visibility.Visible;
        StatusFilterLabelTextBlock.Visibility = _reportKey == "cards" ? Visibility.Visible : Visibility.Collapsed;
        StatusFilterComboBox.Visibility = _reportKey == "cards" ? Visibility.Visible : Visibility.Collapsed;
        _visibleRows = _reportKey switch
        {
            "cards" => BuildCardRows(sourceKey, statusKey, search, from, to),
            "batches" => BuildBatchRows(sourceKey, search, from, to),
            "sales" => BuildSalesRows(search, from, to),
            "printing" => BuildPrintRows(sourceKey, search, from, to),
            "actions" => BuildActionRows(sourceKey, search, from, to, syncOnly: false),
            "sync" => BuildActionRows(sourceKey, search, from, to, syncOnly: true),
            "usage" => [],
            _ => []
        };

        ReportGrid.ItemsSource = _visibleRows;
        var hasRows = _visibleRows.Length > 0;
        ReportGrid.Visibility = hasRows ? Visibility.Visible : Visibility.Collapsed;
        EmptyStatePanel.Visibility = hasRows ? Visibility.Collapsed : Visibility.Visible;
        EmptyStateTitleTextBlock.Text = "لا توجد نتائج مطابقة";
        EmptyStateDescriptionTextBlock.Text = _reportKey == "usage"
            ? "لا تتوفر في مصادر البيانات الحالية سجلات جلسات واستهلاك يمكن عرضها لهذا التقرير."
            : search.Length > 0
                ? "لا توجد سجلات تطابق البحث الحالي مع بقية الفلاتر. غيّر البحث أو امسحه."
                : "لا توجد سجلات في هذا النطاق من المصدر المحدد.";

        UpdateMetrics(_reportKey, _visibleRows);
        var count = _visibleRows.Length;
        var sourceLabel = _reportKey == "sales" ? "سجلات المبيعات" : sourceKey switch
        {
            "hotspot" => "HotSpot",
            "user-manager" => "User Manager",
            _ => "كل المصادر ذات الصلة"
        };
        var rangeText = $"{(from.HasValue ? from.Value.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) : "بداية غير محددة")} — {(to.HasValue ? to.Value.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture) : "نهاية غير محددة")}";
        ResultSummaryTextBlock.Text = $"{count:N0} نتيجة · {sourceLabel} · {rangeText}";
        ReportUpdatedAtTextBlock.Text = $"آخر تحديث للعرض: {DateTime.Now:HH:mm:ss}";
        ReportSourceBadgeTextBlock.Text = _reportKey switch
        {
            "sales" => "مصدر: معاملات المبيعات",
            "printing" => "مصدر: قائمة الطباعة وسجل المهام",
            "actions" => "مصدر: سجل إجراءات الكروت",
            "sync" => "مصدر: سجل طلبات المزامنة",
            "usage" => "مصدر: سجلات الاستخدام غير متاحة",
            _ => "مصدر: سجلات Nexora"
        };
    }
    private ReportTableRow[] BuildCardRows(string sourceKey, string statusKey, string search, DateTime? from, DateTime? to) =>
        _issuanceService.Cards
            .Where(card => MatchesSystem(card.TargetSystem, sourceKey))
            .Where(card => statusKey == "all" || card.DemoStatus == statusKey)
            .Where(card => WithinRange(card.CreatedAt.DateTime, from, to))
            .Where(card => Matches(search, card.CardNumber, card.Username, card.TargetSystem, card.PackageName, card.BatchId, card.PointOfSale, card.DemoStatus))
            .OrderByDescending(card => card.CreatedAt)
            .Select(card => new ReportTableRow(
                card.CardNumber, card.Username, card.TargetSystem, card.PackageName, card.BatchId,
                card.DemoStatus, card.CreatedAtLabel, card.CardNumber, "card", SystemKey(card.TargetSystem)))
            .ToArray();
    private ReportTableRow[] BuildBatchRows(string sourceKey, string search, DateTime? from, DateTime? to) =>
        _issuanceService.Batches
            .Where(batch => MatchesSystem(batch.TargetSystem, sourceKey))
            .Where(batch => WithinRange(batch.CreatedAt.DateTime, from, to))
            .Where(batch => Matches(search, batch.BatchId, batch.TargetSystem, batch.PackageName, batch.PointOfSale, batch.LocalStatus))
            .OrderByDescending(batch => batch.CreatedAt)
            .Select(batch => new ReportTableRow(
                batch.BatchId, batch.TargetSystem, batch.PackageName, batch.Quantity.ToString("N0", CultureInfo.CurrentCulture),
                $"{batch.UnitPrice:N0} ريال", batch.TotalLabel, batch.CreatedAtLabel, batch.BatchId, "batch-details", SystemKey(batch.TargetSystem)))
            .ToArray();
    private ReportTableRow[] BuildSalesRows(string search, DateTime? from, DateTime? to) =>
        _salesService.GetAll()
            .Where(row => WithinRange(row.OccurredAt, from, to))
            .Where(row => Matches(search, row.TransactionNumber, row.PackageName, row.PointOfSale, row.PaymentMethod, row.OperatorName, row.Status))
            .OrderByDescending(row => row.OccurredAt)
            .Select(row => new ReportTableRow(
                row.TransactionNumber, row.OccurredAtLabel, row.PackageName, row.Quantity.ToString("N0", CultureInfo.CurrentCulture),
                row.PointOfSale, row.PaymentMethod, row.TotalLabel, row.TransactionNumber, "sale", null))
            .ToArray();
    private ReportTableRow[] BuildPrintRows(string sourceKey, string search, DateTime? from, DateTime? to)
    {
        var pendingRows = _workflowStore.GetPendingPrintSelection()
            .Where(item => MatchesSystem(item.TargetSystem, sourceKey))
            .Select(item => new
            {
                Item = item,
                Card = _issuanceService.Cards.FirstOrDefault(card =>
                    string.Equals(card.BatchId, item.BatchId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(card.CardNumber, item.CardNumber, StringComparison.OrdinalIgnoreCase) &&
                    SystemKey(card.TargetSystem) == SystemKey(item.TargetSystem))
            })
            .Where(entry => entry.Card is not null)
            .Where(entry => WithinRange((entry.Item.QueuedAt == default ? entry.Card!.CreatedAt : entry.Item.QueuedAt).DateTime, from, to))
            .Where(entry => Matches(search, entry.Item.CardNumber, entry.Item.BatchId, entry.Card!.PackageName, entry.Item.TargetSystem, entry.Card.PointOfSale))
            .Select(entry => new ReportTableRow(
                entry.Item.CardNumber, entry.Item.BatchId, entry.Card!.PackageName, entry.Card.TargetSystem,
                entry.Card.PointOfSale, (entry.Item.QueuedAt == default ? entry.Card.CreatedAt : entry.Item.QueuedAt).ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture), "بانتظار الطباعة", entry.Item.CardNumber, "card", SystemKey(entry.Card.TargetSystem)))
            .ToList();

        var submittedRows = _workflowStore.GetActionHistory()
            .Where(action => action.ActionType == "PrintSubmitted")
            .Where(action => MatchesSystem(action.TargetSystem, sourceKey))
            .Where(action => WithinRange(action.CreatedAt.DateTime, from, to))
            .Where(action => Matches(search, action.ActionLabel, action.BatchId, action.TargetSystem, action.Description))
            .Select(action => new ReportTableRow(
                action.ActionLabel, action.BatchId, action.TargetSystem, action.Count.ToString("N0", CultureInfo.CurrentCulture),
                action.Description, action.CreatedAtLabel, "أُرسلت إلى الطابعة", action.BatchId, "batch", SystemKey(action.TargetSystem)))
            .ToList();

        return pendingRows.Concat(submittedRows)
            .OrderByDescending(row => row.Value6, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
    private ReportTableRow[] BuildActionRows(string sourceKey, string search, DateTime? from, DateTime? to, bool syncOnly) =>
        _workflowStore.GetActionHistory()
            .Where(action => !syncOnly || action.ActionType == "SyncRequested")
            .Where(action => MatchesSystem(action.TargetSystem, sourceKey))
            .Where(action => WithinRange(action.CreatedAt.DateTime, from, to))
            .Where(action => Matches(search, action.ActionLabel, action.BatchId, action.TargetSystem, action.Description, action.ActionType))
            .OrderByDescending(action => action.CreatedAt)
            .Select(action => new ReportTableRow(
                action.ActionLabel, action.TargetSystem, action.BatchId, action.Count.ToString("N0", CultureInfo.CurrentCulture),
                action.CreatedAtLabel, syncOnly ? "تم تسجيل طلب المزامنة" : action.ActionType, action.Description,
                action.BatchId, "batch", SystemKey(action.TargetSystem)))
            .ToArray();
    private void ConfigureReportColumns(string reportKey)
    {
        var configuration = reportKey switch
        {
            "batches" => new[] { "رقم الدفعة", "جهة الإصدار", "الباقة", "عدد الكروت", "سعر الوحدة", "قيمة الإصدار", "تاريخ الإنشاء" },
            "sales" => new[] { "رقم العملية", "تاريخ البيع", "الباقة", "الكمية", "نقطة البيع", "طريقة الدفع", "الإجمالي" },
            "printing" => new[] { "رقم الكرت", "رقم الدفعة", "الباقة", "جهة الإصدار", "نقطة البيع", "تاريخ الإضافة", "حالة القائمة" },
            "actions" => new[] { "العملية", "المصدر", "رقم الدفعة", "عدد العناصر", "وقت العملية", "نوع السجل", "التفاصيل" },
            "sync" => new[] { "الطلب", "جهة الإصدار", "رقم الدفعة", "العناصر", "وقت التسجيل", "الحالة", "التفاصيل" },
            _ => new[] { "رقم الكرت", "اسم المستخدم", "جهة الإصدار", "الباقة", "رقم الدفعة", "حالة الحساب", "تاريخ الإنشاء" }
        };
        var columns = new[] { ColumnOne, ColumnTwo, ColumnThree, ColumnFour, ColumnFive, ColumnSix, ColumnSeven };
        for (var index = 0; index < columns.Length; index++)
        {
            columns[index].Header = configuration[index];
            columns[index].Visibility = Visibility.Visible;
        }
        ReportGrid.ItemsSource = null;
    }
    private void UpdateMetrics(string reportKey, IReadOnlyCollection<ReportTableRow> rows)
    {
        switch (reportKey)
        {
            case "sales":
            {
                var numbers = rows.Select(row => row.ContextValue)
                    .Select(id => _salesService.GetAll().FirstOrDefault(sale => sale.TransactionNumber == id))
                    .Where(sale => sale is not null).Select(sale => sale!).ToArray();
                SetMetrics("إجمالي المبيعات", $"{numbers.Sum(sale => sale.Total):N0} ريال",
                    "معاملات البيع", numbers.Length.ToString("N0", CultureInfo.CurrentCulture),
                    "الكروت المباعة", numbers.Sum(sale => sale.Quantity).ToString("N0", CultureInfo.CurrentCulture));
                break;
            }
            case "batches":
            {
                var ids = rows.Select(row => row.ContextValue).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var batches = _issuanceService.Batches.Where(batch => ids.Contains(batch.BatchId)).ToArray();
                SetMetrics("عدد الدفعات", batches.Length.ToString("N0", CultureInfo.CurrentCulture),
                    "عدد الكروت المُنشأة", batches.Sum(batch => batch.Quantity).ToString("N0", CultureInfo.CurrentCulture),
                    "قيمة الإصدار المسجلة", $"{batches.Sum(batch => batch.Total):N0} ريال");
                break;
            }
            case "printing":
                SetMetrics("عناصر في قائمة الطباعة", rows.Count(row => row.Value7 == "بانتظار الطباعة").ToString("N0", CultureInfo.CurrentCulture),
                    "مهام أُرسلت للطابعة", rows.Count(row => row.Value7 == "أُرسلت إلى الطابعة").ToString("N0", CultureInfo.CurrentCulture),
                    "إجمالي السجلات", rows.Count.ToString("N0", CultureInfo.CurrentCulture));
                break;
            case "usage":
                SetMetrics("الوقت المستخدم", "غير متاح", "حركة البيانات", "غير متاحة", "الجلسات", "مصدر غير متاح");
                break;
            case "actions":
            case "sync":
                SetMetrics("عدد العمليات", rows.Count.ToString("N0", CultureInfo.CurrentCulture),
                    "العناصر المرتبطة", rows.Sum(row => int.TryParse(row.Value4, NumberStyles.Any, CultureInfo.CurrentCulture, out var count) ? count : 0).ToString("N0", CultureInfo.CurrentCulture),
                    "نوع السجل", reportKey == "sync" ? "الربط" : "إجراءات النظام");
                break;
            default:
                SetMetrics("عدد الكروت", rows.Count.ToString("N0", CultureInfo.CurrentCulture),
                    "الدفعات المرتبطة", rows.Select(row => row.Value5).Distinct(StringComparer.OrdinalIgnoreCase).Count().ToString("N0", CultureInfo.CurrentCulture),
                    "قيد الاستخدام", rows.Count(row => row.Value6 == "قيد الاستخدام").ToString("N0", CultureInfo.CurrentCulture));
                break;
        }
    }
    private void SetMetrics(string l1, string v1, string l2, string v2, string l3, string v3)
    {
        MetricOneLabelTextBlock.Text = l1; MetricOneValueTextBlock.Text = v1;
        MetricTwoLabelTextBlock.Text = l2; MetricTwoValueTextBlock.Text = v2;
        MetricThreeLabelTextBlock.Text = l3; MetricThreeValueTextBlock.Text = v3;
    }
    private int GetReportColumnValueIndex(DataGridColumn column)
    {
        var member = column.SortMemberPath;
        if (!string.IsNullOrWhiteSpace(member) && member.StartsWith("Value", StringComparison.Ordinal) &&
            int.TryParse(member.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out var index) && index is >= 1 and <= 7)
            return index - 1;
        return Math.Clamp(ReportColumnFallbackIndex(column), 0, 6);
    }
    private int ReportColumnFallbackIndex(DataGridColumn column) => Math.Max(0, ReportGrid.Columns.IndexOf(column));
    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}
