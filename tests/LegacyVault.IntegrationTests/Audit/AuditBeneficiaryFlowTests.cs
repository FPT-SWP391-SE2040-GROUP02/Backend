using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Xunit;

namespace LegacyVault.IntegrationTests.Audit;

/// <summary>
/// Nhật ký hoạt động khi Owner thêm, sửa, xóa hồ sơ người nhận (SEC-07, SEC-13), trên SQL Server thật (database tạm).
/// </summary>
public sealed class AuditBeneficiaryFlowTests : IAsyncLifetime
{
    private const string CitizenId = "001090000001";
    private const string FullName = "Nguyễn Văn A";

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
    public async Task Sec13_CreateUpdateDelete_LogsEachStepWithoutPersonalData()
    {
        // Chuẩn bị
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);

        // Hành động: tạo, gửi lại đúng người (không đổi gì), sửa họ tên, xóa
        JsonElement beneficiary = await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, CitizenId, FullName);
        Guid beneficiaryId = beneficiary.GetProperty("id").GetGuid();
        await client.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/beneficiaries",
            new { citizenId = CitizenId, fullName = FullName, dateOfBirth = "1990-05-20", email = ApiTestHelpers.BeneficiaryEmail });
        string url = $"/api/v1/vaults/{vaultId}/beneficiaries/{beneficiaryId}";
        string etag = beneficiary.GetProperty("etag").GetString()!;
        Assert.Equal(HttpStatusCode.OK,
            (await client.PutAsJsonAsync(url, new { etag, fullName = "Nguyễn Văn An" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(url)).StatusCode);

        // Kiểm tra: ba dòng (lần gửi lại không ghi), đều trỏ tới hồ sơ, không có họ tên hay số CCCD
        List<AuditLog> logs = new List<AuditLog>();
        foreach (AuditLog log in await AuditTestData.ListForVaultAsync(factory, vaultId))
        {
            if (log.TargetType == AuditTargetType.Beneficiary)
            {
                logs.Add(log);
            }
        }
        Assert.Equal(3, logs.Count);
        Assert.Equal(AuditAction.BeneficiaryCreated, logs[0].Action);
        Assert.Equal(AuditAction.BeneficiaryUpdated, logs[1].Action);
        Assert.Equal(AuditAction.BeneficiaryDeleted, logs[2].Action);
        foreach (AuditLog log in logs)
        {
            Assert.Equal(beneficiaryId, log.TargetId);
            Assert.Null(log.Reason);
        }
        // Hồ sơ chưa ở gói nào: sửa không đổi cấu hình bàn giao nên không có config_version (QUOTA-03).
        Assert.Null(logs[1].TargetVersion);
    }

    [SqlFact]
    public async Task Setup04_UpdateDesignatedBeneficiary_LogsConfigVersion()
    {
        // Chuẩn bị: hồ sơ đang ở một gói
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        JsonElement beneficiary = await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, CitizenId, FullName);
        Guid beneficiaryId = beneficiary.GetProperty("id").GetGuid();
        Guid containerId = (await ApiTestHelpers.CreateContainerAsync(client, vaultId, "Gói A")).GetProperty("id").GetGuid();
        await ApiTestHelpers.AddBeneficiaryToContainerAsync(client, vaultId, containerId, beneficiaryId);
        string url = $"/api/v1/vaults/{vaultId}/beneficiaries/{beneficiaryId}";
        string etag = (await ApiTestHelpers.ReadJsonAsync(await client.GetAsync(url))).GetProperty("etag").GetString()!;

        // Hành động
        Assert.Equal(HttpStatusCode.OK,
            (await client.PutAsJsonAsync(url, new { etag, phone = "0901234567" })).StatusCode);

        // Kiểm tra: ghi config_version mới của kế hoạch
        AuditLog log = Assert.Single(
            await AuditTestData.ListForVaultAsync(factory, vaultId, AuditAction.BeneficiaryUpdated));
        int currentVersion = await ApiTestHelpers.GetPlanConfigVersionAsync(client);
        Assert.Equal(currentVersion, log.TargetVersion);
    }
}
