using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Nexora.Demo.Desktop.Features.NetworkOperations.Monitoring;

/// <summary>Local persistent history of RouterOS interface-state incidents for this demo installation.</summary>
public static class NetworkInterfaceIncidentStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string StoreDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexoraDemo-ResponsivePreview", "NetworkOperations");
    private static readonly string StorePath = Path.Combine(StoreDirectory, "InterfaceIncidents.json");

    public static ObservableCollection<NetworkInterfaceIncident> Incidents { get; } = new();
    public static string? PersistenceError { get; private set; }

    static NetworkInterfaceIncidentStore() => Load();

    public static void ObserveDown(string interfaceName, DateTimeOffset observedAt, int consecutiveDownSamples, DateTimeOffset firstDownAt)
    {
        var incident = FindOpen(interfaceName);
        if (incident is null && consecutiveDownSamples < 3) return;

        if (incident is null)
        {
            incident = new NetworkInterfaceIncident
            {
                Id = $"NCI-{firstDownAt:yyyyMMdd-HHmmss}-{interfaceName}",
                InterfaceName = interfaceName,
                StartedAt = firstDownAt,
                LastObservedAt = observedAt,
                Status = "مفتوحة",
                Evidence = "تم رصد Running=false في 3 قراءات ناجحة متتالية. هذه المعلومة لا تحدد وحدها سبب التوقف ولا تثبت انقطاع الإنترنت بالكامل."
            };
            Incidents.Insert(0, incident);
        }
        else
        {
            incident.LastObservedAt = observedAt;
            incident.Evidence = $"الواجهة ما زالت تعرض Running=false؛ آخر رصد بعد {consecutiveDownSamples:N0} قراءات متتالية. السبب لم يُحسم من حالة الواجهة وحدها.";
        }
        Save();
    }

    public static void ObserveUp(string interfaceName, DateTimeOffset observedAt, int consecutiveUpSamples)
    {
        var incident = FindOpen(interfaceName);
        if (incident is null) return;

        incident.LastObservedAt = observedAt;
        if (consecutiveUpSamples >= 2)
        {
            incident.Status = "تمت الاستعادة";
            incident.ResolvedAt = observedAt;
            incident.Evidence = "عادت Running=true في قراءتين ناجحتين متتاليتين. هذا يؤكد عودة حالة الواجهة فقط، وليس سلامة الإنترنت أو جميع خدمات الشبكة.";
        }
        else
        {
            incident.Evidence = "ظهرت قراءة واحدة Running=true؛ يجري التحقق من الاستعادة بانتظار قراءة ناجحة أخرى.";
        }
        Save();
    }

    public static void ObserveAdministrativelyDisabled(string interfaceName, DateTimeOffset observedAt)
    {
        var incident = FindOpen(interfaceName);
        if (incident is null) return;
        incident.LastObservedAt = observedAt;
        incident.Evidence = "الواجهة معطلة إداريًا في RouterOS؛ لم تعتبرها Nexora مستعادة، لأن الحالة لا تثبت عودة الوصلة.";
        Save();
    }

    private static NetworkInterfaceIncident? FindOpen(string interfaceName) => Incidents.FirstOrDefault(item =>
        item.Status == "مفتوحة" && string.Equals(item.InterfaceName, interfaceName, StringComparison.OrdinalIgnoreCase));

    private static void Load()
    {
        if (!File.Exists(StorePath)) return;
        try
        {
            var json = File.ReadAllText(StorePath, Encoding.UTF8);
            var items = JsonSerializer.Deserialize<List<NetworkInterfaceIncident>>(json, JsonOptions) ?? new();
            foreach (var item in items.OrderByDescending(item => item.StartedAt)) Incidents.Add(item);
            PersistenceError = null;
        }
        catch (Exception ex)
        {
            try
            {
                var quarantine = StorePath + ".invalid-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                File.Move(StorePath, quarantine, overwrite: false);
            }
            catch { /* Preserve the original error message if quarantine cannot be completed. */ }
            PersistenceError = $"تعذر قراءة سجل الحوادث السابق: {ex.Message}";
        }
    }

    private static void Save()
    {
        string? temporaryPath = null;
        try
        {
            Directory.CreateDirectory(StoreDirectory);
            temporaryPath = StorePath + ".tmp";
            var json = JsonSerializer.Serialize(Incidents.ToArray(), JsonOptions);
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            File.Move(temporaryPath, StorePath, overwrite: true);
            PersistenceError = null;
        }
        catch (Exception ex)
        {
            PersistenceError = $"تعذر حفظ سجل الحوادث محليًا: {ex.Message}";
            if (temporaryPath is not null)
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { /* Best-effort temp cleanup. */ }
            }
        }
    }
}