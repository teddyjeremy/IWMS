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
[Route("api/stock-counts")]
[Authorize(Policy = Policies.ViewData)]
public sealed class StockCountsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditLogger _audit;
    private readonly LotTrackingService _lots;

    public StockCountsController(AppDbContext db, AuditLogger audit, LotTrackingService lots)
    {
        _db = db;
        _audit = audit;
        _lots = lots;
    }

    [HttpGet]
    public async Task<ActionResult> List([FromQuery] StockCountStatus? status, CancellationToken ct)
    {
        var query = _db.StockCountSessions.AsNoTracking();
        if (status.HasValue) query = query.Where(c => c.Status == status.Value);

        var counts = await query.OrderByDescending(c => c.CreatedAtUtc)
            .Select(c => new
            {
                c.Id,
                c.CountNumber,
                c.Status,
                c.WarehouseId,
                Warehouse = c.Warehouse.Name,
                c.CreatedAtUtc,
                c.StartedAtUtc,
                c.CompletedAtUtc,
                LineCount = c.Lines.Count,
                CountedLineCount = c.Lines.Count(l => l.CountedQuantity != null),
                VarianceLineCount = c.Lines.Count(l => l.CountedQuantity != null && l.CountedQuantity != l.SystemQuantity)
            }).ToListAsync(ct);

        return Ok(counts);
    }

    [HttpGet("{countId:guid}")]
    public async Task<ActionResult> Get(Guid countId, CancellationToken ct)
    {
        var blind = await _db.StockCountSessions.AsNoTracking()
            .Where(c => c.Id == countId)
            .Select(c => (StockCountStatus?)c.Status)
            .SingleOrDefaultAsync(ct);
        if (blind is null) return NotFound();
        var hideBookQuantity = blind == StockCountStatus.Counting;

        var count = await _db.StockCountSessions.AsNoTracking()
            .Where(c => c.Id == countId)
            .Select(c => new
            {
                c.Id,
                c.CountNumber,
                c.Status,
                c.Notes,
                c.WarehouseId,
                Warehouse = c.Warehouse.Name,
                c.StartedAtUtc,
                c.CompletedAtUtc,
                c.ApprovedAtUtc,
                Lines = c.Lines.OrderBy(l => l.StorageLocation.Code).ThenBy(l => l.Product.Sku)
                    .Select(l => new
                    {
                        l.Id,
                        l.ProductId,
                        l.Product.Sku,
                        ProductName = l.Product.Name,
                        l.StorageLocationId,
                        Location = l.StorageLocation.Code,
                        SystemQuantity = hideBookQuantity ? null : (decimal?)l.SystemQuantity,
                        l.CountedQuantity,
                        Variance = hideBookQuantity || l.CountedQuantity == null ? null : l.CountedQuantity - l.SystemQuantity,
                        l.VarianceReason,
                        l.PostedMovementId
                    }).ToList()
            }).SingleAsync(ct);

        return Ok(count);
    }

    [HttpPost]
    [Authorize(Policy = Policies.PostAdjustment)]
    public async Task<ActionResult> Create(CreateStockCountRequest request, CancellationToken ct)
    {
        if (request.Notes?.Length > 1000)
            throw new DomainException("Notes must be 1000 characters or fewer.");

        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var warehouse = await _db.Warehouses.SingleOrDefaultAsync(w => w.Id == request.WarehouseId, ct);
        if (warehouse is null) return NotFound("Warehouse was not found.");

        var balances = await _db.InventoryBalances
            .Include(b => b.StorageLocation)
            .Where(b => b.StorageLocation.WarehouseId == request.WarehouseId &&
                        b.StorageLocation.IsActive)
            .OrderBy(b => b.StorageLocation.Code)
            .ThenBy(b => b.ProductId)
            .ToListAsync(ct);

        if (balances.Count == 0)
            throw new DomainException("There is no on-hand inventory to count in this warehouse.");

        var count = new StockCountSession
        {
            CountNumber = $"SC-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..20],
            WarehouseId = warehouse.Id,
            CreatedByUserId = User.GetUserId(),
            Notes = request.Notes?.Trim() ?? string.Empty,
            Status = StockCountStatus.Counting,
            StartedAtUtc = DateTime.UtcNow,
            Lines = balances.Select(b => new StockCountLine
            {
                ProductId = b.ProductId,
                StorageLocationId = b.StorageLocationId,
                SystemQuantity = b.OnHand
            }).ToList()
        };

        _db.StockCountSessions.Add(count);
        _audit.Record("StockCount.Started", nameof(StockCountSession), count.Id,
            User.GetUserId(), details: $"{count.CountNumber}; {count.Lines.Count} lines.");
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return CreatedAtAction(nameof(Get), new { countId = count.Id }, new
        {
            count.Id,
            count.CountNumber,
            count.Status,
            LineCount = count.Lines.Count
        });
    }

    [HttpPut("{countId:guid}/lines")]
    [Authorize(Policy = Policies.PostAdjustment)]
    public async Task<ActionResult> RecordCounts(
        Guid countId, RecordStockCountRequest request, CancellationToken ct)
    {
        if (request.Lines is null || request.Lines.Count == 0)
            throw new DomainException("Provide at least one count line.");
        if (request.Lines.Any(l => l.CountedQuantity < 0))
            throw new DomainException("Counted quantities cannot be negative.");
        if (request.Lines.Select(l => l.LineId).Distinct().Count() != request.Lines.Count)
            throw new DomainException("A count line can only appear once per request.");

        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var count = await _db.StockCountSessions.Include(c => c.Lines)
            .SingleOrDefaultAsync(c => c.Id == countId, ct);
        if (count is null) return NotFound();
        if (count.Status is not (StockCountStatus.Counting or StockCountStatus.PendingReview))
            throw new DomainException("Count quantities can only be entered while the session is counting or in variance review.");

        foreach (var input in request.Lines)
        {
            var line = count.Lines.SingleOrDefault(l => l.Id == input.LineId)
                ?? throw new DomainException($"Count line {input.LineId} does not belong to this session.");
            if (input.VarianceReason?.Length > 500)
                throw new DomainException("Variance reasons must be 500 characters or fewer.");

            line.CountedQuantity = input.CountedQuantity;
            line.VarianceReason = input.VarianceReason?.Trim() ?? string.Empty;
            line.CountedByUserId = User.GetUserId();
            line.UpdatedAtUtc = DateTime.UtcNow;
        }

        if (count.Lines.All(l => l.CountedQuantity.HasValue))
        {
            count.Status = StockCountStatus.PendingReview;
            count.CompletedAtUtc = DateTime.UtcNow;
        }

        count.UpdatedAtUtc = DateTime.UtcNow;
        _audit.Record("StockCount.QuantitiesRecorded", nameof(StockCountSession), count.Id,
            User.GetUserId(), details: $"Updated {request.Lines.Count} count lines.");
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Ok(new
        {
            count.Id,
            count.Status,
            CountedLineCount = count.Lines.Count(l => l.CountedQuantity.HasValue),
            TotalLineCount = count.Lines.Count,
            VarianceLineCount = count.Lines.Count(l => l.CountedQuantity.HasValue && l.CountedQuantity != l.SystemQuantity)
        });
    }

    [HttpPost("{countId:guid}/post")]
    [Authorize(Policy = Policies.PostAdjustment)]
    public async Task<ActionResult> Post(Guid countId, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var count = await _db.StockCountSessions.Include(c => c.Lines)
            .SingleOrDefaultAsync(c => c.Id == countId, ct);
        if (count is null) return NotFound();
        if (count.Status != StockCountStatus.PendingReview)
            throw new DomainException("Only a fully counted session pending review can be posted.");
        if (count.Lines.Any(l => !l.CountedQuantity.HasValue))
            throw new DomainException("Every line must have a physical count before posting.");

        var userId = User.GetUserId();
        foreach (var line in count.Lines)
        {
            var variance = line.CountedQuantity!.Value - line.SystemQuantity;
            if (variance == 0) continue;
            if (string.IsNullOrWhiteSpace(line.VarianceReason))
                throw new DomainException($"A variance reason is required for product {line.ProductId} at location {line.StorageLocationId}.");

            var balance = await _db.InventoryBalances.SingleOrDefaultAsync(
                b => b.ProductId == line.ProductId && b.StorageLocationId == line.StorageLocationId, ct)
                ?? throw new DomainException("Inventory balance is missing for a counted line; posting was stopped.");

            // Do not overwrite inventory movements that happened after the snapshot.
            // A fresh count is safer than applying a stale variance to a changed balance.
            if (balance.OnHand != line.SystemQuantity)
                throw new DomainException(
                    $"Stock changed after count line {line.Id} was created. Recount this item before posting.");

            var movement = balance.Post(
                variance > 0 ? MovementType.AdjustmentIncrease : MovementType.AdjustmentDecrease,
                Math.Abs(variance),
                userId,
                line.VarianceReason,
                count.CountNumber,
                sourceLineId: line.Id);
            _db.InventoryMovements.Add(movement);
            var tracking = await _db.Products.AsNoTracking()
                .Where(p => p.Id == line.ProductId)
                .Select(p => new { p.Sku, p.TrackSerials })
                .SingleAsync(ct);
            if (tracking.TrackSerials)
                throw new DomainException($"Serial-tracked product {tracking.Sku} must be adjusted from Inventory so each serial number is captured.");
            if (variance > 0)
                await _lots.ApplyIncreaseAsync(line.ProductId, line.StorageLocationId, variance, "CYCLE-COUNT", null, null, ct);
            else
                await _lots.ApplyDecreaseAsync(line.ProductId, line.StorageLocationId, Math.Abs(variance), null, null, ct);
            line.PostedMovementId = movement.Id;
        }

        count.Status = StockCountStatus.Posted;
        count.ApprovedAtUtc = DateTime.UtcNow;
        count.ApprovedByUserId = userId;
        count.UpdatedAtUtc = DateTime.UtcNow;
        _audit.Record("StockCount.Posted", nameof(StockCountSession), count.Id,
            userId, details: $"{count.CountNumber}; variances: {count.Lines.Count(l => l.CountedQuantity != l.SystemQuantity)}.");
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Ok(new
        {
            count.Id,
            count.CountNumber,
            count.Status,
            PostedAdjustments = count.Lines.Count(l => l.PostedMovementId.HasValue),
            VarianceQuantity = count.Lines.Sum(l => l.CountedQuantity!.Value - l.SystemQuantity)
        });
    }
}

public sealed record CreateStockCountRequest(Guid WarehouseId, string? Notes = null);
public sealed record RecordStockCountRequest(List<RecordStockCountLineRequest> Lines);
public sealed record RecordStockCountLineRequest(Guid LineId, decimal CountedQuantity, string? VarianceReason = null);
