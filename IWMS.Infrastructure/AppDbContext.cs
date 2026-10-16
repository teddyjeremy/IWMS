using IWMS.Domain.Common;
using IWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using IWMS.Application;

namespace IWMS.Infrastructure;

public class AppDbContext : DbContext, IAppDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<StorageLocation> StorageLocations => Set<StorageLocation>();
    public DbSet<InventoryBalance> InventoryBalances => Set<InventoryBalance>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();
    public DbSet<GoodsReceiptLine> GoodsReceiptLines => Set<GoodsReceiptLine>();
    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();
    public DbSet<SalesOrderLine> SalesOrderLines => Set<SalesOrderLine>();
    public DbSet<Reservation> Reservations => Set<Reservation>();
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<ShipmentLine> ShipmentLines => Set<ShipmentLine>();
    public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();
    public DbSet<StockTransferLine> StockTransferLines => Set<StockTransferLine>();
    public DbSet<StockCountSession> StockCountSessions => Set<StockCountSession>();
    public DbSet<StockCountLine> StockCountLines => Set<StockCountLine>();
    public DbSet<ReturnRecord> ReturnRecords => Set<ReturnRecord>();
    public DbSet<ReturnRecordLine> ReturnRecordLines => Set<ReturnRecordLine>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<decimal>().HavePrecision(18, 4);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var ourEntities = modelBuilder.Model.GetEntityTypes()
            .Where(t => typeof(BaseEntity).IsAssignableFrom(t.ClrType))
            .ToList();

        foreach (var entityType in ourEntities)
        {
            foreach (var foreignKey in entityType.GetForeignKeys())
            {
                foreignKey.DeleteBehavior = DeleteBehavior.Restrict;
            }

            modelBuilder.Entity(entityType.ClrType)
                .Property(nameof(BaseEntity.Version))
                .IsRowVersion();
        }

        modelBuilder.Entity<User>(e =>
        {
            e.Property(u => u.Email).HasMaxLength(256);
            e.HasIndex(u => u.Email).IsUnique();
        });

        modelBuilder.Entity<Role>().HasIndex(r => r.Name).IsUnique();

        modelBuilder.Entity<Product>(e =>
        {
            e.Property(p => p.Sku).HasMaxLength(50);
            e.HasIndex(p => p.Sku).IsUnique();
        });

        modelBuilder.Entity<Warehouse>().HasIndex(w => w.Code).IsUnique();

        modelBuilder.Entity<StorageLocation>()
            .HasIndex(l => new { l.WarehouseId, l.Code }).IsUnique();

        modelBuilder.Entity<InventoryBalance>()
            .HasIndex(b => new { b.ProductId, b.StorageLocationId }).IsUnique();

        modelBuilder.Entity<PurchaseOrder>().HasIndex(o => o.OrderNumber).IsUnique();
        modelBuilder.Entity<SalesOrder>().HasIndex(o => o.OrderNumber).IsUnique();
        modelBuilder.Entity<GoodsReceipt>().HasIndex(r => r.IdempotencyKey).IsUnique();
        modelBuilder.Entity<InventoryMovement>().HasIndex(m => new { m.IdempotencyKey, m.Type }).IsUnique();
    }
}