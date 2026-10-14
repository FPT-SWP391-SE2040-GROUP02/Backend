using System.Net;
using System.Text.Json;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests.Handover;

/// <summary>
/// Chạy thật file http/handover-dev.sql trên database tạm để chắc rằng script thử của bạn đúng:
/// sau script, token cố định dev-token-1 nhận được lượt và đủ tài sản.
/// </summary>
public sealed class HandoverDevSqlTests : IAsyncLifetime
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

    private static string FindScript()
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            string candidate = Path.Combine(directory.FullName, "http", "handover-dev.sql");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Không tìm thấy http/handover-dev.sql");
    }

    [SqlFact]
    public async Task DevScript_CreatesClaimableTurnWithSnapshot()
    {
        (HttpClient owner, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        Guid assetId = await ApiTestHelpers.CreateTextAssetAsync(owner, vaultId, "Ghi chú dev");
        Guid containerId = (await ApiTestHelpers.CreateContainerAsync(owner, vaultId, "Gói dev")).GetProperty("id").GetGuid();
        Assert.Equal(HttpStatusCode.OK, (await ApiTestHelpers.AddAssetToContainerAsync(owner, vaultId, containerId, assetId)).StatusCode);
        JsonElement beneficiary = await ApiTestHelpers.CreateBeneficiaryAsync(owner, vaultId, "001090000001", "Người nhận dev");
        Assert.Equal(HttpStatusCode.OK,
            (await ApiTestHelpers.AddBeneficiaryToContainerAsync(owner, vaultId, containerId, beneficiary.GetProperty("id").GetGuid())).StatusCode);

        string script = (await File.ReadAllTextAsync(FindScript())).Replace("<vaultId>", vaultId.ToString());
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            using SqlConnection connection = new SqlConnection(db.Database.GetConnectionString());
            await connection.OpenAsync();
            using SqlCommand command = new SqlCommand(script, connection);
            await command.ExecuteNonQueryAsync();
        });

        HttpClient receiver = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        HttpResponseMessage claim = await receiver.PostAsync("/api/v1/handover/invitations/dev-token-1/claim", null);
        Assert.Equal(HttpStatusCode.OK, claim.StatusCode);
        JsonElement turn = await ApiTestHelpers.ReadJsonAsync(claim);
        Assert.Equal("READY_TO_ACCEPT", turn.GetProperty("status").GetString());
        Assert.Equal("Gói dev", turn.GetProperty("container").GetProperty("name").GetString());
        Assert.Equal(1, turn.GetProperty("items").GetArrayLength());

        // Chạy lại script không lỗi và đưa lượt về trạng thái mời ban đầu.
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            using SqlConnection connection = new SqlConnection(db.Database.GetConnectionString());
            await connection.OpenAsync();
            using SqlCommand command = new SqlCommand(script, connection);
            await command.ExecuteNonQueryAsync();
        });
        HttpClient anonymous = factory.CreateClient();
        JsonElement invitation = await ApiTestHelpers.ReadJsonAsync(await anonymous.GetAsync("/api/v1/handover/invitations/dev-token-1"));
        Assert.True(invitation.GetProperty("valid").GetBoolean());

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            PlanStatus plan = await db.HandoverPlans.Where(p => p.VaultId == vaultId).Select(p => p.Status).SingleAsync();
            Assert.Equal(PlanStatus.HandoverInProgress, plan);
        });
    }
}
