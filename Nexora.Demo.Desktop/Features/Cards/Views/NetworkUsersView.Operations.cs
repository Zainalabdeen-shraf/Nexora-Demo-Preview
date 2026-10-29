using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Shared;

namespace Nexora.Demo.Desktop.Features.Cards.Views;

// Responsibility: Local demo actions and editor interactions.
public partial class NetworkUsersView
{
    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = GeneratedCardsGrid.SelectedItems.OfType<NetworkUserDisplayRow>().ToList();
        if (selected.Count == 0 && GeneratedCardsGrid.SelectedItem is NetworkUserDisplayRow single)
            selected.Add(single);

        ExecuteAction((sender as FrameworkElement)?.Tag?.ToString() ?? string.Empty, selected);
    }
    private void ExecuteAction(string action, IReadOnlyList<NetworkUserDisplayRow> selected)
    {
        if (selected.Count == 0)
        {
            ShowDemoMessage("اختر حسابًا", "حدد مستخدمًا من الجدول أولًا.");
            return;
        }
        if (selected.Count > 1)
        {
            ShowDemoMessage("حدد مستخدمًا واحدًا", "هذه العمليات تعدّل الكرت المحدد فقط. اختر صفًا واحدًا حتى لا تلتبس عليك نتيجة التعديل.");
            return;
        }

        var row = selected[0];
        var state = GetDemoState(row.Card);
        switch (action)
        {
            case "activate":
                state.IsDeleted = false;
                state.AccountStatus = "مفعل";
                RefreshAfterDemoAction(row);
                ShowDemoMessage("تم تفعيل الحساب في المعاينة", $"تم عرض حالة {row.CardNumber} على أنها مفعل. لم يتغير أي حساب حقيقي.");
                break;

            case "suspend":
                state.AccountStatus = "موقوف";
                SetDemoConnectionDown(state);
                RefreshAfterDemoAction(row);
                ShowDemoMessage("تم إيقاف الحساب في المعاينة", $"أصبحت حالة {row.CardNumber} موقوف، وانعكس ذلك في الجدول والتفاصيل.");
                break;

            case "disconnect":
                SetDemoConnectionDown(state);
                RefreshAfterDemoAction(row);
                ShowDemoMessage("تم فصل الاتصال في المعاينة", $"تم عرض {row.CardNumber} كغير متصل، وأزيل عنوان IP وبيانات الجلسة من لوحة التفاصيل.");
                break;

            case "change-package":
            case "edit":
            {
                var result = ShowCardEditorDialog(row, title: "تعديل بيانات الكرت", applyLabel: "حفظ التغييرات");
                if (result is null) return;

                var packageChanged = !string.Equals(result.Package.Name, row.PackageName, StringComparison.OrdinalIgnoreCase);
                if (packageChanged)
                    ApplyDemoPackage(state, result.Package);

                if (result.ValidityExtension > TimeSpan.Zero)
                    ExtendDemoValidity(row, state, result.ValidityExtension);

                if (result.AdditionalDataGb > 0m)
                    state.AddedDataGb += result.AdditionalDataGb;

                if (!string.IsNullOrWhiteSpace(result.Password))
                    state.DemoPassword = result.Password.Trim();

                state.AccountStatus = result.AccountStatus;
                if (state.AccountStatus is "غير متصل" or "موقوف" or "منتهي")
                    SetDemoConnectionDown(state);

                RefreshAfterDemoAction(row);
                var extensionText = result.ValidityExtension > TimeSpan.Zero
                    ? $"\nالتمديد الإضافي: {result.ValidityExtension.TotalHours:0.##} ساعة"
                    : string.Empty;
                var addedDataText = result.AdditionalDataGb > 0m
                    ? $"\nالرصيد المضاف: {result.AdditionalDataGb.ToString("0.##", CultureInfo.InvariantCulture)} GB"
                    : string.Empty;
                ShowDemoMessage(
                    "تم تحديث بيانات الكرت في المعاينة",
                    $"الكرت: {row.CardNumber}\nالباقة: {result.Package.Name}\nالحالة: {state.AccountStatus}\nتاريخ البداية: {state.PackageStartDate ?? row.PackageStartDate}\nتاريخ الانتهاء: {state.EndDate ?? row.EndDate}{extensionText}{addedDataText}\n\nالتعديل خاص بهذا المستخدم وحده، ولا يغيّر بقية كروت الدفعة.");
                break;
            }

            case "renew-package":
            {
                var package = DemoPackageOptions.FirstOrDefault(option => string.Equals(option.Name, row.PackageName, StringComparison.OrdinalIgnoreCase))
                    ?? DemoPackageOptions.First(option => option.Price == 500m);
                ApplyDemoPackage(state, package);
                state.AccountStatus = "مفعل";
                RefreshAfterDemoAction(row);
                ShowDemoMessage("تم تجديد الباقة في المعاينة", $"تم تجديد {row.CardNumber} على باقة {package.Name}.\nتاريخ الانتهاء التجريبي: {state.EndDate}");
                break;
            }

            case "reset-password":
            {
                state.DemoPassword = "TC-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
                RefreshAfterDemoAction(row);
                ShowDemoMessage("كلمة مرور تجريبية جديدة", $"الكرت: {row.CardNumber}\nكلمة مرور العرض: {state.DemoPassword}\n\nهذه كلمة مرور للمعاينة فقط؛ لم يتم إرسالها إلى الراوتر أو حفظها.");
                break;
            }

            case "delete":
            {
                var owner = Window.GetWindow(this);
                var prompt = $"سيختفي {row.CardNumber} من عرض الديمو الحالي فقط. لن يُحذف سجل الكرت أو بيانات الراوتر.";
                var answer = WorkflowFeedbackWindow.ShowNotice(
                    owner, "تأكيد الإخفاء المحلي", prompt, WorkflowFeedbackKind.Warning,
                    [new("الكرت", row.CardNumber), new("النطاق", "عرض الديمو الحالي فقط")],
                    new WorkflowFeedbackAction("hide", "إخفاء الكرت", true),
                    new WorkflowFeedbackAction("cancel", "إلغاء"));
                if (answer != "hide") return;
                state.IsDeleted = true;
                GeneratedCardsGrid.UnselectAll();
                _rowsView.Refresh();
                UpdateEmptyState();
                UpdateSelectedDetails();
                ShowDemoMessage("تم إخفاء الحساب في المعاينة", $"تم إخفاء {row.CardNumber} من هذه الشاشة. لم يتم حذف سجل الكرت أو بيانات الراوتر.");
                break;
            }

            default:
                ShowDemoMessage("إجراء غير معروف", "الإجراء المحدد غير موجود في نموذج العرض الحالي.");
                break;
        }
    }
    private void SetDemoConnectionDown(DemoCardState state)
    {
        state.ConnectionStatus = "غير متصل";
        state.IpAddress = "—";
        state.MacAddress = "غير متاح";
        state.SessionDuration = "غير نشطة";
        state.Download = "—";
        state.Upload = "—";
        state.LastSeen = "الآن";
    }
    private void ApplyDemoPackage(DemoCardState state, DemoPackageOption package)
    {
        var now = DateTime.Now;
        state.PackageName = package.Name;
        state.UnitPrice = package.Price;
        state.PackageStartDate = now.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);
        state.EndDate = now.Add(package.Duration).ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);
        state.RemainingTime = package.RemainingLabel;
        state.PackageProgress = 0;
    }
    private void ExtendDemoValidity(NetworkUserDisplayRow row, DemoCardState state, TimeSpan extension)
    {
        var currentEndText = state.EndDate ?? row.EndDate;
        var formats = new[] { "yyyy/MM/dd HH:mm", "yyyy/M/d HH:mm", "yyyy/MM/dd H:mm" };
        var now = DateTime.Now;
        var effectiveEnd = DateTime.TryParseExact(currentEndText, formats, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var parsedEnd) && parsedEnd > now
                ? parsedEnd
                : now;
        var newEnd = effectiveEnd.Add(extension);
        state.EndDate = newEnd.ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);
        state.RemainingTime = FormatRemainingTime(newEnd - now);
    }
    private static string FormatRemainingTime(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero) return "انتهت الصلاحية";
        if (remaining.TotalDays >= 1)
            return $"{(int)Math.Floor(remaining.TotalDays)} يوم و {remaining.Hours} ساعة";
        if (remaining.TotalHours >= 1)
            return $"{(int)Math.Floor(remaining.TotalHours)} ساعة و {remaining.Minutes} دقيقة";
        return $"{Math.Max(1, remaining.Minutes)} دقيقة";
    }
    private void RefreshAfterDemoAction(NetworkUserDisplayRow previousRow)
    {
        // Reveal the changed state even when it no longer matches the old activity/status selection.
        _activityFilter = "all";
        UpdateActivityButtonStyles();
        var allStatus = CardStatusFilterComboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == "all");
        if (allStatus is not null && !ReferenceEquals(CardStatusFilterComboBox.SelectedItem, allStatus))
            CardStatusFilterComboBox.SelectedItem = allStatus;

        RebuildRows(selectFirst: false);
        var updated = _rowsView.Cast<object>().OfType<NetworkUserDisplayRow>()
            .FirstOrDefault(item => string.Equals(item.CardNumber, previousRow.CardNumber, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.BatchId, previousRow.BatchId, StringComparison.OrdinalIgnoreCase));
        if (updated is null && !string.IsNullOrWhiteSpace(CardSearchTextBox.Text))
        {
            CardSearchTextBox.Clear();
            _rowsView.Refresh();
            UpdateEmptyState();
            updated = _rowsView.Cast<object>().OfType<NetworkUserDisplayRow>()
                .FirstOrDefault(item => string.Equals(item.CardNumber, previousRow.CardNumber, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(item.BatchId, previousRow.BatchId, StringComparison.OrdinalIgnoreCase));
        }
        if (updated is not null)
        {
            GeneratedCardsGrid.SelectedItem = updated;
            GeneratedCardsGrid.ScrollIntoView(updated);
            UpdateSelectedDetails(updated);
        }
    }
    private DemoEditResult? ShowCardEditorDialog(NetworkUserDisplayRow row, string title, string applyLabel)
    {
        var owner = Window.GetWindow(this);
        var dialog = new Window
        {
            Title = title,
            Width = 540,
            Height = 650,
            MinWidth = 540,
            MinHeight = 650,
            MaxWidth = 540,
            MaxHeight = 650,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(243, 245, 249)),
            FlowDirection = FlowDirection.RightToLeft,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI")
        };
        if (owner is not null) dialog.Owner = owner;

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(183, 19, 46)),
            Padding = new Thickness(18, 14, 18, 13)
        };
        var headerStack = new StackPanel();
        headerStack.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.Bold, Foreground = Brushes.White });
        headerStack.Children.Add(new TextBlock
        {
            Text = "إعدادات هذا الكرت فقط — التعديلات مؤقتة داخل المعاينة",
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 225, 232)),
            Margin = new Thickness(0, 4, 0, 0)
        });
        header.Child = headerStack;
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var content = new StackPanel { Margin = new Thickness(18, 11, 18, 8) };
        var summary = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(232, 197, 207)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 9)
        };
        var summaryStack = new StackPanel();
        summaryStack.Children.Add(new TextBlock
        {
            Text = $"الكرت {row.CardNumber}  ·  الرقم التسلسلي {row.SerialNumber}  ·  الدفعة {row.BatchId}",
            FontSize = 11, FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(23, 40, 73))
        });
        summaryStack.Children.Add(new TextBlock
        {
            Text = $"الباقة: {row.PackageName}  ·  الحالة: {row.AccountStatus}  ·  الانتهاء: {row.EndDate}",
            FontSize = 10, Foreground = new SolidColorBrush(Color.FromRgb(99, 113, 137)),
            Margin = new Thickness(0, 4, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis
        });
        summary.Child = summaryStack;
        content.Children.Add(summary);

        void AddLabel(string text) => content.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 10.5,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(23, 40, 73)),
            Margin = new Thickness(0, 0, 0, 4)
        });

        ComboBox CreateEditorCombo()
        {
            return new ComboBox
            {
                Height = 36,
                FlowDirection = FlowDirection.RightToLeft,
                FontSize = 11.5,
                Style = FindResource("NuCombo") as Style,
                Margin = new Thickness(0, 0, 0, 8)
            };
        }

        TextBox CreateEditorTextBox(string text, FlowDirection flowDirection = FlowDirection.LeftToRight)
        {
            return new TextBox
            {
                Text = text,
                Height = 36,
                Padding = new Thickness(10, 3, 10, 3),
                VerticalContentAlignment = VerticalAlignment.Center,
                FlowDirection = flowDirection,
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(206, 217, 232)),
                BorderThickness = new Thickness(1),
                Foreground = new SolidColorBrush(Color.FromRgb(23, 40, 73)),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 8)
            };
        }

        AddLabel("الباقة");
        var packageBox = CreateEditorCombo();
        foreach (var option in DemoPackageOptions)
            packageBox.Items.Add(new ComboBoxItem { Content = option.Name, Tag = option });
        packageBox.SelectedItem = packageBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals((item.Tag as DemoPackageOption)?.Name, row.PackageName, StringComparison.OrdinalIgnoreCase))
            ?? packageBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
        content.Children.Add(packageBox);

        AddLabel("تمديد الصلاحية الحالية");
        var extensionOptions = new[]
        {
            new ValidityExtensionOption("بدون تغيير الصلاحية", TimeSpan.Zero),
            new ValidityExtensionOption("إضافة ساعة واحدة", TimeSpan.FromHours(1)),
            new ValidityExtensionOption("إضافة 6 ساعات", TimeSpan.FromHours(6)),
            new ValidityExtensionOption("إضافة 24 ساعة", TimeSpan.FromHours(24)),
            new ValidityExtensionOption("إضافة أسبوع (7 أيام)", TimeSpan.FromDays(7)),
            new ValidityExtensionOption("إضافة شهر (30 يومًا)", TimeSpan.FromDays(30))
        };
        var extensionBox = CreateEditorCombo();
        foreach (var option in extensionOptions)
            extensionBox.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option });
        extensionBox.SelectedIndex = 0;
        content.Children.Add(extensionBox);

        AddLabel($"إضافة رصيد بيانات (GB) — المضاف سابقًا {row.AddedDataGb.ToString("0.##", CultureInfo.InvariantCulture)} GB");
        var extraDataBox = CreateEditorTextBox("0");
        extraDataBox.ToolTip = "أدخل كمية البيانات الإضافية التي تريد إضافتها لهذا المستخدم؛ الصفر يعني عدم الإضافة.";
        content.Children.Add(extraDataBox);

        AddLabel("كلمة المرور");
        var passwordBox = CreateEditorTextBox(row.PasswordValue);
        passwordBox.ToolTip = "كلمة مرور هذا الكرت. اتركها كما هي إذا لم ترغب بتغييرها.";
        content.Children.Add(passwordBox);

        AddLabel("حالة الحساب");
        var statusBox = CreateEditorCombo();
        foreach (var status in new[] { "مفعل", "غير متصل", "موقوف", "منتهي" })
            statusBox.Items.Add(new ComboBoxItem { Content = status, Tag = status });
        statusBox.SelectedItem = statusBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), row.AccountStatus, StringComparison.OrdinalIgnoreCase))
            ?? statusBox.Items.OfType<ComboBoxItem>().FirstOrDefault();
        content.Children.Add(statusBox);

        content.Children.Add(new TextBlock
        {
            Text = "التمديد يُضاف إلى الصلاحية المتبقية، وإذا كانت منتهية يبدأ من الآن. رصيد البيانات المضاف يتراكم لهذا المستخدم فقط. سجل التغييرات التفصيلي سيُنفذ لاحقًا في شاشة مستقلة.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 9.5,
            Foreground = new SolidColorBrush(Color.FromRgb(99, 113, 137)),
            Margin = new Thickness(0, 0, 0, 0)
        });
        Grid.SetRow(content, 1);
        root.Children.Add(content);

        var footer = new Border
        {
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 228, 239)),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(16, 9, 16, 9)
        };
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            FlowDirection = FlowDirection.LeftToRight,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        buttons.Children.Add(new Button
        {
            Content = "إلغاء", Width = 92, Height = 34, Margin = new Thickness(0, 0, 8, 0),
            Background = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(210, 220, 233)),
            BorderThickness = new Thickness(1), Foreground = new SolidColorBrush(Color.FromRgb(23, 40, 73)),
            FontWeight = FontWeights.SemiBold, IsCancel = true
        });
        var applyButton = new Button
        {
            Content = applyLabel, Width = 140, Height = 34,
            Background = new SolidColorBrush(Color.FromRgb(199, 25, 50)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(199, 25, 50)), BorderThickness = new Thickness(1),
            Foreground = Brushes.White, FontWeight = FontWeights.Bold, IsDefault = true
        };
        applyButton.Click += (_, _) =>
        {
            var rawExtraData = extraDataBox.Text.Trim();
            decimal extraData;
            if (string.IsNullOrWhiteSpace(rawExtraData))
                extraData = 0m;
            else if (!decimal.TryParse(rawExtraData, NumberStyles.Number, CultureInfo.CurrentCulture, out extraData)
                && !decimal.TryParse(rawExtraData, NumberStyles.Number, CultureInfo.InvariantCulture, out extraData))
            {
                WorkflowFeedbackWindow.ShowNotice(dialog, "قيمة غير صحيحة", "أدخل رصيد بيانات بالأرقام، مثل 2 أو 1.5.", WorkflowFeedbackKind.Warning, null,
                    new WorkflowFeedbackAction("close", "تصحيح القيمة", true));
                extraDataBox.Focus();
                extraDataBox.SelectAll();
                return;
            }

            if (extraData < 0m)
            {
                WorkflowFeedbackWindow.ShowNotice(dialog, "قيمة غير صحيحة", "رصيد البيانات لا يمكن أن يكون سالبًا.", WorkflowFeedbackKind.Warning, null,
                    new WorkflowFeedbackAction("close", "تصحيح القيمة", true));
                extraDataBox.Focus();
                extraDataBox.SelectAll();
                return;
            }
            if (packageBox.SelectedItem is not ComboBoxItem || extensionBox.SelectedItem is not ComboBoxItem || statusBox.SelectedItem is not ComboBoxItem)
                return;

            dialog.DialogResult = true;
        };
        buttons.Children.Add(applyButton);
        footer.Child = buttons;
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        dialog.Content = root;

        if (dialog.ShowDialog() != true)
            return null;

        var selectedPackage = (packageBox.SelectedItem as ComboBoxItem)?.Tag as DemoPackageOption;
        var selectedStatus = (statusBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        var selectedExtension = (extensionBox.SelectedItem as ComboBoxItem)?.Tag as ValidityExtensionOption;
        var enteredExtraData = decimal.TryParse(extraDataBox.Text.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out var parsedExtraData)
            ? parsedExtraData
            : decimal.TryParse(extraDataBox.Text.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out parsedExtraData) ? parsedExtraData : 0m;
        return selectedPackage is null || selectedExtension is null || selectedStatus is null
            ? null
            : new DemoEditResult(selectedPackage, selectedStatus, selectedExtension.Duration, enteredExtraData, passwordBox.Text.Trim());
    }
    private void ShowDemoMessage(string title, string message)
    {
        var fullMessage = message + "\n\nمحاكاة محلية فقط — لا يوجد اتصال بالراوتر ولا حفظ دائم.";
        WorkflowFeedbackWindow.ShowNotice(
            Window.GetWindow(this), title, fullMessage, WorkflowFeedbackKind.Info, null,
            new WorkflowFeedbackAction("close", "متابعة", true));
    }
    private static string NormalizeDateLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "—";
        var match = Regex.Match(value, @"(?<time>\d{1,2}:\d{2})\s+(?<date>\d{4}/\d{2}/\d{2})");
        return match.Success ? $"{match.Groups["date"].Value} {match.Groups["time"].Value}" : value;
    }
}
