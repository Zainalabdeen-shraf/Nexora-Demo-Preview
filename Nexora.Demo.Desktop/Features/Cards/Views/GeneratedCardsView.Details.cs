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

// Responsibility: Card selection, focus, and details display.
public partial class GeneratedCardsView
{
    public void FocusCard(string cardNumber)
    {
        SetSearchQuery(cardNumber);
        var card = _cardsView.Cast<object>().OfType<GeneratedDemoCard>()
            .FirstOrDefault(item => string.Equals(item.CardNumber, cardNumber, StringComparison.OrdinalIgnoreCase));
        if (card is not null)
        {
            GeneratedCardsGrid.SelectedItem = card;
            GeneratedCardsGrid.ScrollIntoView(card);
        }
    }
    public void OpenCardDetails(string cardNumber)
    {
        FocusCard(cardNumber);
        if (GeneratedCardsGrid.SelectedItem is GeneratedDemoCard card)
            ShowCardDetails(card);
    }
    private void GeneratedCardsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateWorkflowStatus();
    private void SelectAllVisibleCardsButton_Click(object sender, RoutedEventArgs e)
    {
        GeneratedCardsGrid.SelectAll();
        UpdateWorkflowStatus();
    }
    private void ClearSelectedCardsButton_Click(object sender, RoutedEventArgs e)
    {
        GeneratedCardsGrid.UnselectAll();
        UpdateWorkflowStatus();
    }
    private void OpenSelectedCardDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedCards();
        if (selected.Count != 1)
        {
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "حدد كرتًا واحدًا", "حدد كرتًا واحدًا فقط لعرض تفاصيله.", WorkflowFeedbackKind.Warning, null, new WorkflowFeedbackAction("close", "حسنًا", true));
            return;
        }
        ShowCardDetails(selected[0]);
    }
    private void GeneratedCardsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GeneratedCardsGrid.SelectedItem is GeneratedDemoCard card)
            ShowCardDetails(card);
    }
    private void ShowCardDetails(GeneratedDemoCard card)
    {
        var owner = Window.GetWindow(this);
        var details = new Window
        {
            Title = $"تفاصيل الحساب والتجهيز — {card.CardNumber}",
            Width = 690,
            Height = 600,
            MinWidth = 580,
            MinHeight = 480,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            FlowDirection = FlowDirection.RightToLeft,
            Background = new SolidColorBrush(Color.FromRgb(244, 246, 249)),
            Owner = owner
        };

        var root = new DockPanel { Margin = new Thickness(18) };

        // الهيدر العلوي
        var headerBorder = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 0, 0, 12)
        };

        var headerPanel = new StackPanel();
        var statusLabelText = _workflowStore.GetCardStatusLabel(card.BatchId, card.CardNumber);

        headerPanel.Children.Add(new TextBlock { Text = $"👤 رقم الكرت / اسم المستخدم: {card.CardNumber}", FontSize = 17, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(32, 43, 69)) });

        var statusTextBlock = new TextBlock { Text = $"🏷️ النظام: {card.TargetSystem}  |  📂 الدفعة: {card.BatchId}  |  📌 الحالة الإجرائية: {statusLabelText}", FontSize = 12, Foreground = new SolidColorBrush(Color.FromRgb(201, 24, 61)), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 5, 0, 0) };
        headerPanel.Children.Add(statusTextBlock);
        headerBorder.Child = headerPanel;
        DockPanel.SetDock(headerBorder, Dock.Top);
        root.Children.Add(headerBorder);

        // شريط الأزرار التفاعلية المباشرة داخل نافذة التفاصيل
        var actionPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) };

        var syncBtn = new Button { Content = "⚡ طلب مزامنة", Width = 110, Height = 34, Margin = new Thickness(4, 0, 4, 0), Background = new SolidColorBrush(Color.FromRgb(238, 242, 255)), Foreground = new SolidColorBrush(Color.FromRgb(67, 56, 202)), FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand };
        var printBtn = new Button { Content = "🖨️ تجهيز طباعة", Width = 110, Height = 34, Margin = new Thickness(4, 0, 4, 0), Background = new SolidColorBrush(Color.FromRgb(236, 253, 245)), Foreground = new SolidColorBrush(Color.FromRgb(4, 120, 87)), FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand };
        var saleBtn = new Button { Content = "🛒 بيع إلكتروني", Width = 110, Height = 34, Margin = new Thickness(4, 0, 4, 0), Background = new SolidColorBrush(Color.FromRgb(255, 241, 242)), Foreground = new SolidColorBrush(Color.FromRgb(225, 29, 72)), FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand };

        syncBtn.Click += (_, _) =>
        {
            _workflowStore.RecordRouterUpload(new[] { card });
            statusTextBlock.Text = $"🏷️ النظام: {card.TargetSystem}  |  📂 الدفعة: {card.BatchId}  |  📌 الحالة الإجرائية: {_workflowStore.GetCardStatusLabel(card.BatchId, card.CardNumber)}";
            RefreshFilteredCards();
            WorkflowFeedbackWindow.ShowNotice(details, "طلب المزامنة محفوظ محليًا", "تم تسجيل طلب المزامنة داخل Nexora فقط. لم يتم إرسال الحساب إلى الراوتر لأن الموصل المتاح للقراءة فقط.", WorkflowFeedbackKind.Warning,
                [new("الكرت", card.CardNumber), new("الحالة", "بانتظار موصل كتابة")], new WorkflowFeedbackAction("close", "إغلاق", true));
        };

        printBtn.Click += (_, _) =>
        {
            _workflowStore.QueuePrintSelection(new[] { card });
            statusTextBlock.Text = $"🏷️ النظام: {card.TargetSystem}  |  📂 الدفعة: {card.BatchId}  |  📌 الحالة الإجرائية: {_workflowStore.GetCardStatusLabel(card.BatchId, card.CardNumber)}";
            RefreshFilteredCards();
            WorkflowFeedbackWindow.ShowNotice(details, "تم تجهيز الكرت للطباعة", "أُضيف الكرت إلى قائمة الطباعة المحلية. لم يتم إرسال المستند للطابعة بعد.", WorkflowFeedbackKind.Success,
                [new("الكرت", card.CardNumber), new("الخطوة التالية", "مراجعة قائمة الطباعة")], new WorkflowFeedbackAction("close", "حسنًا", true));
        };

        saleBtn.Click += (_, _) =>
        {
            _workflowStore.RecordOnlineSalePreparation(new[] { card });
            statusTextBlock.Text = $"🏷️ النظام: {card.TargetSystem}  |  📂 الدفعة: {card.BatchId}  |  📌 الحالة الإجرائية: {_workflowStore.GetCardStatusLabel(card.BatchId, card.CardNumber)}";
            RefreshFilteredCards();
            WorkflowFeedbackWindow.ShowNotice(details, "تم تجهيز الكرت للبيع الإلكتروني محليًا", "تم تسجيل حالة التجهيز في Nexora فقط؛ لم يُنشر الكرت في متجر خارجي.", WorkflowFeedbackKind.Info,
                [new("الكرت", card.CardNumber), new("الحالة", "تجهيز محلي")], new WorkflowFeedbackAction("close", "حسنًا", true));
        };

        actionPanel.Children.Add(syncBtn);
        actionPanel.Children.Add(printBtn);
        actionPanel.Children.Add(saleBtn);

        // زر الإغلاق السفلي
        var footerPanel = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var closeButton = new Button { Content = "إغلاق النافذة", Width = 100, Height = 34, Background = new SolidColorBrush(Color.FromRgb(233, 37, 77)), Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand };
        closeButton.Click += (_, _) => details.Close();
        DockPanel.SetDock(closeButton, Dock.Left);
        footerPanel.Children.Add(closeButton);
        footerPanel.Children.Add(actionPanel);

        DockPanel.SetDock(footerPanel, Dock.Bottom);
        root.Children.Add(footerPanel);

        // جدول التفاصيل
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var detailsGrid = new Grid();
        detailsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        detailsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var rows = new (string Label, string Value)[]
        {
            ("كلمة المرور", string.IsNullOrEmpty(card.Password) ? "🔒 (فارغة)" : $"🔑 {card.Password}"),
            ("الباقة المرتبطة", $"📦 {card.PackageName}"),
            ("قيمة السعر", $"💵 {card.UnitPriceLabel}"),
            ("الوقت المسموح", $"⏱️ {card.TimeLimit}"),
            ("حد البيانات", $"📊 {card.DataLimit}"),
            ("سياسة السرعة", $"🚀 {card.RateLimit}"),
            ("مدة الصلاحية", $"📅 {card.PackageValidity}"),
            ("ملف الراوتر (Profile)", $"⚙️ {card.RouterProfile}"),
            ("نقطة البيع", $"🏢 {card.PointOfSale}"),
            ("حساب المشرف", $"👨‍💻 {card.ManagerAccount}"),
            ("المستخدمون المتزامنون", $"👥 {card.SharedUsersLabel}"),
            ("تاريخ الإنشاء المحلي", $"🕒 {card.CreatedAtLabel}"),
            ("ملاحظات إضافية", string.IsNullOrWhiteSpace(card.Comment) ? "—" : card.Comment)
        };

        for (var i = 0; i < rows.Length; i++)
        {
            detailsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var bg = i % 2 == 0 ? Colors.White : Color.FromRgb(248, 250, 253);

            var lblBorder = new Border { Padding = new Thickness(10, 8, 10, 8), Background = new SolidColorBrush(bg), BorderBrush = new SolidColorBrush(Color.FromRgb(232, 221, 228)), BorderThickness = new Thickness(0, 0, 0, 1) };
            lblBorder.Child = new TextBlock { Text = rows[i].Label, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(104, 117, 141)), FontSize = 11.5 };

            var valBorder = new Border { Padding = new Thickness(10, 8, 10, 8), Background = new SolidColorBrush(bg), BorderBrush = new SolidColorBrush(Color.FromRgb(232, 221, 228)), BorderThickness = new Thickness(0, 0, 0, 1) };
            valBorder.Child = new TextBlock { Text = rows[i].Value, Foreground = new SolidColorBrush(Color.FromRgb(32, 43, 69)), FontSize = 11.5, TextWrapping = TextWrapping.Wrap };

            Grid.SetRow(lblBorder, i); Grid.SetColumn(lblBorder, 0); detailsGrid.Children.Add(lblBorder);
            Grid.SetRow(valBorder, i); Grid.SetColumn(valBorder, 1); detailsGrid.Children.Add(valBorder);
        }

        scroll.Content = detailsGrid;
        root.Children.Add(scroll);
        details.Content = root;
        details.ShowDialog();
    }
}
