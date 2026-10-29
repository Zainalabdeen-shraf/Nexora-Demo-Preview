using System.Globalization;

namespace Nexora.Demo.Desktop.Features.Sales.Models;

/// <summary>A durable local sales-ledger operation. Amounts are snapshots so later profile changes do not rewrite history.</summary>
public sealed record SalesOperationRecord(
    string Id,
    string Reference,
    DateTimeOffset OccurredAt,
    string ChannelKey,
    string ChannelLabel,
    string PointId,
    string PointName,
    string PackageName,
    int Quantity,
    decimal UnitPrice,
    decimal GrossAmount,
    decimal CommissionPercent,
    decimal CommissionAmount,
    decimal NetworkAmount,
    decimal AmountReceived,
    decimal OutstandingAmount,
    string PaymentMethod,
    string CollectionStatus,
    bool RequiresPrinting,
    decimal PrintingExpense,
    string ExternalReference,
    string Notes,
    string SourceBatchId = "",
    decimal DiscountAmount = 0m)
{
    public string DiscountAmountLabel => $"{DiscountAmount:N2} ريال";
    public string OccurredAtLabel => OccurredAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);
    public string QuantityLabel => Quantity.ToString("N0", CultureInfo.CurrentCulture);
    public string GrossAmountLabel => Money(GrossAmount);
    public string CommissionLabel => $"{CommissionPercent:0.##}% · {Money(CommissionAmount)}";
    public string NetworkAmountLabel => Money(NetworkAmount);
    public string ReceivedLabel => Money(AmountReceived);
    public string OutstandingLabel => Money(OutstandingAmount);
    public string PrintingExpenseLabel => Money(PrintingExpense);
    private static string Money(decimal amount) => $"{amount:N0} ريال";
}

/// <summary>Batch-level handover/invoice to a physical sales point. The commission rate is snapshotted.</summary>
public sealed record SalesStockTransferRecord(
    string Id,
    string Reference,
    DateTimeOffset OccurredAt,
    string BatchId,
    string TargetSystem,
    string PackageName,
    string PointId,
    string PointName,
    int Quantity,
    decimal UnitPrice,
    decimal GrossAmount,
    decimal CommissionPercent,
    decimal CommissionAmount,
    decimal NetAmount,
    decimal PaidAtTransfer,
    decimal PrintingExpense,
    string SettlementPolicy,
    string Notes)
{
    public decimal Balance => Math.Max(0m, NetAmount - PaidAtTransfer);
    public string StatusLabel => Balance > 0m ? "رصيد مفتوح" : "مسدد";
    public string OccurredAtLabel => OccurredAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);
    public string GrossAmountLabel => $"{GrossAmount:N0} ريال";
    public string CommissionLabel => $"{CommissionPercent:0.##}% · {CommissionAmount:N0} ريال";
    public string NetAmountLabel => $"{NetAmount:N0} ريال";
    public string PaidLabel => $"{PaidAtTransfer:N0} ريال";
    public string BalanceLabel => $"{Balance:N0} ريال";
    public string PrintingExpenseLabel => $"{PrintingExpense:N0} ريال";
}

public sealed record SalesAccountSettlementRecord(
    string Id,
    string Reference,
    DateTimeOffset OccurredAt,
    string PointId,
    string PointName,
    decimal Amount,
    string PaymentMethod,
    string ExternalReference,
    string Notes)
{
    public string OccurredAtLabel => OccurredAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);
    public string AmountLabel => $"{Amount:N0} ريال";
}

public sealed record SalesExpenseRecord(
    string Id,
    string Reference,
    DateTimeOffset OccurredAt,
    string Category,
    string Description,
    decimal Amount,
    string RelatedReference,
    string Notes)
{
    public string OccurredAtLabel => OccurredAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);
    public string AmountLabel => $"{Amount:N0} ريال";
}

public sealed record SalesOperationDraft(
    string ChannelKey,
    string ChannelLabel,
    string PointId,
    string PointName,
    string PackageName,
    int Quantity,
    decimal UnitPrice,
    string CollectionMode,
    decimal AmountReceived,
    string PaymentMethod,
    decimal PrintingCostPerCard,
    string ExternalReference,
    string Notes,
    string SourceBatchId = "",
    int SourceBatchQuantity = 0,
    decimal DiscountAmount = 0m);

public sealed record SalesStockTransferDraft(
    string BatchId,
    string TargetSystem,
    string PackageName,
    int BatchQuantity,
    string PointId,
    string PointName,
    bool PointAllowsCredit,
    decimal CommissionPercent,
    int Quantity,
    decimal UnitPrice,
    decimal PaidAtTransfer,
    decimal PrintingCostPerCard,
    string Notes);

public sealed record SalesSettlementDraft(
    string PointId,
    string PointName,
    decimal Amount,
    string PaymentMethod,
    string ExternalReference,
    string Notes);

public sealed record SalesExpenseDraft(
    string Category,
    string Description,
    decimal Amount,
    string RelatedReference,
    string Notes);

public sealed record SalesOperationsSnapshot(
    int SchemaVersion,
    List<SalesOperationRecord> Operations,
    List<SalesStockTransferRecord> Transfers,
    List<SalesAccountSettlementRecord> Settlements,
    List<SalesExpenseRecord> Expenses);

public sealed record SalesPointAccountRow(
    string PointId,
    string PointName,
    string PointType,
    decimal CommissionPercent,
    bool AllowCredit,
    int TransferCount,
    int CardsSupplied,
    decimal GrossValue,
    decimal CommissionValue,
    decimal NetDue,
    decimal PaidAtTransfer,
    decimal Settlements,
    decimal Balance,
    DateTimeOffset? LastMovement,
    string ContactName = "")
{
    public string ContactLabel => string.IsNullOrWhiteSpace(ContactName) ? "—" : ContactName;
    public string StatusLabel => Balance > 0m ? "عليه رصيد" : "مسدد";
    public string CommissionLabel => $"{CommissionPercent:0.##}%";
    public string GrossValueLabel => $"{GrossValue:N0} ريال";
    public string CommissionValueLabel => $"{CommissionValue:N0} ريال";
    public string NetDueLabel => $"{NetDue:N0} ريال";
    public string PaidLabel => $"{PaidAtTransfer + Settlements:N0} ريال";
    public string BalanceLabel => $"{Balance:N0} ريال";
    public string LastMovementLabel => LastMovement?.ToLocalTime().ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture) ?? "لا توجد حركة";
    public string CreditLabel => AllowCredit ? "يسمح بالآجل" : "فوري";
}

public sealed record SalesHistoryRow(
    DateTimeOffset OccurredAt,
    string Reference,
    string ActivityType,
    string Channel,
    string PointName,
    string Description,
    int Quantity,
    decimal GrossAmount,
    decimal CommissionAmount,
    decimal NetAmount,
    decimal ReceivedAmount,
    decimal OutstandingAmount,
    string Status)
{
    public string OccurredAtLabel => OccurredAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm", CultureInfo.InvariantCulture);
    public string QuantityLabel => Quantity == 0 ? "—" : Quantity.ToString("N0", CultureInfo.CurrentCulture);
    public string GrossLabel => $"{GrossAmount:N0} ريال";
    public string CommissionLabel => $"{CommissionAmount:N0} ريال";
    public string NetLabel => $"{NetAmount:N0} ريال";
    public string ReceivedLabel => $"{ReceivedAmount:N0} ريال";
    public string OutstandingLabel => $"{OutstandingAmount:N0} ريال";
}
