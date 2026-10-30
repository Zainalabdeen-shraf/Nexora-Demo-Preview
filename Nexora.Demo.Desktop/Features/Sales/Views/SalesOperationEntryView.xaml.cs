using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using Nexora.Demo.Desktop.Features.Cards.Services;
using Nexora.Demo.Desktop.Features.Sales.Models;
using Nexora.Demo.Desktop.Features.Sales.Services;
using Nexora.Demo.Desktop.Features.Shared;

namespace Nexora.Demo.Desktop.Features.Sales.Views;

/// <summary>
/// Screenshot-matched sales entry workspace. The visible cart is the source of truth; all
/// lines are validated and written through one SalesOperationsService snapshot.
/// </summary>
public partial class SalesOperationEntryView : UserControl
{
    private static readonly JsonSerializerOptions DraftJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
    private static readonly string DraftPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexoraProductDemo", "sales-entry-draft.json");

    private readonly SalesOperationsService _operationsService;
    private readonly SalesPointDirectoryService _pointsService;
    private readonly DemoCardIssuanceService _cardService;
    private readonly ObservableCollection<SalesCartLine> _cartLines = [];
    private bool _initializing;

    public SalesOperationEntryView(
        SalesOperationsService operationsService,
        SalesPointDirectoryService pointsService,
        DemoCardIssuanceService cardService)
    {
        ArgumentNullException.ThrowIfNull(operationsService);
        ArgumentNullException.ThrowIfNull(pointsService);
        ArgumentNullException.ThrowIfNull(cardService);

        _initializing = true;
        InitializeComponent();
        _operationsService = operationsService;
        _pointsService = pointsService;
        _cardService = cardService;
        SaleItemsGrid.ItemsSource = _cartLines;
        DateTimeDisplayTextBlock.Text = DateTime.Now.ToString("yyyy/MM/dd  HH:mm", CultureInfo.InvariantCulture);

        _cardService.ScenarioChanged += Source_Changed;
        _operationsService.Changed += Source_Changed;
        _pointsService.Changed += Source_Changed;

        PopulatePackages();
        PopulatePoints();
        _initializing = false;
        UpdateChannelState();
        TryRestoreDraft();
        UpdateCartView();
        UpdateCharacterCounters();
        UpdatePreview();
    }

    public event EventHandler<FeatureNavigationRequestedEventArgs>? NavigationRequested;

    private void Source_Changed(object? sender, EventArgs e)
    {
        if (_initializing) return;
        PopulatePackages();
        PopulatePoints();
        UpdateChannelState();
        UpdateCartView();
        UpdatePreview();
    }

    private void PopulatePackages()
    {
        var previousBatchId = (PackageComboBox.SelectedItem as PackageChoice)?.BatchId;
        var items = _cardService.Batches
            .Where(batch => batch.Quantity > 0 && !string.Equals(batch.LocalStatus, "مؤرشف", StringComparison.OrdinalIgnoreCase))
            .Select(batch => new PackageChoice(
                batch.PackageName,
                batch.UnitPrice,
                batch.BatchId,
                batch.Quantity,
                batch.TargetSystem,
                _operationsService.GetAvailableBatchQuantity(batch.BatchId, batch.Quantity, batch.TargetSystem)))
            .Where(item => item.AvailableQuantity > 0 || _cartLines.Any(line => string.Equals(line.BatchId, item.BatchId, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenByDescending(item => item.BatchId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        PackageComboBox.ItemsSource = items;
        PackageComboBox.SelectedItem = items.FirstOrDefault(item =>
            !string.IsNullOrWhiteSpace(previousBatchId)
            && string.Equals(item.BatchId, previousBatchId, StringComparison.OrdinalIgnoreCase));
        if (PackageComboBox.SelectedItem is PackageChoice selected)
        {
            UnitPriceTextBox.Text = selected.UnitPrice.ToString("0.##", CultureInfo.CurrentCulture);
        }
        else
        {
            // Never display the first available batch as if the user had selected it.
            // The reference screen starts with an empty search field and zero cart lines.
            PackageComboBox.SelectedIndex = -1;
            PackageComboBox.Text = string.Empty;
            UnitPriceTextBox.Text = "0";
        }

        BatchAvailabilityTextBlock.Text = items.Length == 0
            ? "لا توجد دفعات بكميات متاحة للبيع. أنشئ دفعة في شاشة إصدار الكروت أولًا أو راجع سجل التوريدات والمبيعات."
            : "المتاح محسوب من الدفعة بعد خصم المبيعات والتوريدات السابقة وأصناف هذه القائمة.";
    }

    private void PopulatePoints()
    {
        var points = _pointsService.GetAll().Where(point => point.IsActive).ToArray();
        PointComboBox.ItemsSource = points;
        SelectDefaultPoint(points, "المركز الرئيسي");
    }

    private void SelectDefaultPoint(IReadOnlyList<SalesPointRecord> points, string preferredName)
    {
        var match = points.FirstOrDefault(point => string.Equals(point.Name, preferredName, StringComparison.CurrentCultureIgnoreCase))
            ?? points.FirstOrDefault(point => !point.IsSystemPoint)
            ?? points.FirstOrDefault(point => point.IsSystemPoint);
        if (match is not null) PointComboBox.SelectedItem = match;
    }

    private void ChannelSelectorComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || ChannelSelectorComboBox is null) return;
        if (_cartLines.Count > 0)
        {
            SetValidationState("أكمل أو ألغِ قائمة البيع الحالية قبل تغيير قناة البيع حتى لا تختلط أنواع العمليات.", "warning");
            var previousKey = WalletChannelRadio.IsChecked == true ? "wallet"
                : NetworkCardsChannelRadio.IsChecked == true ? "network-cards" : "direct";
            ChannelSelectorComboBox.SelectedValue = previousKey;
            return;
        }

        var key = ChannelSelectorComboBox.SelectedValue?.ToString() ?? string.Empty;
        _initializing = true;
        DirectChannelRadio.IsChecked = key == "direct";
        NetworkCardsChannelRadio.IsChecked = key == "network-cards";
        WalletChannelRadio.IsChecked = key == "wallet";
        _initializing = false;
        UpdateChannelState();
        UpdatePreview();
    }

    private void Channel_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing || PointComboBox is null) return;
        if (ChannelSelectorComboBox is not null && ChannelSelectorComboBox.SelectedValue?.ToString() != SelectedChannelKey())
            ChannelSelectorComboBox.SelectedValue = SelectedChannelKey();
        UpdateChannelState();
        UpdatePreview();
    }

    private void UpdateChannelState()
    {
        var channel = SelectedChannelKey();
        if (channel is not ("direct" or "network-cards" or "wallet"))
        {
            PointComboBox.ItemsSource = Array.Empty<SalesPointRecord>();
            PointComboBox.SelectedItem = null;
            PointComboBox.IsEnabled = false;
            SelectedPointDisplayTextBlock.Text = "اختر نقطة البيع";
            CollectionModeComboBox.SelectedValue = "paid";
            AmountReceivedLabelTextBlock.Text = "المبلغ المدفوع";
            AmountReceivedTextBox.Text = "0";
            AmountReceivedTextBox.IsEnabled = false;
            PaymentMethodComboBox.SelectedValue = string.Empty;
            PaymentMethodComboBox.IsEnabled = false;
            PrintingCostFieldPanel.Visibility = Visibility.Collapsed;
            PrintingCostTextBox.Text = "0";
            ChannelNoticeTextBlock.Text = "اختر قناة البيع لإكمال بيانات العملية.";
            AddToListButton.IsEnabled = _operationsService.CanWrite;
            UpdateCartView();
            return;
        }

        var channelPoint = channel switch
        {
            "network-cards" => CreateChannelPoint("system-online-network-cards", "SP-ONLINE", "كروت الشبكات", "قناة إلكترونية", "تجميع مركزي"),
            "wallet" => CreateChannelPoint("system-electronic-wallets", "SP-WALLET", "المحافظ الإلكترونية", "قناة إلكترونية", "تجميع مركزي"),
            _ => CreateChannelPoint("system-direct-sales", "SP-DIRECT", "البيع المباشر", "قناة مباشرة", "فوري")
        };
        PointComboBox.ItemsSource = new[] { channelPoint };
        PointComboBox.SelectedItem = channelPoint;
        PointComboBox.IsEnabled = false;
        SelectedPointDisplayTextBlock.Text = channelPoint.Name;
        CollectionModeComboBox.SelectedValue = channel == "direct" ? "paid" : "central";
        AmountReceivedLabelTextBlock.Text = channel == "direct" ? "التحصيل الكامل (يُحسب تلقائيًا)" : "ضمن التجميع المركزي";
        AmountReceivedTextBox.Text = "0";
        AmountReceivedTextBox.IsEnabled = false;

        var currentPaymentMethod = PaymentMethodComboBox.SelectedValue?.ToString();
        foreach (var item in PaymentMethodComboBox.Items.OfType<ComboBoxItem>())
            item.IsEnabled = channel switch
            {
                "direct" => item.Tag?.ToString() is "نقدي" or "تحويل بنكي",
                "wallet" => item.Tag?.ToString() == "محفظة إلكترونية",
                _ => item.Tag?.ToString() == "كروت الشبكات / تجميع مركزي"
            };
        PaymentMethodComboBox.SelectedValue = channel switch
        {
            "wallet" => "محفظة إلكترونية",
            "network-cards" => "كروت الشبكات / تجميع مركزي",
            _ => currentPaymentMethod is "نقدي" or "تحويل بنكي" ? currentPaymentMethod : "نقدي"
        };
        PaymentMethodComboBox.IsEnabled = channel == "direct";
        PaymentMethodFieldPanel.Visibility = Visibility.Visible;
        PrintingCostFieldPanel.Visibility = Visibility.Visible;
        PrintingCostTextBox.IsEnabled = channel == "direct";
        if (channel != "direct") PrintingCostTextBox.Text = "0";

        ChannelNoticeBorder.Background = new SolidColorBrush(channel is "wallet" or "network-cards"
            ? Color.FromRgb(237, 243, 255) : Color.FromRgb(247, 248, 251));
        ChannelNoticeBorder.BorderBrush = new SolidColorBrush(channel is "wallet" or "network-cards"
            ? Color.FromRgb(198, 212, 245) : Color.FromRgb(224, 229, 236));
        ChannelNoticeTextBlock.Text = channel switch
        {
            "wallet" => "تُحتسب عمولة المحافظ 10٪ تلقائيًا. تُسجّل القيمة ضمن التجميع المركزي ولا تعني أن التحويل وصل فعليًا.",
            "network-cards" => "تُسجّل العملية تحت كروت الشبكات ضمن التجميع المركزي؛ لا توجد تكلفة طباعة ورقية لهذه القناة.",
            _ => "البيع المباشر يُسجّل محصلًا بالكامل. اختر وسيلة الدفع وأدخل تكلفة التجهيز لكل كرت إن وجدت."
        };
        NextReferenceTextBlock.Text = "يُنشأ المرجع الداخلي بعد التسجيل";
    }

    private void PackageComboBox_Loaded(object sender, RoutedEventArgs e) => AttachPackageSearchInput();

    private void AttachPackageSearchInput()
    {
        PackageComboBox.ApplyTemplate();
        if (PackageComboBox.Template.FindName("PART_EditableTextBox", PackageComboBox) is not TextBox input) return;
        input.TextChanged -= PackageSearchTextChanged;
        input.TextChanged += PackageSearchTextChanged;
        input.KeyDown -= EditablePackageSearch_KeyDown;
        input.KeyDown += EditablePackageSearch_KeyDown;
    }

    private void PackageSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_initializing) return;
        // Keep the primary action visually available like the approved reference; the click handler validates missing selections safely.
        AddToListButton.IsEnabled = _operationsService.CanWrite;
    }

    private void ScannerButton_Click(object sender, RoutedEventArgs e)
    {
        AttachPackageSearchInput();
        PackageComboBox.Focus();
        if (PackageComboBox.Template.FindName("PART_EditableTextBox", PackageComboBox) is TextBox input)
        {
            input.Focus();
            input.SelectAll();
        }
        SetValidationState("تم تفعيل حقل البحث؛ يمكن لقارئ الباركود إدخال رقم الدفعة ثم الضغط على Enter لتحديدها.", "info");
    }

    private void EditablePackageSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox searchBox) return;
        var text = searchBox.Text?.Trim() ?? string.Empty;
        var match = PackageComboBox.Items.OfType<PackageChoice>().FirstOrDefault(item =>
            string.Equals(item.BatchId, text, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Name, text, StringComparison.CurrentCultureIgnoreCase)
            || item.DisplayLabel.StartsWith(text, StringComparison.CurrentCultureIgnoreCase));
        if (match is null)
        {
            SetValidationState("لم تُطابق قراءة الباركود أي دفعة متاحة. تحقق من رقم الدفعة أو اخترها من القائمة.", "warning");
            e.Handled = true;
            return;
        }
        PackageComboBox.SelectedItem = match;
        PackageComboBox.IsDropDownOpen = false;
        e.Handled = true;
    }

    private void Package_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || PackageComboBox is null) return;
        if (PackageComboBox.SelectedItem is PackageChoice choice)
        {
            UnitPriceTextBox.Text = choice.UnitPrice.ToString("0.##", CultureInfo.CurrentCulture);
            QuantityTextBox.Text = "1";
            DiscountAmountTextBox.Text = "0";
        }
        UpdateCartView();
        UpdatePreview();
    }

    private void Inputs_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_initializing) UpdatePreview();
    }

    private void Inputs_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateCharacterCounters();
        if (!_initializing) UpdatePreview();
    }

    private void UpdateCharacterCounters()
    {
        if (NotesCounterTextBlock is not null && NotesTextBox is not null)
            NotesCounterTextBlock.Text = $"{NotesTextBox.Text.Length.ToString(CultureInfo.InvariantCulture)} / 500";
        if (PaymentNotesCounterTextBlock is not null && PaymentNotesTextBox is not null)
            PaymentNotesCounterTextBlock.Text = $"{PaymentNotesTextBox.Text.Length.ToString(CultureInfo.InvariantCulture)} / 250";
    }

    private void AddToListButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedChannelKey() is not ("direct" or "network-cards" or "wallet"))
        {
            ShowError("اختر قناة البيع أولًا.");
            ChannelSelectorComboBox.Focus();
            return;
        }
        var package = ResolveSelectedPackage();
        if (package is null)
        {
            ShowError("اختر دفعة من نتائج البحث أولًا؛ لا يمكن إضافة نص بحث غير مطابق لدفعة متاحة.");
            PackageComboBox.Focus();
            return;
        }
        if (!int.TryParse(QuantityTextBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var quantity) || quantity < 1 || quantity > 100_000)
        {
            ShowError("أدخل كمية صحيحة أكبر من صفر.");
            QuantityTextBox.Focus();
            return;
        }
        if (!TryReadDecimal(UnitPriceTextBox.Text, out var price) || price <= 0m || price > 100_000_000m)
        {
            ShowError("أدخل سعر وحدة صحيحًا أكبر من صفر.");
            UnitPriceTextBox.Focus();
            return;
        }
        if (!TryReadDecimal(DiscountAmountTextBox.Text, out var discount) || discount < 0m || discount > quantity * price)
        {
            ShowError("خصم البند يجب أن يكون صفرًا أو قيمة موجبة لا تتجاوز إجمالي البند.");
            DiscountAmountTextBox.Focus();
            return;
        }
        if (!TryReadDecimal(PrintingCostTextBox.Text, out var printingCost) || printingCost < 0m || printingCost > price)
        {
            ShowError("تكلفة الطباعة يجب أن تكون صفرًا أو أقل من سعر الكرت.");
            PrintingCostTextBox.Focus();
            return;
        }

        var available = _operationsService.GetAvailableBatchQuantity(package.BatchId, package.BatchQuantity, package.TargetSystem);
        var alreadyQueued = _cartLines.Where(line => string.Equals(line.BatchId, package.BatchId, StringComparison.OrdinalIgnoreCase)).Sum(line => line.Quantity);
        var remaining = Math.Max(0, available - alreadyQueued);
        if (quantity > remaining)
        {
            ShowError($"الكمية المطلوبة أكبر من المتاح في الدفعة {package.BatchId}. المتاح بعد أصناف القائمة: {remaining:N0} كرت.");
            UpdateCartView();
            return;
        }

        _cartLines.Add(new SalesCartLine(
            _cartLines.Count + 1,
            package.Name,
            $"{package.Name} · {package.BatchId}",
            quantity,
            price,
            discount,
            SelectedChannelKey() == "direct" ? printingCost : 0m,
            package.BatchId,
            package.BatchQuantity,
            package.TargetSystem));

        ChannelSelectorComboBox.IsEnabled = false;
        QuantityTextBox.Text = "1";
        UnitPriceTextBox.Text = package.UnitPrice.ToString("0.##", CultureInfo.CurrentCulture);
        DiscountAmountTextBox.Text = "0";
        UpdateCartView();
        UpdatePreview();
    }

    private void EditCartItemButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SalesCartLine line }) return;

        var dialog = new Window
        {
            Title = "تعديل صنف البيع",
            Width = 510,
            Height = 285,
            MinWidth = 510,
            MinHeight = 285,
            MaxWidth = 510,
            MaxHeight = 285,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            ShowInTaskbar = false,
            Background = Brushes.White,
            FontFamily = new FontFamily("Tahoma"),
            FlowDirection = FlowDirection.RightToLeft
        };
        var layout = new Grid { Margin = new Thickness(18) };
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(14) });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var title = new TextBlock
        {
            Text = line.ProductName + " · " + line.BatchId,
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(49, 55, 67)),
            TextWrapping = TextWrapping.Wrap
        };
        Grid.SetRow(title, 0);
        layout.Children.Add(title);

        var fields = new Grid { FlowDirection = FlowDirection.RightToLeft };
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var quantityField = CreateEditField("الكمية", line.Quantity.ToString(CultureInfo.CurrentCulture));
        var priceField = CreateEditField("سعر الوحدة (ريال)", line.UnitPrice.ToString("0.##", CultureInfo.CurrentCulture));
        var discountField = CreateEditField("خصم البند (ريال)", line.DiscountAmount.ToString("0.##", CultureInfo.CurrentCulture));
        var quantityInput = (TextBox)quantityField.Children[1];
        var priceInput = (TextBox)priceField.Children[1];
        var discountInput = (TextBox)discountField.Children[1];
        Grid.SetColumn(quantityField, 0);
        Grid.SetColumn(priceField, 2);
        Grid.SetColumn(discountField, 4);
        fields.Children.Add(quantityField);
        fields.Children.Add(priceField);
        fields.Children.Add(discountField);
        Grid.SetRow(fields, 2);
        layout.Children.Add(fields);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, FlowDirection = FlowDirection.RightToLeft };
        var save = new Button
        {
            Content = "حفظ التعديلات",
            MinWidth = 125,
            Height = 36,
            Padding = new Thickness(14, 0, 14, 0),
            Background = new SolidColorBrush(Color.FromRgb(159, 24, 37)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(159, 24, 37)),
            BorderThickness = new Thickness(1),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 8, 0),
            IsDefault = true
        };
        var cancel = new Button
        {
            Content = "إلغاء",
            MinWidth = 85,
            Height = 36,
            Padding = new Thickness(14, 0, 14, 0),
            Background = Brushes.White,
            Foreground = new SolidColorBrush(Color.FromRgb(64, 72, 84)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 225, 232)),
            BorderThickness = new Thickness(1),
            IsCancel = true
        };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        Grid.SetRow(buttons, 4);
        layout.Children.Add(buttons);
        dialog.Content = layout;

        save.Click += (_, _) =>
        {
            if (!int.TryParse(quantityInput.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var newQuantity) || newQuantity < 1 || newQuantity > 100_000)
            {
                ShowError("أدخل كمية صحيحة بين 1 و100,000 كرت.");
                quantityInput.Focus();
                return;
            }
            if (!TryReadDecimal(priceInput.Text, out var newPrice) || newPrice <= 0m || newPrice > 100_000_000m)
            {
                ShowError("أدخل سعر وحدة أكبر من صفر.");
                priceInput.Focus();
                return;
            }
            if (!TryReadDecimal(discountInput.Text, out var newDiscount) || newDiscount < 0m || newDiscount > newQuantity * newPrice)
            {
                ShowError("خصم البند يجب أن يكون بين صفر وإجمالي قيمة البند.");
                discountInput.Focus();
                return;
            }
            if (line.PrintingCostPerCard > newPrice)
            {
                ShowError("سعر الوحدة الجديد أقل من تكلفة الطباعة المحفوظة لهذا البند.");
                priceInput.Focus();
                return;
            }
            var available = _operationsService.GetAvailableBatchQuantity(line.BatchId, line.SourceBatchQuantity, line.TargetSystem);
            var otherQueued = _cartLines.Where(item => !ReferenceEquals(item, line)
                && string.Equals(item.BatchId, line.BatchId, StringComparison.OrdinalIgnoreCase)).Sum(item => item.Quantity);
            if (newQuantity > Math.Max(0, available - otherQueued))
            {
                ShowError($"المتاح من الدفعة بعد احتساب الأصناف الأخرى هو {Math.Max(0, available - otherQueued):N0} كرت فقط.");
                quantityInput.Focus();
                return;
            }
            dialog.Tag = line with { Quantity = newQuantity, UnitPrice = newPrice, DiscountAmount = newDiscount };
            dialog.DialogResult = true;
        };

        if (dialog.ShowDialog() == true && dialog.Tag is SalesCartLine editedLine)
        {
            var index = _cartLines.IndexOf(line);
            if (index >= 0) _cartLines[index] = editedLine;
            UpdateCartView();
            UpdatePreview();
        }
    }

    private static StackPanel CreateEditField(string label, string value)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(89, 97, 109)),
            Margin = new Thickness(0, 0, 0, 6)
        });
        panel.Children.Add(new TextBox
        {
            Text = value,
            Height = 36,
            Padding = new Thickness(8, 4, 8, 4),
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            FlowDirection = FlowDirection.LeftToRight
        });
        return panel;
    }

    private void RemoveCartItemButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: SalesCartLine line }) return;
        _cartLines.Remove(line);
        RenumberCartLines();
        if (_cartLines.Count == 0) ChannelSelectorComboBox.IsEnabled = true;
        UpdateCartView();
        UpdatePreview();
    }

    private void RenumberCartLines()
    {
        for (var index = 0; index < _cartLines.Count; index++)
            _cartLines[index] = _cartLines[index] with { Number = index + 1 };
    }

    private void UpdateCartView()
    {
        if (SaleItemsGrid is null) return;
        EmptyStatePanel.Visibility = _cartLines.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CartResultCountTextBlock.Text = $"{_cartLines.Count:N0} صنف";
        CartFooterTextBlock.Text = _cartLines.Count == 0
            ? "لا توجد أصناف"
            : $"{_cartLines.Sum(item => item.Quantity):N0} كرت · {_cartLines.Sum(item => item.Total):N0} ريال بعد الخصم";
        ChannelSelectorComboBox.IsEnabled = _cartLines.Count == 0;

        if (PackageComboBox.SelectedItem is PackageChoice selected)
        {
            var available = _operationsService.GetAvailableBatchQuantity(selected.BatchId, selected.BatchQuantity, selected.TargetSystem);
            var queued = _cartLines.Where(item => string.Equals(item.BatchId, selected.BatchId, StringComparison.OrdinalIgnoreCase)).Sum(item => item.Quantity);
            BatchAvailabilityTextBlock.Text = $"الدفعة {selected.BatchId} · {selected.TargetSystem} · المتاح بعد هذه القائمة {Math.Max(0, available - queued):N0} كرت.";
        }
        else if (_cartLines.Count == 0)
        {
            BatchAvailabilityTextBlock.Text = "لا توجد دفعات متاحة؛ أنشئ دفعة من شاشة إصدار الكروت أولًا.";
        }

        AddToListButton.IsEnabled = _operationsService.CanWrite;
    }

    private PackageChoice? ResolveSelectedPackage()
    {
        var query = PackageComboBox.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query)) return PackageComboBox.SelectedItem as PackageChoice;
        var match = PackageComboBox.Items.OfType<PackageChoice>().FirstOrDefault(item =>
            string.Equals(item.BatchId, query, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Name, query, StringComparison.CurrentCultureIgnoreCase)
            || string.Equals(item.DisplayLabel, query, StringComparison.CurrentCultureIgnoreCase));
        if (match is not null) PackageComboBox.SelectedItem = match;
        return match;
    }

    private bool CanAddSelectedLine()
    {
        if (SelectedChannelKey() is not ("direct" or "network-cards" or "wallet")) return false;
        if (PackageComboBox.SelectedItem is not PackageChoice package) return false;
        var query = PackageComboBox.Text?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(query)
            && !string.Equals(query, package.DisplayLabel, StringComparison.CurrentCultureIgnoreCase)
            && !string.Equals(query, package.Name, StringComparison.CurrentCultureIgnoreCase)
            && !string.Equals(query, package.BatchId, StringComparison.OrdinalIgnoreCase)) return false;
        if (!int.TryParse(QuantityTextBox.Text, NumberStyles.Integer, CultureInfo.CurrentCulture, out var quantity) || quantity < 1 || quantity > 100_000) return false;
        if (!TryReadDecimal(UnitPriceTextBox.Text, out var price) || price <= 0m || price > 100_000_000m) return false;
        if (!TryReadDecimal(DiscountAmountTextBox.Text, out var discount) || discount < 0m || discount > quantity * price) return false;
        if (!TryReadDecimal(PrintingCostTextBox.Text, out var printingCost) || printingCost < 0m || printingCost > price) return false;
        var available = _operationsService.GetAvailableBatchQuantity(package.BatchId, package.BatchQuantity, package.TargetSystem);
        var queued = _cartLines.Where(item => string.Equals(item.BatchId, package.BatchId, StringComparison.OrdinalIgnoreCase)).Sum(item => item.Quantity);
        return quantity <= Math.Max(0, available - queued);
    }

    private void UpdatePreview()
    {
        if (_initializing || PreviewGrossTextBlock is null) return;
        var channel = SelectedChannelKey();
        var gross = _cartLines.Sum(item => item.GrossTotal);
        var discountTotal = _cartLines.Sum(item => item.DiscountAmount);
        var commissionRate = channel == "wallet" ? 10m : 0m;
        var commission = _cartLines.Sum(item => Math.Round((item.GrossTotal - item.DiscountAmount) * commissionRate / 100m, 2, MidpointRounding.AwayFromZero));
        var net = gross - discountTotal - commission;
        var printingTotal = channel == "direct" ? _cartLines.Sum(item => item.Quantity * item.PrintingCostPerCard) : 0m;
        var received = channel == "direct" ? net : 0m;

        PreviewGrossTextBlock.Text = $"{gross:N2} ريال";
        DiscountSummaryTextBlock.Text = $"{discountTotal:N2} ريال";
        PreviewCommissionTextBlock.Text = $"{commission:N2} ريال ({commissionRate:0.##}٪)";
        PreviewNetTextBlock.Text = $"{net:N2} ريال";
        PreviewReceivedTextBlock.Text = $"{received:N2}";
        PreviewBalanceTextBlock.Text = channel is "network-cards" or "wallet" ? "تجميع مركزي" : "0.00 ريال";
        PreviewPrintingCostTextBlock.Text = $"{printingTotal:N2} ريال";
        PreviewProfitTextBlock.Text = $"{net - printingTotal:N2} ريال";

        var invalidLines = GetCartValidationMessage();
        if (string.IsNullOrWhiteSpace(invalidLines))
        {
            SetValidationState(_cartLines.Count == 0
                ? "اختر دفعة وأضف صنفًا إلى القائمة لبدء عملية البيع."
                : channel == "wallet"
                    ? "عمولة المحفظة 10٪ وتُسجل القيمة ضمن التجميع المركزي."
                    : channel == "network-cards"
                        ? "قيمة البيع تُسجل ضمن تجميع الشركة ولا تُعد تحصيلًا فعليًا مؤكدًا."
                        : "راجع الأصناف والقيم ثم اضغط تأكيد وتسجيل البيع.", _cartLines.Count == 0 ? "info" : "success");
        }
        else
        {
            SetValidationState(invalidLines, "warning");
        }

        // The reference keeps the red confirmation action visible even with an empty cart.
        // SaveOperationButton_Click rejects empty/invalid carts before writing anything.
        SaveOperationButton.IsEnabled = _operationsService.CanWrite && _pointsService.CanWrite;
        AddToListButton.IsEnabled = _operationsService.CanWrite;
    }

    private string GetCartValidationMessage()
    {
        if (!_operationsService.CanWrite)
            return _operationsService.StorageWarning ?? "الحفظ متوقف لحماية سجل المبيعات المحلي.";
        if (_cartLines.Count == 0) return string.Empty;
        if (SelectedChannelKey() is not ("direct" or "network-cards" or "wallet"))
            return "اختر قناة البيع قبل تأكيد العملية.";
        var combinedNoteLength = string.Join(" | ", new[] { NotesTextBox.Text?.Trim(), PaymentNotesTextBox.Text?.Trim() }
            .Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal)).Length;
        if ((NotesTextBox.Text?.Length ?? 0) > 500)
            return "ملاحظات العملية تتجاوز الحد المسموح وهو 500 حرف.";
        if ((PaymentNotesTextBox.Text?.Length ?? 0) > 250)
            return "ملاحظات الدفع تتجاوز الحد المسموح وهو 250 حرفًا.";
        if (combinedNoteLength > 500)
            return "إجمالي الملاحظات يتجاوز الحد المسموح وهو 500 حرف.";
        if ((ExternalReferenceTextBox.Text?.Length ?? 0) > 100)
            return "رقم المرجع الخارجي يتجاوز الحد المسموح وهو 100 حرف.";
        var channel = SelectedChannelKey();
        foreach (var lineItem in _cartLines)
        {
            if (lineItem.DiscountAmount < 0m || lineItem.DiscountAmount > lineItem.GrossTotal)
                return $"خصم البند {lineItem.ProductName} غير صالح.";
        }
        foreach (var group in _cartLines.GroupBy(item => item.BatchId, StringComparer.OrdinalIgnoreCase))
        {
            var line = group.First();
            var available = _operationsService.GetAvailableBatchQuantity(line.BatchId, line.SourceBatchQuantity, line.TargetSystem);
            var requested = group.Sum(item => item.Quantity);
            if (requested > available)
                return $"الكمية في الدفعة {line.BatchId} لم تعد متاحة بالكامل؛ المطلوب {requested:N0} والمتاح الآن {available:N0} كرت.";
        }
        if (channel == "direct" && PaymentMethodComboBox.SelectedValue?.ToString() is not ("نقدي" or "تحويل بنكي"))
            return "اختر وسيلة دفع صالحة للبيع المباشر.";
        return string.Empty;
    }

    private void SaveOperationButton_Click(object sender, RoutedEventArgs e)
    {
        if (_cartLines.Count == 0)
        {
            ShowError("أضف صنفًا واحدًا على الأقل إلى قائمة البيع.");
            return;
        }
        if (PointComboBox.SelectedItem is not SalesPointRecord point)
        {
            ShowError("تعذر تحديد قناة ونقطة البيع. اختر قناة صالحة ثم أعد المحاولة.");
            return;
        }
        var validationMessage = GetCartValidationMessage();
        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            ShowError(validationMessage);
            UpdatePreview();
            return;
        }

        var channel = SelectedChannelKey();
        var channelLabel = channel switch
        {
            "direct" => "بيع مباشر",
            "network-cards" => "كروت الشبكات",
            "wallet" => "محافظ إلكترونية",
            _ => "بيع"
        };
        var mode = channel == "direct" ? "paid" : "central";
        var payment = PaymentMethodComboBox.SelectedValue?.ToString() ?? "نقدي";
        var noteParts = new[] { NotesTextBox.Text?.Trim(), PaymentNotesTextBox.Text?.Trim() }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var combinedNotes = string.Join(" | ", noteParts);
        if (combinedNotes.Length > 500)
        {
            ShowError("إجمالي الملاحظات يتجاوز 500 حرف. اختصر الملاحظات أو وزّعها ثم أعد المحاولة.");
            return;
        }

        var allocatedWithinCart = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var drafts = _cartLines.Select(line =>
        {
            if (!allocatedWithinCart.TryGetValue(line.BatchId, out var assignedInCart))
            {
                assignedInCart = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                allocatedWithinCart[line.BatchId] = assignedInCart;
            }

            var alreadyAllocated = _operationsService.GetAllocatedCardNumbers(line.BatchId);
            var cardNumbers = _cardService.Cards
                .Where(card => string.Equals(card.BatchId, line.BatchId, StringComparison.OrdinalIgnoreCase) && !card.IsArchived)
                .OrderBy(card => card.CreatedAt)
                .ThenBy(card => card.SerialNumber, StringComparer.Ordinal)
                .Where(card => !alreadyAllocated.Contains(card.CardNumber) && !assignedInCart.Contains(card.CardNumber))
                .Take(line.Quantity)
                .Select(card => card.CardNumber)
                .ToArray();
            if (cardNumbers.Length != line.Quantity)
                throw new InvalidOperationException($"لا توجد هويات كافية غير مخصصة في الدفعة {line.BatchId} لربط الكمية المطلوبة.");
            foreach (var cardNumber in cardNumbers)
                assignedInCart.Add(cardNumber);

            return new SalesOperationDraft(
                channel,
                channelLabel,
                point.Id,
                point.Name,
                line.ProductName,
                line.Quantity,
                line.UnitPrice,
                mode,
                0m,
                payment,
                channel == "direct" ? line.PrintingCostPerCard : 0m,
                ExternalReferenceTextBox.Text?.Trim() ?? string.Empty,
                combinedNotes,
                line.BatchId,
                line.SourceBatchQuantity,
                line.DiscountAmount,
                line.TargetSystem,
                cardNumbers);
        }).ToArray();
        try
        {
            var saved = _operationsService.RecordOperations(drafts);
            var totalGross = saved.Sum(item => item.GrossAmount);
            var totalDiscount = saved.Sum(item => item.DiscountAmount);
            var totalCommission = saved.Sum(item => item.CommissionAmount);
            var totalNet = saved.Sum(item => item.NetworkAmount);
            var totalQuantity = saved.Sum(item => item.Quantity);
            var referenceList = string.Join("، ", saved.Select(item => item.Reference));
            MessageBox.Show(Window.GetWindow(this),
                $"تم تسجيل عملية البيع بنجاح.\n\nعدد الأصناف: {saved.Count:N0}\nعدد الكروت: {totalQuantity:N0}\nالمراجع: {referenceList}\nالإجمالي قبل الخصم: {totalGross:N2} ريال\nإجمالي الخصم: {totalDiscount:N2} ريال\nالعمولة: {totalCommission:N2} ريال\nصافي المستحق: {totalNet:N2} ريال\nالحالة: {(channel == "direct" ? "محصل بالكامل" : "ضمن تجميع الشركة")}",
                "تم تسجيل البيع", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.RtlReading);

            _cartLines.Clear();
            ExternalReferenceTextBox.Clear();
            NotesTextBox.Clear();
            PaymentNotesTextBox.Clear();
            QuantityTextBox.Text = "1";
            DiscountAmountTextBox.Text = "0";
            PackageComboBox.SelectedItem = null;
            PackageComboBox.SelectedIndex = -1;
            PackageComboBox.Text = string.Empty;
            UnitPriceTextBox.Text = "0";
            try
            {
                if (File.Exists(DraftPath)) File.Delete(DraftPath);
            }
            catch (Exception draftEx) when (draftEx is IOException or UnauthorizedAccessException)
            {
                SetValidationState("تم تسجيل البيع بنجاح، لكن تعذر حذف ملف المسودة القديم. " + draftEx.Message, "warning");
            }
            UpdateCartView();
            UpdatePreview();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException or JsonException or OverflowException)
        {
            ShowError("لم يتم تسجيل أي سطر من الفاتورة؛ تم التحقق من جميع الأصناف قبل الحفظ. راجع البيانات أو سجل الحفظ.\n\n" + ex.Message);
        }
    }

    private void SaveDraftButton_Click(object sender, RoutedEventArgs e)
    {
        var draft = new SavedSaleDraft(
            SelectedChannelKey(),
            ExternalReferenceTextBox.Text?.Trim() ?? string.Empty,
            NotesTextBox.Text ?? string.Empty,
            PaymentNotesTextBox.Text ?? string.Empty,
            PaymentMethodComboBox.SelectedValue?.ToString() ?? "نقدي",
            _cartLines.Select(line => new SavedDraftLine(
                line.BatchId, line.Quantity, line.UnitPrice, line.DiscountAmount, line.PrintingCostPerCard, line.SourceBatchQuantity, line.ProductName)).ToList());
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DraftPath)!);
            var json = JsonSerializer.Serialize(draft, DraftJsonOptions);
            File.WriteAllText(DraftPath, json);
            SetValidationState("تم حفظ مسودة البيع محليًا؛ يمكن استعادتها عند فتح شاشة تسجيل البيع مرة أخرى.", "success");
            MessageBox.Show(Window.GetWindow(this), "تم حفظ مسودة البيع محليًا بنجاح.", "حفظ مسودة", MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.RtlReading);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            ShowError("تعذر حفظ المسودة على الجهاز.\n\n" + ex.Message);
        }
    }

    private void TryRestoreDraft()
    {
        if (!File.Exists(DraftPath)) return;
        try
        {
            var json = File.ReadAllText(DraftPath);
            var draft = JsonSerializer.Deserialize<SavedSaleDraft>(json, DraftJsonOptions);
            // An empty draft must not restore a channel or leave the form looking pre-filled.
            // Preserve meaningful saved carts, but keep the reference screen clean when no lines exist.
            if (draft is null || draft.Lines is null || draft.Lines.Count == 0) return;

            _initializing = true;
            ChannelSelectorComboBox.SelectedValue = draft.ChannelKey is "network-cards" or "wallet" ? draft.ChannelKey : "direct";
            DirectChannelRadio.IsChecked = draft.ChannelKey == "direct" || draft.ChannelKey is not ("network-cards" or "wallet");
            NetworkCardsChannelRadio.IsChecked = draft.ChannelKey == "network-cards";
            WalletChannelRadio.IsChecked = draft.ChannelKey == "wallet";
            ExternalReferenceTextBox.Text = draft.ExternalReference ?? string.Empty;
            NotesTextBox.Text = draft.Notes ?? string.Empty;
            PaymentNotesTextBox.Text = draft.PaymentNotes ?? string.Empty;
            _initializing = false;
            UpdateChannelState();

            var missingLines = new List<string>();
            foreach (var savedLine in draft.Lines ?? [])
            {
                var package = PackageComboBox.Items.OfType<PackageChoice>().FirstOrDefault(item =>
                    string.Equals(item.BatchId, savedLine.BatchId, StringComparison.OrdinalIgnoreCase));
                if (package is null)
                {
                    missingLines.Add(savedLine.BatchId);
                    continue;
                }
                _cartLines.Add(new SalesCartLine(
                    _cartLines.Count + 1,
                    package.Name,
                    $"{package.Name} · {package.BatchId}",
                    savedLine.Quantity,
                    savedLine.UnitPrice,
                    savedLine.DiscountAmount,
                    SelectedChannelKey() == "direct" ? savedLine.PrintingCostPerCard : 0m,
                    package.BatchId,
                    savedLine.SourceBatchQuantity > 0 ? savedLine.SourceBatchQuantity : package.BatchQuantity,
                    package.TargetSystem));
            }
            PaymentMethodComboBox.SelectedValue = SelectedChannelKey() switch
            {
                "wallet" => "محفظة إلكترونية",
                "network-cards" => "كروت الشبكات / تجميع مركزي",
                _ => draft.PaymentMethod is "نقدي" or "تحويل بنكي" ? draft.PaymentMethod : "نقدي"
            };
            ChannelSelectorComboBox.IsEnabled = _cartLines.Count == 0;
            if (missingLines.Count > 0)
                SetValidationState("استُعيدت المسودة، لكن بعض الدفعات لم تعد موجودة: " + string.Join("، ", missingLines), "warning");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            SetValidationState("تعذر استعادة مسودة البيع؛ ملف المسودة لم يُحذف تلقائيًا. " + ex.Message, "warning");
        }
        finally
        {
            _initializing = false;
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_cartLines.Count > 0 || !string.IsNullOrWhiteSpace(ExternalReferenceTextBox.Text)
            || !string.IsNullOrWhiteSpace(NotesTextBox.Text) || !string.IsNullOrWhiteSpace(PaymentNotesTextBox.Text))
        {
            var answer = MessageBox.Show(Window.GetWindow(this), "هل تريد إلغاء بيانات البيع الحالية ومسح القائمة؟", "إلغاء البيع",
                MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No, MessageBoxOptions.RtlReading);
            if (answer != MessageBoxResult.Yes) return;
        }
        _cartLines.Clear();
        ExternalReferenceTextBox.Clear();
        NotesTextBox.Clear();
        PaymentNotesTextBox.Clear();
        QuantityTextBox.Text = "1";
        DiscountAmountTextBox.Text = "0";
        try
        {
            if (File.Exists(DraftPath)) File.Delete(DraftPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError("تم إلغاء نموذج البيع، لكن تعذر حذف ملف المسودة المحفوظ. " + ex.Message);
        }
        ChannelSelectorComboBox.IsEnabled = true;
        UpdateCartView();
        UpdatePreview();
    }

    private void OpenLedgerButton_Click(object sender, RoutedEventArgs e)
        => NavigationRequested?.Invoke(this, new FeatureNavigationRequestedEventArgs("sales", "سجل العمليات", string.Empty, "none"));

    private string SelectedChannelKey()
    {
        var selected = ChannelSelectorComboBox?.SelectedValue?.ToString();
        if (selected == "direct") return "direct";
        if (selected == "network-cards") return "network-cards";
        if (selected == "wallet") return "wallet";
        return string.Empty;
    }

    private static SalesPointRecord CreateChannelPoint(string id, string code, string name, string pointType, string settlementCycle)
    {
        var now = DateTimeOffset.Now;
        return new SalesPointRecord(id, code, name, pointType, string.Empty, string.Empty, string.Empty,
            "قناة نظام ثابتة؛ ليست نقطة بيع ميدانية قابلة للتعديل.", true, now, now, 0m, false, settlementCycle, true);
    }

    private static bool TryReadDecimal(string? value, out decimal result)
        => decimal.TryParse(value?.Trim(), NumberStyles.Number, CultureInfo.CurrentCulture, out result)
           || decimal.TryParse(value?.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out result);

    private void SetValidationState(string message, string state)
    {
        if (ValidationTextBlock is null) return;
        ValidationTextBlock.Text = message;
        var palette = state switch
        {
            "success" => (Background: Color.FromRgb(235, 247, 240), Border: Color.FromRgb(190, 224, 205), Foreground: Color.FromRgb(33, 112, 75)),
            "info" => (Background: Color.FromRgb(237, 243, 255), Border: Color.FromRgb(198, 212, 245), Foreground: Color.FromRgb(49, 82, 139)),
            "error" => (Background: Color.FromRgb(253, 236, 238), Border: Color.FromRgb(241, 190, 196), Foreground: Color.FromRgb(151, 30, 48)),
            _ => (Background: Color.FromRgb(255, 246, 232), Border: Color.FromRgb(235, 208, 157), Foreground: Color.FromRgb(132, 89, 0))
        };
        ValidationStatusBorder.Background = new SolidColorBrush(palette.Background);
        ValidationStatusBorder.BorderBrush = new SolidColorBrush(palette.Border);
        ValidationTextBlock.Foreground = new SolidColorBrush(palette.Foreground);
        if (state is "error" or "warning") BatchAvailabilityTextBlock.Text = message;
    }

    private static void ShowError(string message)
        => MessageBox.Show(message, "تسجيل المبيعات", MessageBoxButton.OK, MessageBoxImage.Warning, MessageBoxResult.OK, MessageBoxOptions.RtlReading);

    private sealed record PackageChoice(string Name, decimal UnitPrice, string BatchId, int BatchQuantity, string TargetSystem, int AvailableQuantity)
    {
        public string DisplayLabel => $"{Name} · {BatchId} · متاح {AvailableQuantity:N0} · {UnitPrice:N0} ريال";
    }

    private void BatchCatalogAddButton_Click(object sender, RoutedEventArgs e)
{
    if (sender is not FrameworkElement element ||
        element.DataContext is not PackageChoice choice)
        return;

    if (!AddToListButton.IsEnabled)
        return;

    PackageComboBox.SelectedItem = choice;
    PackageComboBox.Text = choice.DisplayLabel;
    PackageComboBox.IsDropDownOpen = false;

    QuantityTextBox.Text = "1";
    UnitPriceTextBox.Text =
        choice.UnitPrice.ToString("0.##", CultureInfo.CurrentCulture);
    DiscountAmountTextBox.Text = "0";

    AddToListButton_Click(AddToListButton, e);
}

    private sealed record SalesCartLine(
        int Number,
        string ProductName,
        string PackageBatchLabel,
        int Quantity,
        decimal UnitPrice,
        decimal DiscountAmount,
        decimal PrintingCostPerCard,
        string BatchId,
        int SourceBatchQuantity,
        string TargetSystem)
    {
        public string NumberLabel => Number.ToString("N0", CultureInfo.CurrentCulture);
        public string QuantityLabel => Quantity.ToString("N0", CultureInfo.CurrentCulture);
        public string UnitPriceLabel => UnitPrice.ToString("N2", CultureInfo.CurrentCulture);
        public string DiscountLabel => DiscountAmount.ToString("N2", CultureInfo.CurrentCulture);
        public decimal GrossTotal => Quantity * UnitPrice;
        public decimal Total => Math.Max(0m, GrossTotal - DiscountAmount);
        public string TotalLabel => Total.ToString("N2", CultureInfo.CurrentCulture);
    }

    private sealed record SavedSaleDraft(
        string ChannelKey,
        string ExternalReference,
        string Notes,
        string PaymentNotes,
        string PaymentMethod,
        List<SavedDraftLine> Lines);

    private sealed record SavedDraftLine(
        string BatchId,
        int Quantity,
        decimal UnitPrice,
        decimal DiscountAmount,
        decimal PrintingCostPerCard,
        int SourceBatchQuantity,
        string PackageName);

}

 