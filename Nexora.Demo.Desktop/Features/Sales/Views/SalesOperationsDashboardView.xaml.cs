using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Nexora.Demo.Desktop.Features.Sales.Models;
using Nexora.Demo.Desktop.Features.Sales.Services;
using Nexora.Demo.Desktop.Features.Shared;

namespace Nexora.Demo.Desktop.Features.Sales.Views;

public partial class SalesOperationsDashboardView : UserControl
{
    private readonly SalesOperationsService _operationsService;
    private readonly SalesPointDirectoryService _pointsService;

    public SalesOperationsDashboardView(SalesOperationsService operationsService, SalesPointDirectoryService pointsService)
    {
        ArgumentNullException.ThrowIfNull(operationsService);
        ArgumentNullException.ThrowIfNull(pointsService);
        InitializeComponent();
        _operationsService = operationsService;
        _pointsService = pointsService;
        _operationsService.Changed += Source_Changed;
        _pointsService.Changed += Source_Changed;
        RefreshDashboard();
    }

    public event EventHandler<FeatureNavigationRequestedEventArgs>? NavigationRequested;

    private void Source_Changed(object? sender, EventArgs e) => RefreshDashboard();
    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshDashboard();
    private void NewSaleButton_Click(object sender, RoutedEventArgs e) => Navigate("تسجيل البيع");
    private void ReportsButton_Click(object sender, RoutedEventArgs e) => Navigate("تقارير المبيعات");
    private void TransferButton_Click(object sender, RoutedEventArgs e) => Navigate("حركة الكروت");
    private void AccountsButton_Click(object sender, RoutedEventArgs e) => Navigate("حسابات نقاط البيع");
    private void LedgerButton_Click(object sender, RoutedEventArgs e) => Navigate("سجل العمليات");

    private void Navigate(string tab) => NavigationRequested?.Invoke(this,
        new FeatureNavigationRequestedEventArgs("sales", tab, string.Empty, "none"));

    private void RefreshDashboard()
    {
        var operations = _operationsService.GetOperations();
        var transfers = _operationsService.GetTransfers();
        var expenses = _operationsService.GetExpenses();
        var otherExpenses = expenses.Where(item => !string.Equals(item.Category, "طباعة وخدش", StringComparison.Ordinal)).Sum(item => item.Amount);
        var points = _pointsService.GetAll();
        var accounts = _operationsService.GetPointAccounts(points);
        var today = DateOnly.FromDateTime(DateTime.Today);
        var todaySales = operations.Where(item => DateOnly.FromDateTime(item.OccurredAt.LocalDateTime) == today).ToArray();
        var salesGross = operations.Sum(item => item.GrossAmount);
        var transferGross = transfers.Sum(item => item.GrossAmount);
        var totalPrinting = operations.Sum(item => item.PrintingExpense) + transfers.Sum(item => item.PrintingExpense);
        var totalCardsPrepared = operations.Where(item => item.RequiresPrinting).Sum(item => item.Quantity) + transfers.Sum(item => item.Quantity);
        var outstandingPoints = accounts.Where(row => row.Balance > 0m).ToArray();

        GrossSalesTextBlock.Text = $"{salesGross + transferGross:N0} ريال";
        SalesCountTextBlock.Text = $"{operations.Count:N0} عملية بيع مسجلة";
        ReceivablesTextBlock.Text = $"{outstandingPoints.Sum(row => row.Balance):N0} ريال";
        ReceivablesDetailTextBlock.Text = $"{outstandingPoints.Length:N0} نقطة عليها رصيد";
        CentralCollectionTextBlock.Text = $"{_operationsService.CentralCollectionExpected:N0} ريال";
        CentralCollectionDetailTextBlock.Text = operations.Any(item => item.ChannelKey is "wallet" or "network-cards") ? "إجمالي التجميع المتوقع" : "لا توجد عمليات محصلة حالياً";
        PrintingCostsTextBlock.Text = $"{totalPrinting:N0} ريال";
        PrintingCardsTextBlock.Text = $"{totalCardsPrepared:N0} كرت مطبوع أو مختوم";
        var localArabic = CultureInfo.GetCultureInfo("ar-YE");
        var dateLine = $"اليوم {DateTime.Today.ToString("dddd yyyy/MM/dd", localArabic)} · {todaySales.Length:N0} عملية بيع مسجلة · كافة المؤشرات محدثة لحظياً من عمليات البيع ونقاط البيع.";
        OtherExpensesTextBlock.Text = $"{otherExpenses:N0} ريال";
        OtherExpensesDetailTextBlock.Text = otherExpenses == 0m ? "لا توجد مصروفات أخرى" : $"{expenses.Count(item => !string.Equals(item.Category, "طباعة وخدش", StringComparison.Ordinal)):N0} مصروفات أخرى مسجلة";
        var canWriteEverything = _operationsService.CanWrite && _pointsService.CanWrite;

        var channelRows = operations.GroupBy(item => item.ChannelKey)
            .Select(group => new ChannelSummaryRow(
                ChannelLabelFor(group.Key), group.Count(), group.Sum(item => item.GrossAmount),
                group.Sum(item => item.CommissionAmount), group.Sum(item => item.NetworkAmount)))
            .ToList();
        if (transfers.Count > 0)
        {
            channelRows.Add(new ChannelSummaryRow(
                "البقالات ونقاط البيع", transfers.Count, transfers.Sum(item => item.GrossAmount),
                transfers.Sum(item => item.CommissionAmount), transfers.Sum(item => item.NetAmount)));
        }
        var orderedChannelRows = channelRows.OrderByDescending(row => row.GrossAmount).ToArray();
        var maxChannelGross = orderedChannelRows.Length == 0 ? 0m : orderedChannelRows.Max(row => row.GrossAmount);
        var chartRows = orderedChannelRows.Select(row => row with
        {
            BarHeight = maxChannelGross <= 0m ? 0d : Math.Clamp((double)(row.GrossAmount / maxChannelGross) * 66d, 5d, 66d)
        }).ToArray();
        ChannelsGrid.ItemsSource = chartRows;
        DirectChannelCountTextBlock.Text = $"{operations.Count(item => item.ChannelKey == "direct"):N0} عملية";
        PointChannelCountTextBlock.Text = $"{transfers.Count:N0} عملية";
        NetworkChannelCountTextBlock.Text = $"{operations.Count(item => item.ChannelKey == "network-cards"):N0} عملية";
        WalletChannelCountTextBlock.Text = $"{operations.Count(item => item.ChannelKey == "wallet"):N0} عملية";

        ReceivablesGrid.ItemsSource = outstandingPoints.Take(8).ToArray();

        var history = new List<SalesHistoryRow>();
        history.AddRange(operations.Select(item => new SalesHistoryRow(item.OccurredAt, item.Reference,
            "بيع", item.ChannelLabel, item.PointName, $"{item.SourceBatchId} · {item.PackageName}", item.Quantity, item.GrossAmount,
            item.CommissionAmount, item.NetworkAmount, item.AmountReceived, item.OutstandingAmount, item.CollectionStatus)));
        history.AddRange(transfers.Select(item => new SalesHistoryRow(item.OccurredAt, item.Reference,
            "توريد دفعة", "نقاط البيع", item.PointName, $"{item.BatchId} · {item.PackageName}", item.Quantity,
            item.GrossAmount, item.CommissionAmount, item.NetAmount, item.PaidAtTransfer, item.Balance,
            item.Balance > 0m ? "رصيد مستحق" : "مسدد")));
        history.AddRange(_operationsService.GetSettlements().Select(item => new SalesHistoryRow(item.OccurredAt,
            item.Reference, "تسوية", "تحصيل نقطة", item.PointName, item.PaymentMethod, 0, item.Amount, 0m,
            item.Amount, item.Amount, 0m, "تم التحصيل")));
        RecentGrid.ItemsSource = history.OrderByDescending(row => row.OccurredAt).Take(8).ToArray();
    }

    private static string ChannelLabelFor(string key) => key switch
    {
        "direct" => "بيع مباشر",
        "network-cards" => "كروت الشبكات",
        "wallet" => "محافظ إلكترونية",
        _ => key
    };

    public sealed record ChannelSummaryRow(string ChannelLabel, int Count, decimal GrossAmount, decimal FeeAmount, decimal NetAmount)
    {
        public double BarHeight { get; init; }
        public string CountLabel => Count.ToString("N0", CultureInfo.CurrentCulture);
        public string OperationsLabel => $"{CountLabel} عملية";
        public string GrossLabel => $"{GrossAmount:N0} ريال";
        public string FeeLabel => $"{FeeAmount:N0} ريال";
        public string NetLabel => $"{NetAmount:N0} ريال";
    }
}
