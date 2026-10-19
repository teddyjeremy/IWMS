using IWMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;

namespace IWMS.Api.Security;

public static class Policies
{
    public const string ViewData = "ViewData";
    public const string ManageMasterData = "ManageMasterData";
    public const string ReceiveGoods = "ReceiveGoods";
    public const string PostIssue = "PostIssue";
    public const string ManageSalesOrders = "ManageSalesOrders";
    public const string PostTransfer = "PostTransfer";
    public const string PostAdjustment = "PostAdjustment";
    public const string OverReceive = "OverReceive";
    public const string ReverseMovements = "ReverseMovements";
    public const string ManageSuppliers = "ManageSuppliers";
    public const string ManagePurchaseOrders = "ManagePurchaseOrders";
    public const string ApprovePurchaseOrders = "ApprovePurchaseOrders";
    public const string CancelPurchaseOrders = "CancelPurchaseOrders";
    public const string ManageUsers = "ManageUsers";
    public const string ViewAudit = "ViewAudit";
}

public static class AuthorizationSetup
{
    public static IServiceCollection AddIwmsAuthorization(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Policies.ViewData, p => p.RequireRole(RoleNames.All))
            .AddPolicy(Policies.ManageMasterData, p =>
                p.RequireRole(RoleNames.Administrator, RoleNames.WarehouseManager))
            .AddPolicy(Policies.ReceiveGoods, p =>
                p.RequireRole(RoleNames.WarehouseOperator, RoleNames.WarehouseManager))
            .AddPolicy(Policies.PostIssue, p =>
                p.RequireRole(RoleNames.WarehouseOperator, RoleNames.WarehouseManager))
            .AddPolicy(Policies.ManageSalesOrders, p =>
                p.RequireRole(RoleNames.Administrator, RoleNames.WarehouseManager, RoleNames.SalesOfficer))
            .AddPolicy(Policies.PostTransfer, p =>
                p.RequireRole(RoleNames.WarehouseOperator, RoleNames.WarehouseManager))
            .AddPolicy(Policies.PostAdjustment, p => p.RequireRole(RoleNames.WarehouseManager))
            .AddPolicy(Policies.OverReceive, p => p.RequireRole(RoleNames.WarehouseManager))
            .AddPolicy(Policies.ReverseMovements, p => p.RequireRole(RoleNames.WarehouseManager))
            .AddPolicy(Policies.ManageSuppliers, p =>
                p.RequireRole(RoleNames.Administrator, RoleNames.Purchaser))
            .AddPolicy(Policies.ManagePurchaseOrders, p => p.RequireRole(RoleNames.Purchaser))
            .AddPolicy(Policies.ApprovePurchaseOrders, p => p.RequireRole(RoleNames.WarehouseManager))
            .AddPolicy(Policies.CancelPurchaseOrders, p =>
                p.RequireRole(RoleNames.Purchaser, RoleNames.WarehouseManager))
            .AddPolicy(Policies.ManageUsers, p => p.RequireRole(RoleNames.Administrator))
            .AddPolicy(Policies.ViewAudit, p =>
                p.RequireRole(RoleNames.Administrator, RoleNames.WarehouseManager, RoleNames.Viewer));

        return services;
    }
}