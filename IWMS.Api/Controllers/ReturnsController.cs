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
[Route("api/returns")]
[Authorize(Policy = Policies.ViewData)]
public sealed class ReturnsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditLogger _audit;
    private readonly LotTrackingService _lots;

    public ReturnsController(AppDbContext db, AuditLogger audit, LotTrackingService lots)
    {
        _db = db;
        _audit = audit;
        _lots = lots;
    }

    [HttpGet]
    public async Task<ActionResult> List([FromQuery] ReturnType? type, CancellationToken ct)
    {
        var query = _db.ReturnRecords.AsNoTracking();
        if (type.HasValue) query = query.Where(r => r.Type == type.Value);
        return Ok(await query.OrderByDescending(r => r.ReturnedAtUtc)
            .Select(r => new
            {
                r.Id, r.ReturnNumber, r.Type, r.ReturnedAtUtc, r.Notes,
                r.SalesOrderId, SalesOrderNumber = r.SalesOrder == null ? null : r.SalesOrder.OrderNumber,
                r.GoodsReceiptId, ReceiptNumber = r.GoodsReceipt == null ? null : r.GoodsReceipt.ReceiptNumber,
                r.ProcessedByUserId,
                LineCount = r.Lines.Count,
                TotalQuantity = r.Lines.Sum(l => l.Quantity)
            }).ToListAsync(ct));
    }

    [HttpGet("{returnId:guid}")]
    public async Task<ActionResult> Get(Guid returnId, CancellationToken ct)
    {
        var record = await _db.ReturnRecords.AsNoTracking().Where(r => r.Id == returnId)
            .Select(r => new
            {
                r.Id, r.ReturnNumber, r.Type, r.ReturnedAtUtc, r.Notes,
                r.SalesOrderId, SalesOrderNumber = r.SalesOrder == null ? null : r.SalesOrder.OrderNumber,
                r.GoodsReceiptId, ReceiptNumber = r.GoodsReceipt == null ? null : r.GoodsReceipt.ReceiptNumber,
                Lines = r.Lines.OrderBy(l => l.Product.Sku).Select(l => new
                {
                    l.Id, l.ProductId, l.Product.Sku, ProductName = l.Product.Name,
                    l.Quantity, l.Reason, l.Condition, l.Disposition, l.StorageLocationId,
                    Location = l.StorageLocation == null ? null : l.StorageLocation.Code
                }).ToList()
            }).SingleOrDefaultAsync(ct);
        return record is null ? NotFound() : Ok(record);
    }

    [HttpPost]
    [Authorize(Policy = Policies.PostIssue)]
    public async Task<ActionResult> Create(CreateReturnRequest request, CancellationToken ct)
    {
        if (request.Lines is null || request.Lines.Count == 0)
            throw new DomainException("A return must contain at least one line.");
        if (request.Lines.Any(l => l.Quantity <= 0))
            throw new DomainException("Return quantities must be greater than zero.");
        if (request.Notes?.Length > 1000 || request.Lines.Any(l =>
                l.Reason?.Length > 500 || l.Condition?.Length > 200))
            throw new DomainException("Return notes, reasons or condition descriptions exceed the allowed length.");
        if (request.Lines.Select(l => (l.ProductId, l.StorageLocationId, l.Disposition)).Distinct().Count() != request.Lines.Count)
            throw new DomainException("Duplicate return lines are not allowed.");

        if (request.Type == ReturnType.CustomerReturn && request.SalesOrderId is null)
            throw new DomainException("Customer returns must reference a sales order.");
        if (request.Type == ReturnType.SupplierReturn && request.GoodsReceiptId is null)
            throw new DomainException("Supplier returns must reference a goods receipt.");

        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        SalesOrder? salesOrder = null;
        GoodsReceipt? goodsReceipt = null;

        if (request.Type == ReturnType.CustomerReturn)
        {
            salesOrder = await _db.SalesOrders.SingleOrDefaultAsync(o => o.Id == request.SalesOrderId, ct);
            if (salesOrder is null) return NotFound("Sales order was not found.");
            if (salesOrder.Status is SalesOrderStatus.Draft or SalesOrderStatus.Confirmed or SalesOrderStatus.Reserved
                or SalesOrderStatus.Picking or SalesOrderStatus.Cancelled)
                throw new DomainException("Customer returns require an order that has been dispatched at least in part.");
        }
        else if (request.Type == ReturnType.SupplierReturn)
        {
            goodsReceipt = await _db.GoodsReceipts.Include(g => g.Lines)
                .ThenInclude(l => l.PurchaseOrderLine)
                .SingleOrDefaultAsync(g => g.Id == request.GoodsReceiptId, ct);
            if (goodsReceipt is null) return NotFound("Goods receipt was not found.");
        }
        else
        {
            throw new DomainException("Unsupported return type.");
        }

        var userId = User.GetUserId();
        var record = new ReturnRecord
        {
            ReturnNumber = $"RT-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}"[..20],
            Type = request.Type,
            SalesOrderId = salesOrder?.Id,
            GoodsReceiptId = goodsReceipt?.Id,
            ProcessedByUserId = userId,
            Notes = request.Notes?.Trim() ?? string.Empty,
            ReturnedAtUtc = DateTime.UtcNow,
            Lines = request.Lines.Select(l => new ReturnRecordLine
            {
                ProductId = l.ProductId,
                Quantity = l.Quantity,
                Reason = l.Reason?.Trim() ?? string.Empty,
                Condition = l.Condition?.Trim() ?? string.Empty,
                Disposition = l.Disposition,
                StorageLocationId = l.StorageLocationId
            }).ToList()
        };

        for (var index = 0; index < record.Lines.Count; index++)
        {
            var line = record.Lines[index];
            var input = request.Lines[index];
            if (!await _db.Products.AnyAsync(p => p.Id == line.ProductId && p.IsActive, ct))
                throw new DomainException($"Product {line.ProductId} was not found or is inactive.");

            if (request.Type == ReturnType.CustomerReturn)
            {
                if (!await _db.SalesOrderLines.AnyAsync(l => l.SalesOrderId == salesOrder!.Id && l.ProductId == line.ProductId, ct))
                    throw new DomainException($"Product {line.ProductId} is not part of sales order {salesOrder!.OrderNumber}.");

                var dispatched = await _db.ShipmentLines
                    .Where(sl => sl.SalesOrderLine.SalesOrderId == salesOrder!.Id &&
                                 sl.SalesOrderLine.ProductId == line.ProductId &&
                                 sl.Shipment.Status == ShipmentStatus.Dispatched)
                    .SumAsync(sl => (decimal?)sl.QuantityPicked, ct) ?? 0m;
                var alreadyReturned = await _db.ReturnRecordLines
                    .Where(rl => rl.ReturnRecord.Type == ReturnType.CustomerReturn &&
                                 rl.ReturnRecord.SalesOrderId == salesOrder!.Id &&
                                 rl.ProductId == line.ProductId)
                    .SumAsync(rl => (decimal?)rl.Quantity, ct) ?? 0m;
                var sameRequest = record.Lines.Where(rl => rl.ProductId == line.ProductId).Sum(rl => rl.Quantity);
                if (alreadyReturned + sameRequest > dispatched)
                    throw new DomainException($"Return quantity for product {line.ProductId} exceeds the dispatched quantity remaining after previous returns.");

                if (line.Disposition == ReturnDisposition.ReturnToSupplier)
                    throw new DomainException("Customer returns cannot use the ReturnToSupplier disposition.");

                if (line.Disposition == ReturnDisposition.Restock)
                {
                    if (line.StorageLocationId is null)
                        throw new DomainException("Restocked customer returns must specify a destination storage location.");
                    var location = await _db.StorageLocations.SingleOrDefaultAsync(
                        l => l.Id == line.StorageLocationId && l.IsActive, ct);
                    if (location is null) throw new DomainException("Return destination storage location was not found or is inactive.");

                    var balance = await _db.InventoryBalances.SingleOrDefaultAsync(
                        b => b.ProductId == line.ProductId && b.StorageLocationId == location.Id, ct);
                    if (balance is null)
                    {
                        balance = new InventoryBalance { ProductId = line.ProductId, StorageLocationId = location.Id };
                        _db.InventoryBalances.Add(balance);
                    }
                    var movement = balance.Post(MovementType.ReturnRestock, line.Quantity, userId,
                        line.Reason, record.ReturnNumber, sourceLineId: line.Id);
                    _db.InventoryMovements.Add(movement);
                    await _lots.ApplyIncreaseAsync(line.ProductId, location.Id, line.Quantity, input.LotNumber, input.ExpiryUtc, input.Serials, ct);
                }
                else if (line.Disposition is not (ReturnDisposition.Quarantine or ReturnDisposition.Scrap))
                {
                    throw new DomainException("Unsupported customer return disposition.");
                }
            }
            else
            {
                if (line.Disposition != ReturnDisposition.ReturnToSupplier)
                    throw new DomainException("Supplier returns must use the ReturnToSupplier disposition.");
                if (line.StorageLocationId is null)
                    throw new DomainException("Supplier returns must specify the stock storage location.");
                var receiptLines = goodsReceipt!.Lines.Where(gl => gl.PurchaseOrderLine.ProductId == line.ProductId).ToList();
                if (receiptLines.Count == 0)
                    throw new DomainException($"Product {line.ProductId} was not included in goods receipt {goodsReceipt.ReceiptNumber}.");
                var accepted = receiptLines.Sum(gl => gl.QuantityAccepted);
                var previouslyReturned = await _db.ReturnRecordLines
                    .Where(rl => rl.ReturnRecord.Type == ReturnType.SupplierReturn &&
                                 rl.ReturnRecord.GoodsReceiptId == goodsReceipt.Id &&
                                 rl.ProductId == line.ProductId)
                    .SumAsync(rl => (decimal?)rl.Quantity, ct) ?? 0m;
                var requestedForProduct = record.Lines.Where(rl => rl.ProductId == line.ProductId).Sum(rl => rl.Quantity);
                if (previouslyReturned + requestedForProduct > accepted)
                    throw new DomainException($"Supplier return quantity for product {line.ProductId} exceeds the accepted quantity remaining on this receipt.");

                var balance = await _db.InventoryBalances.SingleOrDefaultAsync(
                    b => b.ProductId == line.ProductId && b.StorageLocationId == line.StorageLocationId, ct);
                if (balance is null)
                    throw new DomainException($"No stock balance exists at the selected location for product {line.ProductId}.");
                var movement = balance.Post(MovementType.Dispatch, line.Quantity, userId,
                    line.Reason, record.ReturnNumber, sourceLineId: line.Id);
                _db.InventoryMovements.Add(movement);
                await _lots.ApplyDecreaseAsync(line.ProductId, line.StorageLocationId.Value, line.Quantity, input.LotNumber, input.Serials, ct);
            }
        }

        _db.ReturnRecords.Add(record);
        _audit.Record("Return.Processed", nameof(ReturnRecord), record.Id, userId,
            details: $"{record.ReturnNumber}; {record.Type}; {record.Lines.Count} lines.");
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return CreatedAtAction(nameof(Get), new { returnId = record.Id }, new
        {
            record.Id, record.ReturnNumber, record.Type, record.ReturnedAtUtc,
            LineCount = record.Lines.Count,
            RestockedQuantity = record.Lines.Where(l => l.Disposition == ReturnDisposition.Restock).Sum(l => l.Quantity),
            SupplierReturnQuantity = record.Lines.Where(l => l.Disposition == ReturnDisposition.ReturnToSupplier).Sum(l => l.Quantity)
        });
    }
}

public sealed record CreateReturnRequest(
    ReturnType Type, Guid? SalesOrderId, Guid? GoodsReceiptId, string? Notes,
    List<CreateReturnLineRequest> Lines);
public sealed record CreateReturnLineRequest(
    Guid ProductId, decimal Quantity, string? Reason, string? Condition,
    ReturnDisposition Disposition, Guid? StorageLocationId, string? LotNumber = null, DateTime? ExpiryUtc = null, string? Serials = null);
