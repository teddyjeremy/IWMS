using IWMS.Api.Reports;
using IWMS.Api.Security;
using IWMS.Application;
using IWMS.Application.Audit;
using IWMS.Domain.Common;
using IWMS.Domain.Entities;
using IWMS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IWMS.Api.Controllers;

[ApiController]
[Route("api/sales-orders")]
[Authorize]
public sealed class SalesOrdersController : ControllerBase
{
    private readonly IAppDbContext _db;
    private readonly AuditLogger _audit;

    public SalesOrdersController(IAppDbContext db, AuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    [HttpGet]
    [Authorize(Policy = Policies.ViewData)]
    public async Task<ActionResult<IReadOnlyList<SalesOrderListItem>>> List(
        [FromQuery] SalesOrderStatus? status,
        [FromQuery] string? search,
        CancellationToken ct)
    {
        var query = _db.SalesOrders.AsNoTracking();

        if (status.HasValue)
            query = query.Where(o => o.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(o =>
                o.OrderNumber.ToLower().Contains(term) ||
                o.Customer.Name.ToLower().Contains(term));
        }

        var orders = await query
            .OrderByDescending(o => o.CreatedAtUtc)
            .Select(o => new SalesOrderListItem(
                o.Id,
                o.OrderNumber,
                o.Customer.Name,
                o.Status,
                o.RequiredDateUtc,
                o.Lines.Sum(l => (decimal?)l.QuantityOrdered) ?? 0m,
                o.Lines.Sum(l => (decimal?)(l.QuantityOrdered * l.UnitPrice)) ?? 0m))
            .ToListAsync(ct);

        return Ok(orders);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Policies.ViewData)]
    public async Task<ActionResult<SalesOrderDetail>> Get(Guid id, CancellationToken ct)
    {
        var order = await _db.SalesOrders
            .AsNoTracking()
            .Where(o => o.Id == id)
            .Select(o => new SalesOrderDetail(
                o.Id,
                o.OrderNumber,
                o.CustomerId,
                o.Customer.Name,
                o.Status,
                o.RequiredDateUtc,
                o.Notes,
                o.CreatedByUserId,
                o.CreatedAtUtc,
                o.Lines.Select(l => new SalesOrderLineDetail(
                    l.Id,
                    l.ProductId,
                    l.Product.Sku,
                    l.Product.Name,
                    l.QuantityOrdered,
                    l.QuantityReserved,
                    l.QuantityPicked,
                    l.QuantityDispatched,
                    l.UnitPrice,
                    l.QuantityOrdered * l.UnitPrice)).ToList(),
                o.Lines.Sum(l => (decimal?)(l.QuantityOrdered * l.UnitPrice)) ?? 0m,
                o.Version))
            .SingleOrDefaultAsync(ct);

        return order is null ? NotFound() : Ok(order);
    }

    [HttpGet("{id:guid}/delivery-note")]
    [Authorize(Policy = Policies.ViewData)]
    public async Task<IActionResult> DeliveryNote(Guid id, CancellationToken ct)
    {
        var order = await _db.SalesOrders.AsNoTracking()
            .Where(o => o.Id == id)
            .Select(o => new
            {
                o.OrderNumber,
                Customer = o.Customer.Name,
                o.Status,
                o.RequiredDateUtc,
                o.Notes,
                Lines = o.Lines.OrderBy(l => l.Product.Sku).Select(l => new
                {
                    l.Product.Sku,
                    l.Product.Name,
                    l.QuantityOrdered,
                    l.QuantityDispatched,
                    l.UnitPrice
                }).ToList()
            })
            .SingleOrDefaultAsync(ct);
        if (order is null)
            return NotFound();

        var lines = new List<string>
        {
            "IWMS Delivery Note",
            order.OrderNumber,
            $"Customer: {order.Customer}",
            $"Status: {order.Status}",
            $"Required date UTC: {order.RequiredDateUtc:yyyy-MM-dd}",
            string.IsNullOrWhiteSpace(order.Notes) ? "" : $"Notes: {order.Notes}",
            "",
            "SKU | Product | Ordered | Dispatched | Unit price",
            new string('-', 90)
        };
        foreach (var line in order.Lines)
            lines.Add($"{line.Sku} | {line.Name} | {line.QuantityOrdered} | {line.QuantityDispatched} | {line.UnitPrice}");
        lines.Add("");
        lines.Add("Received by: ____________________    Date: ____________________");
        return File(SimplePdfWriter.Create(lines), "application/pdf", $"{order.OrderNumber}-delivery-note.pdf");
    }

    [HttpPost]
    [Authorize(Policy = Policies.ManageSalesOrders)]
    public async Task<ActionResult<object>> Create(
        CreateSalesOrderRequest request,
        CancellationToken ct)
    {
        if (request.CustomerId == Guid.Empty)
            throw new DomainException("A customer is required.");
        if (request.Lines is null || request.Lines.Count == 0)
            throw new DomainException("A sales order must contain at least one line.");
        if (request.Lines.Any(l => l.ProductId == Guid.Empty ||
                                   l.QuantityOrdered <= 0 ||
                                   l.UnitPrice < 0))
            throw new DomainException("Each line needs a product, a positive quantity and a non-negative unit price.");
        if (request.Notes?.Length > 2000)
            throw new DomainException("Notes must be 2000 characters or fewer.");

        var customer = await _db.Customers
            .SingleOrDefaultAsync(c => c.Id == request.CustomerId && c.IsActive, ct);
        if (customer is null)
            throw new DomainException("The customer does not exist or is inactive.");

        var productIds = request.Lines.Select(l => l.ProductId).Distinct().ToArray();
        var activeProductIds = await _db.Products
            .Where(p => productIds.Contains(p.Id) && p.IsActive)
            .Select(p => p.Id)
            .ToListAsync(ct);

        if (activeProductIds.Count != productIds.Length)
            throw new DomainException("One or more products do not exist or are inactive.");

        if (request.RequiredDateUtc.HasValue &&
            request.RequiredDateUtc.Value.Kind == DateTimeKind.Local)
            throw new DomainException("RequiredDateUtc must be supplied in UTC.");

        var order = new SalesOrder
        {
            OrderNumber = $"SO-{Guid.NewGuid():N}",
            Status = SalesOrderStatus.Draft,
            CustomerId = customer.Id,
            CreatedByUserId = User.GetUserId(),
            RequiredDateUtc = request.RequiredDateUtc,
            Notes = request.Notes?.Trim() ?? string.Empty,
            Lines = request.Lines.Select(line => new SalesOrderLine
            {
                ProductId = line.ProductId,
                QuantityOrdered = line.QuantityOrdered,
                UnitPrice = line.UnitPrice
            }).ToList()
        };

        _db.SalesOrders.Add(order);
        _audit.Record("SalesOrder.Created", nameof(SalesOrder), order.Id,
            User.GetUserId(), details: order.OrderNumber);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = order.Id },
            new { order.Id, order.OrderNumber, order.Status });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.ManageSalesOrders)]
    public async Task<IActionResult> UpdateDraft(Guid id, UpdateSalesOrderRequest request, CancellationToken ct)
    {
        if (request.CustomerId == Guid.Empty)
            throw new DomainException("A customer is required.");
        if (request.Lines is null || request.Lines.Count == 0)
            throw new DomainException("A sales order must contain at least one line.");
        if (request.Lines.Any(l => l.ProductId == Guid.Empty || l.QuantityOrdered <= 0 || l.UnitPrice < 0))
            throw new DomainException("Each line needs a product, a positive quantity and a non-negative unit price.");
        if (request.Notes?.Length > 2000)
            throw new DomainException("Notes must be 2000 characters or fewer.");
        if (request.RequiredDateUtc.HasValue && request.RequiredDateUtc.Value.Kind == DateTimeKind.Local)
            throw new DomainException("RequiredDateUtc must be supplied in UTC.");

        var order = await _db.SalesOrders.Include(o => o.Lines).SingleOrDefaultAsync(o => o.Id == id, ct);
        if (order is null) return NotFound();
        if (order.Status != SalesOrderStatus.Draft)
            throw new DomainException("Only draft sales orders can be edited.");
        if (order.Lines.Any(l => l.QuantityReserved != 0 || l.QuantityPicked != 0 || l.QuantityDispatched != 0))
            throw new DomainException("An order with reservation or fulfillment activity cannot be edited.");
        _db.SetOriginalVersion(order, request.Version);

        var customer = await _db.Customers.SingleOrDefaultAsync(c => c.Id == request.CustomerId && c.IsActive, ct);
        if (customer is null) throw new DomainException("The customer does not exist or is inactive.");
        var productIds = request.Lines.Select(l => l.ProductId).Distinct().ToArray();
        var activeProductIds = await _db.Products.Where(p => productIds.Contains(p.Id) && p.IsActive).Select(p => p.Id).ToListAsync(ct);
        if (activeProductIds.Count != productIds.Length)
            throw new DomainException("One or more products do not exist or are inactive.");

        _db.SalesOrderLines.RemoveRange(order.Lines);
        order.CustomerId = customer.Id;
        order.RequiredDateUtc = request.RequiredDateUtc;
        order.Notes = request.Notes?.Trim() ?? string.Empty;
        order.UpdatedAtUtc = DateTime.UtcNow;
        order.Lines = request.Lines.Select(line => new SalesOrderLine
        {
            SalesOrderId = order.Id, ProductId = line.ProductId,
            QuantityOrdered = line.QuantityOrdered, UnitPrice = line.UnitPrice
        }).ToList();
        _audit.Record("SalesOrder.DraftUpdated", nameof(SalesOrder), order.Id, User.GetUserId(), details: order.OrderNumber);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
    [HttpPost("{id:guid}/confirm")]
    [Authorize(Policy = Policies.ManageSalesOrders)]
    public async Task<IActionResult> Confirm(Guid id, CancellationToken ct)
    {
        var order = await _db.SalesOrders
            .SingleOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
            return NotFound();
        if (order.Status != SalesOrderStatus.Draft)
            throw new DomainException("Only draft sales orders can be confirmed.");

        order.Status = SalesOrderStatus.Confirmed;
        order.UpdatedAtUtc = DateTime.UtcNow;
        _audit.Record("SalesOrder.Confirmed", nameof(SalesOrder), order.Id,
            User.GetUserId(), details: order.OrderNumber);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = Policies.ManageSalesOrders)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var order = await _db.SalesOrders
            .SingleOrDefaultAsync(o => o.Id == id, ct);
        if (order is null)
            return NotFound();
        if (order.Status is not (SalesOrderStatus.Draft or SalesOrderStatus.Confirmed))
            throw new DomainException(
                "Only draft or confirmed orders can be cancelled here. Orders with reservations or fulfillment activity require the release/cancellation workflow.");

        order.Status = SalesOrderStatus.Cancelled;
        order.UpdatedAtUtc = DateTime.UtcNow;
        _audit.Record("SalesOrder.Cancelled", nameof(SalesOrder), order.Id,
            User.GetUserId(), details: order.OrderNumber);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }
}

public sealed record CreateSalesOrderRequest(
    Guid CustomerId,
    List<CreateSalesOrderLineRequest> Lines,
    DateTime? RequiredDateUtc = null,
    string? Notes = null);

public sealed record CreateSalesOrderLineRequest(
    Guid ProductId,
    decimal QuantityOrdered,
    decimal UnitPrice);

public sealed record UpdateSalesOrderRequest(
    Guid CustomerId,
    List<CreateSalesOrderLineRequest> Lines,
    uint Version,
    DateTime? RequiredDateUtc = null,
    string? Notes = null);

public sealed record SalesOrderListItem(
    Guid Id,
    string OrderNumber,
    string CustomerName,
    SalesOrderStatus Status,
    DateTime? RequiredDateUtc,
    decimal TotalQuantity,
    decimal TotalValue);

public sealed record SalesOrderLineDetail(
    Guid Id,
    Guid ProductId,
    string Sku,
    string ProductName,
    decimal QuantityOrdered,
    decimal QuantityReserved,
    decimal QuantityPicked,
    decimal QuantityDispatched,
    decimal UnitPrice,
    decimal LineTotal);

public sealed record SalesOrderDetail(
    Guid Id,
    string OrderNumber,
    Guid CustomerId,
    string CustomerName,
    SalesOrderStatus Status,
    DateTime? RequiredDateUtc,
    string Notes,
    Guid CreatedByUserId,
    DateTime CreatedAtUtc,
    List<SalesOrderLineDetail> Lines,
    decimal TotalValue,
    uint Version);
