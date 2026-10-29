using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Calendar = System.Windows.Controls.Calendar;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Sales.Services;

namespace Nexora.Demo.Desktop.Features.Sales.Views;

/// <summary>
/// Card-level sales status view. One row represents one issued card; batch-level totals
/// are deliberately not presented as if they were individual card states.
/// </summary>
public partial class SalesCardMovementView : UserControl
{
    private readonly DemoCardIssuanceService _cardService;
    private readonly SalesPointDirectoryService _pointsService;
    private readonly SalesOperationsService _operationsService;
    private CardMovementRow[] _allRows = [];
    private CardMovementRow[] _filteredRows = [];
    private bool _loading = true;
    private bool _suppressCalendarSelection;
    private bool _eventsSubscribed;
    private int _pageIndex;

    public SalesCardMovementView(
        DemoCardIssuanceService cardService,
        SalesPointDirectoryService pointsService,
        SalesOperationsService operationsService)
    {
        _cardService = cardService ?? throw new ArgumentNullException(nameof(cardService));
        _pointsService = pointsService ?? throw new ArgumentNullException(nameof(pointsService));
        _operationsService = operationsService ?? throw new ArgumentNullException(nameof(operationsService));

        InitializeComponent();
        PopulateStatusFilter();
        PopulatePointFilter();
        UpdateDatePlaceholders();
        RefreshRows();
        _loading = false;

        Loaded += View_Loaded;
        Unloaded += View_Unloaded;
    }

    private void View_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_eventsSubscribed)
        {
            _cardService.ScenarioChanged += Source_Changed;
            _pointsService.Changed += Source_Changed;
            _operationsService.Changed += Source_Changed;
            _eventsSubscribed = true;
        }
        RefreshRows();
    }

    private void View_Unloaded(object sender, RoutedEventArgs e)
    {
        if (!_eventsSubscribed) return;
        _cardService.ScenarioChanged -= Source_Changed;
        _pointsService.Changed -= Source_Changed;
        _operationsService.Changed -= Source_Changed;
        _eventsSubscribed = false;
    }

    private void Source_Changed(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess())
            RefreshRows();
        else
            _ = Dispatcher.InvokeAsync(RefreshRows);
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshRows();
    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        _pageIndex = 0;
        ApplyFilters();
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading) return;
        _pageIndex = 0;
        ApplyFilters();
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _pageIndex = 0;
        ApplyFilters();
    }

    private void DateTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateDatePlaceholders();
        if (_loading) return;
        _pageIndex = 0;
        ApplyFilters();
    }

    private void FromCalendarButton_Click(object sender, RoutedEventArgs e) => OpenCalendar(isFromDate: true);
    private void ToCalendarButton_Click(object sender, RoutedEventArgs e) => OpenCalendar(isFromDate: false);

    private void OpenCalendar(bool isFromDate)
    {
        var textBox = isFromDate ? FromDateTextBox : ToDateTextBox;
        var calendar = isFromDate ? FromCalendar : ToCalendar;
        var popup = isFromDate ? FromCalendarPopup : ToCalendarPopup;
        var parsed = TryParseDate(textBox.Text, out var date) ? date : (DateTime?)null;

        _suppressCalendarSelection = true;
        calendar.DisplayDate = parsed ?? DateTime.Today;
        calendar.SelectedDate = parsed;
        _suppressCalendarSelection = false;
        popup.IsOpen = true;
    }

    private void Calendar_SelectedDatesChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressCalendarSelection || sender is not Calendar calendar || calendar.SelectedDate is not DateTime selectedDate)
            return;

        _loading = true;
        if (ReferenceEquals(calendar, FromCalendar))
        {
            FromDateTextBox.Text = FormatDate(selectedDate);
            FromCalendarPopup.IsOpen = false;
        }
        else
        {
            ToDateTextBox.Text = FormatDate(selectedDate);
            ToCalendarPopup.IsOpen = false;
        }
        _loading = false;
        UpdateDatePlaceholders();
        _pageIndex = 0;
        ApplyFilters();
    }

    private void UpdateDatePlaceholders()
    {
        if (FromDatePlaceholderTextBlock is not null && FromDateTextBox is not null)
            FromDatePlaceholderTextBlock.Visibility = string.IsNullOrWhiteSpace(FromDateTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        if (ToDatePlaceholderTextBlock is not null && ToDateTextBox is not null)
            ToDatePlaceholderTextBlock.Visibility = string.IsNullOrWhiteSpace(ToDateTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PageSize_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        _pageIndex = 0;
        ApplyFilters();
    }

    private int PageSize
    {
        get
        {
            var tag = (PageSizeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            return int.TryParse(tag, out var value) ? Math.Clamp(value, 10, 50) : 20;
        }
    }

    private void PopulateStatusFilter()
    {
        var current = CardStatusComboBox.SelectedValue as string ?? "all";
        var options = new List<FilterOption>
        {
            new("كل الحالات", "all"),
            new("مخزون مركزي", "stock"),
            new("مسلّم للبيع", "delivered"),
            new("مباع", "sold"),
            new("موقوف / منتهي", "inactive"),
            new("مرتجع / مستبعد", "returned")
        };
        CardStatusComboBox.ItemsSource = options;
        CardStatusComboBox.SelectedValue = options.Any(item => item.Value == current) ? current : "all";
    }

    private void PopulatePointFilter()
    {
        var current = PointFilterComboBox.SelectedValue as string ?? "all";
        var pointNames = _cardService.Cards
            .Select(card => DisplayPoint(card.PointOfSale))
            .Concat(_pointsService.GetAll().Where(point => !point.IsSystemPoint).Select(point => point.Name))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var options = new List<FilterOption> { new("كل نقاط البيع", "all") };
        options.AddRange(pointNames.Select(name => new FilterOption(name, name)));
        PointFilterComboBox.ItemsSource = options;
        PointFilterComboBox.SelectedValue = options.Any(item => item.Value == current) ? current : "all";
    }

    private void RefreshRows()
    {
        if (!Dispatcher.CheckAccess())
        {
            _ = Dispatcher.InvokeAsync(RefreshRows);
            return;
        }

        _loading = true;
        PopulatePointFilter();
        _allRows = _cardService.Cards
            .Select(CreateRow)
            .OrderByDescending(row => row.CreatedAt)
            .ThenBy(row => row.BatchId, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(row => row.CardNumber, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        _loading = false;
        _pageIndex = 0;
        ApplyFilters();
    }

    private static CardMovementRow CreateRow(GeneratedDemoCard card)
    {
        var state = ResolveCardStatus(card);
        return new CardMovementRow(
            card.BatchId ?? string.Empty,
            card.CardNumber ?? string.Empty,
            DisplayPoint(card.PointOfSale),
            card.PackageName ?? string.Empty,
            card.UnitPrice,
            card.CreatedAt,
            state.Key,
            state.Label,
            string.IsNullOrWhiteSpace(card.DemoStatus) ? "حالة الحساب غير محددة في سجل الكرت" : $"حالة الحساب المسجلة: {card.DemoStatus}",
            card.TargetSystem ?? string.Empty)
        {
            SourceStatus = card.DemoStatus ?? string.Empty,
            Username = card.Username ?? string.Empty
        };
    }

    private static CardStatus ResolveCardStatus(GeneratedDemoCard card)
    {
        var raw = (card.DemoStatus ?? string.Empty).Trim();

        if (ContainsAny(raw, "مرتجع", "إرجاع", "مردود", "هدية", "مستبعد", "ملغي", "ملغى"))
            return new CardStatus("returned", "مرتجع / مستبعد");

        if (ContainsAny(raw, "قيد الاستخدام", "مستخدم", "مباع", "مفعل", "مفعّل", "متصل"))
            return new CardStatus("sold", "مباع");

        if (ContainsAny(raw, "منتهي", "منتهية", "موقوف", "موقوفة", "معطل", "معطلة"))
            return new CardStatus("inactive", "موقوف / منتهي");

        if (IsCentralPoint(card.PointOfSale))
            return new CardStatus("stock", "مخزون مركزي");

        // A card with an assigned sales-point value and an available state is shown as
        // stock held by that point. Batch quantities are not assigned to arbitrary cards.
        return new CardStatus("delivered", "مسلّم للبيع");
    }

    private static bool ContainsAny(string value, params string[] candidates) =>
        candidates.Any(candidate => value.Contains(candidate, StringComparison.CurrentCultureIgnoreCase));

    private static bool IsCentralPoint(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(normalized)
            || normalized.Equals("المركز الرئيسي", StringComparison.CurrentCultureIgnoreCase)
            || normalized.Equals("المخزون المركزي", StringComparison.CurrentCultureIgnoreCase)
            || normalized.Equals("المخزن المركزي", StringComparison.CurrentCultureIgnoreCase)
            || normalized.Equals("المخزون", StringComparison.CurrentCultureIgnoreCase)
            || normalized.Equals("المستودع المركزي", StringComparison.CurrentCultureIgnoreCase)
            || normalized.Equals("main", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("central", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("central stock", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("warehouse", StringComparison.OrdinalIgnoreCase);
    }

    private static string DisplayPoint(string? value) => IsCentralPoint(value) ? "المخزون المركزي" : value!.Trim();

    private void ApplyFilters()
    {
        if (CardsGrid is null || _loading) return;
        if (!TryReadDateRange(out var fromDate, out var toDate, showWarning: true))
            return;

        IEnumerable<CardMovementRow> query = _allRows;
        var point = PointFilterComboBox.SelectedValue as string ?? "all";
        if (point != "all")
            query = query.Where(row => string.Equals(row.PointName, point, StringComparison.CurrentCultureIgnoreCase));

        if (fromDate.HasValue)
            query = query.Where(row => row.CreatedAt.LocalDateTime.Date >= fromDate.Value.Date);
        if (toDate.HasValue)
            query = query.Where(row => row.CreatedAt.LocalDateTime.Date <= toDate.Value.Date);

        var search = SearchTextBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(row => row.CardNumber.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || row.BatchId.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || row.Username.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || row.PackageName.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || row.PointName.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || row.SourceStatus.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || row.TargetSystem.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        }

        var scopedRows = query.OrderByDescending(row => row.CreatedAt).ToArray();
        UpdateSummaryCounts(scopedRows);

        var status = CardStatusComboBox.SelectedValue as string ?? "all";
        if (status != "all")
            query = scopedRows.Where(row => row.StatusKey == status);
        else
            query = scopedRows;

        _filteredRows = query.OrderByDescending(row => row.CreatedAt).ToArray();
        var totalPages = Math.Max(1, (int)Math.Ceiling(_filteredRows.Length / (double)PageSize));
        _pageIndex = Math.Clamp(_pageIndex, 0, totalPages - 1);
        var startIndex = _filteredRows.Length == 0 ? 0 : _pageIndex * PageSize + 1;
        var endIndex = Math.Min((_pageIndex + 1) * PageSize, _filteredRows.Length);
        var pageRows = _filteredRows.Skip(_pageIndex * PageSize).Take(PageSize)
            .Select((row, index) => row with { Index = startIndex + index })
            .ToArray();

        CardsGrid.ItemsSource = pageRows;
        EmptyStatePanel.Visibility = pageRows.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ResultCountTextBlock.Text = $"{_filteredRows.Length:N0} كرت";
        PagerSummaryTextBlock.Text = $"عرض {startIndex:N0}–{endIndex:N0} من أصل {_filteredRows.Length:N0} كرت";
        PageIndicatorTextBlock.Text = $"{_pageIndex + 1} / {totalPages}";
        PreviousPageButton.IsEnabled = _pageIndex > 0;
        NextPageButton.IsEnabled = _pageIndex < totalPages - 1;
    }

    private void UpdateSummaryCounts(IReadOnlyCollection<CardMovementRow> scopedRows)
    {
        CentralStockCountTextBlock.Text = scopedRows.Count(row => row.StatusKey == "stock").ToString("N0", CultureInfo.CurrentCulture);
        DeliveredForSaleCountTextBlock.Text = scopedRows.Count(row => row.StatusKey == "delivered").ToString("N0", CultureInfo.CurrentCulture);
        SoldCountTextBlock.Text = scopedRows.Count(row => row.StatusKey == "sold").ToString("N0", CultureInfo.CurrentCulture);
        InactiveCountTextBlock.Text = scopedRows.Count(row => row.StatusKey == "inactive").ToString("N0", CultureInfo.CurrentCulture);
        ReturnedCountTextBlock.Text = scopedRows.Count(row => row.StatusKey == "returned").ToString("N0", CultureInfo.CurrentCulture);
    }

    private bool TryReadDateRange(out DateTime? fromDate, out DateTime? toDate, bool showWarning)
    {
        fromDate = null;
        toDate = null;
        var fromText = FromDateTextBox.Text?.Trim() ?? string.Empty;
        var toText = ToDateTextBox.Text?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(fromText))
        {
            if (!TryParseDate(fromText, out var parsedFrom))
                return ShowDateWarning("صيغة تاريخ البداية غير صحيحة. استخدم yyyy/MM/dd أو اختر التاريخ من التقويم.", showWarning);
            fromDate = parsedFrom.Date;
        }

        if (!string.IsNullOrWhiteSpace(toText))
        {
            if (!TryParseDate(toText, out var parsedTo))
                return ShowDateWarning("صيغة تاريخ النهاية غير صحيحة. استخدم yyyy/MM/dd أو اختر التاريخ من التقويم.", showWarning);
            toDate = parsedTo.Date;
        }

        if (fromDate.HasValue && toDate.HasValue && fromDate.Value.Date > toDate.Value.Date)
            return ShowDateWarning("تاريخ البداية لا يمكن أن يكون بعد تاريخ النهاية.", showWarning);

        DateRangeWarningTextBlock.Text = string.Empty;
        DateRangeWarningTextBlock.Visibility = Visibility.Collapsed;
        return true;
    }

    private bool ShowDateWarning(string message, bool showWarning)
    {
        if (showWarning)
        {
            DateRangeWarningTextBlock.Text = message;
            DateRangeWarningTextBlock.Visibility = Visibility.Visible;
        }
        return false;
    }

    private static bool TryParseDate(string value, out DateTime date)
    {
        string[] formats = ["yyyy/MM/dd", "yyyy-M-d", "yyyy-MM-dd", "d/M/yyyy", "dd/MM/yyyy"];
        return DateTime.TryParseExact(value.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
            || DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out date);
    }

    private static string FormatDate(DateTime date) => date.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture);

    private void PreviousPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pageIndex <= 0) return;
        _pageIndex--;
        ApplyFilters();
    }

    private void NextPageButton_Click(object sender, RoutedEventArgs e)
    {
        var totalPages = Math.Max(1, (int)Math.Ceiling(_filteredRows.Length / (double)PageSize));
        if (_pageIndex >= totalPages - 1) return;
        _pageIndex++;
        ApplyFilters();
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        _loading = true;
        FromDateTextBox.Clear();
        ToDateTextBox.Clear();
        SearchTextBox.Clear();
        CardStatusComboBox.SelectedValue = "all";
        PointFilterComboBox.SelectedValue = "all";
        PageSizeComboBox.SelectedIndex = 1;
        DateRangeWarningTextBlock.Text = string.Empty;
        DateRangeWarningTextBlock.Visibility = Visibility.Collapsed;
        FromCalendarPopup.IsOpen = false;
        ToCalendarPopup.IsOpen = false;
        _loading = false;
        UpdateDatePlaceholders();
        _pageIndex = 0;
        ApplyFilters();
    }

    private void NewTransferButton_Click(object sender, RoutedEventArgs e)
    {
        var transferView = new SalesStockMovementView(_cardService, _pointsService, _operationsService);
        var window = new Window
        {
            Title = "تسجيل توريد كروت إلى نقطة بيع",
            Content = transferView,
            Owner = Window.GetWindow(this),
            Width = 1240,
            Height = 780,
            MinWidth = 980,
            MinHeight = 620,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            FlowDirection = FlowDirection.RightToLeft,
            Background = Application.Current.TryFindResource("AppBgBrush") as Brush ?? Brushes.White,
            FontFamily = new FontFamily("Segoe UI")
        };
        window.ShowDialog();
        RefreshRows();
    }

    private sealed record FilterOption(string Label, string Value)
    {
        public override string ToString() => Label;
    }

    private sealed record CardStatus(string Key, string Label);

    private sealed record CardMovementRow(
        string BatchId,
        string CardNumber,
        string PointName,
        string PackageName,
        decimal UnitPrice,
        DateTimeOffset CreatedAt,
        string StatusKey,
        string StatusLabel,
        string StatusDetails,
        string TargetSystem)
    {
        public int Index { get; init; }
        public string SourceStatus { get; init; } = string.Empty;
        public string Username { get; init; } = string.Empty;
        public string PriceLabel => $"{UnitPrice:N0} ريال";
        public string CreatedAtLabel => CreatedAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);
    }
}
