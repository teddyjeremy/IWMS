using IWMS.Domain.Common;
using IWMS.Domain.Enums;

namespace IWMS.Domain.Entities;

public class ReturnRecord : BaseEntity
{
    public string ReturnNumber { get; set; } = string.Empty;
    public ReturnType Type { get; set; }
    public DateTime ReturnedAtUtc { get; set; } = DateTime.UtcNow;
    public string Notes { get; set; } = string.Empty;

    public Guid? SalesOrderId { get; set; }
    public SalesOrder? SalesOrder { get; set; }

    public Guid? GoodsReceiptId { get; set; }
    public GoodsReceipt? GoodsReceipt { get; set; }

    public Guid ProcessedByUserId { get; set; }
    public User ProcessedByUser { get; set; } = null!;

    public List<ReturnRecordLine> Lines { get; set; } = new();
}