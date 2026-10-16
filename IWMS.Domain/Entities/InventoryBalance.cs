using IWMS.Domain.Common;
using IWMS.Domain.Enums;

namespace IWMS.Domain.Entities;

public class InventoryBalance : BaseEntity
{
    public decimal OnHand { get; set; }
    public decimal Reserved { get; set; }

    public decimal Available => OnHand - Reserved;

    public Guid ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public Guid StorageLocationId { get; set; }
    public StorageLocation StorageLocation { get; set; } = null!;

    public InventoryMovement Post(
    MovementType type,
    decimal quantity,
    Guid performedByUserId,
    string reason,
    string referenceNumber,
    Guid? idempotencyKey = null
        )
    {
        if (quantity <= 0)
            throw new DomainException("Quantity must be greater than zero.");

        if (type is MovementType.AdjustmentIncrease or MovementType.AdjustmentDecrease
            && string.IsNullOrWhiteSpace(reason))
            throw new DomainException("An adjustment requires a reason.");

        var change = type switch
        {
            MovementType.Receipt or MovementType.TransferIn
                or MovementType.AdjustmentIncrease or MovementType.ReturnRestock => quantity,
            MovementType.Dispatch or MovementType.TransferOut
                or MovementType.AdjustmentDecrease => -quantity,
            _ => throw new DomainException($"Movement type {type} cannot be posted this way.")
        };

        if (change < 0 && -change > Available)
            throw new DomainException(
                $"Insufficient available stock. Available: {Available}, requested: {quantity}.");

        var before = OnHand;
        OnHand += change;
        UpdatedAtUtc = DateTime.UtcNow;

        return new InventoryMovement
        {
            Type = type,
            QuantityChange = change,
            QuantityBefore = before,
            QuantityAfter = OnHand,
            Reason = reason,
            ReferenceNumber = referenceNumber,
            IdempotencyKey = idempotencyKey,
            ProductId = ProductId,
            StorageLocationId = StorageLocationId,
            PerformedByUserId = performedByUserId
        };
    }
}