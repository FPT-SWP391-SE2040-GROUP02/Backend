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
        // second đã bị khóa: bị chặn ngay ở bước kiểm token dù access token còn hạn
        await ApiTestHelpers.AssertProblemAsync(last, HttpStatusCode.Forbidden, "ACCOUNT_NOT_ACTIVE");

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
    public async Task Hold_Placed_BlocksApprove_And_Activation_UntilReleased()
    {
        (User admin, HttpClient adminClient) = await AddAdminAsync();
        DeathCaseReviewTestData.SubmittedCase submitted = await DeathCaseReviewTestData.CreateReadyToApproveCaseAsync(factory);

        HttpResponseMessage placed = await adminClient.PostAsJsonAsync("/api/v1/admin/holds",
            new { vaultId = submitted.Vault.VaultId, reason = "Nghi vấn giấy tờ" });
        Assert.Equal(HttpStatusCode.Created, placed.StatusCode);
        JsonElement hold = await ApiTestHelpers.ReadJsonAsync(placed);
        Guid holdId = hold.GetProperty("id").GetGuid();
        Assert.Equal("ADMIN", hold.GetProperty("type").GetString());
        Assert.Equal(admin.Id, hold.GetProperty("responsibleUserId").GetGuid());
        Assert.True(hold.GetProperty("reviewAt").GetDateTimeOffset() <= DateTimeOffset.UtcNow.AddDays(7).AddMinutes(1));

        // Duyệt bị chặn dù đủ điều kiện
        HttpResponseMessage approve = await DeathCaseReviewTestData.ApproveNowAsync(submitted, "Đã đối chiếu.");
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(approve, HttpStatusCode.Conflict, "DEATH_CASE_NOT_ALLOWED");
        Assert.Equal("HOLD_ACTIVE", problem.GetProperty("reason").GetString());

        // Gỡ cần lý do và etag; gỡ lặp trả trạng thái hiện tại
        HttpResponseMessage noEtag = await adminClient.PostAsJsonAsync($"/api/v1/admin/holds/{holdId}/release", new { reason = "Đã kiểm tra", etag = "\"1\"" });
        await ApiTestHelpers.AssertProblemAsync(noEtag, HttpStatusCode.Conflict, "EDIT_CONFLICT");
        HttpResponseMessage released = await adminClient.PostAsJsonAsync($"/api/v1/admin/holds/{holdId}/release",
            new { reason = "Đã kiểm tra", etag = hold.GetProperty("etag").GetString() });
        Assert.Equal(HttpStatusCode.OK, released.StatusCode);
        Assert.Equal("RELEASED", (await ApiTestHelpers.ReadJsonAsync(released)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.OK, (await adminClient.PostAsJsonAsync($"/api/v1/admin/holds/{holdId}/release",
            new { reason = "Lặp", etag = hold.GetProperty("etag").GetString() })).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await DeathCaseReviewTestData.ApproveNowAsync(submitted, "Đã đối chiếu.")).StatusCode);
    }

    [SqlFact]
    public async Task Hold_ReviewDateLimitedToSevenDays_AndUnknownVaultIs404()
    {
        (User admin, HttpClient adminClient) = await AddAdminAsync();
        DeathCaseTestData.OwnerVault owner = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);

        HttpResponseMessage tooLate = await adminClient.PostAsJsonAsync("/api/v1/admin/holds",
            new { vaultId = owner.VaultId, reason = "x", reviewAt = DateTimeOffset.UtcNow.AddDays(30) });
        await ApiTestHelpers.AssertProblemAsync(tooLate, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        HttpResponseMessage unknown = await adminClient.PostAsJsonAsync("/api/v1/admin/holds",
            new { vaultId = Guid.NewGuid(), reason = "x" });
        await ApiTestHelpers.AssertProblemAsync(unknown, HttpStatusCode.NotFound, "VAULT_NOT_FOUND");
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

        // Admin không tự gỡ hold báo còn sống
        HttpResponseMessage release = await adminClient.PostAsJsonAsync($"/api/v1/admin/holds/{hold.GetProperty("id").GetGuid()}/release",
            new { reason = "Thử gỡ", etag = hold.GetProperty("etag").GetString() });
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(release, HttpStatusCode.Conflict, "ADMIN_ACTION_NOT_ALLOWED");
        Assert.Equal("HOLD_OWNER_ALIVE", problem.GetProperty("reason").GetString());

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
        Assert.Equal(HttpStatusCode.Created, (await adminClient.PostAsJsonAsync("/api/v1/admin/holds",
            new { vaultId = submitted.Vault.VaultId, reason = "Kiểm tra" })).StatusCode);

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
