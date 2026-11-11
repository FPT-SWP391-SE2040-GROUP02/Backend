using System.Net;
using System.Reflection;
using System.Text.Json;
using LegacyVault.Api.Controllers;
using LegacyVault.Api.Security;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.DeathCases;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
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

    [Theory]
    [InlineData(nameof(ExecutorKeyController.Issue))]
    [InlineData(nameof(ExecutorKeyController.Reissue))]
    public void IssuingActionsRequireTheVaultPin(string action)
    {
        MethodInfo? method = typeof(ExecutorKeyController).GetMethod(action);

        Assert.NotNull(method);
        Assert.NotNull(method.GetCustomAttribute<RequireVaultPinAttribute>());
    }
}

/// <summary>Cấp mảnh khóa Người thực thi và "quên khóa" (cấp lại) qua HTTP trên SQL Server thật.</summary>
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

    [SqlFact]
    public async Task StatusStartsWithoutAKeyAndNeverReturnsTheShard()
    {
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);

        JsonElement before = await ApiTestHelpers.ReadJsonAsync(await vault.Client.GetAsync(Url(vault.VaultId)));
        Assert.False(before.GetProperty("hasKey").GetBoolean());
        Assert.Equal(0, before.GetProperty("version").GetInt32());
        Assert.True(before.GetProperty("canIssue").GetBoolean());

        await vault.Client.PostAsync(Url(vault.VaultId), null);
        string raw = await (await vault.Client.GetAsync(Url(vault.VaultId))).Content.ReadAsStringAsync();
        Assert.DoesNotContain("shard", raw, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"hasKey\":true", raw);
    }

    [SqlFact]
    public async Task IssueReturnsTheShardOnceAndStoresOnlyItsHash()
    {
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);

        HttpResponseMessage response = await vault.Client.PostAsync(Url(vault.VaultId), null);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string shard = body.GetProperty("shard").GetString()!;
        Assert.Equal(1, body.GetProperty("version").GetInt32());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            ExecutorKeyShard stored = await db.Set<ExecutorKeyShard>().AsNoTracking().SingleAsync(s => s.VaultId == vault.VaultId);
            Assert.Equal(1, stored.Version);
            Assert.NotEqual(shard, stored.ShardHash);
            Assert.True(ExecutorKeyRules.Matches(vault.VaultId, 1, shard, stored.ShardHash));
            Assert.Equal(1, await db.Set<AuditLog>().CountAsync(
                l => l.VaultId == vault.VaultId && l.Action == AuditAction.ExecutorKeyIssued));
        });
    }

    [SqlFact]
    public async Task IssuingTwiceIsAConflictThatPointsToReissue()
    {
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        await vault.Client.PostAsync(Url(vault.VaultId), null);

        HttpResponseMessage again = await vault.Client.PostAsync(Url(vault.VaultId), null);

        await ApiTestHelpers.AssertProblemAsync(again, HttpStatusCode.Conflict, "EXECUTOR_KEY_ALREADY_ISSUED");
    }

    [SqlFact]
    public async Task ReissueReplacesTheOldShardAndBumpsTheVersion()
    {
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        JsonElement first = await ApiTestHelpers.ReadJsonAsync(await vault.Client.PostAsync(Url(vault.VaultId), null));
        string oldShard = first.GetProperty("shard").GetString()!;

        JsonElement second = await ApiTestHelpers.ReadJsonAsync(
            await vault.Client.PostAsync(Url(vault.VaultId) + "/reissue", null));

        string newShard = second.GetProperty("shard").GetString()!;
        Assert.Equal(2, second.GetProperty("version").GetInt32());
        Assert.NotEqual(oldShard, newShard);
        Assert.NotEqual(JsonValueKind.Null, second.GetProperty("reissuedAt").ValueKind);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            ExecutorKeyShard stored = await db.Set<ExecutorKeyShard>().AsNoTracking().SingleAsync(s => s.VaultId == vault.VaultId);
            Assert.True(ExecutorKeyRules.Matches(vault.VaultId, 2, newShard, stored.ShardHash));
            Assert.False(ExecutorKeyRules.Matches(vault.VaultId, 1, oldShard, stored.ShardHash));
            Assert.False(ExecutorKeyRules.Matches(vault.VaultId, 2, oldShard, stored.ShardHash));
            Assert.Equal(1, await db.Set<AuditLog>().CountAsync(
                l => l.VaultId == vault.VaultId && l.Action == AuditAction.ExecutorKeyReissued));
        });
    }

    [SqlFact]
    public async Task ReissueBeforeAnyIssueIsAConflict()
    {
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);

        HttpResponseMessage response = await vault.Client.PostAsync(Url(vault.VaultId) + "/reissue", null);

        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "EXECUTOR_KEY_NOT_ISSUED");
    }

    [SqlFact]
    public async Task AnotherUserCannotSeeOrIssueTheKey()
    {
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.OwnerVault stranger = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);

        HttpResponseMessage get = await stranger.Client.GetAsync(Url(vault.VaultId));
        HttpResponseMessage post = await stranger.Client.PostAsync(Url(vault.VaultId), null);

        await ApiTestHelpers.AssertProblemAsync(get, HttpStatusCode.NotFound, "VAULT_NOT_FOUND");
        await ApiTestHelpers.AssertProblemAsync(post, HttpStatusCode.NotFound, "VAULT_NOT_FOUND");
    }

    [SqlFact]
    public async Task ReissueIsBlockedOnceThePlanIsLockedByADeathCase()
    {
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        await vault.Client.PostAsync(Url(vault.VaultId), null);
        await ApiTestHelpers.SetPlanStatusAsync(factory, vault.VaultId, PlanStatus.DeathCaseOpen);

        HttpResponseMessage response = await vault.Client.PostAsync(Url(vault.VaultId) + "/reissue", null);
        JsonElement status = await ApiTestHelpers.ReadJsonAsync(await vault.Client.GetAsync(Url(vault.VaultId)));

        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "VAULT_NOT_EDITABLE");
        Assert.False(status.GetProperty("canIssue").GetBoolean());
        Assert.Equal(1, status.GetProperty("version").GetInt32());
    }
}
