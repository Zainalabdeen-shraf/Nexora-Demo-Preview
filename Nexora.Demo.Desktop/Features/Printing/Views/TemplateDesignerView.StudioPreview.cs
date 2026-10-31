using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Nexora.Demo.Desktop.Features.Printing.Views;

// Responsibility: Live studio print-sheet preview and responsive layout calculation.
public partial class TemplateDesignerView
{
    private const double StudioPageWidthMm = 210d;
    private const double StudioPageHeightMm = 297d;
    private const double StudioCardGapMm = 1d;

    private double PageMarginTopMm => ParseDouble(PrintMarginTopTextBox?.Text, 10, 0, 40);
    private double PageMarginBottomMm => ParseDouble(PrintMarginBottomTextBox?.Text, 10, 0, 40);
    private double PageMarginLeftMm => ParseDouble(PrintMarginLeftTextBox?.Text, 10, 0, 40);
    private double PageMarginRightMm => ParseDouble(PrintMarginRightTextBox?.Text, 10, 0, 40);
    private int _studioPreviewPageNumber = 1;
    private bool _isStudioPreviewRendering;

    private sealed record StudioPrintLayout(
        int TotalCards,
        int Columns,
        int Rows,
        int CardsPerPage,
        int TotalPages,
        int PageNumber,
        int CardsOnPage,
        double CardWidthMm,
        double CardHeightMm);

    private void StudioWorkspaceTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, StudioWorkspaceTabs) || !_isReady) return;
        if (StudioWorkspaceTabs.SelectedIndex == 1)
            RefreshStudioPreviews();
        else if (_isAutoFitZoom)
            FitCardToWorkspace();
    }

    private void StudioPresetButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string preset) return;

        var wasRenderingPreview = _isStudioPreviewRendering;
        _isStudioPreviewRendering = true;
        try
        {
            SelectComboTag(TemplateStyleComboBox, preset);
            // Set the theme too so clicking the already-selected preset is effective.
            SelectComboTag(ColorThemeComboBox, preset switch
            {
                "light" => "white",
                "dark" => "dark",
                _ => "blue"
            });
        }
        finally
        {
            _isStudioPreviewRendering = wasRenderingPreview;
        }
        UpdatePreview();
        var label = preset switch
        {
            "light" => "الأبيض والأحمر",
            "dark" => "الأحمر الداكن",
            _ => "الأزرق WiFi"
        };
        SetStatus($"تم تطبيق نمط «{label}» على الكرت. احفظ القالب للاحتفاظ به.", isSuccess: true);
    }

    private void ApplyCardDimensionsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isReady) return;
        var width = ParseDouble(CardWidthTextBox.Text, 92.1, 10, 200);
        var height = ParseDouble(CardHeightTextBox.Text, 52, 10, 200);
        var rotation = ParseDouble(CardRotationTextBox.Text, 0, -180, 180);
        CardWidthTextBox.Text = width.ToString("0.#", CultureInfo.InvariantCulture);
        CardHeightTextBox.Text = height.ToString("0.#", CultureInfo.InvariantCulture);
        CardRotationTextBox.Text = rotation.ToString("0.#", CultureInfo.InvariantCulture);
        ApplyCardDimensionTransform();
        if (_isAutoFitZoom) FitCardToWorkspace();
        RefreshStudioPreviews();
        SetStatus($"تم تطبيق مقاس الكرت {width:0.#} × {height:0.#} مم والدوران {rotation:0.#}°.", isSuccess: true);
    }

    private void ToggleCardOrientationButton_Click(object sender, RoutedEventArgs e)
    {
        var width = CardWidthTextBox.Text;
        CardWidthTextBox.Text = CardHeightTextBox.Text;
        CardHeightTextBox.Text = width;
        ApplyCardDimensionsButton_Click(sender, e);
    }

    private void PrintLayoutSetting_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isReady || _isStudioPreviewRendering) return;
        RefreshStudioPreviews();
    }

    private void PrintLayoutSetting_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_isReady) return;
        var layout = CalculateStudioPrintLayout();
        var wasRenderingPreview = _isStudioPreviewRendering;
        _isStudioPreviewRendering = true;
        try
        {
            PrintTotalCardsTextBox.Text = layout.TotalCards.ToString(CultureInfo.InvariantCulture);
            PrintColumnsTextBox.Text = layout.Columns.ToString(CultureInfo.InvariantCulture);
            PrintRowsTextBox.Text = layout.Rows.ToString(CultureInfo.InvariantCulture);
        }
        finally
        {
            _isStudioPreviewRendering = wasRenderingPreview;
        }
        RefreshStudioPreviews();
    }

    private void ResetPrintLayoutButton_Click(object sender, RoutedEventArgs e)
    {
        var wasRenderingPreview = _isStudioPreviewRendering;
        _isStudioPreviewRendering = true;
        try
        {
            PrintColumnsTextBox.Text = "2";
            PrintRowsTextBox.Text = "5";
            _studioPreviewPageNumber = 1;
        }
        finally
        {
            _isStudioPreviewRendering = wasRenderingPreview;
        }
        RefreshStudioPreviews();
        SetStatus("تمت إعادة تخطيط A4 إلى عمودين × خمسة صفوف وفق مقاس 92.1 × 52 مم وهوامش 10 مم.", isSuccess: true);
    }

    private void PrintPagePreviousButton_Click(object sender, RoutedEventArgs e)
    {
        _studioPreviewPageNumber = Math.Max(1, _studioPreviewPageNumber - 1);
        RefreshStudioPreviews();
    }

    private void PrintPageNextButton_Click(object sender, RoutedEventArgs e)
    {
        var layout = CalculateStudioPrintLayout();
        _studioPreviewPageNumber = Math.Min(layout.TotalPages, _studioPreviewPageNumber + 1);
        RefreshStudioPreviews();
    }

    private StudioPrintLayout CalculateStudioPrintLayout()
    {
        var totalCards = ParseInt(PrintTotalCardsTextBox?.Text, 153, 1, 10000);
        var requestedColumns = ParseInt(PrintColumnsTextBox?.Text, 2, 1, 8);
        var requestedRows = ParseInt(PrintRowsTextBox?.Text, 5, 1, 100);
        var cardWidth = ParseDouble(CardWidthTextBox?.Text, 92.1, 10, 200);
        var cardHeight = ParseDouble(CardHeightTextBox?.Text, 52, 10, 200);

        var usableWidth = StudioPageWidthMm - PageMarginLeftMm - PageMarginRightMm;
        var usableHeight = StudioPageHeightMm - PageMarginTopMm - PageMarginBottomMm;
        var maxColumns = Math.Max(1, (int)Math.Floor((usableWidth + StudioCardGapMm) / (cardWidth + StudioCardGapMm)));
        var maxRows = Math.Max(1, (int)Math.Floor((usableHeight + StudioCardGapMm) / (cardHeight + StudioCardGapMm)));
        var columns = Math.Max(1, Math.Min(requestedColumns, maxColumns));
        var rows = Math.Max(1, Math.Min(requestedRows, maxRows));
        var cardsPerPage = Math.Max(1, columns * rows);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalCards / (double)cardsPerPage));
        _studioPreviewPageNumber = Math.Clamp(_studioPreviewPageNumber, 1, totalPages);
        var firstCardIndex = (_studioPreviewPageNumber - 1) * cardsPerPage;
        var cardsOnPage = Math.Max(0, Math.Min(cardsPerPage, totalCards - firstCardIndex));

        return new StudioPrintLayout(
            totalCards, columns, rows, cardsPerPage, totalPages,
            _studioPreviewPageNumber, cardsOnPage, cardWidth, cardHeight);
    }

    private void PrintSheetPreviewHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_isReady || _isStudioPreviewRendering || StudioWorkspaceTabs.SelectedIndex != 1) return;
        RefreshStudioPreviews();
    }

    private void RefreshStudioPreviews(bool forceBuildSheet = false)
    {
        if (!_isReady || PrintSheetPreviewHost is null) return;
        if (_isStudioPreviewRendering) return;

        _isStudioPreviewRendering = true;
        try
        {
            var layout = CalculateStudioPrintLayout();
            PrintCurrentPageTextBlock.Text = $"{layout.PageNumber} / {layout.TotalPages}";
            var layoutSummary =
                $"{layout.Columns} أعمدة × {layout.Rows} صفوف = {layout.CardsPerPage} كرت/صفحة" +
                (layout.Columns < ParseInt(PrintColumnsTextBox.Text, 3, 1, 8) ||
                 layout.Rows < ParseInt(PrintRowsTextBox.Text, 17, 1, 100)
                    ? " · تم التقييد وفق مقاس الكرت وورقة A4"
                    : string.Empty);
            PrintLayoutSummaryTextBlock.Text = layoutSummary;
            if (PrintLayoutSummaryHeaderTextBlock is not null) PrintLayoutSummaryHeaderTextBlock.Text = layoutSummary;

            // The page-preview tab is a hidden compatibility control. Build its
            // expensive sheet only when an internal legacy caller explicitly selects it.
            if (forceBuildSheet || (StudioWorkspaceTabs is not null && StudioWorkspaceTabs.SelectedIndex == 1))
                BuildStudioPrintSheet(layout);
        }
        catch (Exception ex)
        {
            SetStatus("تعذر تحديث معاينة الاستوديو: " + ex.Message, isSuccess: false);
        }
        finally
        {
            _isStudioPreviewRendering = false;
        }
    }

    private void BuildStudioPrintSheet(StudioPrintLayout layout)
    {
        PrintSheetPreviewHost.Children.Clear();
        PrintSheetPreviewHost.Children.Add(BuildStudioPrintSheetPage(layout));
    }

    // Build a self-contained A4 visual so PDF rendering does not depend on the
    // legacy off-screen preview host, which is intentionally collapsed in XAML.
    private Viewbox BuildStudioPrintSheetPage(StudioPrintLayout layout)
    {
        var pageCanvas = new Canvas
        {
            Width = StudioPageWidthMm,
            Height = StudioPageHeightMm,
            Background = Brushes.White,
            FlowDirection = FlowDirection.LeftToRight,
            ClipToBounds = true
        };
        var pageSurface = new Border
        {
            Width = StudioPageWidthMm,
            Height = StudioPageHeightMm,
            Background = Brushes.White,
            FlowDirection = FlowDirection.LeftToRight,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xD4, 0xDA, 0xE3)),
            BorderThickness = new Thickness(0.5),
            Child = pageCanvas,
            Effect = new DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Opacity = 0.10 }
        };
        var hostWidth = PrintSheetPreviewHost.ActualWidth > 50
            ? PrintSheetPreviewHost.ActualWidth - 12
            : StudioPageWidthMm;
        var hostHeight = PrintSheetPreviewHost.ActualHeight > 50
            ? PrintSheetPreviewHost.ActualHeight - 12
            : StudioPageHeightMm;
        var pageViewbox = new Viewbox
        {
            Width = Math.Max(1, hostWidth),
            Height = Math.Max(1, hostHeight),
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.Both,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = pageSurface
        };

        var totalGridWidth = layout.Columns * layout.CardWidthMm +
            Math.Max(0, layout.Columns - 1) * StudioCardGapMm;
        var usablePageWidth = StudioPageWidthMm - PageMarginLeftMm - PageMarginRightMm;
        var horizontalStart = PageMarginLeftMm + Math.Max(0, (usablePageWidth - totalGridWidth) / 2d);
        var package = GetActivePackage();
        var hours = package?.TimeLimit ?? "—";
        var validity = package?.Validity ?? "—";
        var price = package?.PrintPriceLabel ?? "—";
        var upload = package?.DataLimit ?? "—";
        var packageName = package is null ? "—" : GetPackageName(package);
        var network = PreviewNetworkTextBlock.Text ?? string.Empty;
        var contact = PreviewContactBlock.Text ?? string.Empty;
        const long firstNumber = 50723001;
        var firstCardIndex = (layout.PageNumber - 1) * layout.CardsPerPage;

        for (var i = 0; i < layout.CardsOnPage; i++)
        {
            var index = firstCardIndex + i;
            var number = (firstNumber + index).ToString(CultureInfo.InvariantCulture);
            var username = number;
            var password = "143" + (index % 10).ToString(CultureInfo.InvariantCulture);
            var card = BuildDesignedPrintPreviewCard(
                number, username, password, hours, validity, price, upload,
                network, packageName, contact, index);

            var cardViewbox = new Viewbox
            {
                Width = layout.CardWidthMm,
                Height = layout.CardHeightMm,
                Stretch = Stretch.Uniform,
                StretchDirection = StretchDirection.DownOnly,
                FlowDirection = FlowDirection.LeftToRight,
                ClipToBounds = true,
                Child = card
            };
            var row = i / layout.Columns;
            var columnFromRight = i % layout.Columns;
            var x = horizontalStart +
                (layout.Columns - 1 - columnFromRight) *
                (layout.CardWidthMm + StudioCardGapMm);
            var y = PageMarginTopMm +
                row * (layout.CardHeightMm + StudioCardGapMm);
            Canvas.SetLeft(cardViewbox, x);
            Canvas.SetTop(cardViewbox, y);
            pageCanvas.Children.Add(cardViewbox);
        }

        return pageViewbox;
    }

    private void BuildStudioSingleCardPreview(StudioPrintLayout layout)
    {
        SingleCardPreviewHost.Children.Clear();
        var package = GetActivePackage();
        var number = "50723001";
        var card = BuildDesignedPrintPreviewCard(
            number,
            number,
            "1431",
            package?.TimeLimit ?? "—",
            package?.Validity ?? "—",
            package?.PrintPriceLabel ?? "—",
            package?.DataLimit ?? "—",
            PreviewNetworkTextBlock.Text ?? string.Empty,
            package is null ? "—" : GetPackageName(package),
            PreviewContactBlock.Text ?? string.Empty,
            0);

        var cardViewbox = new Viewbox
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
            ClipToBounds = true,
            Child = card
        };
        SingleCardPreviewHost.Children.Add(cardViewbox);
    }
}
