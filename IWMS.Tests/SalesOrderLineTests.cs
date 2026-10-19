using IWMS.Domain.Entities;

namespace IWMS.Tests;

public class SalesOrderLineTests
{
    [Fact]
    public void Outstanding_quantity_excludes_dispatched_units()
    {
        var line = new SalesOrderLine
        {
            QuantityOrdered = 12,
            QuantityDispatched = 5
        };

        Assert.Equal(7m, line.QuantityOutstanding);
    }

    [Fact]
    public void Line_total_uses_ordered_quantity_and_unit_price()
    {
        var line = new SalesOrderLine
        {
            QuantityOrdered = 3,
            UnitPrice = 19.95m,
            QuantityDispatched = 1
        };

        Assert.Equal(59.85m, line.LineTotal);
    }
}
