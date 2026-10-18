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

        // Đưa Executor chính lên trước, giữ nguyên thứ tự còn lại. Không dùng OrderBy(e => e.Role) trong SQL
        // vì Role lưu dạng chữ, SQL sẽ sắp "Backup" trước "Primary".
        List<Executor> result = new List<Executor>();
        foreach (Executor executor in accepted)
        {
            if (executor.Role == ExecutorRole.Primary)
            {
                result.Add(executor);
            }
        }
        foreach (Executor executor in accepted)
        {
            if (executor.Role != ExecutorRole.Primary)
            {
                result.Add(executor);
            }
        }
        return result;
    }
}
