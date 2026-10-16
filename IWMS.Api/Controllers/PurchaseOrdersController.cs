using IWMS.Api.Dtos;
using IWMS.Application;
using IWMS.Domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IWMS.Api.Controllers;

[ApiController]
[Route("api/purchase-orders")]
public class PurchaseOrdersController : ControllerBase
{
    private readonly IAppDbContext _db;

    public PurchaseOrdersController(IAppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<PurchaseOrderListItem>>> List(
        [FromQuery] PurchaseOrderStatus? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var query = _db.PurchaseOrders.AsNoTracking();

        if (status is not null)
            query = query.Where(o => o.Status == status);

        var result = await query
            .OrderByDescending(o => o.CreatedAtUtc)
            .Select(o => new PurchaseOrderListItem(
                o.Id,
                o.OrderNumber,
                o.Supplier.Name,
                o.Status,
                o.ExpectedDeliveryUtc,
                o.Lines.Sum(l => l.QuantityOrdered),
                o.Lines.Sum(l => l.QuantityReceived)))
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
                o.Supplier.Name,
                o.Status,
                o.ExpectedDeliveryUtc,
                o.Notes,
                o.Lines.Select(l => new PurchaseOrderLineItem(
                    l.Id,
                    l.ProductId,
                    l.Product.Sku,
                    l.Product.Name,
                    l.QuantityOrdered,
                    l.QuantityReceived,
                    l.QuantityOrdered - l.QuantityReceived,
                    l.ExpectedUnitPrice)).ToList()))
            .SingleOrDefaultAsync(ct);

        if (order is null)
            return NotFound();

        return Ok(order);
    }
}