using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Shared;

namespace Nexora.Demo.Desktop.Features.Cards.Views;

/// <summary>
/// Management projection over the same locally persisted records used by issuance,
/// import, batches, and printing. It does not create a second card store.
/// </summary>
public partial class CardManagementView : UserControl
{
    private readonly DemoCardIssuanceService _issuanceService;
    private readonly DemoCardWorkflowStore _workflowStore = new();
    private readonly string _targetName;
    private List<CardManagementRow> _rows = [];
    private ListCollectionView _view;
    private readonly ObservableCollection<CardManagementRow> _pageRows = [];
    private int _currentPage = 1;
    private int _pageSize = 7;
    private int _lastVisibleRowCapacity;
    private bool _updatingPageSizeOptions;
    private string? _sortMemberPath;
    private ListSortDirection _sortDirection = ListSortDirection.Ascending;
    private bool _filtersReady;
    private bool _suppressFilters;
    private bool _isSubscribed;
    private int _duplicateSourceRecords;

    public CardManagementView(DemoCardIssuanceService issuanceService, DemoAccountSystem targetSystem)
    {
        ArgumentNullException.ThrowIfNull(issuanceService);
        _issuanceService = issuanceService;
        _targetName = targetSystem == DemoAccountSystem.HotSpot ? "مستخدمو الهوتسبوت" : "مدير المستخدمين";

        InitializeComponent();
        UpdateSearchPlaceholder();
        CardsDataGrid.Loaded += CardsDataGrid_Loaded;
        CardsDataGrid.SizeChanged += CardsDataGrid_SizeChanged;

        _view = new ListCollectionView(_rows);
        _view.Filter = MatchesCard;
        CardsDataGrid.ItemsSource = _pageRows;
        _filtersReady = true;
        RefreshFromSource(preserveSelection: false);
        Loaded += CardManagementView_Loaded;
        Unloaded += CardManagementView_Unloaded;
    }

    public event EventHandler<FeatureNavigationRequestedEventArgs>? NavigationRequested;

    private void CardManagementView_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_isSubscribed)
        {
            _issuanceService.ScenarioChanged += IssuanceService_ScenarioChanged;
            _isSubscribed = true;
        }
        RefreshFromSource(preserveSelection: true);
    }

    private void CardManagementView_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_isSubscribed)
        {
            _issuanceService.ScenarioChanged -= IssuanceService_ScenarioChanged;
            _isSubscribed = false;
        }
    }

    private void IssuanceService_ScenarioChanged(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(() => RefreshFromSource(preserveSelection: true));


    private int FilteredCount => _view.Cast<object>().OfType<CardManagementRow>().Count();
    private int PageCount => Math.Max(1, (FilteredCount + _pageSize - 1) / _pageSize);


}

public sealed class CardManagementRow : INotifyPropertyChanged
{
    private bool _isMarked;

    public CardManagementRow(GeneratedDemoCard card, DemoCardWorkflowStatus? workflow, bool isMarked)
    {
        Card = card;
        Workflow = workflow;
        _isMarked = isMarked;
        SourceKey = (card.Comment ?? string.Empty).StartsWith("استيراد من ملف:", StringComparison.OrdinalIgnoreCase) ? "import" : "local";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public GeneratedDemoCard Card { get; }
    public DemoCardWorkflowStatus? Workflow { get; }
    public string CardNumber => Card.CardNumber;
    public string Username => Card.Username;
    public bool ShowUsername => !string.IsNullOrWhiteSpace(Card.Username) && !string.Equals(Card.Username, Card.CardNumber, StringComparison.OrdinalIgnoreCase);
    public string PackageName => Card.PackageName;
    public string BatchId => Card.BatchId;
    public string CreatedAtLabel => Card.CreatedAtLabel;
    public DateTimeOffset CreatedAt => Card.CreatedAt;
    public string SourceKey { get; }
    public string SourceLabel => SourceKey == "import" ? "مستورد" : "إصدار";
    public bool IsArchived => Card.IsArchived;
    public string ArchiveActionLabel => Card.IsArchived ? "استعادة الكرت من الأرشيف" : "أرشفة الكرت";
    public string CardStatusLabel => Card.IsArchived ? "مؤرشفة" : string.IsNullOrWhiteSpace(Card.DemoStatus) ? "غير محددة" : Card.DemoStatus;
    public string PrintStatusLabel => CardManagementView.PrintStatusLabelFor(Workflow);
    public bool NeedsReview => !Card.IsArchived && (string.Equals(Card.DemoStatus, "موقوفة", StringComparison.Ordinal) ||
        ContainsAny(Card.LocalStatus, "فشل", "خطأ", "غير مؤكد", "مراجعة"));
    public bool IsMarked
    {
        get => _isMarked;
        set
        {
            if (_isMarked == value) return;
            _isMarked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMarked)));
        }
    }

    private static bool ContainsAny(string? text, params string[] tokens) =>
        !string.IsNullOrWhiteSpace(text) && tokens.Any(token => text.Contains(token, StringComparison.OrdinalIgnoreCase));

}
