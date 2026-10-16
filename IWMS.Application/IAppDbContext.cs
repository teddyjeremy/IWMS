using IWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace IWMS.Application;

public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<Category> Categories { get; }
    DbSet<Product> Products { get; }
    DbSet<Warehouse> Warehouses { get; }
    DbSet<StorageLocation> StorageLocations { get; }
    DbSet<PurchaseOrder> PurchaseOrders { get; }
    DbSet<GoodsReceipt> GoodsReceipts { get; }
    DbSet<InventoryBalance> InventoryBalances { get; }
    DbSet<InventoryMovement> InventoryMovements { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}