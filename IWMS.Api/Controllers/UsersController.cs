using IWMS.Api.Dtos;
using IWMS.Application;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IWMS.Api.Controllers;

[ApiController]
[Route("api/users")]
public class UsersController : ControllerBase
{
    private readonly IAppDbContext _db;

    public UsersController(IAppDbContext db)
    {
        _db = db;
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
}