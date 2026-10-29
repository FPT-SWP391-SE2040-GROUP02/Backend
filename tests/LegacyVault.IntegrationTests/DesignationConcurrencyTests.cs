using System.Net;
using System.Text.Json;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Infrastructure.Data;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests;

/// <summary>
/// QUOTA-05 / AC-92: thêm người nhận song song không vượt suất (docs/api-contract.md mục 6.6).
/// </summary>
/// <remarks>
/// Không có code chặn riêng: HandoverPlan.RowVersion + BumpConfigVersion ở mỗi lần gắn người nhận
/// khiến request lưu sau bị DbUpdateConcurrencyException (409 EDIT_CONFLICT). Hai test này chứng minh điều đó:
/// một test chạy song song qua API, một test tất định bằng hai DbContext riêng.
/// </remarks>
public sealed class DesignationConcurrencyTests : IAsyncLifetime
{
    private const string CitizenA = "001090000001";
    private const string CitizenB = "001090000002";
    private const string CitizenC = "001090000003";

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
    public async Task QUOTA05_TwoNewBeneficiariesAtOnce_DifferentContainers_OnlyOneSucceeds()
    {
        // Chuẩn bị: kho tối đa 2 suất; A đã ở gói 1 (còn 1 suất); B và C chưa ở gói nào.
        // Ba hồ sơ tạo khi kho tạm có 3 suất rồi giảm về 2 (như kho bị giảm suất), vì tạo hồ sơ cũng bị giới hạn theo suất.
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.VaultEntitlements.Where(e => e.VaultId == vaultId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.IncludedBeneficiarySlots, 2)
                    .SetProperty(e => e.ExtraBeneficiarySlots, 1));
        });
        Guid beneficiaryA = await CreateBeneficiaryIdAsync(client, vaultId, CitizenA, "Người A");
        Guid beneficiaryB = await CreateBeneficiaryIdAsync(client, vaultId, CitizenB, "Người B");
        Guid beneficiaryC = await CreateBeneficiaryIdAsync(client, vaultId, CitizenC, "Người C");
        await ApiTestHelpers.SetExtraBeneficiarySlotsAsync(factory, vaultId, 0);
        Guid firstContainerId = await CreateContainerIdAsync(client, vaultId, "Gói 1");
        Guid secondContainerId = await CreateContainerIdAsync(client, vaultId, "Gói 2");
        HttpResponseMessage addA = await ApiTestHelpers.AddBeneficiaryToContainerAsync(
            client, vaultId, firstContainerId, beneficiaryA);
        Assert.Equal(HttpStatusCode.OK, addA.StatusCode);

        // Hành động: B vào gói 1 và C vào gói 2 cùng lúc. Hai gói khác nhau để token Container.UpdatedAt
        // không che cơ chế HandoverPlan.RowVersion.
        Task<HttpResponseMessage> addB = ApiTestHelpers.AddBeneficiaryToContainerAsync(
            client, vaultId, firstContainerId, beneficiaryB);
        Task<HttpResponseMessage> addC = ApiTestHelpers.AddBeneficiaryToContainerAsync(
            client, vaultId, secondContainerId, beneficiaryC);
        HttpResponseMessage[] responses = await Task.WhenAll(addB, addC);

        // Kiểm tra: đúng một request thành công, request kia 409 vì xung đột hoặc hết suất.
        int succeeded = 0;
        int conflicts = 0;
        foreach (HttpResponseMessage response in responses)
        {
            if (response.StatusCode == HttpStatusCode.OK)
            {
                succeeded = succeeded + 1;
            }
            else
            {
                Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
                JsonElement problem = await ApiTestHelpers.ReadJsonAsync(response);
                string code = problem.GetProperty("code").GetString()!;
                Assert.True(
                    code == "EDIT_CONFLICT" || code == "BENEFICIARY_SLOTS_EXCEEDED",
                    $"Mã lỗi không mong đợi: {code}");
                conflicts = conflicts + 1;
            }
        }
        Assert.Equal(1, succeeded);
        Assert.Equal(1, conflicts);

        int distinctDesignated = 0;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            distinctDesignated = await db.Designations
                .Where(d => d.Container.VaultId == vaultId)
                .Select(d => d.BeneficiaryId)
                .Distinct()
                .CountAsync();
        });
        Assert.Equal(2, distinctDesignated);
    }

    [SqlFact]
    public async Task QUOTA05_PlanRowVersion_SecondWriterGetsConcurrencyError()
    {
        // Chuẩn bị: một kho có kế hoạch; hai scope AppDbContext riêng cùng đọc HandoverPlan.
        (HttpClient client, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using (IServiceScope firstScope = factory.Services.CreateScope())
        using (IServiceScope secondScope = factory.Services.CreateScope())
        {
            AppDbContext firstDb = firstScope.ServiceProvider.GetRequiredService<AppDbContext>();
            AppDbContext secondDb = secondScope.ServiceProvider.GetRequiredService<AppDbContext>();
            HandoverPlan firstPlan = await firstDb.HandoverPlans.FirstAsync(p => p.VaultId == vaultId);
            HandoverPlan secondPlan = await secondDb.HandoverPlans.FirstAsync(p => p.VaultId == vaultId);

            // Hành động: cả hai cùng tăng ConfigVersion như AddBeneficiaryAsync; scope 1 lưu trước.
            PlanRules.BumpConfigVersion(firstPlan, now);
            PlanRules.BumpConfigVersion(secondPlan, now);
            await firstDb.SaveChangesAsync();

            // Kiểm tra: scope 1 đã lưu; scope 2 giữ RowVersion cũ nên bị từ chối.
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondDb.SaveChangesAsync());
        }
    }

    private static async Task<Guid> CreateBeneficiaryIdAsync(HttpClient client, Guid vaultId, string citizenId, string fullName)
    {
        JsonElement beneficiary = await ApiTestHelpers.CreateBeneficiaryAsync(client, vaultId, citizenId, fullName);
        return beneficiary.GetProperty("id").GetGuid();
    }

    private static async Task<Guid> CreateContainerIdAsync(HttpClient client, Guid vaultId, string name)
    {
        JsonElement container = await ApiTestHelpers.CreateContainerAsync(client, vaultId, name);
        return container.GetProperty("id").GetGuid();
    }
}
