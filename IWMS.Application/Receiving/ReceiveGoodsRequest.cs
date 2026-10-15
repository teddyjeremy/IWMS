namespace IWMS.Application.Receiving;

public record ReceiveGoodsRequest(
    Guid PurchaseOrderId,
    Guid ReceivedByUserId,
    Guid IdempotencyKey,
    bool AllowOverReceipt,
    string Notes,
    IReadOnlyList<ReceiveGoodsLine> Lines);

public record ReceiveGoodsLine(
    Guid PurchaseOrderLineId,
    Guid StorageLocationId,
    decimal QuantityAccepted,
    decimal QuantityRejected,
    string DiscrepancyNote);

public record ReceiptResult(Guid GoodsReceiptId, string ReceiptNumber, bool WasDuplicate);