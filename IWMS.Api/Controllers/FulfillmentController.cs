using IWMS.Api.Security;
using IWMS.Application.Audit;
using IWMS.Domain.Common;
using IWMS.Domain.Entities;
using IWMS.Domain.Enums;
using IWMS.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IWMS.Api.Controllers;

[ApiController]
[Route("api/fulfillment")]
[Authorize]
public sealed class FulfillmentController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditLogger _audit;

    public FulfillmentController(AppDbContext db, AuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    [HttpGet("shipments")]
    [Authorize(Policy = Policies.ViewData)]
    public async Task<ActionResult> ListShipments(
        [FromQuery] ShipmentStatus? status,
        CancellationToken ct)
    {
        var query = _db.Shipments.AsNoTracking();
        if (status.HasValue) query = query.Where(s => s.Status == status.Value);
        var shipments = await query.OrderByDescending(s => s.CreatedAtUtc)
            .Select(s => new
            {
                s.Id,
                s.ShipmentNumber,
                s.SalesOrderId,
                OrderNumber = s.SalesOrder.OrderNumber,
                Customer = s.SalesOrder.Customer.Name,
                s.Status,
                s.CreatedAtUtc,
                LineCount = s.Lines.Count,
                QuantityToPick = s.Lines.Sum(l => l.QuantityToPick),
                QuantityPicked = s.Lines.Sum(l => l.QuantityPicked)
            }).ToListAsync(ct);
        return Ok(shipments);
    }

    [HttpGet("shipments/{shipmentId:guid}")]
    [Authorize(Policy = Policies.ViewData)]
    public async Task<ActionResult> GetShipment(Guid shipmentId, CancellationToken ct)
    {
        var shipment = await _db.Shipments.AsNoTracking()
            .Where(s => s.Id == shipmentId)
            .Select(s => new
            {
                s.Id,
                s.ShipmentNumber,
                s.Status,
                s.SalesOrderId,
                OrderNumber = s.SalesOrder.OrderNumber,
                Lines = s.Lines.OrderBy(l => l.SalesOrderLine.Product.Sku)
                    .Select(l => new
                    {
                        l.Id,
                        l.SalesOrderLine.Product.Sku,
                        ProductName = l.SalesOrderLine.Product.Name,
                        Location = l.StorageLocation.Code,
                        l.QuantityToPick,
                        l.QuantityPicked
                    }).ToList()
            }).SingleOrDefaultAsync(ct);
        return shipment is null ? NotFound() : Ok(shipment);
    }

    [HttpPost("sales-orders/{orderId:guid}/reserve")]
    [Authorize(Policy = Policies.PostIssue)]
    public async Task<ActionResult> Reserve(Guid orderId, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var order = await _db.SalesOrders
            .Include(o => o.Lines)
            .SingleOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null) return NotFound();
        if (order.Status is not (SalesOrderStatus.Confirmed or SalesOrderStatus.Backordered or SalesOrderStatus.Reserved or SalesOrderStatus.PartiallyDispatched))
            throw new DomainException("Only confirmed or backordered orders can be reserved.");

        var shortages = new List<object>();
        foreach (var line in order.Lines)
        {
            var activeReservations = await _db.Reservations
                .Where(r => r.SalesOrderLineId == line.Id && r.ReleasedAtUtc == null)
                .SumAsync(r => (decimal?)r.Quantity, ct) ?? 0m;
            var remaining = line.QuantityOrdered - line.QuantityDispatched - activeReservations;
            if (remaining <= 0) continue;

            var balances = await _db.InventoryBalances
                .Include(b => b.StorageLocation)
                .Where(b => b.ProductId == line.ProductId &&
                            b.StorageLocation.IsActive &&
                            b.OnHand > b.Reserved)
                .OrderBy(b => b.StorageLocation.Code)
                .ToListAsync(ct);

            foreach (var balance in balances)
            {
                var quantity = Math.Min(remaining, balance.Available);
                if (quantity <= 0) continue;
                balance.Reserved += quantity;
                balance.UpdatedAtUtc = DateTime.UtcNow;
                _db.Reservations.Add(new Reservation
                {
                    SalesOrderLineId = line.Id,
                    StorageLocationId = balance.StorageLocationId,
                    Quantity = quantity
                });
                line.QuantityReserved += quantity;
                remaining -= quantity;
                if (remaining <= 0) break;
            }

            if (remaining > 0)
                shortages.Add(new { lineId = line.Id, productId = line.ProductId, quantityShort = remaining });
        }

        var alreadyDispatched = order.Lines.Any(l => l.QuantityDispatched > 0);
        order.Status = shortages.Count > 0
            ? SalesOrderStatus.Backordered
            : alreadyDispatched ? SalesOrderStatus.PartiallyDispatched : SalesOrderStatus.Reserved;
        order.UpdatedAtUtc = DateTime.UtcNow;
        _audit.Record("SalesOrder.StockReserved", nameof(SalesOrder), order.Id,
            User.GetUserId(), details: $"Shortages: {shortages.Count}");
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Ok(new { order.Id, order.Status, shortages });
    }

    [HttpPost("sales-orders/{orderId:guid}/pick-list")]
    [Authorize(Policy = Policies.PostIssue)]
    public async Task<ActionResult> CreatePickList(Guid orderId, CreatePickListRequest request, CancellationToken ct)
    {
        if (request.Notes?.Length > 1000)
            throw new DomainException("Notes must be 1000 characters or fewer.");

        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var order = await _db.SalesOrders.Include(o => o.Lines)
            .SingleOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null) return NotFound();
        if (order.Status is not (SalesOrderStatus.Reserved or SalesOrderStatus.Backordered or SalesOrderStatus.Picking or SalesOrderStatus.PartiallyDispatched))
            throw new DomainException("Reserve stock before creating a pick list.");

        var shipment = new Shipment
        {
            SalesOrderId = order.Id,
            ShipmentNumber = $"SH-{Guid.NewGuid():N}"[..15],
            Status = ShipmentStatus.PickListCreated,
            Notes = request.Notes?.Trim() ?? string.Empty
        };

        foreach (var line in order.Lines)
        {
            var reservations = await _db.Reservations
                .Where(r => r.SalesOrderLineId == line.Id && r.ReleasedAtUtc == null)
                .GroupBy(r => r.StorageLocationId)
                .Select(g => new { StorageLocationId = g.Key, Quantity = g.Sum(r => r.Quantity) })
                .ToListAsync(ct);
            foreach (var reservation in reservations)
            {
                var alreadyOnOpenPick = await _db.ShipmentLines
                    .Where(sl => sl.SalesOrderLineId == line.Id &&
                                 sl.StorageLocationId == reservation.StorageLocationId &&
                                 sl.Shipment.Status != ShipmentStatus.Dispatched &&
                                 sl.Shipment.Status != ShipmentStatus.Cancelled)
                    .SumAsync(sl => (decimal?)sl.QuantityToPick, ct) ?? 0m;

                var quantity = reservation.Quantity - alreadyOnOpenPick;
                if (quantity <= 0) continue;
                shipment.Lines.Add(new ShipmentLine
                {
                    SalesOrderLineId = line.Id,
                    StorageLocationId = reservation.StorageLocationId,
                    QuantityToPick = quantity
                });
            }
        }

        if (shipment.Lines.Count == 0)
            throw new DomainException("There are no reserved quantities available to put on a new pick list.");

        _db.Shipments.Add(shipment);
        order.Status = SalesOrderStatus.Picking;
        order.UpdatedAtUtc = DateTime.UtcNow;
        _audit.Record("Shipment.PickListCreated", nameof(Shipment), shipment.Id,
            User.GetUserId(), details: shipment.ShipmentNumber);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Ok(new
        {
            shipment.Id,
            shipment.ShipmentNumber,
            shipment.Status,
            Lines = shipment.Lines.Select(l => new { l.Id, l.SalesOrderLineId, l.StorageLocationId, l.QuantityToPick })
        });
    }

    [HttpPost("shipments/{shipmentId:guid}/pick")]
    [Authorize(Policy = Policies.PostIssue)]
    public async Task<ActionResult> Pick(Guid shipmentId, PickShipmentRequest request, CancellationToken ct)
    {
        if (request.Lines is null || request.Lines.Count == 0)
            throw new DomainException("Provide at least one pick line.");
        if (request.Lines.Any(l => l.QuantityPicked < 0))
            throw new DomainException("Picked quantities cannot be negative.");

        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var shipment = await _db.Shipments.Include(s => s.Lines)
            .ThenInclude(l => l.SalesOrderLine)
            .Include(s => s.SalesOrder)
            .SingleOrDefaultAsync(s => s.Id == shipmentId, ct);
        if (shipment is null) return NotFound();
        if (shipment.Status is not (ShipmentStatus.PickListCreated or ShipmentStatus.Picked))
            throw new DomainException("This shipment can no longer be picked.");

        foreach (var input in request.Lines)
        {
            var line = shipment.Lines.SingleOrDefault(l => l.Id == input.ShipmentLineId)
                ?? throw new DomainException($"Shipment line {input.ShipmentLineId} does not belong to this shipment.");
            if (input.QuantityPicked > line.QuantityToPick)
                throw new DomainException("Picked quantity cannot exceed the quantity on the pick list.");
            line.QuantityPicked = input.QuantityPicked;
        }

        shipment.Status = shipment.Lines.All(l => l.QuantityPicked == l.QuantityToPick)
            ? ShipmentStatus.Picked : ShipmentStatus.PickListCreated;
        foreach (var salesLineId in shipment.Lines.Select(l => l.SalesOrderLineId).Distinct())
        {
            var salesLine = shipment.Lines.First(l => l.SalesOrderLineId == salesLineId).SalesOrderLine;
            salesLine.QuantityPicked = await _db.ShipmentLines
                .Where(sl => sl.SalesOrderLineId == salesLineId &&
                             sl.Shipment.Status != ShipmentStatus.Dispatched &&
                             sl.Shipment.Status != ShipmentStatus.Cancelled)
                .SumAsync(sl => (decimal?)sl.QuantityPicked, ct) ?? 0m;
        }
        shipment.UpdatedAtUtc = DateTime.UtcNow;
        _audit.Record("Shipment.Picked", nameof(Shipment), shipment.Id,
            User.GetUserId(), details: shipment.ShipmentNumber);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Ok(new { shipment.Id, shipment.Status });
    }

    [HttpPost("shipments/{shipmentId:guid}/dispatch")]
    [Authorize(Policy = Policies.PostIssue)]
    public async Task<ActionResult> Dispatch(Guid shipmentId, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var shipment = await _db.Shipments
            .Include(s => s.Lines).ThenInclude(l => l.SalesOrderLine)
            .Include(s => s.SalesOrder).ThenInclude(o => o.Lines)
            .SingleOrDefaultAsync(s => s.Id == shipmentId, ct);
        if (shipment is null) return NotFound();
        if (shipment.Status != ShipmentStatus.Picked)
            throw new DomainException("All shipment lines must be picked before dispatch.");

        var userId = User.GetUserId();
        foreach (var line in shipment.Lines)
        {
            if (line.QuantityPicked <= 0) continue;
            var balance = await _db.InventoryBalances
                .SingleOrDefaultAsync(b => b.ProductId == line.SalesOrderLine.ProductId &&
                                           b.StorageLocationId == line.StorageLocationId, ct)
                ?? throw new DomainException("Stock balance was not found for a picked line.");

            var activeReservations = await _db.Reservations
                .Where(r => r.SalesOrderLineId == line.SalesOrderLineId &&
                            r.StorageLocationId == line.StorageLocationId &&
                            r.ReleasedAtUtc == null)
                .OrderBy(r => r.CreatedAtUtc)
                .ToListAsync(ct);
            var reservationTotal = activeReservations.Sum(r => r.Quantity);
            if (reservationTotal < line.QuantityPicked || balance.Reserved < line.QuantityPicked)
                throw new DomainException("Reserved stock no longer covers the picked quantity.");

            var release = line.QuantityPicked;
            foreach (var reservation in activeReservations)
            {
                var take = Math.Min(release, reservation.Quantity);
                reservation.Quantity -= take;
                release -= take;
                if (reservation.Quantity == 0)
                    reservation.ReleasedAtUtc = DateTime.UtcNow;
                if (release == 0) break;
            }

            balance.Reserved -= line.QuantityPicked;
            line.SalesOrderLine.QuantityReserved = Math.Max(
                0, line.SalesOrderLine.QuantityReserved - line.QuantityPicked);
            var movement = balance.Post(
                MovementType.Dispatch,
                line.QuantityPicked,
                userId,
                $"Sales order {shipment.SalesOrder.OrderNumber}",
                shipment.ShipmentNumber,
                idempotencyKey: line.Id,
                sourceLineId: line.Id);
            _db.InventoryMovements.Add(movement);
            line.SalesOrderLine.QuantityDispatched += line.QuantityPicked;
        }

        shipment.Status = ShipmentStatus.Dispatched;
        shipment.DispatchedAtUtc = DateTime.UtcNow;
        shipment.DispatchedByUserId = userId;
        shipment.UpdatedAtUtc = DateTime.UtcNow;

        var allDispatched = shipment.SalesOrder.Lines.All(l => l.QuantityDispatched >= l.QuantityOrdered);
        var anyDispatched = shipment.SalesOrder.Lines.Any(l => l.QuantityDispatched > 0);
        var hasActiveReservations = await _db.Reservations.AnyAsync(
            r => r.SalesOrderLine.SalesOrderId == shipment.SalesOrderId && r.ReleasedAtUtc == null, ct);
        shipment.SalesOrder.Status = allDispatched
            ? SalesOrderStatus.Dispatched
            : hasActiveReservations
                ? (anyDispatched ? SalesOrderStatus.PartiallyDispatched : SalesOrderStatus.Reserved)
                : SalesOrderStatus.Backordered;
        shipment.SalesOrder.UpdatedAtUtc = DateTime.UtcNow;

        _audit.Record("Shipment.Dispatched", nameof(Shipment), shipment.Id,
            userId, details: shipment.ShipmentNumber);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Ok(new { shipment.Id, shipment.Status, shipment.ShipmentNumber, shipment.DispatchedAtUtc });
    }

    [HttpPost("sales-orders/{orderId:guid}/cancel")]
    [Authorize(Policy = Policies.PostIssue)]
    public async Task<IActionResult> CancelOrder(Guid orderId, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var order = await _db.SalesOrders.Include(o => o.Lines)
            .SingleOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null) return NotFound();
        if (order.Status is SalesOrderStatus.Cancelled or SalesOrderStatus.Dispatched ||
            order.Lines.Any(l => l.QuantityDispatched > 0))
            throw new DomainException("An already cancelled or dispatched order cannot be cancelled.");

        var reservations = await _db.Reservations
            .Where(r => r.SalesOrderLine.SalesOrderId == orderId && r.ReleasedAtUtc == null)
            .ToListAsync(ct);
        foreach (var reservation in reservations)
        {
            var balance = await _db.InventoryBalances.SingleOrDefaultAsync(
                b => b.ProductId == reservation.SalesOrderLine.ProductId &&
                     b.StorageLocationId == reservation.StorageLocationId, ct);
            if (balance is null || balance.Reserved < reservation.Quantity)
                throw new DomainException("Inventory reservation data is inconsistent; cancellation was stopped.");
            balance.Reserved -= reservation.Quantity;
            balance.UpdatedAtUtc = DateTime.UtcNow;
            reservation.ReleasedAtUtc = DateTime.UtcNow;
            reservation.SalesOrderLine.QuantityReserved = Math.Max(
                0, reservation.SalesOrderLine.QuantityReserved - reservation.Quantity);
        }

        var openShipments = await _db.Shipments
            .Where(s => s.SalesOrderId == orderId &&
                        s.Status != ShipmentStatus.Dispatched &&
                        s.Status != ShipmentStatus.Cancelled)
            .ToListAsync(ct);
        foreach (var shipment in openShipments)
        {
            shipment.Status = ShipmentStatus.Cancelled;
            shipment.UpdatedAtUtc = DateTime.UtcNow;
        }

        foreach (var line in order.Lines)
            line.QuantityPicked = 0;

        order.Status = SalesOrderStatus.Cancelled;
        order.UpdatedAtUtc = DateTime.UtcNow;
        _audit.Record("SalesOrder.Cancelled", nameof(SalesOrder), order.Id,
            User.GetUserId(), details: $"Cancelled and released {reservations.Count} reservations.");
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return NoContent();
    }

    [HttpPost("shipments/{shipmentId:guid}/cancel")]
    [Authorize(Policy = Policies.PostIssue)]
    public async Task<IActionResult> CancelShipment(Guid shipmentId, CancellationToken ct)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var shipment = await _db.Shipments.Include(s => s.Lines)
            .ThenInclude(l => l.SalesOrderLine)
            .Include(s => s.SalesOrder)
            .SingleOrDefaultAsync(s => s.Id == shipmentId, ct);
        if (shipment is null) return NotFound();
        if (shipment.Status is ShipmentStatus.Dispatched or ShipmentStatus.Cancelled)
            throw new DomainException("This shipment cannot be cancelled.");

        foreach (var line in shipment.Lines)
        {
            var salesLineId = line.SalesOrderLineId;
            line.QuantityPicked = 0;
            line.SalesOrderLine.QuantityPicked = await _db.ShipmentLines
                .Where(sl => sl.SalesOrderLineId == salesLineId &&
                             sl.ShipmentId != shipmentId &&
                             sl.Shipment.Status != ShipmentStatus.Dispatched &&
                             sl.Shipment.Status != ShipmentStatus.Cancelled)
                .SumAsync(sl => (decimal?)sl.QuantityPicked, ct) ?? 0m;
        }

        shipment.Status = ShipmentStatus.Cancelled;
        shipment.UpdatedAtUtc = DateTime.UtcNow;
        var order = shipment.SalesOrder;
        if (order.Status == SalesOrderStatus.Picking)
        {
            var hasReservations = await _db.Reservations.AnyAsync(
                r => r.SalesOrderLine.SalesOrderId == order.Id && r.ReleasedAtUtc == null, ct);
            order.Status = hasReservations ? SalesOrderStatus.Reserved : SalesOrderStatus.Backordered;
            order.UpdatedAtUtc = DateTime.UtcNow;
        }

        _audit.Record("Shipment.Cancelled", nameof(Shipment), shipment.Id,
            User.GetUserId(), details: shipment.ShipmentNumber);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return NoContent();
    }
}

public sealed record CreatePickListRequest(string? Notes = null);
public sealed record PickShipmentRequest(List<PickShipmentLineRequest> Lines);
public sealed record PickShipmentLineRequest(Guid ShipmentLineId, decimal QuantityPicked);
