using IWMS.Domain.Common;

namespace IWMS.Domain.Entities;

public class StockCountLine : BaseEntity
{
    public decimal SystemQuantity { get; set; }
    public decimal? CountedQuantity { get; set; }
    public string VarianceReason { get; set; } = string.Empty;

    public decimal? Variance => CountedQuantity - SystemQuantity;

    public Guid StockCountSessionId { get; set; }
    public StockCountSession StockCountSession { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public Guid StorageLocationId { get; set; }
    public StorageLocation StorageLocation { get; set; } = null!;

    public Guid? CountedByUserId { get; set; }
    public User? CountedByUser { get; set; }

    public Guid? PostedMovementId { get; set; }
    public InventoryMovement? PostedMovement { get; set; }
}