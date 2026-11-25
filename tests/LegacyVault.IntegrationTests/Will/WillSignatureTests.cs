using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Dtos;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LegacyVault.IntegrationTests.Will;

/// <summary>
/// Di chúc số: kích hoạt tạo phiên bản 1, ký lại bằng PIN tạo phiên bản kế tiếp, số liệu thay đổi chưa ký
/// (bộ kiểm PIN thật, SQL Server thật).
/// </summary>
public sealed class WillSignatureTests : IAsyncLifetime
{
    private const string GoodPin = "482913";
    private const string WrongPin = "999000";
    private const string ExecutorEmail = "ky-lai-di-chuc@example.test";
    private const string SecondCitizenId = "000000002026";

    private ApiFactory factory = null!;
    private readonly Mail mail = new Mail();

    public async Task InitializeAsync()
    {
        ApiFactory? created = ApiFactory.CreateForTempDatabase();
        if (created == null)
        {
            return;
        }
        factory = created;
        factory.EnforceVaultPin = true;
        factory.ConfigureAdditionalServices = services =>
        {
            services.RemoveAll<IExecutorInvitationSender>();
            services.AddSingleton<IExecutorInvitationSender>(mail);
        };
        await factory.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (factory == null)
        {
            return;
        }
        await factory.DropAsync();
        await factory.DisposeAsync();
    }

    private sealed class Mail : IExecutorInvitationSender
    {
        public string Token { get; private set; } = string.Empty;

        public Task<bool> SendAsync(string email, string token, DateTimeOffset expiresAt, CancellationToken ct)
        {
            Token = token;
            return Task.FromResult(true);
        }
    }

    private sealed class PreparedPlan
    {
        public HttpClient Owner { get; set; } = null!;
        public HttpClient Executor { get; set; } = null!;
        public Guid VaultId { get; set; }
        public Guid FirstAssetId { get; set; }
        public Guid SecondAssetId { get; set; }
        public Guid ThirdAssetId { get; set; }
        public Guid FirstBeneficiaryId { get; set; }
        public Guid SecondBeneficiaryId { get; set; }
    }

    private static Task<HttpResponseMessage> PostWithPinAsync(HttpClient client, string url, object body, string? pin)
    {
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, url);
        if (pin != null)
        {
            request.Headers.Add("X-Vault-Pin", pin);
        }
        request.Content = JsonContent.Create(body);
        return client.SendAsync(request);
    }

    private static void UsePinForEdits(HttpClient owner)
    {
        owner.DefaultRequestHeaders.Remove("X-Vault-Pin");
        owner.DefaultRequestHeaders.Add("X-Vault-Pin", GoodPin);
    }

    private static void DropPinForEdits(HttpClient owner)
    {
        owner.DefaultRequestHeaders.Remove("X-Vault-Pin");
    }

    /// <summary>
    /// Kế hoạch đủ điều kiện kích hoạt. richConfig = true: tài sản 1 và 2 gán chung cho người nhận 1 và 2, tài sản 3 gán cho người nhận 1 (5 cặp).
    /// </summary>
    private async Task<PreparedPlan> PrepareReadyPlanAsync(bool richConfig)
    {
        (HttpClient owner, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        HttpResponseMessage pinSet = await owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/pin", new { pin = GoodPin, confirmPin = GoodPin });
        Assert.Equal(HttpStatusCode.OK, pinSet.StatusCode);
        UsePinForEdits(owner);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Vault vault = await db.Vaults.SingleAsync(v => v.Id == vaultId);
            await db.VaultEntitlements.Where(e => e.VaultId == vaultId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.ServiceExpiresAt, DateTimeOffset.UtcNow.AddDays(365)));
            db.EkycVerifications.Add(new EkycVerification
            {
                UserId = vault.OwnerId, Status = EkycStatus.Passed, CreatedAt = DateTimeOffset.UtcNow,
                CompletedAt = DateTimeOffset.UtcNow, SpoofCheckPassed = true,
            });
            await db.SaveChangesAsync();
        });

        PreparedPlan plan = new PreparedPlan();
        plan.Owner = owner;
        plan.VaultId = vaultId;
        plan.FirstAssetId = await ApiTestHelpers.CreateTextAssetAsync(owner, vaultId, "Ghi chú một");
        plan.FirstBeneficiaryId = (await ApiTestHelpers.CreateBeneficiaryAsync(owner, vaultId, "000000001015", "Người nhận")).GetProperty("id").GetGuid();
        await ApiTestHelpers.AssignAssetOkAsync(owner, vaultId, plan.FirstAssetId, plan.FirstBeneficiaryId);

        if (richConfig)
        {
            plan.SecondAssetId = await ApiTestHelpers.CreateTextAssetAsync(owner, vaultId, "Ghi chú hai");
            plan.SecondBeneficiaryId = (await ApiTestHelpers.CreateBeneficiaryAsync(owner, vaultId, SecondCitizenId, "Người nhận hai")).GetProperty("id").GetGuid();
            plan.ThirdAssetId = await ApiTestHelpers.CreateTextAssetAsync(owner, vaultId, "Ghi chú ba");
            await ApiTestHelpers.AssignAssetOkAsync(owner, vaultId, plan.FirstAssetId, plan.FirstBeneficiaryId, plan.SecondBeneficiaryId);
            await ApiTestHelpers.AssignAssetOkAsync(owner, vaultId, plan.SecondAssetId, plan.FirstBeneficiaryId, plan.SecondBeneficiaryId);
            await ApiTestHelpers.AssignAssetOkAsync(owner, vaultId, plan.ThirdAssetId, plan.FirstBeneficiaryId);
        }

        HttpClient executor = await ApiTestHelpers.CreateClientForNewUserAsync(factory, true, ExecutorEmail);
        await ApiTestHelpers.EnsureSystemExecutorAsync(factory, ExecutorEmail);
        HttpResponseMessage invite = await owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/executors", new { email = ExecutorEmail });
        Assert.Equal(HttpStatusCode.Created, invite.StatusCode);
        plan.Executor = executor;
        DropPinForEdits(owner);
        return plan;
    }

    private static async Task<JsonElement> GetSetupAsync(PreparedPlan plan)
    {
        HttpResponseMessage response = await plan.Owner.GetAsync($"/api/v1/vaults/{plan.VaultId}/plan-setup");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await ApiTestHelpers.ReadJsonAsync(response);
    }

    private async Task ActivateAsync(PreparedPlan plan)
    {
        JsonElement setup = await GetSetupAsync(plan);
        ActivatePlanRequest request = new ActivatePlanRequest
        {
            ConfigVersion = setup.GetProperty("configVersion").GetInt32(),
            PolicyVersion = setup.GetProperty("policyVersion").GetString()!,
            AcknowledgePolicy = true,
        };
        HttpResponseMessage response = await PostWithPinAsync(plan.Owner, $"/api/v1/vaults/{plan.VaultId}/activate", request, GoodPin);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        HttpResponseMessage accept = await plan.Executor.PostAsJsonAsync("/api/v1/executor-invitations/respond",
            new { token = mail.Token, accept = true, acknowledgeScope = true, scopeVersion = PlanSetupService.ScopeVersion, password = ApiTestHelpers.ExecutorPassword, willVersionNo = 1 });
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
    }

    private static async Task<HttpResponseMessage> SignAsync(PreparedPlan plan, int configVersion, string? pin)
    {
        return await PostWithPinAsync(plan.Owner, $"/api/v1/vaults/{plan.VaultId}/will/sign", new { configVersion }, pin);
    }

    private async Task<List<WillVersion>> ReadVersionsAsync(Guid vaultId)
    {
        List<WillVersion> versions = new List<WillVersion>();
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            versions = await db.Set<WillVersion>().Where(w => w.VaultId == vaultId).OrderBy(w => w.VersionNo).ToListAsync();
        });
        return versions;
    }

    private async Task<int> CountAuditAsync(Guid vaultId, AuditAction action)
    {
        int count = 0;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            count = await db.Set<AuditLog>().CountAsync(l => l.VaultId == vaultId && l.Action == action);
        });
        return count;
    }

    private static void AssertCounts(JsonElement changes, int addedAssignments, int removedAssignments, int affectedRecipientCount)
    {
        Assert.Equal(addedAssignments, changes.GetProperty("addedAssignments").GetInt32());
        Assert.Equal(removedAssignments, changes.GetProperty("removedAssignments").GetInt32());
        Assert.Equal(affectedRecipientCount, changes.GetProperty("affectedRecipientCount").GetInt32());
    }

    /// <summary>
    /// Thêm một tài sản mới gán chung cho người nhận 1 và một người nhận mới, rồi một tài sản nữa gán cho người nhận 1
    /// (3 cặp mới, đều chưa ký).
    /// </summary>
    private async Task AddUnsignedChangesAsync(PreparedPlan plan)
    {
        UsePinForEdits(plan.Owner);
        Guid newAssetId = await ApiTestHelpers.CreateTextAssetAsync(plan.Owner, plan.VaultId, "Ghi chú mới");
        Guid newBeneficiaryId = (await ApiTestHelpers.CreateBeneficiaryAsync(plan.Owner, plan.VaultId, SecondCitizenId, "Người nhận mới")).GetProperty("id").GetGuid();
        await ApiTestHelpers.AssignAssetOkAsync(plan.Owner, plan.VaultId, newAssetId, plan.FirstBeneficiaryId, newBeneficiaryId);
        Guid anotherAssetId = await ApiTestHelpers.CreateTextAssetAsync(plan.Owner, plan.VaultId, "Ghi chú thêm");
        await ApiTestHelpers.AssignAssetOkAsync(plan.Owner, plan.VaultId, anotherAssetId, plan.FirstBeneficiaryId);
        DropPinForEdits(plan.Owner);
    }

    [SqlFact]
    public async Task Activate_CreatesWillVersionOneWithIdsOnly()
    {
        PreparedPlan plan = await PrepareReadyPlanAsync(false);

        await ActivateAsync(plan);

        List<WillVersion> versions = await ReadVersionsAsync(plan.VaultId);
        WillVersion version = Assert.Single(versions);
        Assert.Equal(1, version.VersionNo);
        int configVersion = 0;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            configVersion = (await db.HandoverPlans.SingleAsync(p => p.VaultId == plan.VaultId)).ConfigVersion;
        });
        Assert.Equal(configVersion, version.ConfigVersion);
        WillSnapshot snapshot = WillVersionRules.FromJson(version.SnapshotJson);
        Assert.Single(snapshot.Assignments);
        Assert.Equal(plan.FirstBeneficiaryId, snapshot.Assignments[0].BeneficiaryId);
        Assert.Equal(plan.FirstAssetId, snapshot.Assignments[0].AssetId);
        // Chỉ có id: không có tên tài sản, tên người nhận.
        Assert.DoesNotContain("Ghi chú", version.SnapshotJson);
        Assert.DoesNotContain("Người nhận", version.SnapshotJson);

        JsonElement setup = await GetSetupAsync(plan);
        Assert.Equal(1, setup.GetProperty("willVersion").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, setup.GetProperty("willSignedAt").ValueKind);
        Assert.False(setup.GetProperty("hasUnsignedChanges").GetBoolean());
        AssertCounts(setup.GetProperty("unsignedChanges"), 0, 0, 0);
    }

    [SqlFact]
    public async Task PlanSetup_BeforeActivation_HasNoWillVersion()
    {
        PreparedPlan plan = await PrepareReadyPlanAsync(false);

        JsonElement setup = await GetSetupAsync(plan);

        Assert.Equal(JsonValueKind.Null, setup.GetProperty("willVersion").ValueKind);
        Assert.Equal(JsonValueKind.Null, setup.GetProperty("willSignedAt").ValueKind);
        Assert.False(setup.GetProperty("hasUnsignedChanges").GetBoolean());
    }

    [SqlFact]
    public async Task Sign_WithoutPin_IsRejected()
    {
        PreparedPlan plan = await PrepareReadyPlanAsync(false);
        await ActivateAsync(plan);
        await AddUnsignedChangesAsync(plan);
        JsonElement setup = await GetSetupAsync(plan);

        HttpResponseMessage response = await SignAsync(plan, setup.GetProperty("configVersion").GetInt32(), null);

        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Forbidden, "VAULT_PIN_REQUIRED");
        Assert.Single(await ReadVersionsAsync(plan.VaultId));
    }

    [SqlFact]
    public async Task Sign_WithWrongPin_IsRejected()
    {
        PreparedPlan plan = await PrepareReadyPlanAsync(false);
        await ActivateAsync(plan);
        await AddUnsignedChangesAsync(plan);
        JsonElement setup = await GetSetupAsync(plan);

        HttpResponseMessage response = await SignAsync(plan, setup.GetProperty("configVersion").GetInt32(), WrongPin);

        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Forbidden, "INVALID_VAULT_PIN");
        Assert.Single(await ReadVersionsAsync(plan.VaultId));
        Assert.Equal(0, await CountAuditAsync(plan.VaultId, AuditAction.WillResigned));
    }

    [SqlFact]
    public async Task Sign_WithoutChanges_ReturnsStateAndCreatesNothing()
    {
        PreparedPlan plan = await PrepareReadyPlanAsync(false);
        await ActivateAsync(plan);
        JsonElement setup = await GetSetupAsync(plan);

        HttpResponseMessage response = await SignAsync(plan, setup.GetProperty("configVersion").GetInt32(), GoodPin);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal(1, body.GetProperty("willVersion").GetInt32());
        Assert.False(body.GetProperty("hasUnsignedChanges").GetBoolean());
        Assert.Single(await ReadVersionsAsync(plan.VaultId));
        Assert.Equal(0, await CountAuditAsync(plan.VaultId, AuditAction.WillResigned));
    }

    [SqlFact]
    public async Task Sign_AfterChanges_CreatesVersionTwoAndAuditsIt()
    {
        PreparedPlan plan = await PrepareReadyPlanAsync(false);
        await ActivateAsync(plan);
        await AddUnsignedChangesAsync(plan);
        JsonElement before = await GetSetupAsync(plan);
        Assert.True(before.GetProperty("hasUnsignedChanges").GetBoolean());
        AssertCounts(before.GetProperty("unsignedChanges"), 3, 0, 2);
        int configVersion = before.GetProperty("configVersion").GetInt32();

        HttpResponseMessage response = await SignAsync(plan, configVersion, GoodPin);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal(2, body.GetProperty("willVersion").GetInt32());
        Assert.False(body.GetProperty("hasUnsignedChanges").GetBoolean());
        AssertCounts(body.GetProperty("unsignedChanges"), 0, 0, 0);

        List<WillVersion> versions = await ReadVersionsAsync(plan.VaultId);
        Assert.Equal(2, versions.Count);
        Assert.Equal(2, versions[1].VersionNo);
        Assert.Equal(configVersion, versions[1].ConfigVersion);
        WillSnapshot snapshot = WillVersionRules.FromJson(versions[1].SnapshotJson);
        Assert.Equal(4, snapshot.Assignments.Count);

        List<AuditLog> logs = new List<AuditLog>();
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            logs = await db.Set<AuditLog>().Where(l => l.VaultId == plan.VaultId && l.Action == AuditAction.WillResigned).ToListAsync();
        });
        AuditLog log = Assert.Single(logs);
        Assert.Equal(2, log.TargetVersion);
        Assert.Equal(AuditOutcome.Succeeded, log.Outcome);
        Assert.Equal(AuditActorRole.Owner, log.ActorRole);

        // Ký lần nữa ngay sau đó: không còn gì mới nên không tạo phiên bản 3.
        HttpResponseMessage again = await SignAsync(plan, configVersion, GoodPin);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal(2, (await ReadVersionsAsync(plan.VaultId)).Count);
    }

    [SqlFact]
    public async Task Sign_WithStaleConfigVersion_IsEditConflict()
    {
        PreparedPlan plan = await PrepareReadyPlanAsync(false);
        await ActivateAsync(plan);
        JsonElement setup = await GetSetupAsync(plan);
        int oldConfigVersion = setup.GetProperty("configVersion").GetInt32();
        await AddUnsignedChangesAsync(plan);

        HttpResponseMessage response = await SignAsync(plan, oldConfigVersion, GoodPin);

        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "EDIT_CONFLICT");
        Assert.Single(await ReadVersionsAsync(plan.VaultId));
    }

    [SqlFact]
    public async Task Sign_WhenPlanIsStillDraft_IsNotAllowed()
    {
        PreparedPlan plan = await PrepareReadyPlanAsync(false);
        JsonElement setup = await GetSetupAsync(plan);

        HttpResponseMessage response = await SignAsync(plan, setup.GetProperty("configVersion").GetInt32(), GoodPin);

        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "WILL_SIGN_NOT_ALLOWED");
        Assert.Empty(await ReadVersionsAsync(plan.VaultId));
    }

    [SqlFact]
    public async Task Sign_ByStrangerExecutorOrAdmin_IsNotFound()
    {
        PreparedPlan plan = await PrepareReadyPlanAsync(false);
        await ActivateAsync(plan);
        await AddUnsignedChangesAsync(plan);
        JsonElement setup = await GetSetupAsync(plan);
        int configVersion = setup.GetProperty("configVersion").GetInt32();

        HttpClient stranger = await ApiTestHelpers.CreateClientForNewUserAsync(factory, true);
        User admin = await VaultTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        HttpClient adminClient = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(adminClient, factory.Services, admin);
        User executorUser = await VaultTestData.AddUserAsync(factory, UserRole.Executor, UserStatus.Active);
        HttpClient executorClient = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(executorClient, factory.Services, executorUser);

        foreach (HttpClient other in new List<HttpClient> { stranger, adminClient, executorClient })
        {
            HttpResponseMessage response = await PostWithPinAsync(other, $"/api/v1/vaults/{plan.VaultId}/will/sign", new { configVersion }, GoodPin);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        Assert.Single(await ReadVersionsAsync(plan.VaultId));
    }

    [SqlFact]
    public async Task PlanSetup_CountsRemovedAssignments()
    {
        PreparedPlan plan = await PrepareReadyPlanAsync(true);
        await ActivateAsync(plan);
        UsePinForEdits(plan.Owner);

        // Người nhận 2 mất cả hai tài sản; tài sản 3 không còn người nhận nào (người nhận 1 vẫn còn tài sản 1 và 2).
        HttpResponseMessage removeFirst = await ApiTestHelpers.UnassignAssetAsync(
            plan.Owner, plan.VaultId, plan.FirstAssetId, plan.SecondBeneficiaryId);
        HttpResponseMessage removeSecond = await ApiTestHelpers.UnassignAssetAsync(
            plan.Owner, plan.VaultId, plan.SecondAssetId, plan.SecondBeneficiaryId);
        HttpResponseMessage clearThird = await ApiTestHelpers.AssignAssetAsync(plan.Owner, plan.VaultId, plan.ThirdAssetId);
        DropPinForEdits(plan.Owner);

        Assert.Equal(HttpStatusCode.OK, removeFirst.StatusCode);
        Assert.Equal(HttpStatusCode.OK, removeSecond.StatusCode);
        Assert.Equal(HttpStatusCode.OK, clearThird.StatusCode);
        JsonElement setup = await GetSetupAsync(plan);
        Assert.True(setup.GetProperty("hasUnsignedChanges").GetBoolean());
        AssertCounts(setup.GetProperty("unsignedChanges"), 0, 3, 2);

        // Ký lại: bản mới chỉ còn phần còn lại (tài sản 1 và 2 cho người nhận 1).
        HttpResponseMessage signed = await SignAsync(plan, setup.GetProperty("configVersion").GetInt32(), GoodPin);
        Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
        List<WillVersion> versions = await ReadVersionsAsync(plan.VaultId);
        Assert.Equal(2, versions.Count);
        WillSnapshot latest = WillVersionRules.FromJson(versions[1].SnapshotJson);
        Assert.Equal(2, latest.Assignments.Count);
    }

    [SqlFact]
    public async Task PlanSetup_CountsAssignmentMovedToAnotherAsset()
    {
        PreparedPlan plan = await PrepareReadyPlanAsync(true);
        await ActivateAsync(plan);
        UsePinForEdits(plan.Owner);

        // Người nhận 2 không còn nhận tài sản 1 mà nhận tài sản 3.
        HttpResponseMessage remove = await ApiTestHelpers.UnassignAssetAsync(
            plan.Owner, plan.VaultId, plan.FirstAssetId, plan.SecondBeneficiaryId);
        HttpResponseMessage addToOther = await ApiTestHelpers.AssignAssetAsync(
            plan.Owner, plan.VaultId, plan.ThirdAssetId, plan.FirstBeneficiaryId, plan.SecondBeneficiaryId);
        DropPinForEdits(plan.Owner);

        Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        Assert.Equal(HttpStatusCode.OK, addToOther.StatusCode);
        JsonElement setup = await GetSetupAsync(plan);
        // Một cặp mất, một cặp thêm, cùng liên quan người nhận 2.
        AssertCounts(setup.GetProperty("unsignedChanges"), 1, 1, 1);
    }

    private static Task<HttpResponseMessage> RespondWillAsync(PreparedPlan plan, bool accept, int willVersionNo, string? password)
    {
        return plan.Executor.PostAsJsonAsync($"/api/v1/executor/vaults/{plan.VaultId}/will/respond",
            new { accept, willVersionNo, password });
    }

    private static async Task<JsonElement> GetExecutorVaultAsync(PreparedPlan plan)
    {
        JsonElement vaults = await plan.Executor.GetFromJsonAsync<JsonElement>("/api/v1/executor/vaults");
        return vaults[0];
    }

    private Task<HttpResponseMessage> AcceptInvitationAsync(PreparedPlan plan, int? willVersionNo)
    {
        return plan.Executor.PostAsJsonAsync("/api/v1/executor-invitations/respond",
            new { token = mail.Token, accept = true, acknowledgeScope = true, scopeVersion = PlanSetupService.ScopeVersion,
                password = ApiTestHelpers.ExecutorPassword, willVersionNo });
    }

    private async Task<HttpResponseMessage> OwnerSignAsync(PreparedPlan plan)
    {
        JsonElement setup = await GetSetupAsync(plan);
        ActivatePlanRequest request = new ActivatePlanRequest();
        request.ConfigVersion = setup.GetProperty("configVersion").GetInt32();
        request.PolicyVersion = setup.GetProperty("policyVersion").GetString()!;
        request.AcknowledgePolicy = true;
        return await PostWithPinAsync(plan.Owner, $"/api/v1/vaults/{plan.VaultId}/activate", request, GoodPin);
    }

    [SqlFact]
    public async Task ExecutorCannotAcceptBeforeOwnerSigns_AndSeesPaperOnlyAfter()
    {
        PreparedPlan plan = await PrepareReadyPlanAsync(false);

        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(
            await AcceptInvitationAsync(plan, 1), HttpStatusCode.Conflict, "EXECUTOR_NOT_ALLOWED");
        Assert.Equal("WILL_NOT_SIGNED", problem.GetProperty("reason").GetString());
        JsonElement before = await plan.Executor.GetFromJsonAsync<JsonElement>("/api/v1/executor-invitations/mine");
        Assert.False(before.GetProperty("pending")[0].GetProperty("willReady").GetBoolean());

        Assert.Equal(HttpStatusCode.OK, (await OwnerSignAsync(plan)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await OwnerSignAsync(plan)).StatusCode);

        Assert.Single(await ReadVersionsAsync(plan.VaultId));
        JsonElement mine = await plan.Executor.GetFromJsonAsync<JsonElement>("/api/v1/executor-invitations/mine");
        JsonElement pending = mine.GetProperty("pending")[0];
        Assert.True(pending.GetProperty("willReady").GetBoolean());
        Assert.Equal(1, pending.GetProperty("willVersionNo").GetInt32());
        Assert.Equal(1, pending.GetProperty("assetCount").GetInt32());
        Assert.Equal(1, pending.GetProperty("recipientCount").GetInt32());
        Assert.DoesNotContain("Ghi chú", mine.GetRawText());
        JsonElement noVersion = await ApiTestHelpers.AssertProblemAsync(
            await AcceptInvitationAsync(plan, null), HttpStatusCode.Conflict, "EXECUTOR_NOT_ALLOWED");
        Assert.Equal("WILL_VERSION_CHANGED", noVersion.GetProperty("reason").GetString());

        Assert.Equal(HttpStatusCode.OK, (await AcceptInvitationAsync(plan, 1)).StatusCode);
        Assert.Equal("ACTIVE", (await GetSetupAsync(plan)).GetProperty("planStatus").GetString());
        JsonElement vault = await GetExecutorVaultAsync(plan);
        Assert.Equal(1, vault.GetProperty("confirmedWillVersionNo").GetInt32());
        Assert.False(vault.GetProperty("willConsentRequired").GetBoolean());
    }

    [SqlFact]
    public async Task OwnerChangesAfterSigningDraft_MustSignAgainBeforeExecutorAccepts()
    {
        PreparedPlan plan = await PrepareReadyPlanAsync(false);
        Assert.Equal(HttpStatusCode.OK, (await OwnerSignAsync(plan)).StatusCode);
        await AddUnsignedChangesAsync(plan);

        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(
            await AcceptInvitationAsync(plan, 1), HttpStatusCode.Conflict, "EXECUTOR_NOT_ALLOWED");
        Assert.Equal("WILL_NOT_SIGNED", problem.GetProperty("reason").GetString());
        Assert.False((await GetSetupAsync(plan)).GetProperty("willAwaitingExecutor").GetBoolean());

        Assert.Equal(HttpStatusCode.OK, (await OwnerSignAsync(plan)).StatusCode);
        JsonElement stale = await ApiTestHelpers.AssertProblemAsync(
            await AcceptInvitationAsync(plan, 1), HttpStatusCode.Conflict, "EXECUTOR_NOT_ALLOWED");
        Assert.Equal("WILL_VERSION_CHANGED", stale.GetProperty("reason").GetString());
        Assert.Equal(HttpStatusCode.OK, (await AcceptInvitationAsync(plan, 2)).StatusCode);
    }

    [SqlFact]
    public async Task ResignedWill_ExecutorConfirmsWithPassword()
    {
        PreparedPlan plan = await PrepareReadyPlanAsync(false);
        await ActivateAsync(plan);
        await AddUnsignedChangesAsync(plan);
        int configVersion = (await GetSetupAsync(plan)).GetProperty("configVersion").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await SignAsync(plan, configVersion, GoodPin)).StatusCode);

        JsonElement waiting = await GetExecutorVaultAsync(plan);
        Assert.True(waiting.GetProperty("willConsentRequired").GetBoolean());
        Assert.Equal(1, waiting.GetProperty("confirmedWillVersionNo").GetInt32());
        await ApiTestHelpers.AssertProblemAsync(
            await RespondWillAsync(plan, true, 2, null), HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        await ApiTestHelpers.AssertProblemAsync(
            await RespondWillAsync(plan, true, 2, "sai-mat-khau"), HttpStatusCode.Forbidden, "SIGNING_PASSWORD_INVALID");
        await ApiTestHelpers.AssertProblemAsync(
            await RespondWillAsync(plan, true, 1, ApiTestHelpers.ExecutorPassword), HttpStatusCode.Conflict, "EXECUTOR_NOT_ALLOWED");

        Assert.Equal(HttpStatusCode.NoContent, (await RespondWillAsync(plan, true, 2, ApiTestHelpers.ExecutorPassword)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await RespondWillAsync(plan, true, 2, ApiTestHelpers.ExecutorPassword)).StatusCode);

        JsonElement done = await GetExecutorVaultAsync(plan);
        Assert.False(done.GetProperty("willConsentRequired").GetBoolean());
        Assert.Equal(2, done.GetProperty("confirmedWillVersionNo").GetInt32());
        Assert.Equal(1, await CountAuditAsync(plan.VaultId, AuditAction.ExecutorWillConfirmed));
    }

    [SqlFact]
    public async Task ResignedWill_ExecutorDeclines_KeepsRoleAndOwnerSeesIt()
    {
        PreparedPlan plan = await PrepareReadyPlanAsync(false);
        await ActivateAsync(plan);
        await AddUnsignedChangesAsync(plan);
        int configVersion = (await GetSetupAsync(plan)).GetProperty("configVersion").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await SignAsync(plan, configVersion, GoodPin)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await RespondWillAsync(plan, false, 2, null)).StatusCode);

        JsonElement vault = await GetExecutorVaultAsync(plan);
        Assert.Equal(2, vault.GetProperty("declinedWillVersionNo").GetInt32());
        Assert.Equal(1, vault.GetProperty("confirmedWillVersionNo").GetInt32());
        JsonElement executor = (await GetSetupAsync(plan)).GetProperty("executors")[0];
        Assert.Equal("ACCEPTED", executor.GetProperty("status").GetString());
        Assert.Equal(2, executor.GetProperty("declinedWillVersionNo").GetInt32());
        Assert.Equal(1, await CountAuditAsync(plan.VaultId, AuditAction.ExecutorWillDeclined));
        // Người lạ không trả lời thay được.
        HttpClient stranger = await ApiTestHelpers.CreateClientForNewUserAsync(factory, true);
        HttpResponseMessage outsider = await stranger.PostAsJsonAsync(
            $"/api/v1/executor/vaults/{plan.VaultId}/will/respond", new { accept = false, willVersionNo = 2 });
        Assert.Equal(HttpStatusCode.NotFound, outsider.StatusCode);
    }
}
