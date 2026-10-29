using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;
using System.Globalization;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Cards.Views;
using Nexora.Demo.Desktop.Features.Printing.Views;
using Nexora.Demo.Desktop.Features.Sales.Services;
using Nexora.Demo.Desktop.Features.Sales.Views;
using Nexora.Demo.Desktop.Features.Shared;
using Nexora.Demo.Desktop.Features.Reports.Views;
using Nexora.Demo.Desktop.Features.Networks.Views;
using Nexora.Demo.Desktop.Features.NetworkOperations.Views;
using Nexora.Demo.Desktop.Features.NetworkOperations.Models;
using Nexora.Demo.Desktop.Features.Settings.Views;
using Nexora.Demo.Desktop.Features.Settings.Services;
using Nexora.Demo.Desktop.Features.Packages.Views;
using Nexora.Demo.Desktop.Features.NetworkOperations.Connections;
using System;
using System.Windows.Documents;

namespace Nexora.Demo.Desktop;

// Responsibility: Dashboard metrics, charts, status classification, and drilldowns.
public partial class MainWindow
{
    private void SalesChartDay_Click(object sender, RoutedEventArgs e)
    {
        if (_locationRequiredForCurrentAccount && !_locationSaved)
        {
            ShowLocationModal();
            return;
        }

        if (sender is not Button button)
            return;

        DateOnly day;
        if (button.Tag is DateOnly selectedDate)
            day = selectedDate;
        else if (button.Tag is string rawDate &&
                 DateOnly.TryParseExact(rawDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            day = parsedDate;
        else
            return;

        NavigateTo("reports");
        ActivateSectionTab("مركز التقارير");
        if (SectionFeatureContentHost.Content is ReportsCenterView reportsView)
            reportsView.FocusSalesDay(day);
    }
    private void DashboardRefreshButton_Click(object sender, RoutedEventArgs e) =>
        UpdateDashboardMetrics();
    private void DashboardBatchOpen_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string batchId ||
            string.IsNullOrWhiteSpace(batchId))
            return;

        if (_locationRequiredForCurrentAccount && !_locationSaved)
        {
            ShowLocationModal();
            return;
        }

        NavigateTo("cards");
        ActivateSectionTab("إدارة الدفعات");

        if (SectionFeatureContentHost.Content is CardBatchesView batchesView)
            batchesView.FocusBatch(batchId);
    }
    private void UpdateDashboardMetrics()
    {
        if (DashboardAvailableCardsMetricTextBlock is null ||
        DashboardIssuanceDaysItemsControl is null ||
        DashboardIssuanceChartFooterTextBlock is null ||
        DashboardIssuanceTotalMetricTextBlock is null ||
        DashboardIssuanceAverageMetricTextBlock is null ||
        DashboardIssuancePeakMetricTextBlock is null ||
        DashboardIssuancePeakDayTextBlock is null ||
        DashboardSalesDaysItemsControl is null ||
        DashboardSalesChartFooterTextBlock is null ||
        DashboardRecentBatchesGrid is null)
    {
        return;
    }

        var cards = _demoCardIssuanceService.Cards.ToArray();
        var total = cards.Length;
        var available = cards.Count(card => string.Equals(
            card.DemoStatus, "متاحة", StringComparison.Ordinal));
        var inUse = cards.Count(card => string.Equals(
            card.DemoStatus, "قيد الاستخدام", StringComparison.Ordinal));
        var expired = cards.Count(card => string.Equals(
            card.DemoStatus, "منتهية", StringComparison.Ordinal));
        var suspended = cards.Count(card => string.Equals(
            card.DemoStatus, "موقوفة", StringComparison.Ordinal));

        var batchRows = cards
            .GroupBy(card => Convert.ToString(card.BatchId, CultureInfo.CurrentCulture) ?? "—",
                StringComparer.Ordinal)
            .Select(group =>
            {
                var latestCard = group
                    .OrderByDescending(card => ToDashboardDateTime(card.CreatedAt))
                    .First();

                var syncStates = group
                    .Select(card => ClassifyDashboardSyncState(card.LocalStatus))
                    .ToArray();

                var attentionCards = syncStates.Count(state =>
                    state is DashboardSyncState.Failed or DashboardSyncState.Review);

                var hasPendingSync = syncStates.Any(state => state == DashboardSyncState.Pending);
                var statusLabel = attentionCards > 0
                    ? "تحتاج متابعة"
                    : hasPendingSync ? "قيد الإجراء" : "مسجلة";

                var packageName = group
                    .Select(card => Convert.ToString(card.PackageName, CultureInfo.CurrentCulture))
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "—";

                return new DashboardBatchRow(
                    group.Key,
                    packageName,
                    group.Count(),
                    FormatDashboardDate(latestCard.CreatedAt),
                    statusLabel,
                    ToDashboardDateTime(latestCard.CreatedAt),
                    attentionCards);
            })
            .OrderByDescending(batch => batch.SortAt)
            .ToArray();

        var attentionBatches = batchRows.Count(batch => batch.AttentionCardCount > 0);
        var attentionCardsCount = batchRows.Sum(batch => batch.AttentionCardCount);

        DashboardAvailableCardsMetricTextBlock.Text = available.ToString("N0", CultureInfo.CurrentCulture);
        DashboardAvailableCardsMetricDetailTextBlock.Text = total == 0
            ? "لا توجد كروت مسجلة"
            : $"{Percent(available, total):0.#}% من سجل الكروت";

        DashboardAttentionBatchesMetricTextBlock.Text =
            attentionBatches.ToString("N0", CultureInfo.CurrentCulture);
        DashboardAttentionBatchesMetricDetailTextBlock.Text = attentionBatches == 0
            ? "لا توجد دفعات تحتاج مراجعة"
            : $"{attentionCardsCount:N0} كرت ضمن الدفعات المتأثرة";

        DashboardInUseCardsMetricTextBlock.Text = inUse.ToString("N0", CultureInfo.CurrentCulture);
        DashboardInUseCardsMetricDetailTextBlock.Text = "وفق حالة الكرت المسجلة";
        DashboardRevenueMetricTextBlock.Text =
        $"{_demoSalesService.TotalRevenue:N0} ر.ي";

        DashboardCardsSoldMetricTextBlock.Text =
        _demoSalesService.TotalCardsSold.ToString("N0");


        // Daily sales chart sourced from the demo sales service.
        var salesDays = _demoSalesService.GetDailySummaries();

        DashboardSalesDaysItemsControl.ItemsSource = salesDays;

        DashboardSalesChartFooterTextBlock.Text = salesDays.Count == 0
            ? "لا توجد معاملات مبيعات في السيناريو الحالي."
            : $"سيناريو تجريبي · {salesDays[0].Day:dd/MM/yyyy} – " +
        $"{salesDays[salesDays.Count - 1].Day:dd/MM/yyyy} · " +
        $"إجمالي الفترة: {_demoSalesService.TotalRevenue.ToString("N0", CultureInfo.CurrentCulture)} ر.ي · بيانات غير حية.";

        // Card status panel: retain the existing status drill-down behavior.
        CardStatusTotalTextBlock.Text = $"إجمالي الكروت: {total:N0}";
        AvailableCardCountTextBlock.Text = available.ToString("N0", CultureInfo.CurrentCulture);
        InUseCardCountTextBlock.Text = inUse.ToString("N0", CultureInfo.CurrentCulture);
        ExpiredCardCountTextBlock.Text = expired.ToString("N0", CultureInfo.CurrentCulture);
        SuspendedCardCountTextBlock.Text = suspended.ToString("N0", CultureInfo.CurrentCulture);
        AvailableCardsProgressBar.Value = Percent(available, total);
        InUseCardsProgressBar.Value = Percent(inUse, total);
        ExpiredCardsProgressBar.Value = Percent(expired, total);
        SuspendedCardsProgressBar.Value = Percent(suspended, total);

        // Classify only records that explicitly mention sync/provisioning; never treat every local card as pending.
        var syncStatesForCards = cards
            .Select(card => ClassifyDashboardSyncState(card.LocalStatus))
            .Where(state => state != DashboardSyncState.Unknown)
            .ToArray();

        var syncSuccess = syncStatesForCards.Count(state => state == DashboardSyncState.Succeeded);
        var syncPending = syncStatesForCards.Count(state => state == DashboardSyncState.Pending);
        var syncFailed = syncStatesForCards.Count(state => state == DashboardSyncState.Failed);
        var syncReview = syncStatesForCards.Count(state => state == DashboardSyncState.Review);
        var trackedSyncCount = syncStatesForCards.Length;
        var syncSuccessPercent = trackedSyncCount == 0 ? 0 : syncSuccess * 100d / trackedSyncCount;

        DashboardSyncSuccessCountTextBlock.Text = syncSuccess.ToString("N0", CultureInfo.CurrentCulture);
        DashboardSyncPendingCountTextBlock.Text = syncPending.ToString("N0", CultureInfo.CurrentCulture);
        DashboardSyncFailedCountTextBlock.Text = syncFailed.ToString("N0", CultureInfo.CurrentCulture);
        DashboardSyncReviewCountTextBlock.Text = syncReview.ToString("N0", CultureInfo.CurrentCulture);
        DashboardSyncSuccessPercentTextBlock.Text = trackedSyncCount == 0
            ? "—"
            : $"{syncSuccessPercent:0}%";
        DashboardSyncProgressArcPath.Data = trackedSyncCount == 0
            ? Geometry.Empty
            : CreateDashboardArcGeometry(syncSuccessPercent);
        DashboardSyncFooterTextBlock.Text = trackedSyncCount == 0
            ? "لا توجد حالات مزامنة قابلة للتصنيف ضمن السجلات الحالية."
            : $"تم التصنيف من {trackedSyncCount:N0} سجلًا يحمل حالة تزويد أو مزامنة.";

        // Issuance analytics: current seven days versus the preceding seven days.
        var today = DateTime.Now.Date;
        var arabicCulture = CultureInfo.GetCultureInfo("ar");

        var dailyCounts = Enumerable.Range(0, 7)
            .Select(offset =>
            {
                var day = today.AddDays(offset - 6);
                var count = cards.Count(card =>
                    ToDashboardDateTime(card.CreatedAt).Date == day);

                return (Day: day, Count: count);
            })
            .ToArray();

// Previous seven calendar days: from today - 13 through today - 7.
        var previousWeekCounts = Enumerable.Range(0, 7)
            .Select(offset =>
            {
                var day = today.AddDays(offset - 13);
                return cards.Count(card =>
                    ToDashboardDateTime(card.CreatedAt).Date == day);
            })
            .ToArray();

        var currentTotal = dailyCounts.Sum(item => item.Count);
        var previousTotal = previousWeekCounts.Sum();
        var dailyAverage = currentTotal / 7d;

        var peakDay = dailyCounts
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Day)
            .First();

        // Summary metrics use locally recorded card creation dates.
        DashboardIssuanceTotalMetricTextBlock.Text =
            currentTotal.ToString("N0", CultureInfo.CurrentCulture);

        DashboardIssuanceAverageMetricTextBlock.Text =
            dailyAverage.ToString("N1", CultureInfo.CurrentCulture);

        DashboardIssuancePeakMetricTextBlock.Text =
            peakDay.Count.ToString("N0", CultureInfo.CurrentCulture);

        DashboardIssuancePeakDayTextBlock.Text = peakDay.Count == 0
            ? "لا يوجد إصدار مسجل"
            : peakDay.Day.ToString("dddd dd/MM", arabicCulture);

        // Keep the existing seven-day chart data contract.
        var maxDailyCount = Math.Max(1, dailyCounts.Max(item => item.Count));

        var chartRows = dailyCounts
            .Select(item => new DashboardIssuanceDay(
                item.Day.Date == today
                ? "اليوم"
                : item.Day.ToString("ddd", arabicCulture),
                item.Day.ToString("dd/MM", CultureInfo.CurrentCulture),
                item.Count,
                item.Count.ToString("N0", CultureInfo.CurrentCulture),
                item.Count == 0
                    ? 4
                    : Math.Max(6, 68d * item.Count / maxDailyCount)))
            .ToArray();
        DashboardIssuanceDaysItemsControl.ItemsSource = chartRows;

        // Compare with the preceding week without inventing a growth percentage
        // when that preceding period had no recorded issuance.
        if (previousTotal == 0)
        {
            DashboardIssuanceChartFooterTextBlock.Text = currentTotal == 0
                ? "آخر 7 أيام: لا يوجد إصدار مسجل · الأسبوع السابق: لا يوجد إصدار مسجل."
                : $"إجمالي آخر 7 أيام: {currentTotal:N0} كرت · " +
                "لم يُسجل إصدار في الأسبوع السابق للمقارنة.";
        }
        else
        {
            var difference = currentTotal - previousTotal;
            var changePercent = difference * 100d / previousTotal;

            var differenceText = difference.ToString(
                "+#,0;-#,0;0",
                CultureInfo.CurrentCulture);

            var percentageText = changePercent.ToString(
                "+0.0;-0.0;0.0",
                CultureInfo.CurrentCulture);

            DashboardIssuanceChartFooterTextBlock.Text =
                $"آخر 7 أيام: {currentTotal:N0} كرت · " +
                $"مقارنة بالأسبوع السابق: {differenceText} كرت " +
                $"({percentageText}%).";
        }

        // Recent batches and activity are derived from saved card records and the last verified router read.
        DashboardRecentBatchesGrid.ItemsSource = batchRows.Take(5).ToArray();
        DashboardRecentBatchesEmptyPanel.Visibility = batchRows.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        var activity = batchRows
            .Take(5)
            .Select(batch => new DashboardActivityRow(
                $"دفعة إصدار · {batch.BatchId}",
                $"{batch.CardCount:N0} كرت · {batch.PackageName}",
                batch.SortAt == DateTime.MinValue ? "—" : batch.SortAt.ToString("HH:mm", CultureInfo.CurrentCulture),
                "\uE8A5",
                batch.SortAt))
            .ToList();

        if (RouterConnectionSession.CurrentSnapshot is { } currentSnapshot)
        {
            var readAt = ToDashboardDateTime(currentSnapshot.CollectedAt);
            activity.Add(new DashboardActivityRow(
                "آخر قراءة للراوتر",
                $"{currentSnapshot.Identity} · {currentSnapshot.Interfaces.Count:N0} واجهة",
                readAt == DateTime.MinValue ? "—" : readAt.ToString("HH:mm", CultureInfo.CurrentCulture),
                "\uE968",
                readAt));
        }

        var recentActivity = activity
        .OrderByDescending(item => item.SortAt)
        .Take(3)
        .ToArray();

        DashboardRecentActivityItemsControl.ItemsSource = recentActivity;
        DashboardRecentActivityEmptyPanel.Visibility = recentActivity.Length == 0
            ? Visibility.Visible
            : Visibility.Collapsed;

        UpdateDashboardRouterStatus();
        var viewUpdatedAt = DateTime.Now.ToString("HH:mm:ss", CultureInfo.CurrentCulture);
        DashboardLastUpdatedTextBlock.Text = $"تحديث العرض {viewUpdatedAt}";
    }
    private void UpdateDashboardRouterStatus()
    {
        if (DashboardRouterStatusBadgeTextBlock is null ||
            DashboardNetworkStatusBadgeTextBlock is null)
            return;

        string statusLabel;
        string identity;
        string interfaceSummary;
        string activeInterfaceSummary;
        string lastRead;
        string helpText;
        Brush badgeBackground;
        Brush badgeForeground;
        Brush statusDot;

        if (RouterConnectionSession.CurrentSnapshot is { } snapshot)
        {
            statusLabel = "متصل";
            identity = string.IsNullOrWhiteSpace(snapshot.Identity) ? "راوتر متصل" : snapshot.Identity;
            interfaceSummary = snapshot.Interfaces.Count.ToString("N0", CultureInfo.CurrentCulture);
            activeInterfaceSummary = snapshot.Interfaces.Count(item => item.Running)
                .ToString("N0", CultureInfo.CurrentCulture);
            lastRead = $"آخر قراءة ناجحة: {snapshot.CollectedAt:HH:mm:ss}";
            helpText = "الحالة مبنية على آخر قراءة HTTPS ناجحة، وليست فحصًا لحظيًا مستمرًا.";
            badgeBackground = new SolidColorBrush(Color.FromRgb(220, 252, 231));
            badgeForeground = new SolidColorBrush(Color.FromRgb(22, 132, 91));
            statusDot = new SolidColorBrush(Color.FromRgb(22, 163, 106));
        }
        else if (_savedRouterProfile is not null)
        {
            statusLabel = "اتصال محفوظ";
            identity = _savedRouterProfile.Address;
            interfaceSummary = "—";
            activeInterfaceSummary = "—";
            lastRead = "لا توجد قراءة حالية";
            helpText = "بيانات الاتصال محفوظة، لكن لم يتم التحقق من حالة الراوتر في هذه الجلسة.";
            badgeBackground = new SolidColorBrush(Color.FromRgb(255, 244, 220));
            badgeForeground = new SolidColorBrush(Color.FromRgb(154, 103, 0));
            statusDot = new SolidColorBrush(Color.FromRgb(183, 121, 8));
        }
        else
        {
            statusLabel = "غير متصل";
            identity = "لم تُسجل قراءة ناجحة للراوتر";
            interfaceSummary = "—";
            activeInterfaceSummary = "—";
            lastRead = "آخر قراءة: غير متاحة";
            helpText = "اختبر اتصال HTTPS للراوتر لعرض بياناته الحقيقية هنا.";
            badgeBackground = new SolidColorBrush(Color.FromRgb(254, 226, 226));
            badgeForeground = new SolidColorBrush(Color.FromRgb(185, 28, 28));
            statusDot = new SolidColorBrush(Color.FromRgb(220, 38, 38));
        }

        // Detailed middle-row network panel.
        DashboardNetworkStatusBadge.Background = badgeBackground;
        DashboardNetworkStatusBadgeTextBlock.Text = statusLabel;
        DashboardNetworkStatusBadgeTextBlock.Foreground = badgeForeground;
        DashboardNetworkStatusDot.Fill = statusDot;
        DashboardNetworkIdentityTextBlock.Text = identity;
        DashboardNetworkInterfacesTextBlock.Text = interfaceSummary;
        DashboardNetworkActiveInterfacesTextBlock.Text = activeInterfaceSummary;
        DashboardNetworkLastReadTextBlock.Text = lastRead;
        DashboardNetworkStatusHelpTextBlock.Text = helpText;

        // Compact lower-row router connection summary, using the same source of truth.
        DashboardRouterStatusBadge.Background = badgeBackground;
        DashboardRouterStatusBadgeTextBlock.Text = statusLabel;
        DashboardRouterStatusBadgeTextBlock.Foreground = badgeForeground;
        DashboardRouterStatusDot.Fill = statusDot;
        DashboardRouterIdentityTextBlock.Text = identity;
        DashboardRouterInterfacesTextBlock.Text = statusLabel == "متصل"
            ? $"الواجهات المقروءة: {interfaceSummary}"
            : "الواجهات المقروءة: غير متاحة";
        DashboardRouterLastReadTextBlock.Text = lastRead;
        DashboardRouterStatusHelpTextBlock.Text = helpText;
    }
    private static DashboardSyncState ClassifyDashboardSyncState(object? rawStatus)
    {
        var status = Convert.ToString(rawStatus, CultureInfo.CurrentCulture) ?? string.Empty;
        if (status.Length == 0)
            return DashboardSyncState.Unknown;

        var mentionsProvisioning = status.Contains("مزامنة", StringComparison.OrdinalIgnoreCase)
            || status.Contains("تزويد", StringComparison.OrdinalIgnoreCase)
            || status.Contains("sync", StringComparison.OrdinalIgnoreCase)
            || status.Contains("provision", StringComparison.OrdinalIgnoreCase);

        if (!mentionsProvisioning)
            return DashboardSyncState.Unknown;

        if (status.Contains("فشل", StringComparison.OrdinalIgnoreCase)
            || status.Contains("تعذر", StringComparison.OrdinalIgnoreCase)
            || status.Contains("خطأ", StringComparison.OrdinalIgnoreCase))
            return DashboardSyncState.Failed;

        if (status.Contains("مراجعة", StringComparison.OrdinalIgnoreCase)
            || status.Contains("تعارض", StringComparison.OrdinalIgnoreCase)
            || status.Contains("غير مؤكد", StringComparison.OrdinalIgnoreCase))
            return DashboardSyncState.Review;

        if (status.Contains("نجح", StringComparison.OrdinalIgnoreCase)
            || status.Contains("ناجح", StringComparison.OrdinalIgnoreCase)
            || status.Contains("مكتملة", StringComparison.OrdinalIgnoreCase)
            || status.Contains("تمت مزامنة", StringComparison.OrdinalIgnoreCase))
            return DashboardSyncState.Succeeded;

        if (status.Contains("انتظار", StringComparison.OrdinalIgnoreCase)
            || status.Contains("معلق", StringComparison.OrdinalIgnoreCase)
            || status.Contains("قيد التنفيذ", StringComparison.OrdinalIgnoreCase)
            || status.Contains("مسجل", StringComparison.OrdinalIgnoreCase)
            || status.Contains("مطلوب", StringComparison.OrdinalIgnoreCase)
            || status.Contains("طلب", StringComparison.OrdinalIgnoreCase))
            return DashboardSyncState.Pending;

        // A status explicitly about provisioning but not understood must not be shown as successful.
        return DashboardSyncState.Pending;
    }
    private static DateTime ToDashboardDateTime(object? value)
    {
        if (value is DateTime dateTime)
            return dateTime;

        if (value is DateTimeOffset dateTimeOffset)
            return dateTimeOffset.LocalDateTime;

        var text = Convert.ToString(value, CultureInfo.CurrentCulture);
        return DateTime.TryParse(text, CultureInfo.CurrentCulture,
            DateTimeStyles.AssumeLocal, out var parsed)
            ? parsed
            : DateTime.MinValue;
    }
    private static string FormatDashboardDate(object? value)
    {
        var parsed = ToDashboardDateTime(value);
        return parsed == DateTime.MinValue
            ? "—"
            : parsed.ToString("dd/MM HH:mm", CultureInfo.CurrentCulture);
    }
    private static Geometry CreateDashboardArcGeometry(double percent)
    {
        var sweepAngle = Math.Clamp(percent, 0, 100) * 3.6;
        if (sweepAngle <= 0.1)
            return Geometry.Empty;

        // Avoid a zero-length end segment when the percentage is 100%.
        sweepAngle = Math.Min(359.8, sweepAngle);
        const double center = 47;
        const double radius = 37;

        static Point PointOnCircle(double cx, double cy, double r, double angleDegrees)
        {
            var radians = angleDegrees * Math.PI / 180d;
            return new Point(cx + r * Math.Cos(radians), cy + r * Math.Sin(radians));
        }

        var start = PointOnCircle(center, center, radius, -90);
        var end = PointOnCircle(center, center, radius, -90 + sweepAngle);
        var figure = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        figure.Segments.Add(new ArcSegment(
            end,
            new Size(radius, radius),
            0,
            sweepAngle > 180,
            SweepDirection.Clockwise,
            true));

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }
    private static double Percent(int value, int total) => total <= 0 ? 0 : value * 100d / total;
    private void CardStatusDrilldown_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string selectedStatus) return;
        if (_locationRequiredForCurrentAccount && !_locationSaved)
        {
            ShowLocationModal();
            return;
        }

        var matchedCards = _demoCardIssuanceService.Cards
            .Where(card => string.Equals(card.DemoStatus, selectedStatus, StringComparison.Ordinal))
            .OrderByDescending(card => card.CreatedAt)
            .ToArray();

        CardStatusDrilldownTitleTextBlock.Text = $"سجلات الكروت: {selectedStatus}";
        CardStatusDrilldownSummaryTextBlock.Text =
            $"عدد الكروت بهذه الحالة: {matchedCards.Length:N0}.";
        var workArea = SystemParameters.WorkArea;
        CardStatusDrilldownDialogBorder.Width = Math.Min(1240, Math.Max(420, workArea.Width - 48));
        CardStatusDrilldownDialogBorder.Height = Math.Min(680, Math.Max(350, workArea.Height - 48));
        CardStatusDrilldownGrid.ItemsSource = matchedCards;
        CardStatusDrilldownOverlay.Visibility = Visibility.Visible;
        CardStatusDrilldownGrid.Focus();
    }
    private void CloseCardStatusDrilldown_Click(object sender, RoutedEventArgs e)
    {
        CardStatusDrilldownOverlay.Visibility = Visibility.Collapsed;
        CardStatusDrilldownGrid.ItemsSource = null;
    }
}
