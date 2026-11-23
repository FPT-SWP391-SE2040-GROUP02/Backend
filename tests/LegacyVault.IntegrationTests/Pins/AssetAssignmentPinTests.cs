using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.IntegrationTests.TestSupport;
using Xunit;

namespace LegacyVault.IntegrationTests.Pins;

/// <summary>
/// Gán và gỡ gán tài sản cho người nhận đều đòi mã PIN của kho (header X-Vault-Pin), như các thao tác nhạy cảm khác.
/// Chạy với bộ kiểm PIN thật (EnforceVaultPin = true).
/// </summary>
public sealed class AssetAssignmentPinTests : IAsyncLifetime
{
    private const string GoodPin = "482913";
    private const string WrongPin = "999000";

    private ApiFactory factory = null!;

    public async Task InitializeAsync()
    {
        ApiFactory? created = ApiFactory.CreateForTempDatabase();
        if (created == null)
        {
            return;
        }
        factory = created;
        factory.EnforceVaultPin = true;
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

    /// <summary>Kho đã đặt PIN, có một tài sản và một người nhận; trả về client không gắn header PIN.</summary>
    private async Task<(HttpClient Client, Guid VaultId, Guid AssetId, Guid BeneficiaryId)> PrepareAsync()
    {
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        HttpResponseMessage pinSet = await client.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/pin", new { pin = GoodPin, confirmPin = GoodPin });
        Assert.Equal(HttpStatusCode.OK, pinSet.StatusCode);
        // Các bước chuẩn bị (tạo người nhận...) cũng đòi PIN; bỏ header trước khi trả client về cho test.
        client.DefaultRequestHeaders.Add("X-Vault-Pin", GoodPin);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Ghi chú");
        JsonElement beneficiary = await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, "001090000001", "Người A");
        client.DefaultRequestHeaders.Remove("X-Vault-Pin");
        return (client, vaultId, assetId, beneficiary.GetProperty("id").GetGuid());
    }

    [SqlFact]
    public async Task Assign_WithoutPin_IsRejectedAndWritesNothing()
    {
        (HttpClient client, Guid vaultId, Guid assetId, Guid beneficiaryId) = await PrepareAsync();

        HttpResponseMessage response = await ApiTestHelpers.AssignAssetAsync(client, vaultId, assetId, beneficiaryId);

        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Forbidden, "VAULT_PIN_REQUIRED");
        JsonElement list = await ApiTestHelpers.ReadJsonAsync(await client.GetAsync($"/api/v1/vaults/{vaultId}/assignments"));
        Assert.Equal(0, list.GetArrayLength());
    }

    [SqlFact]
    public async Task Assign_WithWrongPin_IsRejected()
    {
        (HttpClient client, Guid vaultId, Guid assetId, Guid beneficiaryId) = await PrepareAsync();
        client.DefaultRequestHeaders.Add("X-Vault-Pin", WrongPin);

        HttpResponseMessage response = await ApiTestHelpers.AssignAssetAsync(client, vaultId, assetId, beneficiaryId);

        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Forbidden, "INVALID_VAULT_PIN");
    }

    [SqlFact]
    public async Task Assign_AndUnassign_WithGoodPin_Work_ButUnassignWithoutPinIsRejected()
    {
        (HttpClient client, Guid vaultId, Guid assetId, Guid beneficiaryId) = await PrepareAsync();
        client.DefaultRequestHeaders.Add("X-Vault-Pin", GoodPin);

        HttpResponseMessage assign = await ApiTestHelpers.AssignAssetAsync(client, vaultId, assetId, beneficiaryId);
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);

        client.DefaultRequestHeaders.Remove("X-Vault-Pin");
        HttpResponseMessage withoutPin = await ApiTestHelpers.UnassignAssetAsync(client, vaultId, assetId, beneficiaryId);
        await ApiTestHelpers.AssertProblemAsync(withoutPin, HttpStatusCode.Forbidden, "VAULT_PIN_REQUIRED");

        client.DefaultRequestHeaders.Add("X-Vault-Pin", GoodPin);
        HttpResponseMessage unassign = await ApiTestHelpers.UnassignAssetAsync(client, vaultId, assetId, beneficiaryId);
        Assert.Equal(HttpStatusCode.OK, unassign.StatusCode);
    }
}
