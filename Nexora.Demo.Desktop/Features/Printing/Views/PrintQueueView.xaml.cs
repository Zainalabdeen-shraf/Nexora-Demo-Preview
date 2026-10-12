using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Documents;
using System.Windows.Markup;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Shared;

namespace Nexora.Demo.Desktop.Features.Printing.Views;

public partial class PrintQueueView : UserControl
{
    private readonly DemoCardIssuanceService _issuanceService;
    private bool _isSubscribed;

    public PrintQueueView(DemoCardIssuanceService issuanceService)
    {
        InitializeComponent();
        _issuanceService = issuanceService;
        PrintQueueGrid.ItemsSource = _issuanceService.Cards;
        Loaded += PrintQueueView_Loaded;
        Unloaded += PrintQueueView_Unloaded;
        UpdateSummary();
        UpdatePreview();
    }

    public event EventHandler<FeatureNavigationRequestedEventArgs>? NavigationRequested;

    private void PrintQueueView_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_isSubscribed)
        {
            _issuanceService.ScenarioChanged += IssuanceService_ScenarioChanged;
            _isSubscribed = true;
        }
        UpdateSummary();
        UpdatePreview();
    }

    private void PrintQueueView_Unloaded(object sender, RoutedEventArgs e)
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
            UpdateSummary();
            // Follow the newest generated record once per completed batch, not once per card.
            if (_issuanceService.Cards.Count > 0)
            {
                PrintQueueGrid.SelectedItem = _issuanceService.Cards[^1];
                PrintQueueGrid.ScrollIntoView(PrintQueueGrid.SelectedItem);
            }
            UpdatePreview();
        });
    }

    private void PrintQueueGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdatePreview();
    private void PreviewFields_Changed(object sender, RoutedEventArgs e) => UpdatePreview();
    private void TemplateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateTemplateDimensions();

    private void RefreshQueueButton_Click(object sender, RoutedEventArgs e)
    {
        UpdateSummary();
        UpdatePreview();
    }

    private void UpdateSummary()
    {
        if (PrintQueueSummaryTextBlock is null)
            return;
        var all = _issuanceService.Cards.Count;
        var pending = _tabCardWorkflowStore.GetPendingPrintSelection().Count;
        PrintQueueSummaryTextBlock.Text = all == 0
            ? "لا توجد كروت بعد. ابدأ بإنشاء دفعة من قسم الكروت والإصدار."
            : $"{pending:N0} كرت بانتظار الطباعة · {all:N0} كرت متاح للاختيار · { _issuanceService.Batches.Count:N0} دفعة.";
    }

    private void UpdatePreview()
    {
        if (PrintQueueGrid is null || PreviewNumberTextBlock is null)
            return;

        var card = PrintQueueGrid.SelectedItem as GeneratedDemoCard ?? _issuanceService.Cards.LastOrDefault();
        if (card is null)
        {
            PreviewNumberTextBlock.Text = "لم يتم اختيار كرت";
            PreviewUsernameTextBlock.Text = "";
            PreviewPasswordTextBlock.Text = "";
            PreviewPackageTextBlock.Text = "الباقة";
            PreviewPriceTextBlock.Text = "السعر";
            PrintPreviewStatusTextBlock.Text = "أنشئ دفعة من قسم إصدار الكروت، ثم ارجع إلى هذه القائمة.";
            ApplyFieldVisibility();
            return;
        }

        PreviewNumberTextBlock.Text = card.CardNumber;
        PreviewUsernameTextBlock.Text = $"اسم المستخدم: {card.Username}";
        PreviewPasswordTextBlock.Text = string.IsNullOrEmpty(card.Password)
            ? "بدون كلمة مرور"
            : $"كلمة المرور: {card.Password}";
        PreviewPackageTextBlock.Text = card.PackageName.Split('—')[0].Trim();
        PreviewPriceTextBlock.Text = card.UnitPriceLabel;
        PrintPreviewStatusTextBlock.Text = $"معاينة الكرت {card.CardNumber} · الدفعة {card.BatchId} · {card.TargetSystem}.";
        ApplyFieldVisibility();
        UpdateTemplateDimensions();
    }

    private void ApplyFieldVisibility()
    {
        PreviewNumberTextBlock.Visibility = ShowNumberCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PreviewUsernameTextBlock.Visibility = ShowUsernameCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PreviewPasswordTextBlock.Visibility = ShowPasswordCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PreviewPackageTextBlock.Visibility = ShowPackageCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PreviewPriceTextBlock.Visibility = ShowPriceCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateTemplateDimensions()
    {
        if (PrintPreviewBorder is null || TemplateComboBox is null)
            return;

        // Let the preview take the available column width instead of overflowing narrow desktop windows.
        var template = (TemplateComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        PrintPreviewBorder.Width = double.NaN;
        PrintPreviewBorder.MaxWidth = 420;
        PrintPreviewBorder.HorizontalAlignment = HorizontalAlignment.Stretch;
        (PrintPreviewBorder.Height, PrintPreviewBorder.Padding) = template switch
        {
            "compact" => (140d, new Thickness(10)),
            "wide" => (165d, new Thickness(12)),
            _ => (190d, new Thickness(13))
        };
    }

    private void PreparePreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (_issuanceService.Cards.Count == 0)
        {
            PrintPreviewStatusTextBlock.Text = "لا توجد كروت في القائمة حتى الآن.";
            PrintPreviewStatusTextBlock.Foreground = (Brush)FindResource("WarningBrush");
            return;
        }

        var count = PrintQueueGrid.SelectedItems.Count > 0
            ? PrintQueueGrid.SelectedItems.Count
            : _issuanceService.Cards.Count;
        PrintPreviewStatusTextBlock.Text = $"تم تحديث معاينة {count:N0} كرت وفق القالب والحقول المحددة.";
        PrintPreviewStatusTextBlock.Foreground = (Brush)FindResource("SuccessBrush");
    }

    private void SimulatePrintButton_Click(object sender, RoutedEventArgs e) => PrintSelectedCards();

    private void AddSelectedToQueueButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = PrintQueueGrid.SelectedItems.OfType<GeneratedDemoCard>().ToList();
        if (selected.Count == 0)
        {
            PrintPreviewStatusTextBlock.Text = "حدد كرتًا واحدًا أو أكثر لإضافتها إلى قائمة الطباعة.";
            PrintPreviewStatusTextBlock.Foreground = (Brush)FindResource("WarningBrush");
            return;
        }

        try
        {
            _tabCardWorkflowStore.QueuePrintSelection(selected);
            UpdateSummary();
            PrintPreviewStatusTextBlock.Text = $"تمت إضافة {selected.Count:N0} كرت إلى قائمة الطباعة.";
            PrintPreviewStatusTextBlock.Foreground = (Brush)FindResource("SuccessBrush");
        }
        catch (Exception ex)
        {
            PrintPreviewStatusTextBlock.Text = "تعذر إضافة الكروت إلى قائمة الطباعة: " + ex.Message;
            PrintPreviewStatusTextBlock.Foreground = (Brush)FindResource("WarningBrush");
        }
    }

    private void PrintSelectedCards()
    {
        var selected = PrintQueueGrid.SelectedItems.OfType<GeneratedDemoCard>().ToList();
        if (selected.Count == 0 && PrintQueueGrid.SelectedItem is GeneratedDemoCard one)
            selected.Add(one);
        if (selected.Count == 0)
        {
            PrintPreviewStatusTextBlock.Text = "حدد الكروت التي تريد طباعتها أولًا.";
            PrintPreviewStatusTextBlock.Foreground = (Brush)FindResource("WarningBrush");
            return;
        }

        var dialog = new System.Windows.Controls.PrintDialog();
        if (dialog.ShowDialog() != true)
        {
            PrintPreviewStatusTextBlock.Text = "تم إلغاء مهمة الطباعة.";
            return;
        }

        try
        {
            var pageWidth = dialog.PrintableAreaWidth > 100 ? dialog.PrintableAreaWidth : 793.7;
            var pageHeight = dialog.PrintableAreaHeight > 100 ? dialog.PrintableAreaHeight : 1122.5;
            var document = new FixedDocument();
            document.DocumentPaginator.PageSize = new Size(pageWidth, pageHeight);
            var template = (TemplateComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "standard";

            foreach (var card in selected)
            {
                var (requestedWidth, requestedHeight) = template switch
                {
                    "compact" => (320d, 178d),
                    "wide" => (520d, 230d),
                    _ => (420d, 225d)
                };
                var cardWidth = Math.Min(requestedWidth, pageWidth - 60);
                var cardHeight = Math.Min(requestedHeight, pageHeight - 80);
                var printedCard = BuildPrintedCard(card, cardWidth, cardHeight);
                var page = new FixedPage { Width = pageWidth, Height = pageHeight, FlowDirection = FlowDirection.RightToLeft };
                FixedPage.SetLeft(printedCard, (pageWidth - cardWidth) / 2d);
                FixedPage.SetTop(printedCard, (pageHeight - cardHeight) / 2d);
                page.Children.Add(printedCard);
                var pageContent = new PageContent();
                ((IAddChild)pageContent).AddChild(page);
                document.Pages.Add(pageContent);
            }

            dialog.PrintDocument(document.DocumentPaginator, $"Nexora — {selected.Count:N0} بطاقة");
            var printerName = dialog.PrintQueue?.FullName ?? "الطابعة المحددة";
            try
            {
                _tabCardWorkflowStore.RecordPrintSubmitted(selected, printerName);
                UpdateSummary();
                PrintPreviewStatusTextBlock.Text = $"أُرسلت مهمة طباعة تضم {selected.Count:N0} بطاقة إلى {printerName}.";
                PrintPreviewStatusTextBlock.Foreground = (Brush)FindResource("SuccessBrush");
            }
            catch (Exception recordException)
            {
                PrintPreviewStatusTextBlock.Text = $"أُرسلت مهمة الطباعة إلى {printerName}، لكن تعذر تحديث سجل المتابعة: {recordException.Message}";
                PrintPreviewStatusTextBlock.Foreground = (Brush)FindResource("WarningBrush");
            }
        }
        catch (Exception ex)
        {
            PrintPreviewStatusTextBlock.Text = "تعذر إرسال مهمة الطباعة: " + ex.Message;
            PrintPreviewStatusTextBlock.Foreground = (Brush)FindResource("WarningBrush");
        }
    }

    private Border BuildPrintedCard(GeneratedDemoCard card, double width, double height)
    {
        var border = new Border
        {
            Width = width,
            Height = height,
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(118, 13, 13)),
            Background = new SolidColorBrush(Color.FromRgb(160, 24, 24)),
            FlowDirection = FlowDirection.RightToLeft
        };
        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        panel.Children.Add(new TextBlock
        {
            Text = "NEXORA",
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 8)
        });

        if (ShowNumberCheckBox.IsChecked == true)
            panel.Children.Add(PrintLine("رقم الكرت: " + card.CardNumber, 15, true));
        if (ShowUsernameCheckBox.IsChecked == true)
            panel.Children.Add(PrintLine("اسم المستخدم: " + card.Username, 12, false));
        if (ShowPasswordCheckBox.IsChecked == true && !string.IsNullOrWhiteSpace(card.Password))
            panel.Children.Add(PrintLine("كلمة المرور: " + card.Password, 12, false));

        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        if (ShowPackageCheckBox.IsChecked == true)
            footer.Children.Add(PrintLine(card.PackageName, 10.5, true));
        if (ShowPriceCheckBox.IsChecked == true)
        {
            var price = PrintLine(card.UnitPriceLabel, 10.5, true);
            DockPanel.SetDock(price, Dock.Left);
            footer.Children.Add(price);
        }
        panel.Children.Add(footer);
        border.Child = panel;
        return border;
    }

    private static TextBlock PrintLine(string text, double size, bool bold) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = Brushes.White,
        Margin = new Thickness(0, 2, 0, 2),
        TextWrapping = TextWrapping.Wrap
    };

    private void BackToIssuanceButton_Click(object sender, RoutedEventArgs e)
    {
        var selectedCard = PrintQueueGrid.SelectedItems.OfType<GeneratedDemoCard>().FirstOrDefault()
            ?? PrintQueueGrid.SelectedItem as GeneratedDemoCard
            ?? _issuanceService.Cards.LastOrDefault();
        var target = selectedCard?.TargetSystem == "مستخدمو الهوتسبوت"
            ? "hotspot"
            : "user-manager";
        NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs(target, "إضافة الكروت"));
    }
}
