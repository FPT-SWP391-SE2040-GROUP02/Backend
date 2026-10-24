using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace LegacyVault.IntegrationTests.Auth;

/// <summary>
/// Quên và đặt lại mật khẩu qua HTTP trên SQL Server thật: link trong thư, đổi mật khẩu,
/// thu hồi phiên, dùng lại token, rồi đăng nhập bằng mật khẩu mới.
/// </summary>
public sealed class PasswordResetApiTests : IAsyncLifetime
{
    private const string OldPassword = "MatKhauCu123!";
    private const string NewPassword = "MatKhauMoi123!";

    private ApiFactory factory = null!;
    private readonly CapturingPasswordResetEmailSender mail = new CapturingPasswordResetEmailSender();

    public async Task InitializeAsync()
    {
        ApiFactory? created = ApiFactory.CreateForTempDatabase();
        if (created == null)
        {
            return;
        }
        factory = created;
        factory.ConfigureAdditionalServices = services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(mail);
        };
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
    public async Task ForgotThenReset_ChangesPassword_RevokesSessions_AndNewPasswordLogsIn()
    {
        // Chuẩn bị: tài khoản có mật khẩu và một phiên đăng nhập đang mở
        User user = await AddUserAsync();
        Guid sessionId = await AddSessionAsync(user.Id);
        HttpClient client = factory.CreateClient();

        // Hành động 1: quên mật khẩu
        HttpResponseMessage forgot = await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = user.Email.ToUpperInvariant() });

        Assert.Equal(HttpStatusCode.Accepted, forgot.StatusCode);
        Assert.Empty(await forgot.Content.ReadAsByteArrayAsync());
        CapturedResetMail sent = Assert.Single(mail.ResetMails);
        Assert.StartsWith("http://localhost:5173/reset-password?token=", sent.ResetUrl);
        string rawToken = CapturingPasswordResetEmailSender.TokenFromUrl(sent.ResetUrl);

        // Hành động 2: đặt mật khẩu mới bằng token trong link
        HttpResponseMessage reset = await client.PostAsJsonAsync("/api/v1/auth/reset-password",
            new { token = rawToken, newPassword = NewPassword, confirmPassword = NewPassword });

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            User saved = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
            IPasswordHasher hasher = factory.Services.GetRequiredService<IPasswordHasher>();
            Assert.True(hasher.Verify(NewPassword, saved.PasswordHash!));
            Assert.False(hasher.Verify(OldPassword, saved.PasswordHash!));

            RefreshToken session = await db.RefreshTokens.AsNoTracking().SingleAsync(t => t.Id == sessionId);
            Assert.NotNull(session.RevokedAt);

            PasswordResetToken token = await db.Set<PasswordResetToken>().AsNoTracking().SingleAsync(t => t.UserId == user.Id);
            Assert.NotNull(token.UsedAt);
            Assert.NotEqual(rawToken, token.TokenHash);
        });

        // Hành động 3: đăng nhập bằng mật khẩu mới
        HttpResponseMessage login = await LoginAsync(client, user.Email, NewPassword);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        HttpResponseMessage oldLogin = await LoginAsync(client, user.Email, OldPassword);
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
    }

    [SqlFact]
    public async Task Reset_SameTokenTwice_Returns400InvalidResetToken()
    {
        User user = await AddUserAsync();
        HttpClient client = factory.CreateClient();
        await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = user.Email });
        string rawToken = CapturingPasswordResetEmailSender.TokenFromUrl(mail.ResetMails[0].ResetUrl);
        await client.PostAsJsonAsync("/api/v1/auth/reset-password",
            new { token = rawToken, newPassword = NewPassword, confirmPassword = NewPassword });

        HttpResponseMessage again = await client.PostAsJsonAsync("/api/v1/auth/reset-password",
            new { token = rawToken, newPassword = "MatKhauKhac123!", confirmPassword = "MatKhauKhac123!" });

        await ApiTestHelpers.AssertProblemAsync(again, HttpStatusCode.BadRequest, "INVALID_RESET_TOKEN");
    }

    [SqlFact]
    public async Task Forgot_UnknownEmail_Returns404WithoutMail()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/auth/forgot-password", new { email = "khongco@test.local" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(mail.ResetMails);
    }

    [SqlFact]
    public async Task Reset_ShortOrMismatchedPassword_Returns400ValidationError()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage shortPassword = await client.PostAsJsonAsync("/api/v1/auth/reset-password",
            new { token = "bat-ky", newPassword = "ngan", confirmPassword = "ngan" });
        HttpResponseMessage mismatch = await client.PostAsJsonAsync("/api/v1/auth/reset-password",
            new { token = "bat-ky", newPassword = NewPassword, confirmPassword = "KhacNhau123!" });

        // AuthController khai báo [Produces("application/json")] nên lỗi kiểm dữ liệu không mang problem+json:
        // chỉ kiểm status và code, giống các API đăng ký có sẵn.
        Assert.Equal(HttpStatusCode.BadRequest, shortPassword.StatusCode);
        JsonElement first = await ApiTestHelpers.ReadJsonAsync(shortPassword);
        Assert.Equal("VALIDATION_ERROR", first.GetProperty("code").GetString());
        Assert.True(first.GetProperty("errors").TryGetProperty("newPassword", out _));
        Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
        JsonElement second = await ApiTestHelpers.ReadJsonAsync(mismatch);
        Assert.Equal("VALIDATION_ERROR", second.GetProperty("code").GetString());
        Assert.True(second.GetProperty("errors").TryGetProperty("confirmPassword", out _));
    }

    private async Task<User> AddUserAsync()
    {
        IPasswordHasher hasher = factory.Services.GetRequiredService<IPasswordHasher>();
        User user = new User
        {
            Email = $"{Guid.NewGuid():N}@test.local",
            FullName = "Người thử",
            PasswordHash = hasher.Hash(OldPassword),
            Role = UserRole.User,
            Status = UserStatus.Active,
            EmailVerifiedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        await ApiTestHelpers.WithDbAsync(factory, async db =>
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        });
        return user;
    }

    private async Task<Guid> AddSessionAsync(Guid userId)
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
        return session.Id;
    }

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password)
    {
        JsonElement proof = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/login");
        request.Headers.Add("X-CSRF-Token", proof.GetProperty("requestToken").GetString());
        request.Content = JsonContent.Create(new { email, password });
        return await client.SendAsync(request);
    }
}
