using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Nexora.Demo.Desktop.Features.Settings.Services;

namespace Nexora.Demo.Desktop.Features.Settings.Views;

public sealed class DemoBackupHistoryRecord
{
    public DateTime CreatedAt { get; set; }
    public int FilesIncluded { get; set; }
    public string SizeText { get; set; } = string.Empty;
    public string BackupPath { get; set; } = string.Empty;
}

public partial class BackupSettingsView : UserControl
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public ObservableCollection<DemoBackupHistoryRecord> BackupHistory { get; } = new();

    public BackupSettingsView()
    {
        InitializeComponent();
        DataContext = this;
        DataPathTextBlock.Text = "مصدر النسخ: " + DemoSystemSettingsStore.DataDirectory;
        LoadHistory();
        UpdateCount();
    }

    private void LoadHistory()
    {
        var path = DemoSystemSettingsStore.BackupHistoryPath;
        if (!File.Exists(path)) return;
        try
        {
            var records = JsonSerializer.Deserialize<List<DemoBackupHistoryRecord>>(File.ReadAllText(path), JsonOptions) ?? new List<DemoBackupHistoryRecord>();
            foreach (var record in records.OrderByDescending(item => item.CreatedAt)) BackupHistory.Add(record);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            BackupStatusTextBlock.Text = "تعذر قراءة سجل النسخ. لم يتم حذف ملف السجل: " + ex.Message;
        }
    }

    private void UpdateCount() => BackupCountTextBlock.Text = $"{BackupHistory.Count:N0} نسخ";

    private void CreateBackupButton_Click(object sender, RoutedEventArgs e)
    {
        if (DemoSystemSettingsStore.Current.ConfirmBeforeBackup)
        {
            var answer = MessageBox.Show(Window.GetWindow(this),
                "سيتم إنشاء ملف ZIP يحتوي على ملفات بيانات الديمو المحفوظة محليًا. قد تتضمن بعض الملفات بيانات كروت تجريبية وإعداداتك. هل تريد المتابعة؟",
                "تأكيد النسخ الاحتياطي", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (answer != MessageBoxResult.Yes) return;
        }

        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var suggestedDirectory = Path.Combine(documents, "NexoraDemoBackups");
        var dialog = new SaveFileDialog
        {
            Title = "حفظ نسخة Nexora التجريبية",
            Filter = "ZIP backup (*.zip)|*.zip",
            DefaultExt = ".zip",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = "Nexora-Demo-Backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".zip",
            InitialDirectory = Directory.Exists(suggestedDirectory) ? suggestedDirectory : documents
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

        string? stagingRoot = null;
        string? stagedZipPath = null;
        try
        {
            var dataRoot = DemoSystemSettingsStore.DataDirectory;
            stagingRoot = Path.Combine(Path.GetTempPath(), "NexoraDemoBackup-" + Guid.NewGuid().ToString("N"));
            var stagingData = Path.Combine(stagingRoot, "Data");
            Directory.CreateDirectory(stagingData);
            var copiedFiles = 0;

            if (Directory.Exists(dataRoot))
            {
                var targetFullPath = Path.GetFullPath(dialog.FileName);
                foreach (var sourceFile in Directory.EnumerateFiles(dataRoot, "*", SearchOption.AllDirectories))
                {
                    var fullSource = Path.GetFullPath(sourceFile);
                    if (string.Equals(fullSource, targetFullPath, StringComparison.OrdinalIgnoreCase)) continue;
                    var relative = Path.GetRelativePath(dataRoot, sourceFile);
                    var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (parts.Length > 0 && string.Equals(parts[0], "logs", StringComparison.OrdinalIgnoreCase)) continue;
                    var extension = Path.GetExtension(sourceFile);
                    if (extension.Equals(".tmp", StringComparison.OrdinalIgnoreCase) ||
                        extension.Equals(".log", StringComparison.OrdinalIgnoreCase) ||
                        extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)) continue;

                    var destination = Path.Combine(stagingData, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(sourceFile, destination, overwrite: true);
                    copiedFiles++;
                }
            }

            var manifest = new
            {
                Application = "Nexora Demo",
                CreatedAt = DateTimeOffset.Now,
                Scope = "Local demo data only",
                SourceDirectory = dataRoot,
                FilesIncluded = copiedFiles,
                Notes = new[]
                {
                    "This archive contains copied local demo files, not a production database.",
                    "It does not contain RouterOS device state or data that was never saved locally.",
                    "Log files and temporary files are excluded."
                }
            };
            File.WriteAllText(Path.Combine(stagingRoot, "backup-manifest.json"), JsonSerializer.Serialize(manifest, JsonOptions), new UTF8Encoding(false));

            var outputDirectory = Path.GetDirectoryName(Path.GetFullPath(dialog.FileName));
            if (string.IsNullOrWhiteSpace(outputDirectory)) throw new InvalidOperationException("تعذر تحديد مجلد حفظ النسخة الاحتياطية.");
            Directory.CreateDirectory(outputDirectory);
            stagedZipPath = Path.Combine(outputDirectory, "." + Path.GetFileName(dialog.FileName) + "." + Guid.NewGuid().ToString("N") + ".tmp");
            ZipFile.CreateFromDirectory(stagingRoot, stagedZipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            File.Move(stagedZipPath, dialog.FileName, overwrite: true);
            stagedZipPath = null;

            var fileInfo = new FileInfo(dialog.FileName);
            var record = new DemoBackupHistoryRecord
            {
                CreatedAt = DateTime.Now,
                FilesIncluded = copiedFiles,
                SizeText = FormatSize(fileInfo.Length),
                BackupPath = dialog.FileName
            };
            BackupHistory.Insert(0, record);
            while (BackupHistory.Count > 20) BackupHistory.RemoveAt(BackupHistory.Count - 1);
            UpdateCount();

            try
            {
                Directory.CreateDirectory(DemoSystemSettingsStore.DataDirectory);
                var historyTemp = DemoSystemSettingsStore.BackupHistoryPath + ".tmp";
                File.WriteAllText(historyTemp, JsonSerializer.Serialize(BackupHistory.ToList(), JsonOptions), new UTF8Encoding(false));
                File.Move(historyTemp, DemoSystemSettingsStore.BackupHistoryPath, overwrite: true);
            }
            catch (Exception historyEx) when (historyEx is IOException or UnauthorizedAccessException or JsonException)
            {
                BackupStatusTextBlock.Text = $"تم إنشاء النسخة، لكن تعذر تحديث سجل النسخ: {historyEx.Message}\nالمسار: {dialog.FileName}";
                return;
            }

            BackupStatusTextBlock.Text = $"تم إنشاء النسخة بنجاح. الملفات المدرجة: {copiedFiles:N0}.\nالمسار: {dialog.FileName}";
            if (DemoSystemSettingsStore.Current.ShowSuccessMessages)
                MessageBox.Show(Window.GetWindow(this), $"تم إنشاء النسخة الاحتياطية:\n{dialog.FileName}", "اكتمل النسخ الاحتياطي", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or NotSupportedException)
        {
            BackupStatusTextBlock.Text = "تعذر إنشاء النسخة الاحتياطية: " + ex.Message;
        }
        finally
        {
            if (stagedZipPath is not null && File.Exists(stagedZipPath))
            {
                try { File.Delete(stagedZipPath); } catch { /* preserve the primary backup result */ }
            }
            if (stagingRoot is not null && Directory.Exists(stagingRoot))
            {
                try { Directory.Delete(stagingRoot, recursive: true); } catch { /* keep the backup result and preserve original failure */ }
            }
        }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / 1024d / 1024d:N1} MB",
        >= 1024 => $"{bytes / 1024d:N1} KB",
        _ => $"{bytes:N0} B"
    };

    private void OpenDataFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(DemoSystemSettingsStore.DataDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", "\"" + DemoSystemSettingsStore.DataDirectory + "\"") { UseShellExecute = true });
            BackupStatusTextBlock.Text = "فُتح مجلد بيانات الديمو المحلي.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            BackupStatusTextBlock.Text = "تعذر فتح مجلد البيانات: " + ex.Message;
        }
    }
}