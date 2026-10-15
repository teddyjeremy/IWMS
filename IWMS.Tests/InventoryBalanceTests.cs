using IWMS.Domain.Common;
using IWMS.Domain.Entities;
using IWMS.Domain.Enums;

namespace IWMS.Tests;

public class InventoryBalanceTests
{
    private static readonly Guid SomeUser = Guid.NewGuid();

    private static InventoryBalance NewBalance(decimal onHand = 0, decimal reserved = 0) =>
        new()
        {
            ProductId = Guid.NewGuid(),
            StorageLocationId = Guid.NewGuid(),
            OnHand = onHand,
            Reserved = reserved
        };

    [Fact]
    public void Receipt_increases_on_hand_and_records_before_and_after()
    {
        var balance = NewBalance(onHand: 10);

        var movement = balance.Post(MovementType.Receipt, 60, SomeUser, "Goods receipt", "PO-0001");

        Assert.Equal(70m, balance.OnHand);
        Assert.Equal(MovementType.Receipt, movement.Type);
        Assert.Equal(60m, movement.QuantityChange);
        Assert.Equal(10m, movement.QuantityBefore);
        Assert.Equal(70m, movement.QuantityAfter);
        Assert.Equal(SomeUser, movement.PerformedByUserId);
    }

    [Fact]
    public void Dispatch_decreases_on_hand_and_records_a_negative_change()
    {
        var balance = NewBalance(onHand: 50);

        var movement = balance.Post(MovementType.Dispatch, 20, SomeUser, "Order shipped", "SO-0001");

        Assert.Equal(30m, balance.OnHand);
        Assert.Equal(-20m, movement.QuantityChange);
    }

    [Fact]
    public void Removing_more_than_available_is_rejected_and_changes_nothing()
    {
        var balance = NewBalance(onHand: 10);

        Assert.Throws<DomainException>(() =>
            balance.Post(MovementType.Dispatch, 11, SomeUser, "Too much", "SO-0002"));

        Assert.Equal(10m, balance.OnHand);
    }

    [Fact]
    public void Reserved_stock_cannot_be_dispatched()
    {
        var balance = NewBalance(onHand: 10, reserved: 8);

        Assert.Throws<DomainException>(() =>
            balance.Post(MovementType.Dispatch, 3, SomeUser, "Reserved", "SO-0003"));

        balance.Post(MovementType.Dispatch, 2, SomeUser, "Unreserved part", "SO-0003");
        Assert.Equal(8m, balance.OnHand);
    }

    [Fact]
    public void Available_is_on_hand_minus_reserved()
    {
        var balance = NewBalance(onHand: 100, reserved: 30);

        Assert.Equal(70m, balance.Available);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Zero_or_negative_quantities_are_rejected(int quantity)
    {
        var balance = NewBalance(onHand: 10);

        Assert.Throws<DomainException>(() =>
            balance.Post(MovementType.Receipt, quantity, SomeUser, "Bad", "PO-0004"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Adjustments_require_a_reason(string reason)
    {
        var balance = NewBalance(onHand: 10);

        Assert.Throws<DomainException>(() =>
            balance.Post(MovementType.AdjustmentDecrease, 1, SomeUser, reason, "ADJ-1"));
    }

    [Fact]
    public void Reversals_cannot_be_posted_through_the_normal_method()
    {
        var balance = NewBalance(onHand: 10);

        Assert.Throws<DomainException>(() =>
            balance.Post(MovementType.Reversal, 1, SomeUser, "Fix", "REV-1"));
    }

    [Fact]
    public void Fractional_quantities_add_up_exactly()
    {
        var balance = NewBalance();

        balance.Post(MovementType.Receipt, 0.1m, SomeUser, "Part 1", "PO-0005");
        balance.Post(MovementType.Receipt, 0.2m, SomeUser, "Part 2", "PO-0005");

        Assert.Equal(0.3m, balance.OnHand);
    }
}