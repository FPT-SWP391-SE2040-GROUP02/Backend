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

    public Task<Hold?> FindActiveOwnerAliveAsync(Guid vaultId, CancellationToken ct)
    {
        return context.Set<Hold>().FirstOrDefaultAsync(
            h => h.VaultId == vaultId && h.Type == HoldType.OwnerAlive && h.Status == HoldStatus.Active, ct);
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

    public async Task<(List<Incident> Items, int TotalCount)> ListAsync(
        IncidentStatus? status, IncidentType? type, Guid? assigneeUserId, bool overdueOnly, DateTimeOffset now,
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

    public Task<int> CountActiveAdminsAsync(CancellationToken ct)
    {
        return context.Set<User>().CountAsync(u => u.Role == UserRole.Admin && u.Status == UserStatus.Active, ct);
    }

    public Task<int> CountCasesWithoutVerifierAsync(CancellationToken ct)
    {
        return context.Set<DeathCase>().CountAsync(
            c => (c.Status == DeathCaseStatus.UnderReview || c.Status == DeathCaseStatus.SupplementRequired)
                && !c.Assignments.Any(a => a.EndedAt == null), ct);
    }
}
