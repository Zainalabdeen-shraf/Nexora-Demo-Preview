using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Shared;

namespace Nexora.Demo.Desktop.Features.Cards.Views;

// Responsibility: Selection, contextual actions, and selected card details panel.
public partial class NetworkUsersView
{
    public void FocusCard(string cardNumber)
    {
        SetSearchQuery(cardNumber);
        var row = _rowsView.Cast<object>().OfType<NetworkUserDisplayRow>()
            .FirstOrDefault(item => string.Equals(item.CardNumber, cardNumber, StringComparison.OrdinalIgnoreCase));
        if (row is not null)
        {
            GeneratedCardsGrid.SelectedItem = row;
            GeneratedCardsGrid.ScrollIntoView(row);
            UpdateSelectedDetails();
        }
    }
    public void OpenCardDetails(string cardNumber)
    {
        FocusCard(cardNumber);
    }
    private void GeneratedCardsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewReady)
            UpdateSelectedDetails();
    }
    private void GeneratedCardsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GeneratedCardsGrid.SelectedItem is NetworkUserDisplayRow row)
            UpdateSelectedDetails(row);
    }
    private void CardMarkerButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not NetworkUserDisplayRow row)
            return;

        // The red batch marker opens the full set of cards produced in this batch.
        GeneratedCardsGrid.UnselectAll();
        GeneratedCardsGrid.SelectedItem = row;
        if (!string.IsNullOrWhiteSpace(CardSearchTextBox.Text))
            CardSearchTextBox.Clear();
        if (CardStatusFilterComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag?.ToString() == "all") is { } allStatus)
            CardStatusFilterComboBox.SelectedItem = allStatus;
        SetActivityFilter("all");
        SetBatchFilter(row.BatchId);

        var visibleRow = _rowsView.Cast<object>().OfType<NetworkUserDisplayRow>()
            .FirstOrDefault(item => string.Equals(item.CardNumber, row.CardNumber, StringComparison.OrdinalIgnoreCase));
        if (visibleRow is not null)
        {
            GeneratedCardsGrid.SelectedItem = visibleRow;
            GeneratedCardsGrid.ScrollIntoView(visibleRow);
            UpdateSelectedDetails(visibleRow);
        }
    }
    private void RowActionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not NetworkUserDisplayRow row)
            return;

        // Quick actions are per row, never accidentally applied to a previous multi-selection.
        GeneratedCardsGrid.UnselectAll();
        GeneratedCardsGrid.SelectedItem = row;
        UpdateSelectedDetails(row);
        if (button.ContextMenu is { } menu)
        {
            menu.PlacementTarget = button;
            menu.DataContext = row;
            menu.IsOpen = true;
        }
    }
    private void RowActionMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item)
            return;

        var contextMenu = item.Parent as ContextMenu ?? ItemsControl.ItemsControlFromItemContainer(item) as ContextMenu;
        var row = (contextMenu?.PlacementTarget as Button)?.DataContext as NetworkUserDisplayRow;
        if (row is null)
        {
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "حدد حسابًا", "لم يتم العثور على حساب مرتبط بالإجراء. اختر صفًا من الجدول ثم أعد المحاولة.", WorkflowFeedbackKind.Warning, null,
                new WorkflowFeedbackAction("close", "متابعة", true));
            return;
        }

        GeneratedCardsGrid.UnselectAll();
        GeneratedCardsGrid.SelectedItem = row;
        UpdateSelectedDetails(row);
        ExecuteAction(item.Tag?.ToString() ?? string.Empty, new List<NetworkUserDisplayRow> { row });
    }
    private void ShowDetailsMenuItem_Click(object sender, RoutedEventArgs e) => UpdateSelectedDetails();
    private void UpdateSelectedDetails() => UpdateSelectedDetails(GeneratedCardsGrid.SelectedItem as NetworkUserDisplayRow);
    private void UpdateSelectedDetails(NetworkUserDisplayRow? row)
    {
        if (!_viewReady || SelectedCardNumberTextBlock is null)
            return;

        var selectedCount = GeneratedCardsGrid.SelectedItems.Count;
        if (selectedCount > 1)
        {
            SetDetailText("تم تحديد عدة حسابات", "حدد حسابًا واحدًا لعرض التفاصيل");
            return;
        }
        if (row is null)
        {
            SetDetailText("—", "—");
            return;
        }

        SelectedCardNumberTextBlock.Text = row.CardNumber;
        SelectedPasswordTextBlock.Text = row.PasswordDisplay;
        SelectedBatchIdTextBlock.Text = row.BatchId;
        SelectedPackageNameTextBlock.Text = string.IsNullOrWhiteSpace(row.PackageName) ? "—" : row.PackageName;
        SelectedCreatedAtTextBlock.Text = row.CreatedAtLabel;
        SelectedAccountExpiryTextBlock.Text = row.EndDate;
        SelectedCardStatusTextBlock.Text = row.AccountStatus;
        SelectedConnectionStatusTextBlock.Text = row.ConnectionStatus;
        SelectedIpAddressTextBlock.Text = row.IpAddress;
        SelectedMacAddressTextBlock.Text = row.MacAddress;
        SelectedLastSeenTextBlock.Text = row.LastSeenLabel;
        SelectedSessionDurationTextBlock.Text = row.SessionDuration;
        SelectedDownloadTextBlock.Text = row.Download;
        SelectedUploadTextBlock.Text = row.Upload;
        SelectedTotalDownloadTextBlock.Text = row.TotalDownload;
        SelectedTotalUploadTextBlock.Text = row.TotalUpload;
        SelectedPackageStartTextBlock.Text = row.PackageStartDate;
        SelectedPackageEndTextBlock.Text = row.EndDate;
        SelectedTimeRemainingTextBlock.Text = row.RemainingTime;
        SelectedPackageNameInSubscriptionTextBlock.Text = string.IsNullOrWhiteSpace(row.PackageName) ? "—" : row.PackageName;
        SelectedUsageProgressBar.Value = row.UsageProgress;
        SelectedPackageProgressBar.Value = row.PackageProgress;
        SelectedUsageSummaryTextBlock.Text = row.AddedDataGb > 0m
            ? $"إجمالي الاستخدام · رصيد إضافي: {row.AddedDataGb.ToString("0.##", CultureInfo.InvariantCulture)} GB"
            : "إجمالي الاستخدام";
        UpdateBadge(SelectedCardStatusBadgeBorder, SelectedCardStatusTextBlock, SelectedCardStatusDot, row.AccountStatus, account: true);
        UpdateBadge(SelectedConnectionStatusBorder, SelectedConnectionStatusTextBlock, SelectedConnectionStatusDot, row.ConnectionStatus, account: false);
        var connected = row.IsConnected;
        SelectedLiveBadgeTextBlock.Text = connected ? "متصل الآن" : row.ConnectionStatus;
        SelectedLiveBadgeBorder.Background = connected ? new SolidColorBrush(Color.FromRgb(213, 248, 228)) : new SolidColorBrush(Color.FromRgb(233, 238, 247));
        SelectedLiveBadgeBorder.BorderBrush = connected ? new SolidColorBrush(Color.FromRgb(120, 217, 160)) : new SolidColorBrush(Color.FromRgb(190, 202, 219));
        SelectedLiveBadgeDot.Fill = connected ? (Brush)FindResource("NuGreen") : (Brush)FindResource("NuGray");
        SelectedLiveBadgeTextBlock.Foreground = connected ? new SolidColorBrush(Color.FromRgb(8, 118, 66)) : new SolidColorBrush(Color.FromRgb(78, 94, 119));
    }
    private void SetDetailText(string cardNumber, string placeholder)
    {
        SelectedCardNumberTextBlock.Text = cardNumber;
        SelectedPasswordTextBlock.Text = placeholder;
        SelectedBatchIdTextBlock.Text = placeholder;
        SelectedPackageNameTextBlock.Text = placeholder;
        SelectedCreatedAtTextBlock.Text = placeholder;
        SelectedAccountExpiryTextBlock.Text = placeholder;
        SelectedCardStatusTextBlock.Text = "تحديد متعدد";
        SelectedConnectionStatusTextBlock.Text = placeholder;
        SelectedIpAddressTextBlock.Text = placeholder;
        SelectedMacAddressTextBlock.Text = placeholder;
        SelectedLastSeenTextBlock.Text = placeholder;
        SelectedSessionDurationTextBlock.Text = placeholder;
        SelectedDownloadTextBlock.Text = placeholder;
        SelectedUploadTextBlock.Text = placeholder;
        SelectedTotalDownloadTextBlock.Text = placeholder;
        SelectedTotalUploadTextBlock.Text = placeholder;
        SelectedPackageStartTextBlock.Text = placeholder;
        SelectedPackageEndTextBlock.Text = placeholder;
        SelectedTimeRemainingTextBlock.Text = placeholder;
        SelectedPackageNameInSubscriptionTextBlock.Text = placeholder;
        SelectedUsageProgressBar.Value = 0;
        SelectedPackageProgressBar.Value = 0;
        SelectedUsageSummaryTextBlock.Text = "إجمالي الاستخدام";
        UpdateBadge(SelectedCardStatusBadgeBorder, SelectedCardStatusTextBlock, SelectedCardStatusDot, "تحديد متعدد", account: true);
        UpdateBadge(SelectedConnectionStatusBorder, SelectedConnectionStatusTextBlock, SelectedConnectionStatusDot, placeholder, account: false);
        SelectedLiveBadgeTextBlock.Text = placeholder;
        SelectedLiveBadgeBorder.Background = (Brush)FindResource("NuGraySoft");
        SelectedLiveBadgeBorder.BorderBrush = (Brush)FindResource("NuLine");
        SelectedLiveBadgeDot.Fill = (Brush)FindResource("NuGray");
        SelectedLiveBadgeTextBlock.Foreground = (Brush)FindResource("NuGray");
    }
    private void UpdateBadge(Border border, TextBlock text, Ellipse dot, string status, bool account)
    {
        var colors = status switch
        {
            "مفعل" or "متصل الآن" => (Color.FromRgb(213, 248, 228), Color.FromRgb(120, 217, 160), Color.FromRgb(8, 118, 66)),
            "موقوف" => (Color.FromRgb(255, 233, 184), Color.FromRgb(240, 190, 89), Color.FromRgb(153, 92, 0)),
            "منتهي" => (Color.FromRgb(255, 221, 229), Color.FromRgb(237, 145, 162), Color.FromRgb(183, 19, 46)),
            _ => (Color.FromRgb(233, 238, 247), Color.FromRgb(190, 202, 219), Color.FromRgb(78, 94, 119))
        };
        border.Background = new SolidColorBrush(colors.Item1);
        border.BorderBrush = new SolidColorBrush(colors.Item2);
        text.Foreground = new SolidColorBrush(colors.Item3);
        dot.Fill = new SolidColorBrush(colors.Item3);
    }
    private void UpdateEmptyState()
    {
        if (EmptyCardsPanel is null || GeneratedCardsGrid is null)
            return;
        EmptyCardsPanel.Visibility = GeneratedCardsGrid.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyCardsDescriptionTextBlock.Text = _issuanceService.Cards.Count == 0
            ? "لا توجد حسابات في بيانات العرض الحالية."
            : "لا توجد حسابات تطابق المرشحات الحالية؛ غيّر الحالة أو امسح البحث.";
    }
}
