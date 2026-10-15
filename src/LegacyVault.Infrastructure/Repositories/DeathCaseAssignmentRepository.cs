using LegacyVault.Application.Repositories;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.Infrastructure.Repositories;

/// <remarks>
/// Dùng context.Set&lt;T&gt;() thay vì DbSet trên AppDbContext để không sửa AppDbContext (D6, giống DeathCaseRepository).
/// </remarks>
public sealed class DeathCaseAssignmentRepository : IDeathCaseAssignmentRepository
{
    private readonly AppDbContext context;

    public DeathCaseAssignmentRepository(AppDbContext context)
    {
        this.context = context;
    }

    public Task<List<Guid>> FindCaseIdsForJobAsync(DateTimeOffset now, int maxCount, CancellationToken ct)
    {
        // D3: DueAt - 12 giờ <= now tương đương DueAt <= now + 12 giờ (tính một lần ở đây, không tính trên cột).
        DateTimeOffset remindFrom = now.AddHours(DeathCaseAssignmentRules.ReminderBeforeHours);

        return context.Set<DeathCase>()
            .AsNoTracking()
            .Where(c => c.Status == DeathCaseStatus.UnderReview || c.Status == DeathCaseStatus.SupplementRequired)
            .Where(c =>
                // (a) chưa có phân công đang hiệu lực
                !c.Assignments.Any(a => a.EndedAt == null)
                // (b) Verifier đang giữ không còn Active hoặc không còn vai trò Verifier
                || c.Assignments.Any(a => a.EndedAt == null
                    && context.Set<User>().Any(u => u.Id == a.VerifierUserId
                        && (u.Status != UserStatus.Active || u.Role != UserRole.Verifier)))
                // (c) UnderReview, còn không quá 12 giờ tới hạn và chưa nhắc cho hạn này
                || (c.Status == DeathCaseStatus.UnderReview
                    && c.Assignments.Any(a => a.EndedAt == null
                        && a.DueAt <= remindFrom
                        && (a.RemindedDueAt == null || a.RemindedDueAt != a.DueAt)))
                // (d) UnderReview và đã quá hạn
                || (c.Status == DeathCaseStatus.UnderReview
                    && c.Assignments.Any(a => a.EndedAt == null && a.DueAt <= now)))
            .OrderBy(c => c.FirstSubmittedAt)
            .ThenBy(c => c.Id)
            .Select(c => c.Id)
            .Take(maxCount)
            .ToListAsync(ct);
    }
}
