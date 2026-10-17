using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

public sealed class ObjectionRepository : IObjectionRepository
{
    private readonly AppDbContext context;

    public ObjectionRepository(AppDbContext context)
    {
        this.context = context;
    }

    public Task<DeathCaseObjection?> FindOpenByCaseAsync(Guid caseId, CancellationToken ct)
    {
        return context.Set<DeathCaseObjection>()
            .FirstOrDefaultAsync(o => o.CaseId == caseId && o.Status == ObjectionStatus.Open, ct);
    }

    public Task<DeathCaseObjection?> FindLatestByCaseAsync(Guid caseId, CancellationToken ct)
    {
        return context.Set<DeathCaseObjection>()
            .Where(o => o.CaseId == caseId)
            .OrderByDescending(o => o.FirstReportedAt)
            .ThenByDescending(o => o.Id)
            .FirstOrDefaultAsync(ct);
    }

    public void Add(DeathCaseObjection objection)
    {
        context.Set<DeathCaseObjection>().Add(objection);
    }

    public Task<List<HandoverTurn>> FindTurnsWithGrantsAsync(Guid planId, CancellationToken ct)
    {
        return context.Set<HandoverTurn>()
            .Include(t => t.Grant)
            .Where(t => t.PlanId == planId)
            .ToListAsync(ct);
    }

    public bool IsUniqueViolation(Exception exception)
    {
        if (exception is not DbUpdateException)
        {
            return false;
        }
        Exception? inner = exception.InnerException;
        while (inner != null)
        {
            if (inner is Microsoft.Data.SqlClient.SqlException sqlException
                && (sqlException.Number == 2601 || sqlException.Number == 2627))
            {
                return true;
            }
            inner = inner.InnerException;
        }
        return false;
    }
}
