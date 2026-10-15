using IWMS.Domain.Common;
using IWMS.Domain.Enums;

namespace IWMS.Domain.Entities;

public class SalesOrder : BaseEntity
{
    public string OrderNumber { get; set; } = string.Empty;
    public SalesOrderStatus Status { get; set; } = SalesOrderStatus.Draft;
    public DateTime? RequiredDateUtc { get; set; }
    public string Notes { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;

    public Guid CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;

    public List<SalesOrderLine> Lines { get; set; } = new();
    public List<Shipment> Shipments { get; set; } = new();
}