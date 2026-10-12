
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Shared;

namespace Nexora.Demo.Desktop.Features.Cards.Views;

/// <summary>User Manager-only card management page. Local demo actions do not contact RouterOS.</summary>
public partial class UserManagerCardsView : UserControl
{
    private const string UserManagerTarget = "مدير المستخدمين";
    private static readonly IReadOnlyList<CardColumnDefinition> ColumnDefinitions = CreateColumnDefinitions();

    private readonly DemoCardIssuanceService _issuanceService;
    private readonly DemoCardWorkflowStore _workflowStore = new();
    private readonly ObservableCollection<ManagedCardRow> _allRows = [];
    private readonly ListCollectionView _cardsView;
    private readonly ObservableCollection<ColumnSettingItem> _columnSettings = [];
    private readonly Dictionary<string, ManagedCardRow> _rowCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LocalRowState> _rowStates = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<DataGridColumn, CardColumnDefinition> _definitionsByGridColumn = new();
    private ListCollectionView? _columnSettingsView;
    private bool _eventsSubscribed;
    private bool _isBuildingColumns;
    private string? _statusFilter;
    private string? _columnFilterKey;
    private string? _columnFilterValue;
    private ArchiveFilterMode _archiveFilter = ArchiveFilterMode.ActiveOnly;
    private string? _sortKey;
    private ListSortDirection _sortDirection = ListSortDirection.Descending;
    private CardColumnDefinition? _rightClickedColumn;
    private Point _dragStartPoint;

    public UserManagerCardsView(DemoCardIssuanceService issuanceService)
    {
        ArgumentNullException.ThrowIfNull(issuanceService);
        InitializeComponent();
        _issuanceService = issuanceService;
        _cardsView = new ListCollectionView(_allRows) { Filter = MatchCard };
        CardsGrid.ItemsSource = _cardsView;
        InitializeColumnSettings();
        BuildGridColumns();
        RebuildRows();
        UpdateFilterSummary();
        Loaded += UserManagerCardsView_Loaded;
        Unloaded += UserManagerCardsView_Unloaded;
    }

    public event EventHandler<FeatureNavigationRequestedEventArgs>? NavigationRequested;

    private static IReadOnlyList<CardColumnDefinition> CreateColumnDefinitions() => new List<CardColumnDefinition>
    {
        new("Status", "الحالة", "البيانات الأساسية", true, 96, true, true, row => row.StatusLabel, row => row.StatusLabel),
        new("SerialNumber", "الرقم التسلسلي", "البيانات الأساسية", true, 130, true, true, row => row.Source.CardNumber, row => row.Source.CardNumber),
        new("Name", "الاسم", "البيانات الأساسية", true, 120, true, true, row => row.Source.Username, row => row.Source.Username),
        new("Password", "كلمة المرور", "البيانات الأساسية", false, 110, false, true, row => row.Source.PasswordDisplay, row => row.Source.Password),
        new("Price", "السعر", "الباقات والمبيعات", false, 92, true, true, row => row.PriceLabel, row => row.PriceValue),
        new("Package", "الباقة", "الباقات والمبيعات", true, 170, true, true, row => row.PackageName, row => row.PackageName),
        new("BatchNumber", "رقم الدفعة", "الباقات والمبيعات", true, 145, true, true, row => row.Source.BatchId, row => row.Source.BatchId),
        new("EditionNumber", "رقم الطبعة", "الباقات والمبيعات", false, 100, false, false, _ => "—", _ => null),
        new("PointOfSale", "نقطة البيع", "الباقات والمبيعات", false, 130, true, true, row => row.PointOfSale, row => row.PointOfSale),
        new("AllowedTime", "الوقت المسموح", "الاستخدام", false, 115, true, true, row => row.Source.TimeLimit, row => row.Source.TimeLimit),
        new("AllowedDownload", "التنزيل المسموح", "الاستخدام", false, 125, true, true, row => row.Source.DataLimit, row => row.Source.DataLimit),
        new("UsedTime", "الوقت المستخدم", "الاستخدام", false, 112, false, false, _ => "—", _ => null),
        new("DownloadUsed", "التحميل المستخدم", "الاستخدام", false, 125, false, false, _ => "—", _ => null),
        new("UploadUsed", "الرفع المستخدم", "الاستخدام", false, 118, false, false, _ => "—", _ => null),
        new("DownloadUploadUsed", "تحميل + رفع", "الاستخدام", false, 112, false, false, _ => "—", _ => null),
        new("AddedAt", "تاريخ الإضافة", "التواريخ والانتهاء", true, 145, true, true, row => row.Source.CreatedAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture), row => row.Source.CreatedAt),
        new("LastSeen", "آخر ظهور", "التواريخ والانتهاء", false, 145, false, false, _ => "—", _ => null),
        new("FirstLogin", "أول دخول", "التواريخ والانتهاء", false, 145, false, false, _ => "—", _ => null),
        new("ExpiryDate", "تاريخ الانتهاء", "التواريخ والانتهاء", false, 130, false, false, _ => "—", _ => null),
        new("DaysRemaining", "الأيام المتبقية", "التواريخ والانتهاء", false, 115, false, false, _ => "—", _ => null),
        new("TimeRemaining", "الوقت المتبقي", "التواريخ والانتهاء", false, 115, false, false, _ => "—", _ => null),
        new("DownloadRemaining", "التحميل المتبقي", "التواريخ والانتهاء", false, 125, false, false, _ => "—", _ => null),
        new("Comment", "تعليق", "الإدارة والعلاقات", false, 180, true, true, row => EmptyDash(row.Source.Comment), row => row.Source.Comment),
        new("UserManagerClient", "عميل User Manager", "الإدارة والعلاقات", false, 145, false, false, _ => "—", _ => null),
        new("PackageCount", "عدد الباقات", "الإدارة والعلاقات", false, 100, false, false, _ => "—", _ => null),
        new("SessionCount", "عدد جلسات الكرت", "الإدارة والعلاقات", false, 120, false, false, _ => "—", _ => null),
        new("PageNumber", "رقم الصفحة", "الطباعة والأرشيف", false, 95, false, false, _ => "—", _ => null),
        new("SuspendedArchiveSequence", "تسلسل أرشيف المعلّقات", "الطباعة والأرشيف", false, 160, false, false, _ => "—", _ => null)
    };

    private static string EmptyDash(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

    private void InitializeColumnSettings()
    {
        foreach (var definition in ColumnDefinitions)
            AddColumnSetting(definition, definition.DefaultVisible, definition.DefaultWidth);
    }

    private void AddColumnSetting(CardColumnDefinition definition, bool visible, double width)
    {
        var setting = new ColumnSettingItem(definition, visible, width);
        setting.PropertyChanged += ColumnSettingItem_PropertyChanged;
        _columnSettings.Add(setting);
    }

    private void UserManagerCardsView_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_eventsSubscribed)
        {
            _issuanceService.Cards.CollectionChanged += IssuanceCards_CollectionChanged;
            _issuanceService.ScenarioChanged += IssuanceService_ScenarioChanged;
            _eventsSubscribed = true;
        }
        RebuildRows();
    }

    private void UserManagerCardsView_Unloaded(object sender, RoutedEventArgs e)
    {
        if (!_eventsSubscribed) return;
        _issuanceService.Cards.CollectionChanged -= IssuanceCards_CollectionChanged;
        _issuanceService.ScenarioChanged -= IssuanceService_ScenarioChanged;
        _eventsSubscribed = false;
    }

    private void IssuanceCards_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e) =>
        Dispatcher.InvokeAsync(RebuildRows);

    private void IssuanceService_ScenarioChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(RebuildRows);

    private void RebuildRows()
    {
        var cards = _issuanceService.Cards
            .Where(card => string.Equals(card.TargetSystem, UserManagerTarget, StringComparison.Ordinal))
            .ToList();
        _allRows.Clear();
        foreach (var card in cards)
        {
            var key = GetCardKey(card);
            if (!_rowStates.TryGetValue(key, out var state))
            {
                state = new LocalRowState();
                _rowStates[key] = state;
            }
            if (!_rowCache.TryGetValue(key, out var row))
            {
                row = new ManagedCardRow(card, state);
                _rowCache[key] = row;
            }
            else
            {
                row.UpdateSource(card);
            }
            _allRows.Add(row);
        }
        _cardsView.Refresh();
        UpdateSelectionSummary();
        UpdateFooter();
    }

    private static string GetCardKey(GeneratedDemoCard card) => $"{card.BatchId}::{card.CardNumber}";

    private bool MatchCard(object item)
    {
        if (item is not ManagedCardRow row || row.State.IsDeleted) return false;
        if (_archiveFilter == ArchiveFilterMode.ActiveOnly && row.State.IsArchived) return false;
        if (_archiveFilter == ArchiveFilterMode.ArchivedOnly && !row.State.IsArchived) return false;
        if (_statusFilter is not null && !string.Equals(row.StatusLabel, _statusFilter, StringComparison.CurrentCultureIgnoreCase)) return false;
        if (_columnFilterKey is not null && _columnFilterValue is not null &&
            !string.Equals(row[_columnFilterKey], _columnFilterValue, StringComparison.CurrentCultureIgnoreCase)) return false;
        var search = SearchTextBox?.Text.Trim();
        if (string.IsNullOrWhiteSpace(search)) return true;
        return row.Values.Values.Any(value => value.Contains(search, StringComparison.CurrentCultureIgnoreCase));
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchPlaceholderTextBlock is not null)
            SearchPlaceholderTextBlock.Visibility = string.IsNullOrEmpty(SearchTextBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        _cardsView.Refresh();
        UpdateFooter();
    }

    private void FilterButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { FlowDirection = FlowDirection.RightToLeft };
        AddMenuItem(menu, "كل الحالات", (_, _) => { _statusFilter = null; _archiveFilter = ArchiveFilterMode.ActiveOnly; ApplyFilters(); });
        AddMenuItem(menu, "متاحة", (_, _) => SetStatusFilter("متاحة"));
        AddMenuItem(menu, "قيد الاستخدام", (_, _) => SetStatusFilter("قيد الاستخدام"));
        AddMenuItem(menu, "منتهية", (_, _) => SetStatusFilter("منتهية"));
        AddMenuItem(menu, "موقوفة", (_, _) => SetStatusFilter("موقوفة"));
        AddMenuItem(menu, "مفعّلة محليًا", (_, _) => SetStatusFilter("مفعّلة محليًا"));
        AddMenuItem(menu, "معطلة محليًا", (_, _) => SetStatusFilter("معطلة محليًا"));
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "عرض الأرشيف المحلي", (_, _) => { _archiveFilter = ArchiveFilterMode.ArchivedOnly; _statusFilter = null; ApplyFilters(); });
        AddMenuItem(menu, "عرض الكل بما فيه الأرشيف", (_, _) => { _archiveFilter = ArchiveFilterMode.All; _statusFilter = null; ApplyFilters(); });
        AddMenuItem(menu, "إلغاء تصفية العمود", (_, _) => { _columnFilterKey = null; _columnFilterValue = null; ApplyFilters(); });
        OpenMenu(menu, (Button)sender);
    }

    private void SetStatusFilter(string value)
    {
        _statusFilter = value;
        _archiveFilter = ArchiveFilterMode.All;
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        _cardsView.Refresh();
        UpdateFooter();
    }

    private void ColumnsButton_Click(object sender, RoutedEventArgs e) => ShowColumnSettings();

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _columnFilterKey = null;
        _columnFilterValue = null;
        RebuildRows();
    }

    private void PrintButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedRows();
        var rows = selected.Count > 0 ? selected : _cardsView.Cast<ManagedCardRow>().ToList();
        QueueForPrint(rows);
    }

    private void SelectionPrintButton_Click(object sender, RoutedEventArgs e) => QueueForPrint(GetSelectedRows());

    private void QueueForPrint(IReadOnlyCollection<ManagedCardRow> rows)
    {
        if (rows.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "لا توجد كروت محددة للطباعة. حدّد كرتًا أو أكثر أولًا.", "قائمة الطباعة", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _workflowStore.QueuePrintSelection(rows.Select(row => row.ToEffectiveCard()).ToList());
        NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("printing", "قائمة الطباعة"));
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedRows();
        var rows = selected.Count > 0 ? selected : _cardsView.Cast<ManagedCardRow>().ToList();
        if (rows.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "لا توجد سجلات لتصديرها.", "تصدير الكروت", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new SaveFileDialog
        {
            Title = "تصدير سجلات User Manager",
            Filter = "ملف CSV (*.csv)|*.csv",
            FileName = $"UserManager-Cards-{DateTime.Now:yyyyMMdd-HHmm}.csv",
            AddExtension = true,
            DefaultExt = ".csv"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var visible = _columnSettings.Where(setting => setting.IsVisible).ToList();
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", visible.Select(setting => Csv(setting.Definition.Title))));
        foreach (var row in rows)
            builder.AppendLine(string.Join(",", visible.Select(setting => Csv(row[setting.Definition.Key]))));
        try
        {
            File.WriteAllText(dialog.FileName, builder.ToString(), new UTF8Encoding(true));
            MessageBox.Show(Window.GetWindow(this), $"تم تصدير {rows.Count:N0} سجل إلى ملف CSV. هذا تصدير محلي لبيانات الديمو.", "اكتمل التصدير", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), "تعذر حفظ ملف التصدير: " + ex.Message, "خطأ التصدير", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string Csv(string? value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";

    private void AddCardButton_Click(object sender, RoutedEventArgs e) =>
        NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("user-manager", "إضافة الكروت"));

    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { FlowDirection = FlowDirection.RightToLeft };
        AddMenuItem(menu, "إعدادات الأعمدة", (_, _) => ShowColumnSettings());
        AddMenuItem(menu, "تصدير جميع الصفوف الظاهرة", (_, _) => ExportButton_Click(sender, e));
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "مزامنة المحدد مع الراوتر (محاكاة)", (_, _) => SimulateRouterSync(GetSelectedRows()));
        AddMenuItem(menu, "إدارة جلسات المحدد", (_, _) => ShowSessionsNotice(GetSelectedRows()));
        OpenMenu(menu, (Button)sender);
    }

    private void SelectionMoreButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedRows();
        if (selected.Count == 0) return;
        var menu = new ContextMenu { FlowDirection = FlowDirection.RightToLeft };
        AddMenuItem(menu, "تفعيل المحدد محليًا", (_, _) => SetLocalEnabled(selected, true));
        AddMenuItem(menu, "تعطيل المحدد محليًا", (_, _) => SetLocalEnabled(selected, false));
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "تعديل الباقة", (_, _) => ChangePackage(selected));
        AddMenuItem(menu, "تغيير نقطة البيع", (_, _) => ChangePointOfSale(selected));
        AddMenuItem(menu, "مزامنة مع الراوتر (محاكاة)", (_, _) => SimulateRouterSync(selected));
        AddMenuItem(menu, "إدارة الجلسات", (_, _) => ShowSessionsNotice(selected));
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "أرشفة المحدد محليًا", (_, _) => ArchiveRows(selected));
        AddMenuItem(menu, "حذف من عرض الديمو…", (_, _) => DeleteRows(selected));
        OpenMenu(menu, (Button)sender);
    }

    private void ClearSelectionButton_Click(object sender, RoutedEventArgs e) => CardsGrid.UnselectAll();

    private void SelectionDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedRows();
        if (selected.Count == 1) ShowCardDetails(selected[0]);
        else MessageBox.Show(Window.GetWindow(this), "حدد كرتًا واحدًا لعرض التفاصيل.", "تفاصيل الكرت", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CardsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindVisualParent<DataGridRow>(source)?.Item is ManagedCardRow row)
            ShowCardDetails(row);
    }

    private void CardsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelectionSummary();

    private List<ManagedCardRow> GetSelectedRows() => CardsGrid.SelectedItems.OfType<ManagedCardRow>().ToList();

    private void UpdateSelectionSummary()
    {
        var selected = GetSelectedRows();
        SelectionActionsPanel.Visibility = selected.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SelectionCountTextBlock.Text = $"تم تحديد {selected.Count:N0} كرت";
        SelectionDetailsButton.IsEnabled = selected.Count == 1;
    }

    private void UpdateFooter()
    {
        var visibleCount = _cardsView.Cast<ManagedCardRow>().Count();
        var totalCount = _allRows.Count(row => !row.State.IsDeleted);
        CardsCountSummaryTextBlock.Text = $"الظاهر {visibleCount:N0} من {totalCount:N0} سجل User Manager";
        var hasFilters = !string.IsNullOrWhiteSpace(SearchTextBox.Text) || _statusFilter is not null ||
                         _columnFilterKey is not null || _archiveFilter != ArchiveFilterMode.ActiveOnly;
        ClearFilterButton.Visibility = hasFilters ? Visibility.Visible : Visibility.Collapsed;
        UpdateFilterSummary();
        EmptyStateBorder.Visibility = visibleCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSelectionSummary();
    }

    private void UpdateFilterSummary()
    {
        var parts = new List<string>();
        if (_statusFilter is not null) parts.Add("الحالة: " + _statusFilter);
        if (_columnFilterKey is not null && _columnFilterValue is not null)
        {
            var title = ColumnDefinitions.FirstOrDefault(definition => definition.Key == _columnFilterKey)?.Title ?? _columnFilterKey;
            parts.Add($"{title}: {_columnFilterValue}");
        }
        if (_archiveFilter == ArchiveFilterMode.ArchivedOnly) parts.Add("الأرشيف المحلي");
        else if (_archiveFilter == ArchiveFilterMode.All) parts.Add("يشمل الأرشيف");
        CurrentFilterTextBlock.Text = string.Join(" · ", parts);
    }

    private void ClearFilterButton_Click(object sender, RoutedEventArgs e)
    {
        SearchTextBox.Clear();
        _statusFilter = null;
        _columnFilterKey = null;
        _columnFilterValue = null;
        _archiveFilter = ArchiveFilterMode.ActiveOnly;
        ApplyFilters();
    }

    private void CardsGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        if (!_definitionsByGridColumn.TryGetValue(e.Column, out var definition) || !definition.CanSort)
        {
            e.Handled = true;
            return;
        }
        var direction = e.Column.SortDirection == ListSortDirection.Ascending
            ? ListSortDirection.Descending : ListSortDirection.Ascending;
        SortBy(definition, direction);
        e.Handled = true;
    }

    private void SortBy(CardColumnDefinition definition, ListSortDirection direction)
    {
        _sortKey = definition.Key;
        _sortDirection = direction;
        _cardsView.CustomSort = new ManagedCardComparer(definition, direction);
        foreach (var column in CardsGrid.Columns)
            column.SortDirection = _definitionsByGridColumn.TryGetValue(column, out var current) && current.Key == definition.Key
                ? direction : null;
        UpdateFooter();
    }

    private void CardsGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        var header = FindVisualParent<DataGridColumnHeader>(source);
        if (header is not null && _definitionsByGridColumn.TryGetValue(header.Column, out var headerDefinition))
        {
            _rightClickedColumn = headerDefinition;
            var menu = BuildHeaderMenu(headerDefinition);
            menu.PlacementTarget = header;
            menu.Placement = PlacementMode.MousePoint;
            e.Handled = true;
            menu.IsOpen = true;
            return;
        }
        var rowElement = FindVisualParent<DataGridRow>(source);
        if (rowElement?.Item is not ManagedCardRow row) return;
        var cell = FindVisualParent<DataGridCell>(source);
        _rightClickedColumn = cell is not null && _definitionsByGridColumn.TryGetValue(cell.Column, out var cellDefinition)
            ? cellDefinition : null;
        if (!rowElement.IsSelected)
        {
            CardsGrid.SelectedItems.Clear();
            rowElement.IsSelected = true;
        }
        var rowMenu = BuildRowMenu(row);
        rowMenu.PlacementTarget = rowElement;
        rowMenu.Placement = PlacementMode.MousePoint;
        e.Handled = true;
        rowMenu.IsOpen = true;
    }

    private ContextMenu BuildHeaderMenu(CardColumnDefinition definition)
    {
        var menu = new ContextMenu { FlowDirection = FlowDirection.RightToLeft };
        if (definition.CanSort)
        {
            AddMenuItem(menu, "فرز تصاعدي", (_, _) => SortBy(definition, ListSortDirection.Ascending));
            AddMenuItem(menu, "فرز تنازلي", (_, _) => SortBy(definition, ListSortDirection.Descending));
        }
        if (definition.CanFilter)
            AddMenuItem(menu, "تصفية حسب قيمة الكرت المحدد", (_, _) => FilterBySelectedValue(definition));
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "إخفاء هذا العمود", (_, _) => SetColumnVisibility(definition.Key, false));
        AddMenuItem(menu, "إعدادات الأعمدة…", (_, _) => ShowColumnSettings());
        AddMenuItem(menu, "استعادة الأعمدة الافتراضية", (_, _) => ResetColumnSettings());
        AddMenuItem(menu, "نسخ قيمة الخلية المحددة", (_, _) => CopySelectedCell(definition));
        return menu;
    }

    private ContextMenu BuildRowMenu(ManagedCardRow row)
    {
        var menu = new ContextMenu { FlowDirection = FlowDirection.RightToLeft };
        AddMenuItem(menu, "عرض التفاصيل", (_, _) => ShowCardDetails(row));
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "تفعيل محليًا", (_, _) => SetLocalEnabled(new[] { row }, true));
        AddMenuItem(menu, "تعطيل محليًا", (_, _) => SetLocalEnabled(new[] { row }, false));
        AddMenuItem(menu, "تعديل الباقة", (_, _) => ChangePackage(new[] { row }));
        AddMenuItem(menu, "تغيير نقطة البيع", (_, _) => ChangePointOfSale(new[] { row }));
        AddMenuItem(menu, "طباعة الكرت", (_, _) => QueueForPrint(new[] { row }));
        AddMenuItem(menu, "إدارة الجلسات", (_, _) => ShowSessionsNotice(new[] { row }));
        menu.Items.Add(new Separator());
        AddMenuItem(menu, row.State.IsArchived ? "إلغاء الأرشفة محليًا" : "أرشفة الكرت محليًا", (_, _) => ToggleArchive(row));
        AddMenuItem(menu, "حذف من عرض الديمو…", (_, _) => DeleteRows(new[] { row }));
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "مزامنة مع الراوتر (محاكاة)", (_, _) => SimulateRouterSync(new[] { row }));
        if (_rightClickedColumn is not null)
            AddMenuItem(menu, "نسخ قيمة الخلية", (_, _) => CopySelectedCell(_rightClickedColumn));
        return menu;
    }

    private void FilterBySelectedValue(CardColumnDefinition definition)
    {
        var selected = GetSelectedRows();
        var row = selected.FirstOrDefault();
        if (row is null)
        {
            MessageBox.Show(Window.GetWindow(this), "حدد كرتًا أولًا، ثم افتح قائمة رأس العمود لتصفية القيمة.", "التصفية", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var value = definition.Display(row);
        if (string.IsNullOrWhiteSpace(value) || value == "—")
        {
            MessageBox.Show(Window.GetWindow(this), "لا توجد قيمة مسجلة في هذا الحقل لتطبيق التصفية.", "التصفية", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _columnFilterKey = definition.Key;
        _columnFilterValue = value;
        ApplyFilters();
    }

    private void SetColumnVisibility(string key, bool visible)
    {
        var setting = _columnSettings.FirstOrDefault(item => item.Definition.Key == key);
        if (setting is not null) setting.IsVisible = visible;
    }

    private void CopySelectedCell(CardColumnDefinition definition)
    {
        var row = GetSelectedRows().FirstOrDefault();
        if (row is null)
        {
            MessageBox.Show(Window.GetWindow(this), "حدد صفًا أولًا لنسخ قيمة الخلية.", "نسخ القيمة", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try { Clipboard.SetText(row[definition.Key]); }
        catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), "تعذر النسخ إلى الحافظة: " + ex.Message, "نسخ القيمة", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private void SetLocalEnabled(IEnumerable<ManagedCardRow> rows, bool enabled)
    {
        var selected = rows.Distinct().ToList();
        if (selected.Count == 0) return;
        foreach (var row in selected)
        {
            row.State.StatusOverride = enabled ? "مفعّلة محليًا" : "معطلة محليًا";
            row.RefreshValues();
        }
        ApplyFilters();
        CardsScopeTextBlock.Text = $"تم تطبيق الحالة على {selected.Count:N0} كرت داخل واجهة الديمو فقط؛ لم يتم تغيير حالة أي مستخدم على الراوتر.";
    }

    private void ToggleArchive(ManagedCardRow row)
    {
        row.State.IsArchived = !row.State.IsArchived;
        row.RefreshValues();
        ApplyFilters();
    }

    private void ArchiveRows(IEnumerable<ManagedCardRow> rows)
    {
        var selected = rows.Distinct().ToList();
        if (selected.Count == 0) return;
        foreach (var row in selected)
        {
            row.State.IsArchived = true;
            row.RefreshValues();
        }
        _archiveFilter = ArchiveFilterMode.ActiveOnly;
        ApplyFilters();
        CardsScopeTextBlock.Text = $"تمت أرشفة {selected.Count:N0} كرت في حالة العرض المحلية فقط.";
    }

    private void DeleteRows(IEnumerable<ManagedCardRow> rows)
    {
        var selected = rows.Distinct().ToList();
        if (selected.Count == 0) return;
        var answer = MessageBox.Show(Window.GetWindow(this),
            $"سيتم إخفاء {selected.Count:N0} سجل من عرض الديمو الحالي. لن يُرسل طلب حذف إلى الراوتر ولن تُحذف سجلات الإنتاج. هل تريد المتابعة؟",
            "تأكيد الحذف المحلي", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        foreach (var row in selected) row.State.IsDeleted = true;
        CardsGrid.UnselectAll();
        ApplyFilters();
        CardsScopeTextBlock.Text = $"تم إخفاء {selected.Count:N0} سجل من العرض المحلي لهذه الجلسة؛ بيانات الخدمة الأصلية لم تُحذف.";
    }

    private void ChangePackage(IEnumerable<ManagedCardRow> rows)
    {
        var selected = rows.Distinct().ToList();
        if (selected.Count == 0) return;
        var packages = DemoCardPackageCatalog.Load(DemoAccountSystem.UserManager).ToList();
        if (packages.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "لا توجد باقات User Manager مفعّلة في كتالوج الديمو. لم يتغير شيء.", "تعديل الباقة", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new Window
        {
            Title = "تعديل الباقة — محلي",
            Width = 430, Height = 190, MinWidth = 400, MinHeight = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize, FlowDirection = FlowDirection.RightToLeft,
            Background = new SolidColorBrush(Color.FromRgb(244, 246, 248)), Owner = Window.GetWindow(this)
        };
        var root = new DockPanel { Margin = new Thickness(18) };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
        var save = new Button { Content = "تطبيق محلي", Width = 115, Height = 34, Margin = new Thickness(0, 0, 8, 0), Background = new SolidColorBrush(Color.FromRgb(160, 24, 24)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(160, 24, 24)) };
        var cancel = new Button { Content = "إلغاء", Width = 85, Height = 34 };
        var combo = new ComboBox { Height = 35, ItemsSource = packages, DisplayMemberPath = "DisplayName", SelectedIndex = 0 };
        var title = new TextBlock { Text = "اختر باقة من كتالوج User Manager المحلي", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8), Foreground = new SolidColorBrush(Color.FromRgb(32, 36, 42)) };
        DockPanel.SetDock(footer, Dock.Bottom);
        footer.Children.Add(save); footer.Children.Add(cancel);
        root.Children.Add(footer);
        root.Children.Add(new StackPanel { Children = { title, combo } });
        dialog.Content = root;
        save.Click += (_, _) => { if (combo.SelectedItem is DemoCardPackage package) { foreach (var row in selected) { row.State.PackageOverride = package.DisplayName; row.State.PriceOverride = package.Price; row.RefreshValues(); } dialog.DialogResult = true; } };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        if (dialog.ShowDialog() == true)
        {
            ApplyFilters();
            CardsScopeTextBlock.Text = $"تم تغيير عرض الباقة لـ {selected.Count:N0} كرت محليًا فقط؛ لم يتم تغيير الراوتر أو سجل الباقة الأصلي.";
        }
    }

    private void ChangePointOfSale(IEnumerable<ManagedCardRow> rows)
    {
        var selected = rows.Distinct().ToList();
        if (selected.Count == 0) return;
        var choices = _issuanceService.Cards
            .Where(card => string.Equals(card.TargetSystem, UserManagerTarget, StringComparison.Ordinal))
            .Select(card => card.PointOfSale)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(value => value, StringComparer.CurrentCulture)
            .ToList();
        if (choices.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "لا توجد نقاط بيع مسجلة في بيانات الديمو لاختيارها.", "تغيير نقطة البيع", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var combo = new ComboBox { ItemsSource = choices, SelectedItem = selected[0].PointOfSale, MinWidth = 280, Height = 35 };
        if (combo.SelectedItem is null) combo.SelectedIndex = 0;
        var dialog = new Window
        {
            Title = "تغيير نقطة البيع — محلي", Width = 400, Height = 165,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            FlowDirection = FlowDirection.RightToLeft, Background = new SolidColorBrush(Color.FromRgb(244, 246, 248)), Owner = Window.GetWindow(this)
        };
        var body = new StackPanel { Margin = new Thickness(18) };
        body.Children.Add(new TextBlock { Text = "اختر نقطة بيع موجودة في بيانات الديمو", Margin = new Thickness(0, 0, 0, 8), Foreground = new SolidColorBrush(Color.FromRgb(32, 36, 42)) });
        body.Children.Add(combo);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
        var apply = new Button { Content = "تطبيق محلي", Width = 110, Height = 33, Margin = new Thickness(0, 0, 8, 0), Background = new SolidColorBrush(Color.FromRgb(160, 24, 24)), Foreground = Brushes.White };
        var close = new Button { Content = "إلغاء", Width = 80, Height = 33 };
        actions.Children.Add(apply); actions.Children.Add(close); body.Children.Add(actions); dialog.Content = body;
        apply.Click += (_, _) => dialog.DialogResult = true;
        close.Click += (_, _) => dialog.DialogResult = false;
        if (dialog.ShowDialog() == true && combo.SelectedItem is string pointOfSale)
        {
            foreach (var row in selected) { row.State.PointOfSaleOverride = pointOfSale; row.RefreshValues(); }
            ApplyFilters();
            CardsScopeTextBlock.Text = $"تم تغيير نقطة البيع لـ {selected.Count:N0} كرت في عرض الديمو فقط.";
        }
    }

    private void SimulateRouterSync(IEnumerable<ManagedCardRow> rows)
    {
        var selected = rows.Distinct().ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "حدد كرتًا واحدًا أو أكثر أولًا.", "مزامنة تجريبية", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _workflowStore.RecordRouterUpload(selected.Select(row => row.ToEffectiveCard()).ToList());
        MessageBox.Show(Window.GetWindow(this), $"تم تسجيل إجراء محاكاة لـ {selected.Count:N0} كرت في سجل الديمو. لم يتم الاتصال براوتر MikroTik ولم تتغير بيانات مستخدمي الراوتر.", "محاكاة مزامنة الراوتر", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ShowSessionsNotice(IEnumerable<ManagedCardRow> rows)
    {
        var selected = rows.Distinct().ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "حدد كرتًا أو أكثر أولًا.", "إدارة الجلسات", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        MessageBox.Show(Window.GetWindow(this),
            $"تم تحديد {selected.Count:N0} كرت. نموذج الديمو الحالي لا يحتوي على سجلات جلسات فعلية، ولن تُعرض بيانات جلسات تخمينية. إدارة الجلسات الفعلية تتطلب اتصالًا بالراوتر، وهو غير مفعّل هنا.",
            "إدارة الجلسات — غير متاحة في الديمو", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ShowCardDetails(ManagedCardRow row)
    {
        var owner = Window.GetWindow(this);
        var dialog = new Window
        {
            Title = "تفاصيل كرت User Manager",
            Width = 690, Height = 680, MinWidth = 560, MinHeight = 420,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize, FlowDirection = FlowDirection.RightToLeft,
            Background = new SolidColorBrush(Color.FromRgb(244, 246, 248)), Owner = owner
        };
        var root = new DockPanel { Margin = new Thickness(14) };
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        heading.Children.Add(new TextBlock { Text = row.Source.Username, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(32, 36, 42)) });
        heading.Children.Add(new TextBlock { Text = $"الرقم {row.Source.CardNumber} · الدفعة {row.Source.BatchId} · {row.StatusLabel}", FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(160, 24, 24)), Margin = new Thickness(0, 4, 0, 0) });
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var footer = new Button { Content = "إغلاق", Width = 90, Height = 34, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        footer.Click += (_, _) => dialog.Close(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var fields = new Grid();
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var index = 0; index < ColumnDefinitions.Count; index++)
        {
            fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var background = new SolidColorBrush(index % 2 == 0 ? Colors.White : Color.FromRgb(248, 249, 251));
            var label = new Border { Background = background, BorderBrush = new SolidColorBrush(Color.FromRgb(222, 225, 230)), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(9, 7, 9, 7) };
            label.Child = new TextBlock { Text = ColumnDefinitions[index].Title, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(109, 116, 128)), TextWrapping = TextWrapping.Wrap };
            var value = new Border { Background = background, BorderBrush = new SolidColorBrush(Color.FromRgb(222, 225, 230)), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(9, 7, 9, 7) };
            value.Child = new TextBlock { Text = row[ColumnDefinitions[index].Key], FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(32, 36, 42)), TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(label, index); Grid.SetColumn(label, 0); fields.Children.Add(label);
            Grid.SetRow(value, index); Grid.SetColumn(value, 1); fields.Children.Add(value);
        }
        scroll.Content = fields; root.Children.Add(scroll); dialog.Content = root; dialog.ShowDialog();
    }

    private void ShowColumnSettings()
    {
        var owner = Window.GetWindow(this);
        var dialog = new Window
        {
            Title = "إعدادات أعمدة User Manager", Width = 600, Height = 690,
            MinWidth = 520, MinHeight = 540, WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize, FlowDirection = FlowDirection.RightToLeft,
            Background = new SolidColorBrush(Color.FromRgb(244, 246, 248)), Owner = owner
        };
        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock { Text = "الأعمدة المتاحة (28)", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(32, 36, 42)), Margin = new Thickness(0, 0, 0, 10) };
        Grid.SetRow(title, 0); root.Children.Add(title);

        var filters = new Grid { Margin = new Thickness(0, 0, 0, 9) };
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        var search = new TextBox { Height = 34, Padding = new Thickness(8, 4, 8, 4), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "ابحث عن عمود" };
        var group = new ComboBox { Height = 34, Padding = new Thickness(6, 3, 6, 3), ItemsSource = new[] { "كل المجموعات" }.Concat(ColumnDefinitions.Select(definition => definition.Group).Distinct()).ToList(), SelectedIndex = 0 };
        Grid.SetColumn(search, 0); filters.Children.Add(search); Grid.SetColumn(group, 2); filters.Children.Add(group); Grid.SetRow(filters, 1); root.Children.Add(filters);

        _columnSettingsView = new ListCollectionView(_columnSettings);
        _columnSettingsView.Filter = item => item is ColumnSettingItem setting &&
            (string.IsNullOrWhiteSpace(search.Text) || setting.Definition.Title.Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)) &&
            (group.SelectedItem?.ToString() is not string selectedGroup || selectedGroup == "كل المجموعات" || setting.Definition.Group == selectedGroup);
        var list = new ListBox { ItemsSource = _columnSettingsView, AllowDrop = true, HorizontalContentAlignment = HorizontalAlignment.Stretch, BorderBrush = new SolidColorBrush(Color.FromRgb(222, 225, 230)), Background = Brushes.White };
        var template = new DataTemplate(typeof(ColumnSettingItem));
        var rowFactory = new FrameworkElementFactory(typeof(StackPanel)); rowFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal); rowFactory.SetValue(StackPanel.MarginProperty, new Thickness(4, 3, 4, 3));
        var checkFactory = new FrameworkElementFactory(typeof(CheckBox)); checkFactory.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(ColumnSettingItem.IsVisible)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }); checkFactory.SetValue(CheckBox.VerticalAlignmentProperty, VerticalAlignment.Center); checkFactory.SetValue(CheckBox.MarginProperty, new Thickness(0, 0, 8, 0)); rowFactory.AppendChild(checkFactory);
        var nameFactory = new FrameworkElementFactory(typeof(TextBlock)); nameFactory.SetBinding(TextBlock.TextProperty, new Binding("Definition.Title")); nameFactory.SetValue(TextBlock.WidthProperty, 230d); nameFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center); nameFactory.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(32, 36, 42))); rowFactory.AppendChild(nameFactory);
        var groupFactory = new FrameworkElementFactory(typeof(TextBlock)); groupFactory.SetBinding(TextBlock.TextProperty, new Binding("Definition.Group")); groupFactory.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(109, 116, 128))); groupFactory.SetValue(TextBlock.FontSizeProperty, 10.5d); groupFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center); rowFactory.AppendChild(groupFactory);
        template.VisualTree = rowFactory; list.ItemTemplate = template;
        Grid.SetRow(list, 2); root.Children.Add(list);

        var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        var widthLine = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) };
        widthLine.Children.Add(new TextBlock { Text = "عرض العمود المحدد", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Foreground = new SolidColorBrush(Color.FromRgb(80, 88, 100)) });
        var widthBox = new TextBox { Width = 86, Height = 31, Text = "120", FlowDirection = FlowDirection.LeftToRight, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 3, 6, 3) };
        var applyWidth = new Button { Content = "تطبيق العرض", Height = 31, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 0, 10, 0) };
        widthLine.Children.Add(widthBox); widthLine.Children.Add(applyWidth); footer.Children.Add(widthLine);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        var reset = new Button { Content = "استعادة الافتراضي", Width = 135, Height = 35, Margin = new Thickness(0, 0, 8, 0), Background = new SolidColorBrush(Color.FromRgb(249, 237, 237)), Foreground = new SolidColorBrush(Color.FromRgb(127, 17, 24)), BorderBrush = new SolidColorBrush(Color.FromRgb(232, 200, 204)) };
        var close = new Button { Content = "إغلاق", Width = 90, Height = 35 };
        buttons.Children.Add(reset); buttons.Children.Add(close); footer.Children.Add(buttons); Grid.SetRow(footer, 3); root.Children.Add(footer);

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is ColumnSettingItem setting) widthBox.Text = setting.Width.ToString("0", CultureInfo.CurrentCulture);
        };
        search.TextChanged += (_, _) => _columnSettingsView.Refresh();
        group.SelectionChanged += (_, _) => _columnSettingsView.Refresh();
        applyWidth.Click += (_, _) =>
        {
            if (list.SelectedItem is not ColumnSettingItem setting) { MessageBox.Show(dialog, "اختر عمودًا أولًا.", "عرض العمود", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            if (!double.TryParse(widthBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var width) && !double.TryParse(widthBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out width))
            { MessageBox.Show(dialog, "أدخل عرضًا رقميًا صالحًا.", "عرض العمود", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (width is < 60 or > 600) { MessageBox.Show(dialog, "يجب أن يكون العرض بين 60 و600 بكسل.", "عرض العمود", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            setting.Width = width; BuildGridColumns();
        };
        reset.Click += (_, _) => ResetColumnSettings();
        close.Click += (_, _) => dialog.Close();
        list.PreviewMouseLeftButtonDown += (_, args) => _dragStartPoint = args.GetPosition(list);
        list.PreviewMouseMove += (_, args) =>
        {
            if (args.LeftButton != MouseButtonState.Pressed) return;
            var position = args.GetPosition(list);
            if (Math.Abs(position.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(position.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            if (list.SelectedItem is ColumnSettingItem item) DragDrop.DoDragDrop(list, item, DragDropEffects.Move);
        };
        list.Drop += (_, args) =>
        {
            if (args.Data.GetData(typeof(ColumnSettingItem)) is not ColumnSettingItem source) return;
            var targetContainer = FindVisualParent<ListBoxItem>(args.OriginalSource as DependencyObject);
            if (targetContainer?.DataContext is not ColumnSettingItem target || ReferenceEquals(source, target)) return;
            var oldIndex = _columnSettings.IndexOf(source); var newIndex = _columnSettings.IndexOf(target);
            if (oldIndex >= 0 && newIndex >= 0) { _columnSettings.Move(oldIndex, newIndex); BuildGridColumns(); _columnSettingsView.Refresh(); }
        };
        dialog.Content = root;
        dialog.ShowDialog();
        _columnSettingsView = null;
    }

    private void ResetColumnSettings()
    {
        foreach (var setting in _columnSettings) setting.PropertyChanged -= ColumnSettingItem_PropertyChanged;
        _columnSettings.Clear();
        foreach (var definition in ColumnDefinitions) AddColumnSetting(definition, definition.DefaultVisible, definition.DefaultWidth);
        _sortKey = null;
        _statusFilter = null;
        _columnFilterKey = null;
        _columnFilterValue = null;
        _archiveFilter = ArchiveFilterMode.ActiveOnly;
        _cardsView.CustomSort = null;
        BuildGridColumns();
        ApplyFilters();
        _columnSettingsView?.Refresh();
    }

    private void ColumnSettingItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isBuildingColumns) return;
        if (e.PropertyName == nameof(ColumnSettingItem.IsVisible) || e.PropertyName == nameof(ColumnSettingItem.Width))
        {
            BuildGridColumns();
            UpdateFooter();
        }
    }

    private void BuildGridColumns()
    {
        if (CardsGrid is null || _columnSettings is null) return;
        _isBuildingColumns = true;
        try
        {
            CardsGrid.Columns.Clear();
            _definitionsByGridColumn.Clear();
            foreach (var setting in _columnSettings.Where(item => item.IsVisible))
            {
                var definition = setting.Definition;
                DataGridColumn column;
                if (definition.Key == "Status")
                {
                    var borderFactory = new FrameworkElementFactory(typeof(Border));
                    borderFactory.SetBinding(Border.BackgroundProperty, new Binding(nameof(ManagedCardRow.StatusBackground)));
                    borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
                    borderFactory.SetValue(Border.PaddingProperty, new Thickness(7, 3, 7, 3));
                    borderFactory.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                    var textFactory = new FrameworkElementFactory(typeof(TextBlock));
                    textFactory.SetBinding(TextBlock.TextProperty, new Binding(nameof(ManagedCardRow.StatusLabel)));
                    textFactory.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(ManagedCardRow.StatusForeground)));
                    textFactory.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                    textFactory.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
                    borderFactory.AppendChild(textFactory);
                    column = new DataGridTemplateColumn { CellTemplate = new DataTemplate { VisualTree = borderFactory } };
                }
                else
                {
                    column = new DataGridTextColumn
                    {
                        Binding = new Binding($"[{definition.Key}]") { Mode = BindingMode.OneWay },
                        ElementStyle = CreateCellTextStyle()
                    };
                }
                column.Header = definition.Title;
                column.Width = new DataGridLength(setting.Width, DataGridLengthUnitType.Pixel);
                column.MinWidth = Math.Min(setting.Width, 65);
                column.CanUserResize = true;
                column.CanUserReorder = true;
                column.CanUserSort = definition.CanSort;
                column.SortMemberPath = definition.Key;
                column.HeaderStringFormat = null;
                _definitionsByGridColumn[column] = definition;
                CardsGrid.Columns.Add(column);
            }
            if (_sortKey is not null)
            {
                var sorted = CardsGrid.Columns.FirstOrDefault(column => _definitionsByGridColumn.TryGetValue(column, out var definition) && definition.Key == _sortKey);
                if (sorted is not null) sorted.SortDirection = _sortDirection;
            }
        }
        finally { _isBuildingColumns = false; }
    }

    private static Style CreateCellTextStyle()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        style.Setters.Add(new Setter(TextBlock.MarginProperty, new Thickness(7, 0, 7, 0)));
        return style;
    }

    private void OpenMenu(ContextMenu menu, Button target)
    {
        menu.PlacementTarget = target;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private static void AddMenuItem(ItemsControl menu, string text, RoutedEventHandler handler)
    {
        var item = new MenuItem { Header = text, FlowDirection = FlowDirection.RightToLeft, Padding = new Thickness(10, 5, 10, 5) };
        item.Click += handler;
        menu.Items.Add(item);
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T parent) return parent;
            if (child is Visual || child is Visual3D)
                child = VisualTreeHelper.GetParent(child);
            else if (child is FrameworkContentElement content)
                child = content.Parent;
            else
                return null;
        }
        return null;
    }

    private sealed class ManagedCardComparer(CardColumnDefinition definition, ListSortDirection direction) : System.Collections.IComparer
    {
        public int Compare(object? x, object? y)
        {
            if (ReferenceEquals(x, y)) return 0;
            if (x is not ManagedCardRow left) return -1;
            if (y is not ManagedCardRow right) return 1;
            var a = definition.SortValue(left);
            var b = definition.SortValue(right);
            int result;
            if (a is null && b is null) result = 0;
            else if (a is null) result = 1;
            else if (b is null) result = -1;
            else if (a is IComparable comparable && a.GetType() == b.GetType()) result = comparable.CompareTo(b);
            else result = StringComparer.CurrentCultureIgnoreCase.Compare(Convert.ToString(a, CultureInfo.CurrentCulture), Convert.ToString(b, CultureInfo.CurrentCulture));
            return direction == ListSortDirection.Ascending ? result : -result;
        }
    }

    private enum ArchiveFilterMode { ActiveOnly, ArchivedOnly, All }

    private sealed record CardColumnDefinition(
        string Key, string Title, string Group, bool DefaultVisible, double DefaultWidth, bool CanSort, bool CanFilter,
        Func<ManagedCardRow, string> Display, Func<ManagedCardRow, object?> SortValue);

    private sealed class LocalRowState
    {
        public string? StatusOverride { get; set; }
        public string? PackageOverride { get; set; }
        public decimal? PriceOverride { get; set; }
        public string? PointOfSaleOverride { get; set; }
        public bool IsArchived { get; set; }
        public bool IsDeleted { get; set; }
    }

    private sealed class ManagedCardRow
    {
        public GeneratedDemoCard Source { get; private set; }
        public LocalRowState State { get; }
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
        public string this[string key] => Values.TryGetValue(key, out var value) ? value : "—";
        public string StatusLabel => State.IsArchived ? "مؤرشفة محليًا" : State.StatusOverride ?? Source.DemoStatus;
        public Brush StatusBackground => StatusLabel.Contains("متاحة", StringComparison.Ordinal) || StatusLabel.Contains("مفعّلة", StringComparison.Ordinal)
            ? new SolidColorBrush(Color.FromRgb(226, 246, 235))
            : StatusLabel.Contains("موقوفة", StringComparison.Ordinal) || StatusLabel.Contains("معطلة", StringComparison.Ordinal)
                ? new SolidColorBrush(Color.FromRgb(250, 230, 231))
                : StatusLabel.Contains("منتهية", StringComparison.Ordinal) || StatusLabel.Contains("مؤرشفة", StringComparison.Ordinal)
                    ? new SolidColorBrush(Color.FromRgb(237, 239, 242))
                    : new SolidColorBrush(Color.FromRgb(231, 240, 250));
        public Brush StatusForeground => StatusLabel.Contains("متاحة", StringComparison.Ordinal) || StatusLabel.Contains("مفعّلة", StringComparison.Ordinal)
            ? new SolidColorBrush(Color.FromRgb(22, 112, 75))
            : StatusLabel.Contains("موقوفة", StringComparison.Ordinal) || StatusLabel.Contains("معطلة", StringComparison.Ordinal)
                ? new SolidColorBrush(Color.FromRgb(150, 28, 35))
                : new SolidColorBrush(Color.FromRgb(74, 83, 96));
        public string PackageName => State.PackageOverride ?? Source.PackageName;
        public decimal PriceValue => State.PriceOverride ?? Source.UnitPrice;
        public string PriceLabel => $"{PriceValue.ToString("N0", CultureInfo.CurrentCulture)} ريال";
        public string PointOfSale => State.PointOfSaleOverride ?? EmptyDash(Source.PointOfSale);

        public ManagedCardRow(GeneratedDemoCard source, LocalRowState state)
        {
            Source = source;
            State = state;
            RefreshValues();
        }

        public void UpdateSource(GeneratedDemoCard source)
        {
            Source = source;
            RefreshValues();
        }

        public GeneratedDemoCard ToEffectiveCard() => Source with
        {
            PackageName = State.PackageOverride ?? Source.PackageName,
            UnitPrice = State.PriceOverride ?? Source.UnitPrice,
            PointOfSale = State.PointOfSaleOverride ?? Source.PointOfSale
        };

        public void RefreshValues()
        {
            Values.Clear();
            foreach (var definition in ColumnDefinitions)
                Values[definition.Key] = definition.Display(this);
        }
    }

    private sealed class ColumnSettingItem : INotifyPropertyChanged
    {
        private bool _isVisible;
        private double _width;
        public CardColumnDefinition Definition { get; }
        public string Key => Definition.Key;
        public string Title => Definition.Title;
        public string Group => Definition.Group;
        public bool DefaultVisible => Definition.DefaultVisible;
        public double DefaultWidth => Definition.DefaultWidth;
        public bool IsVisible { get => _isVisible; set { if (_isVisible == value) return; _isVisible = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible))); } }
        public double Width { get => _width; set { if (Math.Abs(_width - value) < .1) return; _width = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Width))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
        public ColumnSettingItem(CardColumnDefinition definition, bool visible, double width) { Definition = definition; _isVisible = visible; _width = width; }
    }
}