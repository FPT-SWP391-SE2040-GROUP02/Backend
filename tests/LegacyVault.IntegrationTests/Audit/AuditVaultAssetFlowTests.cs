using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Xunit;

namespace LegacyVault.IntegrationTests.Audit;

/// <summary>
/// Nhật ký hoạt động khi Owner tạo/sửa kho và tải tài sản lên (SEC-07, SEC-13), trên SQL Server thật (database tạm).
/// </summary>
public sealed class AuditVaultAssetFlowTests : IAsyncLifetime
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

    [SqlFact]
    public async Task Sec13_CreateVaultTwice_LogsOnlyTheRealCreation()
    {
        // Chuẩn bị
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);

        // Hành động: tạo kho, rồi bấm "Tạo kho" lần nữa (mở lại kho cũ, SETUP-12)
        Guid vaultId = await ApiTestHelpers.CreateVaultAsync(client);
        HttpResponseMessage again = await client.PostAsync("/api/v1/vaults", null);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);

        // Kiểm tra: đúng một dòng VaultCreated của chủ kho
        Guid ownerId = await AuditTestData.GetOwnerIdAsync(factory, vaultId);
        List<AuditLog> logs = await AuditTestData.ListForVaultAsync(factory, vaultId);
        AuditLog log = Assert.Single(logs);
        Assert.Equal(AuditAction.VaultCreated, log.Action);
        Assert.Equal(AuditTargetType.Vault, log.TargetType);
        Assert.Equal(vaultId, log.TargetId);
        Assert.Equal(ownerId, log.ActorUserId);
        Assert.Equal(AuditActorRole.Owner, log.ActorRole);
        Assert.Equal(AuditOutcome.Succeeded, log.Outcome);
    }

    [SqlFact]
    public async Task Sec13_UpdateVault_LogsVaultUpdated()
    {
        // Chuẩn bị
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await ApiTestHelpers.CreateVaultAsync(client);
        string url = $"/api/v1/vaults/{vaultId}";
        JsonElement vault = await ApiTestHelpers.ReadJsonAsync(await client.GetAsync(url));
        string etag = vault.GetProperty("etag").GetString()!;

        // Hành động
        HttpResponseMessage updated = await client.PutAsJsonAsync(url, new { etag, name = "Kho gia đình" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        // Kiểm tra: tên kho mới không bị ghi vào nhật ký (SEC-07)
        List<AuditLog> logs = await AuditTestData.ListForVaultAsync(factory, vaultId, AuditAction.VaultUpdated);
        AuditLog log = Assert.Single(logs);
        Assert.Equal(vaultId, log.TargetId);
        Assert.Null(log.Reason);
    }

    [SqlFact]
    public async Task Sec13_CreateAsset_LogsAssetCreatedWithVersion()
    {
        // Chuẩn bị
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await ApiTestHelpers.CreateVaultAsync(client);

        // Hành động
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Mật khẩu ngân hàng");

        // Kiểm tra: ghi id và phiên bản tài sản, không ghi tên hay nội dung
        List<AuditLog> logs = await AuditTestData.ListForVaultAsync(factory, vaultId, AuditAction.AssetCreated);
        AuditLog log = Assert.Single(logs);
        Assert.Equal(AuditTargetType.Asset, log.TargetType);
        Assert.Equal(assetId, log.TargetId);
        Assert.Equal(1, log.TargetVersion);
        Assert.Null(log.Reason);
    }

    [SqlFact]
    public async Task State02_RejectedAsset_WritesNoLog()
    {
        // Chuẩn bị: kế hoạch đang khóa vì có hồ sơ chứng tử, Owner không được thêm tài sản (SETUP-05)
        HttpClient client = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await ApiTestHelpers.CreateVaultAsync(client);
        await ApiTestHelpers.SetPlanStatusAsync(factory, vaultId, PlanStatus.DeathCaseOpen);

        // Hành động
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/vaults/{vaultId}/assets/text", new { name = "Ghi chú", content = "Nội dung" });

        // Kiểm tra: bị từ chối thì không có dòng AssetCreated
        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Conflict, "VAULT_NOT_EDITABLE");
        List<AuditLog> logs = await AuditTestData.ListForVaultAsync(factory, vaultId, AuditAction.AssetCreated);
        Assert.Empty(logs);
    }
}
