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
        public Guid VaultId { get; set; }
        public Guid ContainerId { get; set; }
        public Guid FirstAssetId { get; set; }
        public Guid SecondAssetId { get; set; }
        public Guid FirstBeneficiaryId { get; set; }
        public Guid SecondBeneficiaryId { get; set; }
        public Guid SecondContainerId { get; set; }
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
    /// Kế hoạch đủ điều kiện kích hoạt. richConfig = true: gói A có 2 tài sản và 2 người nhận, thêm gói B (tài sản thứ 3, người nhận 1).
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
        plan.ContainerId = (await ApiTestHelpers.CreateContainerAsync(owner, vaultId, "Gói A")).GetProperty("id").GetGuid();
        plan.FirstBeneficiaryId = (await ApiTestHelpers.CreateBeneficiaryAsync(owner, vaultId, "000000001015", "Người nhận")).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddAssetToContainerAsync(owner, vaultId, plan.ContainerId, plan.FirstAssetId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddBeneficiaryToContainerAsync(owner, vaultId, plan.ContainerId, plan.FirstBeneficiaryId)).StatusCode);

        if (richConfig)
        {
            plan.SecondAssetId = await ApiTestHelpers.CreateTextAssetAsync(owner, vaultId, "Ghi chú hai");
            plan.SecondBeneficiaryId = (await ApiTestHelpers.CreateBeneficiaryAsync(owner, vaultId, SecondCitizenId, "Người nhận hai")).GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddAssetToContainerAsync(owner, vaultId, plan.ContainerId, plan.SecondAssetId)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddBeneficiaryToContainerAsync(owner, vaultId, plan.ContainerId, plan.SecondBeneficiaryId)).StatusCode);

            Guid thirdAssetId = await ApiTestHelpers.CreateTextAssetAsync(owner, vaultId, "Ghi chú ba");
            plan.SecondContainerId = (await ApiTestHelpers.CreateContainerAsync(owner, vaultId, "Gói B")).GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddAssetToContainerAsync(owner, vaultId, plan.SecondContainerId, thirdAssetId)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddBeneficiaryToContainerAsync(owner, vaultId, plan.SecondContainerId, plan.FirstBeneficiaryId)).StatusCode);
        }

        HttpClient executor = await ApiTestHelpers.CreateClientForNewUserAsync(factory, true, ExecutorEmail);
        await ApiTestHelpers.EnsureSystemExecutorAsync(factory, ExecutorEmail);
        HttpResponseMessage invite = await owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/executors", new { email = ExecutorEmail });
        Assert.Equal(HttpStatusCode.Created, invite.StatusCode);
        HttpResponseMessage accept = await executor.PostAsJsonAsync("/api/v1/executor-invitations/respond",
            new { token = mail.Token, accept = true, acknowledgeScope = true, scopeVersion = PlanSetupService.ScopeVersion, password = ApiTestHelpers.ExecutorPassword });
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
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

    private static void AssertCounts(JsonElement changes, int removedContainers, int addedContainers, int removedAssets,
        int addedAssets, int removedRecipients, int addedRecipients, int changedRecipientCount)
    {
        Assert.Equal(removedContainers, changes.GetProperty("removedContainers").GetInt32());
        Assert.Equal(addedContainers, changes.GetProperty("addedContainers").GetInt32());
        Assert.Equal(removedAssets, changes.GetProperty("removedAssets").GetInt32());
        Assert.Equal(addedAssets, changes.GetProperty("addedAssets").GetInt32());
        Assert.Equal(removedRecipients, changes.GetProperty("removedRecipients").GetInt32());
        Assert.Equal(addedRecipients, changes.GetProperty("addedRecipients").GetInt32());
        Assert.Equal(changedRecipientCount, changes.GetProperty("changedRecipientCount").GetInt32());
    }

    /// <summary>Thêm tài sản mới + người nhận mới vào gói A, tạo gói B mới (đều chưa ký).</summary>
    private async Task AddUnsignedChangesAsync(PreparedPlan plan)
    {
        UsePinForEdits(plan.Owner);
        Guid newAssetId = await ApiTestHelpers.CreateTextAssetAsync(plan.Owner, plan.VaultId, "Ghi chú mới");
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddAssetToContainerAsync(plan.Owner, plan.VaultId, plan.ContainerId, newAssetId)).StatusCode);
        Guid newBeneficiaryId = (await ApiTestHelpers.CreateBeneficiaryAsync(plan.Owner, plan.VaultId, SecondCitizenId, "Người nhận mới")).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddBeneficiaryToContainerAsync(plan.Owner, plan.VaultId, plan.ContainerId, newBeneficiaryId)).StatusCode);
        Guid assetForNewContainer = await ApiTestHelpers.CreateTextAssetAsync(plan.Owner, plan.VaultId, "Ghi chú gói mới");
        Guid newContainerId = (await ApiTestHelpers.CreateContainerAsync(plan.Owner, plan.VaultId, "Gói mới")).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddAssetToContainerAsync(plan.Owner, plan.VaultId, newContainerId, assetForNewContainer)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddBeneficiaryToContainerAsync(plan.Owner, plan.VaultId, newContainerId, plan.FirstBeneficiaryId)).StatusCode);
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
        Assert.Single(snapshot.Recipients);
        Assert.Single(snapshot.Assets);
        Assert.Equal(plan.ContainerId, snapshot.Recipients[0].ContainerId);
        Assert.Equal(plan.FirstBeneficiaryId, snapshot.Recipients[0].BeneficiaryId);
        Assert.Equal(plan.FirstAssetId, snapshot.Assets[0].AssetId);
        // Chỉ có id: không có tên gói, tên tài sản, tên người nhận.
        Assert.DoesNotContain("Gói", version.SnapshotJson);
        Assert.DoesNotContain("Ghi chú", version.SnapshotJson);
        Assert.DoesNotContain("Người nhận", version.SnapshotJson);

        JsonElement setup = await GetSetupAsync(plan);
        Assert.Equal(1, setup.GetProperty("willVersion").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, setup.GetProperty("willSignedAt").ValueKind);
        Assert.False(setup.GetProperty("hasUnsignedChanges").GetBoolean());
        AssertCounts(setup.GetProperty("unsignedChanges"), 0, 0, 0, 0, 0, 0, 0);
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
        AssertCounts(before.GetProperty("unsignedChanges"), 0, 1, 0, 1, 0, 1, 1);
        int configVersion = before.GetProperty("configVersion").GetInt32();

        HttpResponseMessage response = await SignAsync(plan, configVersion, GoodPin);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal(2, body.GetProperty("willVersion").GetInt32());
        Assert.False(body.GetProperty("hasUnsignedChanges").GetBoolean());
        AssertCounts(body.GetProperty("unsignedChanges"), 0, 0, 0, 0, 0, 0, 0);

        List<WillVersion> versions = await ReadVersionsAsync(plan.VaultId);
        Assert.Equal(2, versions.Count);
        Assert.Equal(2, versions[1].VersionNo);
        Assert.Equal(configVersion, versions[1].ConfigVersion);
        WillSnapshot snapshot = WillVersionRules.FromJson(versions[1].SnapshotJson);
        Assert.Equal(3, snapshot.Recipients.Count);
        Assert.Equal(3, snapshot.Assets.Count);

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
    public async Task PlanSetup_CountsRemovedAssetRecipientAndContainer()
    {
        PreparedPlan plan = await PrepareReadyPlanAsync(true);
        await ActivateAsync(plan);
        UsePinForEdits(plan.Owner);

        HttpResponseMessage removeAsset = await plan.Owner.DeleteAsync(
            $"/api/v1/vaults/{plan.VaultId}/containers/{plan.ContainerId}/assets/{plan.SecondAssetId}");
        HttpResponseMessage removeRecipient = await plan.Owner.DeleteAsync(
            $"/api/v1/vaults/{plan.VaultId}/containers/{plan.ContainerId}/beneficiaries/{plan.SecondBeneficiaryId}");
        HttpResponseMessage removeContainer = await plan.Owner.DeleteAsync(
            $"/api/v1/vaults/{plan.VaultId}/containers/{plan.SecondContainerId}");
        DropPinForEdits(plan.Owner);

        Assert.Equal(HttpStatusCode.OK, removeAsset.StatusCode);
        Assert.Equal(HttpStatusCode.OK, removeRecipient.StatusCode);
        Assert.Equal(HttpStatusCode.OK, removeContainer.StatusCode);
        JsonElement setup = await GetSetupAsync(plan);
        Assert.True(setup.GetProperty("hasUnsignedChanges").GetBoolean());
        AssertCounts(setup.GetProperty("unsignedChanges"), 1, 0, 1, 0, 1, 0, 1);

        // Ký lại: bản mới chỉ còn phần còn lại.
        HttpResponseMessage signed = await SignAsync(plan, setup.GetProperty("configVersion").GetInt32(), GoodPin);
        Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
        List<WillVersion> versions = await ReadVersionsAsync(plan.VaultId);
        Assert.Equal(2, versions.Count);
        WillSnapshot latest = WillVersionRules.FromJson(versions[1].SnapshotJson);
        Assert.Single(latest.Recipients);
        Assert.Single(latest.Assets);
    }

    [SqlFact]
    public async Task PlanSetup_CountsRecipientMovedBetweenContainers()
    {
        PreparedPlan plan = await PrepareReadyPlanAsync(true);
        await ActivateAsync(plan);
        UsePinForEdits(plan.Owner);

        // Gói A đang có 2 người nhận: gỡ người 2 khỏi gói A rồi gắn người 2 vào gói B.
        HttpResponseMessage removeRecipient = await plan.Owner.DeleteAsync(
            $"/api/v1/vaults/{plan.VaultId}/containers/{plan.ContainerId}/beneficiaries/{plan.SecondBeneficiaryId}");
        HttpResponseMessage addToOther = await ApiTestHelpers.AddBeneficiaryToContainerAsync(
            plan.Owner, plan.VaultId, plan.SecondContainerId, plan.SecondBeneficiaryId);
        DropPinForEdits(plan.Owner);

        Assert.Equal(HttpStatusCode.OK, removeRecipient.StatusCode);
        Assert.Equal(HttpStatusCode.OK, addToOther.StatusCode);
        JsonElement setup = await GetSetupAsync(plan);
        // Gói A mất 1 người nhận, gói B thêm 1 người nhận: hai gói có người nhận khác bản ký.
        AssertCounts(setup.GetProperty("unsignedChanges"), 0, 0, 0, 0, 1, 1, 2);
    }
}
