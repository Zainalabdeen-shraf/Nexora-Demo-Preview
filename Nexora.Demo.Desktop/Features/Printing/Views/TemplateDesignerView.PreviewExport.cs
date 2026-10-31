using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nexora.Demo.Desktop.Features.Printing.Views;

// Responsibility: A4 print output and exact in-app preview PDF export.
public partial class TemplateDesignerView
{
    private void PrintStudioPreviewButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isReady) return;

        try
        {
            ApplyPropertiesButton_Click(this, new RoutedEventArgs());
            var layout = CalculateStudioPrintLayout();
            if (layout.TotalCards <= 0)
            {
                SetStatus("لا توجد كروت لإرسالها إلى الطابعة.", isSuccess: false);
                return;
            }

            var printDialog = new System.Windows.Controls.PrintDialog();
            var printTicket = printDialog.PrintTicket ?? new System.Printing.PrintTicket();
            printTicket.PageMediaSize = new System.Printing.PageMediaSize(
                System.Printing.PageMediaSizeName.ISOA4);
            printTicket.PageOrientation = System.Printing.PageOrientation.Portrait;
            printDialog.PrintTicket = printTicket;

            if (printDialog.ShowDialog() != true)
            {
                SetStatus("تم إلغاء مهمة الطباعة.", isSuccess: true);
                return;
            }

            // Print the same card composition and page geometry used by the A4
            // studio preview and PDF exporter. WPF uses DIPs (96 per inch).
            const double dipPerMillimeter = 96d / 25.4d;
            var pageWidth = StudioPageWidthMm * dipPerMillimeter;
            var pageHeight = StudioPageHeightMm * dipPerMillimeter;
            var pageMarginTop = PageMarginTopMm * dipPerMillimeter;
            var pageMarginLeft = PageMarginLeftMm * dipPerMillimeter;
            var usablePageWidth = StudioPageWidthMm - PageMarginLeftMm - PageMarginRightMm;
            var cardGap = StudioCardGapMm * dipPerMillimeter;
            var cardWidth = layout.CardWidthMm * dipPerMillimeter;
            var cardHeight = layout.CardHeightMm * dipPerMillimeter;

            var document = new FixedDocument();
            document.DocumentPaginator.PageSize = new Size(pageWidth, pageHeight);

            var package = GetActivePackage();
            var hours = package?.TimeLimit ?? "—";
            var validity = package?.Validity ?? "—";
            var price = package?.PrintPriceLabel ?? "—";
            var upload = package?.DataLimit ?? "—";
            var packageName = package is null ? "—" : GetPackageName(package);
            var network = PreviewNetworkTextBlock.Text ?? string.Empty;
            var contact = PreviewContactBlock.Text ?? string.Empty;
            const long firstNumber = 50723001;

            for (var pageIndex = 0; pageIndex < layout.TotalPages; pageIndex++)
            {
                var page = new FixedPage
                {
                    Width = pageWidth,
                    Height = pageHeight,
                    Background = Brushes.White,
                    FlowDirection = FlowDirection.LeftToRight
                };

                var firstCardIndex = pageIndex * layout.CardsPerPage;
                var cardsOnPage = Math.Max(
                    0,
                    Math.Min(layout.CardsPerPage, layout.TotalCards - firstCardIndex));
                var totalGridWidth = layout.Columns * layout.CardWidthMm +
                    Math.Max(0, layout.Columns - 1) * StudioCardGapMm;
                var horizontalStart = (PageMarginLeftMm + Math.Max(0, (usablePageWidth - totalGridWidth) / 2d)) * dipPerMillimeter;

                for (var localIndex = 0; localIndex < cardsOnPage; localIndex++)
                {
                    var globalIndex = firstCardIndex + localIndex;
                    var number = (firstNumber + globalIndex).ToString(CultureInfo.InvariantCulture);
                    var username = number;
                    var password = "143" + (globalIndex % 10).ToString(CultureInfo.InvariantCulture);
                    var card = BuildDesignedPrintPreviewCard(
                        number, username, password, hours, validity, price, upload,
                        network, packageName, contact, globalIndex);

                    var cardViewbox = new Viewbox
                    {
                        Width = cardWidth,
                        Height = cardHeight,
                        Stretch = Stretch.Uniform,
                        StretchDirection = StretchDirection.DownOnly,
                        ClipToBounds = true,
                        FlowDirection = FlowDirection.LeftToRight,
                        Child = card
                    };

                    var row = localIndex / layout.Columns;
                    var columnFromRight = localIndex % layout.Columns;
                    var left = horizontalStart +
                        (layout.Columns - 1 - columnFromRight) * (cardWidth + cardGap);
                    var top = pageMarginTop + row * (cardHeight + cardGap);
                    FixedPage.SetLeft(cardViewbox, left);
                    FixedPage.SetTop(cardViewbox, top);
                    page.Children.Add(cardViewbox);
                }

                var pageContent = new PageContent();
                ((IAddChild)pageContent).AddChild(page);
                document.Pages.Add(pageContent);
            }

            printDialog.PrintDocument(
                document.DocumentPaginator,
                "TabCard — " + (string.IsNullOrWhiteSpace(TemplateNameTextBox.Text)
                    ? "A4"
                    : TemplateNameTextBox.Text.Trim()));

            SetStatus(
                $"تم إرسال {layout.TotalCards:N0} كرت في {layout.TotalPages:N0} صفحة A4 إلى الطابعة.",
                isSuccess: true);
        }
        catch (Exception ex)
        {
            SetStatus("تعذرت الطباعة: " + ex.Message, isSuccess: false);
            MessageBox.Show(
                "تعذرت طباعة صفحات A4:\n" + ex.Message,
                "تعذر تنفيذ الطباعة",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private async void ExportStudioPreviewPdfButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isReady) return;
        ApplyPropertiesButton_Click(this, new RoutedEventArgs());
        var save = new SaveFileDialog
        {
            Title = "تصدير ملف معاينة TabCard إلى PDF",
            Filter = "ملف PDF (*.pdf)|*.pdf",
            DefaultExt = ".pdf",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = (string.IsNullOrWhiteSpace(TemplateNameTextBox.Text) ? "TabCard-Preview" : TemplateNameTextBox.Text.Trim()) + "-preview.pdf"
        };
        if (save.ShowDialog(Window.GetWindow(this)) != true) return;

        var oldPage = _studioPreviewPageNumber;
        var tempRoot = Path.Combine(Path.GetTempPath(), "TabCard-Preview-PDF-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var initialLayout = CalculateStudioPrintLayout();
            var totalPages = initialLayout.TotalPages;
            var pageImages = new List<string>(totalPages);
            SetStatus($"يتم تجهيز {totalPages} صفحة PDF من المعاينة الحالية...", isSuccess: true);
            for (var page = 1; page <= totalPages; page++)
            {
                _studioPreviewPageNumber = page;
                var pageLayout = CalculateStudioPrintLayout();
                var pageViewbox = BuildStudioPrintSheetPage(pageLayout);

                // Scale the same A4 composition to true 210 × 297 mm at 300 DPI.
                const double dpi = 300d;
                var pageWidthDip = 210d * 96d / 25.4d;
                var pageHeightDip = 297d * 96d / 25.4d;
                var originalWidth = pageViewbox.Width;
                var originalHeight = pageViewbox.Height;
                var pageSurface = pageViewbox.Child as Border;
                var originalEffect = pageSurface?.Effect;
                try
                {
                    if (pageSurface is not null) pageSurface.Effect = null;
                    pageViewbox.Width = pageWidthDip;
                    pageViewbox.Height = pageHeightDip;
                    pageViewbox.Measure(new Size(pageWidthDip, pageHeightDip));
                    pageViewbox.Arrange(new Rect(0, 0, pageWidthDip, pageHeightDip));
                    pageViewbox.UpdateLayout();
                    var bitmap = new RenderTargetBitmap(
                        (int)Math.Round(210d / 25.4d * dpi),
                        (int)Math.Round(297d / 25.4d * dpi),
                        dpi, dpi, PixelFormats.Pbgra32);
                    bitmap.Render(pageViewbox);
                    var imagePath = Path.Combine(tempRoot, $"page-{page:D4}.png");
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using (var output = File.Create(imagePath)) encoder.Save(output);
                    pageImages.Add(imagePath);
                }
                finally
                {
                    // Restore the designer's hidden preview host even when one page fails.
                    pageViewbox.Width = originalWidth;
                    pageViewbox.Height = originalHeight;
                    if (pageSurface is not null) pageSurface.Effect = originalEffect;
                    pageViewbox.InvalidateMeasure();
                }
            }

            var htmlPath = Path.Combine(tempRoot, "preview.html");
            var html = new StringBuilder();
            html.AppendLine("<!doctype html><html><head><meta charset=\"utf-8\"><style>");
            html.AppendLine("@page{size:A4 portrait;margin:0}html,body{margin:0;padding:0;width:210mm;background:#fff}");
            html.AppendLine(".page{width:210mm;height:297mm;page-break-after:always;break-after:page;overflow:hidden;margin:0;padding:0}");
            html.AppendLine(".page:last-child{page-break-after:auto;break-after:auto}img{display:block;width:210mm;height:297mm;margin:0;padding:0}");
            html.AppendLine("</style></head><body>");
            foreach (var image in pageImages)
                html.Append("<section class=\"page\"><img src=\"").Append(new Uri(image).AbsoluteUri).AppendLine("\"></section>");
            html.AppendLine("</body></html>");
            File.WriteAllText(htmlPath, html.ToString(), new UTF8Encoding(false));

            var edge = FindEdgeExecutable();
            if (edge is null)
                throw new FileNotFoundException("لم يتم العثور على Microsoft Edge، وهو مطلوب لتحويل معاينة الصفحات إلى PDF. افتح Edge أو أصلح تثبيته ثم أعد المحاولة.");
            var outputPdf = Path.GetFullPath(save.FileName);
            if (File.Exists(outputPdf)) File.Delete(outputPdf);
            var start = new ProcessStartInfo(edge)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = tempRoot
            };
            start.ArgumentList.Add("--headless=new");
            start.ArgumentList.Add("--disable-gpu");
            start.ArgumentList.Add("--no-first-run");
            start.ArgumentList.Add("--no-pdf-header-footer");
            start.ArgumentList.Add("--disable-print-preview");
            start.ArgumentList.Add("--user-data-dir=" + Path.Combine(tempRoot, "edge-profile"));
            start.ArgumentList.Add("--print-to-pdf=" + outputPdf);
            start.ArgumentList.Add(new Uri(htmlPath).AbsoluteUri);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("تعذر تشغيل Edge لتصدير PDF.");
            using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(90));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new TimeoutException("انتهت مهلة تصدير PDF.");
            }
            if (process.ExitCode != 0)
                throw new InvalidOperationException($"أغلق Microsoft Edge عملية إنشاء PDF برمز الخطأ {process.ExitCode}.");

            if (!File.Exists(outputPdf) || new FileInfo(outputPdf).Length < 500)
                throw new InvalidDataException("لم يتم إنشاء ملف PDF صالح من المعاينة.");
            using (var check = File.OpenRead(outputPdf))
            {
                var signature = new byte[5];
                if (check.Read(signature, 0, signature.Length) != signature.Length ||
                    Encoding.ASCII.GetString(signature) != "%PDF-")
                    throw new InvalidDataException("الملف الناتج لا يحتوي على ترويسة PDF صحيحة.");

                var tailLength = (int)Math.Min(4096, check.Length);
                check.Seek(-tailLength, SeekOrigin.End);
                var tail = new byte[tailLength];
                check.ReadExactly(tail);
                if (!Encoding.ASCII.GetString(tail).Contains("%%EOF", StringComparison.Ordinal))
                    throw new InvalidDataException("ملف PDF غير مكتمل؛ لم يتم العثور على علامة نهاية الملف.");
            }
            SetStatus($"تم تصدير معاينة PDF بنجاح: {pageImages.Count} صفحة — {outputPdf}", isSuccess: true);
            MessageBox.Show($"تم تصدير معاينة PDF بنجاح.\nعدد الصفحات: {pageImages.Count}\n\n{outputPdf}", "اكتمل التصدير", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            SetStatus("تعذر تصدير معاينة PDF: " + ex.Message, isSuccess: false);
            MessageBox.Show("تعذر تصدير معاينة PDF:\n" + ex.Message, "تعذر التصدير", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _studioPreviewPageNumber = oldPage;
            RefreshStudioPreviews();
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }

        await System.Threading.Tasks.Task.CompletedTask;
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
}
