
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