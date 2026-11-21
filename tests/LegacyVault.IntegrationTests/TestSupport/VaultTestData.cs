using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace LegacyVault.IntegrationTests.TestSupport;

/// <summary>
/// Dữ liệu thử dùng chung: tạo user, kho có kế hoạch đang chạy, luật sư, Người thực thi.
/// Chèn thẳng DB qua ApiTestHelpers.WithDbAsync (không sửa ApiTestHelpers).
/// </summary>
public static class VaultTestData
{
    /// <summary>DEATH-03: mật khẩu thử cố định của mọi tài khoản tạo bằng AddUserAsync (chỉ dùng trong test).</summary>
    public const string TestPassword = "Mat-khau-thu-1!";

    /// <summary>Thêm một tài khoản đã xác minh email với vai trò và trạng thái cho trước, mật khẩu = TestPassword.</summary>
    public static async Task<User> AddUserAsync(ApiFactory factory, UserRole role, UserStatus status)
    {
        IPasswordHasher passwordHasher = factory.Services.GetRequiredService<IPasswordHasher>();

        User user = new User();
        user.Email = $"{Guid.NewGuid():N}@test.local";
        user.FullName = "Người thử";
        user.Role = role;
        user.Status = status;
        user.CreatedAt = DateTimeOffset.UtcNow;
        user.EmailVerifiedAt = DateTimeOffset.UtcNow;
        user.PasswordHash = passwordHasher.Hash(TestPassword);

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        });
        return user;
    }

    /// <summary>Thêm một Verifier đang hoạt động.</summary>
    public static Task<User> AddVerifierAsync(ApiFactory factory)
    {
        return AddUserAsync(factory, UserRole.Verifier, UserStatus.Active);
    }

    /// <summary>
    /// Owner mới tạo kho qua API rồi đặt kế hoạch sang ACTIVE (chưa có luồng kích hoạt kế hoạch).
    /// Trả Owner, client đã gắn token của Owner, Id kho và Id kế hoạch.
    /// </summary>
    public static async Task<OwnerVault> CreateOwnerWithRunningPlanAsync(ApiFactory factory)
    {
        User owner = await AddUserAsync(factory, UserRole.User, UserStatus.Active);
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, owner);
        Guid vaultId = await ApiTestHelpers.CreateVaultAsync(client);
        await ApiTestHelpers.SetPlanStatusAsync(factory, vaultId, PlanStatus.Active);

        Guid planId = Guid.Empty;
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            planId = await db.HandoverPlans.Where(p => p.VaultId == vaultId).Select(p => p.Id).SingleAsync();
        });

        OwnerVault result = new OwnerVault();
        result.Owner = owner;
        result.Client = client;
        result.VaultId = vaultId;
        result.PlanId = planId;
        return result;
    }

    /// <summary>
    /// Chèn thẳng một dòng Executors cho tài khoản userId (chưa dùng luồng mời của phần thiết lập bàn giao).
    /// status Accepted = Executor đang hiệu lực.
    /// </summary>
    public static async Task<Executor> AddExecutorAsync(
        ApiFactory factory, Guid vaultId, Guid userId, ExecutorStatus status)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Executor executor = new Executor();
        executor.VaultId = vaultId;
        executor.UserId = userId;
        executor.InviteeEmail = $"{Guid.NewGuid():N}@test.local";
        executor.Status = status;
        executor.InvitationTokenHash = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        executor.InvitedAt = now.AddDays(-1);
        executor.ExpiresAt = now.AddDays(6);
        if (status == ExecutorStatus.Accepted)
        {
            executor.AcceptedAt = now;
        }

        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            db.Set<Executor>().Add(executor);
            await db.SaveChangesAsync();
        });
        return executor;
    }

    /// <summary>Executor đã chấp nhận (đang hiệu lực) của kho.</summary>
    public static Task<Executor> AddAcceptedExecutorAsync(
        ApiFactory factory, Guid vaultId, Guid userId)
    {
        return AddExecutorAsync(factory, vaultId, userId, ExecutorStatus.Accepted);
    }

    /// <summary>Tài khoản mới là Executor đang hiệu lực của kho, kèm client đã gắn token của tài khoản đó.</summary>
    public static async Task<ExecutorClient> CreateExecutorClientAsync(
        ApiFactory factory, Guid vaultId)
    {
        User user = await AddUserAsync(factory, UserRole.User, UserStatus.Active);
        await AddAcceptedExecutorAsync(factory, vaultId, user.Id);
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, user);

        ExecutorClient result = new ExecutorClient();
        result.User = user;
        result.Client = client;
        return result;
    }

    /// <summary>Tài khoản thử và client đã gắn token của tài khoản đó.</summary>
    public sealed class ExecutorClient
    {
        public User User { get; set; } = null!;

        public HttpClient Client { get; set; } = null!;
    }

    /// <summary>Owner và kho vừa tạo.</summary>
    public sealed class OwnerVault
    {
        public User Owner { get; set; } = null!;

        public HttpClient Client { get; set; } = null!;

        public Guid VaultId { get; set; }

        public Guid PlanId { get; set; }
    }
}
