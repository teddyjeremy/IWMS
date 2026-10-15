using IWMS.Domain.Common;

namespace IWMS.Domain.Entities;

public class StockTransferLine : BaseEntity
{
    public decimal QuantityRequested { get; set; }
    public decimal QuantityDispatched { get; set; }
    public decimal QuantityReceived { get; set; }
    public string DiscrepancyNote { get; set; } = string.Empty;

    public decimal QuantityInTransit => QuantityDispatched - QuantityReceived;

    public Guid StockTransferId { get; set; }
    public StockTransfer StockTransfer { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public Guid FromStorageLocationId { get; set; }
    public StorageLocation FromStorageLocation { get; set; } = null!;

    public Guid ToStorageLocationId { get; set; }
    public StorageLocation ToStorageLocation { get; set; } = null!;
}