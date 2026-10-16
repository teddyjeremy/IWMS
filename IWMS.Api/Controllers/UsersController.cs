using IWMS.Api.Dtos;
using IWMS.Api.Security;
using IWMS.Application;
using IWMS.Application.Audit;
using IWMS.Domain.Common;
using IWMS.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AppUser = IWMS.Domain.Entities.User;


namespace IWMS.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Policy = Policies.ManageUsers)]
public class UsersController : ControllerBase
{
    private readonly IAppDbContext _db;
    private readonly IPasswordHasher<AppUser> _hasher;
    private readonly AuditLogger _audit;

    public UsersController(IAppDbContext db, IPasswordHasher<AppUser> hasher, AuditLogger audit)
    {
        _db = db;
        _hasher = hasher;
        _audit = audit;
    }

    [HttpGet]
    public async Task<ActionResult<List<UserItem>>> List(CancellationToken ct)
    {
        var users = await _db.Users.AsNoTracking()
            .OrderBy(u => u.Email)
            .Select(u => new UserItem(
                u.Id,
                u.FirstName + " " + u.LastName,
                u.Email,
                u.IsActive,
                u.Roles.Select(r => r.Name).ToList()))
            .ToListAsync(ct);

        return Ok(users);
    }

    [HttpPost]
    public async Task<ActionResult> Create(CreateUserRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName) || string.IsNullOrWhiteSpace(request.LastName))
            throw new DomainException("First and last name are required.");

        var email = (request.Email ?? string.Empty).Trim().ToLowerInvariant();
        if (!email.Contains('@'))
            throw new DomainException("A valid email address is required.");
        if (await _db.Users.AnyAsync(u => u.Email == email, ct))
            throw new DomainException("A user with this email already exists.");

        EnsureValidPassword(request.Password);
        var roles = await LoadRolesAsync(request.Roles, ct);

        var user = new AppUser
        {
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = email,
            Roles = roles
        };
        user.PasswordHash = _hasher.HashPassword(user, request.Password);

        _db.Users.Add(user);
        _audit.Record("User.Created", "User", user.Id, User.GetUserId(),
            details: $"{email}; roles: {string.Join(", ", roles.Select(r => r.Name))}");
        await _db.SaveChangesAsync(ct);

        return StatusCode(StatusCodes.Status201Created, new { user.Id });
    }

    [HttpPut("{id:guid}/roles")]
    public async Task<ActionResult> SetRoles(Guid id, SetRolesRequest request, CancellationToken ct)
    {
        var user = await _db.Users.Include(u => u.Roles).SingleOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return NotFound();

        var roles = await LoadRolesAsync(request.Roles, ct);

        var wasAdmin = user.Roles.Any(r => r.Name == RoleNames.Administrator);
        var willBeAdmin = roles.Any(r => r.Name == RoleNames.Administrator);
        if (wasAdmin && !willBeAdmin)
            await EnsureAnotherActiveAdminAsync(user.Id, ct);

        user.Roles.Clear();
        foreach (var role in roles)
            user.Roles.Add(role);
        user.UpdatedAtUtc = DateTime.UtcNow;

        _audit.Record("User.RolesChanged", "User", user.Id, User.GetUserId(),
            details: $"{user.Email}; roles: {string.Join(", ", roles.Select(r => r.Name))}");
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpPut("{id:guid}/status")]
    public async Task<ActionResult> SetStatus(Guid id, SetStatusRequest request, CancellationToken ct)
    {
        var user = await _db.Users.Include(u => u.Roles).SingleOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return NotFound();

        var isAdmin = user.Roles.Any(r => r.Name == RoleNames.Administrator);
        if (!request.IsActive && isAdmin)
            await EnsureAnotherActiveAdminAsync(user.Id, ct);

        user.IsActive = request.IsActive;
        user.UpdatedAtUtc = DateTime.UtcNow;

        _audit.Record(
            request.IsActive ? "User.Activated" : "User.Deactivated",
            "User", user.Id, User.GetUserId(), details: user.Email);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpPut("{id:guid}/password")]
    public async Task<ActionResult> ResetPassword(
        Guid id, ResetPasswordRequest request, CancellationToken ct)
    {
        var user = await _db.Users.SingleOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return NotFound();

        EnsureValidPassword(request.NewPassword);

        user.PasswordHash = _hasher.HashPassword(user, request.NewPassword);
        user.UpdatedAtUtc = DateTime.UtcNow;

        _audit.Record("User.PasswordReset", "User", user.Id, User.GetUserId(), details: user.Email);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    private static void EnsureValidPassword(string? password)
    {
        if (string.IsNullOrEmpty(password)
            || password.Length < 10
            || !password.Any(char.IsLetter)
            || !password.Any(char.IsDigit))
            throw new DomainException("Password must be at least 10 characters and contain letters and digits.");
    }

    private async Task<List<Role>> LoadRolesAsync(List<string>? names, CancellationToken ct)
    {
        var wanted = (names ?? new List<string>()).Distinct().ToList();
        if (wanted.Count == 0)
            throw new DomainException("A user needs at least one role.");

        var roles = await _db.Roles.Where(r => wanted.Contains(r.Name)).ToListAsync(ct);
        if (roles.Count != wanted.Count)
            throw new DomainException("One or more role names are unknown.");

        return roles;
    }

    private async Task EnsureAnotherActiveAdminAsync(Guid exceptUserId, CancellationToken ct)
    {
        var others = await _db.Users.CountAsync(
            u => u.IsActive && u.Id != exceptUserId
                 && u.Roles.Any(r => r.Name == RoleNames.Administrator), ct);

        if (others == 0)
            throw new DomainException("There must always be at least one active administrator.");
    }
}