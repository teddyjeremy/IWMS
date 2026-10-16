using IWMS.Api.Dtos;
using IWMS.Api.Security;
using IWMS.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IWMS.Api.Controllers;

[ApiController]
[Route("api/audit-events")]
[Authorize(Policy = Policies.ViewAudit)]
public class AuditController : ControllerBase
{
    private readonly IAppDbContext _db;

    public AuditController(IAppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<AuditEventItem>>> List(
        [FromQuery] string? action,
        [FromQuery] string? entityName,
        [FromQuery] Guid? actorUserId,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var query = _db.AuditEvents.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(action))
            query = query.Where(a => a.Action.Contains(action.Trim()));
        if (!string.IsNullOrWhiteSpace(entityName))
            query = query.Where(a => a.EntityName == entityName.Trim());
        if (actorUserId is not null)
            query = query.Where(a => a.ActorUserId == actorUserId);
        if (fromUtc is not null)
        {
            var from = ToUtc(fromUtc.Value);
            query = query.Where(a => a.CreatedAtUtc >= from);
        }
        if (toUtc is not null)
        {
            var to = ToUtc(toUtc.Value);
            query = query.Where(a => a.CreatedAtUtc <= to);
        }

        var result = await query
            .OrderByDescending(a => a.CreatedAtUtc)
            .Select(a => new AuditEventItem(
                a.Id,
                a.CreatedAtUtc,
                a.Action,
                a.EntityName,
                a.EntityId,
                a.ActorUser != null
                    ? a.ActorUser.FirstName + " " + a.ActorUser.LastName
                    : "(anonymous)",
                a.Reason,
                a.Details))
            .ToPagedAsync(page, pageSize, ct);

        return Ok(result);
    }

    private static DateTime ToUtc(DateTime value) =>
        value.Kind == DateTimeKind.Local
            ? value.ToUniversalTime()
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}