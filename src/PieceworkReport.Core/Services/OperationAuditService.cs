using PieceworkReport.Core.Data;

namespace PieceworkReport.Core.Services;

public sealed class OperationAuditService(AppDbContext db)
{
    public void Record(string eventType, string username, string detail) =>
        db.SecurityAuditEntries.Add(new SecurityAuditEntry { EventType = eventType, Username = username, Detail = detail });
}
