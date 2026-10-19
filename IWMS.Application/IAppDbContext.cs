using IWMS.Domain.Common;
using IWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace IWMS.Application;

public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<Category> Categories { get; }
    DbSet<Product> Products { get; }
    DbSet<Supplier> Suppliers { get; }
    DbSet<Warehouse> Warehouses { get; }
    DbSet<StorageLocation> StorageLocations { get; }
    DbSet<PurchaseOrder> PurchaseOrders { get; }
    DbSet<PurchaseOrderLine> PurchaseOrderLines { get; }
    DbSet<GoodsReceipt> GoodsReceipts { get; }
    DbSet<InventoryBalance> InventoryBalances { get; }
    DbSet<InventoryMovement> InventoryMovements { get; }
    DbSet<AuditEvent> AuditEvents { get; }
    DbSet<Customer> Customers { get; }
    DbSet<GoodsReceiptLine> GoodsReceiptLines { get; }
    DbSet<SalesOrder> SalesOrders { get; }
    DbSet<SalesOrderLine> SalesOrderLines { get; }
    DbSet<Reservation> Reservations { get; }
    DbSet<Shipment> Shipments { get; }
    DbSet<ShipmentLine> ShipmentLines { get; }
    DbSet<StockTransfer> StockTransfers { get; }
    DbSet<StockTransferLine> StockTransferLines { get; }
    DbSet<StockCountSession> StockCountSessions { get; }
    DbSet<StockCountLine> StockCountLines { get; }
    DbSet<ReturnRecord> ReturnRecords { get; }
    DbSet<ReturnRecordLine> ReturnRecordLines { get; }

    void SetOriginalVersion<TEntity>(TEntity entity, uint version) where TEntity : BaseEntity;

    Task<long> NextPurchaseOrderNumberAsync(CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}