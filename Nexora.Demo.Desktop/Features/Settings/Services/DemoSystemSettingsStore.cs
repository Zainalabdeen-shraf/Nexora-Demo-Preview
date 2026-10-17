using System.IO;
using System.Text;
using System.Text.Json;
using Nexora.Demo.Desktop.Features.Settings.Models;

namespace Nexora.Demo.Desktop.Features.Settings.Services;

/// <summary>Local demo preferences only. This store does not configure a real network device.</summary>
public static class DemoSystemSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private static string? _loadWarning;
    private static DemoSystemPreferences _current = LoadInitial();

    public static DemoSystemPreferences Current => _current;
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexoraProductDemo");
    public static string StorePath => Path.Combine(DataDirectory, "system-preferences-v1.json");
    public static string BackupHistoryPath => Path.Combine(DataDirectory, "backup-history-v1.json");
    public static string? LoadWarning => _loadWarning;

    private static DemoSystemPreferences LoadInitial()
    {
        if (!File.Exists(StorePath)) return new DemoSystemPreferences();
        try
        {
            var loaded = JsonSerializer.Deserialize<DemoSystemPreferences>(File.ReadAllText(StorePath), JsonOptions)
                ?? throw new InvalidDataException("ملف إعدادات النظام فارغ.");
            Normalize(loaded);
            return loaded;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            _loadWarning = "تعذرت قراءة إعدادات النظام المحفوظة؛ عُرضت القيم الافتراضية، ولم يُحذف الملف المتعذر قراءته.";
            return new DemoSystemPreferences();
        }
    }

    public static void Save(DemoSystemPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var snapshot = new DemoSystemPreferences
        {
            StartupSection = preferences.StartupSection,
            NavigationDensity = preferences.NavigationDensity,
            ConfirmBeforeBackup = preferences.ConfirmBeforeBackup,
            ShowSuccessMessages = preferences.ShowSuccessMessages
        };
        Normalize(snapshot);

        Directory.CreateDirectory(DataDirectory);
        if (_loadWarning is not null && File.Exists(StorePath))
        {
            var preserved = StorePath + ".unreadable-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            File.Copy(StorePath, preserved, overwrite: false);
        }

        var temporaryPath = StorePath + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(snapshot, JsonOptions), new UTF8Encoding(false));
            File.Move(temporaryPath, StorePath, overwrite: true);
            _current = snapshot;
            _loadWarning = null;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try { File.Delete(temporaryPath); } catch { /* preserve the original I/O failure */ }
            }
        }
    }

    private static void Normalize(DemoSystemPreferences preferences)
    {
        if (preferences.StartupSection is not ("dashboard" or "network-center" or "cards"))
            preferences.StartupSection = "dashboard";
        if (preferences.NavigationDensity is not ("مريحة" or "مدمجة"))
            preferences.NavigationDensity = "مريحة";
    }
}