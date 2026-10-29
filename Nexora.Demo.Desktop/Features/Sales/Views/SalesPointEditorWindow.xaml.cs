using System.Windows;
using System.Windows.Controls;
using Nexora.Demo.Desktop.Features.Sales.Models;

namespace Nexora.Demo.Desktop.Features.Sales.Views;

public partial class SalesPointEditorWindow : Window
{
    public SalesPointEditorWindow(SalesPointRecord? point)
    {
        InitializeComponent();
        if (point is null)
        {
            EditorTitleTextBlock.Text = "إضافة نقطة بيع";
            CodeTextBox.Text = "يُنشأ تلقائيًا عند الحفظ";
            PointTypeComboBox.SelectedIndex = 1;
            SettlementCycleInput.SelectedValue = "حسب الاتفاق";
            AllowCreditInput.IsChecked = false;
            CommissionPercentInput.Text = string.Empty;
            return;
        }

        EditorTitleTextBlock.Text = "تعديل بيانات نقطة البيع";
        CodeTextBox.Text = point.Code;
        NameTextBox.Text = point.Name;
        PointTypeComboBox.SelectedValue = point.PointType;
        if (PointTypeComboBox.SelectedItem is null)
            PointTypeComboBox.SelectedIndex = 1;
        ContactNameTextBox.Text = point.ContactName;
        PhoneTextBox.Text = point.Phone;
        LocationTextBox.Text = point.Location;
        NotesTextBox.Text = point.Notes;
        CommissionPercentInput.Text = point.CommissionPercent.ToString(System.Globalization.CultureInfo.CurrentCulture);
        SettlementCycleInput.SelectedValue = string.IsNullOrWhiteSpace(point.SettlementCycle) ? "فوري" : point.SettlementCycle;
        if (SettlementCycleInput.SelectedItem is null) SettlementCycleInput.SelectedIndex = 0;
        AllowCreditInput.IsChecked = point.AllowCredit;
        if (point.IsSystemPoint || string.Equals(point.Name, "كروت الشبكات", StringComparison.Ordinal))
        {
            NameTextBox.IsReadOnly = true;
            PointTypeComboBox.IsEnabled = false;
            ContactNameTextBox.IsEnabled = false;
            PhoneTextBox.IsEnabled = false;
            LocationTextBox.IsEnabled = false;
            NotesTextBox.IsReadOnly = true;
            CommissionPercentInput.IsReadOnly = true;
            SettlementCycleInput.IsEnabled = false;
            AllowCreditInput.IsEnabled = false;
        }
    }

    public SalesPointDraft? Draft { get; private set; }

    // Resolve these controls from the window namescope instead of relying on generated fields.
    private TextBox CommissionPercentInput => FindName("CommissionPercentTextBox") as TextBox
        ?? throw new InvalidOperationException("تعذر العثور على حقل نسبة العمولة.");

    private ComboBox SettlementCycleInput => FindName("SettlementCycleComboBox") as ComboBox
        ?? throw new InvalidOperationException("تعذر العثور على حقل دورية التسوية.");

    private CheckBox AllowCreditInput => FindName("AllowCreditCheckBox") as CheckBox
        ?? throw new InvalidOperationException("تعذر العثور على خيار السماح بالآجل.");

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var name = NameTextBox.Text?.Trim() ?? string.Empty;
        if (name.Length < 2)
        {
            MessageBox.Show(this, "أدخل اسمًا واضحًا لنقطة البيع لا يقل عن حرفين.", "بيانات ناقصة", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
            NameTextBox.Focus();
            return;
        }

        var type = (PointTypeComboBox.SelectedValue as string)?.Trim();
        if (string.IsNullOrWhiteSpace(type))
        {
            MessageBox.Show(this, "اختر نوع نقطة البيع.", "بيانات ناقصة", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
            PointTypeComboBox.Focus();
            return;
        }

        if (!decimal.TryParse(CommissionPercentInput.Text?.Trim(), System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.CurrentCulture, out var commission)
            && !decimal.TryParse(CommissionPercentInput.Text?.Trim(), System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.InvariantCulture, out commission))
        {
            MessageBox.Show(this, "أدخل نسبة عمولة رقمية من 0 إلى 100.", "نسبة غير صالحة", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
            CommissionPercentInput.Focus();
            return;
        }
        if (commission < 0m || commission > 100m)
        {
            MessageBox.Show(this, "نسبة العمولة يجب أن تكون بين 0 و100٪.", "نسبة غير صالحة", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
            CommissionPercentInput.Focus();
            return;
        }
        var cycle = (SettlementCycleInput.SelectedValue as string)?.Trim();
        if (string.IsNullOrWhiteSpace(cycle)) cycle = "فوري";
        Draft = new SalesPointDraft(
            name,
            type,
            ContactNameTextBox.Text?.Trim() ?? string.Empty,
            PhoneTextBox.Text?.Trim() ?? string.Empty,
            LocationTextBox.Text?.Trim() ?? string.Empty,
            NotesTextBox.Text?.Trim() ?? string.Empty,
            commission,
            AllowCreditInput.IsChecked == true,
            cycle);
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Draft = null;
        DialogResult = false;
    }
}
