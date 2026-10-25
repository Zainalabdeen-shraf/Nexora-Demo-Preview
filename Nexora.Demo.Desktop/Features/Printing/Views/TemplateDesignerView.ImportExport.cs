using System.Globalization;
using Microsoft.Win32;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Nexora.Demo.Desktop.Features.Cards.Models;

namespace Nexora.Demo.Desktop.Features.Printing.Views;

// Responsibility: Template persistence, import/export, and state restoration.
public partial class TemplateDesignerView
{
    private void ImportTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "استيراد قالب TabCard أو صورة لقالب",
            Filter = "قالب TabCard (*.json)|*.json|صور القوالب (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|كل الملفات المدعومة (*.json;*.png;*.jpg;*.jpeg;*.bmp)|*.json;*.png;*.jpg;*.jpeg;*.bmp",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        try
        {
            if (string.Equals(System.IO.Path.GetExtension(dialog.FileName), ".json", StringComparison.OrdinalIgnoreCase))
            {
                var imported = JsonSerializer.Deserialize<TemplateDesignState>(File.ReadAllText(dialog.FileName));
                if (imported is null || (imported.Format != TemplateFormatId && imported.ElementProperties is not { Count: > 0 }))
                {
                    MessageBox.Show("هذا الملف ليس قالب TabCard قابلًا للاستيراد. يمكن استيراد قالب مُصدّر من TabCard بصيغة JSON أو صورة PNG/JPG/BMP كخلفية.", "صيغة غير مدعومة", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                ApplyTemplateState(imported);
                SetStatus($"تم استيراد القالب «{imported.TemplateName}». احفظه لحفظ النسخة محليًا.", isSuccess: true);
            }
            else
            {
                ImportTemplateImage(dialog.FileName);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show("تعذر استيراد القالب: " + ex.Message, "تعذر الاستيراد", MessageBoxButton.OK, MessageBoxImage.Warning);
            SetStatus("فشل استيراد القالب: " + ex.Message, isSuccess: false);
        }
    }
    private void ImportBackgroundImageButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "اختيار صورة خلفية القالب",
            Filter = "صور القالب (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
        {
            try { ImportTemplateImage(dialog.FileName); }
            catch (Exception ex) { MessageBox.Show("تعذر استيراد الصورة: " + ex.Message, "تعذر الاستيراد", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
    }
    private void ExportTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ApplyPropertiesButton_Click(this, new RoutedEventArgs());
            var dialog = new SaveFileDialog
            {
                Title = "تصدير قالب TabCard",
                Filter = "قالب TabCard (*.json)|*.json",
                FileName = (string.IsNullOrWhiteSpace(TemplateNameTextBox.Text) ? DefaultTemplateName : TemplateNameTextBox.Text.Trim()) + ".json",
                AddExtension = true,
                DefaultExt = ".json",
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(CaptureState(), JsonOptions));
            SetStatus("تم تصدير القالب. ملف JSON يتضمن خصائص العناصر وصورة الخلفية المستوردة إن وجدت.", isSuccess: true);
        }
        catch (Exception ex) { SetStatus("تعذر تصدير القالب: " + ex.Message, isSuccess: false); }
    }
    private void ImportTemplateImage(string sourcePath)
    {
        var extension = System.IO.Path.GetExtension(sourcePath).ToLowerInvariant();
        if (extension != ".png" && extension != ".jpg" && extension != ".jpeg" && extension != ".bmp")
            throw new InvalidDataException("امتداد الصورة غير مدعوم. استخدم PNG أو JPG أو BMP.");
        var info = new FileInfo(sourcePath);
        if (info.Length > MaxImportedImageBytes)
            throw new InvalidDataException("حجم الصورة أكبر من 15 ميجابايت.");

        var assetsDirectory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexoraProductDemo", "templates", "assets");
        Directory.CreateDirectory(assetsDirectory);
        var destination = System.IO.Path.Combine(assetsDirectory, "template-background-" + Guid.NewGuid().ToString("N") + extension);
        File.Copy(sourcePath, destination, overwrite: false);
        _importedTemplateImagePath = destination;
        LoadBackgroundImageFromPath(destination);
        _visibleElements["شعار الشبكة"] = false;
        _visibleElements["الشريط العلوي"] = false;
        _visibleElements["الفاصل"] = false;
        SyncMainElementChecks();
        TemplateNameTextBox.Text = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
        ApplyImportedBackgroundAppearance();
        UpdatePreview();
        SetStatus("تم استيراد صورة القالب كخلفية. يمكن تحريك وتغيير حجم وتدوير كل عنصر بيانات فوقها.", isSuccess: true);
    }
    private void LoadBackgroundImageFromPath(string path)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(path, UriKind.Absolute);
        bitmap.DecodePixelWidth = 1600;
        bitmap.EndInit();
        bitmap.Freeze();
        TemplateBackgroundImage.Source = bitmap;
        TemplateBackgroundImage.Visibility = Visibility.Visible;
    }
    private void ApplyImportedBackgroundAppearance()
    {
        var hasImage = !string.IsNullOrWhiteSpace(_importedTemplateImagePath) && File.Exists(_importedTemplateImagePath);
        TemplateBackgroundImage.Visibility = hasImage ? Visibility.Visible : Visibility.Collapsed;
        if (!hasImage) TemplateBackgroundImage.Source = null;
    }
    private string? GetImportedImageBase64()
    {
        if (string.IsNullOrWhiteSpace(_importedTemplateImagePath) || !File.Exists(_importedTemplateImagePath)) return null;
        var info = new FileInfo(_importedTemplateImagePath);
        return info.Length <= MaxImportedImageBytes ? Convert.ToBase64String(File.ReadAllBytes(_importedTemplateImagePath)) : null;
    }
    private string? RestoreBackgroundFromState(TemplateDesignState state)
    {
        if (!string.IsNullOrWhiteSpace(state.ImportedBackgroundImagePath) && File.Exists(state.ImportedBackgroundImagePath))
            return state.ImportedBackgroundImagePath;
        if (string.IsNullOrWhiteSpace(state.ImportedBackgroundImageBase64)) return null;

        var bytes = Convert.FromBase64String(state.ImportedBackgroundImageBase64);
        if (bytes.LongLength > MaxImportedImageBytes) throw new InvalidDataException("صورة الخلفية داخل القالب أكبر من 15 ميجابايت.");
        var extension = (state.ImportedBackgroundImageExtension ?? ".png").ToLowerInvariant();
        if (extension != ".png" && extension != ".jpg" && extension != ".jpeg" && extension != ".bmp") extension = ".png";
        var assetsDirectory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexoraProductDemo", "templates", "assets");
        Directory.CreateDirectory(assetsDirectory);
        var path = System.IO.Path.Combine(assetsDirectory, "template-background-" + Guid.NewGuid().ToString("N") + extension);
        File.WriteAllBytes(path, bytes);
        return path;
    }
    private void SaveTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ApplyPropertiesButton_Click(this, new RoutedEventArgs());
            var state = CaptureState();
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StoragePath)!);
            File.WriteAllText(StoragePath, JsonSerializer.Serialize(state, JsonOptions));
            SetStatus($"تم حفظ القالب «{state.TemplateName}» محليًا.", isSuccess: true);
        }
        catch (Exception ex)
        {
            SetStatus("تعذر حفظ القالب: " + ex.Message, isSuccess: false);
        }
    }
    private void NewTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        ResetToDefaults();
        SetStatus("قالب جديد غير محفوظ. لن يتغير القالب المحفوظ إلا عند الضغط على حفظ القالب.", isSuccess: true);
    }
    private void DeleteTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "سيتم حذف نسخة القالب المحفوظة محليًا وإرجاع الشاشة إلى الإعدادات الافتراضية. لا تتأثر الكروت أو بيانات المشروع الأخرى. هل تريد المتابعة؟",
            "حذف القالب المحفوظ",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        try
        {
            if (File.Exists(StoragePath)) File.Delete(StoragePath);
            ResetToDefaults();
            SetStatus("تم حذف القالب المحفوظ وإعادة الإعدادات الافتراضية.", isSuccess: true);
        }
        catch (Exception ex)
        {
            SetStatus("تعذر حذف القالب: " + ex.Message, isSuccess: false);
        }
    }
    private TemplateDesignState CaptureState() => new()
    {
        Format = TemplateFormatId,
        TemplateName = string.IsNullOrWhiteSpace(TemplateNameTextBox.Text) ? DefaultTemplateName : TemplateNameTextBox.Text.Trim(),
        TemplateStyle = GetSelectedTag(TemplateStyleComboBox) ?? "classic",
        ColorTheme = GetSelectedTag(ColorThemeComboBox) ?? "red",
        FontFamily = GetSelectedTag(FontFamilyComboBox) ?? "Segoe UI",
        FontSize = ParseDouble(ElementFontSizeTextBox.Text, 11, 8, 28),
        CardWidthMm = ParseDouble(CardWidthTextBox.Text, 66, 10, 200),
        CardHeightMm = ParseDouble(CardHeightTextBox.Text, 15.5, 10, 200),
        CardRotationDegrees = ParseDouble(CardRotationTextBox.Text, 0, -180, 180),
        ImportedBackgroundImagePath = _importedTemplateImagePath,
        ImportedBackgroundImageBase64 = GetImportedImageBase64(),
        ImportedBackgroundImageExtension = string.IsNullOrWhiteSpace(_importedTemplateImagePath) ? ".png" : System.IO.Path.GetExtension(_importedTemplateImagePath),
        HotspotPackageIndex = HotspotPackageComboBox.SelectedIndex,
        UserManagerPackageIndex = UserManagerPackageComboBox.SelectedIndex,
        HotspotPackageKey = (HotspotPackageComboBox.SelectedItem as DemoCardPackage)?.Key ?? string.Empty,
        UserManagerPackageKey = (UserManagerPackageComboBox.SelectedItem as DemoCardPackage)?.Key ?? string.Empty,
        ActivePackageSource = _activePackageSource,
        LoginPageUrl = _loginPageUrl,
        BarcodeText = _barcodeText,
        SelectedElementTag = GetSelectedTag(SelectedElementComboBox) ?? "login",
        SelectedElementX = ParseDouble(ElementXTextBox.Text, 20, 0, Math.Max(DesignBaseWidth, CardDesignCanvas.Width)),
        SelectedElementY = ParseDouble(ElementYTextBox.Text, 73, 0, Math.Max(DesignBaseHeight, CardDesignCanvas.Height)),
        SelectedElementText = SelectedElementTextBox.Text,
        SelectedElementBold = BoldElementCheckBox.IsChecked == true,
        ElementProperties = CaptureElementProperties(),
        VisibleElements = new Dictionary<string, bool>(_visibleElements, StringComparer.Ordinal)
    };
    private void LoadSavedTemplate()
    {
        if (!File.Exists(StoragePath)) return;
        try
        {
            var state = JsonSerializer.Deserialize<TemplateDesignState>(File.ReadAllText(StoragePath));
            if (state is not null) ApplyTemplateState(state);
        }
        catch (Exception ex)
        {
            SetStatus("تعذر قراءة القالب المحفوظ؛ سيتم استخدام الإعدادات الافتراضية. " + ex.Message, isSuccess: false);
        }
    }
    private void ApplyTemplateState(TemplateDesignState state)
    {
        // Migrate only the old built-in 60x35 mm Default_Design to the new voucher geometry.
        // Custom named templates keep their dimensions; the existing JSON is not rewritten
        // until the user deliberately saves the updated default template.
        var isOldBuiltInDefault = string.Equals(state.TemplateName, DefaultTemplateName, StringComparison.OrdinalIgnoreCase)
            && Math.Abs(state.CardWidthMm - 60d) < 0.1d
            && Math.Abs(state.CardHeightMm - 35d) < 0.1d;
        var requiresDipCoordinateMigration = !string.Equals(state.Format, TemplateFormatId, StringComparison.Ordinal);
        if (isOldBuiltInDefault)
        {
            var newProperties = CreateDefaultElementProperties();
            if (state.ElementProperties is not null)
            {
                foreach (var pair in newProperties)
                {
                    if (!state.ElementProperties.TryGetValue(pair.Key, out var oldProperty) || oldProperty is null)
                        continue;
                    // Preserve user-authored text/font family/bold preference while replacing
                    // the old card geometry and small legacy placements with the readable voucher layout.
                    if (oldProperty.Text is not null) pair.Value.Text = oldProperty.Text;
                    if (!string.IsNullOrWhiteSpace(oldProperty.FontFamily)) pair.Value.FontFamily = oldProperty.FontFamily;
                    if (pair.Key is "login" or "price" or "validity") pair.Value.IsBold = oldProperty.IsBold || pair.Value.IsBold;
                }
            }
            state.ElementProperties = newProperties;
            if (state.VisibleElements is not null)
                state.VisibleElements["رمز QR"] = false;
            state.CardWidthMm = 66d;
            state.CardHeightMm = 15.5d;
            state.SelectedElementX *= LegacyToDipScale;
            state.SelectedElementY *= LegacyToDipScale;
            state.Format = TemplateFormatId;
        }
        else if (requiresDipCoordinateMigration)
        {
            // V1 stored base-canvas positions; V2 stored positions after sizing the canvas.
            // Convert the legacy geometry and font sizes to DIPs exactly once, keeping the
            // old card-size position ratios for V1 while preventing the whole card shrinking text.
            var isV2PhysicalCanvas = string.Equals(state.Format, "TabCard.TemplateDesigner.v2", StringComparison.Ordinal);
            var legacyPositionScaleX = LegacyToDipScale * (isV2PhysicalCanvas ? 1d : state.CardWidthMm / BaseWidthMm);
            var legacyPositionScaleY = LegacyToDipScale * (isV2PhysicalCanvas ? 1d : state.CardHeightMm / BaseHeightMm);
            if (state.ElementProperties is not null)
            {
                foreach (var element in state.ElementProperties.Values.Where(value => value is not null))
                {
                    element.X *= legacyPositionScaleX;
                    element.Y *= legacyPositionScaleY;
                    element.Width *= LegacyToDipScale;
                    element.Height *= LegacyToDipScale;
                    element.FontSize *= LegacyToDipScale;
                }
            }
            state.SelectedElementX *= legacyPositionScaleX;
            state.SelectedElementY *= legacyPositionScaleY;
            state.Format = TemplateFormatId;
        }

        var isLegacyCoordinateState = !string.Equals(state.Format, TemplateFormatId, StringComparison.Ordinal);
        TemplateNameTextBox.Text = string.IsNullOrWhiteSpace(state.TemplateName) ? DefaultTemplateName : state.TemplateName;
        SelectComboTag(TemplateStyleComboBox, state.TemplateStyle);
        SelectComboTag(ColorThemeComboBox, state.ColorTheme);
        SelectComboTag(FontFamilyComboBox, state.FontFamily);
        ElementFontSizeTextBox.Text = state.FontSize.ToString("0.#", CultureInfo.InvariantCulture);
        CardWidthTextBox.Text = state.CardWidthMm.ToString("0.#", CultureInfo.InvariantCulture);
        CardHeightTextBox.Text = state.CardHeightMm.ToString("0.#", CultureInfo.InvariantCulture);
        CardRotationTextBox.Text = state.CardRotationDegrees.ToString("0.#", CultureInfo.InvariantCulture);
        var savedHotSpotPackage = HotspotPackageComboBox.Items.OfType<DemoCardPackage>()
            .FirstOrDefault(item => string.Equals(item.Key, state.HotspotPackageKey, StringComparison.OrdinalIgnoreCase));
        if (savedHotSpotPackage is not null)
            HotspotPackageComboBox.SelectedItem = savedHotSpotPackage;
        else if (state.HotspotPackageIndex >= 0 && state.HotspotPackageIndex < HotspotPackageComboBox.Items.Count)
            HotspotPackageComboBox.SelectedIndex = state.HotspotPackageIndex;

        var savedUserManagerPackage = UserManagerPackageComboBox.Items.OfType<DemoCardPackage>()
            .FirstOrDefault(item => string.Equals(item.Key, state.UserManagerPackageKey, StringComparison.OrdinalIgnoreCase));
        if (savedUserManagerPackage is not null)
            UserManagerPackageComboBox.SelectedItem = savedUserManagerPackage;
        else if (state.UserManagerPackageIndex >= 0 && state.UserManagerPackageIndex < UserManagerPackageComboBox.Items.Count)
            UserManagerPackageComboBox.SelectedIndex = state.UserManagerPackageIndex;
        _activePackageSource = state.ActivePackageSource == "user-manager" ? "user-manager" : "hotspot";
        _loginPageUrl = string.IsNullOrWhiteSpace(state.LoginPageUrl) ? DefaultLoginUrl : state.LoginPageUrl;
        _barcodeText = state.BarcodeText ?? "50723015";

        if (state.VisibleElements is not null)
        {
            _visibleElements.Clear();
            foreach (var item in CreateDefaultVisibility()) _visibleElements[item.Key] = item.Value;
            foreach (var item in state.VisibleElements) _visibleElements[item.Key] = item.Value;
        }
        else
        {
            _visibleElements.Clear();
            foreach (var item in CreateDefaultVisibility()) _visibleElements[item.Key] = item.Value;
        }

        _importedTemplateImagePath = RestoreBackgroundFromState(state);
        if (!string.IsNullOrWhiteSpace(_importedTemplateImagePath) && File.Exists(_importedTemplateImagePath))
            LoadBackgroundImageFromPath(_importedTemplateImagePath);
        else
            TemplateBackgroundImage.Source = null;
        ApplyImportedBackgroundAppearance();

        ApplyElementProperties(CreateDefaultElementProperties());
        if (state.ElementProperties is { Count: > 0 }) ApplyElementProperties(state.ElementProperties);
        SelectComboTag(SelectedElementComboBox, state.SelectedElementTag ?? "login");
        if (state.ElementProperties is not { Count: > 0 })
        {
            if (GetSelectedElement() is FrameworkElement selected)
            {
                Canvas.SetLeft(selected, Math.Clamp(state.SelectedElementX, 0, DesignBaseWidth * 200d / BaseWidthMm));
                Canvas.SetTop(selected, Math.Clamp(state.SelectedElementY, 0, DesignBaseHeight * 200d / BaseHeightMm));
                if (selected is TextBlock tb && state.SelectedElementText is not null) tb.Text = state.SelectedElementText;
                if (selected is TextBlock textBlock) textBlock.FontWeight = state.SelectedElementBold ? FontWeights.Bold : FontWeights.Normal;
            }
        }
        UpdateSamplePackageFields();
        SyncMainElementChecks();
        var targetCanvasWidth = DesignBaseWidth * ParseDouble(CardWidthTextBox.Text, 66, 10, 200) / BaseWidthMm;
        var targetCanvasHeight = DesignBaseHeight * ParseDouble(CardHeightTextBox.Text, 15.5, 10, 200) / BaseHeightMm;
        // V2 state stores coordinates in the actual canvas. Legacy V1 state stores base-canvas
        // coordinates, so let ApplyCardDimensionTransform map those positions once on migration.
        if (!isLegacyCoordinateState)
        {
            _lastCanvasWidth = targetCanvasWidth;
            _lastCanvasHeight = targetCanvasHeight;
        }
        ApplyCardDimensionTransform();
        UpdatePreview();
        SyncSelectedElementEditor();
    }
    private void ResetToDefaults()
    {
        _visibleElements.Clear();
        foreach (var pair in CreateDefaultVisibility()) _visibleElements[pair.Key] = pair.Value;
        _importedTemplateImagePath = null;
        TemplateBackgroundImage.Source = null;
        TemplateBackgroundImage.Visibility = Visibility.Collapsed;
        TemplateNameTextBox.Text = DefaultTemplateName;
        SelectComboTag(TemplateStyleComboBox, "classic");
        SelectComboTag(ColorThemeComboBox, "red");
        SelectComboTag(FontFamilyComboBox, "Segoe UI");
        ElementFontSizeTextBox.Text = "11";
        CardWidthTextBox.Text = "66";
        CardHeightTextBox.Text = "15.5";
        CardRotationTextBox.Text = "0";
        _lastCanvasWidth = DesignBaseWidth * 66d / BaseWidthMm;
        _lastCanvasHeight = DesignBaseHeight * 15.5d / BaseHeightMm;
        ApplyElementProperties(CreateDefaultElementProperties());
        ApplyCardDimensionTransform();
        _zoom = 1d;
        ZoomSlider.Value = 1d;
        HotspotPackageComboBox.SelectedIndex = 0;
        UserManagerPackageComboBox.SelectedIndex = 0;
        _activePackageSource = "hotspot";
        _loginPageUrl = DefaultLoginUrl;
        _barcodeText = "50723015";
        ApplyElementProperties(CreateDefaultElementProperties());
        SelectComboTag(SelectedElementComboBox, "login");
        UpdateSamplePackageFields();
        SyncSelectedElementEditor();
        SyncMainElementChecks();
        ApplyImportedBackgroundAppearance();
        ApplyCardDimensionTransform();
        UpdatePreview();
    }
}
