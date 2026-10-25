using System.IO;
using System.Text;
using System.Text.Json;

namespace Nexora.Demo.Desktop.Features.Cards.Services;

/// <summary>
/// Persists batch-only operational metadata for the product demo. This file is separate
/// from card issuance data so disable/archive state and operation feedback do not rewrite
/// the generated card records by themselves.
/// </summary>
public sealed class BatchManagementMetadataStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexoraProductDemo", "card-batch-management-v1.json");
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private static Dictionary<string, BatchManagementMetadata> _entries = new(StringComparer.OrdinalIgnoreCase);
    private static string? _storageWarning;
    private static bool _writesBlocked;

    static BatchManagementMetadataStore()
    {
        if (!File.Exists(StorePath)) return;
        try
        {
            var json = File.ReadAllText(StorePath);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, BatchManagementMetadata>>(json, JsonOptions)
                ?? throw new InvalidDataException("ملف بيانات إدارة الدفعات فارغ أو غير صالح.");
            _entries = new Dictionary<string, BatchManagementMetadata>(loaded, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            _writesBlocked = true;
            _storageWarning = "تعذر قراءة بيانات إدارة الدفعات. تم إيقاف عمليات الكتابة حفاظاً على السجل الحالي. " + ex.Message;
        }
    }

    public string? StorageWarning => _storageWarning;

    public BatchManagementMetadata Get(string batchId, string targetSystem)
    {
        lock (Gate)
        {
            var key = BuildKey(batchId, targetSystem);
            return _entries.TryGetValue(key, out var entry)
                ? entry
                : new BatchManagementMetadata(batchId, targetSystem);
        }
    }

    public void SetSyncState(string batchId, string targetSystem, string state, string message)
    {
        Update(batchId, targetSystem, entry => entry with
        {
            LastSyncState = state,
            LastSyncMessage = TrimMessage(message),
            LastSyncAt = string.Equals(state, "جارٍ المزامنة", StringComparison.Ordinal)
                ? entry.LastSyncAt
                : DateTimeOffset.Now
        });
    }

    public void SetDisabled(string batchId, string targetSystem, bool disabled, string message)
    {
        Update(batchId, targetSystem, entry => entry with
        {
            IsDisabled = disabled,
            LastOperationMessage = TrimMessage(message),
            LastOperationAt = DateTimeOffset.Now
        });
    }

    public void SetArchived(string batchId, string targetSystem, bool archived, bool disableWhenArchiving, string message)
    {
        Update(batchId, targetSystem, entry => entry with
        {
            IsArchived = archived,
            IsDisabled = archived && disableWhenArchiving ? true : entry.IsDisabled,
            ArchivedAt = archived ? DateTimeOffset.Now : null,
            LastOperationMessage = TrimMessage(message),
            LastOperationAt = DateTimeOffset.Now
        });
    }

    public void RecordPrintSubmission(string batchId, string targetSystem, string printerName, int count)
    {
        Update(batchId, targetSystem, entry => entry with
        {
            LastPrinterName = TrimMessage(printerName),
            LastPrintAt = DateTimeOffset.Now,
            LastPrintCount = Math.Max(0, count),
            LastOperationMessage = $"أُرسلت {Math.Max(0, count):N0} بطاقة إلى الطابعة {TrimMessage(printerName)}.",
            LastOperationAt = DateTimeOffset.Now
        });
    }

    private static void Update(string batchId, string targetSystem, Func<BatchManagementMetadata, BatchManagementMetadata> update)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetSystem);
        lock (Gate)
        {
            if (_writesBlocked)
                throw new InvalidOperationException(_storageWarning ?? "تعذر الكتابة إلى بيانات إدارة الدفعات.");

            var key = BuildKey(batchId, targetSystem);
            var current = _entries.TryGetValue(key, out var existing)
                ? existing
                : new BatchManagementMetadata(batchId, targetSystem);
            var next = new Dictionary<string, BatchManagementMetadata>(_entries, StringComparer.OrdinalIgnoreCase)
            {
                [key] = update(current)
            };

            var directory = Path.GetDirectoryName(StorePath)!;
            Directory.CreateDirectory(directory);
            var tempPath = StorePath + ".tmp";
            var json = JsonSerializer.Serialize(next, JsonOptions);
            File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tempPath, StorePath, overwrite: true);
            _entries = next;
            _storageWarning = null;
        }
    }

    private static string BuildKey(string batchId, string targetSystem) =>
        targetSystem.Trim().ToUpperInvariant() + "\u001F" + batchId.Trim().ToUpperInvariant();

    private static string TrimMessage(string? value) => string.IsNullOrWhiteSpace(value)
        ? string.Empty
        : value.Trim().Length <= 500 ? value.Trim() : value.Trim()[..500];
}

public sealed record BatchManagementMetadata(
    string BatchId,
    string TargetSystem,
    bool IsDisabled = false,
    bool IsArchived = false,
    string LastSyncState = "",
    string LastSyncMessage = "",
    DateTimeOffset? LastSyncAt = null,
    DateTimeOffset? ArchivedAt = null,
    string LastPrinterName = "",
    DateTimeOffset? LastPrintAt = null,
    int LastPrintCount = 0,
    string LastOperationMessage = "",
    DateTimeOffset? LastOperationAt = null);
