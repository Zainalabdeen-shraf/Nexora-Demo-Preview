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
            EnsureRasterTemplateBase();
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
            var exportedState = CaptureState();
            exportedState.IsRasterTemplateMode = true;
            exportedState.RasterTemplateImagePath = _rasterTemplateImagePath;
            exportedState.RasterTemplateImageBase64 = GetRasterTemplateImageBase64();
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(exportedState, JsonOptions));
            SetStatus("تم تصدير القالب كملف قابل للاستيراد، مع تضمين صورة التصميم ومواضع حقول البيانات.", isSuccess: true);
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
        _rasterTemplateImagePath = null;
        _isRasterTemplateMode = false;
        LoadBackgroundImageFromPath(destination);
        // Imported art is only the background. Keep the printable data as independent,
        // editable overlays instead of hiding them or baking them into the image.
        _visibleElements["الشريط العلوي"] = false;
        _visibleElements["الفاصل"] = false;
        _visibleElements["رمز الدخول"] = false;
        _visibleElements["اسم المستخدم"] = true;
        _visibleElements["كلمة المرور"] = true;
        foreach (var key in new[] { "الصلاحية", "الوقت", "السعر", "التحميل", "الباقة", "أرقام التواصل", "شعار الشبكة" })
            _visibleElements[key] = true;
        foreach (var key in new[] { "الرقم التسلسلي", "تاريخ الطباعة", "رقم الدفعة", "نقطة البيع", "معلومات إضافية", "كود المكافأة", "رمز QR", "الباركود" })
            _visibleElements[key] = false;
        SyncMainElementChecks();
        TemplateNameTextBox.Text = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
        ApplyImportedBackgroundAppearance();
        UpdatePreview();
        SetStatus("تم اختيار الصورة كخلفية فقط؛ حقول المستخدم وكلمة المرور والصلاحية والوقت والتحميل والتواصل بقيت مستقلة وقابلة للتحرير.", isSuccess: true);
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
        var imagePath = _isRasterTemplateMode ? _rasterTemplateImagePath : _importedTemplateImagePath;
        var hasImage = !string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath);
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
            EnsureRasterTemplateBase();
            var state = CaptureState();
            state.IsRasterTemplateMode = true;
            state.RasterTemplateImagePath = _rasterTemplateImagePath;
            state.RasterTemplateImageBase64 = GetRasterTemplateImageBase64();
            state.Format = TemplateFormatId;
            state.LayoutRevision = 6;

            Directory.CreateDirectory(TemplateLibraryDirectory);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StoragePath)!);
            var libraryJsonPath = System.IO.Path.Combine(TemplateLibraryDirectory, MakeSafeTemplateFileName(state.TemplateName) + ".json");
            File.WriteAllText(libraryJsonPath, JsonSerializer.Serialize(state, JsonOptions));
            File.WriteAllText(StoragePath, JsonSerializer.Serialize(state, JsonOptions));
            _isRasterTemplateMode = true;
            ApplyRasterTemplateModeVisibility();
            LoadTemplateLibrary(state.TemplateName);
            UpdatePreview();
            SetStatus($"تم حفظ «{state.TemplateName}» كقالب صورة مستقل مع حقول البيانات القابلة للتعبئة. يمكنك اختياره مجددًا من قوالبي المحفوظة.", isSuccess: true);
        }
        catch (Exception ex)
        {
            SetStatus("تعذر حفظ القالب: " + ex.Message, isSuccess: false);
        }
    }

    
    public string SaveTemplateToLibraryForIssuance()
    {
        ApplyPropertiesButton_Click(this, new RoutedEventArgs());
        EnsureRasterTemplateBase();

        var state = CaptureState();
        state.IsRasterTemplateMode = true;
        state.RasterTemplateImagePath = _rasterTemplateImagePath;
        state.RasterTemplateImageBase64 = GetRasterTemplateImageBase64();
        state.Format = TemplateFormatId;
        state.LayoutRevision = 5;

        Directory.CreateDirectory(TemplateLibraryDirectory);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StoragePath)!);

        var libraryPath = System.IO.Path.Combine(
            TemplateLibraryDirectory,
            MakeSafeTemplateFileName(state.TemplateName) + ".json");

        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(libraryPath, json);
        File.WriteAllText(StoragePath, json);

        _isRasterTemplateMode = true;
        ApplyRasterTemplateModeVisibility();
        LoadTemplateLibrary(state.TemplateName);
        UpdatePreview();

        SelectedTemplatePath = libraryPath;
        SetStatus($"تم حفظ القالب «{state.TemplateName}» وتجهيزه لإصدار الكروت.", isSuccess: true);

        return libraryPath;
    }

    public bool LoadTemplateFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        try
        {
            var state = JsonSerializer.Deserialize<TemplateDesignState>(
                File.ReadAllText(path), JsonOptions);

            if (state is null)
                return false;

            ApplyTemplateState(state);
            SelectedTemplatePath = System.IO.Path.GetFullPath(path);

            SetStatus($"تم تحميل القالب «{state.TemplateName}».", isSuccess: true);
            return true;
        }
        catch (Exception ex)
        {
            SetStatus("تعذر تحميل القالب: " + ex.Message, isSuccess: false);
            return false;
        }
    }


    private void EnsureRasterTemplateBase()
    {
        if (_isRasterTemplateMode && !string.IsNullOrWhiteSpace(_rasterTemplateImagePath) && File.Exists(_rasterTemplateImagePath))
        {
            ApplyRasterTemplateModeVisibility();
            return;
        }

        var bytes = RenderTemplateBasePng();
        Directory.CreateDirectory(TemplateAssetsDirectory);
        var safeName = MakeSafeTemplateFileName(TemplateNameTextBox.Text);
        var imagePath = System.IO.Path.Combine(TemplateAssetsDirectory, safeName + "-base.png");
        File.WriteAllBytes(imagePath, bytes);
        _rasterTemplateImagePath = imagePath;
        _importedTemplateImagePath = null;
        _isRasterTemplateMode = true;
        LoadBackgroundImageFromPath(imagePath);
        ApplyRasterTemplateModeVisibility();
    }

    private byte[] RenderTemplateBasePng()
    {
        var visibility = new Dictionary<FrameworkElement, Visibility>();
        foreach (var tag in GetAllEditableElementTags())
        {
            if (GetElementByTag(tag) is FrameworkElement element)
            {
                visibility[element] = element.Visibility;
                element.Visibility = Visibility.Collapsed;
            }
        }
        if (PreviewUsernameCaptionTextBlock is not null)
        {
            visibility[PreviewUsernameCaptionTextBlock] = PreviewUsernameCaptionTextBlock.Visibility;
            PreviewUsernameCaptionTextBlock.Visibility = Visibility.Collapsed;
        }
        if (SelectionFrameBorder is not null)
        {
            visibility[SelectionFrameBorder] = SelectionFrameBorder.Visibility;
            SelectionFrameBorder.Visibility = Visibility.Collapsed;
        }

        RemoveResizeHandles();
        var oldTransform = CardDesignCanvas.RenderTransform;
        var oldOrigin = CardDesignCanvas.RenderTransformOrigin;
        try
        {
            CardDesignCanvas.RenderTransform = Transform.Identity;
            CardDesignCanvas.RenderTransformOrigin = new Point(0, 0);
            var width = Math.Max(1d, CardDesignCanvas.Width);
            var height = Math.Max(1d, CardDesignCanvas.Height);
            CardDesignCanvas.Measure(new Size(width, height));
            CardDesignCanvas.Arrange(new Rect(0, 0, width, height));
            CardDesignCanvas.UpdateLayout();
            const double renderScale = 4d;
            var bitmap = new RenderTargetBitmap(
                Math.Max(1, (int)Math.Ceiling(width * renderScale)),
                Math.Max(1, (int)Math.Ceiling(height * renderScale)),
                96d * renderScale, 96d * renderScale, PixelFormats.Pbgra32);
            bitmap.Render(CardDesignCanvas);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        finally
        {
            CardDesignCanvas.RenderTransform = oldTransform;
            CardDesignCanvas.RenderTransformOrigin = oldOrigin;
            foreach (var pair in visibility) pair.Key.Visibility = pair.Value;
            UpdateSelectionFrame();
        }
    }

    private string? GetRasterTemplateImageBase64()
    {
        if (string.IsNullOrWhiteSpace(_rasterTemplateImagePath) || !File.Exists(_rasterTemplateImagePath)) return null;
        var info = new FileInfo(_rasterTemplateImagePath);
        return info.Length <= MaxImportedImageBytes * 2 ? Convert.ToBase64String(File.ReadAllBytes(_rasterTemplateImagePath)) : null;
    }

    private string? RestoreRasterTemplateFromState(TemplateDesignState state)
    {
        if (!string.IsNullOrWhiteSpace(state.RasterTemplateImagePath) && File.Exists(state.RasterTemplateImagePath))
            return state.RasterTemplateImagePath;
        if (string.IsNullOrWhiteSpace(state.RasterTemplateImageBase64)) return null;
        var bytes = Convert.FromBase64String(state.RasterTemplateImageBase64);
        if (bytes.LongLength > MaxImportedImageBytes * 2) throw new InvalidDataException("صورة القالب المحفوظ أكبر من الحد المسموح.");
        Directory.CreateDirectory(TemplateAssetsDirectory);
        var path = System.IO.Path.Combine(TemplateAssetsDirectory, "template-base-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private void ApplyRasterTemplateModeVisibility()
    {
        if (!_isRasterTemplateMode) return;
        TemplateBackgroundImage.Visibility = Visibility.Visible;
        foreach (var tag in GetAllEditableElementTags())
        {
            if (IsDynamicElementTag(tag)) continue;
            var element = GetElementByTag(tag);
            if (element is not null) element.Visibility = Visibility.Collapsed;
        }
        // The background art is flattened; only these independent data fields stay editable.
        PreviewLoginCodeTextBlock.Visibility = Visibility.Collapsed;
        PreviewUsernameTextBlock.Visibility = IsTemplateElementVisible("اسم المستخدم") ? Visibility.Visible : Visibility.Collapsed;
        PreviewUsernameCaptionTextBlock.Visibility = PreviewUsernameTextBlock.Visibility;
        PreviewPasswordTextBlock.Visibility = IsTemplateElementVisible("كلمة المرور") ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string MakeSafeTemplateFileName(string? name)
    {
        var value = string.IsNullOrWhiteSpace(name) ? DefaultTemplateName : name.Trim();
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(ch => invalid.Contains(ch) || char.IsControl(ch) ? '_' : ch).ToArray()).Trim().Trim('.');
        if (string.IsNullOrWhiteSpace(safe)) safe = DefaultTemplateName;
        return safe.Length > 70 ? safe[..70] : safe;
    }

    private void LoadTemplateLibrary(string? selectName = null)
    {
        if (SavedTemplateComboBox is null) return;
        _isLoadingTemplateLibrary = true;
        try
        {
            var entries = new List<SavedTemplateEntry>();
            if (Directory.Exists(TemplateLibraryDirectory))
            {
                foreach (var path in Directory.GetFiles(TemplateLibraryDirectory, "*.json").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        var state = JsonSerializer.Deserialize<TemplateDesignState>(File.ReadAllText(path));
                        if (state is null || string.IsNullOrWhiteSpace(state.TemplateName)) continue;
                        entries.Add(new SavedTemplateEntry { Name = state.TemplateName, Path = path });
                    }
                    catch { /* Ignore one damaged library entry while keeping the rest available. */ }
                }
            }
            if (File.Exists(StoragePath))
            {
                try
                {
                    var state = JsonSerializer.Deserialize<TemplateDesignState>(File.ReadAllText(StoragePath));
                    if (state is not null && !string.IsNullOrWhiteSpace(state.TemplateName) &&
                        !entries.Any(item => string.Equals(item.Name, state.TemplateName, StringComparison.OrdinalIgnoreCase)))
                        entries.Add(new SavedTemplateEntry { Name = state.TemplateName + " (الحالي)", Path = StoragePath });
                }
                catch { }
            }
            SavedTemplateComboBox.ItemsSource = entries.OrderBy(item => item.Name, StringComparer.CurrentCulture).ToList();
            var selected = entries.FirstOrDefault(item => string.Equals(item.Name, selectName, StringComparison.OrdinalIgnoreCase))
                ?? entries.FirstOrDefault(item => string.Equals(item.Name.Replace(" (الحالي)", string.Empty, StringComparison.OrdinalIgnoreCase), TemplateNameTextBox.Text, StringComparison.OrdinalIgnoreCase));
            SavedTemplateComboBox.SelectedItem = selected;
        }
        finally { _isLoadingTemplateLibrary = false; }
    }

    private void SavedTemplateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isReady || _isLoadingTemplateLibrary || SavedTemplateComboBox.SelectedItem is not SavedTemplateEntry entry) return;
        try
        {
            var state = JsonSerializer.Deserialize<TemplateDesignState>(File.ReadAllText(entry.Path));
            if (state is null) throw new InvalidDataException("ملف القالب فارغ أو غير صالح.");
            ApplyTemplateState(state);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StoragePath)!);
            File.WriteAllText(StoragePath, JsonSerializer.Serialize(CaptureState(), JsonOptions));
            SetStatus($"تم تحميل القالب «{state.TemplateName}» وربطه بإعداداته المحفوظة.", isSuccess: true);
        }
        catch (Exception ex)
        {
            SetStatus("تعذر تحميل القالب: " + ex.Message, isSuccess: false);
        }
    }
    private void NewTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        ResetToDefaults();
        SetStatus("قالب جديد غير محفوظ. لن يتغير القالب المحفوظ إلا عند الضغط على حفظ القالب.", isSuccess: true);
    }
    private void DeleteTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        // Never infer a deletion target from editable ComboBox text. Only a real,
        // explicitly selected library entry is eligible for deletion.
        if (SavedTemplateComboBox.SelectedItem is not SavedTemplateEntry selectedEntry ||
            string.IsNullOrWhiteSpace(selectedEntry.Path))
        {
            SetStatus("اختر قالبًا محفوظًا من قائمة القوالب أولًا؛ لن يُحذف القالب الحالي بمجرد كتابة اسمه.", isSuccess: false);
            MessageBox.Show(
                "لم يتم تحديد قالب محفوظ للحذف. اختر قالبًا من قائمة القوالب ثم حاول مجددًا.",
                "لم يتم تحديد قالب",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            var libraryRoot = System.IO.Path.GetFullPath(TemplateLibraryDirectory);
            var libraryPrefix = libraryRoot.TrimEnd(
                System.IO.Path.DirectorySeparatorChar,
                System.IO.Path.AltDirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
            var selectedPath = System.IO.Path.GetFullPath(selectedEntry.Path);

            // Block deleting StoragePath or any file outside the dedicated template library.
            if (!selectedPath.StartsWith(libraryPrefix, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("الحذف متاح للقوالب الموجودة في مكتبة القوالب فقط.", isSuccess: false);
                MessageBox.Show(
                    "هذا الملف ليس قالبًا داخل مكتبة القوالب، لذلك لم يتم حذفه.",
                    "عملية حذف غير مسموحة",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!File.Exists(selectedPath))
            {
                LoadTemplateLibrary(TemplateNameTextBox.Text?.Trim());
                SetStatus("ملف القالب المحدد غير موجود؛ تم تحديث قائمة القوالب.", isSuccess: false);
                return;
            }

            var templateName = selectedEntry.Name.Trim();
            var firstAnswer = MessageBox.Show(
                $"أنت على وشك حذف القالب «{templateName}» فقط.\n\nلن يتم حذف الكروت أو الدفعات أو أي قالب آخر. هل تريد المتابعة؟",
                "تأكيد حذف القالب",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (firstAnswer != MessageBoxResult.Yes) return;

            var finalAnswer = MessageBox.Show(
                $"تأكيد نهائي: حذف «{templateName}» من مكتبة القوالب؟\n\nلا يمكن التراجع عن حذف هذا الملف.",
                "تأكيد نهائي للحذف",
                MessageBoxButton.YesNo,
                MessageBoxImage.Stop);
            if (finalAnswer != MessageBoxResult.Yes) return;

            var currentName = TemplateNameTextBox.Text?.Trim();
            var deletesActiveTemplate = string.Equals(
                templateName,
                currentName,
                StringComparison.OrdinalIgnoreCase);

            File.Delete(selectedPath);

            if (deletesActiveTemplate)
            {
                // Keep the separate active-template snapshot valid and replace it
                // with the built-in default rather than deleting StoragePath.
                ResetToDefaults();
                SelectedTemplatePath = null;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(StoragePath)!);
                File.WriteAllText(StoragePath, JsonSerializer.Serialize(CaptureState(), JsonOptions));
                LoadTemplateLibrary(DefaultTemplateName);
            }
            else
            {
                // Deleting a different library item must not reset the design in progress.
                LoadTemplateLibrary(currentName);
            }

            SetStatus(
                deletesActiveTemplate
                    ? $"تم حذف القالب «{templateName}» فقط، وتم إبقاء القالب الافتراضي محفوظًا."
                    : $"تم حذف القالب «{templateName}» فقط؛ بقي التصميم الحالي دون تغيير.",
                isSuccess: true);
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
        ColorTheme = GetSelectedTag(ColorThemeComboBox) ?? "blue",
        FontFamily = GetSelectedTag(FontFamilyComboBox) ?? "Segoe UI",
        FontSize = ParseDouble(ElementFontSizeTextBox.Text, 11, 8, 28),
        CardWidthMm = ParseDouble(CardWidthTextBox.Text, 92.1, 10, 200),
        CardHeightMm = ParseDouble(CardHeightTextBox.Text, 52, 10, 200),
        PrintColumns = ParseInt(PrintColumnsTextBox.Text, 2, 1, 8),
        PrintRows = ParseInt(PrintRowsTextBox.Text, 5, 1, 100),
        LayoutRevision = 6,
        CardRotationDegrees = ParseDouble(CardRotationTextBox.Text, 0, -180, 180),
        PageMarginTopMm = ParseDouble(PrintMarginTopTextBox.Text, 10, 0, 40),
        PageMarginBottomMm = ParseDouble(PrintMarginBottomTextBox.Text, 10, 0, 40),
        PageMarginLeftMm = ParseDouble(PrintMarginLeftTextBox.Text, 10, 0, 40),
        PageMarginRightMm = ParseDouble(PrintMarginRightTextBox.Text, 10, 0, 40),
        ImportedBackgroundImagePath = _isRasterTemplateMode ? null : _importedTemplateImagePath,
        ImportedBackgroundImageBase64 = _isRasterTemplateMode ? null : GetImportedImageBase64(),
        ImportedBackgroundImageExtension = string.IsNullOrWhiteSpace(_importedTemplateImagePath) ? ".png" : System.IO.Path.GetExtension(_importedTemplateImagePath),
        IsRasterTemplateMode = _isRasterTemplateMode,
        RasterTemplateImagePath = _rasterTemplateImagePath,
        RasterTemplateImageBase64 = _isRasterTemplateMode ? GetRasterTemplateImageBase64() : null,
        LinkedPackageKey = GetActivePackage()?.Key ?? string.Empty,
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
        // The built-in Default_Design intentionally receives the current studio layout once.
        // Custom named templates keep their geometry. Existing JSON is not rewritten until
        // the user chooses Save, so this migration does not silently overwrite the source file.
        var isUnmigratedBuiltInDefault = string.Equals(state.TemplateName, DefaultTemplateName, StringComparison.OrdinalIgnoreCase)
            && state.LayoutRevision < 4;
        var requiresDipCoordinateMigration = !string.Equals(state.Format, TemplateFormatId, StringComparison.Ordinal);
        if (isUnmigratedBuiltInDefault)
        {
            var newProperties = CreateDefaultElementProperties();
            if (state.ElementProperties is not null)
            {
                foreach (var pair in newProperties)
                {
                    if (!state.ElementProperties.TryGetValue(pair.Key, out var oldProperty) || oldProperty is null)
                        continue;
                    // Keep authored labels and preferred font families, but replace outdated
                    // positions/sizes/font sizes with the clean studio layout.
                    if (oldProperty.Text is not null && pair.Key != "username" && pair.Key != "login") pair.Value.Text = oldProperty.Text;
                    if (!string.IsNullOrWhiteSpace(oldProperty.FontFamily)) pair.Value.FontFamily = oldProperty.FontFamily;
                    if (pair.Key is "login" or "price" or "validity") pair.Value.IsBold = oldProperty.IsBold || pair.Value.IsBold;
                }
            }
            state.ElementProperties = newProperties;
            state.CardWidthMm = 46d;
            state.CardHeightMm = 15.8d;
            state.ColorTheme = "blue";
            state.TemplateStyle = "classic";
            state.PrintColumns = 3;
            state.PrintRows = 17;
            // Migrate the built-in card to the independent-layer design. Do not turn
            // the sample banner and fields into one baked image: clients can edit every field.
            state.VisibleElements = CreateDefaultVisibility();
            state.IsRasterTemplateMode = false;
            state.RasterTemplateImagePath = null;
            state.RasterTemplateImageBase64 = null;
            state.ImportedBackgroundImagePath = File.Exists(DefaultStarterTemplateImagePath) ? DefaultStarterTemplateImagePath : null;
            state.ImportedBackgroundImageBase64 = null;
            state.ImportedBackgroundImageExtension = ".png";
            state.Format = TemplateFormatId;
            state.LayoutRevision = 6;
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
        PrintColumnsTextBox.Text = ParseInt(state.PrintColumns.ToString(CultureInfo.InvariantCulture), 2, 1, 8).ToString(CultureInfo.InvariantCulture);
        PrintRowsTextBox.Text = ParseInt(state.PrintRows.ToString(CultureInfo.InvariantCulture), 5, 1, 100).ToString(CultureInfo.InvariantCulture);
        _studioPreviewPageNumber = 1;
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
        if (!string.IsNullOrWhiteSpace(state.LinkedPackageKey))
        {
            var linkedHotspot = HotspotPackageComboBox.Items.OfType<DemoCardPackage>()
                .FirstOrDefault(item => string.Equals(item.Key, state.LinkedPackageKey, StringComparison.OrdinalIgnoreCase));
            var linkedUserManager = UserManagerPackageComboBox.Items.OfType<DemoCardPackage>()
                .FirstOrDefault(item => string.Equals(item.Key, state.LinkedPackageKey, StringComparison.OrdinalIgnoreCase));
            if (_activePackageSource == "user-manager" && linkedUserManager is not null)
                UserManagerPackageComboBox.SelectedItem = linkedUserManager;
            else if (_activePackageSource != "user-manager" && linkedHotspot is not null)
                HotspotPackageComboBox.SelectedItem = linkedHotspot;
            else if (linkedHotspot is not null)
            {
                HotspotPackageComboBox.SelectedItem = linkedHotspot;
                _activePackageSource = "hotspot";
            }
            else if (linkedUserManager is not null)
            {
                UserManagerPackageComboBox.SelectedItem = linkedUserManager;
                _activePackageSource = "user-manager";
            }
        }
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

        // Product rule: username and card number are the same login identifier.
        if (IsTemplateElementVisible("رمز الدخول")) _visibleElements["اسم المستخدم"] = true;
        _visibleElements["رمز الدخول"] = false;
        // Backward-compatible migration: login and username refer to the same account
        // identifier in this system, so do not render two copies on the default template.
        if (state.LayoutRevision < 5 && string.Equals(state.TemplateName, DefaultTemplateName, StringComparison.OrdinalIgnoreCase))
        {
            // Older saves baked a sample card into the image and exposed both login
            // and username as duplicate fields. Upgrade only the built-in default:
            // preserve user element positions but replace the baked image with the
            // bundled artwork as a clean, reusable background.
            _visibleElements["رمز الدخول"] = false;
            _visibleElements["اسم المستخدم"] = true;
            if (File.Exists(DefaultStarterTemplateImagePath))
            {
                state.IsRasterTemplateMode = false;
                state.RasterTemplateImagePath = null;
                state.RasterTemplateImageBase64 = null;
                state.ImportedBackgroundImagePath = DefaultStarterTemplateImagePath;
                state.ImportedBackgroundImageBase64 = null;
                state.ImportedBackgroundImageExtension = ".png";
            }
            state.LayoutRevision = 5;
        }

        _isRasterTemplateMode = state.IsRasterTemplateMode;
        _rasterTemplateImagePath = _isRasterTemplateMode ? RestoreRasterTemplateFromState(state) : null;
        _importedTemplateImagePath = _isRasterTemplateMode ? null : RestoreBackgroundFromState(state);
        var backgroundPath = _isRasterTemplateMode ? _rasterTemplateImagePath : _importedTemplateImagePath;
        if (!string.IsNullOrWhiteSpace(backgroundPath) && File.Exists(backgroundPath))
            LoadBackgroundImageFromPath(backgroundPath);
        else
            TemplateBackgroundImage.Source = null;
        ApplyImportedBackgroundAppearance();

        RestoreCustomElements(state.ElementProperties);
        ApplyElementProperties(CreateDefaultElementProperties());
        if (state.ElementProperties is { Count: > 0 }) ApplyElementProperties(state.ElementProperties);
        var restoredSelectedTag = string.IsNullOrWhiteSpace(state.SelectedElementTag) || string.Equals(state.SelectedElementTag, "login", StringComparison.Ordinal)
            ? "username"
            : state.SelectedElementTag;
        SelectComboTag(SelectedElementComboBox, restoredSelectedTag);
        _studioSelectedFieldTag = restoredSelectedTag;
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
        var targetCanvasWidth = DesignBaseWidth * ParseDouble(CardWidthTextBox.Text, 46, 10, 200) / BaseWidthMm;
        var targetCanvasHeight = DesignBaseHeight * ParseDouble(CardHeightTextBox.Text, 15.8, 10, 200) / BaseHeightMm;
        // V2 state stores coordinates in the actual canvas. Legacy V1 state stores base-canvas
        // coordinates, so let ApplyCardDimensionTransform map those positions once on migration.
        if (!isLegacyCoordinateState)
        {
            _lastCanvasWidth = targetCanvasWidth;
            _lastCanvasHeight = targetCanvasHeight;
        }
        ApplyCardDimensionTransform();
        UpdatePreview();
        SyncFieldToggleButtons();
        SyncSelectedElementEditor();
    }
    private void ResetToDefaults()
    {
        RestoreCustomElements(null);
        _visibleElements.Clear();
        foreach (var pair in CreateDefaultVisibility()) _visibleElements[pair.Key] = pair.Value;
        _importedTemplateImagePath = File.Exists(DefaultStarterTemplateImagePath) ? DefaultStarterTemplateImagePath : null;
        _rasterTemplateImagePath = null;
        _isRasterTemplateMode = false;
        TemplateBackgroundImage.Source = null;
        TemplateBackgroundImage.Visibility = Visibility.Collapsed;
        if (_importedTemplateImagePath is not null) LoadBackgroundImageFromPath(_importedTemplateImagePath);
        TemplateNameTextBox.Text = DefaultTemplateName;
        SelectComboTag(TemplateStyleComboBox, "classic");
        SelectComboTag(ColorThemeComboBox, "blue");
        SelectComboTag(FontFamilyComboBox, "Segoe UI");
        ElementFontSizeTextBox.Text = "11";
        CardWidthTextBox.Text = "92.1";
        CardHeightTextBox.Text = "52";
        CardRotationTextBox.Text = "0";
        PrintColumnsTextBox.Text = "2";
        PrintRowsTextBox.Text = "5";
        PrintTotalCardsTextBox.Text = "153";
        PrintMarginTopTextBox.Text = "10";
        PrintMarginBottomTextBox.Text = "10";
        PrintMarginLeftTextBox.Text = "10";
        PrintMarginRightTextBox.Text = "10";
        _studioPreviewPageNumber = 1;
        // Begin from the 60 × 35 mm design coordinate system so the first transform
        // applies the same proportional remap used when the customer changes card size.
        _lastCanvasWidth = DesignBaseWidth;
        _lastCanvasHeight = DesignBaseHeight;
        ApplyElementProperties(CreateDefaultElementProperties());
        ApplyCardDimensionTransform();
        ApplyDefaultStarterLayout();
        _zoom = 1d;
        ZoomSlider.Value = 1d;
        HotspotPackageComboBox.SelectedIndex = 0;
        UserManagerPackageComboBox.SelectedIndex = 0;
        _activePackageSource = "hotspot";
        _loginPageUrl = DefaultLoginUrl;
        _barcodeText = "50723015";
        SelectComboTag(SelectedElementComboBox, "username");
        _studioSelectedFieldTag = "username";
        UpdateSamplePackageFields();
        SyncSelectedElementEditor();
        SyncMainElementChecks();
        ApplyImportedBackgroundAppearance();
        ApplyCardDimensionTransform();
        UpdatePreview();
        SyncFieldToggleButtons();
        SyncSelectedElementEditor();
    }

    private void ApplyDefaultStarterLayout()
    {
        // Arrange the default fields in the open area of the blue-wave background.
        void Place(string tag, double x, double y, double width, double height, double fontSize, string? text = null)
        {
            if (GetElementByTag(tag) is not FrameworkElement element) return;
            Canvas.SetLeft(element, x);
            Canvas.SetTop(element, y);
            element.Width = width;
            element.Height = height;
            if (element is TextBlock tb)
            {
                tb.FontSize = fontSize;
                if (text is not null) tb.Text = text;
            }
        }
        Place("username", 56, 12, 87, 12, 8.5, "50723015");
        Place("password", 56, 24, 87, 9, 7.0, "كلمة المرور: 1432");
        Place("validity", 56, 34, 41, 7, 5.9, "الصلاحية: 7 أيام");
        Place("time", 98, 34, 43, 7, 5.9, "الوقت: 1 ساعة");
        Place("download", 56, 42, 43, 7, 5.4, "التحميل: —");
        Place("package", 100, 42, 41, 7, 5.4, "الباقة: —");
        Place("contact", 56, 50, 43, 7, 5.0, "777 777 777");
        Place("price", 100, 50, 41, 7, 5.0, "السعر: —");
        // Keep the brand inside the dark-blue left ribbon of the bundled artwork.
        // The username and package facts occupy the bright, printable center/right area.
        Place("logoBadge", 8, 6, 18, 18, 6, null);
        Place("logoMark", 10, 10, 14, 10, 7, "TC");
        Place("brand", 2, 28, 40, 8, 5.7, "TAB CARD");
        Place("networkName", 3, 36, 39, 13, 5.2, "اسم الشبكة");
        KeepUsernameCaptionAligned();
        UpdatePreview();
    }

}
