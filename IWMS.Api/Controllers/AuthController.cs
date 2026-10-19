using IWMS.Api.Dtos;
using IWMS.Api.Security;
using IWMS.Application;
using IWMS.Application.Audit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using AppUser = IWMS.Domain.Entities.User;

namespace IWMS.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAppDbContext _db;
    private readonly IPasswordHasher<AppUser> _hasher;
    private readonly TokenService _tokens;
    private readonly AuditLogger _audit;

    public AuthController(
        IAppDbContext db, IPasswordHasher<AppUser> hasher, TokenService tokens, AuditLogger audit)
    {
        _db = db;
        _hasher = hasher;
        _tokens = tokens;
        _audit = audit;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [EnableRateLimiting("login")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();

        var user = await _db.Users
            .Include(u => u.Roles)
            .SingleOrDefaultAsync(u => u.Email == email, ct);

        if (user is null
            || !user.IsActive
            || _hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password ?? string.Empty)
                == PasswordVerificationResult.Failed)
        {
            _audit.Record("Auth.LoginFailed", "User", user?.Id, null, "Invalid credentials", $"Email: {email}");
            await _db.SaveChangesAsync(ct);

            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Sign-in failed",
                Detail = "Invalid email or password."
            });
        }

        user.LastLoginAtUtc = DateTime.UtcNow;
        _audit.Record("Auth.LoginSucceeded", "User", user.Id, user.Id);
        await _db.SaveChangesAsync(ct);

        var (token, expiresAtUtc) = _tokens.Create(user);

        return Ok(new LoginResponse(
            token,
            expiresAtUtc,
            new CurrentUserDto(
                user.Id,
                user.FullName,
                user.Email,
                user.Roles.Select(r => r.Name).ToList())));
    }

    [Authorize(Policy = Policies.ViewData)]
    [HttpGet("me")]
    public ActionResult<CurrentUserDto> Me()
    {
        var roles = User.FindAll("role").Select(c => c.Value).ToList();

        return Ok(new CurrentUserDto(
            User.GetUserId(),
            User.FindFirst("name")?.Value ?? string.Empty,
            User.FindFirst("email")?.Value ?? string.Empty,
            roles));
    }
}