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

// Responsibility: Designer template integration and package/offer navigation.
public partial class CardIssuanceView
{
    private static Dictionary<string, bool> CreateDefaultTemplateVisibility() => new(StringComparer.Ordinal)
    {
        ["رمز الدخول"] = true, ["اسم المستخدم"] = true, ["كلمة المرور"] = true,
        ["الصلاحية"] = true, ["الوقت"] = false, ["السعر"] = true, ["التحميل"] = false,
        ["الباقة"] = true, ["الرقم التسلسلي"] = false, ["تاريخ الطباعة"] = false,
        ["رقم الدفعة"] = false, ["نقطة البيع"] = false, ["معلومات إضافية"] = false,
        ["كود المكافأة"] = false, ["رمز QR"] = true, ["الباركود"] = false,
        ["شعار الشبكة"] = true, ["الشريط العلوي"] = true, ["الفاصل"] = true
    };
    private sealed record IssuanceTemplateChoice(string Name, string FilePath);
    private bool _refreshingTemplateChoices;

    private static string TemplateLibraryDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexoraProductDemo", "templates", "library");
    private static string PackageTemplateBindingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexoraProductDemo", "templates", "package-template-bindings.json");

    private bool IsTemplateElementVisible(string key) =>
        _templateVisibleElements.TryGetValue(key, out var visible) && visible;

    private void LoadDesignerTemplateDefinition(bool refreshTemplateChoices = true)
    {
        _templateVisibleElements.Clear();
        foreach (var pair in CreateDefaultTemplateVisibility()) _templateVisibleElements[pair.Key] = pair.Value;
        _designerTemplateName = "القالب الافتراضي";
        _designerTemplateStyle = "classic";
        _designerColorTheme = "red";

        try
        {
            if (File.Exists(TemplateDesignStoragePath))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(TemplateDesignStoragePath));
                var root = document.RootElement;
                if (root.TryGetProperty("TemplateName", out var name) && name.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(name.GetString()))
                    _designerTemplateName = name.GetString()!.Trim();
                if (root.TryGetProperty("TemplateStyle", out var style) && style.ValueKind == JsonValueKind.String)
                    _designerTemplateStyle = style.GetString() ?? "classic";
                if (root.TryGetProperty("ColorTheme", out var theme) && theme.ValueKind == JsonValueKind.String)
                    _designerColorTheme = theme.GetString() ?? "red";
                if (root.TryGetProperty("VisibleElements", out var visibleElements) && visibleElements.ValueKind == JsonValueKind.Object)
                {
                    foreach (var item in visibleElements.EnumerateObject())
                        if (item.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                            _templateVisibleElements[item.Name] = item.Value.GetBoolean();
                }
            }
            if (refreshTemplateChoices)
                RefreshTemplateChoices(preferredName: _designerTemplateName);
        }
        catch (Exception ex)
        {
            SetStatus("تعذر قراءة إعدادات القالب المحفوظ؛ ستُستخدم الحقول الافتراضية. " + ex.Message, true);
        }

        if (PrintTemplateComboBox is not null)
            PrintTemplateComboBox.ToolTip = "اختر قالبًا محفوظًا، وسيُحمّل للتصميم ومعاينة الطباعة";
    }

    private static string SafeTemplateFileName(string templateName)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safe = new string((string.IsNullOrWhiteSpace(templateName) ? "القالب الافتراضي" : templateName.Trim())
            .Select(character => invalid.Contains(character) ? '-' : character).ToArray()).Trim().Trim('.', ' ');
        return (string.IsNullOrWhiteSpace(safe) ? "قالب" : safe) + ".json";
    }

    private static string ReadTemplateName(string filePath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(filePath));
            if (document.RootElement.TryGetProperty("TemplateName", out var name) &&
                name.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(name.GetString()))
                return name.GetString()!.Trim();
        }
        catch { }
        return Path.GetFileNameWithoutExtension(filePath);
    }

    private void RefreshTemplateChoices(string? preferredName = null, string? preferredPath = null)
    {
        if (PrintTemplateComboBox is null) return;
        Directory.CreateDirectory(TemplateLibraryDirectory);

        // Migrate the single-template store into the library once, so older users keep their design.
        if (File.Exists(TemplateDesignStoragePath))
        {
            var currentName = ReadTemplateName(TemplateDesignStoragePath);
            var libraryPath = Path.Combine(TemplateLibraryDirectory, SafeTemplateFileName(currentName));
            if (!string.Equals(Path.GetFullPath(TemplateDesignStoragePath), Path.GetFullPath(libraryPath), StringComparison.OrdinalIgnoreCase) &&
                !File.Exists(libraryPath))
                File.Copy(TemplateDesignStoragePath, libraryPath, overwrite: false);
        }

        var choices = Directory.GetFiles(TemplateLibraryDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .Select(path => new IssuanceTemplateChoice(ReadTemplateName(path), path))
            .OrderBy(choice => choice.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        if (choices.Count == 0)
        {
            // Never leave a selectable "default" item pointing at a file that does not exist.
            // Persist the designer's real built-in design once so preview and PDF can share it.
            var defaultDesigner = new Nexora.Demo.Desktop.Features.Printing.Views.TemplateDesignerView();
            var defaultPath = defaultDesigner.SaveTemplateToLibraryForIssuance();
            choices.Add(new IssuanceTemplateChoice(ReadTemplateName(defaultPath), defaultPath));
        }

        _refreshingTemplateChoices = true;
        try
        {
            PrintTemplateComboBox.ItemsSource = null;
            PrintTemplateComboBox.Items.Clear();
            var items = choices.Select(choice => new ComboBoxItem { Content = choice.Name, Tag = choice.FilePath }).ToList();
            foreach (var item in items) PrintTemplateComboBox.Items.Add(item);

            var selectedPath = preferredPath;
            var selectedName = preferredName;
            // An explicit preferred name is used after saving a design and must win over
            // the package's previous association. Package associations are restored later,
            // once the actual package selection has been loaded.
            if (string.IsNullOrWhiteSpace(selectedPath) && string.IsNullOrWhiteSpace(selectedName))
            {
                var associatedFileName = GetAssociatedTemplateFileNameForCurrentPackage();
                if (!string.IsNullOrWhiteSpace(associatedFileName))
                    selectedPath = choices.FirstOrDefault(choice => string.Equals(Path.GetFileName(choice.FilePath), associatedFileName, StringComparison.OrdinalIgnoreCase))?.FilePath;
            }
            var selected = !string.IsNullOrWhiteSpace(selectedPath)
                ? items.FirstOrDefault(item => string.Equals(item.Tag?.ToString(), selectedPath, StringComparison.OrdinalIgnoreCase))
                : null;
            selected ??= !string.IsNullOrWhiteSpace(selectedName)
                ? items.FirstOrDefault(item => string.Equals(item.Content?.ToString(), selectedName, StringComparison.CurrentCultureIgnoreCase))
                : null;
            selected ??= items.FirstOrDefault(item => string.Equals(item.Tag?.ToString(), TemplateDesignStoragePath, StringComparison.OrdinalIgnoreCase));
            selected ??= items.FirstOrDefault();
            PrintTemplateComboBox.SelectedItem = selected;
        }
        finally { _refreshingTemplateChoices = false; }
    }

    private string? GetAssociatedTemplateFileNameForCurrentPackage()
    {
        if (PackageComboBox?.SelectedItem is not DemoCardPackage package) return null;
        var provider = _targetSystem == DemoAccountSystem.HotSpot ? "hotspot" : "user-manager";
        var key = provider + "|" + package.Key;
        try
        {
            if (!File.Exists(PackageTemplateBindingsPath)) return null;
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(PackageTemplateBindingsPath));
            return map is not null && map.TryGetValue(key, out var fileName) ? fileName : null;
        }
        catch { return null; }
    }

    private void SaveTemplateAssociation(string? filePath = null)
    {
        if (PackageComboBox?.SelectedItem is not DemoCardPackage package) return;
        filePath ??= (PrintTemplateComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (string.IsNullOrWhiteSpace(filePath)) return;
        var provider = _targetSystem == DemoAccountSystem.HotSpot ? "hotspot" : "user-manager";
        var key = provider + "|" + package.Key;
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(PackageTemplateBindingsPath))
                map = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(PackageTemplateBindingsPath))
                    ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch { }
        map[key] = Path.GetFileName(filePath);
        Directory.CreateDirectory(Path.GetDirectoryName(PackageTemplateBindingsPath)!);
        File.WriteAllText(PackageTemplateBindingsPath, JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void ApplyTemplateAssociationForSelectedPackage()
    {
        var associatedFileName = GetAssociatedTemplateFileNameForCurrentPackage();
        if (string.IsNullOrWhiteSpace(associatedFileName) || PrintTemplateComboBox is null) return;
        var item = PrintTemplateComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(option =>
            string.Equals(Path.GetFileName(option.Tag?.ToString() ?? string.Empty), associatedFileName, StringComparison.OrdinalIgnoreCase));
        if (item is not null && !ReferenceEquals(PrintTemplateComboBox.SelectedItem, item))
            PrintTemplateComboBox.SelectedItem = item;
    }

    private void PrintTemplateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_refreshingTemplateChoices || PrintTemplateComboBox.SelectedItem is not ComboBoxItem selected)
            return;

        if (selected.Tag is not string sourcePath || !File.Exists(sourcePath))
        {
            SetStatus("ملف القالب المختار غير موجود. أعد اختيار قالب محفوظ أو افتح المصمم.", true);
            return;
        }

        try
        {
            LoadSelectedTemplate(sourcePath, persistAssociation: true);
            UpdatePreview();
            SetStatus($"تم تحميل قالب «{_designerTemplateName}» وتحديث معاينة الكرت الفعلية.", false);
        }
        catch (Exception ex)
        {
            SetStatus("تعذر تحميل القالب المختار: " + ex.Message, true);
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "تعذر تحميل القالب", ex.Message,
                WorkflowFeedbackKind.Error, null, new WorkflowFeedbackAction("close", "إغلاق", true));
        }
    }

    private void LoadSelectedTemplate(string sourcePath, bool persistAssociation)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("ملف القالب المختار غير موجود.", sourcePath);

        var sourceFullPath = Path.GetFullPath(sourcePath);
        var activeFullPath = Path.GetFullPath(TemplateDesignStoragePath);
        Directory.CreateDirectory(Path.GetDirectoryName(activeFullPath)!);

        // The PDF renderer reads the active definition file; keep it byte-for-byte aligned
        // with the selected library template while remembering which library item is active.
        if (!string.Equals(sourceFullPath, activeFullPath, StringComparison.OrdinalIgnoreCase))
            File.Copy(sourceFullPath, activeFullPath, overwrite: true);

        _activeTemplatePath = sourceFullPath;
        LoadDesignerTemplateDefinition(refreshTemplateChoices: false);
        ApplyPrintTemplate();

        // Rebuild the renderer on every explicit template selection. A same-name edit may
        // update a file in place, so path-only caching is not sufficient.
        _selectedTemplateRenderer = new Nexora.Demo.Desktop.Features.Printing.Views.TemplateDesignerView();
        if (!_selectedTemplateRenderer.LoadTemplateFile(sourceFullPath))
            throw new InvalidDataException("لم يتمكن المصمم من قراءة ملف القالب المختار.");
        _selectedTemplateRenderer.ApplyIssuanceContext(_targetSystem, (PackageComboBox.SelectedItem as DemoCardPackage)?.Key ?? string.Empty);
        _selectedTemplateRendererPath = sourceFullPath;
        _selectedTemplateRendererLastWriteUtc = File.GetLastWriteTimeUtc(sourceFullPath);
        UpdatePreview();
        PersistCurrentPreferences();
        if (persistAssociation) SaveTemplateAssociation(sourceFullPath);
    }
    private void ApplyPrintTemplate()
    {
        if (CardPreviewBorder is null) return;

        // The saved Template Designer configuration supplies the current style and color theme.
        if (string.Equals(_designerTemplateStyle, "dark", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_designerColorTheme, "dark", StringComparison.OrdinalIgnoreCase))
        {
            CardPreviewBorder.Background = new SolidColorBrush(Color.FromRgb(55, 17, 24));
            CardPreviewBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(160, 24, 24));
            SetPreviewTextBrush(Brushes.White, new SolidColorBrush(Color.FromRgb(245, 218, 222)));
        }
        else if (string.Equals(_designerColorTheme, "white", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(_designerTemplateStyle, "light", StringComparison.OrdinalIgnoreCase))
        {
            CardPreviewBorder.Background = Brushes.White;
            CardPreviewBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(197, 27, 43));
            SetPreviewTextBrush(new SolidColorBrush(Color.FromRgb(32, 36, 42)), new SolidColorBrush(Color.FromRgb(105, 112, 120)));
        }
        else
        {
            CardPreviewBorder.Background = Brushes.White;
            CardPreviewBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(226, 199, 201));
            SetPreviewTextBrush(new SolidColorBrush(Color.FromRgb(32, 36, 42)), new SolidColorBrush(Color.FromRgb(109, 116, 128)));
        }
    }
    private void SetPreviewTextBrush(Brush primary, Brush secondary)
    {
        PreviewCredentialsCaptionTextBlock.Foreground = secondary;
        PreviewCardNumberTextBlock.Foreground = primary;
        PreviewUsernameTextBlock.Foreground = primary;
        PreviewPasswordTextBlock.Foreground = secondary;
        PreviewPackageTextBlock.Foreground = primary;
        PreviewBrandNameTextBlock.Foreground = primary;
        PreviewBrandSubtitleTextBlock.Foreground = secondary;
        PreviewCardTypeTextBlock.Foreground = primary;
        PreviewSourceTypeTextBlock.Foreground = secondary;
        PreviewHeaderDividerBorder.Background = secondary;
        PreviewFooterDividerBorder.Background = secondary;
    }
    private void OpenTemplateDesignerButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var owner = Window.GetWindow(this);
            var selectedPackage = PackageComboBox.SelectedItem as DemoCardPackage;
            var currentTemplatePath = (PrintTemplateComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            var designer = new Nexora.Demo.Desktop.Features.Printing.Views.TemplateDesignerView();
            if (!string.IsNullOrWhiteSpace(currentTemplatePath) && File.Exists(currentTemplatePath))
                designer.LoadTemplateFile(currentTemplatePath);
            designer.ApplyIssuanceContext(_targetSystem, selectedPackage?.Key ?? string.Empty);

            var ownerWidth = owner is not null && owner.ActualWidth > 700 ? owner.ActualWidth : 1180d;
            var ownerHeight = owner is not null && owner.ActualHeight > 500 ? owner.ActualHeight : 720d;
            var dialog = new Window
            {
                Title = "تصميم قالب الكرت — احفظ ثم اختر القالب",
                Content = designer,
                Owner = owner,
                WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.CanResize,
                ShowInTaskbar = false,
                FlowDirection = FlowDirection.RightToLeft,
                Background = new SolidColorBrush(Color.FromRgb(247, 248, 250)),
                Width = Math.Clamp(ownerWidth * 0.94, 900, 1320),
                Height = Math.Clamp(ownerHeight * 0.92, 620, 900),
                MinWidth = 880,
                MinHeight = 600
            };

            var accepted = dialog.ShowDialog() == true;
            if (!accepted || string.IsNullOrWhiteSpace(designer.SelectedTemplatePath) || !File.Exists(designer.SelectedTemplatePath))
                return;

            var chosenPath = Path.GetFullPath(designer.SelectedTemplatePath);
            RefreshTemplateChoices(preferredPath: chosenPath);
            var chosenItem = PrintTemplateComboBox.Items.OfType<ComboBoxItem>().FirstOrDefault(item =>
            {
                var itemPath = item.Tag?.ToString();
                return !string.IsNullOrWhiteSpace(itemPath) && File.Exists(itemPath) &&
                    string.Equals(Path.GetFullPath(itemPath), chosenPath, StringComparison.OrdinalIgnoreCase);
            });
            if (chosenItem is not null) PrintTemplateComboBox.SelectedItem = chosenItem;
            LoadSelectedTemplate(chosenPath, persistAssociation: true);
            SetStatus($"تم اختيار القالب «{_designerTemplateName}» وعرضه في شاشة الإصدار.", false);
        }
        catch (Exception ex)
        {
            SetStatus("تعذر فتح شاشة تصميم القالب: " + ex.Message, true);
            WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "تعذر فتح تصميم القالب", ex.Message,
                WorkflowFeedbackKind.Error, null, new WorkflowFeedbackAction("close", "إغلاق", true));
        }
    }
    private void AddPackageButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var providerKey = _targetSystem == DemoAccountSystem.HotSpot ? "hotspot" : "user-manager";
            var owner = Window.GetWindow(this);
            var before = DemoPackageStore.Load();
            var packageIdsBefore = new HashSet<string>(
                before.Packages.Select(package => package.Id?.ToString() ?? string.Empty),
                StringComparer.OrdinalIgnoreCase);
            var bindingKeysBefore = new HashSet<string>(
                before.Bindings.Select(binding => string.Join("|", binding.Id, binding.PackageId, binding.ProviderKey)),
                StringComparer.OrdinalIgnoreCase);

                        
            var dialog = new QuickPackageCreateWindow(providerKey)
            {
                Owner = owner,
                WindowStartupLocation = owner is null
                    ? WindowStartupLocation.CenterScreen
                    : WindowStartupLocation.CenterOwner
            };

            dialog.ShowDialog();


            var after = DemoPackageStore.Load();
            var createdPackage = after.Packages
                .Where(package => !package.IsArchived && !packageIdsBefore.Contains(package.Id?.ToString() ?? string.Empty))
                .OrderByDescending(package => package.CreatedAt)
                .FirstOrDefault();

            // Also support attaching an existing commercial definition to this system from the create screen.
            if (createdPackage is null)
            {
                var newBinding = after.Bindings
                    .Where(binding => string.Equals(binding.ProviderKey, providerKey, StringComparison.OrdinalIgnoreCase) &&
                        !bindingKeysBefore.Contains(string.Join("|", binding.Id, binding.PackageId, binding.ProviderKey)))
                    .OrderByDescending(binding => binding.UpdatedAt)
                    .FirstOrDefault();
                if (newBinding is not null)
                    createdPackage = after.Packages.FirstOrDefault(package =>
                        string.Equals(package.Id?.ToString(), newBinding.PackageId?.ToString(), StringComparison.OrdinalIgnoreCase));
            }

            if (createdPackage is not null)
            {
                RefreshPackageCatalog(preserveSelection: false, preferredKey: createdPackage.Id?.ToString());
                SetStatus("تم حفظ الباقة الجديدة وتحديث قائمة الباقات المتاحة للإصدار.", false);
                WorkflowFeedbackWindow.ShowNotice(Window.GetWindow(this), "تم حفظ الباقة", "حُفظ تعريف الباقة وتم تحديث قائمة الباقات المتاحة لهذا النظام.", WorkflowFeedbackKind.Success,
                    new[] { new KeyValuePair<string, string>("الباقة", createdPackage.Name), new KeyValuePair<string, string>("الوجهة", providerKey == "hotspot" ? "HotSpot" : "User Manager") },
                    new WorkflowFeedbackAction("close", "متابعة الإصدار", true));
            }
            else
            {
                RefreshPackageCatalog(preserveSelection: true);
                SetStatus("أُغلقت شاشة الباقات. تم تحديث القائمة إذا كانت البيانات قد تغيّرت.", false);
            }
        }
       catch (Exception ex)
{
    var diagnosticPath = System.IO.Path.Combine(
        System.IO.Path.GetTempPath(),
        "Nexora-QuickPackageCreate-error.txt");

    try
    {
        System.IO.File.WriteAllText(
            diagnosticPath,
            ex.ToString());
    }
    catch
    {
        // Keep the original error visible if writing the log fails.
    }

    SetStatus(
        "تعذر فتح نافذة إنشاء الباقة. راجع سجل التشخيص.",
        true);

    WorkflowFeedbackWindow.ShowNotice(
        Window.GetWindow(this),
        "تعذر فتح نافذة إنشاء الباقة",
        $"الخطأ: {ex.Message}\n" +
        $"ملف التفاصيل: {diagnosticPath}",
        WorkflowFeedbackKind.Error,
        null,
        new WorkflowFeedbackAction("close", "إغلاق", true));
    }
    }
    private void OffersButton_Click(object sender, RoutedEventArgs e)

    {

        var dialog = new IssuanceUtilityDialog(showOffers: true, currentPaperSize: _printPaperSize, currentColumns: _printColumns, showCutMarks: _printCutMarks);

        var owner = Window.GetWindow(this);

        if (owner is not null) dialog.Owner = owner;

        dialog.ShowDialog();

    }
}
