using IWMS.Domain.Common;

namespace IWMS.Domain.Entities;

public class ShipmentLine : BaseEntity
{
    public decimal QuantityToPick { get; set; }
    public decimal QuantityPicked { get; set; }

    public Guid ShipmentId { get; set; }
    public Shipment Shipment { get; set; } = null!;

    public Guid SalesOrderLineId { get; set; }
    public SalesOrderLine SalesOrderLine { get; set; } = null!;

    public Guid StorageLocationId { get; set; }
    public StorageLocation StorageLocation { get; set; } = null!;
}