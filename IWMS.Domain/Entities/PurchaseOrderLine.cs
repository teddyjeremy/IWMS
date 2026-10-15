using IWMS.Domain.Common;

namespace IWMS.Domain.Entities;

public class PurchaseOrderLine : BaseEntity
{
    public decimal QuantityOrdered { get; set; }
    public decimal QuantityReceived { get; set; }
    public decimal ExpectedUnitPrice { get; set; }

    public decimal QuantityOutstanding => QuantityOrdered - QuantityReceived;
    public decimal LineTotal => QuantityOrdered * ExpectedUnitPrice;

    public Guid PurchaseOrderId { get; set; }
    public PurchaseOrder PurchaseOrder { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;
}