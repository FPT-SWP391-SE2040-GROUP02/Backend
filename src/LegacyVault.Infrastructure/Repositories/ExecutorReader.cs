using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

/// <remarks>Chỉ truy cập dữ liệu; luật nghiệp vụ nằm ở service của phần gọi tới.</remarks>
public sealed class ExecutorReader : IExecutorReader
{
    private readonly AppDbContext context;

    public ExecutorReader(AppDbContext context)
    {
        this.context = context;
    }

    public async Task<List<Executor>> GetActiveAsync(Guid vaultId, CancellationToken ct)
    {
        List<Executor> accepted = await context.Set<Executor>()
            .AsNoTracking()
            .Where(e => e.VaultId == vaultId && e.Status == ExecutorStatus.Accepted)
            .OrderBy(e => e.Id)
            .ToListAsync(ct);
        return accepted;
    }

    public async Task<List<Executor>> GetAvailableAsync(Guid vaultId, CancellationToken ct)
    {
        // ASSIGN-02: chỉ lấy Executor đã chấp nhận mà tài khoản còn Active (dòng Executor của người bị khóa vẫn là Accepted).
        List<Executor> accepted = await context.Set<Executor>()
            .AsNoTracking()
            .Where(e => e.VaultId == vaultId
                && e.Status == ExecutorStatus.Accepted
                && e.User != null
                && e.User.Status == UserStatus.Active)
            .OrderBy(e => e.Id)
            .ToListAsync(ct);
        return accepted;
    }
}
