namespace IWMS.Domain.Common;

public static class RoleNames
{
    public const string Administrator = "Administrator";
    public const string WarehouseManager = "Warehouse Manager";
    public const string Purchaser = "Purchaser";
    public const string WarehouseOperator = "Warehouse Operator";
    public const string SalesOfficer = "Sales Officer";
    public const string Viewer = "Viewer";

    public static readonly string[] All =
    {
        Administrator,
        WarehouseManager,
        Purchaser,
        WarehouseOperator,
        SalesOfficer,
        Viewer
    };
}