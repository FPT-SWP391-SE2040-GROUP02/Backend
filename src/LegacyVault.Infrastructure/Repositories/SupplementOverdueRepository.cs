using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

public sealed class SupplementOverdueRepository : ISupplementOverdueRepository
{
    private readonly AppDbContext context;

    public SupplementOverdueRepository(AppDbContext context)
    {
        this.context = context;
    }

    public Task<List<Guid>> FindOverdueCaseIdsAsync(DateTimeOffset now, int maxCount, CancellationToken ct)
    {
        return context.Set<DeathCase>()
            .Where(c => c.Status == DeathCaseStatus.SupplementRequired
                && c.SupplementOverdueAt == null
                && c.Decisions.Any(d => d.Kind == DeathCaseDecisionKind.SupplementRequested
                    && d.VersionNo == c.ReviewVersionNo
                    && d.DueAt != null
                    && d.DueAt <= now))
            .OrderBy(c => c.FirstSubmittedAt)
            .ThenBy(c => c.Id)
            .Select(c => c.Id)
            .Take(maxCount)
            .ToListAsync(ct);
    }
}
