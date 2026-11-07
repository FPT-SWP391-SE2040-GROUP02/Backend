using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.DeathCases;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests.ExecutorAssign;

/// <summary>Người thực thi xem và tải tài sản của kho mình phụ trách: chỉ đúng Executor hiệu lực, có nhật ký, giải mã như chủ kho.</summary>
public sealed class ExecutorAssetsFlowTests : IAsyncLifetime
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

    private static string AssetsUrl(Guid vaultId)
    {
        return "/api/v1/executor/vaults/" + vaultId + "/assets";
    }

    [SqlFact]
    public async Task Executor_ListsAndDownloadsAsset_AndEachDownloadIsAudited()
    {
        // Chuẩn bị: kho có một tài sản chữ, Người thực thi hiệu lực
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(vault.Client, vault.VaultId, "Di chúc");
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);

        // Hành động
        HttpResponseMessage list = await executor.Client.GetAsync(AssetsUrl(vault.VaultId));
        HttpResponseMessage content = await executor.Client.GetAsync(AssetsUrl(vault.VaultId) + "/" + assetId + "/content");

        // Kiểm tra: thấy đúng một tài sản; nội dung giải mã đúng như chủ kho tải; có đúng một dòng nhật ký vai trò Executor
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        JsonElement page = await list.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(assetId, page.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.OK, content.StatusCode);
        Assert.Equal("Nội dung của Di chúc", await content.Content.ReadAsStringAsync());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(1, await db.AuditLogs.CountAsync(a =>
                a.Action == AuditAction.AssetContentDownloaded && a.ActorRole == AuditActorRole.Executor
                && a.ActorUserId == executor.User.Id && a.TargetId == assetId));
        });
    }

    [SqlFact]
    public async Task StrangerAndOwnerRole_Get404_AndUnknownAssetIs404()
    {
        // Chuẩn bị: kho có tài sản; một người lạ; Người thực thi của kho khác
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(vault.Client, vault.VaultId, "Tài sản");
        DeathCaseTestData.OwnerVault other = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient otherExecutor = await DeathCaseTestData.CreateExecutorClientAsync(factory, other.VaultId);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);

        // Hành động
        HttpResponseMessage fromOtherExecutor = await otherExecutor.Client.GetAsync(AssetsUrl(vault.VaultId));
        HttpResponseMessage fromOwnerRoute = await vault.Client.GetAsync(AssetsUrl(vault.VaultId));
        HttpResponseMessage unknownAsset = await executor.Client.GetAsync(
            AssetsUrl(vault.VaultId) + "/" + Guid.NewGuid() + "/content");
        HttpResponseMessage badSort = await executor.Client.GetAsync(AssetsUrl(vault.VaultId) + "?sortBy=password");

        // Kiểm tra: Executor kho khác và chủ kho (không phải Executor) đều 404; tài sản lạ 404; sắp xếp lạ 400; không có dòng nhật ký tải
        Assert.Equal(HttpStatusCode.NotFound, fromOtherExecutor.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, fromOwnerRoute.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknownAsset.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, badSort.StatusCode);
        Assert.NotEqual(Guid.Empty, assetId);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Assert.Equal(0, await db.AuditLogs.CountAsync(a => a.Action == AuditAction.AssetContentDownloaded));
        });
    }
}
