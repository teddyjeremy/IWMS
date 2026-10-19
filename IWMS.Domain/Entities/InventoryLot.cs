using IWMS.Domain.Common;

namespace IWMS.Domain.Entities;

public class InventoryLot : BaseEntity
{
    public string LotNumber { get; set; } = string.Empty;
    public DateTime? ExpiryUtc { get; set; }
    public decimal QuantityOnHand { get; set; }

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public Guid StorageLocationId { get; set; }
    public StorageLocation StorageLocation { get; set; } = null!;
}
