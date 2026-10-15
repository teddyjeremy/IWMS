using IWMS.Domain.Common;

namespace IWMS.Domain.Entities;

public class AuditEvent : BaseEntity
{
    public string Action { get; init; } = string.Empty;
    public string EntityName { get; init; } = string.Empty;
    public Guid? EntityId { get; init; }
    public string Reason { get; init; } = string.Empty;
    public string? Details { get; init; }

    public Guid? ActorUserId { get; init; }
    public User? ActorUser { get; init; }
}