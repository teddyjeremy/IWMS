using IWMS.Domain.Entities;

namespace IWMS.Application.Audit;

public class AuditLogger
{
    private readonly IAppDbContext _db;

    public AuditLogger(IAppDbContext db)
    {
        _db = db;
    }

    public void Record(
        string action,
        string entityName,
        Guid? entityId,
        Guid? actorUserId,
        string reason = "",
        string? details = null)
    {
        _db.AuditEvents.Add(new AuditEvent
        {
            Action = action,
            EntityName = entityName,
            EntityId = entityId,
            ActorUserId = actorUserId,
            Reason = reason,
            Details = details
        });
    }
}