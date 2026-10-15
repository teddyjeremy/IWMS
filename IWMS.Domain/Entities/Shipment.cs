using IWMS.Domain.Common;
using IWMS.Domain.Enums;

namespace IWMS.Domain.Entities;

public class Shipment : BaseEntity
{
    public string ShipmentNumber { get; set; } = string.Empty;
    public ShipmentStatus Status { get; set; } = ShipmentStatus.PickListCreated;
    public DateTime? DispatchedAtUtc { get; set; }
    public string Notes { get; set; } = string.Empty;

    public Guid SalesOrderId { get; set; }
    public SalesOrder SalesOrder { get; set; } = null!;

    public Guid? DispatchedByUserId { get; set; }
    public User? DispatchedByUser { get; set; }

    public List<ShipmentLine> Lines { get; set; } = new();
}