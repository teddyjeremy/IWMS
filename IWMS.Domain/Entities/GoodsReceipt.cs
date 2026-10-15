using IWMS.Domain.Common;

namespace IWMS.Domain.Entities;

public class GoodsReceipt : BaseEntity
{
    public string ReceiptNumber { get; set; } = string.Empty;
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
    public string Notes { get; set; } = string.Empty;

    public Guid PurchaseOrderId { get; set; }
    public PurchaseOrder PurchaseOrder { get; set; } = null!;

    public Guid ReceivedByUserId { get; set; }
    public User ReceivedByUser { get; set; } = null!;

    public List<GoodsReceiptLine> Lines { get; set; } = new();
    public Guid IdempotencyKey { get; set; }
}