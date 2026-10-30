using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Xunit;

namespace LegacyVault.IntegrationTests.Audit;

/// <summary>
/// Nhật ký hoạt động khi Owner thao tác trên gói (SEC-13, SETUP-04, SETUP-16), trên SQL Server thật (database tạm).
/// </summary>
public sealed class AuditContainerFlowTests : IAsyncLifetime
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
    public async Task Setup04_EveryContainerChange_LogsActionWithNewConfigVersion()
    {
        // Chuẩn bị: kho XS có một tài sản và một hồ sơ người nhận
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Sổ tiết kiệm");
        JsonElement beneficiary = await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, "001090000001", "Nguyễn Văn A");
        Guid beneficiaryId = beneficiary.GetProperty("id").GetGuid();

        // Hành động: tạo, đổi tên, thêm/gỡ tài sản, thêm/gỡ người nhận, xóa gói
        JsonElement container = await ApiTestHelpers.CreateContainerAsync(client, vaultId, "Gói gia đình");
        Guid containerId = container.GetProperty("id").GetGuid();
        string url = $"/api/v1/vaults/{vaultId}/containers/{containerId}";
        string etag = container.GetProperty("etag").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(url, new { etag, name = "Gói mới" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await ApiTestHelpers.AddAssetToContainerAsync(client, vaultId, containerId, assetId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await ApiTestHelpers.AddBeneficiaryToContainerAsync(client, vaultId, containerId, beneficiaryId)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"{url}/assets/{assetId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"{url}/beneficiaries/{beneficiaryId}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync(url)).StatusCode);

        // Kiểm tra: đúng thứ tự, đều trỏ tới gói, config_version tăng dần theo từng thao tác
        List<AuditLog> logs = ContainerLogs(await AuditTestData.ListForVaultAsync(factory, vaultId));
        List<AuditAction> actions = new List<AuditAction>();
        foreach (AuditLog log in logs)
        {
            actions.Add(log.Action);
            Assert.Equal(containerId, log.TargetId);
            Assert.Equal(AuditActorRole.Owner, log.ActorRole);
        }
        List<AuditAction> expected = new List<AuditAction>
        {
            AuditAction.ContainerCreated,
            AuditAction.ContainerUpdated,
            AuditAction.ContainerAssetAdded,
            AuditAction.ContainerBeneficiaryAdded,
            AuditAction.ContainerAssetRemoved,
            AuditAction.ContainerBeneficiaryRemoved,
            AuditAction.ContainerRemoved,
        };
        Assert.Equal(expected, actions);
        for (int i = 1; i < logs.Count; i++)
        {
            Assert.True(logs[i].TargetVersion > logs[i - 1].TargetVersion);
        }
        int currentVersion = await ApiTestHelpers.GetPlanConfigVersionAsync(client);
        Assert.Equal(currentVersion, logs[logs.Count - 1].TargetVersion);
    }

    [SqlFact]
    public async Task Sec07_AssetAndBeneficiaryNotes_HoldIdsOnly()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Ví tiền điện tử");
        JsonElement beneficiary = await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, "001090000002", "Trần Thị B");
        Guid beneficiaryId = beneficiary.GetProperty("id").GetGuid();
        Guid containerId = (await ApiTestHelpers.CreateContainerAsync(client, vaultId, "Gói B")).GetProperty("id").GetGuid();

        // Hành động
        await ApiTestHelpers.AddAssetToContainerAsync(client, vaultId, containerId, assetId);
        await ApiTestHelpers.AddBeneficiaryToContainerAsync(client, vaultId, containerId, beneficiaryId);

        // Kiểm tra: ghi id, không ghi tên tài sản, tên người nhận hay số CCCD
        AuditLog assetLog = Assert.Single(
            await AuditTestData.ListForVaultAsync(factory, vaultId, AuditAction.ContainerAssetAdded));
        AuditLog beneficiaryLog = Assert.Single(
            await AuditTestData.ListForVaultAsync(factory, vaultId, AuditAction.ContainerBeneficiaryAdded));
        Assert.Contains(assetId.ToString(), assetLog.Reason);
        Assert.Contains(beneficiaryId.ToString(), beneficiaryLog.Reason);
        Assert.DoesNotContain("Ví tiền điện tử", assetLog.Reason);
        Assert.DoesNotContain("Trần Thị B", beneficiaryLog.Reason);
        Assert.DoesNotContain("001090000002", beneficiaryLog.Reason);
    }

    [SqlFact]
    public async Task Sec13_RepeatedAddAsset_LogsOnce()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Ghi chú");
        Guid containerId = (await ApiTestHelpers.CreateContainerAsync(client, vaultId, "Gói C")).GetProperty("id").GetGuid();

        // Hành động: bấm thêm cùng tài sản hai lần (lần hai không đổi gì)
        await ApiTestHelpers.AddAssetToContainerAsync(client, vaultId, containerId, assetId);
        await ApiTestHelpers.AddAssetToContainerAsync(client, vaultId, containerId, assetId);

        // Kiểm tra
        Assert.Single(await AuditTestData.ListForVaultAsync(factory, vaultId, AuditAction.ContainerAssetAdded));
    }

    [SqlFact]
    public async Task Setup16_ConfirmedLosingLastReadyContainer_LogsPlanDeactivated()
    {
        // Chuẩn bị: kế hoạch đang chạy với đúng một gói READY
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Giấy tờ nhà");
        JsonElement beneficiary = await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, "001090000003", "Lê Văn C");
        Guid containerId = (await ApiTestHelpers.CreateContainerAsync(client, vaultId, "Gói D")).GetProperty("id").GetGuid();
        await ApiTestHelpers.AddAssetToContainerAsync(client, vaultId, containerId, assetId);
        await ApiTestHelpers.AddBeneficiaryToContainerAsync(
            client, vaultId, containerId, beneficiary.GetProperty("id").GetGuid());
        await ApiTestHelpers.SetPlanStatusAsync(factory, vaultId, PlanStatus.Active);
        string url = $"/api/v1/vaults/{vaultId}/containers/{containerId}";

        // Hành động: lần đầu chưa xác nhận (409, không ghi gì), lần hai xác nhận
        HttpResponseMessage unconfirmed = await client.DeleteAsync(url);
        await ApiTestHelpers.AssertProblemAsync(
            unconfirmed, HttpStatusCode.Conflict, "PLAN_DEACTIVATION_CONFIRMATION_REQUIRED");
        Assert.Empty(await AuditTestData.ListForVaultAsync(factory, vaultId, AuditAction.PlanDeactivated));
        HttpResponseMessage confirmed = await client.DeleteAsync($"{url}?confirmPlanDeactivation=true");
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);

        // Kiểm tra: một dòng PlanDeactivated trỏ tới kế hoạch, cùng dòng xóa gói
        AuditLog deactivated = Assert.Single(
            await AuditTestData.ListForVaultAsync(factory, vaultId, AuditAction.PlanDeactivated));
        Assert.Equal(AuditTargetType.Plan, deactivated.TargetType);
        Assert.Single(await AuditTestData.ListForVaultAsync(factory, vaultId, AuditAction.ContainerRemoved));
    }

    private static List<AuditLog> ContainerLogs(List<AuditLog> logs)
    {
        List<AuditLog> result = new List<AuditLog>();
        foreach (AuditLog log in logs)
        {
            if (log.TargetType == AuditTargetType.Container)
            {
                result.Add(log);
            }
        }
        return result;
    }
}
