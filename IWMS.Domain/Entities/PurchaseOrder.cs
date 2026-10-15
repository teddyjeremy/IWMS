using IWMS.Domain.Common;
using IWMS.Domain.Enums;

namespace IWMS.Domain.Entities;

public class PurchaseOrder : BaseEntity
{
    public string OrderNumber { get; set; } = string.Empty;
    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;
    public DateTime? ExpectedDeliveryUtc { get; set; }
    public string Notes { get; set; } = string.Empty;

    public DateTime? ApprovedAtUtc { get; set; }
    public string? RejectionReason { get; set; }

    public Guid SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public Guid CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;

    public Guid? ApprovedByUserId { get; set; }
    public User? ApprovedByUser { get; set; }

    public List<PurchaseOrderLine> Lines { get; set; } = new();
    public List<GoodsReceipt> Receipts { get; set; } = new();
}