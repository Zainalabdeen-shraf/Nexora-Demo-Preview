using System.Globalization;
using Nexora.Demo.Desktop.Features.Cards.Models;
using Nexora.Demo.Desktop.Features.Cards.Services;

namespace Nexora.Demo.Desktop.Features.Cards.Views;

public partial class CardBatchesView
{
    private static BatchSyncState ClassifySyncStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return BatchSyncState.Unknown;
        if (status.Contains("بانتظار المزامنة", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("غير متزامن", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("pending", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("unsynced", StringComparison.OrdinalIgnoreCase)) return BatchSyncState.Pending;
        if (status.Contains("مكتملة المزامنة", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("تمت المزامنة", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("synced", StringComparison.OrdinalIgnoreCase)) return BatchSyncState.Synced;
        if (status.Contains("جارٍ المزامنة", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("جاري المزامنة", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("قيد المزامنة", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("syncing", StringComparison.OrdinalIgnoreCase)) return BatchSyncState.InProgress;
        if (status.Contains("فشلت المزامنة", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("فشل", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("تعذر", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("failure", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("error", StringComparison.OrdinalIgnoreCase)) return BatchSyncState.Failed;
        return BatchSyncState.Unknown;
    }

    private enum BatchSyncState { Unknown, Pending, InProgress, Synced, Failed }

    private sealed class BatchManagementRow
    {
        public BatchManagementRow(
            GeneratedCardBatch batch,
            IReadOnlyList<GeneratedDemoCard> cards,
            BatchManagementMetadata metadata,
            int printedCount,
            bool metadataUnavailable)
        {
            Batch = batch;
            Cards = cards;
            Metadata = metadata;
            PrintedCount = Math.Max(0, printedCount);
            MetadataUnavailable = metadataUnavailable;
            SyncedAccountsCount = cards.Count(card => ClassifySyncStatus(card.LocalStatus) == BatchSyncState.Synced);
            FailedAccountsCount = cards.Count(card => ClassifySyncStatus(card.LocalStatus) == BatchSyncState.Failed);
            InProgressAccountsCount = cards.Count(card => ClassifySyncStatus(card.LocalStatus) == BatchSyncState.InProgress);
            PendingAccountsCount = cards.Count(card => ClassifySyncStatus(card.LocalStatus) == BatchSyncState.Pending);
            LinkedCardCount = cards.Count;
            HasQuantityMismatch = batch.Quantity <= 0 || LinkedCardCount != batch.Quantity;
            IsArchived = metadata.IsArchived || (cards.Count > 0 && cards.All(card => card.IsArchived));
            IsDisabled = metadata.IsDisabled;
            IsFullyExpiredAndArchived = IsArchived && batch.Quantity > 0 && cards.Count == batch.Quantity &&
                cards.All(card => card.IsArchived && string.Equals(card.DemoStatus, "منتهية", StringComparison.OrdinalIgnoreCase));
            StatusLabel = ComputeStatusLabel();
        }

        public GeneratedCardBatch Batch { get; }
        public IReadOnlyList<GeneratedDemoCard> Cards { get; }
        public BatchManagementMetadata Metadata { get; }
        public string BatchId => Batch.BatchId;
        public string DisplayName => Batch.DisplayName;
        public string TargetSystem => Batch.TargetSystem;
        public string SystemKey => Batch.TargetSystem.Contains("هوتسبوت", StringComparison.OrdinalIgnoreCase) ||
                                   Batch.TargetSystem.Contains("HotSpot", StringComparison.OrdinalIgnoreCase)
            ? "hotspot"
            : "user-manager";
        public string PackageName => Batch.PackageName;
        public int Quantity => Batch.Quantity;
        public decimal UnitPrice => Batch.UnitPrice;
        public decimal Total => Batch.Total;
        public string UnitPriceLabel => $"{UnitPrice:N0} ريال";
        public string TotalLabel => Batch.TotalLabel;
        public DateTimeOffset CreatedAt => Batch.CreatedAt;
        public string CreatedAtLabel => Batch.CreatedAtLabel;
        public string LocalStatus => Batch.LocalStatus;
        public string PointOfSale => string.IsNullOrWhiteSpace(Batch.PointOfSale) ? "—" : Batch.PointOfSale;
        public string RouterProfile => Batch.RouterProfile ?? string.Empty;
        public string ManagerAccount => Batch.ManagerAccount ?? string.Empty;
        public int LinkedCardCount { get; }
        public int SyncedAccountsCount { get; }
        public int PendingAccountsCount { get; }
        public int FailedAccountsCount { get; }
        public int InProgressAccountsCount { get; }
        public int PrintedCount { get; }
        public string PrintedCountLabel => PrintedCount.ToString("N0", CultureInfo.CurrentCulture);
        public bool MetadataUnavailable { get; }
        public bool HasQuantityMismatch { get; }
        public bool IsDisabled { get; }
        public bool IsArchived { get; }
        public bool IsFullyExpiredAndArchived { get; }
        public string StatusLabel { get; }
        public bool NeedsReview => MetadataUnavailable || HasQuantityMismatch;
        public bool IsNotSyncedAndNotDisabledOrArchived =>
            !IsDisabled && !IsArchived && SyncedAccountsCount < Quantity &&
            StatusLabel is not "معطلة" and not "معطلة ومؤرشفة" and not "منتهية ومؤرشفة";
        public bool CanSync => !MetadataUnavailable && !IsDisabled && !IsArchived && !HasQuantityMismatch && Cards.Count > 0 &&
            !string.Equals(Metadata.LastSyncState, "جارٍ المزامنة", StringComparison.Ordinal);
        public bool CanPrint => !MetadataUnavailable && !IsArchived && Cards.Any(card =>
            !card.IsArchived && !string.Equals(card.DemoStatus, "منتهية", StringComparison.OrdinalIgnoreCase));
        public bool CanToggleDisabled => !MetadataUnavailable && !IsArchived && !HasQuantityMismatch;
        public bool CanArchive => !MetadataUnavailable && (IsArchived || (!HasQuantityMismatch && (SyncedAccountsCount == 0 || AllCardsExpired)));
        public bool AllCardsExpired => Cards.Count > 0 && Cards.Count == Quantity && Cards.All(card =>
            string.Equals(card.DemoStatus, "منتهية", StringComparison.OrdinalIgnoreCase));
        public string SyncToolTip => MetadataUnavailable
            ? "بيانات حالة الدفعة غير متاحة؛ العمليات موقوفة حفاظاً على السجل."
            : string.Equals(Metadata.LastSyncState, "جارٍ المزامنة", StringComparison.Ordinal) ? "توجد عملية مزامنة جارية لهذه الدفعة."
            : IsArchived ? "استعد الدفعة قبل مزامنتها."
            : IsDisabled ? "أعد تفعيل الدفعة قبل مزامنتها."
            : HasQuantityMismatch ? $"الكمية المسجلة {Quantity:N0} لكن عدد سجلات الكروت {LinkedCardCount:N0}; لا يمكن مزامنتها حتى يتطابق العدد."
            : "تسجيل طلب متابعة محلي فقط. لن تتغير حالة الحساب حتى يكتمل موصل الكتابة الفعلي للراوتر.";
        public string PrintToolTip => CanPrint
            ? $"فتح نافذة الطباعة للقالب الحالي وإرسال الكروت غير المؤرشفة وغير المنتهية. سبق إرسال {PrintedCount:N0} بطاقة للطباعة."
            : "لا توجد كروت متاحة للطباعة في هذه الدفعة.";
        public string DisableActionToolTip => MetadataUnavailable
            ? "بيانات حالة الدفعة غير متاحة؛ العمليات موقوفة حفاظاً على السجل."
            : HasQuantityMismatch ? "الكمية المسجلة لا تطابق سجلات الكروت؛ راجع الدفعة قبل تعطيلها أو إعادة تفعيلها."
            : IsArchived ? "استعد الدفعة أولاً قبل تغيير حالة التعطيل."
            : IsDisabled ? "إعادة تفعيل الدفعة محلياً ضمن الديمو مع الاحتفاظ بحالة المزامنة."
            : "تعطيل الدفعة محلياً ضمن الديمو؛ لا يتم تغيير حسابات على راوتر خارجي.";
        public string ArchiveActionToolTip => MetadataUnavailable
            ? "بيانات حالة الدفعة غير متاحة؛ العمليات موقوفة حفاظاً على السجل."
            : IsArchived ? "استعادة الدفعة من الأرشيف. لا تؤدي الاستعادة وحدها إلى إعادة تفعيلها."
            : HasQuantityMismatch ? "الكمية المسجلة لا تطابق سجلات الكروت؛ راجع الدفعة قبل أرشفتها."
            : SyncedAccountsCount > 0 && !AllCardsExpired
                ? "لا يمكن أرشفة دفعة متزامنة قبل انتهاء جميع كروتها. يمكن تعطيلها بدلاً من ذلك."
                : "أرشفة الدفعة. الدفعة غير المتزامنة ستصبح معطلة ومؤرشفة؛ أما الدفعة المنتهية فتظهر منتهية ومؤرشفة.";

        private string ComputeStatusLabel()
        {
            if (MetadataUnavailable) return "تحتاج مراجعة";
            if (IsFullyExpiredAndArchived) return "منتهية ومؤرشفة";
            if (IsArchived && (IsDisabled || SyncedAccountsCount == 0)) return "معطلة ومؤرشفة";
            if (IsArchived) return "معطلة ومؤرشفة";
            if (IsDisabled) return "معطلة";
            if (HasQuantityMismatch) return "تحتاج مراجعة";
            if (string.Equals(Metadata.LastSyncState, "جارٍ المزامنة", StringComparison.Ordinal)) return "جارٍ المزامنة";

            var cardStates = Cards.Select(card => ClassifySyncStatus(card.LocalStatus)).ToList();
            if (Quantity > 0 && LinkedCardCount == Quantity && SyncedAccountsCount == Quantity &&
                cardStates.All(state => state == BatchSyncState.Synced)) return "مكتملة المزامنة";
            if (SyncedAccountsCount > 0) return "مكتملة جزئياً";
            if (FailedAccountsCount > 0 && FailedAccountsCount == LinkedCardCount) return "فشلت المزامنة";
            if (string.Equals(Metadata.LastSyncState, "فشلت المزامنة", StringComparison.Ordinal) ||
                ClassifySyncStatus(Batch.LocalStatus) == BatchSyncState.Failed) return "فشلت المزامنة";
            if (InProgressAccountsCount > 0 || ClassifySyncStatus(Batch.LocalStatus) == BatchSyncState.InProgress) return "جارٍ المزامنة";
            if (LinkedCardCount > 0 && cardStates.All(state => state == BatchSyncState.Pending)) return "بانتظار المزامنة";
            if (ClassifySyncStatus(Batch.LocalStatus) == BatchSyncState.Pending) return "بانتظار المزامنة";
            return "غير متزامنة";
        }
    }
}
