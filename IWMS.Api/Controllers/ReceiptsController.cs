using IWMS.Api.Dtos;
using IWMS.Application;
using IWMS.Application.Receiving;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IWMS.Api.Controllers;

[ApiController]
[Route("api/receipts")]
public class ReceiptsController : ControllerBase
{
    private readonly GoodsReceivingService _service;
    private readonly IAppDbContext _db;

    public ReceiptsController(GoodsReceivingService service, IAppDbContext db)
    {
        _service = service;
        _db = db;
    }

    [HttpPost]
    public async Task<ActionResult<ReceiptResult>> Receive(
        ReceiveGoodsRequest request, CancellationToken ct)
    {
        var result = await _service.ReceiveAsync(request, ct);

        if (result.WasDuplicate)
            return Ok(result);

        return StatusCode(StatusCodes.Status201Created, result);
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
}