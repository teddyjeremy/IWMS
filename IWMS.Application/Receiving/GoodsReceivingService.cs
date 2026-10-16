using IWMS.Domain.Common;
using IWMS.Domain.Entities;
using IWMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using IWMS.Application.Common;
using IWMS.Application.Audit;

namespace IWMS.Application.Receiving;

public class GoodsReceivingService
{
    private readonly IAppDbContext _db;
    private readonly AuditLogger _audit;

    public GoodsReceivingService(IAppDbContext db, AuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<ReceiptResult> ReceiveAsync(
        ReceiveGoodsRequest request, CancellationToken ct = default)
    {
        var existing = await _db.GoodsReceipts
            .FirstOrDefaultAsync(r => r.IdempotencyKey == request.IdempotencyKey, ct);
        if (existing is not null)
            return new ReceiptResult(existing.Id, existing.ReceiptNumber, WasDuplicate: true);

        if (request.Lines.Count == 0)
            throw new DomainException("A receipt needs at least one line.");

        var order = await _db.PurchaseOrders
            .Include(o => o.Lines)
            .SingleOrDefaultAsync(o => o.Id == request.PurchaseOrderId, ct)
            ?? throw new DomainException("Purchase order not found.");

        if (order.Status is not (PurchaseOrderStatus.Approved or PurchaseOrderStatus.PartiallyReceived))
            throw new DomainException($"Goods cannot be received against an order in status {order.Status}.");

        var receipt = new GoodsReceipt
        {
            ReceiptNumber = NewReceiptNumber(),
            IdempotencyKey = request.IdempotencyKey,
            PurchaseOrderId = order.Id,
            ReceivedByUserId = request.ReceivedByUserId,
            Notes = request.Notes
        };

        foreach (var line in request.Lines)
        {
            if (line.QuantityAccepted < 0 || line.QuantityRejected < 0)
                throw new DomainException("Quantities cannot be negative.");
            if (line.QuantityAccepted == 0 && line.QuantityRejected == 0)
                throw new DomainException("Each receipt line must accept or reject some quantity.");

            var orderLine = order.Lines.SingleOrDefault(l => l.Id == line.PurchaseOrderLineId)
                ?? throw new DomainException("A receipt line does not belong to this purchase order.");

            var locationIsActive = await _db.StorageLocations
                .AnyAsync(l => l.Id == line.StorageLocationId && l.IsActive, ct);
            if (!locationIsActive)
                throw new DomainException("Storage location not found or inactive.");

            var isOverReceipt = line.QuantityAccepted > orderLine.QuantityOutstanding;

            if (isOverReceipt && !request.AllowOverReceipt)
                throw new DomainException(
                    $"Accepted quantity {line.QuantityAccepted} exceeds the outstanding {orderLine.QuantityOutstanding}.");

            if (isOverReceipt)
                _audit.Record(
                    "PurchaseOrder.OverReceived", nameof(PurchaseOrder), order.Id,
                    request.ReceivedByUserId, "Over-receipt permitted",
                    $"Accepted {line.QuantityAccepted}, outstanding was {orderLine.QuantityOutstanding}");
            var receiptLine = new GoodsReceiptLine
            {
                PurchaseOrderLineId = orderLine.Id,
                StorageLocationId = line.StorageLocationId,
                QuantityAccepted = line.QuantityAccepted,
                QuantityRejected = line.QuantityRejected,
                DiscrepancyNote = line.DiscrepancyNote
            };
            receipt.Lines.Add(receiptLine);

            orderLine.QuantityReceived += line.QuantityAccepted;

            if (line.QuantityAccepted > 0)
            {
                var balance = await _db.GetOrCreateBalanceAsync(orderLine.ProductId, line.StorageLocationId, ct);

                var movement = balance.Post(
                 MovementType.Receipt,
                 line.QuantityAccepted,
                 request.ReceivedByUserId,
                 "Goods receipt",
                 receipt.ReceiptNumber,
                 sourceLineId: receiptLine.Id);

                _db.InventoryMovements.Add(movement);
            }
        }

        order.Status = order.Lines.All(l => l.QuantityOutstanding <= 0)
            ? PurchaseOrderStatus.Received
            : PurchaseOrderStatus.PartiallyReceived;

        _db.GoodsReceipts.Add(receipt);
        await _db.SaveChangesAsync(ct);

        return new ReceiptResult(receipt.Id, receipt.ReceiptNumber, WasDuplicate: false);
    }

    private async Task<InventoryBalance> GetOrCreateBalanceAsync(
        Guid productId, Guid storageLocationId, CancellationToken ct)
    {
        var balance = _db.InventoryBalances.Local
            .FirstOrDefault(b => b.ProductId == productId && b.StorageLocationId == storageLocationId)
            ?? await _db.InventoryBalances
                .FirstOrDefaultAsync(b => b.ProductId == productId && b.StorageLocationId == storageLocationId, ct);

        if (balance is null)
        {
            balance = new InventoryBalance
            {
                ProductId = productId,
                StorageLocationId = storageLocationId
            };
            _db.InventoryBalances.Add(balance);
        }

        return balance;
    }

    private static string NewReceiptNumber() =>
        $"GR-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
}