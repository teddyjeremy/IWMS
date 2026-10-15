namespace IWMS.Domain.Enums;

public enum TransferStatus
{
    Draft = 1,
    PendingApproval = 2,
    Approved = 3,
    Rejected = 4,
    InTransit = 5,
    Received = 6,
    Cancelled = 7
}