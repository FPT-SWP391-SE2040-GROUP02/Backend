using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>
/// Dữ liệu thử riêng của module hồ sơ chứng tử: tạo user, kho có kế hoạch đang chạy, Verifier, kỳ điểm danh.
/// Chèn thẳng DB qua ApiTestHelpers.WithDbAsync (không sửa ApiTestHelpers).
/// </summary>
public static class DeathCaseTestData
{
    /// <summary>Thêm một tài khoản đã xác minh email với vai trò và trạng thái cho trước.</summary>
    public static async Task<User> AddUserAsync(ApiFactory factory, UserRole role, UserStatus status)
    {
        User user = new User();
        user.Email = $"{Guid.NewGuid():N}@test.local";
        user.FullName = "Người thử";
        user.Role = role;
        user.Status = status;
        user.CreatedAt = DateTimeOffset.UtcNow;
        user.EmailVerifiedAt = DateTimeOffset.UtcNow;

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        });
        return user;
    }

    /// <summary>Thêm một Verifier đang hoạt động.</summary>
    public static Task<User> AddVerifierAsync(ApiFactory factory)
    {
        return AddUserAsync(factory, UserRole.Verifier, UserStatus.Active);
    }

    /// <summary>
    /// Owner mới tạo kho qua API rồi đặt kế hoạch sang ACTIVE (chưa có luồng kích hoạt kế hoạch).
    /// Trả Owner, client đã gắn token của Owner, Id kho và Id kế hoạch.
    /// </summary>
    public static async Task<OwnerVault> CreateOwnerWithRunningPlanAsync(ApiFactory factory)
    {
        User owner = await AddUserAsync(factory, UserRole.User, UserStatus.Active);
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, owner);
        Guid vaultId = await ApiTestHelpers.CreateVaultAsync(client);
        await ApiTestHelpers.SetPlanStatusAsync(factory, vaultId, PlanStatus.Active);

        Guid planId = Guid.Empty;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            planId = await db.HandoverPlans.Where(p => p.VaultId == vaultId).Select(p => p.Id).SingleAsync();
        });

        OwnerVault result = new OwnerVault();
        result.Owner = owner;
        result.Client = client;
        result.VaultId = vaultId;
        result.PlanId = planId;
        return result;
    }

    /// <summary>
    /// Thêm một kỳ điểm danh. checkedInAt có giá trị = Owner đã điểm danh lúc đó (kỳ đóng luôn);
    /// null = kỳ đang mở.
    /// </summary>
    public static async Task AddCheckInPeriodAsync(
        ApiFactory factory, Guid planId, int sequence, DateTimeOffset startedAt, DateTimeOffset? checkedInAt)
    {
        CheckInPeriod period = new CheckInPeriod();
        period.PlanId = planId;
        period.Sequence = sequence;
        period.StartedAt = startedAt;
        period.DueAt = startedAt.AddDays(30);
        period.GraceEndsAt = startedAt.AddDays(37);
        period.CheckedInAt = checkedInAt;
        period.ClosedAt = checkedInAt;

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            db.CheckInPeriods.Add(period);
            await db.SaveChangesAsync();
        });
    }

    /// <summary>Hồ sơ Draft mới kèm phiên bản 1 rỗng (chưa lưu).</summary>
    public static DeathCase NewDraftCase(OwnerVault vault, Guid openedByUserId)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DeathCase deathCase = new DeathCase();
        deathCase.VaultId = vault.VaultId;
        deathCase.PlanId = vault.PlanId;
        deathCase.Status = DeathCaseStatus.Draft;
        deathCase.CurrentVersionNo = 1;
        deathCase.OpenedByUserId = openedByUserId;
        deathCase.CreatedAt = now;

        DeathCaseVersion version = new DeathCaseVersion();
        version.CaseId = deathCase.Id;
        version.VersionNo = 1;
        version.CreatedByUserId = openedByUserId;
        version.CreatedAt = now;
        deathCase.Versions.Add(version);
        return deathCase;
    }

    /// <summary>Thêm một phân công đã kết thúc để Verifier có "lần được giao gần nhất" = assignedAt.</summary>
    public static async Task AddEndedAssignmentAsync(
        ApiFactory factory, Guid caseId, Guid verifierUserId, DateTimeOffset assignedAt)
    {
        DeathCaseAssignment assignment = new DeathCaseAssignment();
        assignment.CaseId = caseId;
        assignment.VerifierUserId = verifierUserId;
        assignment.AssignedAt = assignedAt;
        assignment.DueAt = assignedAt.AddHours(48);
        assignment.EndedAt = assignedAt.AddHours(1);
        assignment.EndReason = "TEST";

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            db.Set<DeathCaseAssignment>().Add(assignment);
            await db.SaveChangesAsync();
        });
    }

    /// <summary>Owner và kho vừa tạo.</summary>
    public sealed class OwnerVault
    {
        public User Owner { get; set; } = null!;

        public HttpClient Client { get; set; } = null!;

        public Guid VaultId { get; set; }

        public Guid PlanId { get; set; }
    }
}
