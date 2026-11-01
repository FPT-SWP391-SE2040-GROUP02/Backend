using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Notifications;
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

public sealed class CapturingIncidentSender : IIncidentNoticeSender
{
    public List<IncidentNotice> Sent { get; } = new List<IncidentNotice>();

    public Task SendAsync(IncidentNotice notice, CancellationToken ct)
    {
        Sent.Add(notice);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Hold, sự cố, Admin (SRS §6.4, ASSIGN-04, AC-40) trên SQL Server thật: quyền, hold chặn phát hành,
/// sự cố luôn có người và hạn, phân công lại Verifier, nhật ký.
/// </summary>
public sealed class AdminOperationsTests : IAsyncLifetime
{
    private ApiFactory factory = null!;
    private WebApplicationFactory<Program> app = null!;
    private readonly CapturingIncidentSender incidentMail = new CapturingIncidentSender();

    public async Task InitializeAsync()
    {
        ApiFactory? created = ApiFactory.CreateForTempDatabase();
        if (created == null)
        {
            return;
        }
        factory = created;
        await factory.MigrateAsync();
        app = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IIncidentNoticeSender>();
                services.AddSingleton<IIncidentNoticeSender>(incidentMail);
            });
        });
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
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/admin/incidents")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/admin/holds")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/admin/users")).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.CreateClient().GetAsync("/api/v1/admin/dashboard")).StatusCode);
    }

    [SqlFact]
    public async Task Role02_AdminCannotCreateOrGrantExecutorAccount()
    {
        // Chuẩn bị
        (User _, HttpClient client) = await AddAdminAsync();
        User plain = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        string email = "executor-" + Guid.NewGuid().ToString("N") + "@legacyvault.test";

        // Hành động
        HttpResponseMessage create = await client.PostAsJsonAsync("/api/v1/admin/users",
            new { fullName = "Người Thực Hiện", email = email, password = "MatKhauThu123!", role = "EXECUTOR" });
        HttpResponseMessage grant = await client.PutAsJsonAsync($"/api/v1/admin/users/{plain.Id}/role",
            new { role = "EXECUTOR", reason = "Thử cấp vai trò" });

        // Kiểm tra: cả hai đều 400 theo trường role, không tạo tài khoản, không đổi vai trò, không ghi nhật ký
        JsonElement createProblem = await ApiTestHelpers.AssertProblemAsync(create, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.True(createProblem.GetProperty("errors").TryGetProperty("role", out JsonElement _));
        JsonElement grantProblem = await ApiTestHelpers.AssertProblemAsync(grant, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        Assert.True(grantProblem.GetProperty("errors").TryGetProperty("role", out JsonElement _));
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.False(await db.Users.AnyAsync(u => u.Email == email));
            User stored = await db.Users.AsNoTracking().SingleAsync(u => u.Id == plain.Id);
            Assert.Equal(UserRole.User, stored.Role);
            Assert.False(await db.Set<AuditLog>().AnyAsync(a => a.Action == AuditAction.UserRoleChanged || a.Action == AuditAction.AdminCreatedUser));
        });
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
    public async Task AuditLogs_AdminOnly_ListsVerifierRejection_NewestFirst_WithFilters()
    {
        // Chuẩn bị: Verifier từ chối một hồ sơ (ghi DeathCaseRejected) rồi Admin đổi vai trò một tài khoản (ghi UserRoleChanged)
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        Assert.Equal(HttpStatusCode.OK, (await DeathCaseReviewTestData.RejectNowAsync(submitted, "Giấy tờ không hợp lệ")).StatusCode);
        (User admin, HttpClient client) = await AddAdminAsync();
        User plain = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(
            $"/api/v1/admin/users/{plain.Id}/role", new { role = "VERIFIER", reason = "Nhân viên mới" })).StatusCode);

        // Không phải Admin → 404; chưa đăng nhập → 401
        Assert.Equal(HttpStatusCode.NotFound, (await ClientFor(submitted.Verifier.User).GetAsync("/api/v1/admin/audit-logs")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await app.CreateClient().GetAsync("/api/v1/admin/audit-logs")).StatusCode);

        // Mới nhất trước: dòng đổi vai trò (sau) đứng trước dòng từ chối hồ sơ
        JsonElement all = await ListAsync(client, "audit-logs?isAscending=false&pageSize=50");
        List<JsonElement> items = all.GetProperty("items").EnumerateArray().ToList();
        int roleIndex = items.FindIndex(i => i.GetProperty("action").GetString() == "USER_ROLE_CHANGED");
        int rejectIndex = items.FindIndex(i => i.GetProperty("action").GetString() == "DEATH_CASE_REJECTED");
        Assert.True(roleIndex >= 0 && rejectIndex >= 0 && roleIndex < rejectIndex);
        Assert.Equal(all.GetProperty("totalCount").GetInt32(), items.Count);

        // Dòng từ chối: người thực hiện, đối tượng, kho, mã lý do (không có lý do tự do)
        JsonElement reject = items[rejectIndex];
        Assert.Equal(submitted.Verifier.User.Id, reject.GetProperty("actorUserId").GetGuid());
        Assert.Equal(submitted.Verifier.User.Email, reject.GetProperty("actorEmail").GetString());
        Assert.Equal("VERIFIER", reject.GetProperty("actorRole").GetString());
        Assert.Equal("DEATH_CASE", reject.GetProperty("targetType").GetString());
        Assert.Equal(submitted.CaseId, reject.GetProperty("targetId").GetGuid());
        Assert.Equal(submitted.Vault.VaultId, reject.GetProperty("vaultId").GetGuid());
        Assert.Equal("SUCCEEDED", reject.GetProperty("outcome").GetString());
        Assert.Equal("VERIFIER_REJECTED", reject.GetProperty("reason").GetString());

        // Lọc theo loại thao tác và phân trang
        JsonElement onlyReject = await ListAsync(client, "audit-logs?action=DEATH_CASE_REJECTED");
        Assert.All(onlyReject.GetProperty("items").EnumerateArray(), i => Assert.Equal("DEATH_CASE_REJECTED", i.GetProperty("action").GetString()));
        Assert.Contains(onlyReject.GetProperty("items").EnumerateArray(), i => i.GetProperty("targetId").GetGuid() == submitted.CaseId);
        JsonElement paged = await ListAsync(client, "audit-logs?pageIndex=1&pageSize=1&isAscending=false");
        Assert.Single(paged.GetProperty("items").EnumerateArray());
        Assert.True(paged.GetProperty("hasNextPage").GetBoolean());

        // Lọc theo thời gian: khoảng tương lai không có dòng nào
        string future = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddDays(1).ToString("o"));
        JsonElement none = await ListAsync(client, "audit-logs?from=" + future);
        Assert.Equal(0, none.GetProperty("totalCount").GetInt32());

        // Tham số sai → 400
        await ApiTestHelpers.AssertProblemAsync(
            await client.GetAsync("/api/v1/admin/audit-logs?action=NOT_AN_ACTION"), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await ApiTestHelpers.AssertProblemAsync(
            await client.GetAsync("/api/v1/admin/audit-logs?sortBy=reason"), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
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
    public async Task OwnerAlive_CreatesHoldAndIncident_AdminCannotReleaseIt_ConclusionClosesBoth()
    {
        (User admin, HttpClient adminClient) = await AddAdminAsync();
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        HttpClient owner = ClientFor(submitted.Vault.Owner);

        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync($"/api/v1/vaults/{submitted.Vault.VaultId}/death-case/objection", null)).StatusCode);

        JsonElement holds = await ListAsync(adminClient, "holds?status=ACTIVE");
        JsonElement hold = Assert.Single(holds.GetProperty("items").EnumerateArray());
        Assert.Equal("OWNER_ALIVE", hold.GetProperty("type").GetString());
        Assert.Equal(submitted.Verifier.User.Id, hold.GetProperty("responsibleUserId").GetGuid());
        JsonElement incidents = await ListAsync(adminClient, "incidents?type=OWNER_ALIVE");
        JsonElement incident = Assert.Single(incidents.GetProperty("items").EnumerateArray());
        Assert.Equal(submitted.Verifier.User.Id, incident.GetProperty("assigneeUserId").GetGuid());

        // Admin không tự gỡ được hold báo còn sống: không còn API gỡ hold bằng tay
        HttpResponseMessage release = await adminClient.PostAsJsonAsync($"/api/v1/admin/holds/{hold.GetProperty("id").GetGuid()}/release",
            new { reason = "Thử gỡ", etag = hold.GetProperty("etag").GetString() });
        Assert.False(release.IsSuccessStatusCode);
        Assert.Single((await ListAsync(adminClient, "holds?status=ACTIVE")).GetProperty("items").EnumerateArray());

        // Verifier bác phản đối: hold gỡ, sự cố đóng
        HttpResponseMessage conclude = await submitted.Verifier.Client.PostAsJsonAsync(
            $"/api/v1/verifier/death-cases/{submitted.CaseId}/objection/conclude",
            new { conclusion = "OBJECTION_REJECTED", reason = "Không có căn cứ" });
        Assert.Equal(HttpStatusCode.OK, conclude.StatusCode);
        Assert.Empty((await ListAsync(adminClient, "holds?status=ACTIVE")).GetProperty("items").EnumerateArray());
        Assert.Empty((await ListAsync(adminClient, "incidents?type=OWNER_ALIVE&status=OPEN")).GetProperty("items").EnumerateArray());
        Assert.Equal("RESOLVED", Assert.Single((await ListAsync(adminClient, "incidents?type=OWNER_ALIVE")).GetProperty("items").EnumerateArray())
            .GetProperty("status").GetString());
        Assert.NotEqual(Guid.Empty, admin.Id);
    }

    [SqlFact]
    public async Task OwnerAlive_AdminReassignsVerifier_MovesCaseObjectionAndIncident_JobKeepsNewVerifier()
    {
        // Chuẩn bị: hồ sơ của Verifier A bị Owner báo còn sống, A để quá hạn kết luận
        (User admin, HttpClient adminClient) = await AddAdminAsync();
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateSubmittedCaseAsync(factory);
        HttpClient owner = ClientFor(submitted.Vault.Owner);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsync($"/api/v1/vaults/{submitted.Vault.VaultId}/death-case/objection", null)).StatusCode);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<DeathCaseObjection>().Where(o => o.CaseId == submitted.CaseId).ExecuteUpdateAsync(s => s
                .SetProperty(o => o.VerifierDueAt, DateTimeOffset.UtcNow.AddHours(-1)));
        });
        User verifierB = await DeathCaseTestData.AddVerifierAsync(factory);
        JsonElement incident = Assert.Single((await ListAsync(adminClient, "incidents?type=OWNER_ALIVE")).GetProperty("items").EnumerateArray());
        Guid incidentId = incident.GetProperty("id").GetGuid();
        string? etag = incident.GetProperty("etag").GetString();

        // Giao hay đóng tay sự cố OWNER_ALIVE bị chặn: sự cố đi theo hồ sơ
        HttpResponseMessage manualAssign = await adminClient.PostAsJsonAsync($"/api/v1/admin/incidents/{incidentId}/assign",
            new { assigneeUserId = verifierB.Id, reason = "thử", etag });
        Assert.Equal(HttpStatusCode.Conflict, manualAssign.StatusCode);
        Assert.Equal("INCIDENT_FOLLOWS_CASE", (await ApiTestHelpers.ReadJsonAsync(manualAssign)).GetProperty("reason").GetString());
        HttpResponseMessage manualResolve = await adminClient.PostAsJsonAsync($"/api/v1/admin/incidents/{incidentId}/resolve",
            new { note = "thử", etag });
        Assert.Equal(HttpStatusCode.Conflict, manualResolve.StatusCode);

        // Hành động: Admin giao lại người xét duyệt của hồ sơ cho B
        HttpResponseMessage reassign = await adminClient.PostAsJsonAsync(
            $"/api/v1/admin/death-cases/{submitted.CaseId}/reassign-verifier", new { verifierUserId = verifierB.Id, reason = "A nghỉ phép" });
        Assert.Equal(HttpStatusCode.NoContent, reassign.StatusCode);

        // Kiểm tra: hồ sơ, lời báo và sự cố cùng sang B; A mất quyền; hạn kết luận được tính lại
        Assert.Equal(HttpStatusCode.OK, (await ClientFor(verifierB).GetAsync($"/api/v1/verifier/death-cases/{submitted.CaseId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await submitted.Verifier.Client.GetAsync($"/api/v1/verifier/death-cases/{submitted.CaseId}")).StatusCode);
        Assert.Equal(verifierB.Id, Assert.Single((await ListAsync(adminClient, "incidents?type=OWNER_ALIVE")).GetProperty("items").EnumerateArray())
            .GetProperty("assigneeUserId").GetGuid());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            DeathCaseObjection objection = await db.Set<DeathCaseObjection>().SingleAsync(o => o.CaseId == submitted.CaseId);
            Assert.Equal(verifierB.Id, objection.VerifierUserId);
            Assert.True(objection.VerifierDueAt > DateTimeOffset.UtcNow);
        });

        // Job phân công chạy lại không chuyển hồ sơ khỏi B vì hạn cũ đã qua
        using (IServiceScope scope = app.Services.CreateScope())
        {
            DeathCaseAssignmentOutcome outcome = await scope.ServiceProvider.GetRequiredService<IDeathCaseAssignmentJob>()
                .ProcessCaseAsync(submitted.CaseId, CancellationToken.None);
            Assert.Equal(DeathCaseAssignmentOutcome.None, outcome);
        }
        Assert.Equal(HttpStatusCode.OK, (await ClientFor(verifierB).GetAsync($"/api/v1/verifier/death-cases/{submitted.CaseId}")).StatusCode);
        Assert.NotEqual(Guid.Empty, admin.Id);
    }

    [SqlFact]
    public async Task Assign04_NoVerifier_RaisesIncidentWithOwnerAndDue_ResolvedWhenAssigned()
    {
        (User admin, HttpClient adminClient) = await AddAdminAsync();
        DeathCaseReviewTestData.SubmittedCase submitted = await CreateNoVerifierCaseAsync();

        // Job phân công không có ai để giao → sự cố NO_VERIFIER giao Admin, có hạn
        using (IServiceScope scope = app.Services.CreateScope())
        {
            IDeathCaseAssignmentJob job = scope.ServiceProvider.GetRequiredService<IDeathCaseAssignmentJob>();
            DeathCaseAssignmentOutcome outcome = await job.ProcessCaseAsync(submitted.CaseId, CancellationToken.None);
            Assert.Equal(DeathCaseAssignmentOutcome.NoCandidate, outcome);
            await scope.ServiceProvider.GetRequiredService<IOperationsRecorder>()
                .RecordAssignmentOutcomeAsync(submitted.CaseId, outcome, CancellationToken.None);
            // Chạy lặp không tạo sự cố thứ hai
            await scope.ServiceProvider.GetRequiredService<IOperationsRecorder>()
                .RecordAssignmentOutcomeAsync(submitted.CaseId, outcome, CancellationToken.None);
        }
        JsonElement incident = Assert.Single((await ListAsync(adminClient, "incidents?type=NO_VERIFIER")).GetProperty("items").EnumerateArray());
        Assert.Equal(admin.Id, incident.GetProperty("assigneeUserId").GetGuid());
        Assert.Equal(submitted.CaseId, incident.GetProperty("targetId").GetGuid());
        Assert.True(incident.GetProperty("dueAt").GetDateTimeOffset() > DateTimeOffset.UtcNow.AddHours(47));

        // Admin giao Verifier cho hồ sơ (kèm lý do) → sự cố tự đóng, ghi nhật ký
        User verifier = await DeathCaseTestData.AddVerifierAsync(factory);
        HttpResponseMessage reassign = await adminClient.PostAsJsonAsync(
            $"/api/v1/admin/death-cases/{submitted.CaseId}/reassign-verifier", new { verifierUserId = verifier.Id, reason = "Điều phối thủ công" });
        Assert.Equal(HttpStatusCode.NoContent, reassign.StatusCode);
        Assert.Equal("RESOLVED", Assert.Single((await ListAsync(adminClient, "incidents?type=NO_VERIFIER")).GetProperty("items").EnumerateArray())
            .GetProperty("status").GetString());
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
    public async Task Incident_AssignNoteReviewResolve_WithRules()
    {
        (User admin, HttpClient adminClient) = await AddAdminAsync();
        DeathCaseReviewTestData.SubmittedCase submitted = await CreateNoVerifierCaseAsync();
        using (IServiceScope scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IOperationsRecorder>()
                .RecordAssignmentOutcomeAsync(submitted.CaseId, DeathCaseAssignmentOutcome.NoCandidate, CancellationToken.None);
        }
        JsonElement incident = Assert.Single((await ListAsync(adminClient, "incidents")).GetProperty("items").EnumerateArray());
        Guid id = incident.GetProperty("id").GetGuid();
        string etag = incident.GetProperty("etag").GetString()!;
        User verifier = await DeathCaseTestData.AddVerifierAsync(factory);

        // Không giao cho chủ kho
        HttpResponseMessage toOwner = await adminClient.PostAsJsonAsync($"/api/v1/admin/incidents/{id}/assign",
            new { assigneeUserId = submitted.Vault.Owner.Id, reason = "thử", etag });
        Assert.Equal(HttpStatusCode.BadRequest, toOwner.StatusCode);
        // Etag cũ bị từ chối
        HttpResponseMessage stale = await adminClient.PostAsJsonAsync($"/api/v1/admin/incidents/{id}/assign",
            new { assigneeUserId = verifier.Id, reason = "thử", etag = "\"1\"" });
        await ApiTestHelpers.AssertProblemAsync(stale, HttpStatusCode.Conflict, "EDIT_CONFLICT");

        HttpResponseMessage assigned = await adminClient.PostAsJsonAsync($"/api/v1/admin/incidents/{id}/assign",
            new { assigneeUserId = verifier.Id, reason = "Verifier có thời gian", etag });
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        JsonElement detail = await ApiTestHelpers.ReadJsonAsync(assigned);
        Assert.Equal("IN_PROGRESS", detail.GetProperty("status").GetString());
        Assert.Equal(verifier.Id, detail.GetProperty("assigneeUserId").GetGuid());

        Assert.Equal(HttpStatusCode.OK, (await adminClient.PostAsJsonAsync($"/api/v1/admin/incidents/{id}/notes", new { note = "Đã gọi điện" })).StatusCode);

        HttpResponseMessage farReview = await adminClient.PostAsJsonAsync($"/api/v1/admin/incidents/{id}/review",
            new { nextReviewAt = DateTimeOffset.UtcNow.AddDays(30), note = "xa", etag = detail.GetProperty("etag").GetString() });
        await ApiTestHelpers.AssertProblemAsync(farReview, HttpStatusCode.BadRequest, "VALIDATION_ERROR");

        HttpResponseMessage resolved = await adminClient.PostAsJsonAsync($"/api/v1/admin/incidents/{id}/resolve",
            new { note = "Đã xử lý", etag = detail.GetProperty("etag").GetString() });
        Assert.Equal(HttpStatusCode.OK, resolved.StatusCode);
        JsonElement done = await ApiTestHelpers.ReadJsonAsync(resolved);
        Assert.Equal("RESOLVED", done.GetProperty("status").GetString());
        // Lịch sử đủ: tạo, giao, ghi chú, đóng
        List<string> kinds = new List<string>();
        foreach (JsonElement e in done.GetProperty("events").EnumerateArray())
        {
            kinds.Add(e.GetProperty("kind").GetString()!);
        }
        Assert.Equal(new List<string> { "RAISED", "ASSIGNED", "NOTE", "RESOLVED" }, kinds);
        // Đóng lặp không lỗi
        Assert.Equal(HttpStatusCode.OK, (await adminClient.PostAsJsonAsync($"/api/v1/admin/incidents/{id}/resolve",
            new { note = "Lặp", etag = done.GetProperty("etag").GetString() })).StatusCode);
        Assert.NotEqual(Guid.Empty, admin.Id);
    }

    [SqlFact]
    public async Task IncidentJob_Reminds48h_Escalates72h_AssignsOrphans_NeverAutoCloses()
    {
        (User admin, HttpClient adminClient) = await AddAdminAsync();
        DeathCaseReviewTestData.SubmittedCase submitted = await CreateNoVerifierCaseAsync();
        using (IServiceScope scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IOperationsRecorder>()
                .RecordAssignmentOutcomeAsync(submitted.CaseId, DeathCaseAssignmentOutcome.NoCandidate, CancellationToken.None);
        }
        Guid id = Assert.Single((await ListAsync(adminClient, "incidents")).GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid();

        // Chưa tới hạn: job không làm gì
        Assert.Null(await ProcessIncidentAsync(id));

        // Qua 48 giờ: nhắc đúng một lần
        await MoveIncidentBackAsync(id, TimeSpan.FromHours(49));
        Assert.Equal("REMINDED", await ProcessIncidentAsync(id));
        Assert.Null(await ProcessIncidentAsync(id));
        Assert.Single(incidentMail.Sent, n => n.Kind == IncidentNoticeKind.Reminder);

        // Qua 72 giờ: báo Admin một lần; sự cố vẫn mở (không tự đóng)
        await MoveIncidentBackAsync(id, TimeSpan.FromHours(30));
        Assert.Equal("ESCALATED", await ProcessIncidentAsync(id));
        Assert.Null(await ProcessIncidentAsync(id));
        Assert.Single(incidentMail.Sent, n => n.Kind == IncidentNoticeKind.Escalation);
        JsonElement detail = await ApiTestHelpers.ReadJsonAsync(await adminClient.GetAsync($"/api/v1/admin/incidents/{id}"));
        Assert.NotEqual("RESOLVED", detail.GetProperty("status").GetString());
        Assert.True(detail.GetProperty("overdue").GetBoolean());

        // Sự cố mồ côi (chưa có ai): job giao cho Admin
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<Incident>().Where(i => i.Id == id).ExecuteUpdateAsync(s => s
                .SetProperty(i => i.AssigneeUserId, (Guid?)null));
        });
        Assert.Equal("ASSIGNED", await ProcessIncidentAsync(id));
        Assert.Equal(admin.Id, (await ApiTestHelpers.ReadJsonAsync(await adminClient.GetAsync($"/api/v1/admin/incidents/{id}")))
            .GetProperty("assigneeUserId").GetGuid());
    }

    [SqlFact]
    public async Task Dms05_CheckInOverdue_AssignsExecutor_NoExecutorMeansStuckIncident()
    {
        (User admin, HttpClient adminClient) = await AddAdminAsync();

        // Có Executor: sự cố giao Executor chính, hạn có sẵn
        DeathCaseTestData.OwnerVault withExecutor = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, withExecutor.VaultId);
        Guid periodA = await MakePeriodAwaitingAsync(withExecutor);
        await RecordPeriodAsync(periodA);

        // Không có Executor: "bị kẹt" do Admin phụ trách, có ngày xem lại
        DeathCaseTestData.OwnerVault noExecutor = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        Guid periodB = await MakePeriodAwaitingAsync(noExecutor);
        await RecordPeriodAsync(periodB);
        await RecordPeriodAsync(periodB);

        JsonElement overdue = await ListAsync(adminClient, "incidents?type=CHECK_IN_OVERDUE");
        JsonElement first = Assert.Single(overdue.GetProperty("items").EnumerateArray());
        Assert.Equal(executor.User.Id, first.GetProperty("assigneeUserId").GetGuid());
        Assert.Equal(periodA, first.GetProperty("targetId").GetGuid());
        JsonElement stuck = Assert.Single((await ListAsync(adminClient, "incidents?type=NO_EXECUTOR")).GetProperty("items").EnumerateArray());
        Assert.Equal(admin.Id, stuck.GetProperty("assigneeUserId").GetGuid());
        Assert.True(stuck.GetProperty("nextReviewAt").GetDateTimeOffset() <= DateTimeOffset.UtcNow.AddDays(7).AddMinutes(1));

        // DMS-05: Executor được báo ngay khi kế hoạch sang AWAITING_CHECK, đúng một lần dù job ghi nhận lại kỳ nhiều lần;
        // thư dẫn về trang Executor của kho. Kho không có Executor thì không có thư Executor nào.
        await RecordPeriodAsync(periodA);
        IncidentNotice executorNotice = Assert.Single(incidentMail.Sent, n => n.Area == IncidentRecipientArea.Executor);
        Assert.Equal(IncidentNoticeKind.Assigned, executorNotice.Kind);
        Assert.Equal(executor.User.Email, executorNotice.ToEmail);
        Assert.Equal(withExecutor.VaultId, executorNotice.VaultId);
        Assert.Equal("CHECK_IN_OVERDUE", executorNotice.IncidentType);
        Assert.DoesNotContain(incidentMail.Sent, n => n.VaultId == noExecutor.VaultId);

        // Thư nhắc sau 48 giờ cho Executor cũng dẫn về trang Executor, không phải /admin.
        Guid overdueId = first.GetProperty("id").GetGuid();
        await MoveIncidentBackAsync(overdueId, TimeSpan.FromHours(49));
        Assert.Equal("REMINDED", await ProcessIncidentAsync(overdueId));
        IncidentNotice reminder = Assert.Single(incidentMail.Sent, n => n.Kind == IncidentNoticeKind.Reminder);
        Assert.Equal(IncidentRecipientArea.Executor, reminder.Area);
        Assert.Equal(withExecutor.VaultId, reminder.VaultId);

        // Owner điểm danh → kỳ đóng → job sự cố đóng sự cố
        HttpResponseMessage checkIn = await ClientFor(withExecutor.Owner).PostAsync("/api/v1/check-ins", null);
        Assert.Equal(HttpStatusCode.OK, checkIn.StatusCode);
        Assert.Equal("RESOLVED", await ProcessIncidentAsync(first.GetProperty("id").GetGuid()));
    }

    [SqlFact]
    public async Task Dashboard_CountsStuckWorkAndHolds()
    {
        (User admin, HttpClient adminClient) = await AddAdminAsync();
        DeathCaseReviewTestData.SubmittedCase submitted = await CreateNoVerifierCaseAsync();
        using (IServiceScope scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IOperationsRecorder>()
                .RecordAssignmentOutcomeAsync(submitted.CaseId, DeathCaseAssignmentOutcome.NoCandidate, CancellationToken.None);
        }
        await SeedActiveHoldAsync(submitted.Vault.VaultId);

        JsonElement dashboard = await ApiTestHelpers.ReadJsonAsync(await adminClient.GetAsync("/api/v1/admin/dashboard"));

        Assert.Equal(1, dashboard.GetProperty("openIncidents").GetInt32());
        Assert.Equal(1, dashboard.GetProperty("activeHolds").GetInt32());
        Assert.Equal(1, dashboard.GetProperty("casesWithoutVerifier").GetInt32());
        Assert.Equal(1, dashboard.GetProperty("activeAdmins").GetInt32());
        Assert.Equal(0, dashboard.GetProperty("unassignedIncidents").GetInt32());
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

    private async Task<Guid> MakePeriodAwaitingAsync(DeathCaseTestData.OwnerVault vault)
    {
        await DeathCaseTestData.AddCheckInPeriodAsync(factory, vault.PlanId, 1, DateTimeOffset.UtcNow.AddDays(-60), null);
        Guid periodId = Guid.Empty;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            periodId = await db.CheckInPeriods.Where(p => p.PlanId == vault.PlanId).Select(p => p.Id).SingleAsync();
            await db.CheckInPeriods.Where(p => p.Id == periodId).ExecuteUpdateAsync(s => s
                .SetProperty(p => p.AwaitingCheckAt, DateTimeOffset.UtcNow.AddDays(-1)));
        });
        await ApiTestHelpers.SetPlanStatusAsync(factory, vault.VaultId, PlanStatus.AwaitingCheck);
        return periodId;
    }

    private async Task RecordPeriodAsync(Guid periodId)
    {
        using (IServiceScope scope = app.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IOperationsRecorder>().RecordCheckInPeriodAsync(periodId, CancellationToken.None);
        }
    }

    private async Task<string?> ProcessIncidentAsync(Guid id)
    {
        using (IServiceScope scope = app.Services.CreateScope())
        {
            return await scope.ServiceProvider.GetRequiredService<IIncidentJob>().ProcessAsync(id, CancellationToken.None);
        }
    }

    private Task MoveIncidentBackAsync(Guid id, TimeSpan by)
    {
        return ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Incident incident = await db.Set<Incident>().SingleAsync(i => i.Id == id);
            incident.CreatedAt = incident.CreatedAt - by;
            incident.DueAt = incident.DueAt - by;
            await db.SaveChangesAsync();
        });
    }
}
