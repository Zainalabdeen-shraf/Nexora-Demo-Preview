using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nexora.Demo.Desktop.Features.Sales.Services;
using Nexora.Demo.Desktop.Features.Shared;

namespace Nexora.Demo.Desktop.Features.Sales.Views;

public partial class SalesPointsView : UserControl
{
    private readonly DemoSalesService _salesService;
    private PointSaleRow[] _visibleRows = [];
    private bool _initializing = true;

    public SalesPointsView(DemoSalesService salesService)
    {
        ArgumentNullException.ThrowIfNull(salesService);
        InitializeComponent();
        _salesService = salesService;
        _initializing = false;
        RefreshRows();
    }

    public event EventHandler<FeatureNavigationRequestedEventArgs>? NavigationRequested;

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_initializing && PointsGrid is not null) RefreshRows();
    }
    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshRows();

    private void RefreshRows()
    {
        if (PointsGrid is null) return;
        var transactions = _salesService.GetAll();
        var query = transactions.GroupBy(row => row.PointOfSale, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new PointSaleRow(group.Key, group.Count(), group.Sum(row => row.Quantity), group.Sum(row => row.Total),
                group.Count() == 0 ? 0 : group.Sum(row => row.Total) / group.Count(), group.Max(row => row.OccurredAt),
                group.OrderByDescending(row => row.OccurredAt).First().TransactionNumber));
        var search = SearchTextBox.Text?.Trim() ?? string.Empty;
        if (search.Length > 0)
            query = query.Where(row => row.PointOfSale.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        _visibleRows = query.OrderByDescending(row => row.Revenue).ToArray();
        PointsGrid.ItemsSource = _visibleRows;
        var hasRows = _visibleRows.Length > 0;
        PointsGrid.Visibility = hasRows ? Visibility.Visible : Visibility.Collapsed;
        EmptyStatePanel.Visibility = hasRows ? Visibility.Collapsed : Visibility.Visible;
        PointsCountTextBlock.Text = _visibleRows.Length.ToString("N0", CultureInfo.CurrentCulture);
        var totalTransactionCount = _visibleRows.Sum(row => row.TransactionCount);
        TransactionsCountTextBlock.Text = totalTransactionCount.ToString("N0", CultureInfo.CurrentCulture);
        TotalSalesTextBlock.Text = $"{_visibleRows.Sum(row => row.Revenue):N0} ريال";
        ResultSummaryTextBlock.Text = $"عرض {_visibleRows.Length:N0} نقطة بيع · {_visibleRows.Sum(row => row.CardsSold):N0} كرت ضمن معاملات البيع المطابقة.";
    }

    private void PointsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PointsGrid.SelectedItem is not PointSaleRow row) return;
        NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("sales", "معاملات البيع", row.PointOfSale, "point"));
    }

    private void PointsGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        // WPF sorts the current ListCollectionView by the selected bound property.
    }

    public sealed record PointSaleRow(string PointOfSale, int TransactionCount, int CardsSold, decimal Revenue, decimal Average, DateTime LatestSale, string LatestTransaction)
    {
        public string RevenueLabel => $"{Revenue:N0} ريال";
        public string AverageLabel => $"{Average:N0} ريال";
        public string LatestSaleLabel => LatestSale.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);
    }
}
