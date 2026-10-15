using IWMS.Domain.Common;

namespace IWMS.Domain.Entities;

public class InventoryBalance : BaseEntity
{
    public decimal OnHand { get; set; }
    public decimal Reserved { get; set; }

    public decimal Available => OnHand - Reserved;

    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public Guid StorageLocationId { get; set; }
    public StorageLocation StorageLocation { get; set; } = null!;
}