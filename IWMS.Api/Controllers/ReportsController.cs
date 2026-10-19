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
                             o.Status != SalesOrderStatus.Dispatched &&
                             o.Status != SalesOrderStatus.ClosedShort, ct);

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

    [HttpGet("alerts")]
    public async Task<ActionResult<List<AlertItem>>> Alerts(CancellationToken ct)
    {
        var alerts = new List<AlertItem>();
        var lowStock = await _db.Products.AsNoTracking()
            .Where(p => p.IsActive &&
                (p.Balances.Sum(b => (decimal?)b.OnHand) ?? 0m) -
                (p.Balances.Sum(b => (decimal?)b.Reserved) ?? 0m) <= (p.ReorderLevel ?? 0m))
            .OrderBy(p => p.Sku)
            .Select(p => new
            {
                p.Id,
                p.Sku,
                p.Name,
                Available = (p.Balances.Sum(b => (decimal?)(b.OnHand - b.Reserved)) ?? 0m),
                Reorder = p.ReorderLevel ?? 0m
            })
            .Take(25)
            .ToListAsync(ct);
        alerts.AddRange(lowStock.Select(p => new AlertItem(
            "LowStock", p.Sku, $"{p.Name}: available {p.Available}, reorder {p.Reorder}", p.Id, "Inventory")));

        var pending = await _db.PurchaseOrders.AsNoTracking()
            .Where(o => o.Status == PurchaseOrderStatus.Submitted)
            .OrderBy(o => o.CreatedAtUtc)
            .Select(o => new { o.Id, o.OrderNumber, Supplier = o.Supplier.Name })
            .Take(25)
            .ToListAsync(ct);
        alerts.AddRange(pending.Select(o => new AlertItem(
            "PendingApproval", o.OrderNumber, $"Waiting for approval · {o.Supplier}", o.Id, "Purchasing")));

        var now = DateTime.UtcNow;
        var overdue = await _db.PurchaseOrders.AsNoTracking()
            .Where(o => o.ExpectedDeliveryUtc != null && o.ExpectedDeliveryUtc < now &&
                        (o.Status == PurchaseOrderStatus.Approved || o.Status == PurchaseOrderStatus.PartiallyReceived))
            .OrderBy(o => o.ExpectedDeliveryUtc)
            .Select(o => new { o.Id, o.OrderNumber, o.ExpectedDeliveryUtc, Supplier = o.Supplier.Name })
            .Take(25)
            .ToListAsync(ct);
        alerts.AddRange(overdue.Select(o => new AlertItem(
            "OverduePurchaseOrder", o.OrderNumber, $"Expected {o.ExpectedDeliveryUtc:yyyy-MM-dd} · {o.Supplier}", o.Id, "Purchasing")));

        return Ok(alerts);
    }

    [HttpGet("stock-value")]
    public async Task<ActionResult<List<WarehouseValueRow>>> StockValue(CancellationToken ct)
    {
        var rows = await _db.InventoryBalances.AsNoTracking()
            .Select(b => new
            {
                Warehouse = b.StorageLocation.Warehouse.Name,
                b.OnHand,
                Cost = b.Product.CostPrice ?? 0m
            })
            .ToListAsync(ct);
        var grouped = rows
            .GroupBy(r => r.Warehouse)
            .Select(g => new WarehouseValueRow(g.Key, g.Sum(r => r.OnHand), g.Sum(r => r.OnHand * r.Cost)))
            .OrderByDescending(r => r.StockValue)
            .ToList();
        return Ok(grouped);
    }

    [HttpGet("dead-stock")]
    public async Task<ActionResult<List<DeadStockRow>>> DeadStock([FromQuery] int days = 90, CancellationToken ct = default)
    {
        if (days < 1 || days > 3650)
            throw new IWMS.Domain.Common.DomainException("Days must be between 1 and 3650.");
        var cutoff = DateTime.UtcNow.AddDays(-days);
        var balances = await _db.InventoryBalances.AsNoTracking()
            .Where(b => b.OnHand > 0)
            .Select(b => new
            {
                b.ProductId,
                b.StorageLocationId,
                b.Product.Sku,
                Name = b.Product.Name,
                Warehouse = b.StorageLocation.Warehouse.Name,
                Location = b.StorageLocation.Code,
                b.OnHand,
                Value = b.OnHand * (b.Product.CostPrice ?? 0m)
            })
            .ToListAsync(ct);
        var lastMoves = await _db.InventoryMovements.AsNoTracking()
            .GroupBy(m => new { m.ProductId, m.StorageLocationId })
            .Select(g => new { g.Key.ProductId, g.Key.StorageLocationId, Last = g.Max(m => m.CreatedAtUtc) })
            .ToListAsync(ct);
        var lastByKey = lastMoves.ToDictionary(m => (m.ProductId, m.StorageLocationId), m => m.Last);
        var dead = balances
            .Where(b => !lastByKey.TryGetValue((b.ProductId, b.StorageLocationId), out var last) || last < cutoff)
            .Select(b => new DeadStockRow(
                b.Sku, b.Name, $"{b.Warehouse} / {b.Location}", b.OnHand, b.Value,
                lastByKey.TryGetValue((b.ProductId, b.StorageLocationId), out var last) ? last : null))
            .OrderByDescending(r => r.StockValue)
            .Take(200)
            .ToList();
        return Ok(dead);
    }

    [HttpGet("abc")]
    public async Task<ActionResult<List<AbcRow>>> Abc(CancellationToken ct)
    {
        var rows = await _db.InventoryBalances.AsNoTracking()
            .Select(b => new { b.Product.Sku, Name = b.Product.Name, Value = b.OnHand * (b.Product.CostPrice ?? 0m) })
            .ToListAsync(ct);
        var products = rows
            .GroupBy(r => new { r.Sku, r.Name })
            .Select(g => new { g.Key.Sku, g.Key.Name, Value = g.Sum(x => x.Value) })
            .OrderByDescending(p => p.Value)
            .ToList();
        var total = products.Sum(p => p.Value);
        var running = 0m;
        var result = new List<AbcRow>();
        foreach (var product in products)
        {
            running += product.Value;
            var share = total == 0 ? 0 : product.Value / total * 100m;
            var cumulative = total == 0 ? 0 : running / total * 100m;
            var klass = product.Value <= 0 ? "C" : cumulative <= 80m ? "A" : cumulative <= 95m ? "B" : "C";
            result.Add(new AbcRow(product.Sku, product.Name, product.Value, decimal.Round(share, 2), klass));
        }
        return Ok(result);
    }

    [HttpGet("supplier-performance")]
    public async Task<ActionResult<List<SupplierFillRow>>> SupplierPerformance(CancellationToken ct)
    {
        var lines = await _db.PurchaseOrderLines.AsNoTracking()
            .Where(l => l.PurchaseOrder.Status != PurchaseOrderStatus.Draft &&
                        l.PurchaseOrder.Status != PurchaseOrderStatus.Cancelled &&
                        l.PurchaseOrder.Status != PurchaseOrderStatus.Rejected)
            .Select(l => new
            {
                Supplier = l.PurchaseOrder.Supplier.Name,
                l.PurchaseOrderId,
                l.QuantityOrdered,
                l.QuantityReceived
            })
            .ToListAsync(ct);
        var result = lines
            .GroupBy(l => l.Supplier)
            .Select(g =>
            {
                var ordered = g.Sum(l => l.QuantityOrdered);
                var received = g.Sum(l => l.QuantityReceived);
                var fill = ordered == 0 ? 0 : decimal.Round(received / ordered * 100m, 2);
                return new SupplierFillRow(g.Key, g.Select(l => l.PurchaseOrderId).Distinct().Count(), ordered, received, fill);
            })
            .OrderBy(r => r.FillRatePercent)
            .ToList();
        return Ok(result);
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

public sealed record AlertItem(string Kind, string Title, string Detail, Guid? EntityId, string Workspace);
public sealed record WarehouseValueRow(string Warehouse, decimal UnitsOnHand, decimal StockValue);
public sealed record DeadStockRow(string Sku, string Name, string Location, decimal OnHand, decimal StockValue, DateTime? LastMovementUtc);
public sealed record AbcRow(string Sku, string Name, decimal StockValue, decimal SharePercent, string ClassName);
public sealed record SupplierFillRow(string Supplier, int Orders, decimal OrderedQuantity, decimal ReceivedQuantity, decimal FillRatePercent);
