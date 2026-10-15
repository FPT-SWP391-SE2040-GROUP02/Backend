using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

public sealed class ExecutorInvitationRepository(AppDbContext context) : IExecutorInvitationRepository
{
    public Task<Executor?> FindByTokenHashAsync(string hash, CancellationToken ct) =>
        context.Set<Executor>().SingleOrDefaultAsync(e => e.InvitationTokenHash == hash, ct);
    public void Add(Executor executor) => context.Set<Executor>().Add(executor);
}
