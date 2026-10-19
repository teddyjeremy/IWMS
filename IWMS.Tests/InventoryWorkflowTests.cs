using IWMS.Domain.Common;
using IWMS.Domain.Entities;
using IWMS.Domain.Enums;

namespace IWMS.Tests;

public class InventoryWorkflowTests
{
    [Fact]
    public void Inventory_balance_post_transfer_out_reduces_on_hand()
    {
        var balance = new InventoryBalance { OnHand = 20m, Reserved = 3m };

        var movement = balance.Post(
            MovementType.TransferOut, 5m, Guid.NewGuid(), "Warehouse transfer", "TR-TEST");

        Assert.Equal(15m, balance.OnHand);
        Assert.Equal(-5m, movement.QuantityChange);
        Assert.Equal(20m, movement.QuantityBefore);
        Assert.Equal(15m, movement.QuantityAfter);
    }

    [Fact]
    public void Inventory_balance_rejects_transfer_that_would_use_reserved_stock()
    {
        var balance = new InventoryBalance { OnHand = 10m, Reserved = 4m };

        var exception = Assert.Throws<DomainException>(() => balance.Post(
            MovementType.TransferOut, 7m, Guid.NewGuid(), "Warehouse transfer", "TR-TEST"));

        Assert.Contains("Insufficient available stock", exception.Message);
        Assert.Equal(10m, balance.OnHand);
    }

    [Fact]
    public void Transfer_line_tracks_units_in_transit()
    {
        var line = new StockTransferLine
        {
            QuantityRequested = 12m,
            QuantityDispatched = 10m,
            QuantityReceived = 6m
        };

        Assert.Equal(4m, line.QuantityInTransit);
    }

    [Fact]
    public void Stock_count_variance_is_counted_quantity_minus_snapshot()
    {
        var line = new StockCountLine { SystemQuantity = 18m, CountedQuantity = 15m };

        Assert.Equal(-3m, line.Variance);
    }

    [Fact]
    public void Return_record_line_keeps_disposition_and_quantity()
    {
        var line = new ReturnRecordLine
        {
            Quantity = 2m,
            Disposition = ReturnDisposition.Quarantine,
            Reason = "Packaging damaged"
        };

        Assert.Equal(2m, line.Quantity);
        Assert.Equal(ReturnDisposition.Quarantine, line.Disposition);
    }
}
