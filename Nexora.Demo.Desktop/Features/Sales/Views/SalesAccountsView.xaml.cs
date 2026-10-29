using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Nexora.Demo.Desktop.Features.Sales.Models;
using Nexora.Demo.Desktop.Features.Sales.Services;

namespace Nexora.Demo.Desktop.Features.Sales.Views;

public partial class SalesAccountsView : UserControl
{
    private readonly SalesPointDirectoryService _pointsService;
    private readonly SalesOperationsService _operationsService;
    private SalesPointAccountRow[] _rows = [];
    private bool _loading;

    public SalesAccountsView(SalesPointDirectoryService pointsService, SalesOperationsService operationsService)
    {
        ArgumentNullException.ThrowIfNull(pointsService);
        ArgumentNullException.ThrowIfNull(operationsService);
        _loading = true;
        InitializeComponent();
        _pointsService = pointsService;
        _operationsService = operationsService;
        _pointsService.Changed += Source_Changed;
        _operationsService.Changed += Source_Changed;
        RefreshAccounts();
    }

    private void Source_Changed(object? sender, EventArgs e) => RefreshAccounts();
    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshAccounts();

    private void RefreshAccounts()
    {
        if (!IsInitialized) return;
        var selectedId = (AccountsGrid.SelectedItem as SalesPointAccountRow)?.PointId
            ?? (AccountPointComboBox.SelectedItem as AccountChoice)?.PointId;
        _loading = true;
        var points = _pointsService.GetAll();
        _rows = _operationsService.GetPointAccounts(points).ToArray();
        var transfers = _operationsService.GetTransfers();
        var settlements = _operationsService.GetSettlements();

        GrossTextBlock.Text = $"{transfers.Sum(item => item.GrossAmount):N0} ريال";
        CommissionTextBlock.Text = $"{transfers.Sum(item => item.CommissionAmount):N0} ريال";
        SettledTextBlock.Text = $"{transfers.Sum(item => item.PaidAtTransfer) + settlements.Sum(item => item.Amount):N0} ريال";
        OutstandingTextBlock.Text = $"{_rows.Sum(row => row.Balance):N0} ريال";
        ActivePointsTextBlock.Text = points.Count(point => point.IsActive && !point.IsSystemPoint).ToString("N0", CultureInfo.CurrentCulture);

        var choices = _rows
            .Select(row => new AccountChoice(row.PointId, row.PointName, row.Balance, row.TransferCount, row.CommissionPercent))
            .ToArray();
        AccountPointComboBox.ItemsSource = choices;
        AccountPointComboBox.SelectedItem = choices.FirstOrDefault(row => string.Equals(row.PointId, selectedId, StringComparison.OrdinalIgnoreCase))
            ?? choices.FirstOrDefault(row => row.Balance > 0m)
            ?? choices.FirstOrDefault();

        SaveSettlementButton.IsEnabled = _operationsService.CanWrite && choices.Any(choice => choice.Balance > 0m);
        OpenSettlementButton.IsEnabled = _operationsService.CanWrite && choices.Any(choice => choice.Balance > 0m);
        OpenSettlementFromDetailsButton.IsEnabled = _operationsService.CanWrite && choices.Any(choice => choice.Balance > 0m);
        AccountsFootnoteTextBlock.Text = _operationsService.StorageWarning
            ?? "تُحسب الأرصدة من فواتير توريد الدفعات ناقص المدفوع عند التسليم والتسويات المسجلة؛ لا تدخل مستحقات المحافظ في حساب البقالة.";
        _loading = false;
        ApplyAccountFilters(selectedId);
        UpdateSelectedAccount();
    }

    private void AccountSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_loading) ApplyAccountFilters();
    }

    private void AccountStatusFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_loading) ApplyAccountFilters();
    }

    private void ApplyAccountFilters(string? preferredPointId = null)
    {
        if (AccountsGrid is null || AccountSearchTextBox is null || AccountStatusFilterComboBox is null) return;
        preferredPointId ??= (AccountsGrid.SelectedItem as SalesPointAccountRow)?.PointId;
        IEnumerable<SalesPointAccountRow> query = _rows;
        var search = AccountSearchTextBox.Text?.Trim() ?? string.Empty;
        if (search.Length > 0)
        {
            query = query.Where(row => row.PointName.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || row.PointType.Contains(search, StringComparison.CurrentCultureIgnoreCase)
                || row.PointId.Contains(search, StringComparison.CurrentCultureIgnoreCase));
        }
        var state = (AccountStatusFilterComboBox.SelectedValue as string) ?? "all";
        query = state switch
        {
            "open" => query.Where(row => row.Balance > 0m),
            "settled" => query.Where(row => row.Balance <= 0m),
            "credit" => query.Where(row => row.AllowCredit),
            _ => query
        };
        var filtered = query.ToArray();
        AccountsGrid.ItemsSource = filtered;
        AccountsGrid.Visibility = filtered.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        AccountsEmptyStatePanel.Visibility = filtered.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        AccountResultCountTextBlock.Text = $"{filtered.Length:N0} من {_rows.Length:N0} حساب";

        var selected = filtered.FirstOrDefault(row => string.Equals(row.PointId, preferredPointId, StringComparison.OrdinalIgnoreCase))
            ?? filtered.FirstOrDefault();
        AccountsGrid.SelectedItem = selected;
        if (selected is not null)
            AccountsGrid.ScrollIntoView(selected);
        UpdateSelectedAccount();
    }

    private void AccountsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (AccountsGrid.SelectedItem is SalesPointAccountRow row)
        {
            var choice = (AccountPointComboBox.ItemsSource as IEnumerable<AccountChoice>)?
                .FirstOrDefault(item => string.Equals(item.PointId, row.PointId, StringComparison.OrdinalIgnoreCase));
            if (choice is not null && !ReferenceEquals(AccountPointComboBox.SelectedItem, choice))
                AccountPointComboBox.SelectedItem = choice;
        }
        UpdateSelectedAccount();
    }

    private void AccountPointComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        if (AccountPointComboBox.SelectedItem is AccountChoice choice)
            AccountsGrid.SelectedItem = _rows.FirstOrDefault(row => string.Equals(row.PointId, choice.PointId, StringComparison.OrdinalIgnoreCase));
        UpdateSelectedAccount();
    }

    private void UpdateSelectedAccount()
    {
        var row = AccountsGrid.SelectedItem as SalesPointAccountRow;
        if (row is null && AccountPointComboBox.SelectedItem is AccountChoice selectedChoice)
            row = _rows.FirstOrDefault(item => string.Equals(item.PointId, selectedChoice.PointId, StringComparison.OrdinalIgnoreCase));

        if (row is null)
        {
            SelectedPointStatusTextBlock.Text = "—";
            SelectedPointNameTextBlock.Text = "اختر حسابًا من القائمة";
            SelectedPointCodeTextBlock.Text = "—";
            SelectedPointOwnerTextBlock.Text = "—";
            SelectedPointPolicyTextBlock.Text = "—";
            AccountDetailBalanceTextBlock.Text = "—";
            SelectedPointLastMovementTextBlock.Text = "—";
            RecentMovementsGrid.ItemsSource = Array.Empty<AccountMovementRow>();
            RecentMovementsGrid.Visibility = Visibility.Collapsed;
            RecentMovementsEmptyPanel.Visibility = Visibility.Visible;
            SelectedBalanceTextBlock.Text = "0 ريال";
            SelectedAccountDetailTextBlock.Text = "اختر نقطة البيع من القائمة.";
            SaveSettlementButton.IsEnabled = false;
            OpenSettlementFromDetailsButton.IsEnabled = false;
            return;
        }

        var profile = _pointsService.GetAll().FirstOrDefault(point => string.Equals(point.Id, row.PointId, StringComparison.OrdinalIgnoreCase));
        var currentBalance = _operationsService.GetPointBalance(row.PointId);
        SelectedPointStatusTextBlock.Text = profile?.StatusLabel ?? "غير معروف";
        SelectedPointStatusTextBlock.Foreground = profile?.IsActive == false ? Brushes.Firebrick : Brushes.SeaGreen;
        SelectedPointNameTextBlock.Text = row.PointName;
        SelectedPointCodeTextBlock.Text = profile?.Code ?? row.PointId;
        SelectedPointOwnerTextBlock.Text = profile is null
            ? "لا تتوفر بيانات مسؤول مسجلة"
            : string.Join(" · ", new[] { profile.ContactName, profile.Phone }.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (string.IsNullOrWhiteSpace(SelectedPointOwnerTextBlock.Text)) SelectedPointOwnerTextBlock.Text = "لا توجد بيانات تواصل";
        SelectedPointPolicyTextBlock.Text = profile?.CreditPolicyLabel ?? (row.AllowCredit ? "يسمح بالآجل" : "فوري");
        AccountDetailBalanceTextBlock.Text = $"{currentBalance:N0} ريال";
        SelectedPointLastMovementTextBlock.Text = row.LastMovementLabel;
        SelectedBalanceTextBlock.Text = $"{currentBalance:N0} ريال";
        SelectedAccountDetailTextBlock.Text = $"{row.TransferCount:N0} توريد · العمولة الافتراضية {row.CommissionPercent:0.##}% · يتم تسجيل التحصيل دون تغيير فواتير التوريد التاريخية.";

        var movements = BuildRecentMovements(row.PointId);
        RecentMovementsGrid.ItemsSource = movements;
        RecentMovementsGrid.Visibility = movements.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        RecentMovementsEmptyPanel.Visibility = movements.Length > 0 ? Visibility.Collapsed : Visibility.Visible;

        var canSettle = _operationsService.CanWrite && currentBalance > 0m;
        SaveSettlementButton.IsEnabled = canSettle;
        OpenSettlementButton.IsEnabled = canSettle || _rows.Any(item => item.Balance > 0m && _operationsService.CanWrite);
        OpenSettlementFromDetailsButton.IsEnabled = canSettle;
    }

    private AccountMovementRow[] BuildRecentMovements(string pointId)
    {
        var movements = new List<(DateTimeOffset Date, string Reference, string Activity, decimal Amount, decimal Delta)>();
        foreach (var transfer in _operationsService.GetTransfers().Where(item => string.Equals(item.PointId, pointId, StringComparison.OrdinalIgnoreCase)))
            movements.Add((transfer.OccurredAt, transfer.Reference, $"توريد دفعة · {transfer.BatchId}", transfer.NetAmount, transfer.Balance));
        foreach (var settlement in _operationsService.GetSettlements().Where(item => string.Equals(item.PointId, pointId, StringComparison.OrdinalIgnoreCase)))
            movements.Add((settlement.OccurredAt, settlement.Reference, $"تحصيل · {settlement.PaymentMethod}", -settlement.Amount, -settlement.Amount));

        decimal running = 0m;
        var ordered = new List<AccountMovementRow>();
        foreach (var movement in movements.OrderBy(item => item.Date).ThenBy(item => item.Reference, StringComparer.OrdinalIgnoreCase))
        {
            running = Math.Max(0m, running + movement.Delta);
            ordered.Add(new AccountMovementRow(
                movement.Date.ToLocalTime().ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture),
                $"{movement.Activity} · {movement.Reference}",
                $"{movement.Amount:N0} ريال",
                $"{running:N0} ريال",
                movement.Date));
        }
        return ordered.OrderByDescending(item => item.OccurredAt).Take(12).ToArray();
    }

    private void OpenSettlementButton_Click(object sender, RoutedEventArgs e)
    {
        var choices = AccountPointComboBox.ItemsSource as IEnumerable<AccountChoice>;
        var selectedRow = AccountsGrid.SelectedItem as SalesPointAccountRow;
        var point = selectedRow is null
            ? null
            : choices?.FirstOrDefault(item => string.Equals(item.PointId, selectedRow.PointId, StringComparison.OrdinalIgnoreCase));
        point ??= AccountPointComboBox.SelectedItem as AccountChoice;
        if (point is null || point.Balance <= 0m)
            point = choices?.FirstOrDefault(item => item.Balance > 0m);
        if (point is null || point.Balance <= 0m)
        {
            ShowError("لا توجد نقطة بيع برصيد مفتوح يمكن تحصيله حاليًا.");
            return;
        }

        AccountPointComboBox.SelectedItem = point;
        AccountsGrid.SelectedItem = _rows.FirstOrDefault(row => string.Equals(row.PointId, point.PointId, StringComparison.OrdinalIgnoreCase));
        AccountDetailsPanel.Visibility = Visibility.Collapsed;
        SettlementPanel.Visibility = Visibility.Visible;
    }

    private void CloseSettlementButton_Click(object sender, RoutedEventArgs e)
    {
        SettlementPanel.Visibility = Visibility.Collapsed;
        AccountDetailsPanel.Visibility = Visibility.Visible;
    }

    private void SaveSettlementButton_Click(object sender, RoutedEventArgs e)
    {
        if (AccountPointComboBox.SelectedItem is not AccountChoice point)
        {
            ShowError("اختر نقطة عليها رصيد مفتوح.");
            return;
        }
        if (!decimal.TryParse(SettlementAmountTextBox.Text?.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out var amount)
            && !decimal.TryParse(SettlementAmountTextBox.Text?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out amount))
        {
            ShowError("أدخل مبلغ تحصيل رقمي صحيح.");
            SettlementAmountTextBox.Focus();
            return;
        }
        if (amount <= 0m)
        {
            ShowError("مبلغ التحصيل يجب أن يكون أكبر من صفر.");
            SettlementAmountTextBox.Focus();
            return;
        }
        try
        {
            var saved = _operationsService.RecordSettlement(new SalesSettlementDraft(
                point.PointId, point.PointName, amount,
                (SettlementMethodComboBox.SelectedValue as string) ?? "نقدي",
                SettlementReferenceTextBox.Text?.Trim() ?? string.Empty,
                SettlementNotesTextBox.Text?.Trim() ?? string.Empty));
            MessageBox.Show(Window.GetWindow(this),
                $"تم تسجيل التسوية {saved.Reference} بقيمة {saved.Amount:N0} ريال على «{saved.PointName}».\nسيُعاد احتساب الرصيد بعد الحفظ.",
                "تم تسجيل التسوية", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
            SettlementAmountTextBox.Text = "0";
            SettlementReferenceTextBox.Clear();
            SettlementNotesTextBox.Clear();
            SettlementPanel.Visibility = Visibility.Collapsed;
            AccountDetailsPanel.Visibility = Visibility.Visible;
            RefreshAccounts();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            ShowError("لم يتم تسجيل التحصيل. قد يكون المبلغ أكبر من الرصيد الحالي.\n\n" + ex.Message);
            RefreshAccounts();
        }
    }

    private static void ShowError(string message)
        => MessageBox.Show(message, "حسابات نقاط البيع", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.RtlReading);

    private sealed record AccountChoice(string PointId, string PointName, decimal Balance, int TransferCount, decimal CommissionPercent)
    {
        public string DisplayLabel => $"{PointName} · مستحق {Balance:N0} ريال";
    }

    private sealed record AccountMovementRow(string DateLabel, string ActivityLabel, string AmountLabel, string BalanceAfterLabel, DateTimeOffset OccurredAt);
}
