using IWMS.Api.Dtos;
using IWMS.Api.Security;
using IWMS.Application;
using IWMS.Application.Ledger;
using IWMS.Application.Receiving;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IWMS.Api.Controllers;

[ApiController]
[Route("api/receipts")]
[Authorize(Policy = Policies.ViewData)]
public class ReceiptsController : ControllerBase
{
    private readonly GoodsReceivingService _service;
    private readonly StockReversalService _reversal;
    private readonly IAppDbContext _db;
    private readonly IAuthorizationService _authorization;

    public ReceiptsController(
        GoodsReceivingService service,
        StockReversalService reversal,
        IAppDbContext db,
        IAuthorizationService authorization)
    {
        _service = service;
        _reversal = reversal;
        _db = db;
        _authorization = authorization;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<ReceiptListItem>>> List(
        [FromQuery] Guid? purchaseOrderId,
        [FromQuery] bool? hasDiscrepancy,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var query = _db.GoodsReceipts.AsNoTracking();

        if (purchaseOrderId is not null)
            query = query.Where(r => r.PurchaseOrderId == purchaseOrderId);

        if (hasDiscrepancy is not null)
        {
            var wanted = hasDiscrepancy.Value;
            query = query.Where(r =>
                r.Lines.Any(l => l.QuantityRejected > 0 || l.DiscrepancyNote != "") == wanted);
        }

        var result = await query
            .OrderByDescending(r => r.ReceivedAtUtc)
            .Select(r => new ReceiptListItem(
                r.Id,
                r.ReceiptNumber,
                r.PurchaseOrder.OrderNumber,
                r.ReceivedAtUtc,
                r.ReceivedByUser.FirstName + " " + r.ReceivedByUser.LastName,
                r.Lines.Sum(l => l.QuantityAccepted),
                r.Lines.Sum(l => l.QuantityRejected),
                r.Lines.Any(l => l.QuantityRejected > 0 || l.DiscrepancyNote != "")))
            .ToPagedAsync(page, pageSize, ct);

        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ReceiptDetail>> Get(Guid id, CancellationToken ct)
    {
        var receipt = await _db.GoodsReceipts.AsNoTracking()
            .Where(r => r.Id == id)
            .Select(r => new ReceiptDetail(
                r.Id,
                r.ReceiptNumber,
                r.PurchaseOrder.OrderNumber,
                r.ReceivedAtUtc,
                r.ReceivedByUser.FirstName + " " + r.ReceivedByUser.LastName,
                r.Notes,
                r.Lines.Select(l => new ReceiptLineItem(
                    l.PurchaseOrderLine.Product.Sku,
                    l.PurchaseOrderLine.Product.Name,
                    l.StorageLocation.Code,
                    l.QuantityAccepted,
                    l.QuantityRejected,
                    l.DiscrepancyNote)).ToList()))
            .SingleOrDefaultAsync(ct);

        if (receipt is null)
            return NotFound();

        return Ok(receipt);
    }

    [Authorize(Policy = Policies.ReceiveGoods)]
    [HttpPost]
    public async Task<ActionResult<ReceiptResult>> Receive(
        ReceiveGoodsRequest request, CancellationToken ct)
    {
        var overReceiveAllowed =
            (await _authorization.AuthorizeAsync(User, Policies.OverReceive)).Succeeded;

        var secured = request with
        {
            ReceivedByUserId = User.GetUserId(),
            AllowOverReceipt = request.AllowOverReceipt && overReceiveAllowed
        };

        var result = await _service.ReceiveAsync(secured, ct);

        if (result.WasDuplicate)
            return Ok(result);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    [Authorize(Policy = Policies.ReverseMovements)]
    [HttpPost("{id:guid}/reverse")]
    public async Task<ActionResult<ReversalResult>> Reverse(
        Guid id, ReasonRequest request, CancellationToken ct)
    {
        var result = await _reversal.ReverseReceiptAsync(id, request.Reason, User.GetUserId(), ct);
        return Ok(result);
    }
}