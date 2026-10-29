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
    public static async Task<HttpClient> CreateClientForNewUserAsync(ApiFactory factory, bool emailVerified, string? email = null)
    {
        User user = new User();
        user.Email = email ?? $"{Guid.NewGuid():N}@test.local";
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

    /// <summary>
    /// Tên người nhận theo thứ tự (0 → "Người nhận A", 1 → "Người nhận B"...).
    /// Họ tên chỉ được chứa chữ cái và khoảng trắng nên không ghép số vào tên.
    /// </summary>
    public static string BeneficiaryName(int index)
    {
        char letter = (char)('A' + index);
        return "Người nhận " + letter;
    }

    /// <summary>Tạo gói qua API (POST .../containers, 201), trả JSON gói.</summary>
    public static async Task<JsonElement> CreateContainerAsync(HttpClient client, Guid vaultId, string name)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/containers", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadJsonAsync(response);
    }

    /// <summary>Đưa tài sản vào gói (POST .../containers/{containerId}/assets); trả response để test tự kiểm.</summary>
    public static Task<HttpResponseMessage> AddAssetToContainerAsync(
        HttpClient client, Guid vaultId, Guid containerId, Guid assetId)
    {
        return client.PostAsJsonAsync($"/api/v1/vaults/{vaultId}/containers/{containerId}/assets", new { assetId });
    }

    /// <summary>Chỉ định người nhận cho gói (POST .../containers/{containerId}/beneficiaries); trả response để test tự kiểm.</summary>
    public static Task<HttpResponseMessage> AddBeneficiaryToContainerAsync(
        HttpClient client, Guid vaultId, Guid containerId, Guid beneficiaryId)
    {
        return client.PostAsJsonAsync(
            $"/api/v1/vaults/{vaultId}/containers/{containerId}/beneficiaries", new { beneficiaryId });
    }

    /// <summary>Đổi trạng thái kế hoạch bằng cách sửa thẳng HandoverPlans (chưa có luồng kích hoạt kế hoạch).</summary>
    public static async Task SetPlanStatusAsync(ApiFactory factory, Guid vaultId, PlanStatus status)
    {
        await WithDbAsync(factory, async db =>
        {
            await db.HandoverPlans.Where(p => p.VaultId == vaultId)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.Status, status));
        });
    }

    /// <summary>Đặt số suất người nhận mua thêm bằng cách sửa thẳng VaultEntitlements (chưa có module thanh toán).</summary>
    public static async Task SetExtraBeneficiarySlotsAsync(ApiFactory factory, Guid vaultId, int extraSlots)
    {
        await WithDbAsync(factory, async db =>
        {
            await db.VaultEntitlements.Where(e => e.VaultId == vaultId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.ExtraBeneficiarySlots, extraSlots));
        });
    }

    /// <summary>User mới (đã xác minh email) có kho đã nâng lên XS (2 suất người nhận).</summary>
    public static async Task<(HttpClient Client, Guid VaultId)> CreateClientWithPaidVaultAsync(ApiFactory factory)
    {
        HttpClient client = await CreateClientForNewUserAsync(factory, emailVerified: true);
        Guid vaultId = await CreateVaultAsync(client);
        await UpgradeVaultToXsAsync(factory, vaultId);
        return (client, vaultId);
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
