using IWMS.Domain.Enums;

namespace IWMS.Api.Dtos;

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

public record ProductListItem(
    Guid Id, string Sku, string Name, string Category, UnitOfMeasure BaseUnit,
    decimal OnHand, decimal Reserved, decimal Available,
    decimal ReorderLevel, bool IsLowStock, bool IsActive);

public record StockByLocation(
    Guid StorageLocationId, string Warehouse, string Location,
    decimal OnHand, decimal Reserved, decimal Available);

public record ProductDetail(
    Guid Id, string Sku, string Name, string Description, string? Barcode,
    Guid CategoryId, string Category, UnitOfMeasure BaseUnit,
    decimal? CostPrice, decimal ReorderLevel, bool IsActive,
    List<StockByLocation> Stock);

public record SaveProductRequest(
    string Sku, string Name, string Description, string? Barcode,
    Guid CategoryId, UnitOfMeasure BaseUnit,
    decimal? CostPrice, decimal ReorderLevel, bool IsActive);

public record CategoryItem(Guid Id, string Name, string Description);
public record SaveCategoryRequest(string Name, string Description);

public record LocationItem(Guid Id, string Code, string Description, bool IsActive);
public record WarehouseItem(
    Guid Id, string Code, string Name, string Address, bool IsActive, List<LocationItem> Locations);
public record CreateWarehouseRequest(string Code, string Name, string Address);
public record CreateLocationRequest(string Code, string Description);

public record MovementItem(
    Guid Id, DateTime CreatedAtUtc, string Sku, string ProductName,
    string Warehouse, string Location, MovementType Type,
    decimal QuantityChange, decimal QuantityBefore, decimal QuantityAfter,
    string Reason, string ReferenceNumber, string PerformedBy);

public record PurchaseOrderListItem(
    Guid Id, string OrderNumber, string Supplier, PurchaseOrderStatus Status,
    DateTime? ExpectedDeliveryUtc, decimal TotalOrdered, decimal TotalReceived);

public record PurchaseOrderLineItem(
    Guid Id, Guid ProductId, string Sku, string ProductName,
    decimal QuantityOrdered, decimal QuantityReceived, decimal QuantityOutstanding,
    decimal ExpectedUnitPrice);

public record PurchaseOrderDetail(
    Guid Id, string OrderNumber, string Supplier, PurchaseOrderStatus Status,
    DateTime? ExpectedDeliveryUtc, string Notes, List<PurchaseOrderLineItem> Lines);

public record ReceiptLineItem(
    string Sku, string ProductName, string Location,
    decimal QuantityAccepted, decimal QuantityRejected, string DiscrepancyNote);

public record ReceiptDetail(
    Guid Id, string ReceiptNumber, string PurchaseOrderNumber, DateTime ReceivedAtUtc,
    string ReceivedBy, string Notes, List<ReceiptLineItem> Lines);

public record UserItem(Guid Id, string FullName, string Email, bool IsActive, List<string> Roles);

public record ReconciliationMismatch(
    Guid ProductId, Guid StorageLocationId, decimal BalanceOnHand, decimal LedgerTotal);