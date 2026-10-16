using System.Security.Claims;

namespace IWMS.Api.Security;

public static class CurrentUserExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var value = user.FindFirst("sub")?.Value;

        return Guid.TryParse(value, out var id)
            ? id
            : throw new InvalidOperationException("No signed-in user.");
    }
}