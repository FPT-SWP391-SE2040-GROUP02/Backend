using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

public sealed class ExecutorInvitationRepository(AppDbContext context) : IExecutorInvitationRepository
{
    public Task<Executor?> FindByTokenHashAsync(string hash, CancellationToken ct) =>
        context.Set<Executor>().SingleOrDefaultAsync(e => e.InvitationTokenHash == hash, ct);

    public void Add(Executor executor) => context.Set<Executor>().Add(executor);

    public Task<Executor?> FindByIdAsync(Guid executorId, CancellationToken ct) =>
        context.Set<Executor>().SingleOrDefaultAsync(e => e.Id == executorId, ct);

    public Task<List<Guid>> FindDueForJobAsync(
        DateTimeOffset now, DateTimeOffset remindInvitedBefore, int maxCount, CancellationToken ct) =>
        context.Set<Executor>()
            .Where(e => e.Status == ExecutorStatus.Invited
                && (e.ExpiresAt <= now || (e.ReminderSentAt == null && e.InvitedAt <= remindInvitedBefore)))
            .OrderBy(e => e.InvitedAt)
            .ThenBy(e => e.Id)
            .Select(e => e.Id)
            .Take(maxCount)
            .ToListAsync(ct);
}
