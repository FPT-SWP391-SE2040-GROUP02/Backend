using System.Net;
using System.Net.Http.Json;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.DeathCases;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LegacyVault.IntegrationTests.Auth;

/// <summary>
/// Khóa tài khoản có hiệu lực ngay trên SQL Server thật: access token còn hạn bị chặn ở request kế tiếp,
/// refresh token bị thu hồi, mở khóa thì dùng lại được access token còn hạn.
/// </summary>
public sealed class AccountLockApiTests : IAsyncLifetime
{
    private const string GuardedUrl = "/api/v1/vaults/me";

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
    public async Task AdminLock_BlocksAccessTokenImmediately_RevokesSessions_UnlockRestores()
    {
        // Chuẩn bị: người dùng đang đăng nhập (access token còn hạn) và có hai phiên refresh
        User admin = await DeathCaseTestData.AddUserAsync(factory, UserRole.Admin, UserStatus.Active);
        User member = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        HttpClient adminClient = ClientFor(admin);
        HttpClient memberClient = ClientFor(member);
        await AddSessionAsync(member.Id);
        await AddSessionAsync(member.Id);
        HttpResponseMessage before = await memberClient.GetAsync(GuardedUrl);
        await ApiTestHelpers.AssertProblemAsync(before, HttpStatusCode.NotFound, "VAULT_NOT_FOUND");

        // Hành động 1: Admin khóa tài khoản
        HttpResponseMessage locked = await adminClient.PutAsJsonAsync($"/api/v1/admin/users/{member.Id}/status",
            new { status = "LOCKED", reason = "Nghi lộ mật khẩu" });
        Assert.Equal(HttpStatusCode.OK, locked.StatusCode);

        // Kiểm tra: access token cũ bị chặn ngay, mọi phiên refresh bị thu hồi
        HttpResponseMessage blocked = await memberClient.GetAsync(GuardedUrl);
        await ApiTestHelpers.AssertProblemAsync(blocked, HttpStatusCode.Forbidden, "ACCOUNT_NOT_ACTIVE");
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            List<RefreshToken> sessions = await db.RefreshTokens.AsNoTracking().Where(t => t.UserId == member.Id).ToListAsync();
            Assert.Equal(2, sessions.Count);
            foreach (RefreshToken session in sessions)
            {
                Assert.NotNull(session.RevokedAt);
            }
        });

        // Hành động 2: Admin mở khóa
        HttpResponseMessage unlocked = await adminClient.PutAsJsonAsync($"/api/v1/admin/users/{member.Id}/status",
            new { status = "ACTIVE", reason = "Đã xác minh chủ tài khoản" });
        Assert.Equal(HttpStatusCode.OK, unlocked.StatusCode);

        // Kiểm tra: access token còn hạn dùng lại được (phiên refresh đã thu hồi thì vẫn phải đăng nhập lại khi hết hạn)
        HttpResponseMessage after = await memberClient.GetAsync(GuardedUrl);
        await ApiTestHelpers.AssertProblemAsync(after, HttpStatusCode.NotFound, "VAULT_NOT_FOUND");
    }

    [SqlFact]
    public async Task DisabledAccount_SetDirectlyInDb_IsBlockedOnNextRequest()
    {
        User member = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Active);
        HttpClient memberClient = ClientFor(member);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            await db.Users.Where(u => u.Id == member.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.Status, UserStatus.Disabled));
        });

        HttpResponseMessage response = await memberClient.GetAsync(GuardedUrl);

        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Forbidden, "ACCOUNT_NOT_ACTIVE");
    }

    [SqlFact]
    public async Task TokenOfDeletedAccount_Returns401SessionExpired()
    {
        // Token ký đúng khóa nhưng tài khoản không có trong DB
        User ghost = new User
        {
            Email = $"{Guid.NewGuid():N}@test.local",
            FullName = "Không tồn tại",
            Role = UserRole.User,
            Status = UserStatus.Active,
        };
        HttpClient client = ClientFor(ghost);

        HttpResponseMessage response = await client.GetAsync(GuardedUrl);

        await ApiTestHelpers.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "SESSION_EXPIRED");
    }

    [SqlFact]
    public async Task LockedAccountToken_DoesNotBreakPublicEndpoint()
    {
        User member = await DeathCaseTestData.AddUserAsync(factory, UserRole.User, UserStatus.Locked);
        HttpClient memberClient = ClientFor(member);

        HttpResponseMessage response = await memberClient.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = member.Email });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private HttpClient ClientFor(User user)
    {
        HttpClient client = factory.CreateClient();
        ApiTestHelpers.AuthorizeAs(client, factory.Services, user);
        return client;
    }

    private async Task AddSessionAsync(Guid userId)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        RefreshToken session = new RefreshToken
        {
            UserId = userId,
            TokenHash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            FamilyId = Guid.NewGuid(),
            SessionStartedAt = now,
            CreatedAt = now,
            ExpiresAt = now.AddMinutes(30),
        };
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            db.RefreshTokens.Add(session);
            await db.SaveChangesAsync();
        });
    }
}
