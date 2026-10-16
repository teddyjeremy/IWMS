using IWMS.Domain.Common;
using IWMS.Domain.Enums;

namespace IWMS.Domain.Entities;

public class InventoryMovement : BaseEntity
{
    public MovementType Type { get; init; }
    public decimal QuantityChange { get; init; }
    public decimal QuantityBefore { get; init; }
    public decimal QuantityAfter { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string ReferenceNumber { get; init; } = string.Empty;
    public Guid? IdempotencyKey { get; init; }

    public Guid? ReversesMovementId { get; init; }

    public Guid ProductId { get; init; }
    public Product Product { get; init; } = null!;

    public Guid StorageLocationId { get; init; }
    public StorageLocation StorageLocation { get; init; } = null!;

    public Guid PerformedByUserId { get; init; }
    public User PerformedByUser { get; init; } = null!;
}