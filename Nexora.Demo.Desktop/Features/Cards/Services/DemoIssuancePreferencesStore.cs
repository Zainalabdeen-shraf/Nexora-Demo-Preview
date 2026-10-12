using System.Text.Json;
using System.IO;

namespace Nexora.Demo.Desktop.Features.Cards.Services;

/// <summary>Durable, local-only screen preferences. It never represents RouterOS state.</summary>
public sealed class DemoIssuancePreferencesStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexoraProductDemo", "issuance-preferences-v1.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public DemoIssuancePreferences Load()
    {
        if (!File.Exists(StorePath)) return new DemoIssuancePreferences();
        try
        {
            return JsonSerializer.Deserialize<DemoIssuancePreferences>(File.ReadAllText(StorePath), JsonOptions)
                ?? new DemoIssuancePreferences();
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("تعذر قراءة إعدادات شاشة الإصدار المحلية. لم يتم استبدال الملف حفاظًا على الإعدادات السابقة.", ex);
        }
    }

    public void Save(DemoIssuancePreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        var directory = Path.GetDirectoryName(StorePath)!;
        Directory.CreateDirectory(directory);
        var tempPath = StorePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(preferences, JsonOptions));
        File.Move(tempPath, StorePath, true);
    }
}

public sealed class DemoIssuancePreferences
{
    public int SchemaVersion { get; set; } = 1;
    public DemoIssuanceScreenPreferences HotSpot { get; set; } = new();
    public DemoIssuanceScreenPreferences UserManager { get; set; } = new();
}

public sealed class DemoIssuanceScreenPreferences
{
    public string? PackageId { get; set; }
    public string TemplateId { get; set; } = "nexora";
    public string PointOfSale { get; set; } = "main";
    public string RouterProfile { get; set; } = string.Empty;
    public string HotSpotServer { get; set; } = "hotspot1";
    public bool SmartPermissions { get; set; }
    public bool ShowCardNumber { get; set; } = true;
    public bool ShowUsername { get; set; } = true;
    public bool ShowPassword { get; set; } = true;
    public bool ShowPackage { get; set; } = true;
    public bool ShowPrice { get; set; } = true;
}
