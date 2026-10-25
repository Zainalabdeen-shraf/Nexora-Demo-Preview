using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Shared;

namespace Nexora.Demo.Desktop.Features.Cards.Views;

public partial class GeneratedCardsView : UserControl
{
    private readonly DemoCardIssuanceService _issuanceService;
    private readonly ListCollectionView _cardsView;
    private readonly DemoCardWorkflowStore _workflowStore = new();
    private bool _isSubscribed;
    private bool _refreshingBatchFilter;
    private string _activityFilter = "all";

    public GeneratedCardsView(DemoCardIssuanceService issuanceService, DemoAccountSystem targetSystem)
    {
        InitializeComponent();
        _issuanceService = issuanceService;
        _cardsView = new ListCollectionView(_issuanceService.Cards);
        _cardsView.Filter = MatchCard;
        GeneratedCardsGrid.ItemsSource = _cardsView;
        CardSystemFilterComboBox.SelectedItem = CardSystemFilterComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == (targetSystem == DemoAccountSystem.HotSpot ? "hotspot" : "user-manager"));
        CardsScopeTextBlock.Text = targetSystem == DemoAccountSystem.HotSpot
            ? "حسابات HotSpot مع بيانات الكرت والدفعة والحالة."
            : "حسابات الشبكة مع بيانات الكرت والدفعة والحالة.";
        RefreshBatchFilterOptions(_workflowStore.GetPreferredBatchFilter(GetCurrentSystemKey()));
        ApplySystemColumnVisibility();
        Loaded += GeneratedCardsView_Loaded;
        Unloaded += GeneratedCardsView_Unloaded;
        UpdateSummary();
    }

    public event EventHandler<FeatureNavigationRequestedEventArgs>? NavigationRequested;

    private void GeneratedCardsView_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_isSubscribed)
        {
            _issuanceService.ScenarioChanged += IssuanceService_ScenarioChanged;
            _isSubscribed = true;
        }
        RefreshBatchFilterOptions(_workflowStore.GetPreferredBatchFilter(GetCurrentSystemKey()));
        RefreshFilteredCards();
    }

    private void GeneratedCardsView_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_isSubscribed)
        {
            _issuanceService.ScenarioChanged -= IssuanceService_ScenarioChanged;
            _isSubscribed = false;
        }
    }


    // النافذة التفاعلية الاحترافية لتفاصيل الكرت


    // أحداث الأيقونات الفردية


}

// محول العرض التفاعلي لحالة الكرت باللغة والنص الملون
public class WorkflowStatusConverter : IValueConverter
{
    private static readonly DemoCardWorkflowStore WorkflowStore = new();

    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is GeneratedDemoCard card)
        {
            return WorkflowStore.GetCardStatusLabel(card.BatchId, card.CardNumber);
        }
        return "بانتظار المزامنة";
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}