using IWMS.Domain.Common;

namespace IWMS.Domain.Entities;

public class GoodsReceiptLine : BaseEntity
{
    public decimal QuantityAccepted { get; set; }
    public decimal QuantityRejected { get; set; }
    public string DiscrepancyNote { get; set; } = string.Empty;

    public Guid GoodsReceiptId { get; set; }
    public GoodsReceipt GoodsReceipt { get; set; } = null!;

    public Guid PurchaseOrderLineId { get; set; }
    public PurchaseOrderLine PurchaseOrderLine { get; set; } = null!;

    public Guid StorageLocationId { get; set; }
    public StorageLocation StorageLocation { get; set; } = null!;
}