using System.Net;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.DeathCases;
using LegacyVault.IntegrationTests.TestSupport;
using Xunit;

namespace LegacyVault.IntegrationTests.Email;

/// <summary>POST /api/v1/auth/test-email chỉ dành cho Admin đang hoạt động (không cho người lạ dùng server gửi thư).</summary>
public sealed class TestEmailEndpointTests : IAsyncLifetime
{
    private const string Url = "/api/v1/auth/test-email?to=someone@example.test";
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
    public async Task AnonymousGets401_NonAdminAndLockedAdminGet404()
    {
        // Chưa đăng nhập
        HttpResponseMessage anonymous = await factory.CreateClient().PostAsync(Url, null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        // Người dùng thường, Verifier, Admin bị khóa: như endpoint không tồn tại
        User plain = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        User verifier = await DeathCaseTestData.AddVerifierAsync(factory);
        User lockedAdmin = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Locked);
        foreach (User user in new User[] { plain, verifier, lockedAdmin })
        {
            HttpClient client = factory.CreateClient();
            ApiTestHelpers.AuthorizeAs(client, factory.Services, user);
            HttpResponseMessage response = await client.PostAsync(Url, null);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
    }
}
