using System.Net;
using System.Net.Http.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests.Will;

/// <summary>GET /vaults/{id}/will/identity cần PIN kho (bộ kiểm PIN thật, SQL Server thật).</summary>
public sealed class WillIdentityPinTests : IAsyncLifetime
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

    private async Task<(HttpClient Owner, Guid VaultId)> PrepareAsync()
    {
        (HttpClient owner, Guid vaultId) = await ApiTestHelpers.CreateClientWithPaidVaultAsync(factory);
        HttpResponseMessage pinSet = await owner.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/pin", new { pin = GoodPin, confirmPin = GoodPin });
        Assert.Equal(HttpStatusCode.OK, pinSet.StatusCode);
        owner.DefaultRequestHeaders.Remove("X-Vault-Pin");
        return (owner, vaultId);
    }

    private static Task<HttpResponseMessage> GetIdentityAsync(HttpClient client, Guid vaultId, string? pin)
    {
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/vaults/{vaultId}/will/identity");
        if (pin != null)
        {
            request.Headers.Add("X-Vault-Pin", pin);
        }
        return client.SendAsync(request);
    }

    private async Task<int> CountViewsAsync(Guid vaultId)
    {
        int count = 0;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            count = await db.Set<AuditLog>().CountAsync(l => l.VaultId == vaultId && l.Action == AuditAction.WillIdentityViewed);
        });
        return count;
    }

    [SqlFact]
    public async Task Identity_WithoutPin_IsRejectedAndNotAudited()
    {
        (HttpClient owner, Guid vaultId) = await PrepareAsync();

        HttpResponseMessage response = await GetIdentityAsync(owner, vaultId, null);

        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Forbidden, "VAULT_PIN_REQUIRED");
        Assert.Equal(0, await CountViewsAsync(vaultId));
    }

    [SqlFact]
    public async Task Identity_WithWrongPin_IsRejectedAndNotAudited()
    {
        (HttpClient owner, Guid vaultId) = await PrepareAsync();

        HttpResponseMessage response = await GetIdentityAsync(owner, vaultId, WrongPin);

        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Forbidden, "INVALID_VAULT_PIN");
        Assert.Equal(0, await CountViewsAsync(vaultId));
    }

    [SqlFact]
    public async Task Identity_WithGoodPin_ReturnsOkAndIsAudited()
    {
        (HttpClient owner, Guid vaultId) = await PrepareAsync();

        HttpResponseMessage response = await GetIdentityAsync(owner, vaultId, GoodPin);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, await CountViewsAsync(vaultId));
    }
}
