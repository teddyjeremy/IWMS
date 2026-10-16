using System.Security.Claims;
using System.Text;
using IWMS.Domain.Entities;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace IWMS.Api.Security;

public class TokenService
{
    private readonly IConfiguration _config;

    public TokenService(IConfiguration config)
    {
        _config = config;
    }

    public (string Value, DateTime ExpiresAtUtc) Create(User user)
    {
        var key = _config["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key is missing from user secrets.");

        var minutes = int.TryParse(_config["Jwt:ExpiryMinutes"], out var m) ? m : 480;
        var expires = DateTime.UtcNow.AddMinutes(minutes);

        var claims = new List<Claim>
        {
            new("sub", user.Id.ToString()),
            new("name", $"{user.FirstName} {user.LastName}"),
            new("email", user.Email)
        };
        claims.AddRange(user.Roles.Select(r => new Claim("role", r.Name)));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expires,
            Issuer = _config["Jwt:Issuer"],
            Audience = _config["Jwt:Audience"],
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
                SecurityAlgorithms.HmacSha256)
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);
        return (token, expires);
    }
}