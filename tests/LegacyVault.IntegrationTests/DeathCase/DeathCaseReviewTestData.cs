using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>
/// Dữ liệu thử riêng của nhánh B (Verifier xét hồ sơ chứng tử): Verifier có client, hồ sơ đã gửi kèm
/// Verifier được giao thật, đồng hồ 72 giờ, kết thúc phân công, quyết định cuối. Chèn thẳng DB qua
/// ApiTestHelpers.WithDbAsync; không sửa ApiTestHelpers và DeathCaseTestData.
/// </summary>
public static class DeathCaseReviewTestData
{
    /// <summary>Một Verifier và client đã gắn token của Verifier đó.</summary>
    public sealed class VerifierClient
    {
        public User User { get; set; } = null!;

        public HttpClient Client { get; set; } = null!;
    }

    /// <summary>Hồ sơ đã gửi (UNDER_REVIEW) cùng các bên liên quan.</summary>
    public sealed class SubmittedCase
    {
        public DeathCaseTestData.OwnerVault Vault { get; set; } = null!;

        public DeathCaseTestData.ExecutorClient Executor { get; set; } = null!;

        public Guid CaseId { get; set; }

        /// <summary>JSON hồ sơ Executor nhận được ngay sau khi gửi.</summary>
        public JsonElement SubmittedBody { get; set; }

        /// <summary>Verifier ĐƯỢC GIAO THẬT (đọc từ phân công đang hiệu lực), không phải Verifier tạo trước.</summary>
        public VerifierClient Verifier { get; set; } = null!;

        public Guid AssignmentId { get; set; }
    }

    /// <summary>SEC-05: thêm một Verifier đang hoạt động kèm client đã gắn token.</summary>
    public static async Task<VerifierClient> AddVerifierClientAsync(ApiFactory factory)
    {
        User user = await DeathCaseTestData.AddVerifierAsync(factory);
        return CreateClientFor(factory, user);
    }

    public static VerifierClient CreateClientFor(ApiFactory factory, User user)
    {
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, user);
        VerifierClient result = new VerifierClient();
        result.User = user;
        result.Client = client;
        return result;
    }

    /// <summary>
    /// Dựng hồ sơ đã gửi: Owner kho trả phí có một gói READY có người nhận được tạo TRƯỚC khi gửi (cấu hình bị khóa
    /// sau khi gửi), kế hoạch ACTIVE, Executor Accepted điền đủ + một trang PDF rồi gửi qua DeathCaseApi.
    /// verifiersBeforeSubmit Verifier được tạo trước khi gửi để hệ thống có người phân công. Sau khi gửi, đọc
    /// DeathCaseAssignments (EndedAt null) để lấy Verifier ĐƯỢC GIAO THẬT (FindVerifierCandidateAsync chọn người
    /// lâu chưa được giao nhất, chưa từng giao đứng đầu); không có phân công thì ném lỗi rõ ràng.
    /// </summary>
    public static async Task<SubmittedCase> CreateSubmittedCaseAsync(ApiFactory factory, int verifiersBeforeSubmit = 1)
    {
        // Gói READY: kho trả phí, một tài sản chữ, một người nhận được chỉ định.
        (HttpClient ownerClient, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(ownerClient, vaultId, "Ghi chú thừa kế");
        Guid containerId = (await ApiTestHelpers.CreateContainerAsync(ownerClient, vaultId, "Gói thừa kế"))
            .GetProperty("id").GetGuid();
        HttpResponseMessage addAsset = await ApiTestHelpers.AddAssetToContainerAsync(ownerClient, vaultId, containerId, assetId);
        Assert.Equal(HttpStatusCode.OK, addAsset.StatusCode);
        JsonElement beneficiary = await ApiTestHelpers.CreateBeneficiaryAsync(ownerClient, vaultId, "001090000001", "Người nhận 1");
        HttpResponseMessage designate = await ApiTestHelpers.AddBeneficiaryToContainerAsync(
            ownerClient, vaultId, containerId, beneficiary.GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.OK, designate.StatusCode);
        await ApiTestHelpers.SetPlanStatusAsync(factory, vaultId, PlanStatus.Active);

        DeathCaseTestData.OwnerVault vault = new DeathCaseTestData.OwnerVault();
        vault.Client = ownerClient;
        vault.VaultId = vaultId;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            vault.PlanId = await db.HandoverPlans.Where(p => p.VaultId == vaultId).Select(p => p.Id).SingleAsync();
            Guid ownerId = await db.Vaults.Where(v => v.Id == vaultId).Select(v => v.OwnerId).SingleAsync();
            vault.Owner = await db.Users.AsNoTracking().SingleAsync(u => u.Id == ownerId);
        });

        for (int i = 0; i < verifiersBeforeSubmit; i++)
        {
            await DeathCaseTestData.AddVerifierAsync(factory);
        }

        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vaultId);
        JsonElement prepared = await DeathCaseApi.PrepareCompleteDraftAsync(
            executor.Client, vaultId, DeathCaseApi.Today().AddDays(-3));
        JsonElement submitted = await DeathCaseApi.SubmitOkAsync(executor.Client, prepared);
        Guid caseId = submitted.GetProperty("id").GetGuid();

        // Verifier được giao thật = chủ của phân công đang hiệu lực.
        DeathCaseAssignment? assignment = null;
        User? assigned = null;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            assignment = await db.Set<DeathCaseAssignment>()
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.CaseId == caseId && a.EndedAt == null);
            if (assignment != null)
            {
                assigned = await db.Users.AsNoTracking().SingleAsync(u => u.Id == assignment.VerifierUserId);
            }
        });
        if (assignment == null || assigned == null)
        {
            throw new InvalidOperationException(
                "Hồ sơ vừa gửi chưa có phân công đang hiệu lực: cần tạo ít nhất một Verifier hợp lệ trước khi Executor gửi.");
        }

        SubmittedCase result = new SubmittedCase();
        result.Vault = vault;
        result.Executor = executor;
        result.CaseId = caseId;
        result.SubmittedBody = submitted;
        result.Verifier = CreateClientFor(factory, assigned);
        result.AssignmentId = assignment.Id;
        return result;
    }

    /// <summary>
    /// DEATH-08: coi như Owner đã nhận thư lúc deliveredAt: thông báo Delivered, NotifiedVersionNo theo thông báo,
    /// ObjectionStartsAt = deliveredAt, ObjectionEndsAt = deliveredAt + 72 giờ. Đặt deliveredAt lùi quá 72 giờ
    /// để hết thời gian phản đối. Không đổi UpdatedAt của hồ sơ nên etag không đổi.
    /// </summary>
    public static async Task MarkOwnerNotifiedAsync(ApiFactory factory, Guid caseId, DateTimeOffset deliveredAt)
    {
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DeathCaseNotice notice = await db.Set<DeathCaseNotice>()
                .Where(n => n.CaseId == caseId)
                .OrderByDescending(n => n.CreatedAt)
                .FirstAsync();
            int noticeVersionNo = notice.VersionNo;
            await db.Set<DeathCaseNotice>().Where(n => n.Id == notice.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.Status, DeathCaseNoticeStatus.Delivered)
                    .SetProperty(n => n.DeliveredAt, deliveredAt)
                    .SetProperty(n => n.AttemptCount, 1));
            await db.Set<DeathCase>().Where(c => c.Id == caseId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.NotifiedVersionNo, noticeVersionNo)
                    .SetProperty(c => c.ObjectionStartsAt, deliveredAt)
                    .SetProperty(c => c.ObjectionEndsAt, deliveredAt.AddHours(72)));
        });
    }

    /// <summary>Kết thúc phân công đang hiệu lực của hồ sơ (giả lập nhánh C giao lại).</summary>
    public static async Task EndAssignmentAsync(ApiFactory factory, Guid caseId, DateTimeOffset endedAt)
    {
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<DeathCaseAssignment>().Where(a => a.CaseId == caseId && a.EndedAt == null)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(a => a.EndedAt, endedAt)
                    .SetProperty(a => a.EndReason, "TEST"));
        });
    }

    /// <summary>
    /// Chèn thẳng một dòng quyết định của Verifier (kind bất kỳ), lấy VersionNo từ ReviewVersionNo của hồ sơ.
    /// Trả Id dòng vừa thêm. Cần phân công assignmentId đã tồn tại.
    /// </summary>
    public static async Task<Guid> AddFinalDecisionAsync(
        ApiFactory factory, Guid caseId, DeathCaseDecisionKind kind, Guid verifierUserId, Guid assignmentId)
    {
        DeathCaseDecision decision = new DeathCaseDecision();
        decision.CaseId = caseId;
        decision.Kind = kind;
        decision.Reason = "Lý do thử";
        decision.VerifierUserId = verifierUserId;
        decision.AssignmentId = assignmentId;
        decision.DecidedAt = DateTimeOffset.UtcNow;
        if (kind == DeathCaseDecisionKind.SupplementRequested)
        {
            decision.RequestedParts = "FILES";
        }

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DeathCase deathCase = await db.Set<DeathCase>().AsNoTracking().SingleAsync(c => c.Id == caseId);
            decision.VersionNo = 1;
            if (deathCase.ReviewVersionNo != null)
            {
                decision.VersionNo = deathCase.ReviewVersionNo.Value;
            }
            db.Set<DeathCaseDecision>().Add(decision);
            await db.SaveChangesAsync();
        });
        return decision.Id;
    }

    /// <summary>
    /// DEATH-10: chèn thẳng một cờ CHECKIN_AFTER_DEATH chưa xem xét vào bản đang xét (ReviewVersionNo) của hồ sơ.
    /// Không đổi UpdatedAt của hồ sơ nên etag không đổi. Trả Id cờ.
    /// </summary>
    public static async Task<Guid> AddFlagAsync(ApiFactory factory, Guid caseId)
    {
        return await AddFlagToVersionAsync(factory, caseId, null);
    }

    /// <summary>Như AddFlagAsync nhưng gắn vào bản có số versionNo (null = bản đang xét).</summary>
    public static async Task<Guid> AddFlagToVersionAsync(ApiFactory factory, Guid caseId, int? versionNo)
    {
        DeathCaseFlag flag = new DeathCaseFlag();
        flag.CaseId = caseId;
        flag.Kind = DeathCaseFlagKind.CheckinAfterDeath;
        flag.Detail = "Owner có điểm danh sau ngày mất ghi trên giấy chứng tử.";
        flag.CreatedAt = DateTimeOffset.UtcNow;

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DeathCase deathCase = await db.Set<DeathCase>().AsNoTracking().SingleAsync(c => c.Id == caseId);
            int target = 1;
            if (versionNo != null)
            {
                target = versionNo.Value;
            }
            else if (deathCase.ReviewVersionNo != null)
            {
                target = deathCase.ReviewVersionNo.Value;
            }
            flag.VersionId = await db.Set<DeathCaseVersion>()
                .Where(v => v.CaseId == caseId && v.VersionNo == target)
                .Select(v => v.Id)
                .SingleAsync();
            db.Set<DeathCaseFlag>().Add(flag);
            await db.SaveChangesAsync();
        });
        return flag.Id;
    }

    /// <summary>Phân công đang hiệu lực (EndedAt null) của hồ sơ; không có → null.</summary>
    public static async Task<DeathCaseAssignment?> LoadActiveAssignmentAsync(ApiFactory factory, Guid caseId)
    {
        DeathCaseAssignment? assignment = null;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            assignment = await db.Set<DeathCaseAssignment>().AsNoTracking()
                .FirstOrDefaultAsync(a => a.CaseId == caseId && a.EndedAt == null);
        });
        return assignment;
    }

    /// <summary>Chèn thẳng một phân công mới (giả lập nhánh C giao lại). Phân công cũ phải đã kết thúc.</summary>
    public static async Task<DeathCaseAssignment> AddAssignmentAsync(
        ApiFactory factory, Guid caseId, Guid verifierUserId, DateTimeOffset assignedAt, DateTimeOffset dueAt)
    {
        DeathCaseAssignment assignment = new DeathCaseAssignment();
        assignment.CaseId = caseId;
        assignment.VerifierUserId = verifierUserId;
        assignment.AssignedAt = assignedAt;
        assignment.DueAt = dueAt;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            db.Set<DeathCaseAssignment>().Add(assignment);
            await db.SaveChangesAsync();
        });
        return assignment;
    }

    /// <summary>Đặt hạn xét của phân công đang hiệu lực (để thử đặt lại DueAt).</summary>
    public static async Task SetActiveDueAtAsync(ApiFactory factory, Guid caseId, DateTimeOffset dueAt)
    {
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<DeathCaseAssignment>().Where(a => a.CaseId == caseId && a.EndedAt == null)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.DueAt, dueAt));
        });
    }

    public const string VerifierListUrl = "/api/v1/verifier/death-cases";

    public static string VerifierCaseUrl(Guid caseId)
    {
        return VerifierListUrl + "/" + caseId;
    }

    public static string VerifierFileUrl(Guid caseId, Guid fileId)
    {
        return VerifierCaseUrl(caseId) + "/files/" + fileId + "/content";
    }

    /// <summary>Verifier đọc chi tiết hồ sơ (phải 200), trả JSON.</summary>
    public static async Task<JsonElement> GetDetailAsync(HttpClient verifier, Guid caseId)
    {
        HttpResponseMessage response = await verifier.GetAsync(VerifierCaseUrl(caseId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ApiTestHelpers.ReadJsonAsync(response);
    }

    public static Task<HttpResponseMessage> ReviewFlagAsync(
        HttpClient verifier, Guid caseId, Guid flagId, string? etag, string? note)
    {
        return verifier.PostAsJsonAsync(
            VerifierCaseUrl(caseId) + "/flags/" + flagId + "/review", new { etag, note });
    }

    public static Task<HttpResponseMessage> RequestSupplementAsync(
        HttpClient verifier, Guid caseId, string? etag, string? reason, string[]? requestedParts)
    {
        return verifier.PostAsJsonAsync(
            VerifierCaseUrl(caseId) + "/request-supplement", new { etag, reason, requestedParts });
    }

    public static Task<HttpResponseMessage> RejectAsync(HttpClient verifier, Guid caseId, string? etag, string? reason)
    {
        return verifier.PostAsJsonAsync(VerifierCaseUrl(caseId) + "/reject", new { etag, reason });
    }

    /// <summary>Verifier đọc lấy etag hiện tại rồi từ chối với lý do cho trước; trả response của lệnh từ chối.</summary>
    public static async Task<HttpResponseMessage> RejectNowAsync(SubmittedCase submitted, string reason)
    {
        JsonElement detail = await GetDetailAsync(submitted.Verifier.Client, submitted.CaseId);
        return await RejectAsync(submitted.Verifier.Client, submitted.CaseId, detail.GetProperty("etag").GetString(), reason);
    }

    /// <summary>Đặt StatusBeforeCase của hồ sơ (giả lập hồ sơ gửi khi kế hoạch đang ở trạng thái khác ACTIVE).</summary>
    public static async Task SetStatusBeforeCaseAsync(ApiFactory factory, Guid caseId, PlanStatus? status)
    {
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<DeathCase>().Where(c => c.Id == caseId)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.StatusBeforeCase, status));
        });
    }

    /// <summary>Thêm một kỳ điểm danh đang mở bắt đầu daysAgo ngày trước; trả Id kỳ.</summary>
    public static async Task<Guid> AddOpenPeriodAsync(ApiFactory factory, Guid planId, int daysAgo)
    {
        await DeathCaseTestData.AddCheckInPeriodAsync(factory, planId, 1, DateTimeOffset.UtcNow.AddDays(-daysAgo), null);
        Guid periodId = Guid.Empty;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            periodId = await db.CheckInPeriods.Where(p => p.PlanId == planId).Select(p => p.Id).SingleAsync();
        });
        return periodId;
    }

    public static async Task<HandoverPlan> LoadPlanAsync(ApiFactory factory, Guid vaultId)
    {
        HandoverPlan? plan = null;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            plan = await db.HandoverPlans.AsNoTracking().SingleAsync(p => p.VaultId == vaultId);
        });
        return plan!;
    }

    /// <summary>Executor đọc lại hồ sơ (để lấy etag mới nhất, vì thao tác của Verifier làm đổi etag).</summary>
    public static async Task<JsonElement> GetExecutorCaseAsync(SubmittedCase submitted)
    {
        HttpResponseMessage response = await submitted.Executor.Client.GetAsync(DeathCaseApi.CaseUrl(submitted.CaseId));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ApiTestHelpers.ReadJsonAsync(response);
    }

    /// <summary>Executor tạo phiên bản bổ sung từ bản đã gửi (201), trả JSON hồ sơ có bản nháp mới.</summary>
    public static async Task<JsonElement> CreateDraftVersionAsync(SubmittedCase submitted)
    {
        JsonElement current = await GetExecutorCaseAsync(submitted);
        HttpResponseMessage response = await submitted.Executor.Client.PostAsJsonAsync(
            DeathCaseApi.CaseUrl(submitted.CaseId) + "/versions",
            new { etag = current.GetProperty("etag").GetString() });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ApiTestHelpers.ReadJsonAsync(response);
    }

    /// <summary>
    /// Executor gửi lại: tạo bản nháp mới, đổi nơi cấp (không trọng yếu nên không báo lại Owner) rồi gửi. Trả JSON sau khi gửi.
    /// </summary>
    public static async Task<JsonElement> ResubmitAsync(SubmittedCase submitted)
    {
        JsonElement draft = await CreateDraftVersionAsync(submitted);
        HttpResponseMessage edit = await submitted.Executor.Client.PutAsJsonAsync(
            DeathCaseApi.CaseUrl(submitted.CaseId) + "/draft",
            new { etag = draft.GetProperty("etag").GetString(), issuedBy = "UBND phường Bến Thành" });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        JsonElement edited = await ApiTestHelpers.ReadJsonAsync(edit);
        return await DeathCaseApi.SubmitOkAsync(submitted.Executor.Client, edited);
    }
}
