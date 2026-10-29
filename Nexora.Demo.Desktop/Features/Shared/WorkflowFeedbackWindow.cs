using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Nexora.Demo.Desktop.Features.Shared;

public enum WorkflowFeedbackKind
{
    Success,
    Info,
    Warning,
    Error
}

public sealed record WorkflowFeedbackAction(string Key, string Label, bool IsPrimary = false);

/// <summary>App-styled progress and result surface used by important card workflows.</summary>
public sealed class WorkflowFeedbackWindow : Window
{
    private static readonly Color Accent = Color.FromRgb(197, 27, 43);
    private static readonly Color Ink = Color.FromRgb(32, 36, 42);
    private static readonly Color Muted = Color.FromRgb(103, 112, 122);
    private readonly TextBlock? _stageText;
    private string? _selectedAction;

    private WorkflowFeedbackWindow(string title, string message, bool progress, WorkflowFeedbackKind kind)
    {
        Title = title;
        Width = progress ? 450 : 560;
        SizeToContent = SizeToContent.Height;
        MinWidth = progress ? 420 : 480;
        MaxWidth = 650;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        FlowDirection = FlowDirection.RightToLeft;
        FontFamily = new FontFamily("Tahoma");
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var outer = new Border
        {
            Background = Brushes.White,
            CornerRadius = new CornerRadius(15),
            BorderBrush = new SolidColorBrush(Color.FromRgb(224, 227, 232)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 24,
                ShadowDepth = 4,
                Opacity = 0.18
            }
        };
        var content = new StackPanel();
        var topRule = new Border { Height = 5, Background = new SolidColorBrush(progress ? Accent : ToneColor(kind)), CornerRadius = new CornerRadius(15, 15, 0, 0) };
        content.Children.Add(topRule);

        var header = new Grid { Margin = new Thickness(22, 18, 22, 6), Cursor = Cursors.SizeAll };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 17,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Ink),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        header.Children.Add(titleText);
        var tone = progress ? Accent : ToneColor(kind);
        var statusGlyph = progress ? "…" : kind switch
        {
            WorkflowFeedbackKind.Success => "✓",
            WorkflowFeedbackKind.Warning => "!",
            WorkflowFeedbackKind.Error => "×",
            _ => "i"
        };
        var statusIcon = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(Color.FromArgb(24, tone.R, tone.G, tone.B)),
            Margin = new Thickness(12, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = statusGlyph,
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(tone),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            }
        };
        Grid.SetColumn(statusIcon, 1);
        header.Children.Add(statusIcon);
        var close = new Button
        {
            Content = "×",
            Width = 30,
            Height = 30,
            FontSize = 20,
            Padding = new Thickness(0),
            Background = new SolidColorBrush(Color.FromRgb(247, 248, 250)),
            Foreground = new SolidColorBrush(Muted),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Top,
            ToolTip = "إغلاق",
            Visibility = progress ? Visibility.Collapsed : Visibility.Visible
        };
        close.Click += (_, _) => Close();
        Grid.SetColumn(close, 2);
        header.Children.Add(close);
        header.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); };
        content.Children.Add(header);

        var body = new StackPanel { Margin = new Thickness(22, 4, 22, 20) };
        var messageText = new TextBlock
        {
            Text = message,
            FontSize = 12.5,
            Foreground = new SolidColorBrush(Muted),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 21,
            Margin = new Thickness(0, 0, 0, 12)
        };
        body.Children.Add(messageText);

        if (progress)
        {
            var progressBar = new ProgressBar
            {
                Height = 7,
                IsIndeterminate = true,
                Foreground = new SolidColorBrush(Accent),
                Background = new SolidColorBrush(Color.FromRgb(250, 231, 234)),
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 4, 0, 10)
            };
            body.Children.Add(progressBar);
            _stageText = new TextBlock
            {
                Text = "جارٍ بدء العملية…",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Accent),
                TextWrapping = TextWrapping.Wrap
            };
            body.Children.Add(_stageText);
        }

        outer.Child = content;
        content.Children.Add(body);
        Content = outer;
    }

    public static WorkflowFeedbackWindow ShowProgress(Window? owner, string title, string message, string stage)
    {
        var window = new WorkflowFeedbackWindow(title, message, true, WorkflowFeedbackKind.Info);
        SetOwner(window, owner);
        window._stageText!.Text = stage;
        window.Show();
        window.Activate();
        return window;
    }

    public void UpdateStage(string stage)
    {
        if (_stageText is not null)
            _stageText.Text = stage;
    }

    public static string? ShowNotice(
        Window? owner,
        string title,
        string message,
        WorkflowFeedbackKind kind,
        IEnumerable<KeyValuePair<string, string>>? details = null,
        params WorkflowFeedbackAction[] actions)
    {
        var window = new WorkflowFeedbackWindow(title, message, false, kind);
        SetOwner(window, owner);

        if (details is not null)
        {
            var detailRows = details.Where(item => !string.IsNullOrWhiteSpace(item.Key)).ToList();
            if (detailRows.Count > 0 && window.Content is Border root && root.Child is StackPanel rootStack)
            {
                var body = rootStack.Children.OfType<StackPanel>().LastOrDefault();
                if (body is not null)
                {
                    var detailPanel = new StackPanel { Margin = new Thickness(0, 2, 0, 12) };
                    foreach (var pair in detailRows)
                    {
                        var row = new Grid { Margin = new Thickness(0, 3, 0, 3), FlowDirection = FlowDirection.RightToLeft };
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) });
                        // In an RTL grid column 0 is the right-hand column. Put the
                        // Arabic field label on the right and its value on the left.
                        var label = new TextBlock
                        {
                            Text = pair.Key,
                            FontSize = 11,
                            Foreground = new SolidColorBrush(Muted),
                            TextWrapping = TextWrapping.Wrap,
                            TextAlignment = TextAlignment.Right,
                            VerticalAlignment = VerticalAlignment.Center,
                            Margin = new Thickness(0, 0, 10, 0)
                        };
                        var value = new TextBlock
                        {
                            Text = pair.Value,
                            FontSize = 11.5,
                            FontWeight = FontWeights.SemiBold,
                            Foreground = new SolidColorBrush(Ink),
                            TextWrapping = TextWrapping.Wrap,
                            TextAlignment = TextAlignment.Right,
                            FlowDirection = FlowDirection.RightToLeft,
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        row.Children.Add(label);
                        Grid.SetColumn(label, 0);
                        Grid.SetColumn(value, 1);
                        row.Children.Add(value);
                        detailPanel.Children.Add(new Border
                        {
                            Background = new SolidColorBrush(Color.FromRgb(248, 249, 251)),
                            BorderBrush = new SolidColorBrush(Color.FromRgb(235, 237, 241)),
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(7),
                            Padding = new Thickness(10, 7, 10, 7),
                            Child = row
                        });
                    }
                    var index = body.Children.IndexOf(body.Children.OfType<TextBlock>().First());
                    body.Children.Insert(Math.Max(1, index + 1), detailPanel);
                }
            }
        }

        if (actions is null || actions.Length == 0)
            actions = [new WorkflowFeedbackAction("close", "حسنًا", true)];

        if (window.Content is Border contentRoot && contentRoot.Child is StackPanel mainStack)
        {
            var footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                FlowDirection = FlowDirection.RightToLeft,
                Margin = new Thickness(22, 0, 22, 20)
            };
            foreach (var action in actions)
            {
                var button = new Button
                {
                    Content = action.Label,
                    MinWidth = 105,
                    Height = 36,
                    Padding = new Thickness(14, 0, 14, 0),
                    Margin = new Thickness(0, 0, 8, 0),
                    FontSize = 11.5,
                    FontWeight = FontWeights.SemiBold,
                    Cursor = Cursors.Hand,
                    Background = new SolidColorBrush(action.IsPrimary ? ToneColor(kind) : Colors.White),
                    Foreground = new SolidColorBrush(action.IsPrimary ? Colors.White : Ink),
                    BorderBrush = new SolidColorBrush(action.IsPrimary ? ToneColor(kind) : Color.FromRgb(220, 224, 230)),
                    BorderThickness = new Thickness(1)
                };
                button.Click += (_, _) => { window._selectedAction = action.Key; window.Close(); };
                footer.Children.Add(button);
            }
            mainStack.Children.Add(footer);
        }

        window.ShowDialog();
        return window._selectedAction;
    }

    private static void SetOwner(Window window, Window? owner)
    {
        if (owner is null || !owner.IsVisible)
            return;
        window.Owner = owner;
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }

    private static Color ToneColor(WorkflowFeedbackKind kind) => kind switch
    {
        WorkflowFeedbackKind.Success => Color.FromRgb(21, 128, 78),
        WorkflowFeedbackKind.Warning => Color.FromRgb(177, 102, 10),
        WorkflowFeedbackKind.Error => Color.FromRgb(180, 35, 40),
        _ => Accent
    };
}
