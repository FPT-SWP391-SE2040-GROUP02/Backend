using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.IntegrationTests.Audit;

/// <summary>Repository nhật ký giả cho test không cần SQL Server: chỉ giữ các dòng trong List.</summary>
public sealed class FakeAuditLogRepository : IAuditLogRepository
{
    public List<AuditLog> Logs { get; } = new List<AuditLog>();

    public void Add(AuditLog log)
    {
        Logs.Add(log);
    }

    /// <summary>Test dùng fake này chỉ kiểm việc ghi; đọc trang được kiểm trên SQL Server thật.</summary>
    public Task<(List<AuditLog> Items, int TotalCount)> FindPageByVaultIdAsync(
        Guid vaultId, AuditAction? action, DateTimeOffset? from, DateTimeOffset? to,
        int pageIndex, int pageSize, bool isAscending, CancellationToken ct)
    {
        throw new NotSupportedException();
    }

    public Task<(List<AuditLog> Items, int TotalCount)> FindPageAsync(
        AuditAction? action, DateTimeOffset? from, DateTimeOffset? to,
        int pageIndex, int pageSize, bool isAscending, CancellationToken ct)
    {
        throw new NotSupportedException();
    }
}
