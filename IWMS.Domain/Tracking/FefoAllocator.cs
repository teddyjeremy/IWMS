using IWMS.Domain.Common;

namespace IWMS.Domain.Tracking;

public static class FefoAllocator
{
    public readonly record struct Take<T>(T Source, decimal Quantity);

    public static IReadOnlyList<Take<T>> Allocate<T>(
        IEnumerable<T> lots,
        decimal quantity,
        Func<T, decimal> onHand,
        Func<T, DateTime?> expiry,
        Func<T, string> lotNumber)
    {
        if (quantity <= 0)
            throw new DomainException("Quantity must be greater than zero.");

        var ordered = lots
            .OrderBy(lot => expiry(lot) ?? DateTime.MaxValue)
            .ThenBy(lot => lotNumber(lot), StringComparer.Ordinal)
            .ToList();

        var remaining = quantity;
        var takes = new List<Take<T>>();
        foreach (var lot in ordered)
        {
            if (remaining <= 0)
                break;
            var available = onHand(lot);
            if (available <= 0)
                continue;
            var take = Math.Min(available, remaining);
            takes.Add(new Take<T>(lot, take));
            remaining -= take;
        }

        if (remaining > 0)
            throw new DomainException($"Not enough lot quantity. Short by {remaining}.");

        return takes;
    }
}
