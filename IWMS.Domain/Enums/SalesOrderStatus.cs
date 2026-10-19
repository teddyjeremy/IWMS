namespace IWMS.Domain.Enums;

public enum SalesOrderStatus
{
    Draft = 1,
    Confirmed = 2,
    Reserved = 3,
    Picking = 4,
    PartiallyDispatched = 5,
    Dispatched = 6,
    Backordered = 7,
    Cancelled = 8,
    ClosedShort = 9
}