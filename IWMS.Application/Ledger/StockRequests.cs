namespace IWMS.Application.Ledger;

public record IssueStockRequest(
    Guid ProductId,
    Guid StorageLocationId,
    decimal Quantity,
    Guid PerformedByUserId,
    Guid IdempotencyKey,
    string ReferenceNumber,
    string Reason);

public record AdjustStockRequest(
    Guid ProductId,
    Guid StorageLocationId,
    decimal Quantity,
    bool IsIncrease,
    string Reason,
    Guid PerformedByUserId,
    Guid IdempotencyKey);

public record TransferStockRequest(
    Guid ProductId,
    Guid FromStorageLocationId,
    Guid ToStorageLocationId,
    decimal Quantity,
    string Reason,
    Guid PerformedByUserId,
    Guid IdempotencyKey);

public record StockPostingResult(Guid MovementId, string ReferenceNumber, bool WasDuplicate);