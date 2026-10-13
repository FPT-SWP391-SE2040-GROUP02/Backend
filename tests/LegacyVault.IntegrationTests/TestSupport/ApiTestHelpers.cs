using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LegacyVault.IntegrationTests.TestSupport;

/// <summary>Hàm dùng chung cho test gọi API: tạo user thử, đọc JSON, kiểm ProblemDetails, sửa DB trực tiếp.</summary>
public static class ApiTestHelpers
{
    /// <summary>Email của hồ sơ người nhận tạo bằng CreateBeneficiaryAsync.</summary>
    public const string BeneficiaryEmail = "nguoinhan@example.com";

    /// <summary>Thêm một user Active vào DB và trả HttpClient đã gắn Bearer token của user đó.</summary>
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
        AuthorizeAs(client, factory.Services, user);
        return client;
    }

    /// <summary>
    /// Tạo access token JWT cho user bằng ITokenService của chính API đang test (cùng khóa ký),
    /// rồi gắn header Authorization: Bearer cho client. Không đi qua luồng đăng nhập.
    /// </summary>
    public static void AuthorizeAs(HttpClient client, IServiceProvider services, User user)
    {
        ITokenService tokenService = services.GetRequiredService<ITokenService>();
        AccessToken token = tokenService.CreateAccessToken(user);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Value);
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

    /// <summary>Tạo tài sản dạng chữ qua API (POST .../assets/text), trả id tài sản.</summary>
    public static async Task<Guid> CreateTextAssetAsync(HttpClient client, Guid vaultId, string name)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/vaults/{vaultId}/assets/text", new { name, content = $"Nội dung của {name}" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement asset = await ReadJsonAsync(response);
        return asset.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Nâng kho lên hạng XS đang trả phí bằng cách sửa thẳng VaultEntitlements (chưa có module thanh toán):
    /// 200 MiB, 2 suất người nhận.
    /// </summary>
    public static async Task UpgradeVaultToXsAsync(ApiFactory factory, Guid vaultId)
    {
        await WithDbAsync(factory, async db =>
        {
            await db.VaultEntitlements.Where(e => e.VaultId == vaultId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(e => e.Tier, ServiceTier.Xs)
                    .SetProperty(e => e.Status, EntitlementStatus.PaidActive)
                    .SetProperty(e => e.StorageQuotaBytes, 200 * 1_048_576L)
                    .SetProperty(e => e.IncludedBeneficiarySlots, 2));
        });
    }

    /// <summary>Đổi trạng thái dịch vụ của kho bằng cách sửa thẳng VaultEntitlements (ví dụ GraceReadOnly).</summary>
    public static async Task SetEntitlementStatusAsync(ApiFactory factory, Guid vaultId, EntitlementStatus status)
    {
        await WithDbAsync(factory, async db =>
        {
            await db.VaultEntitlements.Where(e => e.VaultId == vaultId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.Status, status));
        });
    }

    /// <summary>
    /// Tạo hồ sơ người nhận người lớn qua API (POST .../beneficiaries, phải là hồ sơ mới, 201), trả JSON hồ sơ.
    /// Kho phải đang trả phí (UpgradeVaultToXsAsync).
    /// </summary>
    public static async Task<JsonElement> CreateBeneficiaryAsync(
        HttpClient client, Guid vaultId, string citizenId, string fullName)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/v1/vaults/{vaultId}/beneficiaries",
            new { citizenId, fullName, dateOfBirth = "1990-05-20", email = BeneficiaryEmail });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    /// <summary>"Tạo kho của tôi" qua API (phải là lần tạo đầu, 201), trả id kho.</summary>
    public static async Task<Guid> CreateVaultAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsync("/api/v1/vaults", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement vault = await ReadJsonAsync(response);
        return vault.GetProperty("id").GetGuid();
    }

    /// <summary>planConfigVersion của kho của user đang gắn token.</summary>
    public static async Task<int> GetPlanConfigVersionAsync(HttpClient client)
    {
        JsonElement vault = await ReadJsonAsync(await client.GetAsync("/api/v1/vaults/me"));
        return vault.GetProperty("planConfigVersion").GetInt32();
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
