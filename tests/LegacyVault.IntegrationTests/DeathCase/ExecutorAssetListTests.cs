using System.Net;
using System.Text.Json;
using LegacyVault.IntegrationTests.TestSupport;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>Người thực thi xem danh sách tài sản của kho theo gói, qua HTTP trên SQL Server thật.</summary>
public sealed class ExecutorAssetListTests : IAsyncLifetime
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

    /// <summary>Kho có một gói Ready: hai tài sản chữ và một người nhận.</summary>
    private async Task<DeathCaseTestData.OwnerVault> CreateVaultWithReadyPackageAsync()
    {
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        await ApiTestHelpers.UpgradeVaultToXsAsync(factory, vault.VaultId);
        Guid first = await ApiTestHelpers.CreateTextAssetAsync(vault.Client, vault.VaultId, "Email gia đình");
        Guid second = await ApiTestHelpers.CreateTextAssetAsync(vault.Client, vault.VaultId, "Lời nhắn cho con");
        JsonElement beneficiary = await ApiTestHelpers.CreateBeneficiaryAsync(
            vault.Client, vault.VaultId, "079201000791", ApiTestHelpers.BeneficiaryName(0));
        JsonElement container = await ApiTestHelpers.CreateContainerAsync(vault.Client, vault.VaultId, "Gói gia đình");
        Guid containerId = container.GetProperty("id").GetGuid();
        Assert.True((await ApiTestHelpers.AddAssetToContainerAsync(vault.Client, vault.VaultId, containerId, first)).IsSuccessStatusCode);
        Assert.True((await ApiTestHelpers.AddAssetToContainerAsync(vault.Client, vault.VaultId, containerId, second)).IsSuccessStatusCode);
        Assert.True((await ApiTestHelpers.AddBeneficiaryToContainerAsync(
            vault.Client, vault.VaultId, containerId, beneficiary.GetProperty("id").GetGuid())).IsSuccessStatusCode);
        return vault;
    }

    [SqlFact]
    public async Task Executor_ListsPackagesWithRecipientsAndAssetsButNoContent()
    {
        DeathCaseTestData.OwnerVault vault = await CreateVaultWithReadyPackageAsync();
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);

        HttpResponseMessage response = await executor.Client.GetAsync($"/api/v1/executor/vaults/{vault.VaultId}/assets");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string raw = await response.Content.ReadAsStringAsync();
        JsonElement packages = JsonDocument.Parse(raw).RootElement;
        Assert.Equal(1, packages.GetArrayLength());
        JsonElement package = packages[0];
        Assert.Equal("Gói gia đình", package.GetProperty("containerName").GetString());
        Assert.Equal(1, package.GetProperty("recipients").GetArrayLength());
        Assert.Equal("0791", package.GetProperty("recipients")[0].GetProperty("citizenIdLast4").GetString());
        Assert.Equal(ApiTestHelpers.BeneficiaryName(0), package.GetProperty("recipients")[0].GetProperty("fullName").GetString());
        JsonElement assets = package.GetProperty("assets");
        Assert.Equal(2, assets.GetArrayLength());
        Assert.Equal("TEXT", assets[0].GetProperty("kind").GetString());
        // Chỉ danh sách: không có nội dung tài sản, và API không có trường nào chứa nội dung.
        Assert.DoesNotContain("Nội dung của", raw);
        Assert.False(assets[0].TryGetProperty("content", out _));
    }

    [SqlFact]
    public async Task Owner_CannotUseTheExecutorAssetList()
    {
        DeathCaseTestData.OwnerVault vault = await CreateVaultWithReadyPackageAsync();

        HttpResponseMessage response = await vault.Client.GetAsync($"/api/v1/executor/vaults/{vault.VaultId}/assets");

        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.NotFound, "VAULT_NOT_FOUND");
    }

    [SqlFact]
    public async Task Stranger_CannotListTheAssetsOfAnotherVault()
    {
        DeathCaseTestData.OwnerVault vault = await CreateVaultWithReadyPackageAsync();
        DeathCaseTestData.OwnerVault other = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient strangersExecutor = await DeathCaseTestData.CreateExecutorClientAsync(factory, other.VaultId);

        HttpResponseMessage response = await strangersExecutor.Client.GetAsync($"/api/v1/executor/vaults/{vault.VaultId}/assets");

        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.NotFound, "VAULT_NOT_FOUND");
    }

    [SqlFact]
    public async Task VaultWithoutAReadyPackage_ReturnsAnEmptyList()
    {
        DeathCaseTestData.OwnerVault vault = await DeathCaseTestData.CreateOwnerWithRunningPlanAsync(factory);
        DeathCaseTestData.ExecutorClient executor = await DeathCaseTestData.CreateExecutorClientAsync(factory, vault.VaultId);

        HttpResponseMessage response = await executor.Client.GetAsync($"/api/v1/executor/vaults/{vault.VaultId}/assets");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement packages = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal(0, packages.GetArrayLength());
    }
}
