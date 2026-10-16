using IWMS.Api.Dtos;
using IWMS.Api.Security;
using IWMS.Application;
using IWMS.Application.Ledger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IWMS.Api.Controllers;

[ApiController]
[Route("api/stock")]
[Authorize(Policy = Policies.ViewData)]
public class StockController : ControllerBase
{
    private readonly StockLedgerService _ledger;
    private readonly IAppDbContext _db;

    public StockController(StockLedgerService ledger, IAppDbContext db)
    {
        _ledger = ledger;
        _db = db;
    }

    [Authorize(Policy = Policies.PostIssue)]
    [HttpPost("issue")]
    public async Task<ActionResult<StockPostingResult>> Issue(
        IssueStockRequest request, CancellationToken ct) =>
        ToResponse(await _ledger.IssueAsync(request with { PerformedByUserId = User.GetUserId() }, ct));

    [Authorize(Policy = Policies.PostAdjustment)]
    [HttpPost("adjust")]
    public async Task<ActionResult<StockPostingResult>> Adjust(
        AdjustStockRequest request, CancellationToken ct) =>
        ToResponse(await _ledger.AdjustAsync(request with { PerformedByUserId = User.GetUserId() }, ct));

    [Authorize(Policy = Policies.PostTransfer)]
    [HttpPost("transfer")]
    public async Task<ActionResult<StockPostingResult>> Transfer(
        TransferStockRequest request, CancellationToken ct) =>
        ToResponse(await _ledger.TransferAsync(request with { PerformedByUserId = User.GetUserId() }, ct));

    [Authorize(Policy = Policies.ViewAudit)]
    [HttpGet("reconciliation")]
    public async Task<ActionResult<List<ReconciliationMismatch>>> Reconcile(CancellationToken ct)
    {
        var ledgerTotals = await _db.InventoryMovements
            .GroupBy(m => new { m.ProductId, m.StorageLocationId })
            .Select(g => new
            {
                g.Key.ProductId,
                g.Key.StorageLocationId,
                Total = g.Sum(m => m.QuantityChange)
            })
            .ToListAsync(ct);

        var balances = await _db.InventoryBalances.AsNoTracking()
            .Select(b => new { b.ProductId, b.StorageLocationId, b.OnHand })
            .ToListAsync(ct);

        var ledgerByKey = ledgerTotals.ToDictionary(
            x => (x.ProductId, x.StorageLocationId), x => x.Total);
        var balanceByKey = balances.ToDictionary(
            x => (x.ProductId, x.StorageLocationId), x => x.OnHand);

        var mismatches = new List<ReconciliationMismatch>();

        foreach (var key in ledgerByKey.Keys.Union(balanceByKey.Keys))
        {
            var ledgerTotal = ledgerByKey.GetValueOrDefault(key);
            var onHand = balanceByKey.GetValueOrDefault(key);

            if (ledgerTotal != onHand)
                mismatches.Add(new ReconciliationMismatch(
                    key.ProductId, key.StorageLocationId, onHand, ledgerTotal));
        }

        return Ok(mismatches);
    }

    private ActionResult<StockPostingResult> ToResponse(StockPostingResult result)
    {
        if (result.WasDuplicate)
            return Ok(result);

        return StatusCode(StatusCodes.Status201Created, result);
    }
}