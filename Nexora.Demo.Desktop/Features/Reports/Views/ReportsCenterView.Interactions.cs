using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Sales.Models;
using Nexora.Demo.Desktop.Features.Sales.Services;
using Nexora.Demo.Desktop.Features.Shared;

namespace Nexora.Demo.Desktop.Features.Reports.Views;

// Responsibility: Column options, row navigation, export, and print actions.
public partial class ReportsCenterView
{
    private void ColumnsButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = sender as UIElement,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
        };
        menu.Items.Add(new MenuItem { Header = "إظهار أو إخفاء الأعمدة", IsEnabled = false });
        menu.Items.Add(new Separator());

        foreach (var column in ReportGrid.Columns.OrderBy(candidate => candidate.DisplayIndex))
        {
            var item = new MenuItem
            {
                Header = Convert.ToString(column.Header, CultureInfo.CurrentCulture) ?? string.Empty,
                IsCheckable = true,
                IsChecked = column.Visibility == Visibility.Visible,
                StaysOpenOnClick = true
            };
            item.Click += (_, _) =>
            {
                var shouldShow = item.IsChecked;
                var visibleCount = ReportGrid.Columns.Count(candidate => candidate.Visibility == Visibility.Visible);
                if (!shouldShow && visibleCount <= 1)
                {
                    item.IsChecked = true;
                    return;
                }
                column.Visibility = shouldShow ? Visibility.Visible : Visibility.Collapsed;
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }
    private void ReportGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        // Keep the WPF DataGrid's normal sortable-column behavior.
    }
    private void ReportGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (ReportGrid.SelectedItem is not ReportTableRow row || string.IsNullOrWhiteSpace(row.ContextValue)) return;
        switch (row.ContextKind)
        {
            case "card":
                NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("network-users", "كل الحسابات", row.ContextValue, "card", row.SystemKey));
                break;
            case "batch-details":
                NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("cards", "إدارة الدفعات", row.ContextValue, "batch-details", row.SystemKey));
                break;
            case "batch":
                NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("network-users", "كل الحسابات", row.ContextValue, "batch", row.SystemKey));
                break;
            case "sale":
                NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("sales", "معاملات البيع", row.ContextValue, "sale"));
                break;
        }
    }
    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_visibleRows.Length == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "لا توجد نتائج لتصديرها ضمن الفلاتر الحالية.", "مركز التقارير", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "تصدير التقرير",
            Filter = "CSV UTF-8 (*.csv)|*.csv",
            DefaultExt = ".csv",
            AddExtension = true,
            FileName = $"Nexora-{_reportKey}-{DateTime.Now:yyyyMMdd-HHmm}.csv"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        try
        {
            var visibleColumns = ReportGrid.Columns.Where(column => column.Visibility == Visibility.Visible).OrderBy(column => column.DisplayIndex).ToArray();
            var headers = visibleColumns.Select(column => Convert.ToString(column.Header, CultureInfo.CurrentCulture) ?? string.Empty).ToArray();
            var valueIndexes = visibleColumns.Select(GetReportColumnValueIndex).ToArray();
            var csv = new StringBuilder();
            csv.AppendLine(string.Join(",", headers.Select(Csv)));
            foreach (var row in _visibleRows)
                csv.AppendLine(string.Join(",", valueIndexes.Select(index => Csv(row.Values[index]))));
            File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(true));
            ResultSummaryTextBlock.Text = $"تم تصدير {_visibleRows.Length:N0} نتيجة إلى الملف المحدد.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), "تعذر تصدير التقرير: " + ex.Message, "مركز التقارير", MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
        }
    }
    private void PrintButton_Click(object sender, RoutedEventArgs e)
    {
        if (_visibleRows.Length == 0) return;
        var dialog = new PrintDialog();
        if (dialog.ShowDialog() != true) return;
        try
        {
            var printable = new Grid
            {
                Width = dialog.PrintableAreaWidth,
                Background = System.Windows.Media.Brushes.White,
                FlowDirection = FlowDirection.RightToLeft
            };
            printable.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            printable.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var title = new TextBlock { Text = $"NEXORA · {Convert.ToString(ReportTypeComboBox.Text, CultureInfo.CurrentCulture)}", FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 12) };
            Grid.SetRow(title, 0); printable.Children.Add(title);
            var table = new DataGrid
            {
                ItemsSource = _visibleRows,
                AutoGenerateColumns = false,
                IsReadOnly = true,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                FlowDirection = FlowDirection.RightToLeft,
                Width = dialog.PrintableAreaWidth
            };
            foreach (var column in ReportGrid.Columns.Where(column => column.Visibility == Visibility.Visible).OrderBy(column => column.DisplayIndex))
            {
                var binding = new System.Windows.Data.Binding($"Value{GetReportColumnValueIndex(column) + 1}");
                table.Columns.Add(new DataGridTextColumn { Header = column.Header, Binding = binding, Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            }
            Grid.SetRow(table, 1); printable.Children.Add(table);
            printable.Measure(new Size(dialog.PrintableAreaWidth, dialog.PrintableAreaHeight));
            printable.Arrange(new Rect(new Point(0, 0), printable.DesiredSize));
            printable.UpdateLayout();
            dialog.PrintVisual(printable, "Nexora Report");
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), "تعذرت طباعة التقرير: " + ex.Message, "مركز التقارير", MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
        }
    }
    public void FocusTransaction(string transactionNumber)
    {
        ReportTypeComboBox.SelectedIndex = 2;
        SearchTextBox.Text = transactionNumber;
        RefreshReport();
    }
}
