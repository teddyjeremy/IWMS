using IWMS.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace IWMS.Infrastructure.Seeding;

/// <summary>
/// Intentionally contains no automatic demo-account creation. Production accounts
/// should be provisioned through the API's administrator-only user management.
/// </summary>
public static class ProductionAccountPolicy
{
    public static bool IsDevelopmentSeedAddress(string? email) =>
        !string.IsNullOrWhiteSpace(email) &&
        email.EndsWith("@iwms.local", StringComparison.OrdinalIgnoreCase);
}
