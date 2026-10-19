using IWMS.Api.Security;
using IWMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace IWMS.Tests;

public sealed class SalesOrderAuthorizationTests
{
    [Fact]
    public async Task ManageSalesOrders_AllowsSalesRolesWithoutGrantingWarehouseFulfillment()
    {
        var services = new ServiceCollection();
        services.AddIwmsAuthorization();
        using var provider = services.BuildServiceProvider();
        var policyProvider = provider.GetRequiredService<IAuthorizationPolicyProvider>();

        var salesPolicy = await policyProvider.GetPolicyAsync(Policies.ManageSalesOrders);
        Assert.NotNull(salesPolicy);
        var salesRoles = Assert.Single(salesPolicy!.Requirements.OfType<RolesAuthorizationRequirement>());
        Assert.Contains(RoleNames.Administrator, salesRoles.AllowedRoles);
        Assert.Contains(RoleNames.WarehouseManager, salesRoles.AllowedRoles);
        Assert.Contains(RoleNames.SalesOfficer, salesRoles.AllowedRoles);
        Assert.DoesNotContain(RoleNames.WarehouseOperator, salesRoles.AllowedRoles);

        var fulfillmentPolicy = await policyProvider.GetPolicyAsync(Policies.PostIssue);
        Assert.NotNull(fulfillmentPolicy);
        var fulfillmentRoles = Assert.Single(fulfillmentPolicy!.Requirements.OfType<RolesAuthorizationRequirement>());
        Assert.Contains(RoleNames.WarehouseManager, fulfillmentRoles.AllowedRoles);
        Assert.Contains(RoleNames.WarehouseOperator, fulfillmentRoles.AllowedRoles);
        Assert.DoesNotContain(RoleNames.SalesOfficer, fulfillmentRoles.AllowedRoles);
    }
}
