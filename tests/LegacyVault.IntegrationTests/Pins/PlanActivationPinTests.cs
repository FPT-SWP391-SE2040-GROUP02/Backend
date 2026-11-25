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

namespace LegacyVault.IntegrationTests.Pins;

/// <summary>
/// Kích hoạt kế hoạch là bước Chủ kho ký di chúc bằng PIN của kho: thiếu, sai hoặc đúng PIN
/// (bộ kiểm PIN thật, SQL Server thật).
/// </summary>
public sealed class PlanActivationPinTests : IAsyncLifetime
{
    private const string GoodPin = "482913";
    private const string WrongPin = "999000";
    private const string ExecutorEmail = "ky-di-chuc@example.test";

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

    /// <summary>Kế hoạch đủ điều kiện kích hoạt: có PIN, có tài sản đã gán cho người nhận, Người thực thi đã chấp nhận.</summary>
    private async Task<(HttpClient Owner, Guid VaultId, ActivatePlanRequest Request)> PrepareReadyPlanAsync()
    {
        (HttpClient owner, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        HttpResponseMessage pinSet = await owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/pin", new { pin = GoodPin, confirmPin = GoodPin });
        Assert.Equal(HttpStatusCode.OK, pinSet.StatusCode);
        // Các bước chuẩn bị (thêm người nhận, mời Người thực thi...) cũng đòi PIN; bỏ header trước khi trả client về cho test.
        owner.DefaultRequestHeaders.Add("X-Vault-Pin", GoodPin);
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
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(owner, vaultId, "Ghi chú");
        JsonElement beneficiary = await ApiTestHelpers.CreateBeneficiaryAsync(owner, vaultId, "000000001015", "Người nhận");
        await ApiTestHelpers.AssignAssetOkAsync(owner, vaultId, assetId, beneficiary.GetProperty("id").GetGuid());

        HttpClient executor = await ApiTestHelpers.CreateClientForNewUserAsync(factory, true, ExecutorEmail);
        await ApiTestHelpers.EnsureSystemExecutorAsync(factory, ExecutorEmail);
        HttpResponseMessage invite = await owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/executors", new { email = ExecutorEmail });
        Assert.Equal(HttpStatusCode.Created, invite.StatusCode);
        executorClient = executor;

        PlanSetupResponse setup = (await owner.GetFromJsonAsync<PlanSetupResponse>($"/api/v1/vaults/{vaultId}/plan-setup"))!;
        owner.DefaultRequestHeaders.Remove("X-Vault-Pin");
        ActivatePlanRequest request = new ActivatePlanRequest
        {
            ConfigVersion = setup.ConfigVersion, PolicyVersion = setup.PolicyVersion, AcknowledgePolicy = true,
        };
        return (owner, vaultId, request);
    }

    private HttpClient executorClient = null!;

    private async Task AcceptAsync()
    {
        HttpResponseMessage accept = await executorClient.PostAsJsonAsync("/api/v1/executor-invitations/respond",
            new { token = mail.Token, accept = true, acknowledgeScope = true, scopeVersion = PlanSetupService.ScopeVersion, password = ApiTestHelpers.ExecutorPassword, willVersionNo = 1 });
        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
    }

    private async Task AssertStillDraftWithoutKeyAsync(Guid vaultId)
    {
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(PlanStatus.Draft, (await db.HandoverPlans.SingleAsync(p => p.VaultId == vaultId)).Status);
            Assert.False(await db.Set<ExecutorKeyShard>().AnyAsync(k => k.VaultId == vaultId));
            Assert.False(await db.Set<AuditLog>().AnyAsync(l => l.VaultId == vaultId && l.Action == AuditAction.PlanActivated));
        });
    }

    [SqlFact]
    public async Task Activate_WithoutPin_IsRejectedAndNothingChanges()
    {
        (HttpClient owner, Guid vaultId, ActivatePlanRequest request) = await PrepareReadyPlanAsync();

        HttpResponseMessage response = await owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/activate", request);

        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Forbidden, "VAULT_PIN_REQUIRED");
        await AssertStillDraftWithoutKeyAsync(vaultId);
    }

    [SqlFact]
    public async Task Activate_WithWrongPin_IsRejectedAndNothingChanges()
    {
        (HttpClient owner, Guid vaultId, ActivatePlanRequest request) = await PrepareReadyPlanAsync();

        HttpResponseMessage response = await PostWithPinAsync(owner, $"/api/v1/vaults/{vaultId}/activate", request, WrongPin);

        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Forbidden, "INVALID_VAULT_PIN");
        await AssertStillDraftWithoutKeyAsync(vaultId);
    }

    [SqlFact]
    public async Task Activate_WithCorrectPin_ActivatesAndSignsWithoutReturningTheKey()
    {
        (HttpClient owner, Guid vaultId, ActivatePlanRequest request) = await PrepareReadyPlanAsync();

        HttpResponseMessage response = await PostWithPinAsync(owner, $"/api/v1/vaults/{vaultId}/activate", request, GoodPin);
        string raw = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("shard", raw, StringComparison.OrdinalIgnoreCase);
        JsonElement body = JsonDocument.Parse(raw).RootElement;
        Assert.Equal("DRAFT", body.GetProperty("planStatus").GetString());
        Assert.True(body.GetProperty("willAwaitingExecutor").GetBoolean());
        await AssertStillDraftWithoutKeyAsync(vaultId);
        await AcceptAsync();
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(1, await db.Set<AuditLog>().CountAsync(l => l.VaultId == vaultId && l.Action == AuditAction.PlanActivated));
            ExecutorKeyShard key = await db.Set<ExecutorKeyShard>().SingleAsync(k => k.VaultId == vaultId);
            Assert.Equal(1, key.Version);
            Assert.False(string.IsNullOrEmpty(key.EncryptedShard));
        });
    }

    [SqlFact]
    public async Task ReissueKey_NeedsThePinToo()
    {
        (HttpClient owner, Guid vaultId, ActivatePlanRequest request) = await PrepareReadyPlanAsync();
        HttpResponseMessage activated = await PostWithPinAsync(owner, $"/api/v1/vaults/{vaultId}/activate", request, GoodPin);
        Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
        await AcceptAsync();

        HttpResponseMessage missing = await owner.PostAsync($"/api/v1/vaults/{vaultId}/executor-key/reissue", null);
        HttpResponseMessage ok = await PostWithPinAsync(owner, $"/api/v1/vaults/{vaultId}/executor-key/reissue", new { }, GoodPin);

        await ApiTestHelpers.AssertProblemAsync(missing, HttpStatusCode.Forbidden, "VAULT_PIN_REQUIRED");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(2, (await ApiTestHelpers.ReadJsonAsync(ok)).GetProperty("version").GetInt32());
    }
}
