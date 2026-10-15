using IWMS.Domain.Common;

namespace IWMS.Domain.Entities;

public class StorageLocation : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public Guid WarehouseId { get; set; }
    public Warehouse Warehouse { get; set; } = null!;
}