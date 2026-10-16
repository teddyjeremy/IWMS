namespace IWMS.Application.Purchasing;

public record PurchaseOrderLineInput(Guid ProductId, decimal Quantity, decimal ExpectedUnitPrice);

public record SavePurchaseOrderRequest(
    Guid SupplierId,
    DateTime? ExpectedDeliveryUtc,
    string Notes,
    IReadOnlyList<PurchaseOrderLineInput> Lines,
    uint Version);

public record PurchaseOrderCreated(Guid Id, string OrderNumber);