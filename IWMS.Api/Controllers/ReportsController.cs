using System.Globalization;
using System.Text;
using IWMS.Api.Security;
using IWMS.Api.Reports;
using IWMS.Application;
using IWMS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IWMS.Api.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize(Policy = Policies.ViewData)]
public sealed class ReportsController : ControllerBase
{
    private readonly IAppDbContext _db;

    public ReportsController(IAppDbContext db) => _db = db;

    [HttpGet("dashboard")]
    public async Task<ActionResult<DashboardReport>> Dashboard(CancellationToken ct)
    {
        var balances = await _db.InventoryBalances.AsNoTracking()
            .Select(b => new
            {
                b.OnHand,
                b.Reserved,
                Cost = b.Product.CostPrice ?? 0m,
                ReorderLevel = b.Product.ReorderLevel ?? 0m,
                ProductActive = b.Product.IsActive
            }).ToListAsync(ct);

        var stockValue = balances.Sum(b => b.OnHand * b.Cost);
        var lowStockCount = await _db.Products.AsNoTracking()
            .Where(p => p.IsActive &&
                (p.Balances.Sum(b => (decimal?)b.OnHand) ?? 0m) -
                (p.Balances.Sum(b => (decimal?)b.Reserved) ?? 0m) <=
                (p.ReorderLevel ?? 0m))
            .CountAsync(ct);

        var openPurchaseOrders = await _db.PurchaseOrders.AsNoTracking()
            .CountAsync(o => o.Status == PurchaseOrderStatus.Submitted ||
                             o.Status == PurchaseOrderStatus.Approved ||
                             o.Status == PurchaseOrderStatus.PartiallyReceived, ct);

        var pendingApprovals = await _db.PurchaseOrders.AsNoTracking()
            .CountAsync(o => o.Status == PurchaseOrderStatus.Submitted, ct);

        var openSalesOrders = await _db.SalesOrders.AsNoTracking()
            .CountAsync(o => o.Status != SalesOrderStatus.Cancelled &&
                             o.Status != SalesOrderStatus.Dispatched, ct);

        return Ok(new DashboardReport(
            stockValue,
            lowStockCount,
            openPurchaseOrders,
            pendingApprovals,
            openSalesOrders,
            balances.Where(b => b.ProductActive).Sum(b => b.OnHand),
            balances.Where(b => b.ProductActive).Sum(b => b.Reserved)));
    }

    [HttpGet("inventory-trend")]
    public async Task<ActionResult<IReadOnlyList<InventoryTrendPoint>>> InventoryTrend(
        [FromQuery] int days = 30,
        CancellationToken ct = default)
    {
        if (days < 1 || days > 366)
            return BadRequest("Days must be between 1 and 366.");

        var from = DateTime.UtcNow.Date.AddDays(-days + 1);
        var movements = await _db.InventoryMovements.AsNoTracking()
            .Where(m => m.CreatedAtUtc >= from)
            .GroupBy(m => m.CreatedAtUtc.Date)
            .Select(g => new { Date = g.Key, Receipts = g.Sum(m => m.QuantityChange > 0 ? m.QuantityChange : 0m),
                Issues = g.Sum(m => m.QuantityChange < 0 ? -m.QuantityChange : 0m), Net = g.Sum(m => m.QuantityChange) })
            .OrderBy(x => x.Date)
            .ToListAsync(ct);

        var byDate = movements.ToDictionary(x => x.Date.Date);
        var result = Enumerable.Range(0, days).Select(offset =>
        {
            var date = from.AddDays(offset);
            if (byDate.TryGetValue(date, out var point))
                return new InventoryTrendPoint(date, point.Receipts, point.Issues, point.Net);
            return new InventoryTrendPoint(date, 0m, 0m, 0m);
        }).ToList();

        return Ok(result);
    }

    [HttpGet("inventory.csv")]
    public async Task<IActionResult> ExportInventoryCsv(CancellationToken ct)
    {
        var rows = await _db.InventoryBalances.AsNoTracking()
            .OrderBy(b => b.Product.Sku)
            .ThenBy(b => b.StorageLocation.Code)
            .Select(b => new
            {
                b.Product.Sku,
                Product = b.Product.Name,
                Warehouse = b.StorageLocation.Warehouse.Name,
                Location = b.StorageLocation.Code,
                b.OnHand,
                b.Reserved,
                Available = b.OnHand - b.Reserved,
                UnitCost = b.Product.CostPrice ?? 0m,
                Value = b.OnHand * (b.Product.CostPrice ?? 0m)
            }).ToListAsync(ct);

        var csv = new StringBuilder();
        csv.AppendLine("SKU,Product,Warehouse,Location,OnHand,Reserved,Available,UnitCost,StockValue");
        foreach (var row in rows)
        {
            csv.Append(Csv(row.Sku)).Append(',')
                .Append(Csv(row.Product)).Append(',')
                .Append(Csv(row.Warehouse)).Append(',')
                .Append(Csv(row.Location)).Append(',')
                .Append(row.OnHand.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.Reserved.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.Available.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.UnitCost.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(row.Value.ToString(CultureInfo.InvariantCulture)).AppendLine();
        }

        return File(new UTF8Encoding(true).GetBytes(csv.ToString()), "text/csv; charset=utf-8",
            $"iwms-inventory-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");
    }


    [HttpGet("inventory.pdf")]
    public async Task<IActionResult> ExportInventoryPdf(CancellationToken ct)
    {
        var rows = await _db.InventoryBalances.AsNoTracking()
            .OrderBy(b => b.Product.Sku)
            .ThenBy(b => b.StorageLocation.Code)
            .Select(b => new
            {
                b.Product.Sku,
                Product = b.Product.Name,
                Warehouse = b.StorageLocation.Warehouse.Name,
                Location = b.StorageLocation.Code,
                b.OnHand,
                b.Reserved,
                Available = b.OnHand - b.Reserved,
                Value = b.OnHand * (b.Product.CostPrice ?? 0m)
            }).ToListAsync(ct);

        var lines = new List<string>
        {
            "IWMS Inventory Report",
            $"Generated UTC: {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
            "",
            "SKU | Product | Warehouse/Location | On hand | Reserved | Available | Stock value",
            new string('-', 110)
        };
        lines.AddRange(rows.Select(r =>
            $"{r.Sku} | {r.Product} | {r.Warehouse}/{r.Location} | {r.OnHand} | {r.Reserved} | {r.Available} | {r.Value}"));
        lines.Add("");
        lines.Add($"Total stock value: {rows.Sum(r => r.Value)}");

        var bytes = SimplePdfWriter.Create(lines);
        return File(bytes, "application/pdf", $"iwms-inventory-{DateTime.UtcNow:yyyyMMdd-HHmmss}.pdf");
    }

    private static string Csv(string? value)
    {
        value ??= string.Empty;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}

public sealed record DashboardReport(
    decimal StockValue,
    int LowStockProducts,
    int OpenPurchaseOrders,
    int PendingApprovals,
    int OpenSalesOrders,
    decimal UnitsOnHand,
    decimal UnitsReserved);

public sealed record InventoryTrendPoint(
    DateTime DateUtc,
    decimal Receipts,
    decimal Issues,
    decimal NetMovement);
