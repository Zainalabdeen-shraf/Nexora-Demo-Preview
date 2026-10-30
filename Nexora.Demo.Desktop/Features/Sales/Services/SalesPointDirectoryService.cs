using System.IO;
using System.Text;
using System.Text.Json;
using Nexora.Demo.Desktop.Features.Sales.Models;

namespace Nexora.Demo.Desktop.Features.Sales.Services;

/// <summary>
/// Persists the sales-point directory locally for the desktop demo. This is not a
/// cloud-synchronized, multi-user or accounting backend.
/// </summary>
public sealed class SalesPointDirectoryService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _gate = new();
    private readonly string _storePath;
    private List<SalesPointRecord> _points = [];
    private bool _writesBlocked;
    private string? _storageWarning;

    public SalesPointDirectoryService(IEnumerable<string> initialPointNames)
    {
        ArgumentNullException.ThrowIfNull(initialPointNames);
        _storePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NexoraProductDemo", "sales-points-v1.json");

        if (File.Exists(_storePath))
        {
            LoadExisting();
            return;
        }

        _points = BuildInitialPoints(initialPointNames);
        try
        {
            Persist(_points);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _writesBlocked = true;
            _storageWarning = "تعذر حفظ سجل نقاط البيع محليًا؛ يمكنك استعراض بيانات البداية، لكن تم إيقاف التعديل حفاظًا على سلامة البيانات. " + ex.Message;
        }
    }

    public string StorePath => _storePath;
    public bool CanWrite => !_writesBlocked;
    public string? StorageWarning => _storageWarning;
    public event EventHandler? Changed;

    public IReadOnlyList<SalesPointRecord> GetAll()
    {
        lock (_gate)
            return _points.OrderBy(point => point.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public SalesPointRecord Save(SalesPointDraft draft, string? id = null)
    {
        ArgumentNullException.ThrowIfNull(draft);
        SalesPointRecord saved;
        lock (_gate)
        {
            EnsureWritable();
            var name = Required(draft.Name, "اسم نقطة البيع", 2, 100);
            var pointType = Required(draft.PointType, "نوع نقطة البيع", 2, 40);
            var contact = Optional(draft.ContactName, 100);
            var phone = Optional(draft.Phone, 32);
            var location = Optional(draft.Location, 160);
            var notes = Optional(draft.Notes, 500);
            if (draft.CommissionPercent < 0m || draft.CommissionPercent > 100m)
                throw new ArgumentOutOfRangeException(nameof(draft), "نسبة العمولة يجب أن تكون بين 0 و100٪.");
            var settlementCycle = Required(draft.SettlementCycle, "دورية التسوية", 2, 40);

            if (_points.Any(point => !string.Equals(point.Id, id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(point.Name, name, StringComparison.CurrentCultureIgnoreCase)))
                throw new InvalidOperationException("يوجد بالفعل سجل لنقطة بيع بهذا الاسم. استخدم اسمًا مميزًا.");

            var now = DateTimeOffset.Now;
            List<SalesPointRecord> next;
            if (!string.IsNullOrWhiteSpace(id))
            {
                var current = _points.FirstOrDefault(point => string.Equals(point.Id, id, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException("لم تعد نقطة البيع المحددة موجودة. حدّث القائمة ثم حاول مجددًا.");
                if (current.IsSystemPoint || string.Equals(current.Name, "كروت الشبكات", StringComparison.Ordinal))
                    throw new InvalidOperationException("نقطة «كروت الشبكات» الافتراضية محمية لأنها تمثل قناة البيع الإلكتروني. لا يمكن تعديلها أو حذفها.");
                saved = current with
                {
                    Name = name,
                    PointType = pointType,
                    ContactName = contact,
                    Phone = phone,
                    Location = location,
                    Notes = notes,
                    CommissionPercent = draft.CommissionPercent,
                    AllowCredit = draft.AllowCredit,
                    SettlementCycle = settlementCycle,
                    UpdatedAt = now
                };
                next = _points.Select(point => string.Equals(point.Id, current.Id, StringComparison.OrdinalIgnoreCase) ? saved : point).ToList();
            }
            else
            {
                saved = new SalesPointRecord(
                    Guid.NewGuid().ToString("N"),
                    NextCode(_points),
                    name,
                    pointType,
                    contact,
                    phone,
                    location,
                    notes,
                    true,
                    now,
                    now,
                    draft.CommissionPercent,
                    draft.AllowCredit,
                    settlementCycle,
                    false);
                next = [.. _points, saved];
            }

            Persist(next);
            _points = next;
            _storageWarning = null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return saved;
    }

    public SalesPointRecord SetActive(string id, bool isActive)
    {
        lock (_gate)
        {
            EnsureWritable();
            var current = _points.FirstOrDefault(point => string.Equals(point.Id, id, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("لم تعد نقطة البيع المحددة موجودة. حدّث القائمة ثم حاول مجددًا.");
            if (current.IsSystemPoint || string.Equals(current.Name, "كروت الشبكات", StringComparison.Ordinal))
                throw new InvalidOperationException("نقطة «كروت الشبكات» قناة افتراضية ثابتة ولا يمكن إيقافها.");
            if (current.IsActive == isActive)
                return current;

            var updated = current with { IsActive = isActive, UpdatedAt = DateTimeOffset.Now };
            var next = _points.Select(point => string.Equals(point.Id, current.Id, StringComparison.OrdinalIgnoreCase) ? updated : point).ToList();
            Persist(next);
            _points = next;
            _storageWarning = null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return GetAll().First(point => string.Equals(point.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public void Delete(string id, bool hasLinkedTransactions)
    {
        lock (_gate)
        {
            EnsureWritable();
            var current = _points.FirstOrDefault(point => string.Equals(point.Id, id, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException("لم تعد نقطة البيع المحددة موجودة. حدّث القائمة ثم حاول مجددًا.");
            if (current.IsSystemPoint || string.Equals(current.Name, "كروت الشبكات", StringComparison.Ordinal))
                throw new InvalidOperationException("لا يمكن حذف نقطة «كروت الشبكات» الافتراضية.");
            if (hasLinkedTransactions)
                throw new InvalidOperationException("لا يمكن حذف نقطة بيع مرتبطة بمعاملات سابقة. أوقفها بدلًا من حذف سجلها.");

            var next = _points.Where(point => !string.Equals(point.Id, current.Id, StringComparison.OrdinalIgnoreCase)).ToList();
            Persist(next);
            _points = next;
            _storageWarning = null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void LoadExisting()
    {
        try
        {
            var loaded = JsonSerializer.Deserialize<List<SalesPointRecord>>(File.ReadAllText(_storePath), JsonOptions)
                ?? throw new InvalidDataException("ملف سجل نقاط البيع فارغ أو غير صالح.");
            if (loaded.Any(point => point is null
                || string.IsNullOrWhiteSpace(point.Id)
                || string.IsNullOrWhiteSpace(point.Code)
                || string.IsNullOrWhiteSpace(point.Name)))
                throw new InvalidDataException("يحتوي سجل نقاط البيع على عناصر ناقصة.");
            if (loaded.GroupBy(point => point.Id, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1)
                || loaded.GroupBy(point => point.Name.Trim(), StringComparer.CurrentCultureIgnoreCase).Any(group => group.Count() > 1))
                throw new InvalidDataException("يحتوي سجل نقاط البيع على معرّفات أو أسماء مكررة.");

            var points = loaded.Select(point => point with
            {
                Name = point.Name.Trim(),
                PointType = string.IsNullOrWhiteSpace(point.PointType) ? "نقطة بيع" : point.PointType.Trim(),
                ContactName = point.ContactName?.Trim() ?? string.Empty,
                Phone = point.Phone?.Trim() ?? string.Empty,
                Location = point.Location?.Trim() ?? string.Empty,
                Notes = point.Notes?.Trim() ?? string.Empty,
                CommissionPercent = point.CommissionPercent < 0m || point.CommissionPercent > 100m ? 0m : point.CommissionPercent,
                SettlementCycle = string.IsNullOrWhiteSpace(point.SettlementCycle) ? "فوري" : point.SettlementCycle.Trim(),
                IsSystemPoint = point.IsSystemPoint || string.Equals(point.Name.Trim(), "كروت الشبكات", StringComparison.Ordinal)
            }).ToList();
            var needsUpgrade = points.Where((point, index) => point != loaded[index]).Any();
            var onlineIndex = points.FindIndex(point => string.Equals(point.Name, "كروت الشبكات", StringComparison.Ordinal));
            if (onlineIndex >= 0)
            {
                var online = points[onlineIndex] with
                {
                    Code = "SP-ONLINE",
                    PointType = "قناة إلكترونية",
                    CommissionPercent = 0m,
                    AllowCredit = false,
                    SettlementCycle = "تجميع مركزي",
                    IsActive = true,
                    IsSystemPoint = true
                };
                if (online != points[onlineIndex]) needsUpgrade = true;
                points[onlineIndex] = online;
            }
            else
            {
                points.Add(CreateOnlineSystemPoint());
                needsUpgrade = true;
            }

            _points = points;
            if (needsUpgrade)
            {
                try
                {
                    Persist(_points);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
                {
                    _writesBlocked = true;
                    _storageWarning = "تمت قراءة نقاط البيع، لكن تعذر ترقية سجل القناة الافتراضية إلى الإصدار الحالي. لم يُستبدل الملف؛ أُوقفت الكتابة لحماية البيانات. " + ex.Message;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            _writesBlocked = true;
            _storageWarning = "تعذرت قراءة سجل نقاط البيع. لم يتم استبدال الملف المتعذر قراءته، وتم إيقاف عمليات الكتابة للحفاظ على البيانات. " + ex.Message;
            _points = [];
        }
    }

    private void EnsureWritable()
    {
        if (_writesBlocked)
            throw new InvalidOperationException(_storageWarning ?? "تم إيقاف تعديل سجل نقاط البيع لحماية البيانات.");
    }

    private void Persist(IReadOnlyCollection<SalesPointRecord> points)
    {
        var directory = Path.GetDirectoryName(_storePath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = _storePath + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(points, JsonOptions), new UTF8Encoding(false));
            File.Move(temporaryPath, _storePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                try { File.Delete(temporaryPath); } catch { /* Keep the original storage failure. */ }
            }
        }
    }

    private static List<SalesPointRecord> BuildInitialPoints(IEnumerable<string> names)
    {
        var now = DateTimeOffset.Now;
        var distinct = names.Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        var points = distinct.Where(name => !string.Equals(name, "كروت الشبكات", StringComparison.Ordinal))
            .Select((name, index) => new SalesPointRecord(
                Guid.NewGuid().ToString("N"),
                $"SP-{index + 1:0000}",
                name,
                name.Contains("وكيل", StringComparison.CurrentCultureIgnoreCase) ? "وكيل"
                    : name.Contains("رئيسي", StringComparison.CurrentCultureIgnoreCase) ? "فرع رئيسي" : "فرع",
                string.Empty,
                string.Empty,
                string.Empty,
                "سجل بداية تجريبي؛ راجع البيانات لتناسب شبكة العمل.",
                true,
                now,
                now,
                0m,
                false,
                "فوري",
                false)).ToList();
        points.Add(CreateOnlineSystemPoint());
        return points;
    }

    private static SalesPointRecord CreateOnlineSystemPoint()
    {
        var now = DateTimeOffset.Now;
        return new SalesPointRecord(
            "system-online-network-cards", "SP-ONLINE", "كروت الشبكات", "قناة إلكترونية",
            string.Empty, string.Empty, string.Empty,
            "نقطة افتراضية ثابتة تمثل قناة البيع عبر الإنترنت؛ لا تُدار كنقطة ميدانية ولا تُطبع منها كروت ورقية.",
            true, now, now, 0m, false, "تجميع مركزي", true);
    }

    private static string NextCode(IEnumerable<SalesPointRecord> points)
    {
        var next = points.Select(point => int.TryParse(point.Code.StartsWith("SP-", StringComparison.OrdinalIgnoreCase) ? point.Code[3..] : string.Empty, out var number) ? number : 0)
            .DefaultIfEmpty(0).Max() + 1;
        return $"SP-{next:0000}";
    }

    private static string Required(string? value, string field, int minLength, int maxLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length < minLength)
            throw new ArgumentException($"{field} يجب ألا يقل عن {minLength} حرفين.");
        if (trimmed.Length > maxLength)
            throw new ArgumentException($"{field} يجب ألا يتجاوز {maxLength} حرفًا.");
        return trimmed;
    }

    private static string Optional(string? value, int maxLength)
    {
        var trimmed = value?.Trim() ?? string.Empty;
        if (trimmed.Length > maxLength)
            throw new ArgumentException($"أحد الحقول الاختيارية يتجاوز الحد المسموح وهو {maxLength} حرفًا.");
        return trimmed;
    }
}
