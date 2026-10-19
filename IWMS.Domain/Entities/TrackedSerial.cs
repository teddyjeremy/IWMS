using IWMS.Domain.Common;
using IWMS.Domain.Enums;

namespace IWMS.Domain.Entities;

public class TrackedSerial : BaseEntity
{
    public string Serial { get; set; } = string.Empty;
    public SerialStatus Status { get; set; } = SerialStatus.InStock;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public Guid? StorageLocationId { get; set; }
    public StorageLocation? StorageLocation { get; set; }
}
