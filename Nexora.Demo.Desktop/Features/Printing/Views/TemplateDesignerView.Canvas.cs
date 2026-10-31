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

// Responsibility: Canvas scaling, zoom, selection, and drag interactions.
public partial class TemplateDesignerView
{
    private void TemplateDesignerView_Loaded(object sender, RoutedEventArgs e)
    {
        FitCardToWorkspace();
    }
    private void DesignPreviewHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_isReady || !_isAutoFitZoom || !IsLoaded) return;
        FitCardToWorkspace();
    }
    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_isReady) return;
        if (!_isApplyingFitZoom) _isAutoFitZoom = false;
        _zoom = Math.Clamp(e.NewValue, 0.2, 8.0);
        ApplyCardDimensionTransform();
    }
    private void ZoomInButton_Click(object sender, RoutedEventArgs e) => ZoomSlider.Value = Math.Min(8.0, ZoomSlider.Value + 0.2);
    private void ZoomOutButton_Click(object sender, RoutedEventArgs e) => ZoomSlider.Value = Math.Max(0.2, ZoomSlider.Value - 0.2);
    private void ZoomFitButton_Click(object sender, RoutedEventArgs e) => FitCardToWorkspace();
    private void FitCardToWorkspace()
    {
        if (DesignPreviewHost is null || ZoomSlider is null || DesignPreviewHost.ActualWidth < 40 || DesignPreviewHost.ActualHeight < 40)
            return;

        _isApplyingFitZoom = true;
        try
        {
            ApplyCardDimensionTransform();
            var availableWidth = Math.Max(40, DesignPreviewHost.ActualWidth - 8);
            var availableHeight = Math.Max(40, DesignPreviewHost.ActualHeight - 8);
            var baseWidth = Math.Max(1, DesignViewportCanvas.Width);
            var baseHeight = Math.Max(1, DesignViewportCanvas.Height);
            var fittedZoom = Math.Clamp(Math.Min(availableWidth / baseWidth, availableHeight / baseHeight), 0.2, 8.0);
            _zoom = fittedZoom;
            ZoomSlider.Value = fittedZoom;
            _isAutoFitZoom = true;
            ApplyCardDimensionTransform();
        }
        finally
        {
            _isApplyingFitZoom = false;
        }
    }
    private void CardDesignCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isReady) return;
        if (IsResizeHandleSource(e.OriginalSource as DependencyObject)) return;
        var element = FindEditableCanvasElement(e.OriginalSource as DependencyObject);
        var tag = element is null ? null : GetTagByElement(element);
        if (element is null || tag is null)
        {
            _draggingElement = null;
            _studioSelectedFieldTag = null;
            SelectedElementComboBox.SelectedIndex = -1;
            SyncFieldToggleButtons();
            SyncSelectedElementEditor();
            return;
        }

        SelectComboTag(SelectedElementComboBox, tag);
        _studioSelectedFieldTag = tag;
        SyncFieldToggleButtons();
        SyncSelectedElementEditor();
        _draggingElement = element;
        _dragStartPoint = e.GetPosition(CardDesignCanvas);
        _dragStartLeft = Canvas.GetLeft(element);
        _dragStartTop = Canvas.GetTop(element);
        if (!double.IsFinite(_dragStartLeft)) _dragStartLeft = 0;
        if (!double.IsFinite(_dragStartTop)) _dragStartTop = 0;
        CardDesignCanvas.CaptureMouse();
        e.Handled = true;
    }
    private void CardDesignCanvas_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isResizingElement || _draggingElement is null || e.LeftButton != MouseButtonState.Pressed) return;
        var current = e.GetPosition(CardDesignCanvas);
        var desiredX = _dragStartLeft + current.X - _dragStartPoint.X;
        var desiredY = _dragStartTop + current.Y - _dragStartPoint.Y;
        var position = ClampElementPosition(_draggingElement, desiredX, desiredY);
        Canvas.SetLeft(_draggingElement, position.X);
        Canvas.SetTop(_draggingElement, position.Y);
        SyncSelectedElementEditor();
        UpdateSelectionFrame();
    }
    private void CardDesignCanvas_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggingElement is null) return;
        _draggingElement = null;
        CardDesignCanvas.ReleaseMouseCapture();
        SyncSelectedElementEditor();
        UpdateSelectionFrame();
        RefreshStudioPreviews();
    }
    private Point ClampElementPosition(FrameworkElement element, double desiredX, double desiredY)
    {
        var width = Math.Min(GetElementWidth(element), Math.Max(1, CardDesignCanvas.Width));
        var height = Math.Min(GetElementHeight(element), Math.Max(1, CardDesignCanvas.Height));
        var radians = GetElementRotation(element) * Math.PI / 180d;
        var rotatedWidth = Math.Abs(width * Math.Cos(radians)) + Math.Abs(height * Math.Sin(radians));
        var rotatedHeight = Math.Abs(width * Math.Sin(radians)) + Math.Abs(height * Math.Cos(radians));
        var minX = Math.Max(0, (rotatedWidth - width) / 2d);
        var minY = Math.Max(0, (rotatedHeight - height) / 2d);
        var maxX = Math.Max(minX, CardDesignCanvas.Width - (rotatedWidth + width) / 2d);
        var maxY = Math.Max(minY, CardDesignCanvas.Height - (rotatedHeight + height) / 2d);
        return new Point(Math.Clamp(desiredX, minX, maxX), Math.Clamp(desiredY, minY, maxY));
    }
    private bool IsResizeHandleSource(DependencyObject? source)
    {
        var current = source;
        while (current is not null && !ReferenceEquals(current, CardDesignCanvas))
        {
            if (current is Thumb thumb && _resizeHandles.Contains(thumb)) return true;
            current = current is Visual || current is System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return false;
    }

    private FrameworkElement? FindEditableCanvasElement(DependencyObject? source)
    {
        if (source is null) return null;
        DependencyObject? current = source;
        FrameworkElement? directChild = null;
        while (current is not null && !ReferenceEquals(current, CardDesignCanvas))
        {
            DependencyObject? parent;
            if (current is Visual || current is System.Windows.Media.Media3D.Visual3D)
                parent = VisualTreeHelper.GetParent(current);
            else
                parent = LogicalTreeHelper.GetParent(current);
            if (current is FrameworkElement element && ReferenceEquals(parent, CardDesignCanvas))
                directChild = element;
            current = parent;
        }
        if (directChild is null || directChild == SelectionFrameBorder || directChild == CardSurfaceBorder || directChild == TemplateBackgroundImage)
            return null;
        var tag = GetTagByElement(directChild);
        return tag is not null && IsDynamicElementTag(tag) ? directChild : null;
    }
    private string? GetTagByElement(FrameworkElement element)
    {
        foreach (var tag in GetAllEditableElementTags())
            if (ReferenceEquals(GetElementByTag(tag), element)) return tag;
        return null;
    }
    private void UpdateSelectionFrame()
    {
        UpdateElementBorderOverlays();
        var selected = GetSelectedElement();
        if (selected is null || SelectionFrameBorder is null)
        {
            if (SelectionFrameBorder is not null) SelectionFrameBorder.Visibility = Visibility.Collapsed;
            RemoveResizeHandles();
            return;
        }

        var left = Canvas.GetLeft(selected);
        var top = Canvas.GetTop(selected);
        if (!double.IsFinite(left)) left = 0;
        if (!double.IsFinite(top)) top = 0;
        Canvas.SetLeft(SelectionFrameBorder, left - 2);
        Canvas.SetTop(SelectionFrameBorder, top - 2);
        SelectionFrameBorder.Width = Math.Max(6, GetElementWidth(selected) + 4);
        SelectionFrameBorder.Height = Math.Max(6, GetElementHeight(selected) + 4);
        SelectionFrameBorder.RenderTransformOrigin = new Point(0.5, 0.5);
        SelectionFrameBorder.RenderTransform = new RotateTransform(GetElementRotation(selected));
        SelectionFrameBorder.Visibility = Visibility.Visible;
        if (!_isResizingElement) UpdateResizeHandles(selected);
    }

    private void UpdateResizeHandles(FrameworkElement selected)
    {
        RemoveResizeHandles();
        if (selected.Visibility != Visibility.Visible || !CardDesignCanvas.IsLoaded) return;

        var left = Canvas.GetLeft(selected);
        var top = Canvas.GetTop(selected);
        if (!double.IsFinite(left)) left = 0;
        if (!double.IsFinite(top)) top = 0;
        var width = GetElementWidth(selected);
        var height = GetElementHeight(selected);
        const double handleSize = 12d;

        var handles = new (string Name, double X, double Y, Cursor Cursor)[]
        {
            ("nw", left, top, Cursors.SizeNWSE),
            ("n", left + width / 2d, top, Cursors.SizeNS),
            ("ne", left + width, top, Cursors.SizeNESW),
            ("e", left + width, top + height / 2d, Cursors.SizeWE),
            ("se", left + width, top + height, Cursors.SizeNWSE),
            ("s", left + width / 2d, top + height, Cursors.SizeNS),
            ("sw", left, top + height, Cursors.SizeNESW),
            ("w", left, top + height / 2d, Cursors.SizeWE)
        };

        var angleRadians = GetElementRotation(selected) * Math.PI / 180d;
        var centerX = left + width / 2d;
        var centerY = top + height / 2d;
        foreach (var item in handles)
        {
            var localX = item.X - centerX;
            var localY = item.Y - centerY;
            var handleX = centerX + localX * Math.Cos(angleRadians) - localY * Math.Sin(angleRadians);
            var handleY = centerY + localX * Math.Sin(angleRadians) + localY * Math.Cos(angleRadians);
            // The resize targets stay hit-testable, but are not drawn as visible
            // corner handles. The selection outline is the only visible overlay.
            var handleVisual = new FrameworkElementFactory(typeof(Border));
            handleVisual.SetValue(Border.BackgroundProperty, Brushes.White);
            handleVisual.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(0xC5, 0x1D, 0x2E)));
            handleVisual.SetValue(Border.BorderThicknessProperty, new Thickness(1.2));
            handleVisual.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            var handleTemplate = new ControlTemplate(typeof(Thumb)) { VisualTree = handleVisual };
            var thumb = new Thumb
            {
                Width = handleSize,
                Height = handleSize,
                Tag = item.Name,
                Cursor = item.Cursor,
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xC5, 0x1D, 0x2E)),
                BorderThickness = new Thickness(1),
                Template = handleTemplate,
                Focusable = false,
                ToolTip = "اسحب لتغيير حجم الحقل",
                SnapsToDevicePixels = true
            };
            thumb.DragStarted += ResizeHandle_DragStarted;
            thumb.DragDelta += ResizeHandle_DragDelta;
            thumb.DragCompleted += ResizeHandle_DragCompleted;
            Canvas.SetLeft(thumb, handleX - handleSize / 2d);
            Canvas.SetTop(thumb, handleY - handleSize / 2d);
            Panel.SetZIndex(thumb, 10001);
            CardDesignCanvas.Children.Add(thumb);
            _resizeHandles.Add(thumb);
        }
    }

    private void MoveResizeHandlesToCurrentBounds(FrameworkElement selected)
    {
        if (_resizeHandles.Count == 0) return;
        var left = Canvas.GetLeft(selected);
        var top = Canvas.GetTop(selected);
        if (!double.IsFinite(left)) left = 0;
        if (!double.IsFinite(top)) top = 0;
        var width = GetElementWidth(selected);
        var height = GetElementHeight(selected);
        const double handleSize = 12d;
        var centerX = left + width / 2d;
        var centerY = top + height / 2d;
        var radians = GetElementRotation(selected) * Math.PI / 180d;
        foreach (var thumb in _resizeHandles)
        {
            var name = thumb.Tag?.ToString() ?? string.Empty;
            var point = name switch
            {
                "nw" => new Point(left, top),
                "n" => new Point(centerX, top),
                "ne" => new Point(left + width, top),
                "e" => new Point(left + width, centerY),
                "se" => new Point(left + width, top + height),
                "s" => new Point(centerX, top + height),
                "sw" => new Point(left, top + height),
                "w" => new Point(left, centerY),
                _ => new Point(centerX, centerY)
            };
            var dx = point.X - centerX;
            var dy = point.Y - centerY;
            var x = centerX + dx * Math.Cos(radians) - dy * Math.Sin(radians);
            var y = centerY + dx * Math.Sin(radians) + dy * Math.Cos(radians);
            Canvas.SetLeft(thumb, x - handleSize / 2d);
            Canvas.SetTop(thumb, y - handleSize / 2d);
        }
    }

    private void RemoveResizeHandles()
    {
        foreach (var handle in _resizeHandles)
            CardDesignCanvas?.Children.Remove(handle);
        _resizeHandles.Clear();
    }

    private void ResizeHandle_DragStarted(object sender, DragStartedEventArgs e)
    {
        _resizeTarget = GetSelectedElement();
        _activeResizeHandle = (sender as Thumb)?.Tag?.ToString();
        _isResizingElement = _resizeTarget is not null && !string.IsNullOrWhiteSpace(_activeResizeHandle);
        if (_resizeTarget is not null)
        {
            _resizeStartWidth = GetElementWidth(_resizeTarget);
            _resizeStartHeight = GetElementHeight(_resizeTarget);
            _resizeStartFontSize = _resizeTarget is TextBlock text ? text.FontSize : 11d;
        }
    }

    private void ResizeHandle_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (!_isResizingElement || _resizeTarget is null || _activeResizeHandle is null) return;
        var element = _resizeTarget;
        var left = Canvas.GetLeft(element);
        var top = Canvas.GetTop(element);
        if (!double.IsFinite(left)) left = 0;
        if (!double.IsFinite(top)) top = 0;
        var width = GetElementWidth(element);
        var height = GetElementHeight(element);
        var right = left + width;
        var bottom = top + height;
        var inverseAngle = -GetElementRotation(element) * Math.PI / 180d;
        var deltaX = e.HorizontalChange * Math.Cos(inverseAngle) - e.VerticalChange * Math.Sin(inverseAngle);
        var deltaY = e.HorizontalChange * Math.Sin(inverseAngle) + e.VerticalChange * Math.Cos(inverseAngle);
        const double minSize = 7d;

        if (_activeResizeHandle.Contains('w')) left = Math.Min(right - minSize, left + deltaX);
        if (_activeResizeHandle.Contains('e')) right = Math.Max(left + minSize, right + deltaX);
        if (_activeResizeHandle.Contains('n')) top = Math.Min(bottom - minSize, top + deltaY);
        if (_activeResizeHandle.Contains('s')) bottom = Math.Max(top + minSize, bottom + deltaY);

        var maxWidth = Math.Max(minSize, CardDesignCanvas.Width);
        var maxHeight = Math.Max(minSize, CardDesignCanvas.Height);
        left = Math.Clamp(left, 0, maxWidth - minSize);
        top = Math.Clamp(top, 0, maxHeight - minSize);
        right = Math.Clamp(right, left + minSize, maxWidth);
        bottom = Math.Clamp(bottom, top + minSize, maxHeight);

        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        element.Width = right - left;
        element.Height = bottom - top;
        if (element is TextBlock textBlock && _activeResizeHandle.Length == 2 && _resizeStartWidth > 0 && _resizeStartHeight > 0)
        {
            var scaleX = element.Width / _resizeStartWidth;
            var scaleY = element.Height / _resizeStartHeight;
            var uniformScale = Math.Clamp(Math.Min(scaleX, scaleY), 0.55d, 3.5d);
            textBlock.FontSize = Math.Clamp(_resizeStartFontSize * uniformScale, 6d, 60d);
        }
        var selectionLeft = Canvas.GetLeft(element);
        var selectionTop = Canvas.GetTop(element);
        Canvas.SetLeft(SelectionFrameBorder, (double.IsFinite(selectionLeft) ? selectionLeft : 0) - 2);
        Canvas.SetTop(SelectionFrameBorder, (double.IsFinite(selectionTop) ? selectionTop : 0) - 2);
        SelectionFrameBorder.Width = element.Width + 4;
        SelectionFrameBorder.Height = element.Height + 4;
        SelectionFrameBorder.RenderTransform = new RotateTransform(GetElementRotation(element));
        MoveResizeHandlesToCurrentBounds(element);
        SyncSelectedElementEditor();
    }

    private void ResizeHandle_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _isResizingElement = false;
        _resizeTarget = null;
        _activeResizeHandle = null;
        _resizeStartWidth = 0;
        _resizeStartHeight = 0;
        _resizeStartFontSize = 0;
        UpdateSelectionFrame();
        SyncSelectedElementEditor();
        RefreshStudioPreviews();
    }
    private static double GetElementWidth(FrameworkElement element) => double.IsFinite(element.Width) && element.Width > 0 ? element.Width : Math.Max(6, element.ActualWidth);
    private static double GetElementHeight(FrameworkElement element) => double.IsFinite(element.Height) && element.Height > 0 ? element.Height : Math.Max(6, element.ActualHeight);
    private static double GetElementRotation(FrameworkElement element) => element.RenderTransform is RotateTransform rotation ? rotation.Angle : 0;
    private void ApplyCardDimensionTransform()
    {
        if (CardDesignCanvas is null || DesignViewportCanvas is null || CardDesignViewbox is null) return;

        var widthMm = ParseDouble(CardWidthTextBox.Text, 92.1, 10, 200);
        var heightMm = ParseDouble(CardHeightTextBox.Text, 52, 10, 200);
        var angle = ParseDouble(CardRotationTextBox.Text, 0, -180, 180);
        var canvasWidth = Math.Max(1, DesignBaseWidth * widthMm / BaseWidthMm);
        var canvasHeight = Math.Max(1, DesignBaseHeight * heightMm / BaseHeightMm);

        // When the physical card changes, remap X/Y positions only. Do not scale each
        // control, border, or font: the user can resize every element independently.
        var oldWidth = Math.Max(1, _lastCanvasWidth);
        var oldHeight = Math.Max(1, _lastCanvasHeight);
        if (Math.Abs(canvasWidth - oldWidth) > 0.1 || Math.Abs(canvasHeight - oldHeight) > 0.1)
        {
            var positionScaleX = canvasWidth / oldWidth;
            var positionScaleY = canvasHeight / oldHeight;
            foreach (var tag in GetAllEditableElementTags())
            {
                if (tag is "header" or "divider") continue;
                var element = GetElementByTag(tag);
                if (element is null) continue;
                var left = Canvas.GetLeft(element);
                var top = Canvas.GetTop(element);
                if (!double.IsFinite(left)) left = 0;
                if (!double.IsFinite(top)) top = 0;
                Canvas.SetLeft(element, left * positionScaleX);
                Canvas.SetTop(element, top * positionScaleY);
            }
        }

        CardDesignCanvas.Width = canvasWidth;
        CardDesignCanvas.Height = canvasHeight;
        CardSurfaceBorder.Width = canvasWidth;
        CardSurfaceBorder.Height = canvasHeight;
        TemplateBackgroundImage.Width = canvasWidth;
        TemplateBackgroundImage.Height = canvasHeight;
        // The default card is a long horizontal voucher: a narrow brand panel on the left,
        // with the credentials and package facts arranged in readable rows on the right.
        CardHeaderBorder.Width = Math.Min(canvasWidth, canvasWidth * 86d / LegacyDesignBaseWidth);
        CardHeaderBorder.Height = canvasHeight;
        Canvas.SetLeft(CardHeaderBorder, 0);
        Canvas.SetTop(CardHeaderBorder, 0);
        CardDividerBorder.Width = Math.Max(1, canvasWidth * 310d / LegacyDesignBaseWidth);
        Canvas.SetLeft(CardDividerBorder, canvasWidth * 94d / LegacyDesignBaseWidth);
        var legacyLogicalHeight = LegacyDesignBaseHeight * ParseDouble(CardHeightTextBox.Text, 52, 10, 200) / BaseHeightMm;
        Canvas.SetTop(CardDividerBorder, Math.Min(canvasHeight - 2d, canvasHeight * 34d / legacyLogicalHeight));

        var radians = angle * Math.PI / 180d;
        var boundsWidth = Math.Abs(canvasWidth * Math.Cos(radians)) + Math.Abs(canvasHeight * Math.Sin(radians));
        var boundsHeight = Math.Abs(canvasWidth * Math.Sin(radians)) + Math.Abs(canvasHeight * Math.Cos(radians));
        // Keep the preview viewport at the exact physical bounds. The old +4 canvas-unit
        // safety padding made a 46 × 15.8 mm card shrink inside its measured print size.
        boundsWidth = Math.Max(1, boundsWidth);
        boundsHeight = Math.Max(1, boundsHeight);

        // Only rotate the canvas. There is intentionally no ScaleTransform on content.
        CardDesignCanvas.RenderTransformOrigin = new Point(0.5, 0.5);
        CardDesignCanvas.RenderTransform = new RotateTransform(angle);
        DesignViewportCanvas.Width = boundsWidth;
        DesignViewportCanvas.Height = boundsHeight;
        Canvas.SetLeft(CardDesignCanvas, (boundsWidth - canvasWidth) / 2d);
        Canvas.SetTop(CardDesignCanvas, (boundsHeight - canvasHeight) / 2d);

        // Keep editable items inside the resized card without changing their own size.
        foreach (var tag in GetAllEditableElementTags())
        {
            var element = GetElementByTag(tag);
            if (element is null) continue;
            var left = Canvas.GetLeft(element);
            var top = Canvas.GetTop(element);
            if (!double.IsFinite(left)) left = 0;
            if (!double.IsFinite(top)) top = 0;
            var clamped = ClampElementPosition(element, left, top);
            Canvas.SetLeft(element, clamped.X);
            Canvas.SetTop(element, clamped.Y);
        }

        _lastCanvasWidth = canvasWidth;
        _lastCanvasHeight = canvasHeight;
        CardDesignViewbox.Width = Math.Max(1, boundsWidth * _zoom);
        CardDesignViewbox.Height = Math.Max(1, boundsHeight * _zoom);
        CardDesignCanvas.ToolTip = $"مقاس القالب: {widthMm:0.#} × {heightMm:0.#} مم؛ الدوران: {angle:0.#}°";
        CanvasSizeTextBlock.Text = $"{widthMm:0.#} × {heightMm:0.#} مم · دوران {angle:0.#}°";
        ZoomPercentageTextBlock.Text = $"{_zoom * 100:0}%";
        UpdateSelectionFrame();
    }
}
