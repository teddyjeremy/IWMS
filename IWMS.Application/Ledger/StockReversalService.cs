using IWMS.Application.Audit;
using IWMS.Application.Common;
using IWMS.Domain.Common;
using IWMS.Domain.Entities;
using IWMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace IWMS.Application.Ledger;

public record ReversalResult(IReadOnlyList<Guid> ReversalMovementIds);

public class StockReversalService
{
    private readonly IAppDbContext _db;
    private readonly AuditLogger _audit;

    public StockReversalService(IAppDbContext db, AuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<ReversalResult> ReverseMovementAsync(
        Guid movementId, string reason, Guid performedByUserId, CancellationToken ct = default)
    {
        var original = await _db.InventoryMovements
            .SingleOrDefaultAsync(m => m.Id == movementId, ct)
            ?? throw new DomainException("Movement not found.");

        if (original.Type == MovementType.Receipt)
            throw new DomainException(
                "A goods receipt movement must be reversed through its receipt.");

        var originals = new List<InventoryMovement> { original };

        if (original.Type is MovementType.TransferOut or MovementType.TransferIn)
        {
            originals = await _db.InventoryMovements
                .Where(m => m.ReferenceNumber == original.ReferenceNumber
                            && (m.Type == MovementType.TransferOut || m.Type == MovementType.TransferIn))
                .ToListAsync(ct);
        }

        var reversals = await ReverseAllAsync(originals, reason, performedByUserId, ct);

        _audit.Record(
            "Movement.Reversed", nameof(InventoryMovement), original.Id, performedByUserId, reason,
            $"{original.Type} {original.ReferenceNumber}: {reversals.Count} reversal movement(s) posted");

        await _db.SaveChangesAsync(ct);

        return new ReversalResult(reversals.Select(r => r.Id).ToList());
    }

    public async Task<ReversalResult> ReverseReceiptAsync(
        Guid receiptId, string reason, Guid performedByUserId, CancellationToken ct = default)
    {
        var receipt = await _db.GoodsReceipts
            .Include(r => r.Lines)
            .Include(r => r.PurchaseOrder)
                .ThenInclude(o => o.Lines)
            .SingleOrDefaultAsync(r => r.Id == receiptId, ct)
            ?? throw new DomainException("Goods receipt not found.");

        var order = receipt.PurchaseOrder;
        var originals = new List<InventoryMovement>();

        foreach (var line in receipt.Lines.Where(l => l.QuantityAccepted > 0))
        {
            var original = await _db.InventoryMovements
                .SingleOrDefaultAsync(m => m.SourceLineId == line.Id && m.Type == MovementType.Receipt, ct)
                ?? throw new DomainException(
                    "This receipt predates movement traceability and cannot be reversed. Use a stock adjustment instead.");

            originals.Add(original);

            var orderLine = order.Lines.Single(l => l.Id == line.PurchaseOrderLineId);
            orderLine.QuantityReceived -= line.QuantityAccepted;
        }

        if (originals.Count == 0)
            throw new DomainException("This receipt accepted no stock, so there is nothing to reverse.");

        var reversals = await ReverseAllAsync(originals, reason, performedByUserId, ct);

        if (order.Status is PurchaseOrderStatus.Received or PurchaseOrderStatus.PartiallyReceived)
        {
            if (order.Lines.All(l => l.QuantityReceived <= 0))
                order.Status = PurchaseOrderStatus.Approved;
            else if (order.Lines.All(l => l.QuantityOutstanding <= 0))
                order.Status = PurchaseOrderStatus.Received;
            else
                order.Status = PurchaseOrderStatus.PartiallyReceived;

            order.UpdatedAtUtc = DateTime.UtcNow;
        }

        _audit.Record(
            "Receipt.Reversed", nameof(GoodsReceipt), receipt.Id, performedByUserId, reason,
            $"{receipt.ReceiptNumber} on {order.OrderNumber}: {reversals.Count} movement(s) reversed");

        await _db.SaveChangesAsync(ct);

        return new ReversalResult(reversals.Select(r => r.Id).ToList());
    }

    private async Task<List<InventoryMovement>> ReverseAllAsync(
        IEnumerable<InventoryMovement> originals, string reason, Guid performedByUserId, CancellationToken ct)
    {
        var reversals = new List<InventoryMovement>();

        foreach (var original in originals)
        {
            if (await _db.InventoryMovements.AnyAsync(m => m.ReversesMovementId == original.Id, ct))
                throw new DomainException("This movement has already been reversed.");

            var balance = await _db.FindBalanceAsync(original.ProductId, original.StorageLocationId, ct)
                ?? throw new DomainException("The stock balance for this movement no longer exists.");

            var reversal = balance.PostReversal(original, performedByUserId, reason);

            _db.InventoryMovements.Add(reversal);
            reversals.Add(reversal);
        }

        return reversals;
    }
}