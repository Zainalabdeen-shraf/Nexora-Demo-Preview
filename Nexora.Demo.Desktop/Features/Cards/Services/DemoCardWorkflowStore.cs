// TabCardWorkflowStoreV1: local JSON state for demo actions and per-system issuance preferences.
using System.IO;
using System.Text;
using System.Text.Json;
using Nexora.Demo.Desktop.Features.Cards.Models;

namespace Nexora.Demo.Desktop.Features.Cards.Services;

/// <summary>
/// Persists local demo workflow state only. It never contacts RouterOS, a printer,
/// or an online-sales platform. Card data itself remains in DemoCardIssuanceService.
/// </summary>
public sealed class DemoCardWorkflowStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexoraProductDemo", "card-workflow-v1.json");
    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
    private static WorkflowSnapshot _state;
    private static string? _storageWarning;
    private static bool _writesBlocked;

    static DemoCardWorkflowStore()
    {
        _state = LoadSnapshot();
    }

    public string? StorageWarning => _storageWarning;

    public IReadOnlyList<DemoWorkflowActionRecord> GetActionHistory()
    {
        lock (Gate)
        {
            return _state.Actions
                .OrderByDescending(item => item.CreatedAt)
                .Select(item => new DemoWorkflowActionRecord(
                    item.Id, item.BatchId, item.TargetSystem, item.ActionType,
                    item.ActionLabel, item.Count, item.Description, item.CreatedAt))
                .ToArray();
        }
    }

    public void RecordLocalGeneration(string batchId, string targetSystem, int totalBatchCount,
        IEnumerable<GeneratedDemoCard> cards)
    {
        var batchCards = cards.ToList();
        Update(state =>
        {
            var systemKey = NormalizeSystemKey(targetSystem);
            var now = DateTimeOffset.Now;
            var batch = FindOrCreateBatch(state, batchId, systemKey, totalBatchCount, now);
            batch.TargetSystem = systemKey;
            batch.TotalCardCount = Math.Max(totalBatchCount, batch.TotalCardCount);
            batch.LastAction = "إنشاء دفعة";
            batch.UpdatedAt = now;

            foreach (var card in batchCards)
            {
                var item = FindOrCreateCard(state, card, now);
                item.LastAction = "تم إنشاء الحساب";
                item.UpdatedAt = now;
            }

            AddAction(state, batchId, systemKey, "CardBatchCreated", "إنشاء دفعة",
                batchCards.Count, "تم إنشاء الدفعة وحساباتها ضمن سجل Nexora.", now);
        });
    }

    public void RecordRouterUpload(IEnumerable<GeneratedDemoCard> cards)
    {
        var selected = cards.DistinctBy(card => (card.BatchId, card.CardNumber)).ToList();
        if (selected.Count == 0)
            throw new InvalidOperationException("حدد كرتًا واحدًا على الأقل لتسجيل طلب المزامنة.");

        Update(state =>
        {
            var now = DateTimeOffset.Now;
            foreach (var group in selected.GroupBy(card => new { card.BatchId, SystemKey = NormalizeSystemKey(card.TargetSystem) }))
            {
                var batch = FindOrCreateBatch(state, group.Key.BatchId, group.Key.SystemKey, group.Count(), now);
                foreach (var card in group)
                {
                    var workflowCard = FindOrCreateCard(state, card, now);
                    workflowCard.RouterUploadSimulated = true;
                    workflowCard.LastAction = "طلب مزامنة";
                    workflowCard.UpdatedAt = now;
                }
                batch.RouterUploadedCount = state.Cards.Count(card => card.BatchId == group.Key.BatchId && card.RouterUploadSimulated);
                batch.LastAction = "طلب مزامنة";
                batch.UpdatedAt = now;
                AddAction(state, group.Key.BatchId, group.Key.SystemKey, "SyncRequested", "طلب مزامنة",
                    group.Count(), "تم تسجيل طلب المزامنة للعناصر المحددة.", now);
            }
        });
    }

    public void RecordOnlineSalePreparation(IEnumerable<GeneratedDemoCard> cards)
    {
        var selected = cards.DistinctBy(card => (card.BatchId, card.CardNumber)).ToList();
        if (selected.Count == 0)
            throw new InvalidOperationException("حدد كرتًا واحدًا على الأقل قبل تجهيز البيع أونلاين.");

        Update(state =>
        {
            var now = DateTimeOffset.Now;
            foreach (var group in selected.GroupBy(card => new { card.BatchId, SystemKey = NormalizeSystemKey(card.TargetSystem) }))
            {
                var batch = FindOrCreateBatch(state, group.Key.BatchId, group.Key.SystemKey, group.Count(), now);
                foreach (var card in group)
                {
                    var workflowCard = FindOrCreateCard(state, card, now);
                    workflowCard.OnlineSalePrepared = true;
                    workflowCard.LastAction = "جاهز للبيع الإلكتروني";
                    workflowCard.UpdatedAt = now;
                }
                batch.OnlineSalePreparedCount = state.Cards.Count(card => card.BatchId == group.Key.BatchId && card.OnlineSalePrepared);
                batch.LastAction = "تجهيز للبيع الإلكتروني";
                batch.UpdatedAt = now;
                AddAction(state, group.Key.BatchId, group.Key.SystemKey, "OnlineSalePrepared", "تجهيز للبيع الإلكتروني",
                    group.Count(), "تم تسجيل تجهيز الحسابات لمسار البيع الإلكتروني.", now);
            }
        });
    }

    public void QueuePrintSelection(IEnumerable<GeneratedDemoCard> cards)
    {
        var selected = cards.DistinctBy(card => (card.BatchId, card.CardNumber, card.TargetSystem)).ToList();
        if (selected.Count == 0)
            throw new InvalidOperationException("لا توجد كروت لإضافتها إلى قائمة الطباعة.");

        Update(state =>
        {
            var now = DateTimeOffset.Now;
            var newlyQueued = new List<GeneratedDemoCard>();
            foreach (var card in selected)
            {
                if (!state.PendingPrintCards.Any(pending =>
                    string.Equals(pending.BatchId, card.BatchId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(pending.CardNumber, card.CardNumber, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(pending.TargetSystem, NormalizeSystemKey(card.TargetSystem), StringComparison.OrdinalIgnoreCase)))
                {
                    state.PendingPrintCards.Add(new CardWorkflowSelection
                    {
                        BatchId = card.BatchId,
                        CardNumber = card.CardNumber,
                        TargetSystem = NormalizeSystemKey(card.TargetSystem),
                        QueuedAt = now
                    });
                    newlyQueued.Add(card);
                }
            }
            foreach (var card in selected)
            {
                var item = FindOrCreateCard(state, card, now);
                item.PrintPrepared = true;
                item.LastAction = "أضيف إلى قائمة الطباعة";
                item.UpdatedAt = now;
            }
            foreach (var group in newlyQueued.GroupBy(card => new { card.BatchId, SystemKey = NormalizeSystemKey(card.TargetSystem) }))
                AddAction(state, group.Key.BatchId, group.Key.SystemKey, "PrintQueued", "إضافة إلى قائمة الطباعة",
                    group.Count(), "تمت إضافة الكروت إلى قائمة الطباعة.", now);
        });
    }

    public IReadOnlyList<CardWorkflowSelection> GetPendingPrintSelection()
    {
        lock (Gate)
            return _state.PendingPrintCards.Select(item => new CardWorkflowSelection
            {
                BatchId = item.BatchId,
                CardNumber = item.CardNumber,
                TargetSystem = item.TargetSystem,
                QueuedAt = item.QueuedAt
            }).ToList();
    }

    public void RecordPrintPreview(IEnumerable<GeneratedDemoCard> cards)
    {
        var selected = cards.DistinctBy(card => (card.BatchId, card.CardNumber, card.TargetSystem)).ToList();
        if (selected.Count == 0)
            return;

        Update(state =>
        {
            var now = DateTimeOffset.Now;
            foreach (var group in selected.GroupBy(card => new { card.BatchId, SystemKey = NormalizeSystemKey(card.TargetSystem) }))
            {
                foreach (var card in group)
                {
                    var item = FindOrCreateCard(state, card, now);
                    item.PrintPrepared = true;
                    item.LastAction = "تمت مراجعة معاينة الطباعة";
                    item.UpdatedAt = now;
                }
                AddAction(state, group.Key.BatchId, group.Key.SystemKey, "PrintPreviewReviewed", "مراجعة معاينة الطباعة",
                    group.Count(), "تمت مراجعة معاينة الطباعة للعناصر المحددة.", now);
            }
        });
    }

    public void RecordPrintSubmitted(IEnumerable<GeneratedDemoCard> cards, string printerName)
    {
        var selected = cards.DistinctBy(card => (card.BatchId, card.CardNumber, card.TargetSystem)).ToList();
        if (selected.Count == 0)
            throw new InvalidOperationException("حدد كرتًا واحدًا على الأقل للطباعة.");

        Update(state =>
        {
            var now = DateTimeOffset.Now;
            foreach (var group in selected.GroupBy(card => new { card.BatchId, SystemKey = NormalizeSystemKey(card.TargetSystem) }))
            {
                foreach (var card in group)
                {
                    var workflowCard = FindOrCreateCard(state, card, now);
                    workflowCard.PrintPrepared = false;
                    workflowCard.LastAction = "أُرسل إلى الطابعة";
                    workflowCard.UpdatedAt = now;
                }

                AddAction(state, group.Key.BatchId, group.Key.SystemKey, "PrintSubmitted", "إرسال إلى الطابعة",
                    group.Count(), $"الطابعة: {printerName} · عدد الكروت: {group.Count():N0}.", now);
            }

            state.PendingPrintCards.RemoveAll(pending => selected.Any(card =>
                string.Equals(pending.BatchId, card.BatchId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(pending.CardNumber, card.CardNumber, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(pending.TargetSystem, NormalizeSystemKey(card.TargetSystem), StringComparison.OrdinalIgnoreCase)));
        });
    }

    public string GetCardStatusLabel(string batchId, string cardNumber)
    {
        lock (Gate)
        {
            var card = _state.Cards.LastOrDefault(item =>
                string.Equals(item.BatchId, batchId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.CardNumber, cardNumber, StringComparison.OrdinalIgnoreCase));
            if (card is null)
                return "لا يوجد سجل إجراءات";
            if (card.OnlineSalePrepared)
                return "جاهز للبيع الإلكتروني";
            if (card.RouterUploadSimulated && card.PrintPrepared)
                return "طلب مزامنة مسجل — بانتظار الطباعة";
            if (card.RouterUploadSimulated)
                return "طلب مزامنة مسجل";
            if (card.PrintPrepared)
                return "في قائمة الطباعة";
            return card.LastAction;
        }
    }

    public void SetPreferredBatchFilter(string systemKey, string batchId)
    {
        if (string.IsNullOrWhiteSpace(batchId))
            return;
        Update(state => state.PreferredBatchBySystem[NormalizeSystemKey(systemKey)] = batchId.Trim());
    }

    public string? GetPreferredBatchFilter(string systemKey)
    {
        lock (Gate)
            return _state.PreferredBatchBySystem.TryGetValue(NormalizeSystemKey(systemKey), out var value) ? value : null;
    }

    public IReadOnlyList<string> GetOptions(string systemKey, string optionKind)
    {
        lock (Gate)
        {
            var normalized = NormalizeSystemKey(systemKey);
            var source = optionKind.Equals("template", StringComparison.OrdinalIgnoreCase)
                ? _state.TemplatesBySystem
                : _state.PointsOfSaleBySystem;
            return source.TryGetValue(normalized, out var values) ? values.ToList() : Array.Empty<string>();
        }
    }

    public IReadOnlyDictionary<string, string> GetIssuancePreferences(string systemKey)
    {
        lock (Gate)
        {
            var normalized = NormalizeSystemKey(systemKey);
            return _state.IssuancePreferencesBySystem.TryGetValue(normalized, out var values)
                ? new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void SaveIssuancePreferences(string systemKey, IReadOnlyDictionary<string, string> preferences)
    {
        if (preferences is null)
            throw new ArgumentNullException(nameof(preferences));

        Update(state =>
        {
            state.IssuancePreferencesBySystem[NormalizeSystemKey(systemKey)] = preferences
                .Where(item => !string.IsNullOrWhiteSpace(item.Key))
                .ToDictionary(item => item.Key, item => item.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);
        });
    }

    public void AddOption(string systemKey, string optionKind, string value)
    {
        var cleanValue = (value ?? string.Empty).Trim();
        if (cleanValue.Length == 0)
            throw new ArgumentException("اكتب اسمًا قبل الحفظ.", nameof(value));

        Update(state =>
        {
            var normalized = NormalizeSystemKey(systemKey);
            var destination = optionKind.Equals("template", StringComparison.OrdinalIgnoreCase)
                ? state.TemplatesBySystem
                : state.PointsOfSaleBySystem;
            if (!destination.TryGetValue(normalized, out var items))
            {
                items = [];
                destination[normalized] = items;
            }
            if (!items.Contains(cleanValue, StringComparer.OrdinalIgnoreCase))
                items.Add(cleanValue);
        });
    }

    private static WorkflowBatchState FindOrCreateBatch(WorkflowSnapshot state, string batchId, string systemKey,
        int totalCount, DateTimeOffset now)
    {
        var batch = state.Batches.FirstOrDefault(item =>
            string.Equals(item.BatchId, batchId, StringComparison.OrdinalIgnoreCase));
        if (batch is not null)
        {
            batch.TargetSystem = NormalizeSystemKey(systemKey);
            batch.TotalCardCount = Math.Max(batch.TotalCardCount, totalCount);
            return batch;
        }
        batch = new WorkflowBatchState
        {
            BatchId = batchId,
            TargetSystem = NormalizeSystemKey(systemKey),
            TotalCardCount = totalCount,
            LastAction = "تم الإنشاء",
            UpdatedAt = now
        };
        state.Batches.Insert(0, batch);
        return batch;
    }

    private static WorkflowCardState FindOrCreateCard(WorkflowSnapshot state, GeneratedDemoCard card, DateTimeOffset now)
    {
        var existing = state.Cards.FirstOrDefault(item =>
            string.Equals(item.BatchId, card.BatchId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.CardNumber, card.CardNumber, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(item.TargetSystem, NormalizeSystemKey(card.TargetSystem), StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return existing;

        existing = new WorkflowCardState
        {
            BatchId = card.BatchId,
            CardNumber = card.CardNumber,
            TargetSystem = NormalizeSystemKey(card.TargetSystem),
            LastAction = "تم الإنشاء",
            UpdatedAt = now
        };
        state.Cards.Add(existing);
        return existing;
    }

    private static void AddAction(WorkflowSnapshot state, string batchId, string systemKey, string actionType,
        string actionLabel, int count, string description, DateTimeOffset now)
    {
        state.Actions.Insert(0, new WorkflowActionState
        {
            Id = Guid.NewGuid().ToString("N"),
            BatchId = batchId,
            TargetSystem = NormalizeSystemKey(systemKey),
            ActionType = actionType,
            ActionLabel = actionLabel,
            Count = count,
            Description = description,
            CreatedAt = now
        });
        if (state.Actions.Count > 500)
            state.Actions.RemoveRange(500, state.Actions.Count - 500);
    }

    private static string NormalizeSystemKey(string value)
    {
        var normalized = (value ?? string.Empty).Trim();
        return normalized.Equals("hotspot", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("هوتسبوت", StringComparison.OrdinalIgnoreCase)
            ? "hotspot"
            : "user-manager";
    }

    private static void Update(Action<WorkflowSnapshot> change)
    {
        lock (Gate)
        {
            if (_writesBlocked)
                throw new InvalidOperationException(_storageWarning ?? "تعذر حفظ سجل إجراءات الكروت.");

            var next = JsonSerializer.Deserialize<WorkflowSnapshot>(JsonSerializer.Serialize(_state, JsonOptions), JsonOptions)
                       ?? new WorkflowSnapshot();
            change(next);
            SaveSnapshot(next);
            _state = next;
            _storageWarning = null;
        }
    }

    private static WorkflowSnapshot LoadSnapshot()
    {
        if (!File.Exists(StorePath))
            return new WorkflowSnapshot();

        try
        {
            var json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<WorkflowSnapshot>(json, JsonOptions) ?? new WorkflowSnapshot();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            try
            {
                var backup = StorePath + ".corrupt-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json";
                File.Copy(StorePath, backup, overwrite: false);
                _storageWarning = "ملف سجل الإجراءات غير صالح؛ حُفظت نسخة احتياطية باسم " + Path.GetFileName(backup) + ". سيتم إنشاء سجل إجراءات جديد عند أول تعديل.";
                return new WorkflowSnapshot();
            }
            catch (Exception backupException) when (backupException is IOException or UnauthorizedAccessException)
            {
                _writesBlocked = true;
                _storageWarning = "تعذر قراءة ملف سجل الإجراءات أو حفظ نسخة احتياطية له. لم يتم استبداله: " + ex.Message;
                return new WorkflowSnapshot();
            }
        }
    }

    private static void SaveSnapshot(WorkflowSnapshot state)
    {
        var directory = Path.GetDirectoryName(StorePath)!;
        Directory.CreateDirectory(directory);
        var tempPath = StorePath + ".tmp";
        var json = JsonSerializer.Serialize(state, JsonOptions);
        File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(tempPath, StorePath, overwrite: true);
    }

    private sealed class WorkflowSnapshot
    {
        public List<WorkflowBatchState> Batches { get; set; } = [];
        public List<WorkflowCardState> Cards { get; set; } = [];
        public List<WorkflowActionState> Actions { get; set; } = [];
        public List<CardWorkflowSelection> PendingPrintCards { get; set; } = [];
        public Dictionary<string, string> PreferredBatchBySystem { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<string>> TemplatesBySystem { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<string>> PointsOfSaleBySystem { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Dictionary<string, string>> IssuancePreferencesBySystem { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class WorkflowBatchState
    {
        public string BatchId { get; set; } = string.Empty;
        public string TargetSystem { get; set; } = "user-manager";
        public int TotalCardCount { get; set; }
        public int RouterUploadedCount { get; set; }
        public int OnlineSalePreparedCount { get; set; }
        public string LastAction { get; set; } = "تم الإنشاء";
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    }

    private sealed class WorkflowCardState
    {
        public string BatchId { get; set; } = string.Empty;
        public string CardNumber { get; set; } = string.Empty;
        public string TargetSystem { get; set; } = "user-manager";
        public bool RouterUploadSimulated { get; set; }
        public bool OnlineSalePrepared { get; set; }
        public bool PrintPrepared { get; set; }
        public string LastAction { get; set; } = "تم الإنشاء";
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    }

    private sealed class WorkflowActionState
    {
        public string Id { get; set; } = string.Empty;
        public string BatchId { get; set; } = string.Empty;
        public string TargetSystem { get; set; } = "user-manager";
        public string ActionType { get; set; } = string.Empty;
        public string ActionLabel { get; set; } = string.Empty;
        public int Count { get; set; }
        public string Description { get; set; } = string.Empty;
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    }

    public sealed class CardWorkflowSelection
    {
        public string BatchId { get; set; } = string.Empty;
        public string CardNumber { get; set; } = string.Empty;
        public string TargetSystem { get; set; } = "user-manager";
        public DateTimeOffset QueuedAt { get; set; } = DateTimeOffset.Now;
    }
}

public sealed record DemoWorkflowActionRecord(
    string Id,
    string BatchId,
    string TargetSystem,
    string ActionType,
    string ActionLabel,
    int Count,
    string Description,
    DateTimeOffset CreatedAt)
{
    public string CreatedAtLabel => CreatedAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
}
