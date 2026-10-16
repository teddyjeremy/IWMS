
using IWMS.Application.Common;
using IWMS.Domain.Common;
using IWMS.Domain.Entities;
using IWMS.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using IWMS.Application.Audit;

namespace IWMS.Application.Ledger;

public class StockLedgerService
{
    private readonly IAppDbContext _db;
    private readonly AuditLogger _audit;

    public StockLedgerService(IAppDbContext db, AuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    public async Task<StockPostingResult> IssueAsync(
        IssueStockRequest request,
        CancellationToken ct = default)
    {
        var duplicate = await FindDuplicateAsync(
            request.IdempotencyKey, ct);

        if (duplicate is not null)
            return duplicate;

        await EnsureProductIsActiveAsync(request.ProductId, ct);
        await EnsureLocationIsActiveAsync(request.StorageLocationId, ct);

        var balance = await _db.FindBalanceAsync(
            request.ProductId,
            request.StorageLocationId,
            ct)
            ?? throw new DomainException(
                "There is no stock of this product at this location.");

        var reference = string.IsNullOrWhiteSpace(request.ReferenceNumber)
            ? NewReference("ISS")
            : request.ReferenceNumber;

        var type = MovementType.Dispatch;

        var movement = balance.Post(
            type,
            request.Quantity,
            request.PerformedByUserId,
            request.Reason,
            reference,
            request.IdempotencyKey);

        _audit.Record(
            "Stock.Issued",
            nameof(InventoryMovement),
            movement.Id,
            request.PerformedByUserId,
            request.Reason,
            $"{type}: {movement.QuantityBefore} -> {movement.QuantityAfter} " +
            $"(product {request.ProductId}, location {request.StorageLocationId})");

        _db.InventoryMovements.Add(movement);

        await _db.SaveChangesAsync(ct);

        return new StockPostingResult(
            movement.Id,
            movement.ReferenceNumber,
            WasDuplicate: false);
    }

    public async Task<StockPostingResult> AdjustAsync(
        AdjustStockRequest request,
        CancellationToken ct = default)
    {
        var duplicate = await FindDuplicateAsync(
            request.IdempotencyKey, ct);

        if (duplicate is not null)
            return duplicate;

        await EnsureProductIsActiveAsync(request.ProductId, ct);
        await EnsureLocationIsActiveAsync(request.StorageLocationId, ct);

        InventoryBalance balance;

        if (request.IsIncrease)
        {
            balance = await _db.GetOrCreateBalanceAsync(
                request.ProductId,
                request.StorageLocationId,
                ct);
        }
        else
        {
            balance = await _db.FindBalanceAsync(
                request.ProductId,
                request.StorageLocationId,
                ct)
                ?? throw new DomainException(
                    "There is no stock of this product at this location.");
        }

        var type = request.IsIncrease
            ? MovementType.AdjustmentIncrease
            : MovementType.AdjustmentDecrease;

        var movement = balance.Post(
            type,
            request.Quantity,
            request.PerformedByUserId,
            request.Reason,
            NewReference("ADJ"),
            request.IdempotencyKey);

        _audit.Record(
            "Stock.Adjusted",
            nameof(InventoryMovement),
            movement.Id,
            request.PerformedByUserId,
            request.Reason,
            $"{type}: {movement.QuantityBefore} -> {movement.QuantityAfter} " +
            $"(product {request.ProductId}, location {request.StorageLocationId})");

        _db.InventoryMovements.Add(movement);

        await _db.SaveChangesAsync(ct);

        return new StockPostingResult(
            movement.Id,
            movement.ReferenceNumber,
            WasDuplicate: false);
    }

    public async Task<StockPostingResult> TransferAsync(
        TransferStockRequest request,
        CancellationToken ct = default)
    {
        var duplicate = await FindDuplicateAsync(
            request.IdempotencyKey, ct);

        if (duplicate is not null)
            return duplicate;

        if (request.FromStorageLocationId == request.ToStorageLocationId)
        {
            throw new DomainException(
                "The source and destination locations must be different.");
        }

        await EnsureProductIsActiveAsync(request.ProductId, ct);
        await EnsureLocationIsActiveAsync(
            request.FromStorageLocationId, ct);
        await EnsureLocationIsActiveAsync(
            request.ToStorageLocationId, ct);

        var source = await _db.FindBalanceAsync(
            request.ProductId,
            request.FromStorageLocationId,
            ct)
            ?? throw new DomainException(
                "There is no stock of this product at the source location.");

        var destination = await _db.GetOrCreateBalanceAsync(
            request.ProductId,
            request.ToStorageLocationId,
            ct);

        var reference = NewReference("TRF");

        var outMovement = source.Post(
            MovementType.TransferOut,
            request.Quantity,
            request.PerformedByUserId,
            request.Reason,
            reference,
            request.IdempotencyKey);

        var inMovement = destination.Post(
            MovementType.TransferIn,
            request.Quantity,
            request.PerformedByUserId,
            request.Reason,
            reference,
            request.IdempotencyKey);

        _db.InventoryMovements.AddRange(
            outMovement,
            inMovement);

        await _db.SaveChangesAsync(ct);

        return new StockPostingResult(
            outMovement.Id,
            reference,
            WasDuplicate: false);
    }

    private async Task<StockPostingResult?> FindDuplicateAsync(
        Guid idempotencyKey,
        CancellationToken ct)
    {
        var existing = await _db.InventoryMovements
            .Where(m => m.IdempotencyKey == idempotencyKey)
            .OrderBy(m => m.Type)
            .FirstOrDefaultAsync(ct);

        return existing is null
            ? null
            : new StockPostingResult(
                existing.Id,
                existing.ReferenceNumber,
                WasDuplicate: true);
    }

    private async Task EnsureProductIsActiveAsync(
        Guid productId,
        CancellationToken ct)
    {
        var isActive = await _db.Products.AnyAsync(
            p => p.Id == productId && p.IsActive,
            ct);

        if (!isActive)
        {
            throw new DomainException(
                "Product not found or inactive.");
        }
    }

    private async Task EnsureLocationIsActiveAsync(
        Guid locationId,
        CancellationToken ct)
    {
        var isActive = await _db.StorageLocations.AnyAsync(
            l => l.Id == locationId && l.IsActive,
            ct);

        if (!isActive)
        {
            throw new DomainException(
                "Storage location not found or inactive.");
        }
    }

    private static string NewReference(string prefix) =>
        $"{prefix}-{DateTime.UtcNow:yyyyMMdd}-" +
        $"{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
}
