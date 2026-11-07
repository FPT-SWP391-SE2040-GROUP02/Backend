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

public sealed class HoldRepository : IHoldRepository
{
    private readonly AppDbContext context;

    public HoldRepository(AppDbContext context)
    {
        this.context = context;
    }

    public void Add(Hold hold)
    {
        context.Set<Hold>().Add(hold);
    }

    public Task<Hold?> FindByIdAsync(Guid holdId, CancellationToken ct)
    {
        return context.Set<Hold>().FirstOrDefaultAsync(h => h.Id == holdId, ct);
    }

    public Task<bool> HasActiveHoldAsync(Guid vaultId, CancellationToken ct)
    {
        return context.Set<Hold>().AnyAsync(h => h.VaultId == vaultId && h.Status == HoldStatus.Active, ct);
    }

    public async Task<(List<Hold> Items, int TotalCount)> ListAsync(
        HoldStatus? status, Guid? vaultId, int pageIndex, int pageSize, CancellationToken ct)
    {
        IQueryable<Hold> query = context.Set<Hold>().AsNoTracking();
        if (status != null)
        {
            query = query.Where(h => h.Status == status.Value);
        }
        if (vaultId != null)
        {
            query = query.Where(h => h.VaultId == vaultId.Value);
        }
        int total = await query.CountAsync(ct);
        List<Hold> items = await query
            .OrderBy(h => h.ReviewAt)
            .ThenBy(h => h.Id)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }

    public Task<int> CountActiveAsync(CancellationToken ct)
    {
        return context.Set<Hold>().CountAsync(h => h.Status == HoldStatus.Active, ct);
    }

    public Task<int> CountReviewOverdueAsync(DateTimeOffset now, CancellationToken ct)
    {
        return context.Set<Hold>().CountAsync(h => h.Status == HoldStatus.Active && h.ReviewAt <= now, ct);
    }

    public bool IsUniqueViolation(Exception exception)
    {
        return SqlErrors.IsUniqueViolation(exception);
    }
}

public sealed class IncidentRepository : IIncidentRepository
{
    private readonly AppDbContext context;

    public IncidentRepository(AppDbContext context)
    {
        this.context = context;
    }

    public void Add(Incident incident)
    {
        context.Set<Incident>().Add(incident);
    }

    public void AddEvent(IncidentEvent incidentEvent)
    {
        context.Set<IncidentEvent>().Add(incidentEvent);
    }

    public Task<Incident?> FindByIdAsync(Guid incidentId, CancellationToken ct)
    {
        return context.Set<Incident>().Include(i => i.Events).FirstOrDefaultAsync(i => i.Id == incidentId, ct);
    }

    public Task<Incident?> FindOpenAsync(IncidentType type, string targetType, Guid? targetId, CancellationToken ct)
    {
        return context.Set<Incident>().FirstOrDefaultAsync(
            i => i.Type == type && i.TargetType == targetType && i.TargetId == targetId && i.Status != IncidentStatus.Resolved, ct);
    }

    public Task<List<Guid>> ListOpenTargetIdsAsync(IncidentType type, CancellationToken ct)
    {
        return context.Set<Incident>().AsNoTracking()
            .Where(i => i.Type == type && i.Status != IncidentStatus.Resolved && i.TargetId != null)
            .Select(i => i.TargetId!.Value)
            .ToListAsync(ct);
    }

    public async Task<(List<Incident> Items, int TotalCount)> ListAsync(
        IncidentStatus? status, IncidentType? type, Guid? assigneeUserId, string? search, bool overdueOnly, DateTimeOffset now,
        int pageIndex, int pageSize, CancellationToken ct)
    {
        IQueryable<Incident> query = context.Set<Incident>().AsNoTracking();
        if (status != null)
        {
            query = query.Where(i => i.Status == status.Value);
        }
        if (type != null)
        {
            query = query.Where(i => i.Type == type.Value);
        }
        if (assigneeUserId != null)
        {
            query = query.Where(i => i.AssigneeUserId == assigneeUserId.Value);
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(i => i.Summary.Contains(s) || i.Id.ToString().Contains(s));
        }
        if (overdueOnly)
        {
            query = query.Where(i => i.Status != IncidentStatus.Resolved && i.DueAt <= now);
        }
        int total = await query.CountAsync(ct);
        List<Incident> items = await query
            .OrderBy(i => i.Status == IncidentStatus.Resolved)
            .ThenBy(i => i.DueAt)
            .ThenBy(i => i.Id)
            .Skip((pageIndex - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);
        return (items, total);
    }

    public Task<List<Guid>> FindIdsForJobAsync(DateTimeOffset now, int maxCount, CancellationToken ct)
    {
        return context.Set<Incident>()
            .Where(i => i.Status != IncidentStatus.Resolved
                && (i.AssigneeUserId == null
                    || (i.DueAt <= now && i.RemindedCount == 0)
                    || (i.EscalatedAt == null && i.CreatedAt <= now.AddHours(-72))))
            .OrderBy(i => i.DueAt)
            .ThenBy(i => i.Id)
            .Select(i => i.Id)
            .Take(maxCount)
            .ToListAsync(ct);
    }

    public Task<int> CountOpenAsync(CancellationToken ct)
    {
        return context.Set<Incident>().CountAsync(i => i.Status != IncidentStatus.Resolved, ct);
    }

    public Task<int> CountOverdueAsync(DateTimeOffset now, CancellationToken ct)
    {
        return context.Set<Incident>().CountAsync(i => i.Status != IncidentStatus.Resolved && i.DueAt <= now, ct);
    }

    public Task<int> CountUnassignedAsync(CancellationToken ct)
    {
        return context.Set<Incident>().CountAsync(i => i.Status != IncidentStatus.Resolved && i.AssigneeUserId == null, ct);
    }

    public async Task<Guid?> FindLeastLoadedAdminAsync(CancellationToken ct)
    {
        Guid? id = await context.Set<User>()
            .Where(u => u.Role == UserRole.Admin && u.Status == UserStatus.Active)
            .Select(u => new
            {
                u.Id,
                Open = context.Set<Incident>().Count(i => i.AssigneeUserId == u.Id && i.Status != IncidentStatus.Resolved),
            })
            .OrderBy(x => x.Open)
            .ThenBy(x => x.Id)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(ct);
        return id;
    }

    public bool IsUniqueViolation(Exception exception)
    {
        return SqlErrors.IsUniqueViolation(exception);
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

public sealed class OperationsScanRepository : IOperationsScanRepository
{
    private readonly AppDbContext context;

    public OperationsScanRepository(AppDbContext context)
    {
        this.context = context;
    }

    public async Task<List<StaleCaseRow>> ListStaleCasesAsync(DateTimeOffset now, int take, CancellationToken ct)
    {
        var rows = await context.Set<DeathCaseAssignment>().AsNoTracking()
            .Join(context.Set<DeathCase>().AsNoTracking(), a => a.CaseId, c => c.Id, (a, c) => new { Assignment = a, Case = c })
            .Where(x => x.Assignment.EndedAt == null && x.Assignment.DueAt < now
                && (x.Case.Status == DeathCaseStatus.UnderReview || x.Case.Status == DeathCaseStatus.SupplementRequired))
            .OrderBy(x => x.Assignment.DueAt)
            .Select(x => new { x.Case.Id, x.Case.VaultId, x.Assignment.VerifierUserId })
            .Take(take)
            .ToListAsync(ct);
        return rows.Select(r => new StaleCaseRow(r.Id, r.VaultId, r.VerifierUserId)).ToList();
    }

    public async Task<List<CaseRow>> ListSupplementLoopCasesAsync(int minCount, int take, CancellationToken ct)
    {
        var rows = await context.Set<DeathCase>().AsNoTracking()
            .Where(c => (c.Status == DeathCaseStatus.UnderReview || c.Status == DeathCaseStatus.SupplementRequired)
                && context.Set<DeathCaseDecision>().Count(
                    d => d.CaseId == c.Id && d.Kind == DeathCaseDecisionKind.SupplementRequested) >= minCount)
            .Select(c => new { c.Id, c.VaultId })
            .Take(take)
            .ToListAsync(ct);
        return rows.Select(r => new CaseRow(r.Id, r.VaultId)).ToList();
    }

    public async Task<List<PlanRow>> ListOverduePlansAsync(int take, CancellationToken ct)
    {
        var rows = await context.Set<HandoverPlan>().AsNoTracking()
            .Where(p => p.Status == PlanStatus.CheckinOverdue || p.Status == PlanStatus.AwaitingCheck)
            .OrderBy(p => p.UpdatedAt)
            .Select(p => new { p.Id, p.VaultId })
            .Take(take)
            .ToListAsync(ct);
        return rows.Select(r => new PlanRow(r.Id, r.VaultId)).ToList();
    }

    public Task<List<Guid>> ListExpiringVaultIdsAsync(DateTimeOffset now, DateTimeOffset until, int take, CancellationToken ct)
    {
        return context.Set<VaultEntitlement>().AsNoTracking()
            .Where(e => e.Status == EntitlementStatus.PaidActive && e.ServiceExpiresAt != null
                && e.ServiceExpiresAt > now && e.ServiceExpiresAt <= until)
            .OrderBy(e => e.ServiceExpiresAt)
            .Select(e => e.VaultId)
            .Take(take)
            .ToListAsync(ct);
    }
}
