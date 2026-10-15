using IWMS.Domain.Common;

namespace IWMS.Domain.Entities;

public class SalesOrderLine : BaseEntity
{
    public decimal QuantityOrdered { get; set; }
    public decimal QuantityReserved { get; set; }
    public decimal QuantityPicked { get; set; }
    public decimal QuantityDispatched { get; set; }
    public decimal UnitPrice { get; set; }

    public decimal QuantityOutstanding => QuantityOrdered - QuantityDispatched;
    public decimal LineTotal => QuantityOrdered * UnitPrice;

    public Guid SalesOrderId { get; set; }
    public SalesOrder SalesOrder { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public List<Reservation> Reservations { get; set; } = new();
}