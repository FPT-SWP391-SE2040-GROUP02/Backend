using System.Net;
using System.Text.Json;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.DeathCases;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.Will;

/// <summary>GET /vaults/{id}/will/identity: danh tính đầy đủ chỉ cho Chủ kho, có nhật ký, không cache (SQL Server thật).</summary>
public sealed class WillIdentityTests : IAsyncLifetime
{
    private const string OwnerCitizenId = "012345678901";
    private const string OwnerFullName = "Nguyen Van Chu Kho";
    private const string OwnerAddress = "12 Duong Thu Nghiem, Phuong 3, Quan 1";
    private const string RecipientCitizenId = "000000001015";

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

    private async Task<(HttpClient Owner, Guid VaultId, Guid OwnerUserId, Guid BeneficiaryId)> PrepareAsync(bool withIdentity)
    {
        (HttpClient owner, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        JsonElement beneficiary = await ApiTestHelpers.CreateBeneficiaryAsync(owner, vaultId, RecipientCitizenId, "Người nhận");
        Guid beneficiaryId = beneficiary.GetProperty("id").GetGuid();
        Guid ownerUserId = Guid.Empty;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            Vault vault = await db.Vaults.SingleAsync(v => v.Id == vaultId);
            ownerUserId = vault.OwnerId;
        });
        if (withIdentity)
        {
            await ApiTestHelpers.WithDbAsync(factory, async db =>
            {
                IOwnerIdentityProtector protector = factory.Services.GetRequiredService<IOwnerIdentityProtector>();
                OwnerIdentity identity = new OwnerIdentity();
                identity.UserId = ownerUserId;
                identity.SourceVerificationId = Guid.NewGuid();
                identity.CreatedAt = DateTimeOffset.UtcNow;
                OwnerIdentityData data = new OwnerIdentityData();
                data.CitizenId = OwnerCitizenId;
                data.FullName = OwnerFullName;
                data.DateOfBirth = new DateOnly(1970, 3, 9);
                data.ResidenceAddress = OwnerAddress;
                protector.Protect(identity, data);
                db.Set<OwnerIdentity>().Add(identity);
                await db.SaveChangesAsync();
            });
        }
        return (owner, vaultId, ownerUserId, beneficiaryId);
    }

    private async Task<List<AuditLog>> ReadAuditAsync(Guid vaultId, AuditAction action)
    {
        List<AuditLog> logs = new List<AuditLog>();
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            logs = await db.Set<AuditLog>().Where(l => l.VaultId == vaultId && l.Action == action).ToListAsync();
        });
        return logs;
    }

    [SqlFact]
    public async Task Owner_GetsFullIdentityWithoutCaching_AndTheViewIsAudited()
    {
        (HttpClient owner, Guid vaultId, Guid ownerUserId, Guid beneficiaryId) = await PrepareAsync(true);

        HttpResponseMessage response = await owner.GetAsync($"/api/v1/vaults/{vaultId}/will/identity");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string cacheControl = response.Headers.CacheControl?.ToString() ?? string.Empty;
        Assert.Contains("no-store", cacheControl);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);
        JsonElement ownerJson = body.GetProperty("owner");
        Assert.Equal(OwnerFullName, ownerJson.GetProperty("fullName").GetString());
        Assert.Equal("1970-03-09", ownerJson.GetProperty("dateOfBirth").GetString());
        Assert.Equal(OwnerCitizenId, ownerJson.GetProperty("citizenId").GetString());
        Assert.Equal(OwnerAddress, ownerJson.GetProperty("residenceAddress").GetString());
        JsonElement recipients = body.GetProperty("recipients");
        Assert.Equal(1, recipients.GetArrayLength());
        Assert.Equal(beneficiaryId, recipients[0].GetProperty("beneficiaryId").GetGuid());
        Assert.Equal("Người nhận", recipients[0].GetProperty("fullName").GetString());
        Assert.Equal("1990-05-20", recipients[0].GetProperty("dateOfBirth").GetString());
        Assert.Equal(RecipientCitizenId, recipients[0].GetProperty("citizenId").GetString());

        AuditLog log = Assert.Single(await ReadAuditAsync(vaultId, AuditAction.WillIdentityViewed));
        Assert.Equal(ownerUserId, log.ActorUserId);
        Assert.Equal(AuditActorRole.Owner, log.ActorRole);
        Assert.Equal(AuditOutcome.Succeeded, log.Outcome);
        Assert.Null(log.Reason);

        // Nhật ký không chứa họ tên, CCCD hay nơi cư trú đã giải mã.
        string everything = string.Empty;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            List<AuditLog> all = await db.Set<AuditLog>().Where(l => l.VaultId == vaultId).ToListAsync();
            everything = JsonSerializer.Serialize(all);
        });
        Assert.DoesNotContain(OwnerCitizenId, everything);
        Assert.DoesNotContain(RecipientCitizenId, everything);
        Assert.DoesNotContain(OwnerFullName, everything);
        Assert.DoesNotContain(OwnerAddress, everything);

        // Mỗi lần gọi thành công ghi một dòng.
        HttpResponseMessage second = await owner.GetAsync($"/api/v1/vaults/{vaultId}/will/identity");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(2, (await ReadAuditAsync(vaultId, AuditAction.WillIdentityViewed)).Count);
    }

    [SqlFact]
    public async Task Owner_WithoutEkycIdentity_GetsNullOwnerAndStillAuditedView()
    {
        (HttpClient owner, Guid vaultId, _, _) = await PrepareAsync(false);

        HttpResponseMessage response = await owner.GetAsync($"/api/v1/vaults/{vaultId}/will/identity");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await ApiTestHelpers.ReadJsonAsync(response);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("owner").ValueKind);
        Assert.Equal(1, body.GetProperty("recipients").GetArrayLength());
        Assert.Single(await ReadAuditAsync(vaultId, AuditAction.WillIdentityViewed));
    }

    [SqlFact]
    public async Task StrangerAdminAndExecutor_AreNotFoundAndNothingIsAudited()
    {
        (HttpClient owner, Guid vaultId, _, _) = await PrepareAsync(true);
        Assert.NotNull(owner);

        HttpClient stranger = await ApiTestHelpers.CreateClientForNewUserAsync(factory, true);
        User admin = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        HttpClient adminClient = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(adminClient, factory.Services, admin);
        User executorUser = await DeathCaseTestData.AddUserAsync(factory, UserRole.Executor, UserStatus.Active);
        HttpClient executorClient = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(executorClient, factory.Services, executorUser);

        foreach (HttpClient other in new List<HttpClient> { stranger, adminClient, executorClient })
        {
            HttpResponseMessage response = await other.GetAsync($"/api/v1/vaults/{vaultId}/will/identity");
            await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.NotFound, "VAULT_NOT_FOUND");
        }
        Assert.Empty(await ReadAuditAsync(vaultId, AuditAction.WillIdentityViewed));
    }

    [SqlFact]
    public async Task Anonymous_IsUnauthorized()
    {
        (_, Guid vaultId, _, _) = await PrepareAsync(false);
        HttpClient anonymous = factory.CreateClient();

        HttpResponseMessage response = await anonymous.GetAsync($"/api/v1/vaults/{vaultId}/will/identity");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
