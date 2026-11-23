using System.Net;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Xunit;

namespace LegacyVault.IntegrationTests.Audit;

/// <summary>
/// Nhật ký hoạt động khi Owner gán hoặc gỡ gán tài sản cho người nhận (SEC-13, SETUP-04), trên SQL Server thật (database tạm).
/// </summary>
public sealed class AuditAssignmentFlowTests : IAsyncLifetime
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
    public async Task Setup04_AssignAndUnassign_LogActionWithNewConfigVersion()
    {
        // Chuẩn bị: kho XS có một tài sản và hai hồ sơ người nhận
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Sổ tiết kiệm");
        Guid a = (await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, "001090000001", "Nguyễn Văn A")).GetProperty("id").GetGuid();
        Guid b = (await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, "001090000002", "Trần Thị B")).GetProperty("id").GetGuid();

        // Hành động: gán cho A, đổi sang B (A rời, B vào), gỡ B
        await ApiTestHelpers.AssignAssetOkAsync(client, vaultId, assetId, a);
        await ApiTestHelpers.AssignAssetOkAsync(client, vaultId, assetId, b);
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.UnassignAssetAsync(client, vaultId, assetId, b)).StatusCode);

        // Kiểm tra: bốn dòng (gán A, gỡ A + gán B, gỡ B), đều trỏ tới tài sản, config_version không giảm
        // và khớp phiên bản hiện tại ở dòng cuối
        List<AuditLog> logs = AssignmentLogs(await AuditTestData.ListForVaultAsync(factory, vaultId));
        Assert.Equal(4, logs.Count);
        foreach (AuditLog log in logs)
        {
            Assert.Equal(AuditTargetType.Asset, log.TargetType);
            Assert.Equal(assetId, log.TargetId);
            Assert.Equal(AuditActorRole.Owner, log.ActorRole);
        }
        Assert.Equal(AuditAction.AssetAssigned, logs[0].Action);
        for (int i = 1; i < logs.Count; i++)
        {
            Assert.True(logs[i].TargetVersion >= logs[i - 1].TargetVersion);
        }
        int currentVersion = await ApiTestHelpers.GetPlanConfigVersionAsync(client);
        Assert.Equal(currentVersion, logs[logs.Count - 1].TargetVersion);
    }

    [SqlFact]
    public async Task Sec07_Notes_HoldIdsOnly()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Ví tiền điện tử");
        Guid beneficiaryId = (await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, "001090000002", "Trần Thị B")).GetProperty("id").GetGuid();

        // Hành động
        await ApiTestHelpers.AssignAssetOkAsync(client, vaultId, assetId, beneficiaryId);

        // Kiểm tra: ghi id người nhận, không ghi tên tài sản, tên người nhận hay số CCCD
        AuditLog log = Assert.Single(await AuditTestData.ListForVaultAsync(factory, vaultId, AuditAction.AssetAssigned));
        Assert.Contains(beneficiaryId.ToString(), log.Reason);
        Assert.DoesNotContain("Ví tiền điện tử", log.Reason);
        Assert.DoesNotContain("Trần Thị B", log.Reason);
        Assert.DoesNotContain("001090000002", log.Reason);
    }

    [SqlFact]
    public async Task Sec13_RepeatedAssign_LogsOnce()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Ghi chú");
        Guid a = (await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, "001090000001", "Nguyễn Văn A")).GetProperty("id").GetGuid();

        // Hành động: bấm gán hai lần (lần hai không đổi gì)
        await ApiTestHelpers.AssignAssetOkAsync(client, vaultId, assetId, a);
        await ApiTestHelpers.AssignAssetOkAsync(client, vaultId, assetId, a);

        // Kiểm tra
        Assert.Single(await AuditTestData.ListForVaultAsync(factory, vaultId, AuditAction.AssetAssigned));
    }

    [SqlFact]
    public async Task Setup16_ConfirmedLosingLastAssignment_LogsPlanDeactivated()
    {
        // Chuẩn bị: kế hoạch đang chạy với đúng một dòng gán
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(client, vaultId, "Giấy tờ nhà");
        Guid a = (await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, "001090000003", "Lê Văn C")).GetProperty("id").GetGuid();
        await ApiTestHelpers.AssignAssetOkAsync(client, vaultId, assetId, a);
        await ApiTestHelpers.SetPlanStatusAsync(factory, vaultId, PlanStatus.Active);

        // Hành động: lần đầu chưa xác nhận (409, không ghi gì), lần hai xác nhận
        HttpResponseMessage unconfirmed = await ApiTestHelpers.UnassignAssetAsync(client, vaultId, assetId, a);
        await ApiTestHelpers.AssertProblemAsync(
            unconfirmed, HttpStatusCode.Conflict, "PLAN_DEACTIVATION_CONFIRMATION_REQUIRED");
        Assert.Empty(await AuditTestData.ListForVaultAsync(factory, vaultId, AuditAction.PlanDeactivated));
        HttpResponseMessage confirmed = await ApiTestHelpers.UnassignAssetAsync(client, vaultId, assetId, a, confirmPlanDeactivation: true);
        Assert.Equal(HttpStatusCode.OK, confirmed.StatusCode);

        // Kiểm tra: một dòng PlanDeactivated trỏ tới kế hoạch, cùng dòng gỡ gán
        AuditLog deactivated = Assert.Single(
            await AuditTestData.ListForVaultAsync(factory, vaultId, AuditAction.PlanDeactivated));
        Assert.Equal(AuditTargetType.Plan, deactivated.TargetType);
        Assert.Single(await AuditTestData.ListForVaultAsync(factory, vaultId, AuditAction.AssetUnassigned));
    }

    private static List<AuditLog> AssignmentLogs(List<AuditLog> logs)
    {
        List<AuditLog> result = new List<AuditLog>();
        foreach (AuditLog log in logs)
        {
            if (log.Action == AuditAction.AssetAssigned || log.Action == AuditAction.AssetUnassigned)
            {
                result.Add(log);
            }
        }
        return result;
    }
}
