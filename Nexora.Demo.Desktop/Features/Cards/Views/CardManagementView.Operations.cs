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

// Responsibility: Card row actions, printing, archive/restore, and status mapping.
public partial class CardManagementView
{
    private void CardsDataGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (CardsDataGrid.SelectedItem is CardManagementRow row) ShowCardDetails(row.Card);
    }
    private void RowPrintButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is CardManagementRow row)
            QueueForPrinting(new[] { row.Card });
    }
    private void RowDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is CardManagementRow row)
            ShowCardDetails(row.Card);
    }
    private void RowArchiveRestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not CardManagementRow row)
            return;

        if (row.Card.IsArchived)
            RestoreCards(new[] { row.Card });
        else
            ArchiveCards(new[] { row.Card });
    }
    private void OpenBatch(GeneratedDemoCard card) =>
        NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("cards", "إدارة الدفعات", card.BatchId, "batch", SystemKey(card.TargetSystem)));
    private void BulkPrintButton_Click(object sender, RoutedEventArgs e) => QueueForPrinting(GetMarkedRows().Select(row => row.Card));
    private void QueueForPrinting(IEnumerable<GeneratedDemoCard> cards)
    {
        var selected = cards.DistinctBy(card => (card.TargetSystem, card.CardNumber)).ToList();
        if (selected.Count == 0)
        {
            SetOperationStatus("لم يتم تحديد كروت للطباعة.", true);
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "لا توجد كروت للطباعة", "حدد كرتًا واحدًا أو أكثر ثم أعد المحاولة.", WorkflowFeedbackKind.Warning, null,
                new WorkflowFeedbackAction("close", "العودة للقائمة", true));
            return;
        }
        try
        {
            var pendingKeys = _workflowStore.GetPendingPrintSelection()
                .Select(item => $"{NormalizeSystem(item.TargetSystem)}\u001F{item.BatchId}\u001F{item.CardNumber}")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var newlyQueued = selected.Count(card => !pendingKeys.Contains(WorkflowKey(card)));
            _workflowStore.QueuePrintSelection(selected);
            SetOperationStatus($"أُضيف {newlyQueued:N0} كرت إلى قائمة الطباعة؛ {selected.Count - newlyQueued:N0} كرت موجودة فيها مسبقًا.", false);
            var choice = WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "تم تجهيز قائمة الطباعة", "حُفظت العناصر في قائمة الطباعة المحلية، ولم تُرسل للطابعة بعد.", WorkflowFeedbackKind.Success,
                [new("إجمالي المحدد", selected.Count.ToString("N0")), new("أضيف حديثًا", newlyQueued.ToString("N0")), new("كان موجودًا مسبقًا", (selected.Count - newlyQueued).ToString("N0"))],
                new WorkflowFeedbackAction("open", "فتح قائمة الطباعة", true), new WorkflowFeedbackAction("close", "لاحقًا"));
            if (choice == "open")
                NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("printing", "قائمة الطباعة"));
        }
        catch (Exception ex)
        {
            SetOperationStatus("تعذر تحديث قائمة الطباعة. " + ex.Message, true);
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "تعذر تحديث قائمة الطباعة", ex.Message, WorkflowFeedbackKind.Error, null,
                new WorkflowFeedbackAction("close", "إغلاق", true));
        }
    }
    private void BulkArchiveButton_Click(object sender, RoutedEventArgs e) => ArchiveCards(GetMarkedRows().Select(row => row.Card));
    private void ArchiveCards(IEnumerable<GeneratedDemoCard> cards)
    {
        var selected = cards.Where(card => !card.IsArchived).DistinctBy(card => (card.TargetSystem, card.CardNumber)).ToList();
        if (selected.Count == 0)
        {
            SetOperationStatus("لا توجد كروت غير مؤرشفة في التحديد الحالي.", false);
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "لا توجد كروت للأرشفة", "حدد كروتًا غير مؤرشفة أولًا.", WorkflowFeedbackKind.Warning, null,
                new WorkflowFeedbackAction("close", "حسنًا", true));
            return;
        }
        var answer = WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "تأكيد أرشفة الكروت",
            $"ستؤرشف {selected.Count:N0} كرت. ستبقى سجلاتها محفوظة ويمكن استعادتها لاحقًا.", WorkflowFeedbackKind.Warning,
            [new("عدد الكروت", selected.Count.ToString("N0")), new("الأثر", "تغيير حالة الأرشفة في سجل Nexora")],
            new WorkflowFeedbackAction("confirm", "تأكيد الأرشفة", true), new WorkflowFeedbackAction("cancel", "إلغاء"));
        if (answer != "confirm") return;
        try
        {
            _issuanceService.ArchiveCards(selected, "أرشفة من شاشة إدارة الكروت");
            RefreshFromSource(preserveSelection: false);
            SetOperationStatus($"تمت أرشفة {selected.Count:N0} كرت.", false);
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "تمت أرشفة الكروت", "تم حفظ حالة الأرشفة في Nexora مع الإبقاء على سجلات الكروت.", WorkflowFeedbackKind.Success,
                [new("عدد الكروت", selected.Count.ToString("N0"))], new WorkflowFeedbackAction("close", "إغلاق", true));
        }
        catch (Exception ex)
        {
            SetOperationStatus("تعذر أرشفة الكروت. " + ex.Message, true);
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "تعذرت أرشفة الكروت", ex.Message, WorkflowFeedbackKind.Error, null,
                new WorkflowFeedbackAction("close", "إغلاق", true));
        }
    }
    private void BulkRestoreButton_Click(object sender, RoutedEventArgs e) => RestoreCards(GetMarkedRows().Select(row => row.Card));
    private void RestoreCards(IEnumerable<GeneratedDemoCard> cards)
    {
        var selected = cards.Where(card => card.IsArchived).DistinctBy(card => (card.TargetSystem, card.CardNumber)).ToList();
        if (selected.Count == 0)
        {
            SetOperationStatus("لا توجد سجلات مؤرشفة في التحديد الحالي.", false);
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "لا توجد كروت للاستعادة", "حدد سجلات مؤرشفة ثم أعد المحاولة.", WorkflowFeedbackKind.Warning, null,
                new WorkflowFeedbackAction("close", "حسنًا", true));
            return;
        }
        try
        {
            _issuanceService.RestoreCards(selected);
            RefreshFromSource(preserveSelection: false);
            SetOperationStatus($"تمت استعادة {selected.Count:N0} كرت من الأرشيف.", false);
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "تمت استعادة الكروت", "أُعيدت السجلات إلى العرض النشط مع الاحتفاظ بهوياتها وبيانات دفعاتها.", WorkflowFeedbackKind.Success,
                [new("عدد الكروت", selected.Count.ToString("N0"))], new WorkflowFeedbackAction("close", "إغلاق", true));
        }
        catch (Exception ex)
        {
            SetOperationStatus("تعذرت استعادة الكروت. " + ex.Message, true);
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "تعذرت استعادة الكروت", ex.Message, WorkflowFeedbackKind.Error, null,
                new WorkflowFeedbackAction("close", "إغلاق", true));
        }
    }
    private void SetOperationStatus(string message, bool isError)
    {
        OperationStatusTextBlock.Text = message;
        OperationStatusTextBlock.Foreground = (Brush)FindResource(isError ? "DangerBrush" : "MutedBrush");
        OperationStatusTextBlock.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
    }
    private void ShowCardDetails(GeneratedDemoCard card)
    {
        var workflow = _workflowStore.GetCardWorkflowStatuses()
            .Where(item => string.Equals(item.BatchId, card.BatchId, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(item.CardNumber, card.CardNumber, StringComparison.OrdinalIgnoreCase) &&
                           string.Equals(item.TargetSystem, SystemKey(card.TargetSystem), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.UpdatedAt)
            .FirstOrDefault();

        var details = new Window
        {
            Title = "تفاصيل الكرت — " + card.CardNumber,
            Width = 720,
            Height = 620,
            MinWidth = 610,
            MinHeight = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            FlowDirection = FlowDirection.RightToLeft,
            Background = (Brush)FindResource("AppBgBrush"),
            Owner = Window.GetWindow(this)
        };

        var root = new DockPanel { Margin = new Thickness(16) };
        var header = new Border
        {
            Background = Brushes.White,
            BorderBrush = (Brush)FindResource("BorderBrushSoft"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 10)
        };
        var headerContent = new StackPanel();
        headerContent.Children.Add(new TextBlock { Text = card.CardNumber, FontSize = 18, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("TextBrush") });
        headerContent.Children.Add(new TextBlock { Text = $"{card.TargetSystem} · الدفعة {card.BatchId}", FontSize = 11.5, Foreground = (Brush)FindResource("MutedBrush"), Margin = new Thickness(0, 4, 0, 0) });
        header.Child = headerContent;
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        var batchButton = new Button { Content = "فتح الدفعة الأصلية", Style = (Style)FindResource("SecondaryButton"), Height = 34, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 6, 0) };
        batchButton.Click += (_, _) => { details.Close(); OpenBatch(card); };
        var printButton = new Button { Content = "إضافة إلى الطباعة / إعادة الطباعة", Style = (Style)FindResource("SecondaryButton"), Height = 34, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(0, 0, 6, 0) };
        printButton.Click += (_, _) => { details.Close(); QueueForPrinting(new[] { card }); };
        var closeButton = new Button { Content = "إغلاق", Style = (Style)FindResource("PrimaryButton"), Height = 34, Padding = new Thickness(16, 0, 16, 0) };
        closeButton.Click += (_, _) => details.Close();
        footer.Children.Add(batchButton);
        footer.Children.Add(printButton);
        footer.Children.Add(closeButton);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var rows = new List<(string Label, string Value)>
        {
            ("اسم المستخدم", string.IsNullOrWhiteSpace(card.Username) ? "—" : card.Username),
            ("حالة الكرت", card.IsArchived ? "مؤرشفة" : card.DemoStatus),
            ("حالة الطباعة", PrintStatusLabelFor(workflow)),
            ("المصدر", IsImported(card) ? "مستورد" : "إصدار"),
            ("الباقة", card.PackageName),
            ("السعر", card.UnitPriceLabel),
            ("رقم الدفعة", card.BatchId),
            ("تاريخ الإنشاء", card.CreatedAtLabel),
            ("ملف الاستخدام", card.RouterProfile),
            ("الوقت المسموح", card.TimeLimit),
            ("حد البيانات", card.DataLimit),
            ("سياسة السرعة", card.RateLimit),
            ("مدة الصلاحية", card.PackageValidity),
            ("نقطة البيع", card.PointOfSale),
            ("الأجهزة المتزامنة المسموحة", card.SharedUsersLabel),
            ("ملاحظات", string.IsNullOrWhiteSpace(card.Comment) ? "—" : card.Comment)
        };
        if (SystemKey(card.TargetSystem) == "user-manager")
            rows.Insert(rows.FindIndex(row => row.Label == "ملاحظات"), ("حساب مدير المستخدمين", card.ManagerAccount));
        if (card.IsArchived)
            rows.Insert(3, ("سبب الأرشفة", string.IsNullOrWhiteSpace(card.ArchiveReason) ? "أرشفة" : card.ArchiveReason));

        var batchActions = _workflowStore.GetActionHistory()
            .Where(action => action.ActionType != "SyncRequested" && action.ActionType != "OnlineSalePrepared")
            .Where(action => string.Equals(action.BatchId, card.BatchId, StringComparison.OrdinalIgnoreCase) &&
                             string.Equals(NormalizeSystem(action.TargetSystem), SystemKey(card.TargetSystem), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(action => action.CreatedAt)
            .Take(5)
            .Select(action => $"{action.CreatedAtLabel} · {action.ActionLabel} · {action.Count:N0} سجل · {action.Description}")
            .ToList();
        rows.Add(("سجل إجراءات الدفعة", batchActions.Count == 0 ? "لا توجد إجراءات مسجلة لهذه الدفعة." : string.Join(Environment.NewLine, batchActions)));

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(175) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var index = 0; index < rows.Count; index++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var background = index % 2 == 0 ? Brushes.White : new SolidColorBrush(Color.FromRgb(248, 250, 252));
            var label = new Border { Background = background, Padding = new Thickness(10, 7, 10, 7), BorderBrush = (Brush)FindResource("BorderBrushSoft"), BorderThickness = new Thickness(0, 0, 0, 1) };
            label.Child = new TextBlock { Text = rows[index].Label, FontWeight = FontWeights.SemiBold, FontSize = 11, Foreground = (Brush)FindResource("MutedBrush"), TextWrapping = TextWrapping.Wrap };
            var value = new Border { Background = background, Padding = new Thickness(10, 7, 10, 7), BorderBrush = (Brush)FindResource("BorderBrushSoft"), BorderThickness = new Thickness(0, 0, 0, 1) };
            value.Child = new TextBlock { Text = string.IsNullOrWhiteSpace(rows[index].Value) ? "—" : rows[index].Value, FontSize = 11.5, Foreground = (Brush)FindResource("TextBrush"), TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(label, index); Grid.SetColumn(label, 0); grid.Children.Add(label);
            Grid.SetRow(value, index); Grid.SetColumn(value, 1); grid.Children.Add(value);
        }
        scroll.Content = grid;
        root.Children.Add(scroll);
        details.Content = root;
        details.ShowDialog();
    }
    internal static string PrintStatusLabelFor(DemoCardWorkflowStatus? workflow) =>
        workflow?.PrintQueued == true ? "في قائمة الطباعة" :
        workflow?.PrintSubmitted == true ? "مهمة طباعة مسجلة" :
        workflow?.PrintPrepared == true ? "مجهز للطباعة" : "غير مضاف للطباعة";
    private static string IdentityKey(GeneratedDemoCard card) => SystemKey(card.TargetSystem) + "\u001F" + card.CardNumber;
    private static string WorkflowKey(GeneratedDemoCard card) => SystemKey(card.TargetSystem) + "\u001F" + card.BatchId + "\u001F" + card.CardNumber;
    private static string WorkflowKey(DemoCardWorkflowStatus status) => NormalizeSystem(status.TargetSystem) + "\u001F" + status.BatchId + "\u001F" + status.CardNumber;
    private static string SystemKey(string targetSystem) => targetSystem.Contains("هوتسبوت", StringComparison.OrdinalIgnoreCase) ? "hotspot" : "user-manager";
    private static string NormalizeSystem(string value) =>
        value.Contains("hotspot", StringComparison.OrdinalIgnoreCase) || value.Contains("هوتسبوت", StringComparison.OrdinalIgnoreCase)
            ? "hotspot"
            : "user-manager";
    private static bool IsImported(GeneratedDemoCard card) =>
        (card.Comment ?? string.Empty).StartsWith("استيراد من ملف:", StringComparison.OrdinalIgnoreCase);
}
