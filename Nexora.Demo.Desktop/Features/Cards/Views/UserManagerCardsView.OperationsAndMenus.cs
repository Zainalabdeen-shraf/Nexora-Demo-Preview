using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Shared;

namespace Nexora.Demo.Desktop.Features.Cards.Views;

// Responsibility: Printing, exporting, row operations, and context menus.
public partial class UserManagerCardsView
{
    private void PrintButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedRows();
        var rows = selected.Count > 0 ? selected : _cardsView.Cast<ManagedCardRow>().ToList();
        QueueForPrint(rows);
    }
    private void SelectionPrintButton_Click(object sender, RoutedEventArgs e) => QueueForPrint(GetSelectedRows());
    private void QueueForPrint(IReadOnlyCollection<ManagedCardRow> rows)
    {
        if (rows.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "لا توجد كروت محددة للطباعة. حدّد كرتًا أو أكثر أولًا.", "قائمة الطباعة", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _workflowStore.QueuePrintSelection(rows.Select(row => row.ToEffectiveCard()).ToList());
        NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("printing", "قائمة الطباعة"));
    }
    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedRows();
        var rows = selected.Count > 0 ? selected : _cardsView.Cast<ManagedCardRow>().ToList();
        if (rows.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "لا توجد سجلات لتصديرها.", "تصدير الكروت", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new SaveFileDialog
        {
            Title = "تصدير سجلات User Manager",
            Filter = "ملف CSV (*.csv)|*.csv",
            FileName = $"UserManager-Cards-{DateTime.Now:yyyyMMdd-HHmm}.csv",
            AddExtension = true,
            DefaultExt = ".csv"
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var visible = _columnSettings.Where(setting => setting.IsVisible).ToList();
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(",", visible.Select(setting => Csv(setting.Definition.Title))));
        foreach (var row in rows)
            builder.AppendLine(string.Join(",", visible.Select(setting => Csv(row[setting.Definition.Key]))));
        try
        {
            File.WriteAllText(dialog.FileName, builder.ToString(), new UTF8Encoding(true));
            MessageBox.Show(Window.GetWindow(this), $"تم تصدير {rows.Count:N0} سجل إلى ملف CSV. هذا تصدير محلي لبيانات الديمو.", "اكتمل التصدير", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), "تعذر حفظ ملف التصدير: " + ex.Message, "خطأ التصدير", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    private static string Csv(string? value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
    private void AddCardButton_Click(object sender, RoutedEventArgs e) =>
        NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("user-manager", "إضافة الكروت"));
    private void MoreButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { FlowDirection = FlowDirection.RightToLeft };
        AddMenuItem(menu, "إعدادات الأعمدة", (_, _) => ShowColumnSettings());
        AddMenuItem(menu, "تصدير جميع الصفوف الظاهرة", (_, _) => ExportButton_Click(sender, e));
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "مزامنة المحدد مع الراوتر", (_, _) => SimulateRouterSync(GetSelectedRows()));
        AddMenuItem(menu, "إدارة جلسات المحدد", (_, _) => ShowSessionsNotice(GetSelectedRows()));
        OpenMenu(menu, (Button)sender);
    }
    private void SelectionMoreButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedRows();
        if (selected.Count == 0) return;
        var menu = new ContextMenu { FlowDirection = FlowDirection.RightToLeft };
        AddMenuItem(menu, "تفعيل المحدد محليًا", (_, _) => SetLocalEnabled(selected, true));
        AddMenuItem(menu, "تعطيل المحدد محليًا", (_, _) => SetLocalEnabled(selected, false));
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "تعديل الباقة", (_, _) => ChangePackage(selected));
        AddMenuItem(menu, "تغيير نقطة البيع", (_, _) => ChangePointOfSale(selected));
        AddMenuItem(menu, "مزامنة مع الراوتر", (_, _) => SimulateRouterSync(selected));
        AddMenuItem(menu, "إدارة الجلسات", (_, _) => ShowSessionsNotice(selected));
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "أرشفة المحدد محليًا", (_, _) => ArchiveRows(selected));
        AddMenuItem(menu, "حذف من عرض الديمو…", (_, _) => DeleteRows(selected));
        OpenMenu(menu, (Button)sender);
    }
    private void ClearSelectionButton_Click(object sender, RoutedEventArgs e) => CardsGrid.UnselectAll();
    private void SelectionDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = GetSelectedRows();
        if (selected.Count == 1) ShowCardDetails(selected[0]);
        else MessageBox.Show(Window.GetWindow(this), "حدد كرتًا واحدًا لعرض التفاصيل.", "تفاصيل الكرت", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private void CardsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && FindVisualParent<DataGridRow>(source)?.Item is ManagedCardRow row)
            ShowCardDetails(row);
    }
    private void CardsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSelectionSummary();
    private List<ManagedCardRow> GetSelectedRows() => CardsGrid.SelectedItems.OfType<ManagedCardRow>().ToList();
    private void CardsGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        var header = FindVisualParent<DataGridColumnHeader>(source);
        if (header is not null && _definitionsByGridColumn.TryGetValue(header.Column, out var headerDefinition))
        {
            _rightClickedColumn = headerDefinition;
            var menu = BuildHeaderMenu(headerDefinition);
            menu.PlacementTarget = header;
            menu.Placement = PlacementMode.MousePoint;
            e.Handled = true;
            menu.IsOpen = true;
            return;
        }
        var rowElement = FindVisualParent<DataGridRow>(source);
        if (rowElement?.Item is not ManagedCardRow row) return;
        var cell = FindVisualParent<DataGridCell>(source);
        _rightClickedColumn = cell is not null && _definitionsByGridColumn.TryGetValue(cell.Column, out var cellDefinition)
            ? cellDefinition : null;
        if (!rowElement.IsSelected)
        {
            CardsGrid.SelectedItems.Clear();
            rowElement.IsSelected = true;
        }
        var rowMenu = BuildRowMenu(row);
        rowMenu.PlacementTarget = rowElement;
        rowMenu.Placement = PlacementMode.MousePoint;
        e.Handled = true;
        rowMenu.IsOpen = true;
    }
    private ContextMenu BuildRowMenu(ManagedCardRow row)
    {
        var menu = new ContextMenu { FlowDirection = FlowDirection.RightToLeft };
        AddMenuItem(menu, "عرض التفاصيل", (_, _) => ShowCardDetails(row));
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "تفعيل محليًا", (_, _) => SetLocalEnabled(new[] { row }, true));
        AddMenuItem(menu, "تعطيل محليًا", (_, _) => SetLocalEnabled(new[] { row }, false));
        AddMenuItem(menu, "تعديل الباقة", (_, _) => ChangePackage(new[] { row }));
        AddMenuItem(menu, "تغيير نقطة البيع", (_, _) => ChangePointOfSale(new[] { row }));
        AddMenuItem(menu, "طباعة الكرت", (_, _) => QueueForPrint(new[] { row }));
        AddMenuItem(menu, "إدارة الجلسات", (_, _) => ShowSessionsNotice(new[] { row }));
        menu.Items.Add(new Separator());
        AddMenuItem(menu, row.State.IsArchived ? "إلغاء الأرشفة محليًا" : "أرشفة الكرت محليًا", (_, _) => ToggleArchive(row));
        AddMenuItem(menu, "حذف من عرض الديمو…", (_, _) => DeleteRows(new[] { row }));
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "مزامنة مع الراوتر", (_, _) => SimulateRouterSync(new[] { row }));
        if (_rightClickedColumn is not null)
            AddMenuItem(menu, "نسخ قيمة الخلية", (_, _) => CopySelectedCell(_rightClickedColumn));
        return menu;
    }
    private void CopySelectedCell(CardColumnDefinition definition)
    {
        var row = GetSelectedRows().FirstOrDefault();
        if (row is null)
        {
            MessageBox.Show(Window.GetWindow(this), "حدد صفًا أولًا لنسخ قيمة الخلية.", "نسخ القيمة", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        try { Clipboard.SetText(row[definition.Key]); }
        catch (Exception ex) { MessageBox.Show(Window.GetWindow(this), "تعذر النسخ إلى الحافظة: " + ex.Message, "نسخ القيمة", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void SetLocalEnabled(IEnumerable<ManagedCardRow> rows, bool enabled)
    {
        var selected = rows.Distinct().ToList();
        if (selected.Count == 0) return;
        foreach (var row in selected)
        {
            row.State.StatusOverride = enabled ? "مفعّلة محليًا" : "معطلة محليًا";
            row.RefreshValues();
        }
        ApplyFilters();
        CardsScopeTextBlock.Text = $"تم تطبيق الحالة على {selected.Count:N0} كرت داخل واجهة الديمو فقط؛ لم يتم تغيير حالة أي مستخدم على الراوتر.";
    }
    private void ToggleArchive(ManagedCardRow row)
    {
        row.State.IsArchived = !row.State.IsArchived;
        row.RefreshValues();
        ApplyFilters();
    }
    private void ArchiveRows(IEnumerable<ManagedCardRow> rows)
    {
        var selected = rows.Distinct().ToList();
        if (selected.Count == 0) return;
        foreach (var row in selected)
        {
            row.State.IsArchived = true;
            row.RefreshValues();
        }
        _archiveFilter = ArchiveFilterMode.ActiveOnly;
        ApplyFilters();
        CardsScopeTextBlock.Text = $"تمت أرشفة {selected.Count:N0} كرت في حالة العرض المحلية فقط.";
    }
    private void DeleteRows(IEnumerable<ManagedCardRow> rows)
    {
        var selected = rows.Distinct().ToList();
        if (selected.Count == 0) return;
        var answer = MessageBox.Show(Window.GetWindow(this),
            $"سيتم إخفاء {selected.Count:N0} سجل من عرض الديمو الحالي. لن يُرسل طلب حذف إلى الراوتر ولن تُحذف سجلات الإنتاج. هل تريد المتابعة؟",
            "تأكيد الحذف المحلي", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        foreach (var row in selected) row.State.IsDeleted = true;
        CardsGrid.UnselectAll();
        ApplyFilters();
        CardsScopeTextBlock.Text = $"تم إخفاء {selected.Count:N0} سجل من العرض المحلي لهذه الجلسة؛ بيانات الخدمة الأصلية لم تُحذف.";
    }
    private void ChangePackage(IEnumerable<ManagedCardRow> rows)
    {
        var selected = rows.Distinct().ToList();
        if (selected.Count == 0) return;
        var packages = DemoCardPackageCatalog.Load(DemoAccountSystem.UserManager).ToList();
        if (packages.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "لا توجد باقات User Manager مفعّلة في كتالوج الديمو. لم يتغير شيء.", "تعديل الباقة", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dialog = new Window
        {
            Title = "تعديل الباقة — محلي",
            Width = 430, Height = 190, MinWidth = 400, MinHeight = 170,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize, FlowDirection = FlowDirection.RightToLeft,
            Background = new SolidColorBrush(Color.FromRgb(244, 246, 248)), Owner = Window.GetWindow(this)
        };
        var root = new DockPanel { Margin = new Thickness(18) };
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
        var save = new Button { Content = "تطبيق محلي", Width = 115, Height = 34, Margin = new Thickness(0, 0, 8, 0), Background = new SolidColorBrush(Color.FromRgb(160, 24, 24)), Foreground = Brushes.White, BorderBrush = new SolidColorBrush(Color.FromRgb(160, 24, 24)) };
        var cancel = new Button { Content = "إلغاء", Width = 85, Height = 34 };
        var combo = new ComboBox { Height = 35, ItemsSource = packages, DisplayMemberPath = "DisplayName", SelectedIndex = 0 };
        var title = new TextBlock { Text = "اختر باقة من كتالوج User Manager المحلي", FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8), Foreground = new SolidColorBrush(Color.FromRgb(32, 36, 42)) };
        DockPanel.SetDock(footer, Dock.Bottom);
        footer.Children.Add(save); footer.Children.Add(cancel);
        root.Children.Add(footer);
        root.Children.Add(new StackPanel { Children = { title, combo } });
        dialog.Content = root;
        save.Click += (_, _) => { if (combo.SelectedItem is DemoCardPackage package) { foreach (var row in selected) { row.State.PackageOverride = package.DisplayName; row.State.PriceOverride = package.Price; row.RefreshValues(); } dialog.DialogResult = true; } };
        cancel.Click += (_, _) => dialog.DialogResult = false;
        if (dialog.ShowDialog() == true)
        {
            ApplyFilters();
            CardsScopeTextBlock.Text = $"تم تغيير عرض الباقة لـ {selected.Count:N0} كرت محليًا فقط؛ لم يتم تغيير الراوتر أو سجل الباقة الأصلي.";
        }
    }
    private void ChangePointOfSale(IEnumerable<ManagedCardRow> rows)
    {
        var selected = rows.Distinct().ToList();
        if (selected.Count == 0) return;
        var choices = _issuanceService.Cards
            .Where(card => string.Equals(card.TargetSystem, UserManagerTarget, StringComparison.Ordinal))
            .Select(card => card.PointOfSale)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(value => value, StringComparer.CurrentCulture)
            .ToList();
        if (choices.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "لا توجد نقاط بيع مسجلة في بيانات الديمو لاختيارها.", "تغيير نقطة البيع", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var combo = new ComboBox { ItemsSource = choices, SelectedItem = selected[0].PointOfSale, MinWidth = 280, Height = 35 };
        if (combo.SelectedItem is null) combo.SelectedIndex = 0;
        var dialog = new Window
        {
            Title = "تغيير نقطة البيع — محلي", Width = 400, Height = 165,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
            FlowDirection = FlowDirection.RightToLeft, Background = new SolidColorBrush(Color.FromRgb(244, 246, 248)), Owner = Window.GetWindow(this)
        };
        var body = new StackPanel { Margin = new Thickness(18) };
        body.Children.Add(new TextBlock { Text = "اختر نقطة بيع موجودة في بيانات الديمو", Margin = new Thickness(0, 0, 0, 8), Foreground = new SolidColorBrush(Color.FromRgb(32, 36, 42)) });
        body.Children.Add(combo);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
        var apply = new Button { Content = "تطبيق محلي", Width = 110, Height = 33, Margin = new Thickness(0, 0, 8, 0), Background = new SolidColorBrush(Color.FromRgb(160, 24, 24)), Foreground = Brushes.White };
        var close = new Button { Content = "إلغاء", Width = 80, Height = 33 };
        actions.Children.Add(apply); actions.Children.Add(close); body.Children.Add(actions); dialog.Content = body;
        apply.Click += (_, _) => dialog.DialogResult = true;
        close.Click += (_, _) => dialog.DialogResult = false;
        if (dialog.ShowDialog() == true && combo.SelectedItem is string pointOfSale)
        {
            foreach (var row in selected) { row.State.PointOfSaleOverride = pointOfSale; row.RefreshValues(); }
            ApplyFilters();
            CardsScopeTextBlock.Text = $"تم تغيير نقطة البيع لـ {selected.Count:N0} كرت في عرض الديمو فقط.";
        }
    }
    private void SimulateRouterSync(IEnumerable<ManagedCardRow> rows)
    {
        var selected = rows.Distinct().ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "حدد كرتًا واحدًا أو أكثر أولًا.", "مزامنة الراوتر", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        _workflowStore.RecordRouterUpload(selected.Select(row => row.ToEffectiveCard()).ToList());
        MessageBox.Show(Window.GetWindow(this), $"تم تسجيل طلب المزامنة لـ {selected.Count:N0} كرت.", "محاكاة مزامنة الراوتر", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private void ShowSessionsNotice(IEnumerable<ManagedCardRow> rows)
    {
        var selected = rows.Distinct().ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(Window.GetWindow(this), "حدد كرتًا أو أكثر أولًا.", "إدارة الجلسات", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        MessageBox.Show(Window.GetWindow(this),
            $"تم تحديد {selected.Count:N0} كرت. نموذج الديمو الحالي لا يحتوي على سجلات جلسات فعلية، ولن تُعرض بيانات جلسات تخمينية. إدارة الجلسات الفعلية تتطلب اتصالًا بالراوتر، وهو غير مفعّل هنا.",
            "إدارة الجلسات — غير متاحة في الديمو", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private void ShowCardDetails(ManagedCardRow row)
    {
        var owner = Window.GetWindow(this);
        var dialog = new Window
        {
            Title = "تفاصيل كرت User Manager",
            Width = 690, Height = 680, MinWidth = 560, MinHeight = 420,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize, FlowDirection = FlowDirection.RightToLeft,
            Background = new SolidColorBrush(Color.FromRgb(244, 246, 248)), Owner = owner
        };
        var root = new DockPanel { Margin = new Thickness(14) };
        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        heading.Children.Add(new TextBlock { Text = row.Source.Username, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(32, 36, 42)) });
        heading.Children.Add(new TextBlock { Text = $"الرقم {row.Source.CardNumber} · الدفعة {row.Source.BatchId} · {row.StatusLabel}", FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromRgb(160, 24, 24)), Margin = new Thickness(0, 4, 0, 0) });
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var footer = new Button { Content = "إغلاق", Width = 90, Height = 34, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        footer.Click += (_, _) => dialog.Close(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var fields = new Grid();
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (var index = 0; index < ColumnDefinitions.Count; index++)
        {
            fields.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var background = new SolidColorBrush(index % 2 == 0 ? Colors.White : Color.FromRgb(248, 249, 251));
            var label = new Border { Background = background, BorderBrush = new SolidColorBrush(Color.FromRgb(222, 225, 230)), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(9, 7, 9, 7) };
            label.Child = new TextBlock { Text = ColumnDefinitions[index].Title, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(109, 116, 128)), TextWrapping = TextWrapping.Wrap };
            var value = new Border { Background = background, BorderBrush = new SolidColorBrush(Color.FromRgb(222, 225, 230)), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(9, 7, 9, 7) };
            value.Child = new TextBlock { Text = row[ColumnDefinitions[index].Key], FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(32, 36, 42)), TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(label, index); Grid.SetColumn(label, 0); fields.Children.Add(label);
            Grid.SetRow(value, index); Grid.SetColumn(value, 1); fields.Children.Add(value);
        }
        scroll.Content = fields; root.Children.Add(scroll); dialog.Content = root; dialog.ShowDialog();
    }
    private void OpenMenu(ContextMenu menu, Button target)
    {
        menu.PlacementTarget = target;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }
    private static void AddMenuItem(ItemsControl menu, string text, RoutedEventHandler handler)
    {
        var item = new MenuItem { Header = text, FlowDirection = FlowDirection.RightToLeft, Padding = new Thickness(10, 5, 10, 5) };
        item.Click += handler;
        menu.Items.Add(item);
    }
}
