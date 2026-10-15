using IWMS.Domain.Common;
using IWMS.Domain.Enums;

namespace IWMS.Domain.Entities;

public class StockCountSession : BaseEntity
{
    public string CountNumber { get; set; } = string.Empty;
    public StockCountStatus Status { get; set; } = StockCountStatus.Draft;
    public string Notes { get; set; } = string.Empty;

    public DateTime? StartedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }

    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;

    public Guid CreatedByUserId { get; set; }
    public User CreatedByUser { get; set; } = null!;

    public Guid? ApprovedByUserId { get; set; }
    public User? ApprovedByUser { get; set; }

    public List<StockCountLine> Lines { get; set; } = new();
}