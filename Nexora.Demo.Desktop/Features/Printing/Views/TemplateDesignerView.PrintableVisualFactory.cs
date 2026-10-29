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

// Responsibility: Visual cloning and construction of printable card elements.
public partial class TemplateDesignerView
{
    private Canvas BuildDesignedPrintCard(string number, string username, string password, string hours, string validity, string price, string upload, string network, string packageName, string contact, int cardIndex)
    {
        // Preserve the original print-path surface and coordinates. Page-preview layout
        // has its own builder below so preview-only scaling never alters actual printing.
        var viewport = new Canvas
        {
            Width = Math.Max(1, DesignViewportCanvas.Width),
            Height = Math.Max(1, DesignViewportCanvas.Height),
            Background = Brushes.Transparent,
            FlowDirection = FlowDirection.LeftToRight,
            ClipToBounds = true
        };
        var cardCanvas = new Canvas
        {
            Width = Math.Max(1, CardDesignCanvas.Width),
            Height = Math.Max(1, CardDesignCanvas.Height),
            Background = Brushes.Transparent,
            FlowDirection = FlowDirection.LeftToRight,
            RenderTransformOrigin = CardDesignCanvas.RenderTransformOrigin,
            RenderTransform = CardDesignCanvas.RenderTransform?.CloneCurrentValue(),
            ClipToBounds = true
        };
        Canvas.SetLeft(cardCanvas, double.IsFinite(Canvas.GetLeft(CardDesignCanvas)) ? Canvas.GetLeft(CardDesignCanvas) : 0);
        Canvas.SetTop(cardCanvas, double.IsFinite(Canvas.GetTop(CardDesignCanvas)) ? Canvas.GetTop(CardDesignCanvas) : 0);
        viewport.Children.Add(cardCanvas);

        foreach (UIElement child in CardDesignCanvas.Children)
        {
            if (ReferenceEquals(child, SelectionFrameBorder) || child is not FrameworkElement source) continue;
            var clone = CloneTemplateVisual(source, number, username, password, hours, validity, price, upload, network, packageName, contact, cardIndex);
            if (clone is null) continue;
            var left = Canvas.GetLeft(source);
            var top = Canvas.GetTop(source);
            Canvas.SetLeft(clone, double.IsFinite(left) ? left : 0);
            Canvas.SetTop(clone, double.IsFinite(top) ? top : 0);
            Panel.SetZIndex(clone, Panel.GetZIndex(source));
            cardCanvas.Children.Add(clone);
        }
        return viewport;
    }
    private Canvas BuildDesignedPrintPreviewCard(string number, string username, string password, string hours, string validity, string price, string upload, string network, string packageName, string contact, int cardIndex)
    {
        // This builder is intentionally preview-only. The editor surface is 420 × 245,
        // but the physical card defaults to 46 × 15.8 mm. Most visible template content
        // occupies only the upper part of the editor canvas; crop that unused workspace
        // and compact Y coordinates so each preview slot uses the full physical card area.
        var sourceWidth = Math.Max(1, CardDesignCanvas.Width);
        var sourceHeight = Math.Max(1, CardDesignCanvas.Height);
        var physicalWidth = ParseDouble(CardWidthTextBox.Text, 46, 10, 200);
        var physicalHeight = ParseDouble(CardHeightTextBox.Text, 15.8, 10, 200);

        // Some default fields deliberately extend slightly past the editor canvas's
        // right edge (for example the password/contact labels). Include their bounds so
        // they do not get clipped in the preview-only surface.
        var contentRight = (double)sourceWidth;
        var contentBottom = 1d;
        foreach (UIElement child in CardDesignCanvas.Children)
        {
            if (child is not FrameworkElement source ||
                ReferenceEquals(source, SelectionFrameBorder) ||
                source.Visibility != Visibility.Visible ||
                IsPreviewFullCanvasBackground(source, sourceHeight))
                continue;

            var left = Canvas.GetLeft(source);
            var top = Canvas.GetTop(source);
            var safeLeft = double.IsFinite(left) ? Math.Max(0, left) : 0;
            var safeTop = double.IsFinite(top) ? Math.Max(0, top) : 0;
            contentRight = Math.Max(contentRight, safeLeft + GetElementWidth(source));
            contentBottom = Math.Max(contentBottom, safeTop + GetElementHeight(source));
        }

        var previewWidth = Math.Max(sourceWidth, contentRight);
        var previewHeight = Math.Max(1, previewWidth * physicalHeight / physicalWidth);
        var verticalScale = Math.Min(1d, previewHeight / contentBottom);
        var previewCanvas = new Canvas
        {
            Width = previewWidth,
            Height = previewHeight,
            Background = Brushes.Transparent,
            FlowDirection = FlowDirection.LeftToRight,
            ClipToBounds = true
        };

        foreach (UIElement child in CardDesignCanvas.Children)
        {
            if (ReferenceEquals(child, SelectionFrameBorder) || child is not FrameworkElement source) continue;
            var clone = CloneTemplateVisual(source, number, username, password, hours, validity, price, upload, network, packageName, contact, cardIndex);
            if (clone is null) continue;

            var left = Canvas.GetLeft(source);
            var top = Canvas.GetTop(source);
            Canvas.SetLeft(clone, double.IsFinite(left) ? left : 0);
            Canvas.SetTop(clone, (double.IsFinite(top) ? top : 0) * verticalScale);
            ApplyPreviewVerticalScale(clone, verticalScale, previewWidth, previewHeight, sourceHeight);
            Panel.SetZIndex(clone, Panel.GetZIndex(source));
            previewCanvas.Children.Add(clone);
        }

        return previewCanvas;
    }

    public FrameworkElement CreateIssuancePreviewElement(
    string number,
    string username,
    string password,
    string hours,
    string validity,
    string price,
    string upload,
    string network,
    string packageName,
    string contact,
    int cardIndex = 0)
{
    UpdatePreview();

    var card = BuildDesignedPrintPreviewCard(
        number,
        username,
        password,
        hours,
        validity,
        price,
        upload,
        network,
        packageName,
        contact,
        cardIndex);

    return new Viewbox
    {
        Stretch = Stretch.Uniform,
        StretchDirection = StretchDirection.Both,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
        Child = card,
        Margin = new Thickness(2),
        ClipToBounds = true
    };
}

public Size GetCardLayoutSizeMillimeters() => new Size(
    ParseDouble(CardWidthTextBox.Text, 66, 10, 200),
    ParseDouble(CardHeightTextBox.Text, 15.5, 10, 200));

    
    private static bool IsPreviewFullCanvasBackground(FrameworkElement element, double sourceCanvasHeight)
    {
        var elementName = element.Name.Length > 0 ? element.Name : element.Tag?.ToString();
        return elementName is nameof(CardSurfaceBorder) or nameof(TemplateBackgroundImage) or nameof(CardHeaderBorder) ||
               (element.Height >= 0.9 * sourceCanvasHeight && Canvas.GetTop(element) <= 1d);
    }
    private static void ApplyPreviewVerticalScale(FrameworkElement element, double scale, double previewWidth, double previewHeight, double sourceCanvasHeight)
    {
        // Background surfaces must span the preview card; ordinary elements are fitted
        // to the occupied design area. Text is scaled by the same Y-layout factor as its
        // line box, while all X positions, widths, and the original print builder remain intact.
        if (IsPreviewFullCanvasBackground(element, sourceCanvasHeight))
        {
            element.Height = previewHeight;
            var elementName = element.Name.Length > 0 ? element.Name : element.Tag?.ToString();
            if (elementName is nameof(CardSurfaceBorder) or nameof(TemplateBackgroundImage))
                element.Width = previewWidth;
        }
        else if (double.IsFinite(element.Height) && element.Height > 0)
        {
            element.Height *= scale;
        }

        element.Margin = new Thickness(element.Margin.Left, element.Margin.Top * scale, element.Margin.Right, element.Margin.Bottom * scale);

        if (element is TextBlock text)
            text.FontSize = Math.Max(1, text.FontSize * scale);

        if (element is Grid grid)
        {
            foreach (var row in grid.RowDefinitions)
            {
                if (row.Height.IsAbsolute) row.Height = new GridLength(row.Height.Value * scale, GridUnitType.Pixel);
                if (double.IsFinite(row.MinHeight) && row.MinHeight > 0) row.MinHeight *= scale;
                if (double.IsFinite(row.MaxHeight) && row.MaxHeight > 0 && row.MaxHeight < double.MaxValue) row.MaxHeight *= scale;
            }
            foreach (UIElement child in grid.Children)
            {
                if (child is FrameworkElement nested)
                    ApplyPreviewVerticalScale(nested, scale, previewWidth, previewHeight, sourceCanvasHeight);
            }
        }
        else if (element is Border border && border.Child is FrameworkElement nestedChild)
        {
            ApplyPreviewVerticalScale(nestedChild, scale, previewWidth, previewHeight, sourceCanvasHeight);
        }
    }
    private FrameworkElement? CloneTemplateVisual(FrameworkElement source, string number, string username, string password, string hours, string validity, string price, string upload, string network, string packageName, string contact, int cardIndex)
    {
        FrameworkElement? clone = source switch
        {
            TextBlock text => new TextBlock
            {
                Text = GetPrintPreviewText(text, number, username, password, hours, validity, price, upload, network, packageName, contact, cardIndex),
                FontFamily = text.FontFamily,
                FontSize = text.FontSize,
                FontWeight = text.FontWeight,
                FontStyle = text.FontStyle,
                FontStretch = text.FontStretch,
                Foreground = text.Foreground,
                Background = text.Background,
                TextAlignment = text.TextAlignment,
                TextWrapping = text.TextWrapping,
                TextTrimming = text.TextTrimming,
                FlowDirection = text.FlowDirection,
                Padding = text.Padding,
                VerticalAlignment = text.VerticalAlignment,
                HorizontalAlignment = text.HorizontalAlignment
            },
            Border border => CloneBorder(border, number, username, password, hours, validity, price, upload, network, packageName, contact, cardIndex),
            Ellipse ellipse => new Ellipse { Fill = ellipse.Fill, Stroke = ellipse.Stroke, StrokeThickness = ellipse.StrokeThickness },
            Image image => new Image { Source = image.Source, Stretch = image.Stretch, StretchDirection = image.StretchDirection },
            Grid grid => CloneGrid(grid, number, username, password, hours, validity, price, upload, network, packageName, contact, cardIndex),
            _ => null
        };

        if (clone is null) return null;
        CopyCommonElementProperties(source, clone);
        clone.Tag = source.Name;
        return clone;
    }
    private Border CloneBorder(Border source, string number, string username, string password, string hours, string validity, string price, string upload, string network, string packageName, string contact, int cardIndex)
    {
        var clone = new Border
        {
            Background = source.Background,
            BorderBrush = source.BorderBrush,
            BorderThickness = source.BorderThickness,
            CornerRadius = source.CornerRadius,
            Padding = source.Padding
        };
        if (source.Child is FrameworkElement child)
            clone.Child = CloneTemplateVisual(child, number, username, password, hours, validity, price, upload, network, packageName, contact, cardIndex);
        return clone;
    }
    private Grid CloneGrid(Grid source, string number, string username, string password, string hours, string validity, string price, string upload, string network, string packageName, string contact, int cardIndex)
    {
        var clone = new Grid { Background = source.Background };
        foreach (var row in source.RowDefinitions)
            clone.RowDefinitions.Add(new RowDefinition { Height = row.Height, MinHeight = row.MinHeight, MaxHeight = row.MaxHeight });
        foreach (var column in source.ColumnDefinitions)
            clone.ColumnDefinitions.Add(new ColumnDefinition { Width = column.Width, MinWidth = column.MinWidth, MaxWidth = column.MaxWidth });
        foreach (UIElement child in source.Children)
        {
            if (child is not FrameworkElement childElement) continue;
            var childClone = CloneTemplateVisual(childElement, number, username, password, hours, validity, price, upload, network, packageName, contact, cardIndex);
            if (childClone is null) continue;
            Grid.SetRow(childClone, Grid.GetRow(childElement));
            Grid.SetColumn(childClone, Grid.GetColumn(childElement));
            Grid.SetRowSpan(childClone, Grid.GetRowSpan(childElement));
            Grid.SetColumnSpan(childClone, Grid.GetColumnSpan(childElement));
            clone.Children.Add(childClone);
        }
        return clone;
    }
    private static void CopyCommonElementProperties(FrameworkElement source, FrameworkElement target)
    {
        target.Width = source.Width;
        target.Height = source.Height;
        target.Margin = source.Margin;
        target.Opacity = source.Opacity;
        target.Visibility = source.Visibility;
        target.HorizontalAlignment = source.HorizontalAlignment;
        target.VerticalAlignment = source.VerticalAlignment;
        target.FlowDirection = source.FlowDirection;
        target.RenderTransformOrigin = source.RenderTransformOrigin;
        target.RenderTransform = source.RenderTransform?.CloneCurrentValue();
        target.ClipToBounds = source.ClipToBounds;
        target.IsHitTestVisible = false;
    }
    private string GetPrintPreviewText(TextBlock source, string number, string username, string password, string hours, string validity, string price, string upload, string network, string packageName, string contact, int cardIndex)
    {
        return source.Name switch
        {
            nameof(PreviewLoginCodeTextBlock) => number,
            nameof(PreviewUsernameTextBlock) => username,
            nameof(PreviewPasswordTextBlock) => string.IsNullOrWhiteSpace(password) ? string.Empty : "كلمة المرور: " + password,
            nameof(PreviewValidityTextBlock) => "الصلاحية: " + validity,
            "PreviewTimeTextBlock" => "الوقت: " + hours,
            nameof(PreviewPriceTextBlock) => string.IsNullOrWhiteSpace(price) || price.Trim() == "—"
                ? "السعر: —"
                : "السعر: " + price + (price.Contains("ريال", StringComparison.Ordinal) ? string.Empty : " ريال"),
            nameof(PreviewPackageTextBlock) => "الباقة: " + packageName,
            nameof(PreviewDownloadTextBlock) => "التحميل: " + upload,
            "PreviewContactTextBlock" => string.IsNullOrWhiteSpace(contact) ? source.Text : contact,
            nameof(PreviewPrintDateTextBlock) => "تاريخ الطباعة: " + DateTime.Now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture),
            nameof(PreviewSerialTextBlock) => "الرقم التسلسلي: " + number,
            nameof(PreviewBatchTextBlock) => "رقم الدفعة: B-" + (cardIndex + 1).ToString("D4", CultureInfo.InvariantCulture),
            nameof(PreviewNetworkTextBlock) => string.IsNullOrWhiteSpace(network) ? source.Text : network,
            _ => source.Text
        };
    }
    public Size GetPhysicalCardSizeMillimeters()
    {
        var width = ParseDouble(CardWidthTextBox.Text, 46, 10, 200);
        var height = ParseDouble(CardHeightTextBox.Text, 15.8, 10, 200);
        var radians = ParseDouble(CardRotationTextBox.Text, 0, -180, 180) * Math.PI / 180d;
        return new Size(
            Math.Abs(width * Math.Cos(radians)) + Math.Abs(height * Math.Sin(radians)),
            Math.Abs(width * Math.Sin(radians)) + Math.Abs(height * Math.Cos(radians)));
    }
    public FrameworkElement CreatePrintableCard(
        GeneratedDemoCard card,
        int cardIndex,
        bool showNumber = true,
        bool showUsername = true,
        bool showPassword = true,
        bool showPackage = true,
        bool showPrice = true)
    {
        ArgumentNullException.ThrowIfNull(card);
        var originalVisibility = new Dictionary<FrameworkElement, Visibility>
        {
            [PreviewLoginCodeTextBlock] = PreviewLoginCodeTextBlock.Visibility,
            [PreviewUsernameTextBlock] = PreviewUsernameTextBlock.Visibility,
            [PreviewPasswordTextBlock] = PreviewPasswordTextBlock.Visibility,
            [PreviewPackageTextBlock] = PreviewPackageTextBlock.Visibility,
            [PreviewPriceTextBlock] = PreviewPriceTextBlock.Visibility
        };

        try
        {
            if (!showNumber) PreviewLoginCodeTextBlock.Visibility = Visibility.Collapsed;
            if (!showUsername) PreviewUsernameTextBlock.Visibility = Visibility.Collapsed;
            if (!showPassword || string.IsNullOrWhiteSpace(card.Password) ||
                string.Equals(card.Password.Trim(), card.Username?.Trim(), StringComparison.Ordinal))
                PreviewPasswordTextBlock.Visibility = Visibility.Collapsed;
            if (!showPackage) PreviewPackageTextBlock.Visibility = Visibility.Collapsed;
            if (!showPrice) PreviewPriceTextBlock.Visibility = Visibility.Collapsed;

            var packageName = card.PackageName;
            var separator = packageName.LastIndexOf(" — ", StringComparison.Ordinal);
            if (separator > 0) packageName = packageName[..separator].Trim();
            var network = string.IsNullOrWhiteSpace(PreviewNetworkTextBlock.Text) ? "" : PreviewNetworkTextBlock.Text;
            var cardCanvas = BuildDesignedPrintCard(
                card.CardNumber,
                card.Username ?? string.Empty,
                card.Password,
                card.TimeLimit,
                card.PackageValidity,
                card.UnitPrice.ToString("0.##", CultureInfo.CurrentCulture),
                card.DataLimit,
                network,
                packageName,
                PreviewContactBlock.Text,
                cardIndex);
            return FlattenCardKeepingNumberSelectable(cardCanvas, card.CardNumber);
        }
        finally
        {
            foreach (var pair in originalVisibility) pair.Key.Visibility = pair.Value;
        }
    }
    private Canvas FlattenCardKeepingNumberSelectable(Canvas viewport, string cardNumberText)
    {
        // Keep exactly one credential value as live text. If the design hides the
        // redundant login-number layer, use the visible username field as the card
        // number's printable text layer, without duplicating a second number.
        // Everything else is rasterized so the issued PDF remains non-selectable.
        if (viewport.Children.OfType<Canvas>().FirstOrDefault() is not Canvas cardCanvas)
            return viewport;

        var loginLayer = cardCanvas.Children.OfType<FrameworkElement>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), nameof(PreviewLoginCodeTextBlock), StringComparison.Ordinal));
        var usernameLayer = cardCanvas.Children.OfType<FrameworkElement>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), nameof(PreviewUsernameTextBlock), StringComparison.Ordinal));
        var cardNumber = loginLayer?.Visibility == Visibility.Visible
            ? loginLayer
            : usernameLayer?.Visibility == Visibility.Visible ? usernameLayer : null;
        var isUsernameNumberLayer = ReferenceEquals(cardNumber, usernameLayer);
        var numberOriginalVisibility = cardNumber?.Visibility ?? Visibility.Collapsed;
        var numberWasVisible = numberOriginalVisibility == Visibility.Visible;
        if (cardNumber is not null) cardNumber.Visibility = Visibility.Collapsed;

        var width = Math.Max(1, viewport.Width);
        var height = Math.Max(1, viewport.Height);
        viewport.Measure(new Size(width, height));
        viewport.Arrange(new Rect(0, 0, width, height));
        viewport.UpdateLayout();
        const int renderScale = 5;
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(width * renderScale)),
            Math.Max(1, (int)Math.Ceiling(height * renderScale)),
            96d * renderScale, 96d * renderScale, PixelFormats.Pbgra32);
        bitmap.Render(viewport);
        bitmap.Freeze();

        var flattened = new Canvas { Width = width, Height = height, Background = Brushes.Transparent, FlowDirection = FlowDirection.LeftToRight, ClipToBounds = true };
        flattened.Children.Add(new Image
        {
            Source = bitmap,
            Width = width,
            Height = height,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false
        });

        if (cardNumber is not null)
        {
            cardCanvas.Children.Remove(cardNumber);
            if (numberWasVisible)
            {
                if (isUsernameNumberLayer && cardNumber is TextBlock usernameNumberText)
                    usernameNumberText.Text = cardNumberText ?? string.Empty;
                cardNumber.Visibility = numberOriginalVisibility;
                var numberLayer = new Canvas
                {
                    Width = cardCanvas.Width,
                    Height = cardCanvas.Height,
                    Background = Brushes.Transparent,
                    FlowDirection = FlowDirection.LeftToRight,
                    ClipToBounds = cardCanvas.ClipToBounds,
                    RenderTransformOrigin = cardCanvas.RenderTransformOrigin,
                    RenderTransform = cardCanvas.RenderTransform?.CloneCurrentValue(),
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(numberLayer, double.IsFinite(Canvas.GetLeft(cardCanvas)) ? Canvas.GetLeft(cardCanvas) : 0);
                Canvas.SetTop(numberLayer, double.IsFinite(Canvas.GetTop(cardCanvas)) ? Canvas.GetTop(cardCanvas) : 0);
                cardNumber.IsHitTestVisible = false;
                numberLayer.Children.Add(cardNumber);
                Panel.SetZIndex(numberLayer, 10);
                flattened.Children.Add(numberLayer);
            }
        }

        return flattened;
    }

}
