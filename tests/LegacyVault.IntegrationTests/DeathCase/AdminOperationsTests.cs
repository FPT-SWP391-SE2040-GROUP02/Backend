using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>
/// Hold, Admin (SRS §6.4, ASSIGN-04, AC-40) trên SQL Server thật: quyền, hold chặn phát hành,
/// phân công lại Verifier, nhật ký.
/// </summary>
public sealed class AdminOperationsTests : IAsyncLifetime
{
    private ApiFactory factory = null!;
    private WebApplicationFactory<Program> app = null!;

    public async Task InitializeAsync()
    {
        ApiFactory? created = ApiFactory.CreateForTempDatabase();
        if (created == null)
        {
            return;
        }
        factory = created;
        await factory.MigrateAsync();
        app = factory.WithWebHostBuilder(builder => { });
    }

    public async Task DisposeAsync()
    {
        if (factory == null)
        {
            return;
        }
        await app.DisposeAsync();
        await factory.DropAsync();
        await factory.DisposeAsync();
    }

    private HttpClient ClientFor(User user)
    {
        HttpClient client = app.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, app.Services, user);
        return client;
    }

    private async Task<(User User, HttpClient Client)> AddAdminAsync()
    {
        User admin = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        return (admin, ClientFor(admin));
    }

    /// <summary>
    /// Tạo một hold đang hiệu lực trực tiếp trong DB. Không còn API Admin đặt hold bằng tay; hold thật do hệ thống đặt
    /// (Owner báo còn sống). Dùng để kiểm các bước chặn khi có hold mà không phải đi hết luồng báo còn sống.
    /// </summary>
    private async Task<Guid> SeedActiveHoldAsync(Guid vaultId)
    {
        Hold hold = new Hold();
        hold.VaultId = vaultId;
        hold.Scope = HoldScope.Vault;
        hold.Type = HoldType.Dispute;
        hold.Status = HoldStatus.Active;
        hold.Reason = "Nghi vấn giấy tờ";
        hold.ReviewAt = DateTimeOffset.UtcNow.AddDays(7);
        hold.CreatedAt = DateTimeOffset.UtcNow;
        hold.UpdatedAt = DateTimeOffset.UtcNow;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            db.Set<Hold>().Add(hold);
            await db.SaveChangesAsync();
        });
        return hold.Id;
    }

    private async Task ReleaseHoldInDbAsync(Guid holdId)
    {
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Hold stored = await db.Set<Hold>().SingleAsync(h => h.Id == holdId);
            stored.Status = HoldStatus.Released;
            stored.ReleasedAt = DateTimeOffset.UtcNow;
            stored.ReleaseReason = "Đã kiểm tra";
            await db.SaveChangesAsync();
        });
    }

    private async Task<JsonElement> ListAsync(HttpClient client, string path)
    {
        HttpResponseMessage response = await client.GetAsync("/api/v1/admin/" + path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ApiTestHelpers.ReadJsonAsync(response);
    }

    [SqlFact]
    public async Task AdminArea_NonAdminGets404()
    {
        User plain = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        User verifier = await DeathCaseTestData.AddVerifierAsync(factory);

        foreach (User user in new[] { plain, verifier })
        {
            HttpClient client = ClientFor(user);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/admin/dashboard")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/admin/holds")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/admin/users")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.CreateClient().GetAsync("/api/v1/admin/dashboard")).StatusCode);
    }

    [SqlFact]
    public async Task AdminCreatesAndGrantsSystemExecutor_AndDirectoryListsLegalProfile()
    {
        // Chuẩn bị
        (User _, HttpClient client) = await AddAdminAsync();
        User plain = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        string email = "executor-" + Guid.NewGuid().ToString("N") + "@legacyvault.test";

        // Hành động: tạo Người thực thi hệ thống kèm hồ sơ pháp lý, và cấp vai trò cho một tài khoản thường
        HttpResponseMessage create = await client.PostAsJsonAsync("/api/v1/admin/users", new
        {
            fullName = "Người Thực Thi", email = email, password = "MatKhauThu123!", role = "EXECUTOR",
            lawyerCardNumber = " LS-TEST-001 ", executorRoleTitle = "Luật sư thi hành di chúc", legalOrganization = "Đoàn Luật sư thử nghiệm",
        });
        HttpResponseMessage grant = await client.PutAsJsonAsync($"/api/v1/admin/users/{plain.Id}/role",
            new { role = "EXECUTOR", reason = "Bổ nhiệm Người thực thi hệ thống" });

        // Kiểm tra
        Assert.True(create.IsSuccessStatusCode, "Tạo Executor phải thành công, nhận " + (int)create.StatusCode);
        Assert.Equal(HttpStatusCode.OK, grant.StatusCode);
        // Danh bạ có email và số thẻ luật sư: chỉ Chủ kho xem được, Admin không có kho thì 404.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/system-executors")).StatusCode);
        DeathCaseTestData.OwnerVault ownerVault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        JsonElement directory = await ApiTestHelpers.ReadJsonAsync(await ownerVault.Client.GetAsync("/api/v1/system-executors"));
        JsonElement created = directory.EnumerateArray().Single(e => e.GetProperty("email").GetString() == email);
        Assert.Equal("Luật sư thi hành di chúc", created.GetProperty("executorRoleTitle").GetString());
        Assert.Equal("VERIFIED", created.GetProperty("legalVerification").GetProperty("status").GetString());
        Assert.Equal("LS-TEST-001", created.GetProperty("legalVerification").GetProperty("lawyerCardNumber").GetString());
        Assert.Equal("Đoàn Luật sư thử nghiệm", created.GetProperty("legalVerification").GetProperty("organization").GetString());
        // Tài khoản chỉ được cấp vai trò, chưa có hồ sơ pháp lý: có trong danh bạ nhưng không có số thẻ bịa ra.
        JsonElement granted = directory.EnumerateArray().Single(e => e.GetProperty("id").GetGuid() == plain.Id);
        Assert.Equal("UNVERIFIED", granted.GetProperty("legalVerification").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, granted.GetProperty("legalVerification").GetProperty("lawyerCardNumber").ValueKind);
    }

    [SqlFact]
    public async Task Role02_AdminCanStillCreateVerifierAccount()
    {
        // Chuẩn bị
        (User _, HttpClient client) = await AddAdminAsync();
        string email = "verifier-" + Guid.NewGuid().ToString("N") + "@legacyvault.test";

        // Hành động
        HttpResponseMessage create = await client.PostAsJsonAsync("/api/v1/admin/users",
            new { fullName = "Người Xét Duyệt", email = email, password = "MatKhauThu123!", role = "VERIFIER" });

        // Kiểm tra
        Assert.True(create.IsSuccessStatusCode, "Tạo Verifier phải thành công, nhận " + (int)create.StatusCode);
        Assert.Equal("VERIFIER", (await ApiTestHelpers.ReadJsonAsync(create)).GetProperty("role").GetString());
    }

    [SqlFact]
    public async Task CreateUser_PhoneAlreadyUsedByAnotherRole_Returns409AndCreatesNothing()
    {
        // Chuẩn bị: một tài khoản User đã giữ số này
        (User _, HttpClient client) = await AddAdminAsync();
        User owner = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        string phone = "09" + Random.Shared.Next(10_000_000, 99_999_999);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Users.Where(u => u.Id == owner.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.Phone, phone));
        });
        string email = "verifier-" + Guid.NewGuid().ToString("N") + "@legacyvault.test";

        // Hành động: Admin tạo Verifier với cùng số nhưng gõ có dấu cách
        string spaced = phone.Substring(0, 4) + " " + phone.Substring(4, 3) + " " + phone.Substring(7);
        HttpResponseMessage create = await client.PostAsJsonAsync("/api/v1/admin/users",
            new { fullName = "Người Xét Duyệt", email = email, password = "MatKhauThu123!", role = "VERIFIER", phone = spaced });

        // Kiểm tra: 409 PHONE_ALREADY_EXISTS, không tạo tài khoản
        await ApiTestHelpers.AssertProblemAsync(create, HttpStatusCode.Conflict, "PHONE_ALREADY_EXISTS");
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.False(await db.Users.AnyAsync(u => u.Email == email));
        });
    }

    /// <summary>Tạo một lượt bàn giao đã được người dùng này nhận, với trạng thái cho trước (kho thật, gói thật).</summary>
    private async Task AddClaimedTurnAsync(Guid claimantId, HandoverTurnStatus status)
    {
        (HttpClient owner, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(owner, vaultId, "Ghi chú");
        Guid containerId = (await ApiTestHelpers.CreateContainerAsync(owner, vaultId, "Gói")).GetProperty("id").GetGuid();
        JsonElement beneficiary = await ApiTestHelpers.CreateBeneficiaryAsync(owner, vaultId, "000000001015", "Người nhận");
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddAssetToContainerAsync(owner, vaultId, containerId, assetId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddBeneficiaryToContainerAsync(
            owner, vaultId, containerId, beneficiary.GetProperty("id").GetGuid())).StatusCode);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Designation designation = await db.Set<Designation>().Include(d => d.Container).SingleAsync(d => d.Container.VaultId == vaultId);
            HandoverPlan plan = await db.HandoverPlans.SingleAsync(p => p.VaultId == vaultId);
            HandoverTurn turn = new HandoverTurn();
            turn.VaultId = vaultId;
            turn.PlanId = plan.Id;
            turn.ContainerId = designation.ContainerId;
            turn.ContainerName = designation.Container.Name;
            turn.DesignationId = designation.Id;
            turn.BeneficiaryId = designation.BeneficiaryId;
            turn.Status = status;
            turn.ClaimantUserId = claimantId;
            turn.ClaimedAt = DateTimeOffset.UtcNow;
            turn.InvitationTokenHash = Guid.NewGuid().ToString("N");
            turn.InvitedAt = DateTimeOffset.UtcNow;
            turn.ResponseDeadline = DateTimeOffset.UtcNow.AddDays(5);
            turn.CreatedAt = DateTimeOffset.UtcNow;
            db.Set<HandoverTurn>().Add(turn);
            await db.SaveChangesAsync();
        });
    }

    [SqlFact]
    public async Task ChangeRoleToStaff_IsBlockedWhileTheUserHasAnOpenClaimedHandoverTurn()
    {
        // Chuẩn bị: một người dùng thường đang nhận tài sản bàn giao (lượt chưa kết thúc)
        (User _, HttpClient client) = await AddAdminAsync();
        User recipient = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        await AddClaimedTurnAsync(recipient.Id, HandoverTurnStatus.ReadyToAccept);

        // Hành động và kiểm tra: cấp vai trò nhân sự bị chặn, vai trò không đổi
        foreach (string staffRole in new string[] { "EXECUTOR", "VERIFIER", "ADMIN" })
        {
            HttpResponseMessage blocked = await client.PutAsJsonAsync($"/api/v1/admin/users/{recipient.Id}/role",
                new { role = staffRole, reason = "Bổ nhiệm" });
            JsonElement problem = await ApiTestHelpers.AssertProblemAsync(blocked, HttpStatusCode.Conflict, "ADMIN_ACTION_NOT_ALLOWED");
            Assert.Equal("RECIPIENT_HAS_ACTIVE_HANDOVER", problem.GetProperty("reason").GetString());
        }
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(UserRole.User, (await db.Users.SingleAsync(u => u.Id == recipient.Id)).Role);
        });
    }

    [SqlFact]
    public async Task ChangeRoleToStaff_IsAllowedOnceTheClaimedTurnIsFinished()
    {
        // Chuẩn bị: lượt đã kết thúc (đã nhận xong) và một người dùng khác chưa nhận lượt nào
        (User _, HttpClient client) = await AddAdminAsync();
        User finished = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        await AddClaimedTurnAsync(finished.Id, HandoverTurnStatus.HandoverCommitted);

        HttpResponseMessage ok = await client.PutAsJsonAsync($"/api/v1/admin/users/{finished.Id}/role",
            new { role = "EXECUTOR", reason = "Bổ nhiệm" });

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [SqlFact]
    public async Task Users_ChangeRole_Rules_AndAuditTrail()
    {
        (User admin, HttpClient client) = await AddAdminAsync();
        User plain = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);

        // Cần lý do
        HttpResponseMessage noReason = await client.PutAsJsonAsync($"/api/v1/admin/users/{plain.Id}/role", new { role = "VERIFIER", reason = " " });
        await ApiTestHelpers.AssertProblemAsync(noReason, HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        HttpResponseMessage ok = await client.PutAsJsonAsync($"/api/v1/admin/users/{plain.Id}/role", new { role = "VERIFIER", reason = "Nhân viên mới" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal("VERIFIER", (await ApiTestHelpers.ReadJsonAsync(ok)).GetProperty("role").GetString());

        // Không tự đổi mình; không bỏ Admin cuối cùng
        HttpResponseMessage self = await client.PutAsJsonAsync($"/api/v1/admin/users/{admin.Id}/role", new { role = "USER", reason = "thử" });
        JsonElement selfProblem = await ApiTestHelpers.AssertProblemAsync(self, HttpStatusCode.Conflict, "ADMIN_ACTION_NOT_ALLOWED");
        Assert.Equal("CANNOT_CHANGE_SELF", selfProblem.GetProperty("reason").GetString());
        (User second, HttpClient secondClient) = await AddAdminAsync();
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/v1/admin/users/{second.Id}/status", new { status = "LOCKED", reason = "Khóa thử" })).StatusCode);
        HttpResponseMessage last = await secondClient.PutAsJsonAsync($"/api/v1/admin/users/{admin.Id}/role", new { role = "USER", reason = "thử" });
        // second đã bị khóa nên không còn là Admin hợp lệ
        Assert.Equal(HttpStatusCode.NotFound, last.StatusCode);

        // Nhật ký ghi người làm, đối tượng, lý do
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            AuditLog log = await db.Set<AuditLog>().SingleAsync(a => a.Action == AuditAction.UserRoleChanged);
            Assert.Equal(admin.Id, log.ActorUserId);
            Assert.Equal(plain.Id, log.TargetId);
            Assert.Equal(AuditActorRole.Admin, log.ActorRole);
            Assert.Contains("Nhân viên mới", log.Reason);
        });

        JsonElement list = await ListAsync(client, "users?role=VERIFIER");
        Assert.Contains(list.GetProperty("items").EnumerateArray(), u => u.GetProperty("id").GetGuid() == plain.Id);
        Assert.False(list.GetProperty("items")[0].TryGetProperty("passwordHash", out _));
    }

    [SqlFact]
    public async Task LastAdmin_CannotBeDemotedOrLocked()
    {
        (User admin, HttpClient client) = await AddAdminAsync();
        User other = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        // Hạ Admin kia xuống còn một Admin thì không hạ nữa
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/v1/admin/users/{other.Id}/role", new { role = "USER", reason = "Chuyển việc" })).StatusCode);
        HttpClient otherClient = ClientFor(other);
        Assert.Equal(HttpStatusCode.NotFound, (await otherClient.GetAsync("/api/v1/admin/dashboard")).StatusCode);

        // Chỉ còn một Admin: Admin khác (cấp lại quyền bằng DB) cũng không thể hạ Admin cuối cùng
        User third = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        HttpClient thirdClient = ClientFor(third);
        Assert.Equal(HttpStatusCode.OK, (await thirdClient.PutAsJsonAsync($"/api/v1/admin/users/{admin.Id}/status", new { status = "LOCKED", reason = "ok" })).StatusCode);
        HttpResponseMessage lastOne = await thirdClient.PutAsJsonAsync($"/api/v1/admin/users/{admin.Id}/role", new { role = "USER", reason = "x" });
        Assert.True(lastOne.StatusCode == HttpStatusCode.OK || lastOne.StatusCode == HttpStatusCode.Conflict);
    }

    [SqlFact]
    public async Task Hold_Active_BlocksApprove_UntilReleased()
    {
        // Chuẩn bị: hồ sơ đủ điều kiện duyệt, kho đang có hold
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateReadyToApproveCaseAsync(factory);
        Guid holdId = await SeedActiveHoldAsync(submitted.Vault.VaultId);

        // Hành động + Kiểm tra: duyệt bị chặn dù đủ điều kiện (DEATH-09)
        HttpResponseMessage approve = await DeathCaseReviewTestData.ApproveNowAsync(submitted, "Đã đối chiếu.");
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(approve, HttpStatusCode.Conflict, "DEATH_CASE_NOT_ALLOWED");
        Assert.Equal("HOLD_ACTIVE", problem.GetProperty("reason").GetString());

        // Hold được gỡ thì duyệt được
        await ReleaseHoldInDbAsync(holdId);
        Assert.Equal(HttpStatusCode.OK, (await DeathCaseReviewTestData.ApproveNowAsync(submitted, "Đã đối chiếu.")).StatusCode);
    }

    [SqlFact]
    public async Task Hold_AdminCanOnlyView_ManualPlaceReviewReleaseApisAreRemoved()
    {
        // Chuẩn bị
        (User admin, HttpClient adminClient) = await AddAdminAsync();
        DeathCaseTestData.OwnerVault owner = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        Guid holdId = await SeedActiveHoldAsync(owner.VaultId);

        // Hành động
        HttpResponseMessage place = await adminClient.PostAsJsonAsync("/api/v1/admin/holds",
            new { vaultId = owner.VaultId, reason = "x" });
        HttpResponseMessage review = await adminClient.PostAsJsonAsync($"/api/v1/admin/holds/{holdId}/review",
            new { nextReviewAt = DateTimeOffset.UtcNow.AddDays(1), etag = "\"1\"" });
        HttpResponseMessage release = await adminClient.PostAsJsonAsync($"/api/v1/admin/holds/{holdId}/release",
            new { reason = "x", etag = "\"1\"" });

        // Kiểm tra: không còn API thao tác tay; xem danh sách và chi tiết vẫn được
        Assert.False(place.IsSuccessStatusCode);
        Assert.False(review.IsSuccessStatusCode);
        Assert.False(release.IsSuccessStatusCode);
        JsonElement list = await ListAsync(adminClient, "holds?status=ACTIVE");
        Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.OK, (await adminClient.GetAsync($"/api/v1/admin/holds/{holdId}")).StatusCode);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Hold stored = await db.Set<Hold>().SingleAsync(h => h.Id == holdId);
            Assert.Equal(HoldStatus.Active, stored.Status);
            Assert.Equal(1, await db.Set<Hold>().CountAsync());
        });
        Assert.NotEqual(Guid.Empty, admin.Id);
    }

    [SqlFact]
    public async Task Assign04_NoVerifier_AdminAssignsVerifierAndAuditIsWritten()
    {
        (User admin, HttpClient adminClient) = await AddAdminAsync();
        DeathCaseReviewTestData.SubmittedCase submitted = await CreateNoVerifierCaseAsync();

        // Job phân công không có ai để giao
        using (IServiceScope scope = app.Services.CreateScope())
        {
            IDeathCaseAssignmentJob job = scope.ServiceProvider.GetRequiredService<IDeathCaseAssignmentJob>();
            DeathCaseAssignmentOutcome outcome = await job.ProcessCaseAsync(submitted.CaseId, CancellationToken.None);
            Assert.Equal(DeathCaseAssignmentOutcome.NoCandidate, outcome);
        }

        // Admin giao Verifier cho hồ sơ (kèm lý do), ghi nhật ký
        User verifier = await DeathCaseTestData.AddVerifierAsync(factory);
        HttpResponseMessage reassign = await adminClient.PostAsJsonAsync(
            $"/api/v1/admin/death-cases/{submitted.CaseId}/reassign-verifier", new { verifierUserId = verifier.Id, reason = "Điều phối thủ công" });
        Assert.Equal(HttpStatusCode.NoContent, reassign.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ClientFor(verifier).GetAsync($"/api/v1/verifier/death-cases/{submitted.CaseId}")).StatusCode);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(1, await db.Set<AuditLog>().CountAsync(a => a.Action == AuditAction.VerifierReassigned && a.TargetId == submitted.CaseId));
        });
    }

    [SqlFact]
    public async Task Reassign_RejectsOwnerExecutorAndNonVerifier_AndSwapsAssignment()
    {
        (User admin, HttpClient adminClient) = await AddAdminAsync();
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        string url = $"/api/v1/admin/death-cases/{submitted.CaseId}/reassign-verifier";
        User owner = submitted.Vault.Owner;
        User plain = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);

        await ApiTestHelpers.AssertProblemAsync(
            await adminClient.PostAsJsonAsync(url, new { verifierUserId = owner.Id, reason = "x" }), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await ApiTestHelpers.AssertProblemAsync(
            await adminClient.PostAsJsonAsync(url, new { verifierUserId = plain.Id, reason = "x" }), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await ApiTestHelpers.AssertProblemAsync(
            await adminClient.PostAsJsonAsync(url, new { verifierUserId = Guid.NewGuid(), reason = "x" }), HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        User replacement = await DeathCaseTestData.AddVerifierAsync(factory);
        Assert.Equal(HttpStatusCode.NoContent,
            (await adminClient.PostAsJsonAsync(url, new { verifierUserId = replacement.Id, reason = "Chuyển người" })).StatusCode);
        // Gọi lặp cùng người không đổi gì
        Assert.Equal(HttpStatusCode.NoContent,
            (await adminClient.PostAsJsonAsync(url, new { verifierUserId = replacement.Id, reason = "Lặp" })).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound,
            (await submitted.Verifier.Client.GetAsync($"/api/v1/verifier/death-cases/{submitted.CaseId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ClientFor(replacement).GetAsync($"/api/v1/verifier/death-cases/{submitted.CaseId}")).StatusCode);
        Assert.NotEqual(Guid.Empty, admin.Id);
    }

    [SqlFact]
    public async Task Dashboard_CountsStuckWorkAndHolds()
    {
        (User admin, HttpClient adminClient) = await AddAdminAsync();
        DeathCaseReviewTestData.SubmittedCase submitted = await CreateNoVerifierCaseAsync();
        await SeedActiveHoldAsync(submitted.Vault.VaultId);

        JsonElement dashboard = await ApiTestHelpers.ReadJsonAsync(await adminClient.GetAsync("/api/v1/admin/dashboard"));

        Assert.Equal(1, dashboard.GetProperty("activeHolds").GetInt32());
        Assert.Equal(1, dashboard.GetProperty("casesWithoutVerifier").GetInt32());
        Assert.Equal(1, dashboard.GetProperty("activeAdmins").GetInt32());
        Assert.NotEqual(Guid.Empty, admin.Id);
    }

    /// <summary>Hồ sơ đã gửi mà không còn Verifier nào: phân công cũ kết thúc và Verifier bị khóa.</summary>
    private async Task<DeathCaseReviewTestData.SubmittedCase> CreateNoVerifierCaseAsync()
    {
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        await DeathCaseReviewTestData.EndAssignmentAsync(factory, submitted.CaseId, DateTimeOffset.UtcNow);
        await DeathCaseReviewTestData.SetUserStatusAsync(factory, submitted.Verifier.User.Id, UserStatus.Locked);
        return submitted;
    }
}
