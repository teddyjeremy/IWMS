using IWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace IWMS.Application;

public interface IAppDbContext
{
    DbSet<PurchaseOrder> PurchaseOrders { get; }
    DbSet<GoodsReceipt> GoodsReceipts { get; }
    DbSet<InventoryBalance> InventoryBalances { get; }
    DbSet<InventoryMovement> InventoryMovements { get; }
    DbSet<StorageLocation> StorageLocations { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}