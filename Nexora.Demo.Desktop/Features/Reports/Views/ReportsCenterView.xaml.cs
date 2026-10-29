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

/// <summary>Unified report workspace over the demo's existing issuance, sales, print and workflow records.</summary>
public partial class ReportsCenterView : UserControl
{
    private readonly DemoCardIssuanceService _issuanceService;
    private readonly DemoSalesService _salesService;
    private readonly DemoCardWorkflowStore _workflowStore = new();
    private ReportTableRow[] _visibleRows = [];
    private string _reportKey = "cards";
    private bool _refreshing = true;
    private bool _isSubscribed;

    public ReportsCenterView(DemoCardIssuanceService issuanceService, DemoSalesService salesService)
    {
        ArgumentNullException.ThrowIfNull(issuanceService);
        ArgumentNullException.ThrowIfNull(salesService);
        InitializeComponent();
        _issuanceService = issuanceService;
        _salesService = salesService;
        FromDatePicker.SelectedDate = DateTime.Today.AddDays(-30);
        ToDatePicker.SelectedDate = DateTime.Today;
        Loaded += ReportsCenterView_Loaded;
        Unloaded += ReportsCenterView_Unloaded;
        _refreshing = false;
        RefreshReport();
    }

    public event EventHandler<FeatureNavigationRequestedEventArgs>? NavigationRequested;

    public void FocusSalesDay(DateOnly day)
    {
        _refreshing = true;
        try
        {
            ReportTypeComboBox.SelectedIndex = 2;
            SourceFilterComboBox.SelectedIndex = 0;
            StatusFilterComboBox.SelectedIndex = 0;
            SearchTextBox.Clear();
            FromDatePicker.SelectedDate = day.ToDateTime(TimeOnly.MinValue);
            ToDatePicker.SelectedDate = day.ToDateTime(TimeOnly.MaxValue);
        }
        finally
        {
            _refreshing = false;
        }
        RefreshReport();
    }

    private void ReportsCenterView_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_isSubscribed)
        {
            _issuanceService.Cards.CollectionChanged += Source_Changed;
            _issuanceService.Batches.CollectionChanged += Source_Changed;
            _isSubscribed = true;
        }
        RefreshReport();
    }

    private void ReportsCenterView_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_isSubscribed)
        {
            _issuanceService.Cards.CollectionChanged -= Source_Changed;
            _issuanceService.Batches.CollectionChanged -= Source_Changed;
            _isSubscribed = false;
        }
    }

    private void Source_Changed(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        Dispatcher.InvokeAsync(RefreshReport);


    private sealed record ReportTableRow(
        string Value1, string Value2, string Value3, string Value4, string Value5, string Value6, string Value7,
        string ContextValue, string ContextKind, string? SystemKey)
    {
        public IReadOnlyList<string> Values => [Value1, Value2, Value3, Value4, Value5, Value6, Value7];
    }
}
