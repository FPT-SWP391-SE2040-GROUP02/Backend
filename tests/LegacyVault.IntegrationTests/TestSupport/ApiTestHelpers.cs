using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.TestSupport;

/// <summary>Hàm dùng chung cho test gọi API: tạo user thử, đọc JSON, kiểm ProblemDetails, sửa DB trực tiếp.</summary>
public static class ApiTestHelpers
{
    /// <summary>Header đăng nhập giả của môi trường Development (DevHeaderAuthenticationHandler).</summary>
    public const string DevUserHeader = "X-Dev-User-Id";

    /// <summary>Thêm một user Active vào DB và trả HttpClient đã gắn header đăng nhập của user đó.</summary>
    public static async Task<HttpClient> CreateClientForNewUserAsync(ApiFactory factory, bool emailVerified)
    {
        User user = new User();
        user.Email = $"{Guid.NewGuid():N}@test.local";
        user.FullName = "Người thử";
        user.Role = UserRole.User;
        user.Status = UserStatus.Active;
        user.CreatedAt = DateTimeOffset.UtcNow;
        if (emailVerified)
        {
            user.EmailVerifiedAt = DateTimeOffset.UtcNow;
        }

        await WithDbAsync(factory, async db =>
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        });

        HttpClient client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevUserHeader, user.Id.ToString());
        return client;
    }

    /// <summary>Chạy một đoạn code với AppDbContext riêng (scope riêng), dùng để chuẩn bị dữ liệu hoặc kiểm DB.</summary>
    public static async Task WithDbAsync(ApiFactory factory, Func<AppDbContext, Task> action)
    {
        using (IServiceScope scope = factory.Services.CreateScope())
        {
            AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await action(db);
        }
    }

    public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        JsonElement json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.Clone();
    }

    /// <summary>Kiểm status, Content-Type application/problem+json và trường code; trả ProblemDetails để kiểm tiếp.</summary>
    public static async Task<JsonElement> AssertProblemAsync(
        HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        MediaTypeHeaderValue? contentType = response.Content.Headers.ContentType;
        Assert.NotNull(contentType);
        Assert.Equal("application/problem+json", contentType.MediaType);

        JsonElement problem = await ReadJsonAsync(response);
        Assert.Equal(code, problem.GetProperty("code").GetString());
        return problem;
    }
}
