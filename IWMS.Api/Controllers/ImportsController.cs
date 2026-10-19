using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IWMS.Api.Security;
using IWMS.Application;
using IWMS.Application.Audit;
using IWMS.Application.Ledger;
using IWMS.Domain.Common;
using IWMS.Domain.Entities;
using IWMS.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IWMS.Api.Controllers;

[ApiController]
[Route("api/imports")]
public sealed class ImportsController : ControllerBase
{
    private readonly IAppDbContext _db;
    private readonly AuditLogger _audit;
    private readonly StockLedgerService _ledger;

    public ImportsController(IAppDbContext db, AuditLogger audit, StockLedgerService ledger)
    {
        _db = db;
        _audit = audit;
        _ledger = ledger;
    }

    [HttpPost("products")]
    [Authorize(Policy = Policies.ManageMasterData)]
    public async Task<ActionResult> Products(CsvDocumentRequest request, CancellationToken ct)
    {
        var table = Parse(request.Csv);
        var created = 0;
        var updated = 0;
        var errors = new List<string>();
        var rowNumber = 1;
        foreach (var row in table)
        {
            rowNumber++;
            try
            {
                var sku = Required(row, "Sku");
                var name = Required(row, "Name");
                var categoryName = Required(row, "Category");
                var category = await _db.Categories.SingleOrDefaultAsync(c => c.Name == categoryName, ct);
                if (category is null)
                {
                    category = new Category { Name = categoryName, Description = "Created by catalogue import" };
                    _db.Categories.Add(category);
                    await _db.SaveChangesAsync(ct);
                }

                if (!Enum.TryParse<UnitOfMeasure>(Optional(row, "BaseUnit") ?? "Each", true, out var unit))
                    throw new DomainException($"Unknown base unit '{Optional(row, "BaseUnit")}'.");

                var product = await _db.Products.SingleOrDefaultAsync(p => p.Sku == sku, ct);
                var barcode = BlankToNull(Optional(row, "Barcode"));
                if (barcode is not null && await _db.Products.AnyAsync(p => p.Barcode == barcode && p.Sku != sku, ct))
                    throw new DomainException($"Barcode '{barcode}' is already used.");

                if (product is null)
                {
                    product = new Product { Sku = sku };
                    _db.Products.Add(product);
                    created++;
                }
                else
                {
                    updated++;
                }

                product.Name = name;
                product.Description = Optional(row, "Description") ?? string.Empty;
                product.Barcode = barcode;
                product.CategoryId = category.Id;
                product.BaseUnit = unit;
                product.CostPrice = ParseDecimal(Optional(row, "CostPrice"));
                product.ReorderLevel = ParseDecimal(Optional(row, "ReorderLevel")) ?? 0m;
                product.TrackLots = ParseBool(Optional(row, "TrackLots"));
                product.TrackSerials = ParseBool(Optional(row, "TrackSerials"));
                product.IsActive = true;
                product.UpdatedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }
            catch (Exception ex) when (ex is DomainException or FormatException)
            {
                errors.Add($"Row {rowNumber}: {ex.Message}");
            }
        }

        _audit.Record("Catalog.Imported", nameof(Product), Guid.Empty, User.GetUserId(),
            details: $"Created {created}, updated {updated}, errors {errors.Count}.");
        await _db.SaveChangesAsync(ct);
        return Ok(new { created, updated, errors });
    }

    [HttpPost("opening-stock")]
    [Authorize(Policy = Policies.PostAdjustment)]
    public async Task<ActionResult> OpeningStock(CsvDocumentRequest request, CancellationToken ct)
    {
        var table = Parse(request.Csv);
        var batch = request.BatchId ?? Guid.NewGuid();
        var posted = 0;
        var duplicates = 0;
        var errors = new List<string>();
        var rowNumber = 1;
        foreach (var row in table)
        {
            rowNumber++;
            try
            {
                var sku = Required(row, "Sku");
                var warehouseCode = Required(row, "WarehouseCode").ToUpperInvariant();
                var locationCode = Required(row, "LocationCode").ToUpperInvariant();
                var quantity = ParseDecimal(Required(row, "Quantity"))
                    ?? throw new DomainException("Quantity is required.");
                if (quantity <= 0)
                    throw new DomainException("Quantity must be greater than zero.");

                var product = await _db.Products.AsNoTracking().SingleOrDefaultAsync(p => p.Sku == sku && p.IsActive, ct)
                    ?? throw new DomainException($"Product '{sku}' was not found.");
                var location = await _db.StorageLocations.AsNoTracking()
                    .SingleOrDefaultAsync(l => l.IsActive && l.Code == locationCode && l.Warehouse.Code == warehouseCode, ct)
                    ?? throw new DomainException($"Location '{warehouseCode}/{locationCode}' was not found.");

                DateTime? expiry = null;
                var expiryText = Optional(row, "ExpiryUtc");
                if (!string.IsNullOrWhiteSpace(expiryText))
                {
                    if (!DateTime.TryParse(expiryText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                        throw new DomainException("ExpiryUtc must be a date.");
                    expiry = parsed;
                }

                var result = await _ledger.AdjustAsync(new AdjustStockRequest(
                    product.Id,
                    location.Id,
                    quantity,
                    true,
                    "Opening stock import",
                    User.GetUserId(),
                    RowKey(batch, rowNumber),
                    Optional(row, "LotNumber"),
                    Optional(row, "Serials")), ct);
                if (result.WasDuplicate) duplicates++;
                else posted++;

                if (expiry is not null && !string.IsNullOrWhiteSpace(Optional(row, "LotNumber")))
                {
                    var lot = await _db.InventoryLots.SingleOrDefaultAsync(
                        l => l.ProductId == product.Id && l.StorageLocationId == location.Id && l.LotNumber == Optional(row, "LotNumber"), ct);
                    if (lot is not null)
                    {
                        lot.ExpiryUtc = expiry;
                        await _db.SaveChangesAsync(ct);
                    }
                }
            }
            catch (Exception ex) when (ex is DomainException or FormatException)
            {
                errors.Add($"Row {rowNumber}: {ex.Message}");
            }
        }

        return Ok(new { batchId = batch, posted, duplicates, errors });
    }

    private static Guid RowKey(Guid batch, int row)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{batch:N}:{row}"));
        Span<byte> guid = stackalloc byte[16];
        bytes.AsSpan(0, 16).CopyTo(guid);
        return new Guid(guid);
    }

    private static List<Dictionary<string, string>> Parse(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
            throw new DomainException("Paste or upload a CSV file.");
        var lines = csv.Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2)
            throw new DomainException("The CSV needs a header row and at least one data row.");
        var headers = Split(lines[0]).Select(NormalizeHeader).ToArray();
        var rows = new List<Dictionary<string, string>>();
        foreach (var line in lines.Skip(1))
        {
            var cells = Split(line);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Length && i < cells.Count; i++)
                row[headers[i]] = cells[i];
            if (row.Values.Any(value => !string.IsNullOrWhiteSpace(value)))
                rows.Add(row);
        }
        return rows;
    }

    private static List<string> Split(string line)
    {
        var cells = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else quoted = !quoted;
            }
            else if (ch == ',' && !quoted)
            {
                cells.Add(current.ToString().Trim());
                current.Clear();
            }
            else current.Append(ch);
        }
        cells.Add(current.ToString().Trim());
        return cells;
    }

    private static string NormalizeHeader(string header) =>
        header.Replace(" ", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal);

    private static string Required(Dictionary<string, string> row, string name) =>
        Optional(row, name) ?? throw new DomainException($"{name} is required.");

    private static string? Optional(Dictionary<string, string> row, string name) =>
        row.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

    private static string? BlankToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static decimal? ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed))
            throw new FormatException($"'{value}' is not a number.");
        return parsed;
    }

    private static bool ParseBool(string? value) =>
        value is not null && (value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                              value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                              value.Equals("1", StringComparison.OrdinalIgnoreCase));
}

public sealed record CsvDocumentRequest(string Csv, Guid? BatchId = null);
