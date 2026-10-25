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

// Responsibility: Reusable UI helpers and property value parsing.
public partial class TemplateDesignerView
{
    private void SetStatus(string message, bool isSuccess)
    {
        if (TemplateStatusTextBlock is null) return;
        TemplateStatusTextBlock.Text = message;
        TemplateStatusTextBlock.Foreground = (Brush)FindResource(isSuccess ? "SuccessBrush" : "DangerBrush");
    }
    private Window CreateDialog(string title, double width, double height)
    {
        var window = new Window
        {
            Title = title,
            Width = width,
            Height = height,
            MinWidth = Math.Min(width, 620),
            MinHeight = Math.Min(height, 460),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            FlowDirection = FlowDirection.RightToLeft,
            FontFamily = new FontFamily("Segoe UI"),
            Background = (Brush)FindResource("AppBgBrush"),
            Owner = Window.GetWindow(this)
        };
        return window;
    }
    private StackPanel BuildLabeledField(string caption, Control control)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = caption, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        panel.Children.Add(control);
        return panel;
    }
    private StackPanel BuildDialogActions(Window dialog, string confirmText, string cancelText)
    {
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        var confirm = MakeButton(confirmText, primary: true);
        confirm.MinWidth = 140;
        confirm.Click += (_, _) => dialog.DialogResult = true;
        var cancel = MakeButton(cancelText, primary: false);
        cancel.MinWidth = 100;
        cancel.Margin = new Thickness(8, 0, 0, 0);
        cancel.Click += (_, _) => dialog.DialogResult = false;
        actions.Children.Add(confirm);
        actions.Children.Add(cancel);
        return actions;
    }
    private Button MakeButton(string text, bool primary)
    {
        var button = new Button
        {
            Content = text,
            Style = (Style)FindResource(primary ? "PrimaryButton" : "SecondaryButton"),
            Height = 36,
            Padding = new Thickness(14, 0, 14, 0),
            MinWidth = 90
        };
        return button;
    }
    private TextBox AddTextField(Panel host, string label, string initialValue)
    {
        var panel = new StackPanel { Margin = new Thickness(5, 4, 5, 7) };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 10.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        var textBox = new TextBox { Text = initialValue, Height = 32, Padding = new Thickness(7, 4, 7, 4), FontSize = 11.5 };
        panel.Children.Add(textBox);
        host.Children.Add(panel);
        return textBox;
    }
    private static int ParseInt(string? text, int fallback, int min, int max)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            int.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value);
        if (value == 0 && string.IsNullOrWhiteSpace(text)) value = fallback;
        return Math.Clamp(value, min, max);
    }
    private static long ParseLong(string? text, long fallback)
    {
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) return value;
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.CurrentCulture, out value)) return value;
        return fallback;
    }
    private static double ParseDouble(string? text, double fallback, double min, double max)
    {
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
            !double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value))
            value = fallback;
        if (!double.IsFinite(value)) value = fallback;
        return Math.Clamp(value, min, max);
    }
    private static string? GetSelectedTag(ComboBox combo) => (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
    private static void SelectComboTag(ComboBox combo, string? tag)
    {
        for (var i = 0; i < combo.Items.Count; i++)
        {
            if (combo.Items[i] is ComboBoxItem item && string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = i;
                return;
            }
        }
    }
}
