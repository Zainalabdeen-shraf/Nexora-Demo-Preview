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

// Responsibility: Point-of-sale options and persisted issuance preferences.
public partial class CardIssuanceView
{
    private void AddPointOfSaleButton_Click(object sender, RoutedEventArgs e) => AddPointOfSaleOption();
    private void AddPointOfSaleOption()
    {
        var value = PromptForOption("إضافة نقطة بيع", "اسم نقطة البيع");
        if (string.IsNullOrWhiteSpace(value)) return;

        try
        {
            _workflowStore.AddOption(GetCurrentSystemKey(), "point-of-sale", value);
            var existing = PointOfSaleComboBox.Items.OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                existing = new ComboBoxItem { Content = value, Tag = "custom-pos:" + value };
                PointOfSaleComboBox.Items.Add(existing);
            }
            PointOfSaleComboBox.SelectedItem = existing;
            PersistCurrentPreferences();
            SetStatus($"تم حفظ نقطة البيع «{value}».", false);
        }
        catch (Exception ex)
        {
            SetStatus("تعذر حفظ نقطة البيع: " + ex.Message, true);
        }
    }
    private string? PromptForOption(string title, string prompt)

    {

        var editor = new TextBox

        {

            Height = 34,

            Margin = new Thickness(0, 7, 0, 11),

            Padding = new Thickness(8, 4, 8, 4),

            VerticalContentAlignment = VerticalAlignment.Center,

            FlowDirection = FlowDirection.RightToLeft

        };

        var saveButton = new Button

        {

            Content = "حفظ",

            MinWidth = 88,

            Height = 34,

            Padding = new Thickness(14, 0, 14, 0),

            Background = new SolidColorBrush(Color.FromRgb(160, 24, 24)),

            Foreground = Brushes.White,

            BorderBrush = new SolidColorBrush(Color.FromRgb(118, 13, 13)),

            IsDefault = true

        };

        var cancelButton = new Button { Content = "إلغاء", MinWidth = 88, Height = 34, Padding = new Thickness(14, 0, 14, 0), IsCancel = true };

        var dialog = new Window

        {

            Title = title,

            Width = 390,

            Height = 178,

            MinWidth = 350,

            ResizeMode = ResizeMode.NoResize,

            WindowStartupLocation = WindowStartupLocation.CenterOwner,

            FlowDirection = FlowDirection.RightToLeft,

            Background = new SolidColorBrush(Color.FromRgb(244, 245, 247)),

            Owner = Window.GetWindow(this)

        };

        var stack = new StackPanel { Margin = new Thickness(16) };

        stack.Children.Add(new TextBlock { Text = prompt, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(32, 36, 42)) });

        stack.Children.Add(editor);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };

        buttons.Children.Add(saveButton);

        buttons.Children.Add(cancelButton);

        stack.Children.Add(buttons);

        dialog.Content = stack;

        saveButton.Click += (_, _) =>

        {

            if (string.IsNullOrWhiteSpace(editor.Text))

            {

                MessageBox.Show(dialog, "اكتب اسمًا قبل الحفظ.", title, MessageBoxButton.OK, MessageBoxImage.Information);

                editor.Focus();

                return;

            }

            dialog.DialogResult = true;

            dialog.Close();

        };

        return dialog.ShowDialog() == true ? editor.Text.Trim() : null;

    }
    private string GetCurrentSystemKey() => _targetSystem == DemoAccountSystem.HotSpot ? "hotspot" : "user-manager";
    private void LoadPersistedOptions()

    {

        foreach (var value in _workflowStore.GetOptions(GetCurrentSystemKey(), "point-of-sale"))

        {

            if (!PointOfSaleComboBox.Items.OfType<ComboBoxItem>().Any(item => string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase)))

                PointOfSaleComboBox.Items.Add(new ComboBoxItem { Content = value, Tag = "custom-pos:" + value });

        }

    }
    private void LoadPersistedPreferences()

    {

        var values = _workflowStore.GetIssuancePreferences(GetCurrentSystemKey());

        if (values.Count == 0)

            return;

        _isApplyingPreferences = true;

        try

        {

            void SetText(string key, TextBox control)

            {

                if (values.TryGetValue(key, out var value) && value is not null)

                    control.Text = value;

            }

            void SetTag(string key, ComboBox control)

            {

                if (values.TryGetValue(key, out var value))

                    control.SelectedItem = control.Items.OfType<ComboBoxItem>().FirstOrDefault(item => item.Tag?.ToString() == value)

                        ?? control.SelectedItem;

            }

            SetText("Quantity", QuantityTextBox);

            SetText("CardPrefix", CardPrefixTextBox);

            SetText("CardSuffix", CardSuffixTextBox);

            SetText("CardNumberLength", CardNumberLengthTextBox);
            SetText("PasswordLength", PasswordLengthTextBox);
SetText("RouterProfile", RouterProfileTextBox);

            SetText("Comment", CommentTextBox);

            SetTag("CardNumberCharacterSet", CardNumberCharacterSetComboBox);
            SetTag("PasswordCharacterSet", PasswordCharacterSetComboBox);

            SetTag("PasswordMode", PasswordModeComboBox);

            SetTag("ManagerAccount", ManagerUserComboBox);

            SetTag("SharedUsers", SharedUsersComboBox);

            SetTag("PointOfSale", PointOfSaleComboBox);

            if (values.TryGetValue("PackageKey", out var packageKey))

                PackageComboBox.SelectedItem = PackageComboBox.Items.OfType<DemoCardPackage>().FirstOrDefault(package => package.Key == packageKey)

                    ?? PackageComboBox.SelectedItem;
if (values.TryGetValue("BatchMode", out var batchMode))

            {

                NewBatchRadioButton.IsChecked = batchMode == "new";

                AppendToPreviousBatchRadioButton.IsChecked = batchMode == "append";

            }

            if (values.TryGetValue("ExistingBatchId", out var existingBatchId))

            {

                RefreshExistingBatches();

                ExistingBatchComboBox.SelectedItem = ExistingBatchComboBox.Items.OfType<GeneratedCardBatch>()

                    .FirstOrDefault(batch => string.Equals(batch.BatchId, existingBatchId, StringComparison.OrdinalIgnoreCase))

                    ?? ExistingBatchComboBox.SelectedItem;

            }

            if (values.TryGetValue("PrintPaperSize", out var paperSize)) _printPaperSize = paperSize;

            if (values.TryGetValue("PrintColumns", out var columns) && int.TryParse(columns, out var parsedColumns)) _printColumns = Math.Clamp(parsedColumns, 1, 8);

            if (values.TryGetValue("PrintCutMarks", out var cutMarks)) _printCutMarks = cutMarks != "false";

        }

        finally

        {

            _isApplyingPreferences = false;

        }

    }
    private void PersistCurrentPreferences()

    {

        if (_isApplyingPreferences || !IsLoaded || PackageComboBox is null)

            return;

        try

        {

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)

            {

                ["Quantity"] = QuantityTextBox.Text,

                ["CardPrefix"] = CardPrefixTextBox.Text,

                ["CardSuffix"] = CardSuffixTextBox.Text,

                ["CardNumberLength"] = CardNumberLengthTextBox.Text,
                ["PasswordLength"] = PasswordLengthTextBox.Text,
["RouterProfile"] = RouterProfileTextBox.Text,

                ["Comment"] = CommentTextBox.Text,

                ["CardNumberCharacterSet"] = ReadTag(CardNumberCharacterSetComboBox) ?? "NumericOnly",
                ["PasswordCharacterSet"] = ReadTag(PasswordCharacterSetComboBox) ?? "NumericOnly",

                ["PasswordMode"] = ReadTag(PasswordModeComboBox) ?? "Empty",

                ["ManagerAccount"] = ReadTag(ManagerUserComboBox) ?? "admin",

                ["SharedUsers"] = ReadTag(SharedUsersComboBox) ?? "1",

                ["PointOfSale"] = ReadTag(PointOfSaleComboBox) ?? "main",

                ["PackageKey"] = (PackageComboBox.SelectedItem as DemoCardPackage)?.Key ?? string.Empty,
                ["ExistingBatchId"] = (ExistingBatchComboBox.SelectedItem as GeneratedCardBatch)?.BatchId ?? string.Empty,

                ["BatchMode"] = AppendToPreviousBatchRadioButton.IsChecked == true ? "append" : "new",

                ["PrintPaperSize"] = _printPaperSize,

                ["PrintColumns"] = _printColumns.ToString(CultureInfo.InvariantCulture),

                ["PrintCutMarks"] = _printCutMarks ? "true" : "false"

            };

            _workflowStore.SaveIssuancePreferences(GetCurrentSystemKey(), values);

        }

        catch (Exception ex)

        {

            SetStatus("تعذر حفظ تفضيلات الشاشة: " + ex.Message, true);

        }

    }
}
