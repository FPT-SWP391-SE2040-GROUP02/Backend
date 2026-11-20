using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Xunit;

namespace LegacyVault.IntegrationTests.DeathCases;

/// <summary>API đếm theo trạng thái hoặc vai trò cho các thẻ lọc của Admin: cùng bộ lọc với danh sách nhưng bỏ qua nhóm đang chọn.</summary>
public sealed class AdminCountsTests : IAsyncLifetime
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

    private async Task<HttpClient> AdminAsync(User admin)
    {
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, admin);
        return await Task.FromResult(client);
    }

    private static async Task<JsonElement> CountsOf(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("counts");
    }

    [SqlFact]
    public async Task UserCounts_GroupByRole_AndRespectStatusFilter()
    {
        // Chuẩn bị: một Admin đang gọi, hai người dùng thường (một bị khóa), một Verifier
        User admin = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Locked);
        await DeathCaseTestData.AddUserAsync(factory, UserRole.Verifier, UserStatus.Active);
        HttpClient client = await AdminAsync(admin);

        // Hành động
        HttpResponseMessage all = await client.GetAsync("/api/v1/admin/users/counts");
        JsonElement allBody = await all.Content.ReadFromJsonAsync<JsonElement>();
        JsonElement locked = await CountsOf(await client.GetAsync("/api/v1/admin/users/counts?status=LOCKED"));
        HttpResponseMessage badStatus = await client.GetAsync("/api/v1/admin/users/counts?status=KHONG_CO");

        // Kiểm tra: đủ bốn vai trò, vai trò không có người vẫn là 0
        Assert.Equal(4, allBody.GetProperty("total").GetInt32());
        JsonElement counts = allBody.GetProperty("counts");
        Assert.Equal(1, counts.GetProperty("ADMIN").GetInt32());
        Assert.Equal(2, counts.GetProperty("USER").GetInt32());
        Assert.Equal(1, counts.GetProperty("VERIFIER").GetInt32());
        Assert.Equal(0, counts.GetProperty("EXECUTOR").GetInt32());
        Assert.Equal(1, locked.GetProperty("USER").GetInt32());
        Assert.Equal(0, locked.GetProperty("ADMIN").GetInt32());
        Assert.Equal(HttpStatusCode.BadRequest, badStatus.StatusCode);
    }

}
