using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Nexora.Demo.Desktop.Features.Sales.Models;

namespace Nexora.Demo.Desktop.Features.Sales.Views;

public partial class SalesExpenseWindow : Window
{
    public SalesExpenseWindow()
    {
        InitializeComponent();
    }

    public SalesExpenseDraft? Draft { get; private set; }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var category = (CategoryComboBox.SelectedValue as string)?.Trim();
        var description = DescriptionTextBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(category) || description.Length < 2)
        {
            MessageBox.Show(this, "اختر تصنيف المصروف وأدخل وصفًا واضحًا.", "بيانات ناقصة", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
            return;
        }
        if (!decimal.TryParse(AmountTextBox.Text?.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out var amount)
            && !decimal.TryParse(AmountTextBox.Text?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out amount))
        {
            MessageBox.Show(this, "أدخل مبلغًا رقميًا صحيحًا.", "مبلغ غير صالح", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
            AmountTextBox.Focus();
            return;
        }
        if (amount <= 0m)
        {
            MessageBox.Show(this, "يجب أن يكون مبلغ المصروف أكبر من صفر.", "مبلغ غير صالح", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
            AmountTextBox.Focus();
            return;
        }
        Draft = new SalesExpenseDraft(category, description, amount, ReferenceTextBox.Text?.Trim() ?? string.Empty, NotesTextBox.Text?.Trim() ?? string.Empty);
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Draft = null;
        DialogResult = false;
    }
}
