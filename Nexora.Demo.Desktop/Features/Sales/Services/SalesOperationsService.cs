using System.IO;
using System.Text;
using System.Text.Json;
using Nexora.Demo.Desktop.Features.Sales.Models;

namespace Nexora.Demo.Desktop.Features.Sales.Services;

/// <summary>
/// Durable local operating ledger for the sales feature. This service intentionally does not
/// pretend to synchronize with the company portal, payment wallets, or a production accounting API.
/// </summary>
public sealed class SalesOperationsService
{
    private const int CurrentSchemaVersion = 2;
    private const decimal WalletCommissionPercent = 10m;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _gate = new();
    private readonly string _storePath;
    private List<SalesOperationRecord> _operations = [];
    private List<SalesStockTransferRecord> _transfers = [];
    private List<SalesAccountSettlementRecord> _settlements = [];
    private List<SalesExpenseRecord> _expenses = [];
    private bool _writesBlocked;
    private string? _storageWarning;
    private long _referenceSequence;

    public SalesOperationsService()
    {
        _storePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "NexoraProductDemo", "sales-operations-v1.json");

        if (File.Exists(_storePath))
        {
            LoadExisting();
            return;
        }

        try
        {
            Persist(new SalesOperationsSnapshot(CurrentSchemaVersion, [], [], [], []));
        }
        catch (Exception ex) when (IsStorageException(ex))
        {
            _writesBlocked = true;
            _storageWarning = "تعذر إنشاء سجل عمليات المبيعات المحلي. تم تعطيل الكتابة لحماية البيانات. " + ex.Message;
        }
    }

    public string StorePath => _storePath;
    public bool CanWrite => !_writesBlocked;
    public string? StorageWarning => _storageWarning;
    public event EventHandler? Changed;

    public IReadOnlyList<SalesOperationRecord> GetOperations()
    {
        lock (_gate) return _operations.OrderByDescending(item => item.OccurredAt).ToArray();
    }

    public IReadOnlyList<SalesStockTransferRecord> GetTransfers()
    {
        lock (_gate) return _transfers.OrderByDescending(item => item.OccurredAt).ToArray();
    }

    public IReadOnlyList<SalesAccountSettlementRecord> GetSettlements()
    {
        lock (_gate) return _settlements.OrderByDescending(item => item.OccurredAt).ToArray();
    }

    public IReadOnlyList<SalesExpenseRecord> GetExpenses()
    {
        lock (_gate) return _expenses.OrderByDescending(item => item.OccurredAt).ToArray();
    }

    public decimal TotalTransferBalance
    {
        get
        {
            lock (_gate)
                return Math.Max(0m, _transfers.Sum(item => item.NetAmount)
                    - _transfers.Sum(item => item.PaidAtTransfer)
                    - _settlements.Sum(item => item.Amount));
        }
    }

    public decimal CentralCollectionExpected
    {
        get
        {
            lock (_gate)
                return _operations.Where(item => item.CollectionStatus == "ضمن تجميع الشركة")
                    .Sum(item => item.OutstandingAmount);
        }
    }

    public int GetTransferredQuantity(string batchId)
    {
        lock (_gate)
            return _transfers.Where(item => string.Equals(item.BatchId, batchId, StringComparison.OrdinalIgnoreCase))
                .Sum(item => item.Quantity);
    }

    public int GetAvailableBatchQuantity(string batchId, int sourceBatchQuantity)
    {
        lock (_gate)
        {
            var transferred = _transfers.Where(item => string.Equals(item.BatchId, batchId, StringComparison.OrdinalIgnoreCase))
                .Sum(item => item.Quantity);
            var sold = _operations.Where(item => string.Equals(item.SourceBatchId, batchId, StringComparison.OrdinalIgnoreCase))
                .Sum(item => item.Quantity);
            return Math.Max(0, sourceBatchQuantity - transferred - sold);
        }
    }

    public SalesOperationRecord RecordOperation(SalesOperationDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return RecordOperations(new[] { draft })[0];
    }

    /// <summary>
    /// Records a multi-line sale in one durable snapshot. Every line is validated before any
    /// changes are committed, so a cart is never partially saved if one line is invalid.
    /// </summary>
    public IReadOnlyList<SalesOperationRecord> RecordOperations(IReadOnlyList<SalesOperationDraft> drafts)
    {
        ArgumentNullException.ThrowIfNull(drafts);
        if (drafts.Count == 0)
            throw new ArgumentException("أضف صنفًا واحدًا على الأقل قبل تسجيل البيع.", nameof(drafts));

        var savedRecords = new List<SalesOperationRecord>(drafts.Count);
        lock (_gate)
        {
            EnsureWritable();
            var nextOperations = new List<SalesOperationRecord>(_operations);
            var nextExpenses = new List<SalesExpenseRecord>(_expenses);
            var requestedByBatch = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var sourceQuantityByBatch = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var draft in drafts)
            {
                ArgumentNullException.ThrowIfNull(draft);
                var channel = Required(draft.ChannelKey, "قناة البيع", 2, 40);
                if (channel is not ("direct" or "network-cards" or "wallet"))
                    throw new ArgumentException("قناة البيع غير معروفة.");

                var channelLabel = Required(draft.ChannelLabel, "اسم قناة البيع", 2, 80);
                var pointId = Required(draft.PointId, "نقطة البيع", 1, 80);
                var pointName = Required(draft.PointName, "اسم نقطة البيع", 2, 100);
                var packageName = Required(draft.PackageName, "اسم الباقة", 2, 100);
                var requestedCollectionMode = Required(draft.CollectionMode, "حالة التحصيل", 2, 40);
                var paymentMethod = Required(draft.PaymentMethod, "وسيلة الدفع", 2, 60);
                var expectedPointId = channel switch
                {
                    "direct" => "system-direct-sales",
                    "network-cards" => "system-online-network-cards",
                    "wallet" => "system-electronic-wallets",
                    _ => string.Empty
                };
                var expectedPointName = channel switch
                {
                    "direct" => "البيع المباشر",
                    "network-cards" => "كروت الشبكات",
                    "wallet" => "المحافظ الإلكترونية",
                    _ => string.Empty
                };
                if (!string.Equals(pointId, expectedPointId, StringComparison.Ordinal)
                    || !string.Equals(pointName, expectedPointName, StringComparison.Ordinal))
                    throw new InvalidOperationException("نقطة البيع لا تطابق قناة البيع المحددة. حدّث شاشة تسجيل البيع ثم أعد المحاولة.");
                if (channel == "direct" && requestedCollectionMode != "paid")
                    throw new InvalidOperationException("البيع المباشر يُسجّل محصلًا بالكامل فقط في هذا الإصدار؛ البيع الآجل وتسوياته يُداران عبر حسابات نقاط البيع بعد توريد الكروت للبقالة.");
                if (channel == "direct" && paymentMethod is not ("نقدي" or "تحويل بنكي"))
                    throw new InvalidOperationException("استخدم قناة «محافظ إلكترونية» لعمليات المحافظ حتى تُحتسب عمولتها الثابتة 10٪ بصورة صحيحة.");
                if (channel == "wallet" && paymentMethod != "محفظة إلكترونية")
                    throw new InvalidOperationException("وسيلة تحصيل قناة المحافظ الإلكترونية يجب أن تكون محفظة إلكترونية.");
                if (channel == "network-cards" && paymentMethod != "كروت الشبكات / تجميع مركزي")
                    throw new InvalidOperationException("قناة «كروت الشبكات» تُسجّل ضمن التجميع المركزي.");

                var batchId = Required(draft.SourceBatchId, "دفعة الكروت المصدر", 2, 100);
                var quantity = draft.Quantity;
                if (quantity < 1 || quantity > 100_000)
                    throw new ArgumentOutOfRangeException(nameof(drafts), "الكمية يجب أن تكون بين 1 و100,000 كرت.");
                if (draft.UnitPrice <= 0m || draft.UnitPrice > 100_000_000m)
                    throw new ArgumentOutOfRangeException(nameof(drafts), "سعر الكرت يجب أن يكون أكبر من صفر وأقل من الحد المسموح.");
                if (draft.PrintingCostPerCard < 0m || draft.PrintingCostPerCard > draft.UnitPrice)
                    throw new ArgumentOutOfRangeException(nameof(drafts), "تكلفة طباعة الكرت يجب أن تكون صفرًا أو قيمة موجبة لا تتجاوز سعر الكرت.");
                if (draft.SourceBatchQuantity < 1)
                    throw new ArgumentOutOfRangeException(nameof(drafts), "الدفعة المصدر لا تحتوي كمية صالحة للبيع.");

                if (sourceQuantityByBatch.TryGetValue(batchId, out var priorSourceQuantity)
                    && priorSourceQuantity != draft.SourceBatchQuantity)
                    throw new InvalidOperationException($"بيانات كمية المصدر للدفعة {batchId} غير متطابقة بين أصناف السلة.");
                sourceQuantityByBatch[batchId] = draft.SourceBatchQuantity;
                var alreadyRequested = requestedByBatch.TryGetValue(batchId, out var priorRequested) ? priorRequested : 0;
                var available = Math.Max(0, GetAvailableBatchQuantity(batchId, draft.SourceBatchQuantity) - alreadyRequested);
                if (quantity > available)
                    throw new InvalidOperationException($"المتاح من الدفعة {batchId} هو {available:N0} كرت فقط بعد احتساب المبيعات والتوريدات السابقة وأصناف السلة.");
                requestedByBatch[batchId] = checked(alreadyRequested + quantity);

                if (channel == "direct" && string.Equals(requestedCollectionMode, "central", StringComparison.Ordinal))
                    throw new ArgumentException("التجميع المركزي مخصص للقنوات الإلكترونية؛ استخدم «محصل بالكامل» للبيع المباشر.");

                var gross = checked(quantity * draft.UnitPrice);
                var discount = draft.DiscountAmount;
                if (discount < 0m || discount > gross)
                    throw new ArgumentOutOfRangeException(nameof(drafts), "خصم البند يجب أن يكون بين صفر وإجمالي قيمة البند.");
                var discountedGross = gross - discount;
                var commissionPercent = channel == "wallet" ? WalletCommissionPercent : 0m;
                var commission = Math.Round(discountedGross * commissionPercent / 100m, 2, MidpointRounding.AwayFromZero);
                var networkAmount = discountedGross - commission;
                var requiresPrinting = channel == "direct";
                var printingExpense = requiresPrinting ? checked(quantity * draft.PrintingCostPerCard) : 0m;
                var collectionMode = channel is "wallet" or "network-cards" ? "central" : requestedCollectionMode;
                var received = collectionMode switch
                {
                    "paid" => networkAmount,
                    "partial" => draft.AmountReceived,
                    "credit" or "central" => 0m,
                    _ => throw new ArgumentException("اختر حالة التحصيل.")
                };
                if (received < 0m || received > networkAmount)
                    throw new ArgumentOutOfRangeException(nameof(drafts), "المبلغ المحصل يجب ألا يكون أقل من صفر أو أكبر من صافي المستحق.");
                if (collectionMode == "paid" && draft.AmountReceived > 0m && draft.AmountReceived != networkAmount)
                    throw new ArgumentException("لأن الحالة محصل بالكامل، فإن المبلغ المحصل يساوي صافي المستحق.");

                var status = collectionMode switch
                {
                    "paid" => "محصل بالكامل",
                    "partial" when received > 0m => "محصل جزئيًا",
                    "partial" or "credit" => "آجل",
                    "central" => "ضمن تجميع الشركة",
                    _ => "غير محدد"
                };
                if (channel is "wallet" or "network-cards")
                    status = "ضمن تجميع الشركة";

                var saved = new SalesOperationRecord(
                    Guid.NewGuid().ToString("N"), NextReference("SAL"), DateTimeOffset.Now,
                    channel, channelLabel, pointId, pointName, packageName, quantity, draft.UnitPrice,
                    gross, commissionPercent, commission, networkAmount, received,
                    Math.Max(0m, networkAmount - received), paymentMethod, status,
                    requiresPrinting, printingExpense,
                    Optional(draft.ExternalReference, 100), Optional(draft.Notes, 500), batchId, discount);

                savedRecords.Add(saved);
                nextOperations.Add(saved);
                if (printingExpense > 0m)
                {
                    nextExpenses.Add(new SalesExpenseRecord(
                        Guid.NewGuid().ToString("N"), NextReference("EXP"), DateTimeOffset.Now,
                        "طباعة وخدش", $"تكلفة تجهيز {quantity:N0} كرتًا · {packageName}",
                        printingExpense, saved.Reference, "سُجل تلقائيًا مع عملية البيع."));
                }
            }

            Commit(nextOperations, _transfers, _settlements, nextExpenses);
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return savedRecords.ToArray();
    }

    public SalesStockTransferRecord RecordStockTransfer(SalesStockTransferDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        SalesStockTransferRecord saved;
        lock (_gate)
        {
            EnsureWritable();
            var batchId = Required(draft.BatchId, "رقم الدفعة", 2, 100);
            var pointId = Required(draft.PointId, "نقطة البيع", 1, 80);
            var pointName = Required(draft.PointName, "اسم نقطة البيع", 2, 100);
            var packageName = Required(draft.PackageName, "اسم الباقة", 2, 100);
            if (draft.BatchQuantity < 1 || draft.Quantity < 1 || draft.Quantity > draft.BatchQuantity)
                throw new ArgumentOutOfRangeException(nameof(draft), "كمية التسليم غير صالحة.");
            if (draft.CommissionPercent < 0m || draft.CommissionPercent > 100m)
                throw new ArgumentOutOfRangeException(nameof(draft), "نسبة العمولة يجب أن تكون بين 0 و100٪.");
            if (draft.UnitPrice <= 0m || draft.UnitPrice > 100_000_000m)
                throw new ArgumentOutOfRangeException(nameof(draft), "سعر الكرت غير صالح.");
            if (draft.PrintingCostPerCard < 0m || draft.PrintingCostPerCard > draft.UnitPrice)
                throw new ArgumentOutOfRangeException(nameof(draft), "تكلفة الطباعة والخدش لكل كرت غير صالحة.");

            var alreadyTransferred = _transfers
                .Where(item => string.Equals(item.BatchId, batchId, StringComparison.OrdinalIgnoreCase))
                .Sum(item => item.Quantity);
            var alreadySold = _operations.Where(item => string.Equals(item.SourceBatchId, batchId, StringComparison.OrdinalIgnoreCase))
                .Sum(item => item.Quantity);
            var available = draft.BatchQuantity - alreadyTransferred - alreadySold;
            if (draft.Quantity > available)
                throw new InvalidOperationException($"الكمية المتاحة من الدفعة {batchId} هي {Math.Max(0, available):N0} كرت فقط بعد احتساب المبيعات والتوريدات السابقة؛ لا يمكن تسليم كمية تتجاوز المتاح.");

            var gross = checked(draft.Quantity * draft.UnitPrice);
            var commission = Math.Round(gross * draft.CommissionPercent / 100m, 2, MidpointRounding.AwayFromZero);
            var net = gross - commission;
            var paid = draft.PaidAtTransfer;
            if (paid < 0m || paid > net)
                throw new ArgumentOutOfRangeException(nameof(draft), "المبلغ المدفوع يجب أن يكون بين صفر وصافي المستحق.");
            if (!draft.PointAllowsCredit && paid < net)
                throw new InvalidOperationException("سياسة نقطة البيع لا تسمح بالآجل. سجّل صافي المستحق كاملًا كمبلغ مدفوع أو عدّل سياسة النقطة أولًا.");

            var printingExpense = checked(draft.Quantity * draft.PrintingCostPerCard);
            var policy = paid >= net ? "فوري" : paid > 0m ? "جزئي/آجل" : "آجل";
            saved = new SalesStockTransferRecord(
                Guid.NewGuid().ToString("N"), NextReference("TRF"), DateTimeOffset.Now,
                batchId, Required(draft.TargetSystem, "نظام الدفعة", 2, 100), packageName,
                pointId, pointName, draft.Quantity, draft.UnitPrice, gross,
                draft.CommissionPercent, commission, net, paid, printingExpense, policy,
                Optional(draft.Notes, 500));

            var nextTransfers = new List<SalesStockTransferRecord>(_transfers) { saved };
            var nextExpenses = _expenses;
            if (printingExpense > 0m)
            {
                var expense = new SalesExpenseRecord(
                    Guid.NewGuid().ToString("N"), NextReference("EXP"), DateTimeOffset.Now,
                    "طباعة وخدش", $"تجهيز دفعة {batchId} لنقطة {pointName}",
                    printingExpense, saved.Reference, "سُجل تلقائيًا مع تسليم الدفعة.");
                nextExpenses = new List<SalesExpenseRecord>(_expenses) { expense };
            }

            Commit(_operations, nextTransfers, _settlements, nextExpenses);
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return saved;
    }

    public SalesAccountSettlementRecord RecordSettlement(SalesSettlementDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        SalesAccountSettlementRecord saved;
        lock (_gate)
        {
            EnsureWritable();
            var pointId = Required(draft.PointId, "نقطة البيع", 1, 80);
            var pointName = Required(draft.PointName, "اسم نقطة البيع", 2, 100);
            if (draft.Amount <= 0m || draft.Amount > 100_000_000_000m)
                throw new ArgumentOutOfRangeException(nameof(draft), "أدخل مبلغ تسوية أكبر من صفر.");

            var balance = CalculatePointBalance(pointId, _transfers, _settlements);
            if (balance <= 0m)
                throw new InvalidOperationException("لا يوجد رصيد مستحق على نقطة البيع المحددة.");
            if (draft.Amount > balance)
                throw new InvalidOperationException($"المبلغ أكبر من الرصيد المستحق ({balance:N0} ريال). لا يمكن تسجيل تحصيل يتجاوز الدين.");

            saved = new SalesAccountSettlementRecord(
                Guid.NewGuid().ToString("N"), NextReference("SET"), DateTimeOffset.Now,
                pointId, pointName, draft.Amount,
                Required(draft.PaymentMethod, "وسيلة التحصيل", 2, 60),
                Optional(draft.ExternalReference, 100), Optional(draft.Notes, 500));
            Commit(_operations, _transfers, new List<SalesAccountSettlementRecord>(_settlements) { saved }, _expenses);
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return saved;
    }

    public SalesExpenseRecord RecordExpense(SalesExpenseDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        SalesExpenseRecord saved;
        lock (_gate)
        {
            EnsureWritable();
            if (draft.Amount <= 0m || draft.Amount > 100_000_000_000m)
                throw new ArgumentOutOfRangeException(nameof(draft), "قيمة المصروف يجب أن تكون أكبر من صفر.");
            var category = Required(draft.Category, "تصنيف المصروف", 2, 80);
            if (string.Equals(category, "طباعة وخدش", StringComparison.Ordinal))
                throw new InvalidOperationException("تُسجّل تكلفة الطباعة والخدش تلقائيًا مع حركة البيع أو توريد الدفعة؛ سجّل هنا أي مستلزمات إضافية فقط لتجنب احتساب التكلفة مرتين.");
            saved = new SalesExpenseRecord(
                Guid.NewGuid().ToString("N"), NextReference("EXP"), DateTimeOffset.Now,
                category,
                Required(draft.Description, "وصف المصروف", 2, 180), draft.Amount,
                Optional(draft.RelatedReference, 100), Optional(draft.Notes, 500));
            Commit(_operations, _transfers, _settlements, new List<SalesExpenseRecord>(_expenses) { saved });
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return saved;
    }

    public decimal GetPointBalance(string pointId)
    {
        lock (_gate) return CalculatePointBalance(pointId, _transfers, _settlements);
    }

    public IReadOnlyList<SalesPointAccountRow> GetPointAccounts(IEnumerable<SalesPointRecord> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        lock (_gate)
        {
            return points.Where(point => !point.IsSystemPoint)
                .Select(point =>
                {
                    var pointTransfers = _transfers.Where(item => string.Equals(item.PointId, point.Id, StringComparison.OrdinalIgnoreCase)).ToArray();
                    var pointSettlements = _settlements.Where(item => string.Equals(item.PointId, point.Id, StringComparison.OrdinalIgnoreCase)).ToArray();
                    var movementTimes = pointTransfers.Select(item => item.OccurredAt)
                        .Concat(pointSettlements.Select(item => item.OccurredAt)).ToArray();
                    DateTimeOffset? lastMovement = movementTimes.Length == 0 ? null : movementTimes.Max();
                    var netDue = pointTransfers.Sum(item => item.NetAmount);
                    var paidAtTransfer = pointTransfers.Sum(item => item.PaidAtTransfer);
                    var settlements = pointSettlements.Sum(item => item.Amount);
                    return new SalesPointAccountRow(
                        point.Id, point.Name, point.PointType, point.CommissionPercent, point.AllowCredit,
                        pointTransfers.Length, pointTransfers.Sum(item => item.Quantity),
                        pointTransfers.Sum(item => item.GrossAmount), pointTransfers.Sum(item => item.CommissionAmount),
                        netDue, paidAtTransfer, settlements,
                        Math.Max(0m, netDue - paidAtTransfer - settlements), lastMovement, point.ContactName);
                })
                .OrderByDescending(row => row.Balance)
                .ThenBy(row => row.PointName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
    }

    private void LoadExisting()
    {
        try
        {
            var json = File.ReadAllText(_storePath);
            var snapshot = JsonSerializer.Deserialize<SalesOperationsSnapshot>(json, JsonOptions)
                ?? throw new InvalidDataException("ملف عمليات المبيعات فارغ أو غير صالح.");
            if (snapshot.SchemaVersion is not (1 or CurrentSchemaVersion))
                throw new InvalidDataException($"إصدار ملف عمليات المبيعات ({snapshot.SchemaVersion}) غير مدعوم بهذا الإصدار.");
            if (snapshot.Operations is null || snapshot.Transfers is null || snapshot.Settlements is null || snapshot.Expenses is null)
                throw new InvalidDataException("أحد أقسام سجل عمليات المبيعات مفقود.");

            _operations = snapshot.Operations;
            _transfers = snapshot.Transfers;
            _settlements = snapshot.Settlements;
            _expenses = snapshot.Expenses;
            if (HasDuplicates(_operations.Select(item => item.Reference))
                || HasDuplicates(_transfers.Select(item => item.Reference))
                || HasDuplicates(_settlements.Select(item => item.Reference))
                || HasDuplicates(_expenses.Select(item => item.Reference)))
                throw new InvalidDataException("توجد أرقام مرجعية مكررة في السجل المالي.");
            _referenceSequence = _operations.Count + _transfers.Count + _settlements.Count + _expenses.Count;
            if (snapshot.SchemaVersion < CurrentSchemaVersion)
            {
                try
                {
                    Persist(new SalesOperationsSnapshot(CurrentSchemaVersion, _operations, _transfers, _settlements, _expenses));
                }
                catch (Exception ex) when (IsStorageException(ex))
                {
                    _writesBlocked = true;
                    _storageWarning = "تمت قراءة سجل المبيعات السابق، لكن تعذر ترقية بنيته. لم تُحذف البيانات، وأُوقفت الكتابة لحمايتها. " + ex.Message;
                }
            }
        }
        catch (Exception ex) when (IsStorageException(ex) || ex is InvalidDataException or NotSupportedException)
        {
            _writesBlocked = true;
            _storageWarning = "تعذرت قراءة سجل عمليات المبيعات. لم يتم استبدال الملف غير المقروء، وتم إيقاف الكتابة لحماية البيانات. " + ex.Message;
            _operations = [];
            _transfers = [];
            _settlements = [];
            _expenses = [];
        }
    }

    private void Commit(
        List<SalesOperationRecord> operations,
        List<SalesStockTransferRecord> transfers,
        List<SalesAccountSettlementRecord> settlements,
        List<SalesExpenseRecord> expenses)
    {
        var snapshot = new SalesOperationsSnapshot(CurrentSchemaVersion, operations, transfers, settlements, expenses);
        Persist(snapshot);
        _operations = operations;
        _transfers = transfers;
        _settlements = settlements;
        _expenses = expenses;
        _storageWarning = null;
    }

    private void Persist(SalesOperationsSnapshot snapshot)
    {
        var folder = Path.GetDirectoryName(_storePath)!;
        Directory.CreateDirectory(folder);
        var tempPath = _storePath + ".tmp";
        try
        {
            File.WriteAllText(tempPath, JsonSerializer.Serialize(snapshot, JsonOptions), new UTF8Encoding(false));
            File.Move(tempPath, _storePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* preserve original error */ }
            }
        }
    }

    private void EnsureWritable()
    {
        if (_writesBlocked)
            throw new InvalidOperationException(_storageWarning ?? "تم إيقاف الكتابة في سجل المبيعات لحماية البيانات.");
    }

    private string NextReference(string prefix)
    {
        _referenceSequence++;
        return $"{prefix}-{DateTime.Now:yyMMdd}-{_referenceSequence:000000}";
    }

    private static decimal CalculatePointBalance(
        string pointId,
        IReadOnlyCollection<SalesStockTransferRecord> transfers,
        IReadOnlyCollection<SalesAccountSettlementRecord> settlements)
    {
        var netDue = transfers.Where(item => string.Equals(item.PointId, pointId, StringComparison.OrdinalIgnoreCase)).Sum(item => item.NetAmount);
        var paidAtTransfer = transfers.Where(item => string.Equals(item.PointId, pointId, StringComparison.OrdinalIgnoreCase)).Sum(item => item.PaidAtTransfer);
        var settled = settlements.Where(item => string.Equals(item.PointId, pointId, StringComparison.OrdinalIgnoreCase)).Sum(item => item.Amount);
        return Math.Max(0m, netDue - paidAtTransfer - settled);
    }

    private static bool HasDuplicates(IEnumerable<string> values)
        => values.GroupBy(value => value, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1);

    private static bool IsStorageException(Exception ex)
        => ex is IOException or UnauthorizedAccessException or JsonException or System.Security.SecurityException;

    private static string Required(string? value, string field, int min, int max)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length < min) throw new ArgumentException($"{field} مطلوب، ويجب ألا يقل عن {min} أحرف.");
        if (text.Length > max) throw new ArgumentException($"{field} يجب ألا يتجاوز {max} حرفًا.");
        return text;
    }

    private static string Optional(string? value, int max)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length > max) throw new ArgumentException($"النص يجب ألا يتجاوز {max} حرفًا.");
        return text;
    }
}
