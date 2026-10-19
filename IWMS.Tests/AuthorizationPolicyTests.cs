using System.Security.Claims;
using IWMS.Api.Security;
using IWMS.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IWMS.Tests;

public sealed class AuthorizationPolicyTests
{
    [Theory]
    [InlineData(RoleNames.Administrator, true)]
    [InlineData(RoleNames.WarehouseManager, true)]
    [InlineData(RoleNames.SalesOfficer, true)]
    [InlineData(RoleNames.WarehouseOperator, false)]
    [InlineData(RoleNames.Purchaser, false)]
    [InlineData(RoleNames.Viewer, false)]
    public async Task ManageSalesOrders_AllowsOnlyIntendedRoles(string role, bool expected)
    {
        using var provider = CreateAuthorizationProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var user = PrincipalWithRole(role);

        var result = await authorization.AuthorizeAsync(user, resource: null, Policies.ManageSalesOrders);

        Assert.Equal(expected, result.Succeeded);
    }

    [Theory]
    [InlineData(RoleNames.Administrator, true)]
    [InlineData(RoleNames.WarehouseManager, true)]
    [InlineData(RoleNames.WarehouseOperator, true)]
    [InlineData(RoleNames.SalesOfficer, false)]
    [InlineData(RoleNames.Purchaser, false)]
    [InlineData(RoleNames.Viewer, false)]
    public async Task PostIssue_DoesNotGrantSalesOfficerWarehouseIssueAccess(string role, bool expected)
    {
        using var provider = CreateAuthorizationProvider();
        var authorization = provider.GetRequiredService<IAuthorizationService>();
        var user = PrincipalWithRole(role);

        var result = await authorization.AuthorizeAsync(user, resource: null, Policies.PostIssue);

        Assert.Equal(expected, result.Succeeded);
    }

    private static ServiceProvider CreateAuthorizationProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddIwmsAuthorization();
        return services.BuildServiceProvider();
    }

    private static ClaimsPrincipal PrincipalWithRole(string role) =>
        new(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, role) },
            authenticationType: "test"));
}
