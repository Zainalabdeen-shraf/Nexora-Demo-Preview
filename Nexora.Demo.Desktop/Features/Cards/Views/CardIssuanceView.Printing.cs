using System.IO;
using System.Text.Json;
using System.Globalization;
using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.Win32;
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

// Responsibility: Local print preview document construction and print queue navigation.
public partial class CardIssuanceView
{
    private void OpenPrintQueueButton_Click(object sender, RoutedEventArgs e)
    {
        List<GeneratedDemoCard> cards;
        var targetName = _targetSystem == DemoAccountSystem.HotSpot ? "مستخدمو الهوتسبوت" : "مدير المستخدمين";
        var selectedBatch = RecentBatchesGrid.SelectedItem as GeneratedCardBatch;
        var batch = selectedBatch ?? _issuanceService.Batches
            .Where(item => string.Equals(item.TargetSystem, targetName, StringComparison.Ordinal))
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefault();

        if (batch is not null)
        {
            cards = _issuanceService.Cards
                .Where(card => string.Equals(card.BatchId, batch.BatchId, StringComparison.Ordinal))
                .OrderBy(card => card.CreatedAt).ToList();
        }
        else if (_lastGeneratedCards.Count > 0)
        {
            cards = _lastGeneratedCards.ToList();
        }
        else
        {
            cards = _issuanceService.Cards
                .Where(card => string.Equals(card.TargetSystem, targetName, StringComparison.Ordinal))
                .OrderByDescending(card => card.CreatedAt).Take(100).OrderBy(card => card.CreatedAt).ToList();
        }
        _ = ShowLocalPrintPreviewAsync(cards);
    }
    private async Task ShowLocalPrintPreviewAsync(List<GeneratedDemoCard> cards)
    {
        if (cards is null || cards.Count == 0)
        {
            SetStatus("لا توجد كروت للمعاينة. أنشئ دفعة أو اختر دفعة موجودة.", true);
            return;
        }

        var orderedCards = cards.ToList();
        var batchIds = string.Join("، ", orderedCards.Select(card => card.BatchId)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(5));
        var owner = Window.GetWindow(this);

        var selectedPackage = PackageComboBox.SelectedItem as DemoCardPackage;
        var renderer = new Nexora.Demo.Desktop.Features.Printing.Views.TemplateDesignerView();
        var activeDesignPath = _activeTemplatePath;
        if (string.IsNullOrWhiteSpace(activeDesignPath) || !File.Exists(activeDesignPath))
            activeDesignPath = (PrintTemplateComboBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (string.IsNullOrWhiteSpace(activeDesignPath) || !File.Exists(activeDesignPath))
            activeDesignPath = File.Exists(TemplateDesignStoragePath) ? TemplateDesignStoragePath : null;
        if (string.IsNullOrWhiteSpace(activeDesignPath) || !File.Exists(activeDesignPath))
            throw new FileNotFoundException("لا يوجد قالب محفوظ للطباعة. ارجع إلى شاشة الإصدار واختر قالبًا أولًا.");

        activeDesignPath = Path.GetFullPath(activeDesignPath);
        if (!renderer.LoadTemplateFile(activeDesignPath))
            throw new InvalidDataException("تعذر تحميل القالب المختار إلى معاينة الطباعة.");
        renderer.ApplyIssuanceContext(_targetSystem, selectedPackage?.Key ?? string.Empty);

        // Use the same card dimensions and 5 mm / 1 mm page spacing as BuildPreviewHtml.
        var cardMillimeters = renderer.GetCardLayoutSizeMillimeters();
        var cardWidth = Math.Max(1d, cardMillimeters.Width * 96d / 25.4d);
        var cardHeight = Math.Max(1d, cardMillimeters.Height * 96d / 25.4d);
        const double paperWidthMm = 210d;
        const double paperHeightMm = 297d;
        const double marginMm = 5d;
        const double gapMm = 1d;
        var columns = Math.Max(1, (int)Math.Floor(((paperWidthMm - 2 * marginMm) + gapMm) / (cardMillimeters.Width + gapMm)));
        var rows = Math.Max(1, (int)Math.Floor(((paperHeightMm - 2 * marginMm) + gapMm) / (cardMillimeters.Height + gapMm)));
        var cardsPerPage = Math.Max(1, columns * rows);
        var pageCount = (int)Math.Ceiling(orderedCards.Count / (double)cardsPerPage);
        var pageWidth = paperWidthMm * 96d / 25.4d;
        var pageHeight = paperHeightMm * 96d / 25.4d;
        var margin = marginMm * 96d / 25.4d;
        var gap = gapMm * 96d / 25.4d;

        var workArea = SystemParameters.WorkArea;
        var previewWindow = new Window
        {
            Title = "معاينة الطباعة — Nexora",
            Width = Math.Min(1040, workArea.Width * 0.92),
            Height = Math.Min(760, workArea.Height * 0.90),
            MinWidth = Math.Min(760, workArea.Width * 0.80),
            MinHeight = Math.Min(560, workArea.Height * 0.78),
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(244, 246, 249)),
            FlowDirection = FlowDirection.RightToLeft,
            ResizeMode = ResizeMode.CanResize,
            ShowInTaskbar = false,
            ShowActivated = true
        };
        if (owner is not null) previewWindow.Owner = owner;

        var root = new Grid { Margin = new Thickness(12), FlowDirection = FlowDirection.RightToLeft };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var currentPageIndex = 0;
        Action renderPreviewPage = () => { };
        var heading = new Grid
        {
            Margin = new Thickness(2, 0, 2, 10),
            Background = Brushes.White
        };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = new TextBlock
        {
            Text = "معاينة الطباعة",
            FontSize = 19,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(143, 16, 26)),
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right
        };
        var headingText = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 9, 14, 9)
        };
        headingText.Children.Add(title);
        headingText.Children.Add(new TextBlock
        {
            Text = $"القالب المختار · {orderedCards.Count:N0} كرت · {pageCount:N0} صفحة",
            FontSize = 11,
            Margin = new Thickness(0, 3, 0, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(96, 105, 116)),
            TextAlignment = TextAlignment.Right
        });
        heading.Children.Add(headingText);
        var pageInfo = new TextBlock
        {
            Text = $"الصفحة 1 من {pageCount:N0} · {orderedCards.Count:N0} كرت",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(96, 105, 116)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 10, 0)
        };
        var navigationButtonBackground = Brushes.White;
        var navigationButtonForeground = new SolidColorBrush(Color.FromRgb(58, 66, 78));
        var navigationButtonBorder = new SolidColorBrush(Color.FromRgb(213, 219, 228));
        var previousPageButton = new Button
        {
            Content = "السابق",
            MinWidth = 62,
            Height = 31,
            Padding = new Thickness(10, 0, 10, 0),
            Background = navigationButtonBackground,
            Foreground = navigationButtonForeground,
            BorderBrush = navigationButtonBorder,
            BorderThickness = new Thickness(1),
            FontFamily = new FontFamily("Tahoma"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand,
            IsEnabled = false,
            ToolTip = "عرض الصفحة السابقة"
        };
        var nextPageButton = new Button
        {
            Content = "التالي",
            MinWidth = 62,
            Height = 31,
            Padding = new Thickness(10, 0, 10, 0),
            Background = navigationButtonBackground,
            Foreground = navigationButtonForeground,
            BorderBrush = navigationButtonBorder,
            BorderThickness = new Thickness(1),
            FontFamily = new FontFamily("Tahoma"),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand,
            IsEnabled = pageCount > 1,
            ToolTip = "عرض الصفحة التالية"
        };
        var pageNavigation = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            FlowDirection = FlowDirection.RightToLeft
        };
        pageNavigation.Children.Add(previousPageButton);
        pageNavigation.Children.Add(pageInfo);
        pageNavigation.Children.Add(nextPageButton);
        Grid.SetColumn(pageNavigation, 1);
        heading.Children.Add(pageNavigation);
        previousPageButton.Click += (_, _) =>
        {
            if (currentPageIndex <= 0) return;
            currentPageIndex--;
            renderPreviewPage();
        };
        nextPageButton.Click += (_, _) =>
        {
            if (currentPageIndex >= pageCount - 1) return;
            currentPageIndex++;
            renderPreviewPage();
        };
        root.Children.Add(heading);
        Grid.SetRow(heading, 0);

        var previewSurface = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(232, 236, 241)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(220, 225, 232)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8)
        };
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            CanContentScroll = false,
            Background = Brushes.Transparent
        };
        var pageStack = new StackPanel { Orientation = Orientation.Vertical, HorizontalAlignment = HorizontalAlignment.Center };
        var pageCanvas = new Canvas
        {
            Width = pageWidth,
            Height = pageHeight,
            Background = Brushes.White,
            ClipToBounds = true
        };
        var pageBorder = new Border
        {
            Width = pageWidth,
            Height = pageHeight,
            Background = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(210, 215, 223)),
            BorderThickness = new Thickness(1),
            Child = pageCanvas,
            Margin = new Thickness(0, 0, 0, 8),
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 12, ShadowDepth = 2, Opacity = 0.16 }
        };

        // Render one page at a time to keep the preview responsive even for large batches.
        // Navigation lets the user inspect every sheet; the PDF action exports the full batch.
        renderPreviewPage = () =>
        {
            pageCanvas.Children.Clear();
            var startIndex = currentPageIndex * cardsPerPage;
            var pageCards = orderedCards.Skip(startIndex).Take(cardsPerPage).ToList();
            for (var index = 0; index < pageCards.Count; index++)
            {
                var card = pageCards[index];
                var packageName = card.PackageName;
                var separator = packageName.LastIndexOf(" — ", StringComparison.Ordinal);
                if (separator > 0) packageName = packageName[..separator].Trim();
                var visual = renderer.CreateIssuancePreviewElement(
                    card.CardNumber,
                    card.Username,
                    card.Password,
                    card.TimeLimit,
                    card.PackageValidity,
                    card.UnitPrice.ToString("0.##", CultureInfo.CurrentCulture),
                    card.DataLimit,
                    "NEXORA",
                    packageName,
                    card.PointOfSale,
                    startIndex + index);
                visual.Width = cardWidth;
                visual.Height = cardHeight;
                var row = index / columns;
                var column = index % columns;
                Canvas.SetLeft(visual, margin + column * (cardWidth + gap));
                Canvas.SetTop(visual, margin + row * (cardHeight + gap));
                pageCanvas.Children.Add(visual);
            }

            pageInfo.Text = $"الصفحة {currentPageIndex + 1:N0} من {pageCount:N0} · {orderedCards.Count:N0} كرت";
            previousPageButton.IsEnabled = currentPageIndex > 0;
            nextPageButton.IsEnabled = currentPageIndex < pageCount - 1;
        };
        renderPreviewPage();

        // Fit the entire A4 sheet in the window so the user can inspect the whole page
        // before creating the PDF, rather than being left with a cropped top section.
        var pagePreviewBox = new Viewbox
        {
            Width = Math.Min(680, Math.Max(280, workArea.Width * 0.68)),
            Height = Math.Min(590, Math.Max(300, workArea.Height * 0.68)),
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = pageBorder
        };
        previewSurface.SizeChanged += (_, _) =>
        {
            var availableWidth = previewSurface.ActualWidth - 28;
            var availableHeight = previewSurface.ActualHeight - 28;
            if (availableWidth > 180) pagePreviewBox.Width = availableWidth;
            if (availableHeight > 220) pagePreviewBox.Height = availableHeight;
        };
        pageStack.Children.Add(pagePreviewBox);
        scroll.Content = pageStack;
        previewSurface.Child = scroll;
        root.Children.Add(previewSurface);
        Grid.SetRow(previewSurface, 1);

        var footer = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var footerNote = new TextBlock
        {
            Text = "معاينة الصفحة كاملة. عند المتابعة سيُصدَّر كل الكروت باستخدام القالب المحدد أعلاه.",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(88, 96, 106)),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };
        footer.Children.Add(footerNote);
        var createPdfButton = new Button
        {
            Content = "إنشاء PDF",
            MinWidth = 150,
            Height = 42,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(18, 0, 18, 0),
            Background = new SolidColorBrush(Color.FromRgb(191, 27, 42)),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.FromRgb(143, 16, 26)),
            BorderThickness = new Thickness(1),
            FontFamily = new FontFamily("Tahoma"),
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        Grid.SetColumn(createPdfButton, 1);
        footer.Children.Add(createPdfButton);
        root.Children.Add(footer);
        Grid.SetRow(footer, 2);
        previewWindow.Content = root;

        createPdfButton.Click += async (_, _) =>
        {
            createPdfButton.IsEnabled = false;
            var oldContent = createPdfButton.Content;
            createPdfButton.Content = "يجري إنشاء PDF…";
            var progress = WorkflowFeedbackWindow.ShowProgress(
                previewWindow,
                "إنشاء ملف PDF",
                $"يجري إخراج جميع صفحات الكروت وفق القالب المختار ({orderedCards.Count:N0} كرت).",
                "تجهيز الصفحات…");
            try
            {
                await Task.Delay(100);
                var firstBatchId = orderedCards.Select(card => card.BatchId).FirstOrDefault() ?? $"NXR-{DateTime.Now:yyMMdd-HHmmss}";
                var pdfPath = GetDefaultPdfPath(firstBatchId);
                progress.UpdateStage("إخراج الصفحات والتحقق من سلامة PDF…");
                await ExportPreviewTablePdfAsync(orderedCards, batchIds, pdfPath, activeDesignPath);
                if (!File.Exists(pdfPath) || new FileInfo(pdfPath).Length < 500)
                    throw new InvalidDataException("ملف PDF الناتج غير موجود أو غير مكتمل.");

                Process.Start(new ProcessStartInfo(pdfPath) { UseShellExecute = true });
                SetStatus($"تم إنشاء PDF وفتحه — {orderedCards.Count:N0} كرت.", false);
                progress.Close();
                previewWindow.Close();
            }
            catch (Exception ex)
            {
                footerNote.Text = "تعذر إنشاء PDF: " + ex.Message;
                footerNote.Foreground = new SolidColorBrush(Color.FromRgb(180, 35, 24));
                createPdfButton.Content = oldContent;
                createPdfButton.IsEnabled = true;
                SetStatus("تعذر إنشاء PDF: " + ex.Message, true);
            }
            finally
            {
                if (progress.IsVisible) progress.Close();
            }
        };

        previewWindow.ShowDialog();
    }
    private static string GetDefaultPdfPath(string batchId)
    {
        var safeId = string.Concat((batchId ?? string.Empty).Select(character =>
            Path.GetInvalidFileNameChars().Contains(character) ? '-' : character));
        if (string.IsNullOrWhiteSpace(safeId)) safeId = $"NXR-{DateTime.Now:yyMMdd-HHmmss}";
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Nexora", "Prints");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, safeId + ".pdf");
        if (File.Exists(path)) path = Path.Combine(folder, safeId + "-" + DateTime.Now.ToString("HHmmssfff", CultureInfo.InvariantCulture) + ".pdf");
        return path;
    }
    private static async Task ExportPreviewTablePdfAsync(
        List<GeneratedDemoCard> cards, string batchNames, string outputPath, string? selectedTemplatePath = null)
    {
        if (cards.Count == 0)
            throw new InvalidOperationException("لا توجد كروت لتصديرها.");
        var edgePath = FindEdgeExecutable();
        if (edgePath is null)
            throw new FileNotFoundException("لم يتم العثور على Microsoft Edge؛ لا يمكن تحويل معاينة HTML إلى PDF تلقائيًا على هذا الجهاز.");

        var exportFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexoraProductDemo", "exports");
        Directory.CreateDirectory(exportFolder);
        var htmlPath = Path.Combine(exportFolder, $"cards-preview-{Guid.NewGuid():N}.html");
        var profilePath = Path.Combine(Path.GetTempPath(), $"NexoraEdgePdf-{Guid.NewGuid():N}");
        var outputFullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputFullPath)!);
        await File.WriteAllTextAsync(
            htmlPath,
            BuildPreviewHtml(cards, batchNames, selectedTemplatePath),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        try
        {
            var startInfo = new ProcessStartInfo(edgePath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            startInfo.ArgumentList.Add("--headless=new");
            startInfo.ArgumentList.Add("--disable-gpu");
            startInfo.ArgumentList.Add("--no-first-run");
            startInfo.ArgumentList.Add("--no-default-browser-check");
            startInfo.ArgumentList.Add("--no-pdf-header-footer");
            startInfo.ArgumentList.Add("--disable-extensions");
            startInfo.ArgumentList.Add("--user-data-dir=" + profilePath);
            startInfo.ArgumentList.Add("--print-to-pdf=" + outputFullPath);
            startInfo.ArgumentList.Add(new Uri(htmlPath).AbsoluteUri);

            using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("تعذر تشغيل Microsoft Edge لإنشاء PDF.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(75));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new TimeoutException("انتهت مهلة تحويل المعاينة إلى PDF بعد 75 ثانية.");
            }

            if (process.ExitCode != 0)
                throw new InvalidOperationException($"أغلق Microsoft Edge عملية التحويل برمز {process.ExitCode}.");
            if (!File.Exists(outputFullPath) || new FileInfo(outputFullPath).Length < 500)
                throw new InvalidDataException("لم ينتج Microsoft Edge ملف PDF بالحجم المتوقع.");

            await using var stream = File.OpenRead(outputFullPath);
            var start = new byte[5];
            if (await stream.ReadAsync(start) != start.Length || Encoding.ASCII.GetString(start) != "%PDF-")
                throw new InvalidDataException("الملف الناتج لا يحتوي على ترويسة PDF صالحة.");
            var tailLength = (int)Math.Min(2048, stream.Length);
            stream.Seek(-tailLength, SeekOrigin.End);
            var tail = new byte[tailLength];
            await stream.ReadExactlyAsync(tail);
            if (!Encoding.ASCII.GetString(tail).Contains("%%EOF", StringComparison.Ordinal))
                throw new InvalidDataException("ملف PDF غير مكتمل؛ لا توجد علامة نهاية الملف.");
        }
        catch
        {
            if (File.Exists(outputFullPath))
            {
                try { File.Delete(outputFullPath); } catch { }
            }
            throw;
        }
        finally
        {
            try { if (Directory.Exists(profilePath)) Directory.Delete(profilePath, recursive: true); } catch { }
            try { if (File.Exists(htmlPath)) File.Delete(htmlPath); } catch { }
        }
    }

    private static string? FindEdgeExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Edge", "Application", "msedge.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private sealed record HtmlTemplateElement(
        double X, double Y, double Width, double Height, double RotationDegrees,
        string Text, bool IsBold, double FontSize, string FontFamily);

    private sealed record HtmlTemplateLayout(
        string Name, double CardWidthMm, double CardHeightMm, string TemplateStyle, string ColorTheme,
        Dictionary<string, HtmlTemplateElement> Elements, Dictionary<string, bool> VisibleElements, string? BackgroundDataUri);

    private static HtmlTemplateLayout LoadHtmlTemplateLayout(string? selectedTemplatePath = null)
    {
        var defaultElements = new Dictionary<string, HtmlTemplateElement>(StringComparer.Ordinal)
        {
            ["login"] = new(58, 3, 146, 17, 0, "50723015", true, 15, "Tahoma"),
            ["username"] = new(58, 22, 95, 11, 0, "اسم المستخدم", false, 8, "Tahoma"),
            ["password"] = new(154, 22, 90, 11, 0, "كلمة المرور", false, 8, "Tahoma"),
            ["validity"] = new(58, 34, 62, 10, 0, "الصلاحية", true, 7.5, "Tahoma"),
            ["time"] = new(121, 34, 48, 10, 0, "الوقت", false, 7.5, "Tahoma"),
            ["price"] = new(171, 34, 72, 10, 0, "السعر", true, 7.5, "Tahoma"),
            ["package"] = new(58, 45, 92, 10, 0, "الباقة", false, 8, "Tahoma"),
            ["download"] = new(151, 45, 92, 10, 0, "التحميل", false, 8, "Tahoma"),
            ["brand"] = new(5, 24, 48, 10, 0, "NEXORA", true, 8, "Tahoma"),
            ["networkName"] = new(4, 37, 50, 12, 0, "اسم الشبكة", false, 7, "Tahoma"),
            ["header"] = new(0, 0, 54, 58, 0, "", false, 8, "Tahoma"),
            ["divider"] = new(55, 20, 1, 35, 0, "", false, 8, "Tahoma"),
            ["qr"] = new(215, 2, 30, 30, 0, "▦", true, 20, "Tahoma")
        };
        var defaultVisibility = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["رمز الدخول"] = true, ["اسم المستخدم"] = true, ["كلمة المرور"] = true,
            ["الصلاحية"] = true, ["الوقت"] = true, ["السعر"] = true, ["التحميل"] = true,
            ["الباقة"] = true, ["أرقام التواصل"] = true, ["الرقم التسلسلي"] = false,
            ["تاريخ الطباعة"] = false, ["رقم الدفعة"] = false, ["نقطة البيع"] = false,
            ["معلومات إضافية"] = false, ["كود المكافأة"] = false, ["رمز QR"] = false,
            ["الباركود"] = false, ["شعار الشبكة"] = true, ["اسم العلامة"] = true,
            ["اسم الشبكة"] = true, ["الشريط العلوي"] = true, ["الشريط الجانبي"] = true,
            ["الفاصل"] = true
        };
        var fallback = new HtmlTemplateLayout("القالب الافتراضي", 66, 15.5, "classic", "red", defaultElements, defaultVisibility, null);
        var hasExplicitTemplate = !string.IsNullOrWhiteSpace(selectedTemplatePath);
        var templatePath = hasExplicitTemplate
            ? Path.GetFullPath(selectedTemplatePath!)
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NexoraProductDemo", "templates", "tabcard-template-design.json");
        if (!File.Exists(templatePath))
        {
            if (hasExplicitTemplate)
                throw new FileNotFoundException("ملف القالب المختار غير موجود أثناء تجهيز PDF.", templatePath);
            return fallback;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(templatePath));
            var root = document.RootElement;
            var isRasterTemplateMode = root.TryGetProperty("IsRasterTemplateMode", out var rasterModeValue)
                && rasterModeValue.ValueKind == JsonValueKind.True;
            string ReadString(string name, string defaultValue) =>
                root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                    ? value.GetString() ?? defaultValue : defaultValue;
            double ReadDouble(string name, double defaultValue) =>
                root.TryGetProperty(name, out var value) && value.TryGetDouble(out var number) && double.IsFinite(number)
                    ? number : defaultValue;

            var elements = new Dictionary<string, HtmlTemplateElement>(StringComparer.Ordinal);
            if (root.TryGetProperty("ElementProperties", out var elementObject) && elementObject.ValueKind == JsonValueKind.Object)
            {
                foreach (var item in elementObject.EnumerateObject())
                {
                    if (item.Value.ValueKind != JsonValueKind.Object) continue;
                    var element = item.Value;
                    double Number(string name, double defaultValue) =>
                        element.TryGetProperty(name, out var property) && property.TryGetDouble(out var number) && double.IsFinite(number)
                            ? number : defaultValue;
                    string Text(string name, string defaultValue = "") =>
                        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
                            ? property.GetString() ?? defaultValue : defaultValue;
                    bool Boolean(string name, bool defaultValue = false) =>
                        element.TryGetProperty(name, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False
                            ? property.GetBoolean() : defaultValue;
                    elements[item.Name] = new HtmlTemplateElement(
                        Number("X", 0), Number("Y", 0), Math.Max(0, Number("Width", 10)), Math.Max(0, Number("Height", 10)),
                        Number("RotationDegrees", 0), Text("Text"), Boolean("IsBold"), Math.Clamp(Number("FontSize", 8), 4, 32), Text("FontFamily", "Tahoma"));
                }
            }
            if (elements.Count == 0)
            {
                if (hasExplicitTemplate && !isRasterTemplateMode)
                    throw new InvalidDataException("القالب المختار لا يحتوي على عناصر تصميم قابلة للرسم.");
                // Raster-only templates still need data fields positioned over their image.
                elements = defaultElements;
            }

            var visibility = new Dictionary<string, bool>(defaultVisibility, StringComparer.Ordinal);
            if (root.TryGetProperty("VisibleElements", out var visibleObject) && visibleObject.ValueKind == JsonValueKind.Object)
                foreach (var item in visibleObject.EnumerateObject())
                    if (item.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        visibility[item.Name] = item.Value.GetBoolean();

            string? backgroundDataUri = null;
            var backgroundBase64 = isRasterTemplateMode
                ? ReadString("RasterTemplateImageBase64", string.Empty)
                : ReadString("ImportedBackgroundImageBase64", string.Empty);
            var backgroundPath = isRasterTemplateMode
                ? ReadString("RasterTemplateImagePath", string.Empty)
                : ReadString("ImportedBackgroundImagePath", string.Empty);
            var extension = isRasterTemplateMode
                ? Path.GetExtension(backgroundPath).ToLowerInvariant()
                : ReadString("ImportedBackgroundImageExtension", ".png").ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(extension)) extension = ".png";

            if (!string.IsNullOrWhiteSpace(backgroundBase64))
            {
                var mime = extension is ".jpg" or ".jpeg" ? "image/jpeg" :
                    extension == ".bmp" ? "image/bmp" : "image/png";
                backgroundDataUri = $"data:{mime};base64,{backgroundBase64}";
            }
            else if (!string.IsNullOrWhiteSpace(backgroundPath) && File.Exists(backgroundPath))
            {
                var bytes = File.ReadAllBytes(backgroundPath);
                if (bytes.Length <= 15 * 1024 * 1024)
                {
                    var actualExtension = Path.GetExtension(backgroundPath).ToLowerInvariant();
                    var mime = actualExtension is ".jpg" or ".jpeg" ? "image/jpeg" :
                        actualExtension == ".bmp" ? "image/bmp" : "image/png";
                    backgroundDataUri = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
                }
            }

            if (isRasterTemplateMode && backgroundDataUri is null && hasExplicitTemplate)
                throw new InvalidDataException("القالب المصوّر المختار لا يحتوي على صورة قابلة للقراءة.");
            return new HtmlTemplateLayout(
                ReadString("TemplateName", "القالب المحفوظ"),
                Math.Clamp(ReadDouble("CardWidthMm", 66), 10, 200),
                Math.Clamp(ReadDouble("CardHeightMm", 15.5), 10, 200),
                ReadString("TemplateStyle", "classic"), ReadString("ColorTheme", "red"), elements, visibility, backgroundDataUri);
        }
        catch (Exception ex)
        {
            if (hasExplicitTemplate)
                throw new InvalidDataException("تعذر قراءة القالب المختار لإنشاء PDF؛ لم يتم الرجوع إلى القالب الافتراضي.", ex);

            // The built-in layout is only used when no explicit template was selected.
            return fallback;
        }
    }

    private static string BuildPreviewHtml(
        List<GeneratedDemoCard> cards, string batchNames, string? selectedTemplatePath = null)
    {
        static string H(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);
        var layout = LoadHtmlTemplateLayout(selectedTemplatePath);
        // ElementProperties are stored in the same scaled DIP canvas used by TemplateDesignerView.
        var cardWidthDip = 420d * layout.CardWidthMm / 60d;
        var cardHeightDip = 245d * layout.CardHeightMm / 35d;
        var gapMm = 1d;
        var pageWidthMm = 210d - 10d;
        var pageHeightMm = 297d - 10d;
        var columns = Math.Max(1, (int)Math.Floor((pageWidthMm + gapMm) / (layout.CardWidthMm + gapMm)));
        var rows = Math.Max(1, (int)Math.Floor((pageHeightMm + gapMm) / (layout.CardHeightMm + gapMm)));
        var perPage = Math.Max(1, columns * rows);

        var labelForTag = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["login"] = "رمز الدخول", ["username"] = "اسم المستخدم", ["password"] = "كلمة المرور",
            ["validity"] = "الصلاحية", ["time"] = "الوقت", ["price"] = "السعر", ["download"] = "التحميل",
            ["package"] = "الباقة", ["contact"] = "أرقام التواصل", ["serial"] = "الرقم التسلسلي",
            ["printDate"] = "تاريخ الطباعة", ["batch"] = "رقم الدفعة", ["pointOfSale"] = "نقطة البيع",
            ["extra"] = "معلومات إضافية", ["reward"] = "كود المكافأة", ["qr"] = "رمز QR", ["barcode"] = "الباركود",
            ["brand"] = "شعار الشبكة", ["networkName"] = "اسم الشبكة", ["logoBadge"] = "شعار الشبكة",
            ["logoMark"] = "شعار الشبكة", ["header"] = "الشريط العلوي", ["divider"] = "الفاصل"
        };
        string CssColor(string key, string fallback) => layout.ColorTheme.Equals("dark", StringComparison.OrdinalIgnoreCase)
            ? key == "background" ? "#371118" : key == "primary" ? "#F5DADD" : "#FFFFFF"
            : layout.TemplateStyle.Equals("dark", StringComparison.OrdinalIgnoreCase)
                ? key == "background" ? "#371118" : key == "primary" ? "#F5DADD" : "#FFFFFF"
                : key == "background" ? "#FFFFFF" : key == "primary" ? "#A01818" : fallback;
        var bg = CssColor("background", "#20242A");
        var primary = CssColor("primary", "#20242A");
        var muted = layout.ColorTheme.Equals("dark", StringComparison.OrdinalIgnoreCase) || layout.TemplateStyle.Equals("dark", StringComparison.OrdinalIgnoreCase) ? "#F5DADD" : "#626B75";
        // Add the (potentially large) embedded image once in the stylesheet, not once per voucher.
        var backgroundCss = layout.BackgroundDataUri is null ? string.Empty
            : $"background-image:url('{layout.BackgroundDataUri}');background-size:100% 100%;background-repeat:no-repeat;";

        string RenderElement(string tag, HtmlTemplateElement element, GeneratedDemoCard card, int cardIndex)
        {
            if (labelForTag.TryGetValue(tag, out var visibilityKey) &&
                layout.VisibleElements.TryGetValue(visibilityKey, out var isVisible) && !isVisible)
                return string.Empty;
            if (tag == "password" && (string.IsNullOrWhiteSpace(card.Password) ||
                string.Equals(card.Password.Trim(), card.Username?.Trim(), StringComparison.Ordinal))) return string.Empty;
            if (tag == "contact" && string.IsNullOrWhiteSpace(element.Text)) return string.Empty;
            if (tag == "extra" && string.IsNullOrWhiteSpace(card.Comment) && string.IsNullOrWhiteSpace(element.Text)) return string.Empty;
            if (tag == "reward" && string.IsNullOrWhiteSpace(element.Text)) return string.Empty;

            string value = tag switch
            {
                "login" => card.CardNumber,
                "username" => "اسم المستخدم: " + card.Username,
                "password" => "كلمة المرور: " + card.Password,
                "validity" => "الصلاحية: " + card.PackageValidity,
                "time" => "الوقت: " + card.TimeLimit,
                "price" => "السعر: " + card.UnitPriceLabel,
                "download" => "التحميل: " + card.DataLimit,
                "package" => "الباقة: " + StripPackagePrice(card.PackageName),
                "contact" => element.Text,
                "serial" => "الرقم التسلسلي: " + card.CardNumber,
                "printDate" => "تاريخ الطباعة: " + DateTime.Now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture),
                "batch" => "رقم الدفعة: " + card.BatchId,
                "pointOfSale" => "نقطة البيع: " + card.PointOfSale,
                "extra" => string.IsNullOrWhiteSpace(card.Comment) ? element.Text : card.Comment,
                "reward" => element.Text,
                "brand" => string.IsNullOrWhiteSpace(element.Text) ? "NEXORA" : element.Text,
                "networkName" => string.IsNullOrWhiteSpace(element.Text) ? "اسم الشبكة" : element.Text,
                "qr" => "▦",
                "barcode" => "▏▌▏▎▌▏▏▌▎▏▌▏",
                "logoMark" => string.IsNullOrWhiteSpace(element.Text) ? "NX" : element.Text,
                _ => element.Text
            };
            var x = Math.Clamp(element.X / Math.Max(1, cardWidthDip) * 100d, -10, 110);
            var y = Math.Clamp(element.Y / Math.Max(1, cardHeightDip) * 100d, -10, 110);
            var width = Math.Clamp(element.Width / Math.Max(1, cardWidthDip) * 100d, 0, 120);
            var height = Math.Clamp(element.Height / Math.Max(1, cardHeightDip) * 100d, 0, 120);
            var fontFamily = (element.FontFamily ?? "Tahoma").Replace("'", string.Empty, StringComparison.Ordinal);
            var weight = element.IsBold ? "700" : "400";
            var textColor = tag is "login" or "price" or "validity" ? primary :
                tag is "brand" or "networkName" ? (layout.TemplateStyle.Equals("dark", StringComparison.OrdinalIgnoreCase) ? "#FFFFFF" : primary) : muted;
            var align = tag is "login" or "barcode" or "qr" or "logoMark" ? "center" : "right";
            var direction = tag is "login" or "barcode" or "qr" or "logoMark" ? "ltr" : "rtl";
            var rotation = Math.Abs(element.RotationDegrees) > 0.01 ? $"transform:rotate({element.RotationDegrees.ToString("0.##", CultureInfo.InvariantCulture)}deg);" : string.Empty;
            var baseStyle = $"left:{x.ToString("0.###", CultureInfo.InvariantCulture)}%;top:{y.ToString("0.###", CultureInfo.InvariantCulture)}%;width:{width.ToString("0.###", CultureInfo.InvariantCulture)}%;height:{height.ToString("0.###", CultureInfo.InvariantCulture)}%;font-family:'{H(fontFamily)}',Tahoma,sans-serif;font-size:{element.FontSize.ToString("0.##", CultureInfo.InvariantCulture)}px;font-weight:{weight};color:{textColor};text-align:{align};direction:{direction};{rotation}";
            if (tag == "header")
                return $"<div class='el shape header' style='{baseStyle}background:{primary};'></div>";
            if (tag == "divider")
                return $"<div class='el shape divider' style='{baseStyle}background:{primary};'></div>";
            if (tag == "logoBadge")
                return $"<div class='el shape logo-badge' style='{baseStyle}border:1px solid {primary};border-radius:50%;'></div>";
            if (tag is "qr" or "barcode")
                return $"<div class='el code {tag}' style='{baseStyle}'>{H(value)}</div>";
            if (tag == "logoMark")
                return $"<div class='el logo-mark' style='{baseStyle}'>{H(value)}</div>";
            return $"<div class='el text {H(tag)}' style='{baseStyle}'>{H(value)}</div>";
        }

        var pages = new StringBuilder();
        var batchNumbers = string.Join("، ", cards.Select(card => card.BatchId).Distinct(StringComparer.OrdinalIgnoreCase).Take(5));
        for (var pageStart = 0; pageStart < cards.Count; pageStart += perPage)
        {
            pages.Append("<section class='page'>");
            var pageCards = cards.Skip(pageStart).Take(perPage).ToList();
            for (var localIndex = 0; localIndex < pageCards.Count; localIndex++)
            {
                var globalIndex = pageStart + localIndex;
                var card = pageCards[localIndex];
                pages.Append("<article class='voucher'>");
                foreach (var pair in layout.Elements.OrderBy(pair => pair.Key == "header" ? -2 : pair.Key == "logoBadge" ? -1 : 0))
                    pages.Append(RenderElement(pair.Key, pair.Value, card, globalIndex));
                pages.Append("</article>");
            }
            pages.Append("</section>");
        }

        return $$"""
            <!doctype html>
            <html lang="ar" dir="rtl">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width,initial-scale=1">
              <title>كروت Nexora — {{H(batchNumbers)}}</title>
              <style>
                @page { size: A4 portrait; margin: 5mm; }
                * { box-sizing: border-box; }
                html, body { margin: 0; padding: 0; color: #20242a; font-family: Tahoma, Arial, sans-serif; direction: rtl; }
                .page { position: relative; padding-top: 0mm; width: 200mm; height: 287mm; display: grid; grid-template-columns: repeat({{columns}}, {{layout.CardWidthMm.ToString("0.###", CultureInfo.InvariantCulture)}}mm); grid-auto-rows: {{layout.CardHeightMm.ToString("0.###", CultureInfo.InvariantCulture)}}mm; gap: 1mm; align-content: start; justify-content: start; overflow: hidden; break-after: page; page-break-after: always; }
                .page:last-child { break-after: auto; page-break-after: auto; }
                .voucher { width: {{layout.CardWidthMm.ToString("0.###", CultureInfo.InvariantCulture)}}mm; height: {{layout.CardHeightMm.ToString("0.###", CultureInfo.InvariantCulture)}}mm; position: relative; overflow: hidden; background-color: {{bg}}; {{backgroundCss}} border: .22mm solid {{primary}}; border-radius: 1mm; page-break-inside: avoid; break-inside: avoid; }
                .el { position: absolute; box-sizing: border-box; overflow: hidden; line-height: 1.05; white-space: pre-wrap; overflow-wrap: anywhere; padding: .1mm .3mm; }
                .shape { padding: 0; }
                .header { opacity: .98; }
                .divider { min-width: .2mm; }
                .text { text-overflow: clip; }
                .login { font-size: 14px; letter-spacing: .3px; }
                .code { background: #fff; border: .2mm solid #d5d9df; border-radius: .4mm; }
                .qr { font-size: 18px; line-height: 1; }
                .barcode { font-family: Consolas, monospace !important; white-space: nowrap; font-size: 8px !important; }
                .logo-mark { font-weight: 700; }
                .page + .page { margin-top: 0; }
              </style>
            </head>
            <body>
              {{pages}}
            </body>
            </html>
            """;
    }

    private static string StripPackagePrice(string packageName)
    {
        var separator = packageName.LastIndexOf(" — ", StringComparison.Ordinal);
        return separator > 0 ? packageName[..separator].Trim() : packageName.Trim();
    }

    private System.Windows.Documents.FlowDocument BuildDesignedPrintDocument(
        List<GeneratedDemoCard> cards, string batchIds, double printableWidth, double printableHeight)
    {
        var designer = new Nexora.Demo.Desktop.Features.Printing.Views.TemplateDesignerView();
        var cardMillimeters = designer.GetPhysicalCardSizeMillimeters();
        var cardWidth = Math.Max(40, cardMillimeters.Width * 96d / 25.4d);
        var cardHeight = Math.Max(20, cardMillimeters.Height * 96d / 25.4d);
        var document = new System.Windows.Documents.FlowDocument
        {
            PageWidth = Math.Max(500, printableWidth),
            PageHeight = Math.Max(600, printableHeight),
            PagePadding = new Thickness(2),
            ColumnWidth = double.PositiveInfinity,
            FlowDirection = FlowDirection.RightToLeft,
            FontFamily = new FontFamily("Tahoma"),
            FontSize = 8
        };
        document.Blocks.Add(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run($"Nexora · رقم الدفعة: {batchIds} · عدد الكروت: {cards.Count:N0}"))
        {
            FontSize = 8,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(0, 0, 0, 2)
        });

        var usableWidth = Math.Max(cardWidth + 4, printableWidth - 4);
        var columns = Math.Clamp((int)Math.Floor(usableWidth / (cardWidth + 3)), 1, 4);
        var table = new System.Windows.Documents.Table { CellSpacing = 0 };
        for (var column = 0; column < columns; column++)
            table.Columns.Add(new System.Windows.Documents.TableColumn { Width = new GridLength(usableWidth / columns, GridUnitType.Pixel) });
        var group = new System.Windows.Documents.TableRowGroup();
        for (var index = 0; index < cards.Count; index += columns)
        {
            var row = new System.Windows.Documents.TableRow();
            for (var offset = 0; offset < columns; offset++)
            {
                var cell = new System.Windows.Documents.TableCell
                {
                    Padding = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(225, 228, 233)),
                    BorderThickness = new Thickness(0.25),
                    TextAlignment = TextAlignment.Center,
                    Background = Brushes.White
                };
                var cardIndex = index + offset;
                if (cardIndex < cards.Count)
                {
                    var element = designer.CreatePrintableCard(cards[cardIndex], cardIndex);
                    var viewbox = new Viewbox
                    {
                        Width = cardWidth,
                        Height = cardHeight,
                        Stretch = Stretch.Uniform,
                        StretchDirection = StretchDirection.DownOnly,
                        Child = element,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    cell.Blocks.Add(new System.Windows.Documents.BlockUIContainer(viewbox) { Margin = new Thickness(0) });
                }
                else
                {
                    cell.Blocks.Add(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run(" ")) { Margin = new Thickness(0) });
                }
                row.Cells.Add(cell);
            }
            group.Rows.Add(row);
        }
        table.RowGroups.Add(group);
        document.Blocks.Add(table);
        return document;
    }

    private static System.Windows.Documents.FlowDocument BuildLocalPrintDocument(
        List<GeneratedDemoCard> cards, string batchIds, double printableWidth, double printableHeight)
    {
        var document = new System.Windows.Documents.FlowDocument
        {
            PageWidth = Math.Max(500, printableWidth),
            PageHeight = Math.Max(600, printableHeight),
            PagePadding = new Thickness(24),
            ColumnWidth = double.PositiveInfinity,
            FlowDirection = FlowDirection.RightToLeft,
            FontFamily = new FontFamily("Tahoma"),
            FontSize = 10
        };
        document.Blocks.Add(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run("تاب كارد — معاينة طباعة الكروت"))
        {
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(0, 0, 0, 8)
        });
        document.Blocks.Add(new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run($"الدفعة: {batchIds} · عدد الكروت: {cards.Count:N0}"))
        {
            FontSize = 11,
            TextAlignment = TextAlignment.Right,
            Margin = new Thickness(0, 0, 0, 12)
        });

        var table = new System.Windows.Documents.Table { CellSpacing = 0 };
        foreach (var width in new[] { 0.45, 1.05, 1.15, 1.05, 1.3, 0.8 })
            table.Columns.Add(new System.Windows.Documents.TableColumn { Width = new GridLength(width, GridUnitType.Star) });
        var headerGroup = new System.Windows.Documents.TableRowGroup();
        var headerRow = new System.Windows.Documents.TableRow();
        foreach (var header in new[] { "م", "رقم الكرت", "اسم المستخدم", "كلمة المرور", "الباقة", "السعر" })
            headerRow.Cells.Add(CreateLocalPrintCell(header, true));
        headerGroup.Rows.Add(headerRow);
        table.RowGroups.Add(headerGroup);

        var bodyGroup = new System.Windows.Documents.TableRowGroup();
        for (var index = 0; index < cards.Count; index++)
        {
            var card = cards[index];
            var row = new System.Windows.Documents.TableRow();
            row.Cells.Add(CreateLocalPrintCell((index + 1).ToString(CultureInfo.CurrentCulture), false));
            row.Cells.Add(CreateLocalPrintCell(card.CardNumber, false));
            row.Cells.Add(CreateLocalPrintCell(card.Username, false));
            row.Cells.Add(CreateLocalPrintCell(card.PasswordDisplay, false));
            row.Cells.Add(CreateLocalPrintCell(card.PackageName, false));
            row.Cells.Add(CreateLocalPrintCell(card.UnitPriceLabel, false));
            bodyGroup.Rows.Add(row);
        }
        table.RowGroups.Add(bodyGroup);
        document.Blocks.Add(table);
        return document;
    }
    private static System.Windows.Documents.TableCell CreateLocalPrintCell(string value, bool header)
    {
        var paragraph = new System.Windows.Documents.Paragraph(new System.Windows.Documents.Run(value ?? string.Empty))
        { Margin = new Thickness(0), TextAlignment = TextAlignment.Center };
        var cell = new System.Windows.Documents.TableCell(paragraph)
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(222, 225, 230)),
            BorderThickness = new Thickness(0.5),
            Padding = new Thickness(4, 3, 4, 3),
            Background = header ? new SolidColorBrush(Color.FromRgb(249, 237, 237)) : Brushes.White,
            FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal
        };
        return cell;
    }
}
