using IWMS.Domain.Common;
using System.Security.Cryptography.X509Certificates;

namespace IWMS.Domain.Entities;

public class Warehouse : BaseEntity
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public List<StorageLocation> StorageLocations { get; set; } = new();
}