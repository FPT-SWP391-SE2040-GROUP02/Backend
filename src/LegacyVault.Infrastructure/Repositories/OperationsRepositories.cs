using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

internal static class SqlErrors
{
    /// <summary>SQL Server: 2601 = trùng chỉ mục unique, 2627 = vi phạm ràng buộc unique.</summary>
    public static bool IsUniqueViolation(Exception exception)
    {
        if (exception is not DbUpdateException)
        {
            return false;
        }
        Exception? inner = exception.InnerException;
        while (inner != null)
        {
            if (inner is SqlException sqlException && (sqlException.Number == 2601 || sqlException.Number == 2627))
            {
                return true;
            }
            inner = inner.InnerException;
        }
        return false;
    }
}

public sealed class AdminUserRepository : IAdminUserRepository
{
    private readonly AppDbContext context;

    public AdminUserRepository(AppDbContext context)
    {
        this.context = context;
    }

    public async Task<(List<User> Items, int TotalCount)> ListAsync(
        string? search, UserRole? role, UserStatus? status, int pageIndex, int pageSize, CancellationToken ct)
    {
        IQueryable<User> query = context.Set<User>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim().ToLowerInvariant();
            query = query.Where(u => u.Email.Contains(term) || u.FullName.ToLower().Contains(term));
        }
        if (role != null)
        {
            query = query.Where(u => u.Role == role.Value);
        }
        if (status != null)
        {
            query = query.Where(u => u.Status == status.Value);
        }
        int total = await query.CountAsync(ct);
        List<User> items = await query
            .OrderBy(u => u.CreatedAt)
            .ThenBy(u => u.Id)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }

    public async Task<Dictionary<UserRole, int>> CountByRoleAsync(string? search, UserStatus? status, CancellationToken ct)
    {
        IQueryable<User> query = context.Set<User>().AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            string term = search.Trim().ToLowerInvariant();
            query = query.Where(u => u.Email.Contains(term) || u.FullName.ToLower().Contains(term));
        }
        if (status != null)
        {
            query = query.Where(u => u.Status == status.Value);
        }
        var rows = await query.GroupBy(u => u.Role).Select(g => new { Role = g.Key, Count = g.Count() }).ToListAsync(ct);
        return rows.ToDictionary(r => r.Role, r => r.Count);
    }

    public Task<List<User>> FindByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct)
    {
        return context.Set<User>().AsNoTracking().Where(u => ids.Contains(u.Id)).ToListAsync(ct);
    }

    public Task<int> CountActiveAdminsAsync(CancellationToken ct)
    {
        return context.Set<User>().CountAsync(u => u.Role == UserRole.Admin && u.Status == UserStatus.Active, ct);
    }

    public async Task<List<(Guid CaseId, DeathCaseStatus Status, DateTimeOffset DueAt)>> ListOpenAssignmentsAsync(
        Guid verifierUserId, CancellationToken ct)
    {
        var rows = await context.Set<DeathCaseAssignment>().AsNoTracking()
            .Join(context.Set<DeathCase>().AsNoTracking(), a => a.CaseId, c => c.Id, (a, c) => new { Assignment = a, Case = c })
            .Where(x => x.Assignment.VerifierUserId == verifierUserId && x.Assignment.EndedAt == null
                && (x.Case.Status == DeathCaseStatus.UnderReview || x.Case.Status == DeathCaseStatus.SupplementRequired))
            .OrderBy(x => x.Assignment.DueAt)
            .Select(x => new { x.Case.Id, x.Case.Status, x.Assignment.DueAt })
            .ToListAsync(ct);
        return rows.Select(r => (r.Id, r.Status, r.DueAt)).ToList();
    }

    public Task<List<DeathCaseDecision>> ListRecentDecisionsAsync(Guid verifierUserId, int take, CancellationToken ct)
    {
        return context.Set<DeathCaseDecision>().AsNoTracking()
            .Where(d => d.VerifierUserId == verifierUserId)
            .OrderByDescending(d => d.DecidedAt)
            .ThenByDescending(d => d.Id)
            .Take(take)
            .ToListAsync(ct);
    }

    public Task<List<AuditLog>> ListRecentAuditByActorAsync(Guid userId, int take, CancellationToken ct)
    {
        return context.Set<AuditLog>().AsNoTracking()
            .Where(l => l.ActorUserId == userId)
            .OrderByDescending(l => l.At)
            .ThenByDescending(l => l.Id)
            .Take(take)
            .ToListAsync(ct);
    }

    public async Task<List<(PlanStatus Status, int Count)>> CountPlansByStatusAsync(CancellationToken ct)
    {
        var rows = await context.Set<HandoverPlan>().AsNoTracking()
            .GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        return rows.Select(r => (r.Status, r.Count)).ToList();
    }

    public async Task<List<(Guid VerifierUserId, int Open, int Overdue)>> CountVerifierLoadAsync(
        DateTimeOffset now, CancellationToken ct)
    {
        var rows = await context.Set<DeathCaseAssignment>().AsNoTracking()
            .Join(context.Set<DeathCase>().AsNoTracking(), a => a.CaseId, c => c.Id, (a, c) => new { Assignment = a, Case = c })
            .Where(x => x.Assignment.EndedAt == null
                && (x.Case.Status == DeathCaseStatus.UnderReview || x.Case.Status == DeathCaseStatus.SupplementRequired))
            .Select(x => x.Assignment)
            .GroupBy(a => a.VerifierUserId)
            .Select(g => new { VerifierUserId = g.Key, Open = g.Count(), Overdue = g.Count(a => a.DueAt < now) })
            .ToListAsync(ct);
        return rows.Select(r => (r.VerifierUserId, r.Open, r.Overdue)).ToList();
    }

    public async Task<List<(AuditLog Log, string? ActorFullName)>> ListRecentAuditAsync(int take, CancellationToken ct)
    {
        var rows = await (
            from log in context.Set<AuditLog>().AsNoTracking()
            join user in context.Set<User>().AsNoTracking() on log.ActorUserId equals user.Id into actors
            from actor in actors.DefaultIfEmpty()
            orderby log.At descending, log.Id descending
            select new { Log = log, Name = actor == null ? (string?)null : actor.FullName })
            .Take(take)
            .ToListAsync(ct);
        return rows.Select(r => (r.Log, (string?)r.Name)).ToList();
    }

    public Task<int> CountCasesWithoutVerifierAsync(CancellationToken ct)
    {
        return context.Set<DeathCase>().CountAsync(
            c => (c.Status == DeathCaseStatus.UnderReview || c.Status == DeathCaseStatus.SupplementRequired)
                && !c.Assignments.Any(a => a.EndedAt == null), ct);
    }
}
