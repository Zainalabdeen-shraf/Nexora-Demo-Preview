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

// Responsibility: Issuance form controls, package binding, validation, preview, and existing-batch selection.
public partial class CardIssuanceView
{
    private void RefreshPackageCatalog(bool preserveSelection, string? preferredKey = null)

    {

        if (PackageComboBox is null)

            return;


        var oldKey = preserveSelection ? (PackageComboBox.SelectedItem as DemoCardPackage)?.Key : null;

        var packages = DemoCardPackageCatalog.Load(_targetSystem);

        PackageComboBox.ItemsSource = packages;


        var desiredKey = preferredKey ?? oldKey;

        var selected = !string.IsNullOrWhiteSpace(desiredKey)

            ? packages.FirstOrDefault(package => package.Key == desiredKey)

            : null;

        PackageComboBox.SelectedItem = selected ?? packages.ElementAtOrDefault(packages.Count > 1 ? 1 : 0);


        if (PackageComboBox.SelectedItem is null)

        {

            SetStatus("لا توجد باقات مفعّلة لهذا النظام. استخدم زر (+) لإنشاء باقة أو ربط باقة موجودة.", true);

            RouterProfileTextBox.Text = string.Empty;

        }

        else

        {

            UpdateProfileSuggestion();

        }

        RefreshExistingBatches();

    }
    private void PackageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)

    {

        if (PackageComboBox is null || !IsLoaded)

            return;

        _previewCard = null;

        UpdateProfileSuggestion();
        ApplyTemplateAssociationForSelectedPackage();

        RefreshExistingBatches();

        UpdatePreview();

        PersistCurrentPreferences();

    }
    private void UpdateProfileSuggestion()

    {

        if (PackageComboBox?.SelectedItem is not DemoCardPackage package || RouterProfileTextBox is null)

            return;

        RouterProfileTextBox.Text = _targetSystem == DemoAccountSystem.HotSpot

            ? package.HotSpotProfile

            : package.UserManagerProfile;

    }
    private void FormSelectionChanged(object sender, SelectionChangedEventArgs e)

    {

        if (!IsLoaded)

            return;

        _previewCard = null;

        RefreshExistingBatches();

        UpdatePreview();

        PersistCurrentPreferences();

    }
    private void FormTextChanged(object sender, TextChangedEventArgs e)

    {

        if (!IsLoaded)

            return;

        _previewCard = null;

        RefreshExistingBatches();

        UpdatePreview();

        PersistCurrentPreferences();

    }
    private void PasswordModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)

    {

        if (!IsLoaded)

            return;

        _previewCard = null;

        UpdatePasswordControls();


        UpdatePreview();

        PersistCurrentPreferences();

    }
    private void UpdatePasswordControls()
    {
        if (PasswordModeComboBox is null || PasswordCharacterSetComboBox is null || PasswordLengthTextBox is null || PasswordGenerationPanel is null)
            return;

        var generated = ReadTag(PasswordModeComboBox) == "Generated";
        PasswordCharacterSetComboBox.IsEnabled = generated;
        PasswordLengthTextBox.IsEnabled = generated;
        PasswordGenerationPanel.Visibility = Visibility.Visible;
        PasswordGenerationPanel.IsEnabled = generated;
        PasswordGenerationPanel.Opacity = generated ? 1.0 : 0.7;
    }
    private void UpdatePreview()

    {

        if (!IsLoaded || PackageComboBox?.SelectedItem is not DemoCardPackage package)

            return;


        var prefix = CardPrefixTextBox.Text.Trim().ToUpperInvariant();

        var suffix = CardSuffixTextBox.Text.Trim().ToUpperInvariant();

        var totalLength = TryParseInteger(CardNumberLengthTextBox.Text, out var parsedLength) ? Math.Clamp(parsedLength, 5, 24) : 8;

        var bodyLength = Math.Max(1, totalLength - prefix.Length - suffix.Length);

        // CardNumber is the reserved sequential serial; the login username is a separate credential.
        var body = new string('0', Math.Min(bodyLength, 16));
        var cardNumberSample = _previewCard?.CardNumber ?? $"{prefix}{body}{suffix}";
        PreviewCardNumberTextBlock.Text = cardNumberSample;
        var usernamePlaceholderChar = ReadTag(CardNumberCharacterSetComboBox) switch
        {
            "LettersOnly" => 'A',
            "AlphaNumeric" => 'A',
            _ => '0'
        };
        var usernameSample = _previewCard?.Username ?? new string(usernamePlaceholderChar, Math.Clamp(totalLength, 1, 24));
        PreviewUsernameTextBlock.Text = $"اسم المستخدم: {usernameSample}";


        var mode = ReadTag(PasswordModeComboBox);

        var passwordPlaceholderChar = ReadTag(PasswordCharacterSetComboBox) switch
        {
            "LettersOnly" => 'A',
            "AlphaNumeric" => 'A',
            _ => '0'
        };
        var passwordText = _previewCard is not null
            ? _previewCard.Password
            : mode switch
            {
                "Empty" => string.Empty,
                "Generated" => new string(passwordPlaceholderChar, Math.Clamp(TryParseInteger(PasswordLengthTextBox.Text, out var pLength) ? pLength : 8, 1, 12)),
                _ => usernameSample
            };
        PreviewPasswordTextBlock.Text = string.IsNullOrWhiteSpace(passwordText)
            ? string.Empty
            : $"كلمة المرور: {passwordText}";

        PreviewBrandNameTextBlock.Text = "زين نت";
        PreviewBrandPanel.Visibility = Visibility.Visible;
        PreviewBrandSubtitleTextBlock.Visibility = Visibility.Collapsed;
        PreviewSourcePanel.Visibility = Visibility.Collapsed;
        PreviewCredentialsCaptionTextBlock.Visibility = Visibility.Collapsed;
        PreviewCardNumberRow.Visibility = Visibility.Collapsed;
        PreviewCardNumberTextBlock.Visibility = Visibility.Collapsed;
        PreviewUsernameTextBlock.Visibility = Visibility.Visible;
        PreviewPasswordTextBlock.Visibility = string.IsNullOrWhiteSpace(passwordText) ? Visibility.Collapsed : Visibility.Visible;
        PreviewHeaderDividerBorder.Visibility = Visibility.Collapsed;
        PreviewFooterDividerBorder.Visibility = Visibility.Collapsed;

        if (CardPreviewBorder.Child is Grid previewCardLayout && previewCardLayout.RowDefinitions.Count >= 4)
        {
            previewCardLayout.RowDefinitions[1].Height = new GridLength(0);
            previewCardLayout.RowDefinitions[3].Height = new GridLength(0);
        }

        var packageName = package.DisplayName.Split('—')[0].Trim();
        var packageParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(packageName))
            packageParts.Add(packageName);
        if (package.PrintPrice > 0)
            packageParts.Add($"{package.PrintPrice:N0} ريال");
        PreviewPackageTextBlock.Text = string.Join(" · ", packageParts);
        PreviewPackageTextBlock.Visibility = packageParts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var timingParts = new List<string>();
        if (!string.IsNullOrWhiteSpace(package.TimeLimit) &&
            (string.IsNullOrWhiteSpace(packageName) || packageName.IndexOf(package.TimeLimit, StringComparison.OrdinalIgnoreCase) < 0))
            timingParts.Add(package.TimeLimit.Trim());
        if (!string.IsNullOrWhiteSpace(package.Validity))
        {
            var validityText = package.Validity.Trim();
            if (!validityText.StartsWith("صلاحية", StringComparison.Ordinal))
                validityText = $"صلاحية {validityText}";
            timingParts.Add(validityText);
        }
        PreviewBatchTextBlock.Text = string.Join(" · ", timingParts);
        PreviewBatchTextBlock.Visibility = timingParts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var qrVisible = IsTemplateElementVisible("رمز QR");
        var barcodeVisible = IsTemplateElementVisible("الباركود");
        PreviewQrBorder.Visibility = qrVisible ? Visibility.Visible : Visibility.Collapsed;
        PreviewBarcodeBorder.Visibility = barcodeVisible ? Visibility.Visible : Visibility.Collapsed;
        if (PreviewQrBorder.Parent is Grid previewFieldsGrid && previewFieldsGrid.ColumnDefinitions.Count >= 5)
        {
            previewFieldsGrid.ColumnDefinitions[0].Width = qrVisible ? new GridLength(54) : new GridLength(0);
            previewFieldsGrid.ColumnDefinitions[1].Width = qrVisible ? new GridLength(5) : new GridLength(0);
            previewFieldsGrid.ColumnDefinitions[2].Width = barcodeVisible ? new GridLength(54) : new GridLength(0);
            previewFieldsGrid.ColumnDefinitions[3].Width = barcodeVisible ? new GridLength(5) : new GridLength(0);
        }

        UpdateSelectedTemplatePreview(cardNumberSample, usernameSample, passwordText, package);
    }

    private void UpdateSelectedTemplatePreview(string cardNumber, string username, string password, DemoCardPackage package)
    {
        if (CardPreviewBorder is null)
            return;

        try
        {
            var selectedPath = _activeTemplatePath;
            if (string.IsNullOrWhiteSpace(selectedPath) || !File.Exists(selectedPath))
                selectedPath = (PrintTemplateComboBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            if (string.IsNullOrWhiteSpace(selectedPath) || !File.Exists(selectedPath))
                selectedPath = File.Exists(TemplateDesignStoragePath) ? TemplateDesignStoragePath : null;

            if (string.IsNullOrWhiteSpace(selectedPath) || !File.Exists(selectedPath))
                throw new FileNotFoundException("لا يوجد ملف قالب محفوظ للمعاينة. افتح (+) بجانب القالب واحفظ التصميم باختيار القالب.");

            var normalizedPath = Path.GetFullPath(selectedPath);
            var lastWriteUtc = File.GetLastWriteTimeUtc(normalizedPath);
            if (_selectedTemplateRenderer is null ||
                !string.Equals(_selectedTemplateRendererPath, normalizedPath, StringComparison.OrdinalIgnoreCase) ||
                _selectedTemplateRendererLastWriteUtc != lastWriteUtc)
            {
                var renderer = new Nexora.Demo.Desktop.Features.Printing.Views.TemplateDesignerView();
                if (!renderer.LoadTemplateFile(normalizedPath))
                    throw new InvalidDataException("ملف القالب موجود لكن تعذر قراءة عناصر التصميم منه.");
                renderer.ApplyIssuanceContext(_targetSystem, package.Key);
                _selectedTemplateRenderer = renderer;
                _selectedTemplateRendererPath = normalizedPath;
                _selectedTemplateRendererLastWriteUtc = lastWriteUtc;
            }

            var packageName = package.DisplayName.Split('—')[0].Trim();
            var network = string.IsNullOrWhiteSpace(PreviewBrandNameTextBlock?.Text) ? string.Empty : PreviewBrandNameTextBlock.Text;
            var pointOfSale = (PointOfSaleComboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? string.Empty;
            var cardVisual = _selectedTemplateRenderer.CreateIssuancePreviewElement(
                cardNumber,
                username,
                password,
                package.TimeLimit ?? string.Empty,
                package.Validity ?? string.Empty,
                package.PrintPrice.ToString("0.##", CultureInfo.CurrentCulture),
                package.DataLimit ?? string.Empty,
                network,
                packageName,
                pointOfSale,
                0);

            // This is the selected designer's real visual tree, not the static fallback
            // card previously embedded in CardIssuanceView.xaml.
            CardPreviewBorder.Child = cardVisual;
            CardPreviewBorder.Padding = new Thickness(2);
            CardPreviewBorder.Background = Brushes.White;
            CardPreviewBorder.ToolTip = $"معاينة مباشرة للقالب: {_designerTemplateName}";
        }
        catch (Exception ex)
        {
            // Never disguise a failed template load as a successful default-card preview.
            SetStatus("تعذر عرض القالب المختار: " + ex.Message, true);
            CardPreviewBorder.ToolTip = ex.Message;
            CardPreviewBorder.Child = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(255, 245, 245)),
                Padding = new Thickness(10),
                Child = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "تعذّر تحميل القالب المختار",
                            Foreground = new SolidColorBrush(Color.FromRgb(165, 25, 37)),
                            FontWeight = FontWeights.Bold,
                            FontSize = 12,
                            TextAlignment = TextAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = "أعد اختيار القالب أو افتح (+) بجانب القالب ثم اضغط «اختيار القالب».",
                            Foreground = new SolidColorBrush(Color.FromRgb(95, 55, 60)),
                            FontSize = 10,
                            TextWrapping = TextWrapping.Wrap,
                            TextAlignment = TextAlignment.Center,
                            Margin = new Thickness(0, 4, 0, 0)
                        }
                    }
                }
            };
        }
    }
private void BatchMode_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || ExistingBatchPanel is null || AppendToPreviousBatchRadioButton is null)
            return;

        var append = AppendToPreviousBatchRadioButton.IsChecked == true;
        ExistingBatchPanel.IsEnabled = append;
        PickExistingBatchButton.IsEnabled = append;
        BatchRegistrationHintTextBlock.Visibility = append ? Visibility.Visible : Visibility.Collapsed;
        BatchRegistrationHintTextBlock.Text = append
            ? "اختر دفعة سابقة؛ ستُحمّل إعداداتها المطابقة تلقائيًا."
            : "سيُنشأ رقم دفعة جديد تلقائيًا.";

        RefreshExistingBatches();
        if (append && ExistingBatchComboBox.SelectedItem is GeneratedCardBatch batch)
            ApplyPreviousBatchSettings(batch);
        PersistCurrentPreferences();
    }
private void ExistingBatchComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _isApplyingPreviousBatch || AppendToPreviousBatchRadioButton.IsChecked != true ||
            ExistingBatchComboBox.SelectedItem is not GeneratedCardBatch batch)
            return;

        ApplyPreviousBatchSettings(batch);
    }
private void RefreshExistingBatches()
    {
        if (_isApplyingPreviousBatch || ExistingBatchComboBox is null || PackageComboBox is null)
            return;

        var previousId = (ExistingBatchComboBox.SelectedItem as GeneratedCardBatch)?.BatchId;
        var targetName = _targetSystem == DemoAccountSystem.HotSpot ? "مستخدمو الهوتسبوت" : "مدير المستخدمين";
        var eligible = _issuanceService.Batches
            .Where(batch => string.Equals(batch.TargetSystem, targetName, StringComparison.Ordinal))
            .OrderByDescending(batch => batch.CreatedAt)
            .ToList();

        _isApplyingPreviousBatch = true;
        try
        {
            ExistingBatchComboBox.ItemsSource = eligible;
            ExistingBatchComboBox.SelectedItem = eligible.FirstOrDefault(batch =>
                string.Equals(batch.BatchId, previousId, StringComparison.OrdinalIgnoreCase))
                ?? eligible.FirstOrDefault();
            AppendToPreviousBatchRadioButton.IsEnabled = eligible.Count > 0;
            ExistingBatchPanel.IsEnabled = AppendToPreviousBatchRadioButton.IsChecked == true && eligible.Count > 0;
        }
        finally
        {
            _isApplyingPreviousBatch = false;
        }

        if (eligible.Count == 0 && AppendToPreviousBatchRadioButton.IsChecked == true)
            NewBatchRadioButton.IsChecked = true;
    }
    private void ApplyPreviousBatchSettings(GeneratedCardBatch batch)
    {
        _isApplyingPreviousBatch = true;
        try
        {
            var package = PackageComboBox.Items.OfType<DemoCardPackage>().FirstOrDefault(item =>
                string.Equals(item.DisplayName, batch.PackageName, StringComparison.Ordinal) && item.Price == batch.UnitPrice);
            if (package is null)
            {
                SetStatus("لا يمكن الإلحاق بهذه الدفعة لأن باقتها لم تعد موجودة في قائمة الباقات.", true);
                return;
            }

            PackageComboBox.SelectedItem = package;
            RouterProfileTextBox.Text = batch.RouterProfile;

            var pointOfSale = PointOfSaleComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
                string.Equals(item.Content?.ToString(), batch.PointOfSale, StringComparison.Ordinal));
            if (pointOfSale is null)
            {
                SetStatus("نقطة البيع المسجلة في الدفعة السابقة غير متاحة حاليًا. راجع نقاط البيع أولًا.", true);
                return;
            }
            PointOfSaleComboBox.SelectedItem = pointOfSale;

            if (_targetSystem == DemoAccountSystem.UserManager)
            {
                var manager = ManagerUserComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
                    string.Equals(item.Tag?.ToString(), batch.ManagerAccount, StringComparison.OrdinalIgnoreCase));
                if (manager is null)
                {
                    SetStatus("مستخدم يوزر منجر المرتبط بالدفعة السابقة غير متاح في القائمة.", true);
                    return;
                }
                ManagerUserComboBox.SelectedItem = manager;
            }

            var sharedUsers = SharedUsersComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
                string.Equals(item.Tag?.ToString(), batch.SharedUsers.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
            if (sharedUsers is null)
            {
                SetStatus("عدد الأجهزة المرتبط بالدفعة السابقة غير متاح في إعدادات الشاشة الحالية.", true);
                return;
            }
            SharedUsersComboBox.SelectedItem = sharedUsers;
            ExistingBatchComboBox.SelectedItem = batch;
            _previewCard = null;
            UpdatePreview();
            PersistCurrentPreferences();
        }
        finally
        {
            _isApplyingPreviousBatch = false;
        }

        RefreshExistingBatches();
        BatchRegistrationHintTextBlock.Text = $"الدفعة المحددة: {batch.BatchId} · المسجل حاليًا {batch.Quantity:N0} كرت. ستتم إضافة العدد الجديد إليها.";
        SetStatus($"تم تحميل إعدادات الدفعة السابقة {batch.BatchId}. حدّد عدد الكروت المراد إضافته ثم أنشئ الدفعة.", false);
    }
private void PickExistingBatchButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshExistingBatches();
        var batches = ExistingBatchComboBox.Items.OfType<GeneratedCardBatch>().ToList();
        if (batches.Count == 0)
        {
            SetStatus("لا توجد دفعات سابقة لهذا النظام بعد. أنشئ الدفعة الأولى، وبعدها يمكنك الإلحاق بها.", false);
            return;
        }

        AppendToPreviousBatchRadioButton.IsChecked = true;
        RefreshExistingBatches();
        if (ExistingBatchComboBox.SelectedItem is GeneratedCardBatch selectedBatch)
            ApplyPreviousBatchSettings(selectedBatch);
        ExistingBatchComboBox.Focus();
        ExistingBatchComboBox.IsDropDownOpen = true;
    }
}
