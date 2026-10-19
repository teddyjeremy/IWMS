using IWMS.Api.Security;
using IWMS.Application.Audit;
using IWMS.Domain.Common;
using IWMS.Domain.Entities;
using IWMS.Domain.Enums;
using IWMS.Application.Tracking;
using IWMS.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IWMS.Api.Controllers;

[ApiController]
[Route("api/stock-transfers")]
[Authorize(Policy = Policies.ViewData)]
public sealed class StockTransfersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditLogger _audit;
    private readonly LotTrackingService _lots;

    public StockTransfersController(AppDbContext db, AuditLogger audit, LotTrackingService lots)
    {
        _db = db;
        _audit = audit;
        _lots = lots;
    }

    [HttpGet]
    public async Task<ActionResult> List([FromQuery] TransferStatus? status, CancellationToken ct)
    {
        var query = _db.StockTransfers.AsNoTracking();
        if (status.HasValue) query = query.Where(t => t.Status == status.Value);

        return Ok(await query.OrderByDescending(t => t.CreatedAtUtc)
            .Select(t => new
            {
                t.Id, t.TransferNumber, t.Status, t.Reason,
                t.SourceWarehouseId, SourceWarehouse = t.SourceWarehouse.Name,
                t.DestinationWarehouseId, DestinationWarehouse = t.DestinationWarehouse.Name,
                t.RequestedByUserId, t.ApprovedAtUtc, t.DispatchedAtUtc, t.ReceivedAtUtc,
                LineCount = t.Lines.Count,
                RequestedUnits = t.Lines.Sum(l => l.QuantityRequested),
                DispatchedUnits = t.Lines.Sum(l => l.QuantityDispatched),
                ReceivedUnits = t.Lines.Sum(l => l.QuantityReceived)
            }).ToListAsync(ct));
    }

    [HttpGet("{transferId:guid}")]
    public async Task<ActionResult> Get(Guid transferId, CancellationToken ct)
    {
        var transfer = await _db.StockTransfers.AsNoTracking().Where(t => t.Id == transferId)
            .Select(t => new
            {
                t.Id, t.TransferNumber, t.Status, t.Reason, t.RejectionReason,
                t.SourceWarehouseId, SourceWarehouse = t.SourceWarehouse.Name,
                t.DestinationWarehouseId, DestinationWarehouse = t.DestinationWarehouse.Name,
                t.RequestedByUserId, t.ApprovedByUserId, t.ApprovedAtUtc, t.DispatchedAtUtc, t.ReceivedAtUtc,
                Lines = t.Lines.OrderBy(l => l.Product.Sku).Select(l => new
                {
                    l.Id, l.ProductId, l.Product.Sku, ProductName = l.Product.Name,
                    l.FromStorageLocationId, FromLocation = l.FromStorageLocation.Code,
                    l.ToStorageLocationId, ToLocation = l.ToStorageLocation.Code,
                    l.QuantityRequested, l.QuantityDispatched, l.QuantityReceived,
                    l.QuantityInTransit, l.DiscrepancyNote
                }).ToList()
            }).SingleOrDefaultAsync(ct);

        return transfer is null ? NotFound() : Ok(transfer);
    }

    [HttpPost]
    [Authorize(Policy = Policies.PostTransfer)]
    public async Task<ActionResult> Create(CreateStockTransferRequest request, CancellationToken ct)
    {
        if (request.SourceWarehouseId == request.DestinationWarehouseId)
            throw new DomainException("Source and destination warehouses must be different.");
        if (request.Lines is null || request.Lines.Count == 0)
            throw new DomainException("A transfer must contain at least one line.");
        if (request.Lines.Any(l => l.Quantity <= 0))
            throw new DomainException("Transfer quantities must be greater than zero.");
        if (request.Reason?.Length > 1000)
            throw new DomainException("Transfer reason must be 1000 characters or fewer.");
        if (request.Lines.Select(l => (l.ProductId, l.FromStorageLocationId, l.ToStorageLocationId)).Distinct().Count() != request.Lines.Count)
            throw new DomainException("Duplicate transfer lines are not allowed.");

        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var source = await _db.Warehouses.SingleOrDefaultAsync(w => w.Id == request.SourceWarehouseId && w.IsActive, ct);
        var destination = await _db.Warehouses.SingleOrDefaultAsync(w => w.Id == request.DestinationWarehouseId && w.IsActive, ct);
        if (source is null || destination is null) return NotFound("Source or destination warehouse was not found or is inactive.");

        var locationIds = request.Lines.SelectMany(l => new[] { l.FromStorageLocationId, l.ToStorageLocationId }).Distinct().ToList();
        var locations = await _db.StorageLocations.Where(l => locationIds.Contains(l.Id) && l.IsActive).ToListAsync(ct);
        foreach (var line in request.Lines)
        {
            var from = locations.SingleOrDefault(l => l.Id == line.FromStorageLocationId);
            var to = locations.SingleOrDefault(l => l.Id == line.ToStorageLocationId);
            if (from is null || to is null)
                throw new DomainException("Every transfer line must use active storage locations.");
            if (from.WarehouseId != source.Id || to.WarehouseId != destination.Id)
                throw new DomainException("Transfer locations must belong to their selected source and destination warehouses.");
            if (line.FromStorageLocationId == line.ToStorageLocationId)
                throw new DomainException("A transfer line must move stock to a different location.");
            if (!await _db.Products.AnyAsync(p => p.Id == line.ProductId && p.IsActive, ct))
                throw new DomainException($"Product {line.ProductId} was not found or is inactive.");
        }

        var transfer = new StockTransfer
        {
            TransferNumber = $"TR-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..20],
            SourceWarehouseId = source.Id,
            DestinationWarehouseId = destination.Id,
            RequestedByUserId = User.GetUserId(),
            Reason = request.Reason?.Trim() ?? string.Empty,
            Status = TransferStatus.PendingApproval,
            Lines = request.Lines.Select(l => new StockTransferLine
            {
                ProductId = l.ProductId,
                FromStorageLocationId = l.FromStorageLocationId,
                ToStorageLocationId = l.ToStorageLocationId,
                QuantityRequested = l.Quantity
            }).ToList()
        };

        _db.StockTransfers.Add(transfer);
        _audit.Record("StockTransfer.Submitted", nameof(StockTransfer), transfer.Id, User.GetUserId(),
            details: $"{transfer.TransferNumber}; {transfer.Lines.Count} lines.");
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return CreatedAtAction(nameof(Get), new { transferId = transfer.Id },
            new { transfer.Id, transfer.TransferNumber, transfer.Status, LineCount = transfer.Lines.Count });
    }

    [HttpPost("{transferId:guid}/approve")]
    [Authorize(Policy = Policies.PostAdjustment)]
    public async Task<ActionResult> Approve(Guid transferId, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var transfer = await _db.StockTransfers.SingleOrDefaultAsync(t => t.Id == transferId, ct);
        if (transfer is null) return NotFound();
        if (transfer.Status != TransferStatus.PendingApproval)
            throw new DomainException("Only transfers pending approval can be approved.");
        transfer.Status = TransferStatus.Approved;
        transfer.ApprovedAtUtc = DateTime.UtcNow;
        transfer.ApprovedByUserId = User.GetUserId();
        transfer.UpdatedAtUtc = DateTime.UtcNow;
        _audit.Record("StockTransfer.Approved", nameof(StockTransfer), transfer.Id, User.GetUserId(),
            details: transfer.TransferNumber);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Ok(new { transfer.Id, transfer.TransferNumber, transfer.Status, transfer.ApprovedAtUtc });
    }

    [HttpPost("{transferId:guid}/reject")]
    [Authorize(Policy = Policies.PostAdjustment)]
    public async Task<ActionResult> Reject(Guid transferId, RejectStockTransferRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 500)
            throw new DomainException("A rejection reason of 1–500 characters is required.");
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var transfer = await _db.StockTransfers.SingleOrDefaultAsync(t => t.Id == transferId, ct);
        if (transfer is null) return NotFound();
        if (transfer.Status != TransferStatus.PendingApproval)
            throw new DomainException("Only transfers pending approval can be rejected.");
        transfer.Status = TransferStatus.Rejected;
        transfer.RejectionReason = request.Reason.Trim();
        transfer.UpdatedAtUtc = DateTime.UtcNow;
        _audit.Record("StockTransfer.Rejected", nameof(StockTransfer), transfer.Id, User.GetUserId(),
            reason: transfer.RejectionReason, details: transfer.TransferNumber);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Ok(new { transfer.Id, transfer.TransferNumber, transfer.Status, transfer.RejectionReason });
    }

    [HttpPost("{transferId:guid}/dispatch")]
    [Authorize(Policy = Policies.PostTransfer)]
    public async Task<ActionResult> Dispatch(Guid transferId, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var transfer = await _db.StockTransfers.Include(t => t.Lines)
            .SingleOrDefaultAsync(t => t.Id == transferId, ct);
        if (transfer is null) return NotFound();
        if (transfer.Status != TransferStatus.Approved)
            throw new DomainException("Only approved transfers can be dispatched.");
        var userId = User.GetUserId();

        foreach (var line in transfer.Lines)
        {
            var balance = await _db.InventoryBalances.SingleOrDefaultAsync(
                b => b.ProductId == line.ProductId && b.StorageLocationId == line.FromStorageLocationId, ct);
            if (balance is null)
                throw new DomainException($"No source stock balance exists for transfer line {line.Id}.");
            var movement = balance.Post(MovementType.TransferOut, line.QuantityRequested, userId,
                transfer.Reason, transfer.TransferNumber, sourceLineId: line.Id);
            _db.InventoryMovements.Add(movement);
            var takes = await _lots.ApplyDecreaseAsync(line.ProductId, line.FromStorageLocationId, line.QuantityRequested, null, null, ct);
            foreach (var take in takes)
            {
                _db.TransferLotLines.Add(new TransferLotLine
                {
                    StockTransferLineId = line.Id,
                    LotNumber = take.Source.LotNumber,
                    ExpiryUtc = take.Source.ExpiryUtc,
                    Quantity = take.Quantity
                });
            }
            line.QuantityDispatched = line.QuantityRequested;
            line.UpdatedAtUtc = DateTime.UtcNow;
        }

        transfer.Status = TransferStatus.InTransit;
        transfer.DispatchedAtUtc = DateTime.UtcNow;
        transfer.UpdatedAtUtc = DateTime.UtcNow;
        _audit.Record("StockTransfer.Dispatched", nameof(StockTransfer), transfer.Id, userId,
            details: transfer.TransferNumber);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Ok(new { transfer.Id, transfer.TransferNumber, transfer.Status, transfer.DispatchedAtUtc });
    }

    [HttpPost("{transferId:guid}/receive")]
    [Authorize(Policy = Policies.PostTransfer)]
    public async Task<ActionResult> Receive(Guid transferId, ReceiveStockTransferRequest request, CancellationToken ct)
    {
        if (request.Lines is null || request.Lines.Count == 0)
            throw new DomainException("Provide received quantities for the transfer lines.");
        if (request.Lines.Any(l => l.QuantityReceived < 0 || l.DiscrepancyNote?.Length > 500))
            throw new DomainException("Received quantities cannot be negative and discrepancy notes must be 500 characters or fewer.");
        if (request.Lines.Select(l => l.LineId).Distinct().Count() != request.Lines.Count)
            throw new DomainException("A transfer line can only appear once in a receipt.");

        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var transfer = await _db.StockTransfers.Include(t => t.Lines)
            .SingleOrDefaultAsync(t => t.Id == transferId, ct);
        if (transfer is null) return NotFound();
        if (transfer.Status != TransferStatus.InTransit)
            throw new DomainException("Only in-transit transfers can be received.");
        if (request.Lines.Count != transfer.Lines.Count)
            throw new DomainException("Receive every transfer line in one receipt, including lines with zero received.");

        var userId = User.GetUserId();
        foreach (var input in request.Lines)
        {
            var line = transfer.Lines.SingleOrDefault(l => l.Id == input.LineId)
                ?? throw new DomainException($"Transfer line {input.LineId} does not belong to this transfer.");
            if (input.QuantityReceived > line.QuantityDispatched)
                throw new DomainException($"Received quantity cannot exceed dispatched quantity for line {line.Id}.");
            if (input.QuantityReceived != line.QuantityDispatched && string.IsNullOrWhiteSpace(input.DiscrepancyNote))
                throw new DomainException($"A discrepancy note is required for line {line.Id} when received quantity differs from dispatched quantity.");

            if (input.QuantityReceived > 0)
            {
                var balance = await _db.InventoryBalances.SingleOrDefaultAsync(
                    b => b.ProductId == line.ProductId && b.StorageLocationId == line.ToStorageLocationId, ct);
                if (balance is null)
                {
                    balance = new InventoryBalance
                    {
                        ProductId = line.ProductId,
                        StorageLocationId = line.ToStorageLocationId,
                        OnHand = 0,
                        Reserved = 0
                    };
                    _db.InventoryBalances.Add(balance);
                }
                var movement = balance.Post(MovementType.TransferIn, input.QuantityReceived, userId,
                    transfer.Reason, transfer.TransferNumber, sourceLineId: line.Id);
                _db.InventoryMovements.Add(movement);
                var allocations = await _db.TransferLotLines.Where(l => l.StockTransferLineId == line.Id).ToListAsync(ct);
                var remaining = input.QuantityReceived;
                foreach (var allocation in allocations)
                {
                    if (remaining <= 0) break;
                    var take = Math.Min(remaining, allocation.Quantity);
                    await _lots.ApplyIncreaseAsync(line.ProductId, line.ToStorageLocationId, take, allocation.LotNumber, allocation.ExpiryUtc, null, ct);
                    remaining -= take;
                }
            }
            line.QuantityReceived = input.QuantityReceived;
            line.DiscrepancyNote = input.DiscrepancyNote?.Trim() ?? string.Empty;
            line.UpdatedAtUtc = DateTime.UtcNow;
        }

        transfer.Status = TransferStatus.Received;
        transfer.ReceivedAtUtc = DateTime.UtcNow;
        transfer.UpdatedAtUtc = DateTime.UtcNow;
        _audit.Record("StockTransfer.Received", nameof(StockTransfer), transfer.Id, userId,
            details: $"{transfer.TransferNumber}; received {transfer.Lines.Sum(l => l.QuantityReceived)} of {transfer.Lines.Sum(l => l.QuantityDispatched)} units.");
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return Ok(new
        {
            transfer.Id, transfer.TransferNumber, transfer.Status, transfer.ReceivedAtUtc,
            DispatchedUnits = transfer.Lines.Sum(l => l.QuantityDispatched),
            ReceivedUnits = transfer.Lines.Sum(l => l.QuantityReceived),
            DiscrepancyLines = transfer.Lines.Count(l => l.QuantityReceived != l.QuantityDispatched)
        });
    }
}

public sealed record CreateStockTransferRequest(
    Guid SourceWarehouseId, Guid DestinationWarehouseId, string? Reason,
    List<CreateStockTransferLineRequest> Lines);
public sealed record CreateStockTransferLineRequest(
    Guid ProductId, Guid FromStorageLocationId, Guid ToStorageLocationId, decimal Quantity);
public sealed record RejectStockTransferRequest(string Reason);
public sealed record ReceiveStockTransferRequest(List<ReceiveStockTransferLineRequest> Lines);
public sealed record ReceiveStockTransferLineRequest(Guid LineId, decimal QuantityReceived, string? DiscrepancyNote = null);
