using IWMS.Api.Dtos;
using IWMS.Api.Reports;
using IWMS.Api.Security;
using IWMS.Application;
using IWMS.Application.Purchasing;
using IWMS.Domain.Entities;
using IWMS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IWMS.Api.Controllers;

[ApiController]
[Route("api/purchase-orders")]
[Authorize(Policy = Policies.ViewData)]
public class PurchaseOrdersController : ControllerBase
{
    private readonly IAppDbContext _db;
    private readonly PurchasingService _purchasing;

    public PurchaseOrdersController(IAppDbContext db, PurchasingService purchasing)
    {
        _db = db;
        _purchasing = purchasing;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<PurchaseOrderListItem>>> List(
        [FromQuery] PurchaseOrderStatus? status,
        [FromQuery] Guid? supplierId,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var query = _db.PurchaseOrders.AsNoTracking();

        if (status is not null)
            query = query.Where(o => o.Status == status);
        if (supplierId is not null)
            query = query.Where(o => o.SupplierId == supplierId);
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(o => o.OrderNumber.Contains(search.Trim()));

        var result = await query
            .OrderByDescending(o => o.CreatedAtUtc)
            .Select(o => new PurchaseOrderListItem(
                o.Id,
                o.OrderNumber,
                o.Supplier.Name,
                o.Status,
                o.ExpectedDeliveryUtc,
                o.CreatedByUser.FirstName + " " + o.CreatedByUser.LastName,
                o.Lines.Sum(l => l.QuantityOrdered),
                o.Lines.Sum(l => l.QuantityReceived),
                o.Lines.Sum(l => l.QuantityOrdered * l.ExpectedUnitPrice)))
            .ToPagedAsync(page, pageSize, ct);

        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PurchaseOrderDetail>> Get(Guid id, CancellationToken ct)
    {
        var order = await _db.PurchaseOrders.AsNoTracking()
            .Where(o => o.Id == id)
            .Select(o => new PurchaseOrderDetail(
                o.Id,
                o.OrderNumber,
                o.SupplierId,
                o.Supplier.Name,
                o.Status,
                o.ExpectedDeliveryUtc,
                o.Notes,
                o.CreatedByUser.FirstName + " " + o.CreatedByUser.LastName,
                o.ApprovedByUser != null
                    ? o.ApprovedByUser.FirstName + " " + o.ApprovedByUser.LastName
                    : null,
                o.ApprovedAtUtc,
                o.RejectionReason,
                o.Lines
                    .OrderBy(l => l.Product.Sku)
                    .Select(l => new PurchaseOrderLineItem(
                        l.Id,
                        l.ProductId,
                        l.Product.Sku,
                        l.Product.Name,
                        l.QuantityOrdered,
                        l.QuantityReceived,
                        l.QuantityOrdered - l.QuantityReceived,
                        l.ExpectedUnitPrice,
                        l.QuantityOrdered * l.ExpectedUnitPrice))
                    .ToList(),
                o.Version))
            .SingleOrDefaultAsync(ct);

        if (order is null)
            return NotFound();

        var history = await _db.AuditEvents.AsNoTracking()
            .Where(a => a.EntityName == nameof(PurchaseOrder) && a.EntityId == id)
            .OrderBy(a => a.CreatedAtUtc)
            .Select(a => new PurchaseOrderHistoryItem(
                a.CreatedAtUtc,
                a.Action,
                a.ActorUser != null
                    ? a.ActorUser.FirstName + " " + a.ActorUser.LastName
                    : "(system)",
                a.Reason))
            .ToListAsync(ct);

        return Ok(order with { History = history });
    }

    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> Pdf(Guid id, CancellationToken ct)
    {
        var order = await _db.PurchaseOrders.AsNoTracking()
            .Where(o => o.Id == id)
            .Select(o => new
            {
                o.OrderNumber,
                Supplier = o.Supplier.Name,
                o.Status,
                o.ExpectedDeliveryUtc,
                o.Notes,
                Lines = o.Lines.OrderBy(l => l.Product.Sku).Select(l => new
                {
                    l.Product.Sku,
                    l.Product.Name,
                    l.QuantityOrdered,
                    l.QuantityReceived,
                    l.ExpectedUnitPrice
                }).ToList()
            })
            .SingleOrDefaultAsync(ct);
        if (order is null)
            return NotFound();

        var lines = new List<string>
        {
            "IWMS Purchase Order",
            order.OrderNumber,
            $"Supplier: {order.Supplier}",
            $"Status: {order.Status}",
            $"Expected delivery UTC: {order.ExpectedDeliveryUtc:yyyy-MM-dd}",
            string.IsNullOrWhiteSpace(order.Notes) ? "" : $"Notes: {order.Notes}",
            "",
            "SKU | Product | Ordered | Received | Unit price | Line total",
            new string('-', 90)
        };
        foreach (var line in order.Lines)
            lines.Add($"{line.Sku} | {line.Name} | {line.QuantityOrdered} | {line.QuantityReceived} | {line.ExpectedUnitPrice} | {line.QuantityOrdered * line.ExpectedUnitPrice}");
        lines.Add("");
        lines.Add($"Order value: {order.Lines.Sum(l => l.QuantityOrdered * l.ExpectedUnitPrice)}");
        return File(SimplePdfWriter.Create(lines), "application/pdf", $"{order.OrderNumber}.pdf");
    }

    [Authorize(Policy = Policies.ManagePurchaseOrders)]
    [HttpPost]
    public async Task<ActionResult<PurchaseOrderCreated>> Create(
        SavePurchaseOrderRequest request, CancellationToken ct)
    {
        var created = await _purchasing.CreateAsync(request, User.GetUserId(), ct);
        return StatusCode(StatusCodes.Status201Created, created);
    }

    [Authorize(Policy = Policies.ManagePurchaseOrders)]
    [HttpPut("{id:guid}")]
    public async Task<ActionResult> Update(
        Guid id, SavePurchaseOrderRequest request, CancellationToken ct)
    {
        await _purchasing.UpdateAsync(id, request, User.GetUserId(), ct);
        return NoContent();
    }

    [Authorize(Policy = Policies.ManagePurchaseOrders)]
    [HttpPost("{id:guid}/submit")]
    public async Task<ActionResult> Submit(Guid id, CancellationToken ct)
    {
        await _purchasing.SubmitAsync(id, User.GetUserId(), ct);
        return NoContent();
    }

    [Authorize(Policy = Policies.ApprovePurchaseOrders)]
    [HttpPost("{id:guid}/approve")]
    public async Task<ActionResult> Approve(Guid id, CancellationToken ct)
    {
        await _purchasing.ApproveAsync(id, User.GetUserId(), ct);
        return NoContent();
    }

    [Authorize(Policy = Policies.ApprovePurchaseOrders)]
    [HttpPost("{id:guid}/reject")]
    public async Task<ActionResult> Reject(Guid id, ReasonRequest request, CancellationToken ct)
    {
        await _purchasing.RejectAsync(id, request.Reason, User.GetUserId(), ct);
        return NoContent();
    }

    [Authorize(Policy = Policies.CancelPurchaseOrders)]
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult> Cancel(Guid id, ReasonRequest request, CancellationToken ct)
    {
        await _purchasing.CancelAsync(id, request.Reason, User.GetUserId(), ct);
        return NoContent();
    }
}