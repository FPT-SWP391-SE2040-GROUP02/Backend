using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;

namespace LegacyVault.IntegrationTests.Audit;

/// <summary>Repository nhật ký giả cho test không cần SQL Server: chỉ giữ các dòng trong List.</summary>
public sealed class FakeAuditLogRepository : IAuditLogRepository
{
    public List<AuditLog> Logs { get; } = new List<AuditLog>();

    public void Add(AuditLog log)
    {
        Logs.Add(log);
    }
}
