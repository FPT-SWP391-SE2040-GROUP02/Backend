using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.DeathCases;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests.Auth;

/// <summary>
/// Chế độ bảo trì (Features.MaintenanceMode) trên API thật và SQL Server database tạm: Admin bật cờ ở trang cấu hình,
/// tài khoản thường bị chặn ở đăng nhập, làm mới phiên và mọi API khác; Admin, cờ công khai và health vẫn dùng được.
/// </summary>
public sealed class MaintenanceModeTests : IAsyncLifetime
{
    private const string SettingUrl = "/api/v1/admin/settings/Features.MaintenanceMode";

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
    public async Task MaintenanceOn_BlocksUserApi_ButAdminPublicFlagAndHealthStillWork()
    {
        HttpClient admin = await AdminClientAsync();
        HttpClient user = await ApiTestHelpers.CreateClientForNewUserAsync(factory, emailVerified: true);
        HttpClient anonymous = factory.CreateClient();

        await SetMaintenanceAsync(admin, true);

        HttpResponseMessage userApi = await user.GetAsync("/api/v1/vaults/me");
        JsonElement problem = await ApiTestHelpers.AssertProblemAsync(
            userApi, HttpStatusCode.ServiceUnavailable, "MAINTENANCE_MODE");
        Assert.False(string.IsNullOrWhiteSpace(problem.GetProperty("title").GetString()));

        HttpResponseMessage register = await anonymous.PostAsJsonAsync("/api/v1/auth/register", new
        {
            email = $"{Guid.NewGuid():N}@test.local",
            password = "Mat-khau-moi-1!",
            confirmPassword = "Mat-khau-moi-1!",
            fullName = "Người mới",
        });
        await ApiTestHelpers.AssertProblemAsync(register, HttpStatusCode.ServiceUnavailable, "MAINTENANCE_MODE");

        HttpResponseMessage adminApi = await admin.GetAsync("/api/v1/admin/settings");
        Assert.Equal(HttpStatusCode.OK, adminApi.StatusCode);

        HttpResponseMessage publicFlags = await anonymous.GetAsync("/api/v1/settings/public");
        Assert.Equal(HttpStatusCode.OK, publicFlags.StatusCode);
        JsonElement flags = await ApiTestHelpers.ReadJsonAsync(publicFlags);
        Assert.True(flags.GetProperty("Features.MaintenanceMode").GetBoolean());

        HttpResponseMessage live = await anonymous.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, live.StatusCode);

        // Tắt bảo trì thì tài khoản thường dùng lại được ngay (Admin lưu là xóa bộ đệm cấu hình).
        await SetMaintenanceAsync(admin, false);
        HttpResponseMessage afterOff = await user.GetAsync("/api/v1/vaults/me");
        Assert.NotEqual(HttpStatusCode.ServiceUnavailable, afterOff.StatusCode);
    }

    [SqlFact]
    public async Task MaintenanceOn_OnlyAdminCanLogIn()
    {
        User admin = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        User owner = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        HttpClient adminApi = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(adminApi, factory.Services, admin);
        await SetMaintenanceAsync(adminApi, true);

        HttpResponseMessage ownerLogin = await LoginAsync(SecureClient(), owner.Email);
        await ApiTestHelpers.AssertProblemAsync(ownerLogin, HttpStatusCode.ServiceUnavailable, "MAINTENANCE_MODE");
        Assert.False(HasRefreshCookie(ownerLogin));
        int ownerSessions = await CountSessionsAsync(owner.Id);
        Assert.Equal(0, ownerSessions);

        HttpResponseMessage adminLogin = await LoginAsync(SecureClient(), admin.Email);
        Assert.Equal(HttpStatusCode.OK, adminLogin.StatusCode);
        JsonElement session = await ApiTestHelpers.ReadJsonAsync(adminLogin);
        Assert.Equal("ADMIN", session.GetProperty("user").GetProperty("role").GetString());
        Assert.True(HasRefreshCookie(adminLogin));
    }

    [SqlFact]
    public async Task MaintenanceOn_RefreshIsRefused_ButSessionSurvivesUntilMaintenanceEnds()
    {
        HttpClient admin = await AdminClientAsync();
        User owner = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        HttpClient browser = SecureClient();

        HttpResponseMessage login = await LoginAsync(browser, owner.Email);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        await SetMaintenanceAsync(admin, true);
        HttpResponseMessage blocked = await RefreshAsync(browser);
        await ApiTestHelpers.AssertProblemAsync(blocked, HttpStatusCode.ServiceUnavailable, "MAINTENANCE_MODE");
        Assert.False(HasRefreshCookie(blocked));

        // Refresh token cũ không bị xoay hay thu hồi nên hết bảo trì là dùng tiếp, không phải đăng nhập lại.
        await SetMaintenanceAsync(admin, false);
        HttpResponseMessage resumed = await RefreshAsync(browser);
        Assert.Equal(HttpStatusCode.OK, resumed.StatusCode);
        Assert.True(HasRefreshCookie(resumed));
    }

    private async Task<HttpClient> AdminClientAsync()
    {
        User admin = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, admin);
        return client;
    }

    /// <summary>Bật hoặc tắt bảo trì qua chính API Admin (đọc danh sách trước để các tham số mặc định được tạo).</summary>
    private static async Task SetMaintenanceAsync(HttpClient admin, bool on)
    {
        HttpResponseMessage list = await admin.GetAsync("/api/v1/admin/settings");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        string value = "false";
        if (on)
        {
            value = "true";
        }
        HttpResponseMessage update = await admin.PutAsJsonAsync(SettingUrl, new
        {
            value,
            isEnabled = true,
            reason = "Kiểm thử chế độ bảo trì",
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
    }

    /// <summary>Client giữ cookie qua https để cookie refresh (có thể là Secure) được gửi lại như trình duyệt.</summary>
    private HttpClient SecureClient()
    {
        WebApplicationFactoryClientOptions options = new WebApplicationFactoryClientOptions();
        options.BaseAddress = new Uri("https://localhost");
        options.HandleCookies = true;
        return factory.CreateClient(options);
    }

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email)
    {
        string proof = await CsrfAsync(client);
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login");
        request.Headers.Add("X-CSRF-Token", proof);
        request.Content = JsonContent.Create(new { email, password = DeathCaseTestData.TestPassword });
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> RefreshAsync(HttpClient client)
    {
        string proof = await CsrfAsync(client);
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/refresh");
        request.Headers.Add("X-CSRF-Token", proof);
        return await client.SendAsync(request);
    }

    private static async Task<string> CsrfAsync(HttpClient client)
    {
        JsonElement proof = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        string? token = proof.GetProperty("requestToken").GetString();
        Assert.False(string.IsNullOrEmpty(token));
        return token!;
    }

    private static bool HasRefreshCookie(HttpResponseMessage response)
    {
        IEnumerable<string>? cookies;
        if (!response.Headers.TryGetValues("Set-Cookie", out cookies))
        {
            return false;
        }
        foreach (string cookie in cookies)
        {
            if (cookie.StartsWith("lv_refresh=", StringComparison.Ordinal) && !cookie.StartsWith("lv_refresh=;", StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private async Task<int> CountSessionsAsync(Guid userId)
    {
        int count = 0;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            count = await db.RefreshTokens.CountAsync(t => t.UserId == userId);
        });
        return count;
    }
}
