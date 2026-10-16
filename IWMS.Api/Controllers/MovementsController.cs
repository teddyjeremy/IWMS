using IWMS.Api.Dtos;
using IWMS.Api.Security;
using IWMS.Application;
using IWMS.Application.Ledger;
using IWMS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IWMS.Api.Controllers;

[ApiController]
[Route("api/movements")]
[Authorize(Policy = Policies.ViewData)]
public class MovementsController : ControllerBase
{
    private readonly IAppDbContext _db;
    private readonly StockReversalService _reversal;

    public MovementsController(IAppDbContext db, StockReversalService reversal)
    {
        _db = db;
        _reversal = reversal;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<MovementItem>>> List(
        [FromQuery] Guid? productId,
        [FromQuery] Guid? storageLocationId,
        [FromQuery] MovementType? type,
        [FromQuery] string? reference,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var query = _db.InventoryMovements.AsNoTracking();

        if (productId is not null)
            query = query.Where(m => m.ProductId == productId);
        if (storageLocationId is not null)
            query = query.Where(m => m.StorageLocationId == storageLocationId);
        if (type is not null)
            query = query.Where(m => m.Type == type);
        if (!string.IsNullOrWhiteSpace(reference))
            query = query.Where(m => m.ReferenceNumber.Contains(reference.Trim()));
        if (fromUtc is not null)
        {
            var from = ToUtc(fromUtc.Value);
            query = query.Where(m => m.CreatedAtUtc >= from);
        }
        if (toUtc is not null)
        {
            var to = ToUtc(toUtc.Value);
            query = query.Where(m => m.CreatedAtUtc <= to);
        }

        var result = await query
            .OrderByDescending(m => m.CreatedAtUtc)
            .Select(m => new MovementItem(
                m.Id,
                m.CreatedAtUtc,
                m.Product.Sku,
                m.Product.Name,
                m.StorageLocation.Warehouse.Name,
                m.StorageLocation.Code,
                m.Type,
                m.QuantityChange,
                m.QuantityBefore,
                m.QuantityAfter,
                m.Reason,
                m.ReferenceNumber,
                m.PerformedByUser.FirstName + " " + m.PerformedByUser.LastName))
            .ToPagedAsync(page, pageSize, ct);

        return Ok(result);
    }

    [Authorize(Policy = Policies.ReverseMovements)]
    [HttpPost("{id:guid}/reverse")]
    public async Task<ActionResult<ReversalResult>> Reverse(
        Guid id, ReasonRequest request, CancellationToken ct)
    {
        var result = await _reversal.ReverseMovementAsync(id, request.Reason, User.GetUserId(), ct);
        return Ok(result);
    }

    private static DateTime ToUtc(DateTime value) =>
        value.Kind == DateTimeKind.Local
            ? value.ToUniversalTime()
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}