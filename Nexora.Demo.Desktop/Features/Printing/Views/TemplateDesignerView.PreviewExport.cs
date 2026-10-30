using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Nexora.Demo.Desktop.Features.Printing.Views;

// Responsibility: export the exact in-app A4 preview pages to a shareable PDF.
public partial class TemplateDesignerView
{
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
                RefreshStudioPreviews(forceBuildSheet: true);
                PrintSheetPreviewHost.UpdateLayout();
                var pageViewbox = PrintSheetPreviewHost.Children.OfType<Viewbox>().FirstOrDefault()
                    ?? throw new InvalidOperationException("تعذر الوصول إلى صفحة المعاينة داخل مساحة العمل.");

                // Scale the same A4 composition to true 210 × 297 mm at 300 DPI.
                const double dpi = 300d;
                var pageWidthDip = 210d * 96d / 25.4d;
                var pageHeightDip = 297d * 96d / 25.4d;
                var originalWidth = pageViewbox.Width;
                var originalHeight = pageViewbox.Height;
                var pageSurface = pageViewbox.Child as Border;
                var originalEffect = pageSurface?.Effect;
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
                pageViewbox.Width = originalWidth;
                pageViewbox.Height = originalHeight;
                if (pageSurface is not null) pageSurface.Effect = originalEffect;
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
            if (!File.Exists(outputPdf) || new FileInfo(outputPdf).Length < 500)
                throw new InvalidDataException("لم يتم إنشاء ملف PDF صالح من المعاينة.");
            using (var check = File.OpenRead(outputPdf))
            {
                var signature = new byte[5];
                if (check.Read(signature, 0, signature.Length) != signature.Length || Encoding.ASCII.GetString(signature) != "%PDF-")
                    throw new InvalidDataException("الملف الناتج لا يحتوي على ترويسة PDF صحيحة.");
            }
            SetStatus($"تم تصدير ملف PDF بنجاح · {pageImages.Count} صفحة · {Path.GetFileName(outputPdf)}", isSuccess: true);
        }
        catch (Exception ex)
        {
            SetStatus("تعذر تصدير PDF: " + ex.Message, isSuccess: false);
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
