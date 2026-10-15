using IWMS.Domain.Common;

namespace IWMS.Domain.Entities;

public class Reservation : BaseEntity
{
    public decimal Quantity { get; set; }
    public DateTime? ReleasedAtUtc { get; set; }

    public bool IsActive => ReleasedAtUtc is null;

    public Guid SalesOrderLineId { get; set; }
    public SalesOrderLine SalesOrderLine { get; set; } = null!;

    public Guid StorageLocationId { get; set; }
    public StorageLocation StorageLocation { get; set; } = null!;
}