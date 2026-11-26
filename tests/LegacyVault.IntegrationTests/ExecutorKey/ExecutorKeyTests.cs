using System.Net;
using System.Reflection;
using System.Text.Json;
using LegacyVault.Api.Controllers;
using LegacyVault.Api.Security;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.ExecutorKey;

/// <summary>Luật thuần của mảnh khóa Người thực thi: sinh, chuẩn hóa, băm, kiểm.</summary>
public sealed class ExecutorKeyRulesTests
{
    [Fact]
    public void GeneratedShardHas4GroupsOfFourFromTheCrockfordAlphabet()
    {
        string shard = ExecutorKeyRules.GenerateShard();

        string[] groups = shard.Split('-');
        Assert.Equal(4, groups.Length);
        Assert.All(groups, group => Assert.Equal(4, group.Length));
        Assert.Equal(16, ExecutorKeyRules.Normalize(shard).Length);
        Assert.Matches("^[0-9A-HJKMNP-TV-Z-]+$", shard);
    }

    [Fact]
    public void ShardIssuedBeforeTheShorterFormatStillMatches()
    {
        // Mảnh cũ 13 nhóm (52 ký tự) đã cấp cho các kho trước đây vẫn phải được chấp nhận.
        Guid vaultId = Guid.NewGuid();
        string oldShard = "THWD-JYZZ-1GGC-JY1K-TYJ1-SDAG-NMTA-3FKB-3S25-4CDV-2ZWQ-JQ6F-SCX0";
        string hash = ExecutorKeyRules.Hash(vaultId, 1, oldShard);

        Assert.True(ExecutorKeyRules.Matches(vaultId, 1, oldShard.ToLowerInvariant(), hash));
    }

    [Fact]
    public void TwoGeneratedShardsDiffer()
    {
        Assert.NotEqual(ExecutorKeyRules.GenerateShard(), ExecutorKeyRules.GenerateShard());
    }

    [Fact]
    public void NormalizeIgnoresDashesSpacesCaseAndConfusableLetters()
    {
        Assert.Equal("01213ABCD", ExecutorKeyRules.Normalize(" o1-2l3 abcd "));
    }

    [Fact]
    public void HashIsBoundToVaultAndVersionAndMatchesTypedShard()
    {
        Guid vaultId = Guid.NewGuid();
        string shard = ExecutorKeyRules.GenerateShard();
        string hash = ExecutorKeyRules.Hash(vaultId, 1, shard);

        Assert.Equal(64, hash.Length);
        Assert.True(ExecutorKeyRules.Matches(vaultId, 1, shard.ToLowerInvariant().Replace("-", " "), hash));
        Assert.False(ExecutorKeyRules.Matches(vaultId, 2, shard, hash));
        Assert.False(ExecutorKeyRules.Matches(Guid.NewGuid(), 1, shard, hash));
        Assert.False(ExecutorKeyRules.Matches(vaultId, 1, ExecutorKeyRules.GenerateShard(), hash));
    }

    [Fact]
    public void ReissueRequiresTheVaultPin()
    {
        MethodInfo? method = typeof(ExecutorKeyController).GetMethod(nameof(ExecutorKeyController.Reissue));

        Assert.NotNull(method);
        Assert.NotNull(method.GetCustomAttribute<RequireVaultPinAttribute>());
    }

    [Fact]
    public void OwnerCannotIssueTheFirstKey_TheServerCreatesItAtActivation()
    {
        // Endpoint cấp mã lần đầu đã bị xóa: không còn action nào ngoài xem trạng thái và đổi mã.
        Assert.Null(typeof(ExecutorKeyController).GetMethod("Issue"));
    }
}

/// <summary>Mã nhận do server tạo: Chủ tài sản xem trạng thái và đổi mã, Người thực thi đọc khi bàn giao bắt đầu (SQL Server thật).</summary>
public sealed class ExecutorKeyFlowTests : IAsyncLifetime
{
    private ApiFactory factory = null!;

    public async Task InitializeAsync()
    {
        ApiFactory? created = ApiFactory.CreateForTempDatabase();
        if (created == null)
        {
            return;
        }
        factory = created;
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

    private static string Url(Guid vaultId)
    {
        return "/api/v1/vaults/" + vaultId + "/executor-key";
    }

    private static string HandoverKeyUrl(Guid vaultId)
    {
        return "/api/v1/executor/vaults/" + vaultId + "/handover-key";
    }

    /// <summary>Tạo mã như lúc kích hoạt kế hoạch (dùng đúng ExecutorKeyIssuer của server).</summary>
    private async Task SeedKeyAsync(Guid vaultId)
    {
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IVaultRepository vaults = scope.ServiceProvider.GetRequiredService<IVaultRepository>();
            ExecutorKeyIssuer issuer = scope.ServiceProvider.GetRequiredService<ExecutorKeyIssuer>();
            IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            Vault? vault = await vaults.FindByIdAsync(vaultId, CancellationToken.None);
            await issuer.EnsureReadableAsync(vault!, AuditActorRole.Owner, vault!.OwnerId, DateTimeOffset.UtcNow, CancellationToken.None);
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
        }
    }

    [SqlFact]
    public async Task StatusStartsWithoutAKeyAndNeverReturnsTheShard()
    {
        VaultTestData.OwnerVault vault = await VaultTestData.CreateOwnerWithRunningPlanAsync(factory);

        JsonElement before = await ApiTestHelpers.ReadJsonAsync(await vault.Client.GetAsync(Url(vault.VaultId)));
        Assert.False(before.GetProperty("hasKey").GetBoolean());
        Assert.Equal(0, before.GetProperty("version").GetInt32());
        Assert.True(before.GetProperty("canIssue").GetBoolean());

        await SeedKeyAsync(vault.VaultId);
        string raw = await (await vault.Client.GetAsync(Url(vault.VaultId))).Content.ReadAsStringAsync();
        Assert.DoesNotContain("shard", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"hasKey\":true", raw);
    }

    [SqlFact]
    public async Task IssueEndpointIsGone()
    {
        VaultTestData.OwnerVault vault = await VaultTestData.CreateOwnerWithRunningPlanAsync(factory);

        HttpResponseMessage response = await vault.Client.PostAsync(Url(vault.VaultId), null);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        JsonElement status = await ApiTestHelpers.ReadJsonAsync(await vault.Client.GetAsync(Url(vault.VaultId)));
        Assert.False(status.GetProperty("hasKey").GetBoolean());
    }

    [SqlFact]
    public async Task SeededKeyStoresOnlyTheHashAndAnEnvelopeCopy()
    {
        VaultTestData.OwnerVault vault = await VaultTestData.CreateOwnerWithRunningPlanAsync(factory);
        VaultTestData.ExecutorClient executor = await VaultTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        await SeedKeyAsync(vault.VaultId);
        await ApiTestHelpers.SetPlanStatusAsync(factory, vault.VaultId, PlanStatus.HandoverInProgress);

        JsonElement read = await ApiTestHelpers.ReadJsonAsync(await executor.Client.GetAsync(HandoverKeyUrl(vault.VaultId)));

        string shard = read.GetProperty("shard").GetString()!;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            ExecutorKeyShard stored = await db.Set<ExecutorKeyShard>().AsNoTracking().SingleAsync(s => s.VaultId == vault.VaultId);
            Assert.Equal(1, stored.Version);
            Assert.NotEqual(shard, stored.ShardHash);
            Assert.True(ExecutorKeyRules.Matches(vault.VaultId, 1, shard, stored.ShardHash));
            // Bản đọc lại trên hệ thống là bản mã hóa phong bì, không phải mã gốc.
            Assert.False(string.IsNullOrEmpty(stored.EncryptedShard));
            Assert.DoesNotContain(shard, stored.EncryptedShard);
            Assert.NotNull(stored.WrappedDataKey);
            Assert.False(string.IsNullOrEmpty(stored.EncryptionKeyId));
            Assert.Equal(1, await db.Set<AuditLog>().CountAsync(
                l => l.VaultId == vault.VaultId && l.Action == AuditAction.ExecutorKeyIssued));
        });
    }

    [SqlFact]
    public async Task ReissueBumpsTheVersionAndNeverReturnsTheShard()
    {
        VaultTestData.OwnerVault vault = await VaultTestData.CreateOwnerWithRunningPlanAsync(factory);
        VaultTestData.ExecutorClient executor = await VaultTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        await SeedKeyAsync(vault.VaultId);
        await ApiTestHelpers.SetPlanStatusAsync(factory, vault.VaultId, PlanStatus.HandoverInProgress);
        string oldShard = (await ApiTestHelpers.ReadJsonAsync(
            await executor.Client.GetAsync(HandoverKeyUrl(vault.VaultId)))).GetProperty("shard").GetString()!;
        // Kế hoạch đang chạy lại (Active) thì Chủ tài sản mới đổi mã được.
        await ApiTestHelpers.SetPlanStatusAsync(factory, vault.VaultId, PlanStatus.Active);

        HttpResponseMessage response = await vault.Client.PostAsync(Url(vault.VaultId) + "/reissue", null);
        string raw = await response.Content.ReadAsStringAsync();
        JsonElement second = JsonDocument.Parse(raw).RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("shard", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, second.GetProperty("version").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, second.GetProperty("reissuedAt").ValueKind);
        await ApiTestHelpers.SetPlanStatusAsync(factory, vault.VaultId, PlanStatus.HandoverInProgress);
        JsonElement read = await ApiTestHelpers.ReadJsonAsync(await executor.Client.GetAsync(HandoverKeyUrl(vault.VaultId)));
        string newShard = read.GetProperty("shard").GetString()!;
        Assert.NotEqual(oldShard, newShard);
        Assert.Equal(2, read.GetProperty("version").GetInt32());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            ExecutorKeyShard stored = await db.Set<ExecutorKeyShard>().AsNoTracking().SingleAsync(s => s.VaultId == vault.VaultId);
            Assert.True(ExecutorKeyRules.Matches(vault.VaultId, 2, newShard, stored.ShardHash));
            Assert.False(ExecutorKeyRules.Matches(vault.VaultId, 2, oldShard, stored.ShardHash));
            Assert.Equal(1, await db.Set<AuditLog>().CountAsync(
                l => l.VaultId == vault.VaultId && l.Action == AuditAction.ExecutorKeyReissued));
        });
    }

    [SqlFact]
    public async Task ReissueBeforeAnyKeyIsAConflict()
    {
        VaultTestData.OwnerVault vault = await VaultTestData.CreateOwnerWithRunningPlanAsync(factory);

        HttpResponseMessage response = await vault.Client.PostAsync(Url(vault.VaultId) + "/reissue", null);

        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "EXECUTOR_KEY_NOT_ISSUED");
    }

    [SqlFact]
    public async Task AnotherUserCannotSeeOrReissueTheKey()
    {
        VaultTestData.OwnerVault vault = await VaultTestData.CreateOwnerWithRunningPlanAsync(factory);
        VaultTestData.OwnerVault stranger = await VaultTestData.CreateOwnerWithRunningPlanAsync(factory);
        await SeedKeyAsync(vault.VaultId);

        HttpResponseMessage get = await stranger.Client.GetAsync(Url(vault.VaultId));
        HttpResponseMessage post = await stranger.Client.PostAsync(Url(vault.VaultId) + "/reissue", null);

        await ApiTestHelpers.AssertProblemAsync(get, HttpStatusCode.NotFound, "VAULT_NOT_FOUND");
        await ApiTestHelpers.AssertProblemAsync(post, HttpStatusCode.NotFound, "VAULT_NOT_FOUND");
    }

    [SqlFact]
    public async Task ReissueIsBlockedOnceThePlanIsLockedByADeathCase()
    {
        VaultTestData.OwnerVault vault = await VaultTestData.CreateOwnerWithRunningPlanAsync(factory);
        await SeedKeyAsync(vault.VaultId);
        await ApiTestHelpers.SetPlanStatusAsync(factory, vault.VaultId, PlanStatus.HandoverRequested);

        HttpResponseMessage response = await vault.Client.PostAsync(Url(vault.VaultId) + "/reissue", null);
        JsonElement status = await ApiTestHelpers.ReadJsonAsync(await vault.Client.GetAsync(Url(vault.VaultId)));

        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "VAULT_NOT_EDITABLE");
        Assert.False(status.GetProperty("canIssue").GetBoolean());
        Assert.Equal(1, status.GetProperty("version").GetInt32());
    }

    [SqlFact]
    public async Task ExecutorCannotReadTheKeyBeforeHandoverStarts_ButCanAfter()
    {
        VaultTestData.OwnerVault vault = await VaultTestData.CreateOwnerWithRunningPlanAsync(factory);
        VaultTestData.ExecutorClient executor = await VaultTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        await SeedKeyAsync(vault.VaultId);

        // Kế hoạch Active (và cả khi đã mở hồ sơ chứng tử) thì chưa xem được mã.
        JsonElement early = await ApiTestHelpers.AssertProblemAsync(
            await executor.Client.GetAsync(HandoverKeyUrl(vault.VaultId)), HttpStatusCode.Conflict, "HANDOVER_NOT_ALLOWED");
        Assert.Equal("HANDOVER_NOT_STARTED", early.GetProperty("reason").GetString());
        await ApiTestHelpers.SetPlanStatusAsync(factory, vault.VaultId, PlanStatus.HandoverRequested);
        await ApiTestHelpers.AssertProblemAsync(
            await executor.Client.GetAsync(HandoverKeyUrl(vault.VaultId)), HttpStatusCode.Conflict, "HANDOVER_NOT_ALLOWED");

        await ApiTestHelpers.SetPlanStatusAsync(factory, vault.VaultId, PlanStatus.HandoverInProgress);
        HttpResponseMessage response = await executor.Client.GetAsync(HandoverKeyUrl(vault.VaultId));
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(16, ExecutorKeyRules.Normalize(body.GetProperty("shard").GetString()).Length);
        Assert.Equal(1, body.GetProperty("version").GetInt32());
        Assert.Equal(vault.Owner.FullName, body.GetProperty("ownerFullName").GetString());
    }

    [SqlFact]
    public async Task ExecutorCanStillReadTheKeyOnceTheLastRecipientAcceptedAndThePlanIsClosed()
    {
        VaultTestData.OwnerVault vault = await VaultTestData.CreateOwnerWithRunningPlanAsync(factory);
        VaultTestData.ExecutorClient executor = await VaultTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        await SeedKeyAsync(vault.VaultId);
        await ApiTestHelpers.SetPlanStatusAsync(factory, vault.VaultId, PlanStatus.Closed);

        HttpResponseMessage response = await executor.Client.GetAsync(HandoverKeyUrl(vault.VaultId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [SqlFact]
    public async Task OwnerStrangerAndNotYetAcceptedExecutorCannotReadTheKey()
    {
        VaultTestData.OwnerVault vault = await VaultTestData.CreateOwnerWithRunningPlanAsync(factory);
        await SeedKeyAsync(vault.VaultId);
        await ApiTestHelpers.SetPlanStatusAsync(factory, vault.VaultId, PlanStatus.HandoverInProgress);
        VaultTestData.OwnerVault stranger = await VaultTestData.CreateOwnerWithRunningPlanAsync(factory);
        User invitedUser = await VaultTestData.AddUserAsync(factory, UserRole.Executor, UserStatus.Active);
        await VaultTestData.AddExecutorAsync(factory, vault.VaultId, invitedUser.Id, ExecutorStatus.Invited);
        HttpClient invited = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(invited, factory.Services, invitedUser);

        await ApiTestHelpers.AssertProblemAsync(
            await vault.Client.GetAsync(HandoverKeyUrl(vault.VaultId)), HttpStatusCode.NotFound, "VAULT_NOT_FOUND");
        await ApiTestHelpers.AssertProblemAsync(
            await stranger.Client.GetAsync(HandoverKeyUrl(vault.VaultId)), HttpStatusCode.NotFound, "VAULT_NOT_FOUND");
        await ApiTestHelpers.AssertProblemAsync(
            await invited.GetAsync(HandoverKeyUrl(vault.VaultId)), HttpStatusCode.NotFound, "VAULT_NOT_FOUND");
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await factory.CreateClient().GetAsync(HandoverKeyUrl(vault.VaultId))).StatusCode);
    }

    [SqlFact]
    public async Task KeyWithoutAnEnvelopeCopyAsksTheOwnerToReissue()
    {
        VaultTestData.OwnerVault vault = await VaultTestData.CreateOwnerWithRunningPlanAsync(factory);
        VaultTestData.ExecutorClient executor = await VaultTestData.CreateExecutorClientAsync(factory, vault.VaultId);
        await SeedKeyAsync(vault.VaultId);
        await ApiTestHelpers.SetPlanStatusAsync(factory, vault.VaultId, PlanStatus.HandoverInProgress);
        // Mã cấp trước tính năng này chỉ có hash, không có bản mã hóa.
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Set<ExecutorKeyShard>().Where(s => s.VaultId == vault.VaultId).ExecuteUpdateAsync(s => s
                .SetProperty(k => k.EncryptedShard, (string?)null)
                .SetProperty(k => k.WrappedDataKey, (byte[]?)null)
                .SetProperty(k => k.EncryptionKeyId, (string?)null));
        });

        HttpResponseMessage response = await executor.Client.GetAsync(HandoverKeyUrl(vault.VaultId));

        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "EXECUTOR_KEY_NOT_ISSUED");
    }
}
