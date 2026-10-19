using IWMS.Domain.Common;

namespace IWMS.Domain.Entities;

public class TransferLotLine : BaseEntity
{
    public string LotNumber { get; set; } = string.Empty;
    public DateTime? ExpiryUtc { get; set; }
    public decimal Quantity { get; set; }

    public Guid StockTransferLineId { get; set; }
    public StockTransferLine StockTransferLine { get; set; } = null!;
}
