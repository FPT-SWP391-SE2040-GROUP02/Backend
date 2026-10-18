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

    /// <summary>
    /// Hồ sơ đang xét/chờ bổ sung cần giao hoặc nhắc (a)-(d), và hồ sơ Suspended có báo còn sống Open cần giao hoặc giao lại (e, ALIVE-08).
    /// </summary>
    public Task<List<Guid>> FindCaseIdsForJobAsync(DateTimeOffset now, int maxCount, CancellationToken ct)
    {
        // D3: DueAt - 12 giờ <= now tương đương DueAt <= now + 12 giờ (tính một lần ở đây, không tính trên cột).
        DateTimeOffset remindFrom = now.AddHours(DeathCaseAssignmentRules.ReminderBeforeHours);

        return context.Set<DeathCase>()
            .AsNoTracking()
            .Where(c => c.Status == DeathCaseStatus.UnderReview
                || c.Status == DeathCaseStatus.SupplementRequired
                || c.Status == DeathCaseStatus.Suspended)
            .Where(c =>
                // Điều kiện (a)-(d) chỉ áp dụng cho hồ sơ đang xét hoặc chờ bổ sung
                ((c.Status == DeathCaseStatus.UnderReview || c.Status == DeathCaseStatus.SupplementRequired)
                    && (
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
                        && c.Assignments.Any(a => a.EndedAt == null && a.DueAt <= now))))
                // (e) ALIVE-08: hồ sơ Suspended có báo còn sống đang mở mà chưa có người giữ, người giữ mất quyền,
                // chưa có hạn Verifier hoặc đã quá hạn. Không lấy chỉ vì sắp tới hạn (không nhắc riêng).
                || (c.Status == DeathCaseStatus.Suspended
                    && context.Set<DeathCaseObjection>().Any(o => o.CaseId == c.Id
                        && o.Status == ObjectionStatus.Open
                        && (o.VerifierDueAt == null
                            || o.VerifierDueAt <= now
                            || !c.Assignments.Any(a => a.EndedAt == null)
                            || c.Assignments.Any(a => a.EndedAt == null
                                && context.Set<User>().Any(u => u.Id == a.VerifierUserId
                                    && (u.Status != UserStatus.Active || u.Role != UserRole.Verifier)))))))
            .OrderBy(c => c.FirstSubmittedAt)
            .ThenBy(c => c.Id)
            .Select(c => c.Id)
            .Take(maxCount)
            .ToListAsync(ct);
    }
}
