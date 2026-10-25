using System.Globalization;
using Microsoft.Win32;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Nexora.Demo.Desktop.Features.Cards.Models;

namespace Nexora.Demo.Desktop.Features.Printing.Views;

// Responsibility: Print-preview and sample-card dialogs.
public partial class TemplateDesignerView
{
    private void PreviewTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyPropertiesButton_Click(this, new RoutedEventArgs());
        ShowPrintPreviewDialog();
    }
    private void ShowPrintPreviewDialog()
    {
        const double pageWidthUnits = 210d;
        const double pageHeightUnits = 297d; // A4 portrait dimensions, represented in millimetre-like page units
        var dialog = CreateDialog("معاينة صفحة الطباعة", 1180, 820);
        dialog.MinWidth = 900;
        dialog.MinHeight = 650;
        dialog.WindowState = WindowState.Normal;
        dialog.ResizeMode = ResizeMode.CanResize;
        dialog.Owner = Window.GetWindow(this);
        dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        dialog.Topmost = false;
        var root = new Grid { Margin = new Thickness(12) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock
        {
            Text = "معاينة صفحة الطباعة — الصفحة كاملة",
            FontSize = 19,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("PrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 8)
        };
        Grid.SetRow(title, 0);
        root.Children.Add(title);

        var settingsBorder = new Border
        {
            Background = Brushes.White,
            BorderBrush = (Brush)FindResource("BorderBrushSoft"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8),
            Margin = new Thickness(0, 0, 0, 8)
        };
        var settingsPanel = new UniformGrid { Columns = 7 };
        var selectedPackage = GetActivePackage();
        var hours = AddTextField(settingsPanel, "الوقت / مدة الاستخدام", selectedPackage?.TimeLimit ?? "—");
        var validity = AddTextField(settingsPanel, "الصلاحية بالأيام", selectedPackage?.Validity ?? "—");
        var price = AddTextField(settingsPanel, "السعر على الكرت", selectedPackage is null ? "—" : selectedPackage.PrintPrice.ToString("0.##", CultureInfo.InvariantCulture));
        var upload = AddTextField(settingsPanel, "حد التحميل", selectedPackage?.DataLimit ?? "—");
        var cardCount = AddTextField(settingsPanel, "إجمالي الكروت", "51");
        var columns = AddTextField(settingsPanel, "عدد الأعمدة", "3");
        var rows = AddTextField(settingsPanel, "حد الصفوف/صفحة", "17");
        var numberPrefix = AddTextField(settingsPanel, "بداية الرقم", "50723001");
        var usernamePrefix = AddTextField(settingsPanel, "بادئة اسم المستخدم", "");
        var passwordPrefix = AddTextField(settingsPanel, "بادئة كلمة المرور", "");
        var packageName = AddTextField(settingsPanel, "اسم الباقة", selectedPackage is not null ? GetPackageName(selectedPackage) : "—");
        var networkName = AddTextField(settingsPanel, "اسم الشبكة", PreviewNetworkTextBlock.Text);
        var contact = AddTextField(settingsPanel, "أرقام التواصل", PreviewContactBlock.Text);
        networkName.ToolTip = "اكتب اسم الشبكة الحقيقي؛ سيتم تحديث المصمم. احفظ القالب لتثبيت التغيير للطباعة.";
        contact.ToolTip = "أدخل أرقام التواصل الصحيحة؛ لا توجد أرقام مثبتة في كتالوج الباقات. احفظ القالب بعد التعديل.";
        networkName.TextChanged += (_, _) =>
        {
            if (PreviewNetworkTextBlock.Text == networkName.Text) return;
            PreviewNetworkTextBlock.Text = networkName.Text;
            UpdatePreview();
            SetStatus("تم تحديث اسم الشبكة في المصمم؛ احفظ القالب لتثبيته للطباعة.", isSuccess: true);
        };
        contact.TextChanged += (_, _) =>
        {
            if (PreviewContactBlock.Text == contact.Text) return;
            PreviewContactBlock.Text = contact.Text;
            UpdatePreview();
            SetStatus("تم تحديث أرقام التواصل في المصمم؛ احفظ القالب لتثبيته للطباعة.", isSuccess: true);
        };
        var previewPage = AddTextField(settingsPanel, "رقم الصفحة", "1");
        hours.IsReadOnly = validity.IsReadOnly = price.IsReadOnly = upload.IsReadOnly = packageName.IsReadOnly = true;
        hours.ToolTip = validity.ToolTip = price.ToolTip = upload.ToolTip = packageName.ToolTip = "تُقرأ هذه البيانات من الباقة المختارة؛ اختر الباقة من شريط المصمم لتغييرها.";
        settingsBorder.Child = settingsPanel;
        Grid.SetRow(settingsBorder, 1);
        root.Children.Add(settingsBorder);

        var previewBorder = new Border
        {
            Background = (Brush)FindResource("AppBgBrush"),
            BorderBrush = (Brush)FindResource("BorderBrushSoft"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(5)
        };
        var previewRoot = new Grid();
        previewRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        previewRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var previewSummary = new TextBlock
        {
            Text = "ورقة A4 عمودية (210 × 297 مم) — افتراضيًا 3 أعمدة × 17 صفًا = 51 كرتًا.",
            Foreground = (Brush)FindResource("MutedBrush"),
            FontSize = 10.5,
            Margin = new Thickness(3, 0, 3, 4)
        };
        Grid.SetRow(previewSummary, 0);
        previewRoot.Children.Add(previewSummary);

        var pageHost = new Grid { ClipToBounds = true, Background = (Brush)FindResource("AppBgBrush") };
        var pageCards = new Canvas { Width = pageWidthUnits, Height = pageHeightUnits, ClipToBounds = true };
        var pageSurface = new Border
        {
            Width = pageWidthUnits,
            Height = pageHeightUnits,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xCF, 0xD4, 0xDB)),
            BorderThickness = new Thickness(0.5),
            Padding = new Thickness(0),
            Child = pageCards,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 9, ShadowDepth = 2, Opacity = 0.16 }
        };
        var pageViewbox = new Viewbox { Stretch = Stretch.Uniform, Child = pageSurface, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        pageHost.Children.Add(pageViewbox);
        pageHost.SizeChanged += (_, _) => FitPrintPage();
        Grid.SetRow(pageHost, 1);
        previewRoot.Children.Add(pageHost);
        previewBorder.Child = previewRoot;
        Grid.SetRow(previewBorder, 2);
        root.Children.Add(previewBorder);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 7, 0, 0) };
        var updatePreviewButton = MakeButton("معاينة الصفحة", primary: true);
        updatePreviewButton.MinWidth = 130;
        var closeButton = MakeButton("إغلاق", primary: false);
        closeButton.MinWidth = 90;
        closeButton.Margin = new Thickness(8, 0, 0, 0);
        updatePreviewButton.Click += (_, _) => RenderSampleCards();
        closeButton.Click += (_, _) => dialog.Close();
        actions.Children.Add(updatePreviewButton);
        actions.Children.Add(closeButton);
        Grid.SetRow(actions, 3);
        root.Children.Add(actions);
        dialog.Content = root;

        void FitPrintPage()
        {
            if (pageHost.ActualWidth < 50 || pageHost.ActualHeight < 50) return;
            var availableWidth = Math.Max(1, pageHost.ActualWidth - 14);
            var availableHeight = Math.Max(1, pageHost.ActualHeight - 10);
            var scale = Math.Min(availableWidth / pageWidthUnits, availableHeight / pageHeightUnits);
            pageViewbox.Width = pageWidthUnits * scale;
            pageViewbox.Height = pageHeightUnits * scale;
        }

        void RenderSampleCards()
        {
            const double pageMarginMm = 5d;
            const double cardGapMm = 1d;
            var total = ParseInt(cardCount.Text, 51, 1, 10000);
            var requestedColumns = ParseInt(columns.Text, 3, 1, 8);
            var cardWidthMm = ParseDouble(CardWidthTextBox.Text, 66, 10, 200);
            var cardHeightMm = ParseDouble(CardHeightTextBox.Text, 15.5, 10, 200);
            var usableWidthMm = pageWidthUnits - pageMarginMm * 2d;
            var usableHeightMm = pageHeightUnits - pageMarginMm * 2d;
            var maxColumns = Math.Max(1, (int)Math.Floor((usableWidthMm + cardGapMm) / (cardWidthMm + cardGapMm)));
            var maxRows = Math.Max(1, (int)Math.Floor((usableHeightMm + cardGapMm) / (cardHeightMm + cardGapMm)));
            var colCount = Math.Min(requestedColumns, maxColumns);
            var requestedRows = ParseInt(rows.Text, maxRows, 1, 100);
            var rowCapacity = Math.Min(requestedRows, maxRows);
            var perPage = Math.Max(1, rowCapacity * colCount);
            var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)perPage));
            var pageNumber = ParseInt(previewPage.Text, 1, 1, totalPages);
            previewPage.Text = pageNumber.ToString(CultureInfo.InvariantCulture);
            columns.Text = colCount.ToString(CultureInfo.InvariantCulture);
            rows.Text = rowCapacity.ToString(CultureInfo.InvariantCulture);
            cardCount.Text = total.ToString(CultureInfo.InvariantCulture);
            if (colCount != requestedColumns || rowCapacity != requestedRows)
                SetStatus($"تم ضبط التخطيط حسب مقاس الكرت الفعلي: {colCount} أعمدة × {rowCapacity} صفوف كحد أقصى؛ لم يتم تصغير الخطوط أو الكروت.", isSuccess: false);

            pageCards.Children.Clear();
            var firstIndex = (pageNumber - 1) * perPage;
            var cardsOnPage = Math.Min(perPage, Math.Max(0, total - firstIndex));
            var first = ParseLong(numberPrefix.Text, 50723001);
            var priceText = string.IsNullOrWhiteSpace(price.Text) ? "—" : price.Text.Trim();
            var uploadText = string.IsNullOrWhiteSpace(upload.Text) ? "—" : upload.Text.Trim();
            previewSummary.Text = $"A4 عمودي · الصفحة {pageNumber} من {totalPages} · {colCount} أعمدة · السعة {perPage} كرت/صفحة · {cardsOnPage} كرت في هذه الصفحة · إجمالي {total:N0} كرت · المقاس {cardWidthMm:0.#} × {cardHeightMm:0.#} مم";

            for (var i = 0; i < cardsOnPage; i++)
            {
                var globalIndex = firstIndex + i;
                var cardNumber = (first + globalIndex).ToString(CultureInfo.InvariantCulture);
                var sampleUsername = string.IsNullOrWhiteSpace(usernamePrefix.Text)
                    ? cardNumber
                    : usernamePrefix.Text.Trim() + (globalIndex + 1).ToString(CultureInfo.InvariantCulture);
                var samplePassword = string.IsNullOrWhiteSpace(passwordPrefix.Text)
                    ? "143" + (globalIndex % 10).ToString(CultureInfo.InvariantCulture)
                    : passwordPrefix.Text.Trim() + (globalIndex + 1).ToString(CultureInfo.InvariantCulture);
                var sampleCard = BuildDesignedPrintPreviewCard(
                    cardNumber, sampleUsername, samplePassword, hours.Text.Trim(), validity.Text.Trim(),
                    priceText, uploadText, networkName.Text.Trim(), packageName.Text.Trim(), contact.Text.Trim(), globalIndex);

                var cardViewbox = new Viewbox
                {
                    Width = cardWidthMm,
                    Height = cardHeightMm,
                    Stretch = Stretch.Uniform,
                    StretchDirection = StretchDirection.DownOnly,
                    ClipToBounds = true,
                    Child = sampleCard
                };
                var rowIndex = i / colCount;
                var columnIndexFromRight = i % colCount;
                var x = pageWidthUnits - pageMarginMm - cardWidthMm - columnIndexFromRight * (cardWidthMm + cardGapMm);
                var y = pageMarginMm + rowIndex * (cardHeightMm + cardGapMm);
                Canvas.SetLeft(cardViewbox, x);
                Canvas.SetTop(cardViewbox, y);
                cardViewbox.Cursor = Cursors.Hand;
                cardViewbox.ToolTip = "انقر لمعاينة البطاقة بحجم مكبّر";
                cardViewbox.MouseLeftButtonUp += (_, _) => ShowSampleCardDetailDialog(
                    cardNumber, sampleUsername, samplePassword, hours.Text.Trim(), validity.Text.Trim(),
                    priceText, uploadText, networkName.Text.Trim(), packageName.Text.Trim(), contact.Text.Trim(), globalIndex);
                pageCards.Children.Add(cardViewbox);
            }
            FitPrintPage();
        }

        RenderSampleCards();
        dialog.Loaded += (_, _) => FitPrintPage();
        dialog.ShowDialog();
    }
    private void ShowSampleCardDetailDialog(string number, string username, string password, string hours, string validity, string price, string upload, string network, string packageName, string contact, int cardIndex)
    {
        var dialog = CreateDialog("معاينة بطاقة مكبّرة", 940, 360);
        dialog.MinWidth = 720;
        dialog.MinHeight = 300;

        var root = new Grid { Margin = new Thickness(18) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var title = new TextBlock
        {
            Text = "معاينة مكبّرة — انقر إغلاق للعودة إلى ورقة A4",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("PrimaryBrush"),
            Margin = new Thickness(0, 0, 0, 14)
        };
        root.Children.Add(title);

        var cardSurface = new Border
        {
            Background = Brushes.White,
            BorderBrush = (Brush)FindResource("BorderBrushSoft"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new Viewbox
            {
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.UpOnly,
                Width = 820,
                Height = 195,
                Child = BuildDesignedPrintPreviewCard(number, username, password, hours, validity, price, upload, network, packageName, contact, cardIndex)
            }
        };
        Grid.SetRow(cardSurface, 1);
        root.Children.Add(cardSurface);

        var close = MakeButton("إغلاق", primary: false);
        close.MinWidth = 100;
        close.Margin = new Thickness(0, 14, 0, 0);
        close.HorizontalAlignment = HorizontalAlignment.Left;
        close.Click += (_, _) => dialog.Close();
        Grid.SetRow(close, 2);
        root.Children.Add(close);

        dialog.Content = root;
        dialog.ShowDialog();
    }
}
