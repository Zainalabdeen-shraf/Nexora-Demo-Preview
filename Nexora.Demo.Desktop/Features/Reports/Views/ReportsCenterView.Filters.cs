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

// Responsibility: Report filter state and row-matching rules.
public partial class ReportsCenterView
{
    private string SelectedTag(ComboBox combo, string fallback = "all") =>
        (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;
    private void Filters_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing && ReportGrid is not null)
            RefreshReport();
    }
    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_refreshing && ReportGrid is not null)
            RefreshReport();
    }
    private void DateFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing && ReportGrid is not null)
            RefreshReport();
    }
    private static bool WithinRange(DateTime value, DateTime? from, DateTime? to) =>
        (!from.HasValue || value.Date >= from.Value.Date) && (!to.HasValue || value.Date <= to.Value.Date);
    private static bool Matches(string search, params string?[] fields) =>
        search.Length == 0 || fields.Any(value => !string.IsNullOrWhiteSpace(value) && value.Contains(search, StringComparison.CurrentCultureIgnoreCase));
    private static bool MatchesSystem(string targetSystem, string sourceKey) => sourceKey == "all" || SystemKey(targetSystem) == sourceKey;
    private static string SystemKey(string targetSystem) =>
        targetSystem.Contains("hotspot", StringComparison.OrdinalIgnoreCase) || targetSystem.Contains("هوتسبوت", StringComparison.OrdinalIgnoreCase)
            ? "hotspot" : "user-manager";
}
