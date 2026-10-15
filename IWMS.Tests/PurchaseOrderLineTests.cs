using IWMS.Domain.Entities;

namespace IWMS.Tests;

public class PurchaseOrderLineTests
{
    [Fact]
    public void Receiving_60_then_40_of_100_leaves_nothing_outstanding()
    {
        var line = new PurchaseOrderLine { QuantityOrdered = 100 };

        line.QuantityReceived += 60;
        Assert.Equal(40m, line.QuantityOutstanding);

        line.QuantityReceived += 40;
        Assert.Equal(0m, line.QuantityOutstanding);
    }
}