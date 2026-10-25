using System.IO;
using System.Text.Json;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Packages.Services;
using Nexora.Demo.Desktop.Features.Packages.Views;
using Nexora.Demo.Desktop.Features.Shared;

namespace Nexora.Demo.Desktop.Features.Cards.Views;

// Responsibility: Local print preview document construction and print queue navigation.
public partial class CardIssuanceView
{
    private void OpenPrintQueueButton_Click(object sender, RoutedEventArgs e)
    {
        List<GeneratedDemoCard> cards;
        var targetName = _targetSystem == DemoAccountSystem.HotSpot ? "مستخدمو الهوتسبوت" : "مدير المستخدمين";
        var selectedBatch = RecentBatchesGrid.SelectedItem as GeneratedCardBatch;
        var batch = selectedBatch ?? _issuanceService.Batches
            .Where(item => string.Equals(item.TargetSystem, targetName, StringComparison.Ordinal))
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefault();

        if (batch is not null)
        {
            cards = _issuanceService.Cards
                .Where(card => string.Equals(card.BatchId, batch.BatchId, StringComparison.Ordinal))
                .OrderBy(card => card.CreatedAt).ToList();
        }
        else if (_lastGeneratedCards.Count > 0)
        {
            cards = _lastGeneratedCards.ToList();
        }
        else
        {
            cards = _issuanceService.Cards
                .Where(card => string.Equals(card.TargetSystem, targetName, StringComparison.Ordinal))
                .OrderByDescending(card => card.CreatedAt).Take(100).OrderBy(card => card.CreatedAt).ToList();
        }
        ShowLocalPrintPreview(cards);
    }
    private void ShowLocalPrintPreview(List<GeneratedDemoCard> cards)
    {
        if (cards is null || cards.Count == 0)
        {
            SetStatus("لا توجد كروت للمعاينة بعد. أنشئ دفعة أولًا أو اختر دفعة موجودة.", false);
            return;
        }

        var orderedCards = cards.ToList();
        var batchIds = string.Join("، ", orderedCards.Select(card => card.BatchId).Distinct(StringComparer.OrdinalIgnoreCase).Take(3));
        var previewRows = orderedCards.Select((card, index) => new
        {
            SerialNumber = index + 1,
            CardNumber = card.CardNumber,
            Username = card.Username,
            Password = card.PasswordDisplay,
            Package = card.PackageName,
            Price = card.UnitPriceLabel
        }).ToList();

        var owner = Window.GetWindow(this);
        var previewWindow = new Window
        {
            Title = "معاينة الطباعة",
            Width = 1080,
            Height = 690,
            MinWidth = 780,
            MinHeight = 480,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(247, 248, 250)),
            FlowDirection = FlowDirection.RightToLeft,
            ResizeMode = ResizeMode.CanResize
        };
        if (owner is not null) previewWindow.Owner = owner;

        var root = new Grid { Margin = new Thickness(12), FlowDirection = FlowDirection.RightToLeft };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(192, 27, 42)),
            BorderThickness = new Thickness(0, 3, 0, 0),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(13, 9, 13, 9),
            Margin = new Thickness(0, 0, 0, 8)
        };
        var headerStack = new StackPanel { FlowDirection = FlowDirection.RightToLeft };
        headerStack.Children.Add(new TextBlock
        {
            Text = "معاينة الطباعة",
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(32, 36, 42)),
            HorizontalAlignment = HorizontalAlignment.Right
        });
        headerStack.Children.Add(new TextBlock
        {
            Text = $"الدفعة: {batchIds}  ·  عدد الكروت: {orderedCards.Count:N0}",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(105, 112, 120)),
            Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right
        });
        header.Child = headerStack;
        root.Children.Add(header);
        Grid.SetRow(header, 0);

        var cardsGrid = new DataGrid
        {
            AutoGenerateColumns = false,
            ItemsSource = previewRows,
            IsReadOnly = true,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            HorizontalGridLinesBrush = new SolidColorBrush(Color.FromRgb(225, 228, 233)),
            VerticalGridLinesBrush = new SolidColorBrush(Color.FromRgb(225, 228, 233)),
            RowHeight = 32,
            ColumnHeaderHeight = 38,
            FontSize = 12,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(222, 225, 230)),
            BorderThickness = new Thickness(1),
            FlowDirection = FlowDirection.RightToLeft,
            Margin = new Thickness(0, 0, 0, 8)
        };
        var headerStyle = new Style(typeof(System.Windows.Controls.Primitives.DataGridColumnHeader));
        headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Color.FromRgb(249, 237, 237))));
        headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, new SolidColorBrush(Color.FromRgb(127, 17, 24))));
        headerStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        headerStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Center));
        cardsGrid.ColumnHeaderStyle = headerStyle;
        cardsGrid.Columns.Add(new DataGridTextColumn { Header = "م", Binding = new System.Windows.Data.Binding("SerialNumber"), Width = new DataGridLength(48) });
        cardsGrid.Columns.Add(new DataGridTextColumn { Header = "رقم الكرت", Binding = new System.Windows.Data.Binding("CardNumber"), Width = new DataGridLength(1.1, DataGridLengthUnitType.Star) });
        cardsGrid.Columns.Add(new DataGridTextColumn { Header = "اسم المستخدم", Binding = new System.Windows.Data.Binding("Username"), Width = new DataGridLength(1.2, DataGridLengthUnitType.Star) });
        cardsGrid.Columns.Add(new DataGridTextColumn { Header = "كلمة المرور", Binding = new System.Windows.Data.Binding("Password"), Width = new DataGridLength(1.1, DataGridLengthUnitType.Star) });
        cardsGrid.Columns.Add(new DataGridTextColumn { Header = "الباقة", Binding = new System.Windows.Data.Binding("Package"), Width = new DataGridLength(1.3, DataGridLengthUnitType.Star) });
        cardsGrid.Columns.Add(new DataGridTextColumn { Header = "السعر", Binding = new System.Windows.Data.Binding("Price"), Width = new DataGridLength(0.8, DataGridLengthUnitType.Star) });
        root.Children.Add(cardsGrid);
        Grid.SetRow(cardsGrid, 1);

        var footer = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(222, 225, 230)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(9)
        };
        var footerGrid = new Grid { FlowDirection = FlowDirection.RightToLeft };
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var closeButton = new Button
        {
            Content = "إغلاق",
            MinWidth = 100,
            Height = 34,
            Margin = new Thickness(2),
            Padding = new Thickness(12, 0, 12, 0),
            Background = Brushes.White,
            Foreground = new SolidColorBrush(Color.FromRgb(32, 36, 42)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(222, 225, 230)),
            BorderThickness = new Thickness(1),
            FontSize = 12,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        closeButton.Click += (_, _) => previewWindow.Close();
        var note = new TextBlock
        {
            Text = "عرض تجريبي محلي. الرقم في عمود (م) هو ترتيب عرض فقط؛ أرقام الكروت نفسها لم تتحول إلى تسلسل.",
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromRgb(105, 112, 120)),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(10, 0, 10, 0)
        };
        var printButton = new Button
        {
            Content = "طباعة المعاينة",
            MinWidth = 140,
            Height = 34,
            Margin = new Thickness(2),
            Padding = new Thickness(12, 0, 12, 0),
            Background = new SolidColorBrush(Color.FromRgb(192, 27, 42)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(192, 27, 42)),
            BorderThickness = new Thickness(1),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        printButton.Click += (_, _) =>
        {
            var dialog = new PrintDialog();
            if (dialog.ShowDialog() != true) return;
            try
            {
                var document = BuildLocalPrintDocument(orderedCards, batchIds, dialog.PrintableAreaWidth, dialog.PrintableAreaHeight);
                dialog.PrintDocument(((System.Windows.Documents.IDocumentPaginatorSource)document).DocumentPaginator, "طباعة كروت تاب كارد");
            }
            catch (Exception ex)
            {
                MessageBox.Show(previewWindow, "تعذرت طباعة المعاينة: " + ex.Message, "تاب كارد", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        };
        footerGrid.Children.Add(closeButton);
        Grid.SetColumn(closeButton, 0);
        footerGrid.Children.Add(note);
        Grid.SetColumn(note, 1);
        footerGrid.Children.Add(printButton);
        Grid.SetColumn(printButton, 2);
        footer.Child = footerGrid;
        root.Children.Add(footer);
        Grid.SetRow(footer, 2);

        previewWindow.Content = root;
        previewWindow.ShowDialog();
    }
    private static System.Windows.Documents.FlowDocument BuildLocalPrintDocument(
        List<GeneratedDemoCard> cards, string batchIds, double printableWidth, double printableHeight)
    {
        var document = new System.Windows.Documents.FlowDocument
        {
            PageWidth = Math.Max(500, printableWidth),
            PageHeight = Math.Max(600, printableHeight),
            PagePadding = new Thickness(24),
            ColumnWidth = double.PositiveInfinity,
            FlowDirection = FlowDirection.RightToLeft,
            FontFamily = new FontFamily("Tahoma"),
            FontSize = 10
        };
        document.Blocks.Add(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("تاب كارد — معاينة طباعة الكروت"))
        {
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(0, 0, 0, 8)
        });
        document.Blocks.Add(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run($"الدفعة: {batchIds} · عدد الكروت: {cards.Count:N0}"))
        {
            FontSize = 11,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var table = new System.Windows.Documents.Table { CellSpacing = 0 };
        foreach (var width in new[] { 0.45, 1.05, 1.15, 1.05, 1.3, 0.8 })
            table.Columns.Add(new System.Windows.Documents.TableColumn { Width = new GridLength(width, GridUnitType.Star) });
        var headerGroup = new System.Windows.Documents.TableRowGroup();
        var headerRow = new System.Windows.Documents.TableRow();
        foreach (var header in new[] { "م", "رقم الكرت", "اسم المستخدم", "كلمة المرور", "الباقة", "السعر" })
            headerRow.Cells.Add(CreateLocalPrintCell(header, true));
        headerGroup.Rows.Add(headerRow);
        table.RowGroups.Add(headerGroup);

        var bodyGroup = new System.Windows.Documents.TableRowGroup();
        for (var index = 0; index < cards.Count; index++)
        {
            var card = cards[index];
            var row = new System.Windows.Documents.TableRow();
            row.Cells.Add(CreateLocalPrintCell((index + 1).ToString(CultureInfo.CurrentCulture), false));
            row.Cells.Add(CreateLocalPrintCell(card.CardNumber, false));
            row.Cells.Add(CreateLocalPrintCell(card.Username, false));
            row.Cells.Add(CreateLocalPrintCell(card.PasswordDisplay, false));
            row.Cells.Add(CreateLocalPrintCell(card.PackageName, false));
            row.Cells.Add(CreateLocalPrintCell(card.UnitPriceLabel, false));
            bodyGroup.Rows.Add(row);
        }
        table.RowGroups.Add(bodyGroup);
        document.Blocks.Add(table);
        return document;
    }
    private static System.Windows.Documents.TableCell CreateLocalPrintCell(string value, bool header)
    {
        var paragraph = new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run(value ?? string.Empty))
        { Margin = new Thickness(0), TextAlignment = TextAlignment.Center };
        var cell = new System.Windows.Documents.TableCell(paragraph)
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(222, 225, 230)),
            BorderThickness = new Thickness(0.5),
            Padding = new Thickness(4, 3, 4, 3),
            Background = header ? new SolidColorBrush(Color.FromRgb(249, 237, 237)) : Brushes.White,
            FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal
        };
        return cell;
    }
}
