using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Shared;

namespace Nexora.Demo.Desktop.Features.Cards.Views;

public partial class PendingCardsView : UserControl
{
    private readonly DemoCardIssuanceService _issuanceService;
    private readonly DemoAccountSystem _targetSystem;
    private readonly DemoCardWorkflowStore _workflowStore = new();
    private bool _isSubscribed;

    public event EventHandler<FeatureNavigationRequestedEventArgs>? NavigationRequested;

    public PendingCardsView(
        DemoCardIssuanceService issuanceService,
        DemoAccountSystem targetSystem)
    {
        ArgumentNullException.ThrowIfNull(issuanceService);

        InitializeComponent();

        _issuanceService = issuanceService;
        _targetSystem = targetSystem;

        TargetSystemTextBlock.Text =
            targetSystem == DemoAccountSystem.HotSpot
                ? "النظام المحدد: هوتسبوت"
                : "النظام المحدد: يوزر منجر";

        Loaded += PendingCardsView_Loaded;
        Unloaded += PendingCardsView_Unloaded;
    }

    private void PendingCardsView_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_isSubscribed)
        {
            _issuanceService.ScenarioChanged += IssuanceService_ScenarioChanged;
            _isSubscribed = true;
        }

        RefreshRows();
    }

    private void PendingCardsView_Unloaded(object sender, RoutedEventArgs e)
    {
        if (_isSubscribed)
        {
            _issuanceService.ScenarioChanged -= IssuanceService_ScenarioChanged;
            _isSubscribed = false;
        }
    }

    private void IssuanceService_ScenarioChanged(object? sender, EventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (IsLoaded)
                RefreshRows();
        });
    }

    private void RefreshRows()
    {
        var systemKey = _targetSystem == DemoAccountSystem.HotSpot
            ? "hotspot"
            : "user-manager";

        var targetName = _targetSystem == DemoAccountSystem.HotSpot
            ? "مستخدمو الهوتسبوت"
            : "مدير المستخدمين";

        var statuses = _workflowStore.GetCardWorkflowStatuses()
            .ToDictionary(
                item => MakeKey(item.TargetSystem, item.BatchId, item.CardNumber),
                StringComparer.OrdinalIgnoreCase);

        var rows = new List<PendingCardRow>();

        foreach (var card in _issuanceService.Cards
                     .Where(card =>
                         string.Equals(card.TargetSystem, targetName, StringComparison.Ordinal) &&
                         !card.IsArchived)
                     .OrderByDescending(card => card.CreatedAt))
        {
            statuses.TryGetValue(
                MakeKey(systemKey, card.BatchId, card.CardNumber),
                out var state);

            var syncPending = state is null || !state.RouterUploadSimulated;
            var printPending = state is null || !state.PrintSubmitted;
            var printQueued = state?.PrintQueued ?? false;

            // A completed demo workflow should no longer appear in the pending list.
            if (!syncPending && !printPending && !printQueued)
                continue;

            var pendingStatus = syncPending && printPending
                ? "بانتظار المزامنة والطباعة"
                : syncPending
                    ? "بانتظار المزامنة"
                    : printQueued
                        ? "في قائمة الطباعة"
                        : "بانتظار الطباعة";

            rows.Add(new PendingCardRow(
                card,
                card.BatchId,
                card.CardNumber,
                card.Username,
                card.PackageName,
                pendingStatus,
                state?.LastAction ?? "لم تُسجّل إجراءات بعد",
                card.CreatedAtLabel));
        }

        PendingCardsGrid.ItemsSource = rows;
        PendingCountTextBlock.Text = $"{rows.Count:N0} كرت";
        EmptyStateBorder.Visibility = rows.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private List<GeneratedDemoCard> GetSelectedCards() =>
        PendingCardsGrid.SelectedItems
            .OfType<PendingCardRow>()
            .Select(row => row.Card)
            .ToList();

    private void RetrySyncButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedCards();

        if (selected.Count == 0)
        {
            SetStatus("حدد كرتًا واحدًا على الأقل أولًا.", true);
            return;
        }

        try
        {
            _workflowStore.RecordRouterUpload(selected);

            SetStatus(
                $"تم تسجيل طلب المزامنة لـ {selected.Count:N0} كرت.",
                false);

            RefreshRows();
        }
        catch (Exception ex)
        {
            SetStatus("تعذر تسجيل طلب المزامنة: " + ex.Message, true);
        }
    }

    private void QueuePrintButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedCards();

        if (selected.Count == 0)
        {
            SetStatus("حدد كرتًا واحدًا على الأقل أولًا.", true);
            return;
        }

        try
        {
            _workflowStore.QueuePrintSelection(selected);

            NavigationRequested?.Invoke(
                this,
                new FeatureNavigationRequestedEventArgs("printing", "قائمة الطباعة"));
        }
        catch (Exception ex)
        {
            SetStatus("تعذر إضافة الكروت إلى قائمة الطباعة: " + ex.Message, true);
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshRows();
        SetStatus("تم تحديث قائمة المعلّقات.", false);
    }

    private void SetStatus(string message, bool isError)
    {
        PendingStatusTextBlock.Text = message;
        PendingStatusTextBlock.Foreground =
            (Brush)FindResource(isError ? "DangerBrush" : "SuccessBrush");
    }

    private static string MakeKey(string systemKey, string batchId, string cardNumber) =>
        $"{systemKey}\u001F{batchId}\u001F{cardNumber}";

    private sealed record PendingCardRow(
        GeneratedDemoCard Card,
        string BatchId,
        string CardNumber,
        string Username,
        string PackageName,
        string PendingStatus,
        string LastAction,
        string CreatedAtLabel);
}