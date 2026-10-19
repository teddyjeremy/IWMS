using IWMS.Domain.Common;
using IWMS.Domain.Entities;
using IWMS.Domain.Enums;
using IWMS.Domain.Tracking;
using Microsoft.EntityFrameworkCore;

namespace IWMS.Application.Tracking;

public sealed class LotTrackingService
{
    private readonly IAppDbContext _db;

    public LotTrackingService(IAppDbContext db) => _db = db;

    public async Task ApplyIncreaseAsync(
        Guid productId,
        Guid locationId,
        decimal quantity,
        string? lotNumber,
        DateTime? expiryUtc,
        string? serials,
        CancellationToken ct = default)
    {
        if (quantity <= 0)
            return;

        var product = await _db.Products.AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => new { p.TrackLots, p.TrackSerials })
            .SingleAsync(ct);

        if (product.TrackLots)
        {
            if (string.IsNullOrWhiteSpace(lotNumber))
                throw new DomainException("This product is lot-tracked. Enter a lot or batch number.");
            var number = lotNumber.Trim();
            var lot = await _db.InventoryLots.SingleOrDefaultAsync(
                l => l.ProductId == productId && l.StorageLocationId == locationId && l.LotNumber == number, ct);
            if (lot is null)
            {
                lot = new InventoryLot
                {
                    ProductId = productId,
                    StorageLocationId = locationId,
                    LotNumber = number,
                    ExpiryUtc = expiryUtc?.ToUniversalTime(),
                    QuantityOnHand = 0
                };
                _db.InventoryLots.Add(lot);
            }
            else if (expiryUtc is not null)
            {
                lot.ExpiryUtc = expiryUtc.Value.ToUniversalTime();
            }
            lot.QuantityOnHand += quantity;
            lot.UpdatedAtUtc = DateTime.UtcNow;
        }

        if (product.TrackSerials)
            await AddSerialsAsync(productId, locationId, quantity, serials, ct);
    }

    public async Task<IReadOnlyList<FefoAllocator.Take<InventoryLot>>> ApplyDecreaseAsync(
        Guid productId,
        Guid locationId,
        decimal quantity,
        string? lotNumber,
        string? serials,
        CancellationToken ct = default)
    {
        if (quantity <= 0)
            return Array.Empty<FefoAllocator.Take<InventoryLot>>();

        var product = await _db.Products.AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => new { p.TrackLots, p.TrackSerials })
            .SingleAsync(ct);

        IReadOnlyList<FefoAllocator.Take<InventoryLot>> takes = Array.Empty<FefoAllocator.Take<InventoryLot>>();
        if (product.TrackLots)
        {
            var query = _db.InventoryLots.Where(l => l.ProductId == productId && l.StorageLocationId == locationId && l.QuantityOnHand > 0);
            if (!string.IsNullOrWhiteSpace(lotNumber))
            {
                var number = lotNumber.Trim();
                query = query.Where(l => l.LotNumber == number);
            }
            var lots = await query.ToListAsync(ct);
            takes = FefoAllocator.Allocate(lots, quantity, lot => lot.QuantityOnHand, lot => lot.ExpiryUtc, lot => lot.LotNumber);
            foreach (var take in takes)
            {
                take.Source.QuantityOnHand -= take.Quantity;
                take.Source.UpdatedAtUtc = DateTime.UtcNow;
            }
        }

        if (product.TrackSerials)
            await IssueSerialsAsync(productId, locationId, quantity, serials, SerialStatus.Issued, ct);

        return takes;
    }

    public async Task<IReadOnlyList<FefoSuggestion>> SuggestAsync(
        Guid productId, Guid locationId, decimal quantity, CancellationToken ct = default)
    {
        var lots = await _db.InventoryLots.AsNoTracking()
            .Where(l => l.ProductId == productId && l.StorageLocationId == locationId && l.QuantityOnHand > 0)
            .ToListAsync(ct);
        return FefoAllocator.Allocate(lots, quantity, lot => lot.QuantityOnHand, lot => lot.ExpiryUtc, lot => lot.LotNumber)
            .Select(take => new FefoSuggestion(take.Source.LotNumber, take.Source.ExpiryUtc, take.Quantity, take.Source.QuantityOnHand))
            .ToList();
    }

    public static IReadOnlyList<string> ParseSerials(string? serials, decimal quantity)
    {
        if (quantity != decimal.Truncate(quantity))
            throw new DomainException("Serial-tracked products must move in whole units.");
        var expected = (int)quantity;
        var values = (serials ?? string.Empty)
            .Split(new[] { ',', '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (values.Count != expected)
            throw new DomainException($"Enter exactly {expected} serial number(s), separated by commas.");
        return values;
    }

    private async Task AddSerialsAsync(Guid productId, Guid locationId, decimal quantity, string? serials, CancellationToken ct)
    {
        foreach (var serial in ParseSerials(serials, quantity))
        {
            var existing = await _db.TrackedSerials.SingleOrDefaultAsync(
                s => s.ProductId == productId && s.Serial.ToLower() == serial.ToLower(), ct);
            if (existing is not null && existing.Status is SerialStatus.InStock or SerialStatus.InTransit)
                throw new DomainException($"Serial {serial} is already in stock.");
            if (existing is null)
            {
                _db.TrackedSerials.Add(new TrackedSerial
                {
                    ProductId = productId,
                    StorageLocationId = locationId,
                    Serial = serial,
                    Status = SerialStatus.InStock
                });
            }
            else
            {
                existing.Status = SerialStatus.InStock;
                existing.StorageLocationId = locationId;
                existing.UpdatedAtUtc = DateTime.UtcNow;
            }
        }
    }

    private async Task IssueSerialsAsync(
        Guid productId, Guid locationId, decimal quantity, string? serials, SerialStatus status, CancellationToken ct)
    {
        var requested = string.IsNullOrWhiteSpace(serials) ? null : ParseSerials(serials, quantity);
        var available = await _db.TrackedSerials
            .Where(s => s.ProductId == productId && s.StorageLocationId == locationId && s.Status == SerialStatus.InStock)
            .OrderBy(s => s.CreatedAtUtc)
            .ToListAsync(ct);

        List<TrackedSerial> chosen;
        if (requested is null)
        {
            if (quantity != decimal.Truncate(quantity))
                throw new DomainException("Serial-tracked products must move in whole units.");
            if (available.Count < (int)quantity)
                throw new DomainException("Not enough serial numbers are in stock at this location.");
            chosen = available.Take((int)quantity).ToList();
        }
        else
        {
            chosen = new List<TrackedSerial>();
            foreach (var serial in requested)
            {
                var match = available.SingleOrDefault(s => s.Serial.Equals(serial, StringComparison.OrdinalIgnoreCase))
                    ?? throw new DomainException($"Serial {serial} is not in stock at this location.");
                chosen.Add(match);
            }
        }

        foreach (var serial in chosen)
        {
            serial.Status = status;
            if (status == SerialStatus.Issued)
                serial.StorageLocationId = null;
            serial.UpdatedAtUtc = DateTime.UtcNow;
        }
    }
}

public sealed record FefoSuggestion(string LotNumber, DateTime? ExpiryUtc, decimal Quantity, decimal LotOnHand);
