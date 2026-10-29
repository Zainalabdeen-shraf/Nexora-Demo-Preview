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

// Responsibility: Dynamic grid column definitions, visibility, and column chooser.
public partial class UserManagerCardsView
{
    private static IReadOnlyList<CardColumnDefinition> CreateColumnDefinitions() => new List<CardColumnDefinition>
    {
        new("Status", "الحالة", "البيانات الأساسية", true, 96, true, true, row => row.StatusLabel, row => row.StatusLabel),
        new("SerialNumber", "الرقم التسلسلي", "البيانات الأساسية", true, 130, true, true, row => row.Source.CardNumber, row => row.Source.CardNumber),
        new("Name", "الاسم", "البيانات الأساسية", true, 120, true, true, row => row.Source.Username, row => row.Source.Username),
        new("Password", "كلمة المرور", "البيانات الأساسية", false, 110, false, true, row => row.Source.PasswordDisplay, row => row.Source.Password),
        new("Price", "السعر", "الباقات والمبيعات", false, 92, true, true, row => row.PriceLabel, row => row.PriceValue),
        new("Package", "الباقة", "الباقات والمبيعات", true, 170, true, true, row => row.PackageName, row => row.PackageName),
        new("BatchNumber", "رقم الدفعة", "الباقات والمبيعات", true, 145, true, true, row => row.Source.BatchId, row => row.Source.BatchId),
        new("EditionNumber", "رقم الطبعة", "الباقات والمبيعات", false, 100, false, false, _ => "—", _ => null),
        new("PointOfSale", "نقطة البيع", "الباقات والمبيعات", false, 130, true, true, row => row.PointOfSale, row => row.PointOfSale),
        new("AllowedTime", "الوقت المسموح", "الاستخدام", false, 115, true, true, row => row.Source.TimeLimit, row => row.Source.TimeLimit),
        new("AllowedDownload", "التنزيل المسموح", "الاستخدام", false, 125, true, true, row => row.Source.DataLimit, row => row.Source.DataLimit),
        new("UsedTime", "الوقت المستخدم", "الاستخدام", false, 112, false, false, _ => "—", _ => null),
        new("DownloadUsed", "التحميل المستخدم", "الاستخدام", false, 125, false, false, _ => "—", _ => null),
        new("UploadUsed", "الرفع المستخدم", "الاستخدام", false, 118, false, false, _ => "—", _ => null),
        new("DownloadUploadUsed", "تحميل + رفع", "الاستخدام", false, 112, false, false, _ => "—", _ => null),
        new("AddedAt", "تاريخ الإضافة", "التواريخ والانتهاء", true, 145, true, true, row => row.Source.CreatedAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture), row => row.Source.CreatedAt),
        new("LastSeen", "آخر ظهور", "التواريخ والانتهاء", false, 145, false, false, _ => "—", _ => null),
        new("FirstLogin", "أول دخول", "التواريخ والانتهاء", false, 145, false, false, _ => "—", _ => null),
        new("ExpiryDate", "تاريخ الانتهاء", "التواريخ والانتهاء", false, 130, false, false, _ => "—", _ => null),
        new("DaysRemaining", "الأيام المتبقية", "التواريخ والانتهاء", false, 115, false, false, _ => "—", _ => null),
        new("TimeRemaining", "الوقت المتبقي", "التواريخ والانتهاء", false, 115, false, false, _ => "—", _ => null),
        new("DownloadRemaining", "التحميل المتبقي", "التواريخ والانتهاء", false, 125, false, false, _ => "—", _ => null),
        new("Comment", "تعليق", "الإدارة والعلاقات", false, 180, true, true, row => EmptyDash(row.Source.Comment), row => row.Source.Comment),
        new("UserManagerClient", "عميل User Manager", "الإدارة والعلاقات", false, 145, false, false, _ => "—", _ => null),
        new("PackageCount", "عدد الباقات", "الإدارة والعلاقات", false, 100, false, false, _ => "—", _ => null),
        new("SessionCount", "عدد جلسات الكرت", "الإدارة والعلاقات", false, 120, false, false, _ => "—", _ => null),
        new("PageNumber", "رقم الصفحة", "الطباعة والأرشيف", false, 95, false, false, _ => "—", _ => null),
        new("SuspendedArchiveSequence", "تسلسل أرشيف المعلّقات", "الطباعة والأرشيف", false, 160, false, false, _ => "—", _ => null)
    };
    private static string EmptyDash(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
    private void InitializeColumnSettings()
    {
        foreach (var definition in ColumnDefinitions)
            AddColumnSetting(definition, definition.DefaultVisible, definition.DefaultWidth);
    }
    private void AddColumnSetting(CardColumnDefinition definition, bool visible, double width)
    {
        var setting = new ColumnSettingItem(definition, visible, width);
        setting.PropertyChanged += ColumnSettingItem_PropertyChanged;
        _columnSettings.Add(setting);
    }
    private void ColumnsButton_Click(object sender, RoutedEventArgs e) => ShowColumnSettings();
    private ContextMenu BuildHeaderMenu(CardColumnDefinition definition)
    {
        var menu = new ContextMenu { FlowDirection = FlowDirection.RightToLeft };
        if (definition.CanSort)
        {
            AddMenuItem(menu, "فرز تصاعدي", (_, _) => SortBy(definition, ListSortDirection.Ascending));
            AddMenuItem(menu, "فرز تنازلي", (_, _) => SortBy(definition, ListSortDirection.Descending));
        }
        if (definition.CanFilter)
            AddMenuItem(menu, "تصفية حسب قيمة الكرت المحدد", (_, _) => FilterBySelectedValue(definition));
        menu.Items.Add(new Separator());
        AddMenuItem(menu, "إخفاء هذا العمود", (_, _) => SetColumnVisibility(definition.Key, false));
        AddMenuItem(menu, "إعدادات الأعمدة…", (_, _) => ShowColumnSettings());
        AddMenuItem(menu, "استعادة الأعمدة الافتراضية", (_, _) => ResetColumnSettings());
        AddMenuItem(menu, "نسخ قيمة الخلية المحددة", (_, _) => CopySelectedCell(definition));
        return menu;
    }
    private void SetColumnVisibility(string key, bool visible)
    {
        var setting = _columnSettings.FirstOrDefault(item => item.Definition.Key == key);
        if (setting is not null) setting.IsVisible = visible;
    }
    private void ShowColumnSettings()
    {
        var owner = Window.GetWindow(this);
        var dialog = new Window
        {
            Title = "إعدادات أعمدة User Manager", Width = 600, Height = 690,
            MinWidth = 520, MinHeight = 540, WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize, FlowDirection = FlowDirection.RightToLeft,
            Background = new SolidColorBrush(Color.FromRgb(244, 246, 248)), Owner = owner
        };
        var root = new Grid { Margin = new Thickness(14) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var title = new TextBlock { Text = "الأعمدة المتاحة (28)", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(32, 36, 42)), Margin = new Thickness(0, 0, 0, 10) };
        Grid.SetRow(title, 0); root.Children.Add(title);

        var filters = new Grid { Margin = new Thickness(0, 0, 0, 9) };
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        filters.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        var search = new TextBox { Height = 34, Padding = new Thickness(8, 4, 8, 4), VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "ابحث عن عمود" };
        var group = new ComboBox { Height = 34, Padding = new Thickness(6, 3, 6, 3), ItemsSource = new[] { "كل المجموعات" }.Concat(ColumnDefinitions.Select(definition => definition.Group).Distinct()).ToList(), SelectedIndex = 0 };
        Grid.SetColumn(search, 0); filters.Children.Add(search); Grid.SetColumn(group, 2); filters.Children.Add(group); Grid.SetRow(filters, 1); root.Children.Add(filters);

        _columnSettingsView = new ListCollectionView(_columnSettings);
        _columnSettingsView.Filter = item => item is ColumnSettingItem setting &&
            (string.IsNullOrWhiteSpace(search.Text) || setting.Definition.Title.Contains(search.Text.Trim(), StringComparison.CurrentCultureIgnoreCase)) &&
            (group.SelectedItem?.ToString() is not string selectedGroup || selectedGroup == "كل المجموعات" || setting.Definition.Group == selectedGroup);
        var list = new ListBox { ItemsSource = _columnSettingsView, AllowDrop = true, HorizontalContentAlignment = HorizontalAlignment.Stretch, BorderBrush = new SolidColorBrush(Color.FromRgb(222, 225, 230)), Background = Brushes.White };
        var template = new DataTemplate(typeof(ColumnSettingItem));
        var rowFactory = new FrameworkElementFactory(typeof(StackPanel)); rowFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal); rowFactory.SetValue(StackPanel.MarginProperty, new Thickness(4, 3, 4, 3));
        var checkFactory = new FrameworkElementFactory(typeof(CheckBox)); checkFactory.SetBinding(CheckBox.IsCheckedProperty, new Binding(nameof(ColumnSettingItem.IsVisible)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }); checkFactory.SetValue(CheckBox.VerticalAlignmentProperty, VerticalAlignment.Center); checkFactory.SetValue(CheckBox.MarginProperty, new Thickness(0, 0, 8, 0)); rowFactory.AppendChild(checkFactory);
        var nameFactory = new FrameworkElementFactory(typeof(TextBlock)); nameFactory.SetBinding(TextBlock.TextProperty, new Binding("Definition.Title")); nameFactory.SetValue(TextBlock.WidthProperty, 230d); nameFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center); nameFactory.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(32, 36, 42))); rowFactory.AppendChild(nameFactory);
        var groupFactory = new FrameworkElementFactory(typeof(TextBlock)); groupFactory.SetBinding(TextBlock.TextProperty, new Binding("Definition.Group")); groupFactory.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(109, 116, 128))); groupFactory.SetValue(TextBlock.FontSizeProperty, 10.5d); groupFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center); rowFactory.AppendChild(groupFactory);
        template.VisualTree = rowFactory; list.ItemTemplate = template;
        Grid.SetRow(list, 2); root.Children.Add(list);

        var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        var widthLine = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) };
        widthLine.Children.Add(new TextBlock { Text = "عرض العمود المحدد", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Foreground = new SolidColorBrush(Color.FromRgb(80, 88, 100)) });
        var widthBox = new TextBox { Width = 86, Height = 31, Text = "120", FlowDirection = FlowDirection.LeftToRight, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(6, 3, 6, 3) };
        var applyWidth = new Button { Content = "تطبيق العرض", Height = 31, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 0, 10, 0) };
        widthLine.Children.Add(widthBox); widthLine.Children.Add(applyWidth); footer.Children.Add(widthLine);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left };
        var reset = new Button { Content = "استعادة الافتراضي", Width = 135, Height = 35, Margin = new Thickness(0, 0, 8, 0), Background = new SolidColorBrush(Color.FromRgb(249, 237, 237)), Foreground = new SolidColorBrush(Color.FromRgb(127, 17, 24)), BorderBrush = new SolidColorBrush(Color.FromRgb(232, 200, 204)) };
        var close = new Button { Content = "إغلاق", Width = 90, Height = 35 };
        buttons.Children.Add(reset); buttons.Children.Add(close); footer.Children.Add(buttons); Grid.SetRow(footer, 3); root.Children.Add(footer);

        list.SelectionChanged += (_, _) =>
        {
            if (list.SelectedItem is ColumnSettingItem setting) widthBox.Text = setting.Width.ToString("0", CultureInfo.CurrentCulture);
        };
        search.TextChanged += (_, _) => _columnSettingsView.Refresh();
        group.SelectionChanged += (_, _) => _columnSettingsView.Refresh();
        applyWidth.Click += (_, _) =>
        {
            if (list.SelectedItem is not ColumnSettingItem setting)
            {
                WorkflowFeedbackWindow.ShowNotice(dialog, "لم يتم اختيار عمود", "اختر عمودًا من القائمة قبل تعديل عرضه.", WorkflowFeedbackKind.Warning, null,
                    new WorkflowFeedbackAction("close", "متابعة", true));
                return;
            }
            if (!double.TryParse(widthBox.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out var width) && !double.TryParse(widthBox.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out width))
            {
                WorkflowFeedbackWindow.ShowNotice(dialog, "قيمة العرض غير صالحة", "أدخل عرضًا رقميًا للعمود مثل 120.", WorkflowFeedbackKind.Warning, null,
                    new WorkflowFeedbackAction("close", "تصحيح القيمة", true));
                return;
            }
            if (width is < 60 or > 600)
            {
                WorkflowFeedbackWindow.ShowNotice(dialog, "العرض خارج النطاق", "يجب أن يكون عرض العمود بين 60 و600 بكسل.", WorkflowFeedbackKind.Warning,
                    [new("القيمة المدخلة", width.ToString("0.##", CultureInfo.CurrentCulture))], new WorkflowFeedbackAction("close", "تصحيح القيمة", true));
                return;
            }
            setting.Width = width; BuildGridColumns();
        };
        reset.Click += (_, _) => ResetColumnSettings();
        close.Click += (_, _) => dialog.Close();
        list.PreviewMouseLeftButtonDown += (_, args) => _dragStartPoint = args.GetPosition(list);
        list.PreviewMouseMove += (_, args) =>
        {
            if (args.LeftButton != MouseButtonState.Pressed) return;
            var position = args.GetPosition(list);
            if (Math.Abs(position.X - _dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(position.Y - _dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            if (list.SelectedItem is ColumnSettingItem item) DragDrop.DoDragDrop(list, item, DragDropEffects.Move);
        };
        list.Drop += (_, args) =>
        {
            if (args.Data.GetData(typeof(ColumnSettingItem)) is not ColumnSettingItem source) return;
            var targetContainer = FindVisualParent<ListBoxItem>(args.OriginalSource as DependencyObject);
            if (targetContainer?.DataContext is not ColumnSettingItem target || ReferenceEquals(source, target)) return;
            var oldIndex = _columnSettings.IndexOf(source); var newIndex = _columnSettings.IndexOf(target);
            if (oldIndex >= 0 && newIndex >= 0) { _columnSettings.Move(oldIndex, newIndex); BuildGridColumns(); _columnSettingsView.Refresh(); }
        };
        dialog.Content = root;
        dialog.ShowDialog();
        _columnSettingsView = null;
    }
    private void ResetColumnSettings()
    {
        foreach (var setting in _columnSettings) setting.PropertyChanged -= ColumnSettingItem_PropertyChanged;
        _columnSettings.Clear();
        foreach (var definition in ColumnDefinitions) AddColumnSetting(definition, definition.DefaultVisible, definition.DefaultWidth);
        _sortKey = null;
        _statusFilter = null;
        _columnFilterKey = null;
        _columnFilterValue = null;
        _archiveFilter = ArchiveFilterMode.ActiveOnly;
        _cardsView.CustomSort = null;
        BuildGridColumns();
        ApplyFilters();
        _columnSettingsView?.Refresh();
    }
    private void ColumnSettingItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isBuildingColumns) return;
        if (e.PropertyName == nameof(ColumnSettingItem.IsVisible) || e.PropertyName == nameof(ColumnSettingItem.Width))
        {
            BuildGridColumns();
            UpdateFooter();
        }
    }
    private void BuildGridColumns()
    {
        if (CardsGrid is null || _columnSettings is null) return;
        _isBuildingColumns = true;
        try
        {
            CardsGrid.Columns.Clear();
            _definitionsByGridColumn.Clear();
            foreach (var setting in _columnSettings.Where(item => item.IsVisible))
            {
                var definition = setting.Definition;
                DataGridColumn column;
                if (definition.Key == "Status")
                {
                    var borderFactory = new FrameworkElementFactory(typeof(Border));
                    borderFactory.SetBinding(Border.BackgroundProperty, new Binding(nameof(ManagedCardRow.StatusBackground)));
                    borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
                    borderFactory.SetValue(Border.PaddingProperty, new Thickness(7, 3, 7, 3));
                    borderFactory.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                    var textFactory = new FrameworkElementFactory(typeof(TextBlock));
                    textFactory.SetBinding(TextBlock.TextProperty, new Binding(nameof(ManagedCardRow.StatusLabel)));
                    textFactory.SetBinding(TextBlock.ForegroundProperty, new Binding(nameof(ManagedCardRow.StatusForeground)));
                    textFactory.SetValue(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                    textFactory.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
                    borderFactory.AppendChild(textFactory);
                    column = new DataGridTemplateColumn { CellTemplate = new DataTemplate { VisualTree = borderFactory } };
                }
                else
                {
                    column = new DataGridTextColumn
                    {
                        Binding = new Binding($"[{definition.Key}]") { Mode = BindingMode.OneWay },
                        ElementStyle = CreateCellTextStyle()
                    };
                }
                column.Header = definition.Title;
                column.Width = new DataGridLength(setting.Width, DataGridLengthUnitType.Pixel);
                column.MinWidth = Math.Min(setting.Width, 65);
                column.CanUserResize = true;
                column.CanUserReorder = true;
                column.CanUserSort = definition.CanSort;
                column.SortMemberPath = definition.Key;
                column.HeaderStringFormat = null;
                _definitionsByGridColumn[column] = definition;
                CardsGrid.Columns.Add(column);
            }
            if (_sortKey is not null)
            {
                var sorted = CardsGrid.Columns.FirstOrDefault(column => _definitionsByGridColumn.TryGetValue(column, out var definition) && definition.Key == _sortKey);
                if (sorted is not null) sorted.SortDirection = _sortDirection;
            }
        }
        finally { _isBuildingColumns = false; }
    }
    private static Style CreateCellTextStyle()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
        style.Setters.Add(new Setter(TextBlock.MarginProperty, new Thickness(7, 0, 7, 0)));
        return style;
    }
}
