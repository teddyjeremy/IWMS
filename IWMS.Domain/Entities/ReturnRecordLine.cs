using IWMS.Domain.Common;
using IWMS.Domain.Enums;

namespace IWMS.Domain.Entities;

public class ReturnRecordLine : BaseEntity
{
    public decimal Quantity { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
    public ReturnDisposition Disposition { get; set; }

    public Guid ReturnRecordId { get; set; }
    public ReturnRecord ReturnRecord { get; set; } = null!;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public Guid? StorageLocationId { get; set; }
    public StorageLocation? StorageLocation { get; set; }
}