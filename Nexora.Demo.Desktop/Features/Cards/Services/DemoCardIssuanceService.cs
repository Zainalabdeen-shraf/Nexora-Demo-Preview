using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Nexora.Demo.Desktop.Features.Cards.Models;

namespace Nexora.Demo.Desktop.Features.Cards.Services;

/// <summary>
/// Local-only card issuance simulator. It persists the preview scenario to a dedicated
/// LocalAppData folder so cards, batches and the print queue share one durable source.
/// No RouterOS, production database, RADIUS service or physical printer is contacted.
/// </summary>
public sealed class DemoCardIssuanceService
{
    private const int MaxCardsPerOperation = 5000;
    private const string Digits = "0123456789";
    private const string Letters = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string AlphaNumeric = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NexoraProductDemo", "card-issuance-v1.json");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private int _batchSequence;
    private bool _storeReadFailed;

    public ObservableCollection<GeneratedDemoCard> Cards { get; } = [];
    public ObservableCollection<GeneratedCardBatch> Batches { get; } = [];
    public event EventHandler? ScenarioChanged;
    public string? StorageWarning { get; private set; }
    public string StorageDescription => _storeReadFailed
        ? "تعذر قراءة سجل الكروت؛ تم إيقاف التعديل حفاظًا على البيانات."
        : "سجل الكروت والدفعات جاهز.";

    public DemoCardIssuanceService()
    {
        if (File.Exists(StorePath))
        {
            try
            {
                var json = File.ReadAllText(StorePath);
                var state = JsonSerializer.Deserialize<DemoCardIssuanceStore>(json, JsonOptions)
                    ?? throw new InvalidDataException("ملف إصدار الكروت فارغ أو غير صالح.");
                if (state.Cards is null || state.Batches is null)
                    throw new InvalidDataException("ملف إصدار الكروت لا يحتوي على قوائم الكروت والدفعات المطلوبة.");

                foreach (var batch in state.Batches.OrderByDescending(item => item.CreatedAt))
                    Batches.Add(batch);
                foreach (var card in state.Cards.OrderBy(item => item.CreatedAt))
                    Cards.Add(card);
                _batchSequence = Batches.Count;
                return;
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException or UnauthorizedAccessException)
            {
                _storeReadFailed = true;
                StorageWarning = "تعذر قراءة سجل الكروت. لم يتم استبدال البيانات حفاظًا على سلامتها. " + ex.Message;
                return;
            }
        }

        try
        {
            SeedScenario();
            SaveSnapshot(Cards.ToList(), Batches.ToList());
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            _storeReadFailed = true;
            StorageWarning = "تعذر حفظ بيانات العرض الأولية على القرص؛ تم تعطيل إنشاء الدفعات حتى لا نعرض بيانات غير محفوظة على أنها محفوظة. " + ex.Message;
        }
    }

    public GeneratedCardBatch GenerateBatch(CardIssuanceRequest request, string? existingBatchId = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_storeReadFailed)
            throw new InvalidOperationException(StorageWarning ?? "تعذر قراءة سجل الكروت.");
        Validate(request);

        var targetName = request.TargetSystem == DemoAccountSystem.HotSpot
            ? "مستخدمو الهوتسبوت"
            : "مدير المستخدمين";

        GeneratedCardBatch? existingBatch = null;
        if (!string.IsNullOrWhiteSpace(existingBatchId))
        {
            existingBatch = Batches.FirstOrDefault(item =>
                string.Equals(item.BatchId, existingBatchId, StringComparison.OrdinalIgnoreCase));

            if (existingBatch is null)
                throw new InvalidOperationException("رقم الدفعة السابقة غير موجود في سجل الكروت.");

            if (!string.Equals(existingBatch.TargetSystem, targetName, StringComparison.Ordinal))
                throw new InvalidOperationException("لا يمكن إضافة الكروت إلى دفعة تابعة لنظام آخر.");

            if (!string.Equals(existingBatch.PackageName, request.Package.DisplayName, StringComparison.Ordinal) ||
                existingBatch.UnitPrice != request.Package.Price)
                throw new InvalidOperationException("للحفاظ على دقة السجل، اختر نفس الباقة والسعر الموجودين في الدفعة السابقة.");

            if (!string.Equals(existingBatch.RouterProfile, request.RouterProfile, StringComparison.Ordinal) ||
                !string.Equals(existingBatch.PointOfSale, request.PointOfSale, StringComparison.Ordinal) ||
                !string.Equals(existingBatch.ManagerAccount, request.ManagerAccount, StringComparison.Ordinal) ||
                existingBatch.SharedUsers != request.SharedUsers)
                throw new InvalidOperationException("الدفعة السابقة لا تطابق ملف الاستخدام أو نقطة البيع أو إعداد المشاركة الحالي.");
        }

        var now = DateTimeOffset.Now;
        var batchId = existingBatch?.BatchId ?? $"NXR-{now:yyMMdd-HHmmss}-{++_batchSequence:000}";
        var batch = existingBatch is null
            ? new GeneratedCardBatch(batchId, targetName, request.Package.DisplayName, request.Quantity,
                request.Package.Price, now, "بانتظار المزامنة",
                request.RouterProfile, request.PointOfSale, request.ManagerAccount, request.SharedUsers, request.BatchName.Trim())
            : existingBatch with { Quantity = existingBatch.Quantity + request.Quantity };

        // A shared identifier set prevents both serials and usernames from colliding.
        var knownIdentifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var card in Cards.Where(card => string.Equals(card.TargetSystem, targetName, StringComparison.Ordinal)))
        {
            if (!string.IsNullOrWhiteSpace(card.CardNumber)) knownIdentifiers.Add(card.CardNumber);
            if (!string.IsNullOrWhiteSpace(card.Username)) knownIdentifiers.Add(card.Username);
        }
        var existingCards = Cards.Where(card => string.Equals(card.TargetSystem, targetName, StringComparison.Ordinal)).ToList();
        var newCards = new List<GeneratedDemoCard>(request.Quantity);
        var tokenLength = request.CardNumberLength - request.CardNumberPrefix.Length - request.CardNumberSuffix.Length;
        var requestedStart = ParseSequenceStart(request.SequenceStartValue);
        var nextSequence = request.NumberGenerationMode == CardNumberGenerationMode.Sequential
            ? ResolveNextSequenceStart(existingCards, request.CardNumberPrefix, request.CardNumberSuffix, requestedStart)
            : BigInteger.One;
        var cardAlphabet = GetAlphabet(request.CardNumberCharacterSet);

        for (var index = 0; index < request.Quantity; index++)
        {
            var number = request.NumberGenerationMode switch
            {
                CardNumberGenerationMode.Random => GenerateUniqueToken(
                    tokenLength, cardAlphabet, knownIdentifiers, request.CardNumberPrefix, request.CardNumberSuffix),
                CardNumberGenerationMode.Sequential => GenerateSequentialNumber(
                    tokenLength, knownIdentifiers, request.CardNumberPrefix, request.CardNumberSuffix, ref nextSequence),
                _ => throw new InvalidOperationException("طريقة توليد رقم الكرت غير معروفة.")
            };

            var username = GenerateUniqueToken(
                request.UsernameLength, GetAlphabet(request.UsernameCharacterSet), knownIdentifiers, string.Empty, string.Empty);

            var password = request.PasswordMode switch
            {
                PasswordGenerationMode.Empty => string.Empty,
                PasswordGenerationMode.SameAsUsername => username,
                PasswordGenerationMode.Generated => GenerateToken(
                    request.PasswordLength, GetAlphabet(request.PasswordCharacterSet)),
                _ => throw new InvalidOperationException("سياسة كلمة المرور غير معروفة.")
            };

            newCards.Add(new GeneratedDemoCard(
                batchId, number, username, password, targetName, request.Package.DisplayName,
                request.Package.Price, request.RouterProfile, request.Package.TimeLimit,
                request.Package.DataLimit, request.Package.RateLimit, now.AddMilliseconds(index),
                "بانتظار المزامنة", request.PointOfSale, request.Comment,
                request.ManagerAccount, request.SharedUsers, request.Package.Validity, "متاحة"));
        }

        // Persist the complete candidate state before publishing it to the shared in-memory collections.
        // If disk persistence fails, no partial batch is exposed to the other screens.
        var nextCards = Cards.Concat(newCards).ToList();
        var nextBatches = Batches.ToList();
        var existingIndex = existingBatch is null ? -1 : nextBatches.FindIndex(item => item.BatchId == existingBatch.BatchId);
        if (existingBatch is null)
            nextBatches.Insert(0, batch);
        else if (existingIndex >= 0)
            nextBatches[existingIndex] = batch;
        else
            throw new InvalidOperationException("تغير سجل الدفعة أثناء العملية. أعد المحاولة.");

        SaveSnapshot(nextCards, nextBatches);

        if (existingBatch is null)
            Batches.Insert(0, batch);
        else
            Batches[existingIndex] = batch;
        foreach (var card in newCards)
            Cards.Add(card);

        StorageWarning = null;
        ScenarioChanged?.Invoke(this, EventArgs.Empty);
        return batch;
    }

    public GeneratedCardBatch ImportCards(
    DemoAccountSystem targetSystem,
    IReadOnlyCollection<string> importedCardNumbers,
    DemoCardPackage package,
    string batchId,
    string routerProfile,
    string pointOfSale,
    string comment,
    string managerAccount = "admin",
    int sharedUsers = 1)
{
    ArgumentNullException.ThrowIfNull(importedCardNumbers);
    ArgumentNullException.ThrowIfNull(package);

    if (_storeReadFailed)
        throw new InvalidOperationException(
            StorageWarning ?? "تعذر قراءة سجل الكروت.");

    if (targetSystem is not (DemoAccountSystem.HotSpot or DemoAccountSystem.UserManager))
        throw new ArgumentOutOfRangeException(nameof(targetSystem));

    batchId = batchId?.Trim() ?? string.Empty;

    if (string.IsNullOrWhiteSpace(batchId))
        throw new ArgumentException("أدخل رقم الدفعة.", nameof(batchId));

    if (Batches.Any(batch =>
        string.Equals(batch.BatchId, batchId, StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException(
            "رقم الدفعة مستخدم بالفعل. تحقّق من الرقم ثم أعد المحاولة.");
    }

    var targetName = targetSystem == DemoAccountSystem.HotSpot
        ? "مستخدمو الهوتسبوت"
        : "مدير المستخدمين";

    var numbers = importedCardNumbers
        .Select(number => number.Trim())
        .Where(number => number.Length > 0)
        .ToList();

    if (numbers.Count is < 1 or > MaxCardsPerOperation)
    {
        throw new ArgumentOutOfRangeException(
            nameof(importedCardNumbers),
            $"عدد الكروت يجب أن يكون بين 1 و{MaxCardsPerOperation:N0} كرت.");
    }

    if (numbers.Any(number => number.Length > 128 || number.Any(char.IsControl)))
    {
        throw new ArgumentException(
            "يوجد رقم كرت فارغ أو أطول من الحد المسموح أو يحتوي على محارف تحكم.");
    }

    var duplicateNumbers = numbers
        .GroupBy(number => number, StringComparer.OrdinalIgnoreCase)
        .Where(group => group.Count() > 1)
        .Select(group => group.Key)
        .Take(5)
        .ToList();

    if (duplicateNumbers.Count > 0)
    {
        throw new InvalidOperationException(
            "الملف يحتوي على أرقام كروت مكررة: " +
            string.Join("، ", duplicateNumbers));
    }

    var knownNumbers = new HashSet<string>(
        Cards
            .Where(card => string.Equals(
                card.TargetSystem, targetName, StringComparison.Ordinal))
            .Select(card => card.CardNumber),
        StringComparer.OrdinalIgnoreCase);

    if (targetSystem == DemoAccountSystem.UserManager)
    {
        knownNumbers.UnionWith(
            Cards
                .Where(card => string.Equals(
                    card.TargetSystem, targetName, StringComparison.Ordinal))
                .Select(card => card.Username));
    }

    var existingNumbers = numbers
        .Where(number => knownNumbers.Contains(number))
        .Take(5)
        .ToList();

    if (existingNumbers.Count > 0)
    {
        throw new InvalidOperationException(
            "توجد أرقام مستخدمة مسبقًا في النظام المحدد: " +
            string.Join("، ", existingNumbers));
    }

    var now = DateTimeOffset.Now;

    var batch = new GeneratedCardBatch(
        batchId,
        targetName,
        package.DisplayName,
        numbers.Count,
        package.Price,
        now,
        "بانتظار المزامنة",
        routerProfile,
        pointOfSale,
        managerAccount,
        sharedUsers);

    var newCards = numbers.Select((number, index) =>
        new GeneratedDemoCard(
            batchId,
            number,
            number,
            string.Empty,
            targetName,
            package.DisplayName,
            package.Price,
            routerProfile,
            package.TimeLimit,
            package.DataLimit,
            package.RateLimit,
            now.AddMilliseconds(index),
            "بانتظار المزامنة",
            pointOfSale,
            comment,
            managerAccount,
            sharedUsers,
            package.Validity,
            "متاحة"))
        .ToList();

    // Save the complete state before publishing imported cards.
    var nextCards = Cards.Concat(newCards).ToList();
    var nextBatches = Batches.ToList();
    nextBatches.Insert(0, batch);

    SaveSnapshot(nextCards, nextBatches);

    Batches.Insert(0, batch);

    foreach (var card in newCards)
        Cards.Add(card);

    StorageWarning = null;
    ScenarioChanged?.Invoke(this, EventArgs.Empty);

    return batch;
}

    public void ArchiveCards(IEnumerable<GeneratedDemoCard> cards, string reason = "أرشفة محلية من إدارة الكروت")
        => UpdateArchiveState(cards, archived: true, reason);

    public void RestoreCards(IEnumerable<GeneratedDemoCard> cards)
        => UpdateArchiveState(cards, archived: false, string.Empty);

    /// <summary>
    /// Stores simulated synchronization outcomes in the local demo issuance snapshot.
    /// The supplied status map is keyed by the generated card number; unrelated cards and
    /// batches are left untouched. This operation never contacts RouterOS or a network.
    /// </summary>
    public void UpdateBatchSynchronizationResults(
        string batchId,
        string targetSystem,
        IReadOnlyDictionary<string, string> cardStatuses,
        string batchStatus)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetSystem);
        ArgumentNullException.ThrowIfNull(cardStatuses);
        ArgumentException.ThrowIfNullOrWhiteSpace(batchStatus);
        if (_storeReadFailed)
            throw new InvalidOperationException(StorageWarning ?? "تعذر قراءة سجل الكروت.");

        bool Matches(GeneratedDemoCard card) =>
            string.Equals(card.BatchId, batchId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(card.TargetSystem, targetSystem, StringComparison.OrdinalIgnoreCase);
        bool MatchesBatch(GeneratedCardBatch batch) =>
            string.Equals(batch.BatchId, batchId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(batch.TargetSystem, targetSystem, StringComparison.OrdinalIgnoreCase);

        var nextCards = Cards.Select(card => Matches(card) && cardStatuses.TryGetValue(card.CardNumber, out var status)
            ? card with { LocalStatus = status }
            : card).ToList();
        var nextBatches = Batches.Select(batch => MatchesBatch(batch)
            ? batch with { LocalStatus = batchStatus }
            : batch).ToList();
        if (!nextBatches.Any(MatchesBatch))
            throw new InvalidOperationException("الدفعة غير موجودة في سجل الإصدار الحالي.");

        // Persist the full candidate state before publishing changes to bound screens.
        SaveSnapshot(nextCards, nextBatches);
        for (var index = 0; index < nextCards.Count; index++)
            if (Cards[index] != nextCards[index])
                Cards[index] = nextCards[index];
        for (var index = 0; index < nextBatches.Count; index++)
            if (Batches[index] != nextBatches[index])
                Batches[index] = nextBatches[index];

        StorageWarning = null;
        ScenarioChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Archives or restores cards for a batch without overwriting LocalStatus, because that
    /// field records synchronization state and must remain independent from archive state.
    /// </summary>
    public void SetBatchCardsArchiveStatePreservingSyncStatus(
        string batchId,
        string targetSystem,
        bool archived,
        string reason = "أرشفة الدفعة")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(batchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetSystem);
        if (_storeReadFailed)
            throw new InvalidOperationException(StorageWarning ?? "تعذر قراءة سجل الكروت.");

        var now = DateTimeOffset.Now;
        var nextCards = Cards.Select(card =>
                string.Equals(card.BatchId, batchId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(card.TargetSystem, targetSystem, StringComparison.OrdinalIgnoreCase)
                    ? card with
                    {
                        ArchivedAt = archived ? now : null,
                        ArchiveReason = archived ? (reason ?? string.Empty) : string.Empty
                    }
                    : card)
            .ToList();
        if (!nextCards.Any(card =>
                string.Equals(card.BatchId, batchId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(card.TargetSystem, targetSystem, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("لا توجد كروت محفوظة لهذه الدفعة.");

        SaveSnapshot(nextCards, Batches.ToList());
        for (var index = 0; index < nextCards.Count; index++)
            if (Cards[index] != nextCards[index])
                Cards[index] = nextCards[index];

        StorageWarning = null;
        ScenarioChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateArchiveState(IEnumerable<GeneratedDemoCard> cards, bool archived, string reason)
    {
        ArgumentNullException.ThrowIfNull(cards);
        if (_storeReadFailed)
            throw new InvalidOperationException(StorageWarning ?? "تعذر قراءة سجل الكروت.");

        var cardKeys = cards.Select(card => $"{card.TargetSystem}\u001F{card.CardNumber}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (cardKeys.Count == 0) return;

        var now = DateTimeOffset.Now;
        var nextCards = Cards.Select(card => cardKeys.Contains($"{card.TargetSystem}\u001F{card.CardNumber}")
            ? card with
            {
                ArchivedAt = archived ? now : null,
                ArchiveReason = archived ? reason : string.Empty,
                LocalStatus = archived ? "مؤرشف" : "بانتظار المزامنة"
            }
            : card).ToList();

        SaveSnapshot(nextCards, Batches.ToList());
        Cards.Clear();
        foreach (var card in nextCards.OrderBy(card => card.CreatedAt))
            Cards.Add(card);
        ScenarioChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SeedScenario()
    {
        var statuses = new[] { "متاحة", "قيد الاستخدام", "منتهية", "موقوفة" };
        var targets = new[] { DemoAccountSystem.HotSpot, DemoAccountSystem.UserManager };
        var sequence = 1;
        var groupNumber = 1;

        foreach (var target in targets)
        {
            var packages = DemoCardPackageCatalog.Load(target);
            if (packages.Count == 0)
                throw new InvalidDataException($"لا توجد باقات مرتبطة بـ{(target == DemoAccountSystem.HotSpot ? "الهوتسبوت" : "مدير المستخدمين")} في الكتالوج.");

            foreach (var package in packages.Take(4))
            {
                var targetName = target == DemoAccountSystem.HotSpot ? "مستخدمو الهوتسبوت" : "مدير المستخدمين";
                var profile = target == DemoAccountSystem.HotSpot ? package.HotSpotProfile : package.UserManagerProfile;
                var managerAccount = target == DemoAccountSystem.UserManager ? "admin" : "غير منطبق";
                var createdAt = DateTimeOffset.Now.AddDays(-Math.Max(1, 8 - groupNumber));
                var batchId = $"DEMO-{(target == DemoAccountSystem.HotSpot ? "HS" : "UM")}-{groupNumber:00}";
                Batches.Add(new GeneratedCardBatch(batchId, targetName, package.DisplayName, 2,
                    package.Price, createdAt, "بانتظار المزامنة", profile,
                    groupNumber % 3 == 0 ? "وكيل الشبكة" : groupNumber % 2 == 0 ? "نقطة بيع الحي" : "المركز الرئيسي",
                    managerAccount, 1));

                for (var row = 0; row < 2; row++)
                {
                    var pointOfSale = groupNumber % 3 == 0 ? "وكيل الشبكة" : groupNumber % 2 == 0 ? "نقطة بيع الحي" : "المركز الرئيسي";
                    var status = statuses[(sequence - 1) % statuses.Length];
                    var number = $"26001{sequence:000}";
                    var username = $"61{sequence:0000}";
                    var password = username;
                    Cards.Add(new GeneratedDemoCard(batchId, number, username, password, targetName,
                        package.DisplayName, package.Price, profile, package.TimeLimit, package.DataLimit,
                        package.RateLimit, createdAt.AddMinutes(row), "بانتظار المزامنة",
                        pointOfSale, "حساب تم إنشاؤه ضمن دفعة الإصدار", managerAccount, 1,
                        package.Validity, status));
                    sequence++;
                }
                groupNumber++;
            }
        }
    }

    private static void SaveSnapshot(IReadOnlyCollection<GeneratedDemoCard> cards, IReadOnlyCollection<GeneratedCardBatch> batches)
    {
        var directory = Path.GetDirectoryName(StorePath)!;
        Directory.CreateDirectory(directory);
        var tempPath = StorePath + ".tmp";
        var json = JsonSerializer.Serialize(new DemoCardIssuanceStore(cards.ToList(), batches.ToList()), JsonOptions);
        File.WriteAllText(tempPath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(tempPath, StorePath, overwrite: true);
    }

    public sealed record DemoCardIssuanceStore(List<GeneratedDemoCard> Cards, List<GeneratedCardBatch> Batches);

    private static void Validate(CardIssuanceRequest request)
    {
        if (request.Quantity is < 1 or > MaxCardsPerOperation)
            throw new ArgumentOutOfRangeException(nameof(request.Quantity),
                $"الكمية يجب أن تكون بين 1 و{MaxCardsPerOperation:N0} كرت في العملية الواحدة.");

        if (request.CardNumberLength is < 5 or > 24)
            throw new ArgumentOutOfRangeException(nameof(request.CardNumberLength), "طول رقم الكرت يجب أن يكون بين 5 و24 رمزًا.");

        var tokenLength = request.CardNumberLength - request.CardNumberPrefix.Length - request.CardNumberSuffix.Length;
        if (tokenLength < 1)
            throw new ArgumentException("الطول الكلي لا يترك مساحة لتوليد الرقم بعد البادئة واللاحقة.");



        if (request.PasswordMode == PasswordGenerationMode.Generated &&
            request.PasswordLength is < 4 or > 20)
            throw new ArgumentOutOfRangeException(nameof(request.PasswordLength), "طول كلمة المرور يجب أن يكون بين 4 و20 رمزًا.");

        if (request.CardNumberPrefix.Any(char.IsWhiteSpace) || request.CardNumberSuffix.Any(char.IsWhiteSpace))
            throw new ArgumentException("لا يمكن أن تحتوي البادئة أو اللاحقة على مسافات.");

        if (request.NumberGenerationMode == CardNumberGenerationMode.Sequential)
        {
            var start = ParseSequenceStart(request.SequenceStartValue);
            if (start.ToString(CultureInfo.InvariantCulture).Length > tokenLength)
                throw new ArgumentException("رقم بداية التسلسل لا يتسع للطول المتبقي من رقم الكرت.");
        }

        if (string.IsNullOrWhiteSpace(request.RouterProfile))
            throw new ArgumentException("اختر ملف الاستخدام الفني أو اترك الملف المقترح من الباقة.");
        if (string.IsNullOrWhiteSpace(request.PointOfSale))
            throw new ArgumentException("اختر نقطة البيع المرتبطة بالدفعة.");
        if (request.SharedUsers is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(request.SharedUsers), "عدد الاتصالات المتزامنة يجب أن يكون بين 1 و100.");
        if (request.TargetSystem == DemoAccountSystem.UserManager && string.IsNullOrWhiteSpace(request.ManagerAccount))
            throw new ArgumentException("اختر مستخدم مدير المستخدمين.");
    }

    private static BigInteger ParseSequenceStart(string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? "1" : value.Trim();
        if (!BigInteger.TryParse(normalized, NumberStyles.None, CultureInfo.InvariantCulture, out var result) || result < BigInteger.Zero)
            throw new ArgumentException("رقم بداية التسلسل يجب أن يكون عددًا صحيحًا غير سالب.");
        return result;
    }

    private static BigInteger ResolveNextSequenceStart(
        IEnumerable<GeneratedDemoCard> existingCards, string prefix, string suffix, BigInteger requestedStart)
    {
        var next = requestedStart;
        foreach (var card in existingCards)
        {
            var value = card.CardNumber ?? string.Empty;
            if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
                value.Length <= prefix.Length + suffix.Length)
                continue;
            var body = value.Substring(prefix.Length, value.Length - prefix.Length - suffix.Length);
            if (BigInteger.TryParse(body, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= next)
                next = parsed + BigInteger.One;
        }
        return next;
    }

    private static string GenerateSequentialNumber(
        int tokenLength, HashSet<string> knownValues, string prefix, string suffix, ref BigInteger nextValue)
    {
        for (var attempt = 0; attempt < MaxCardsPerOperation * 2; attempt++)
        {
            var body = nextValue.ToString(CultureInfo.InvariantCulture);
            if (body.Length > tokenLength)
                throw new InvalidOperationException("نفد نطاق الأرقام المتاح لهذا الطول. زد طول الرقم أو غيّر رقم البداية.");
            body = body.PadLeft(tokenLength, '0');
            nextValue += BigInteger.One;
            var value = prefix + body + suffix;
            if (knownValues.Add(value)) return value;
        }
        throw new InvalidOperationException("تعذر العثور على أرقام متسلسلة غير مستخدمة ضمن النطاق المحدد.");
    }

    private static string GenerateUniqueToken(int length, string alphabet, HashSet<string> knownValues, string prefix, string suffix)
    {
        for (var attempt = 0; attempt < 4000; attempt++)
        {
            var value = prefix + GenerateToken(length, alphabet) + suffix;
            if (knownValues.Add(value)) return value;
        }
        throw new InvalidOperationException("تعذر ضمان تفرد القيم المولدة؛ قلل الكمية أو غيّر طول الرقم.");
    }

    private static string GenerateToken(int length, string alphabet)
    {
        var buffer = new char[length];
        for (var index = 0; index < buffer.Length; index++)
            buffer[index] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        return new string(buffer);
    }

    private static string GetAlphabet(CardNumberCharacterSet characterSet) => characterSet switch
    {
        CardNumberCharacterSet.NumericOnly => Digits,
        CardNumberCharacterSet.AlphaNumeric => AlphaNumeric,
        CardNumberCharacterSet.LettersOnly => Letters,
        _ => throw new ArgumentOutOfRangeException(nameof(characterSet))
    };

    private static string GetAlphabet(CredentialCharacterSet characterSet) => characterSet switch
    {
        CredentialCharacterSet.NumericOnly => Digits,
        CredentialCharacterSet.AlphaNumeric => AlphaNumeric,
        CredentialCharacterSet.LettersOnly => Letters,
        _ => throw new ArgumentOutOfRangeException(nameof(characterSet))
    };
}
