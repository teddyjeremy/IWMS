using IWMS.Domain.Common;
using IWMS.Domain.Tracking;

namespace IWMS.Tests;

public class FefoAllocatorTests
{
    private sealed record Lot(string Number, DateTime? Expiry, decimal OnHand);

    [Fact]
    public void Allocate_consumes_the_earliest_expiry_first()
    {
        var lots = new[]
        {
            new Lot("LATER", new DateTime(2027, 6, 1), 5),
            new Lot("SOON", new DateTime(2026, 11, 1), 4),
            new Lot("NONE", null, 9)
        };

        var takes = FefoAllocator.Allocate(lots, 6, lot => lot.OnHand, lot => lot.Expiry, lot => lot.Number);

        Assert.Equal(new[] { "SOON", "LATER" }, takes.Select(take => take.Source.Number).ToArray());
        Assert.Equal(4m, takes[0].Quantity);
        Assert.Equal(2m, takes[1].Quantity);
    }

    [Fact]
    public void Allocate_rejects_a_shortage()
    {
        var lots = new[] { new Lot("A", new DateTime(2026, 12, 1), 1) };
        var error = Assert.Throws<DomainException>(() =>
            FefoAllocator.Allocate(lots, 3, lot => lot.OnHand, lot => lot.Expiry, lot => lot.Number));
        Assert.Contains("Short by", error.Message);
    }
}
