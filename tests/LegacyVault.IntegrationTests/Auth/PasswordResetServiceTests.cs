using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.GoogleAuth;
using Xunit;

namespace LegacyVault.IntegrationTests.Auth;

/// <summary>Luật quên và đặt lại mật khẩu bằng fake; khóa transaction trên SQL thật nằm ở PasswordResetApiTests.</summary>
public sealed class PasswordResetServiceTests
{
    private readonly FakeUserRepository users = new FakeUserRepository();
    private readonly FakeRefreshTokenRepository refreshTokens = new FakeRefreshTokenRepository();
    private readonly FakeUnitOfWork unitOfWork = new FakeUnitOfWork();
    private readonly CapturingPasswordResetEmailSender mail = new CapturingPasswordResetEmailSender();
    private readonly FixedTimeProvider clock = new FixedTimeProvider(new DateTimeOffset(2026, 10, 17, 8, 0, 0, TimeSpan.Zero));
    private readonly FakePasswordResetTokenRepository resetTokens;
    private readonly User user;

    public PasswordResetServiceTests()
    {
        resetTokens = new FakePasswordResetTokenRepository(users);
        user = new User
        {
            Email = "an@example.test",
            FullName = "Nguyễn An",
            PasswordHash = "hashed-MatKhauCu123!",
            Role = UserRole.User,
            Status = UserStatus.Active,
            EmailVerifiedAt = clock.Now.AddDays(-1),
            CreatedAt = clock.Now.AddDays(-1),
        };
        users.Users.Add(user);
    }

    private PasswordResetService Service
    {
        get
        {
            return new PasswordResetService(users, resetTokens, refreshTokens, unitOfWork, new FakePasswordHasher(),
                new FakeTokenService(), mail, new FakeAppUrlProvider(), clock);
        }
    }

    [Fact]
    public async Task Forgot_SendsOneTimeLinkAndStoresOnlyHash()
    {
        // Hành động: email viết hoa, có khoảng trắng vẫn tìm đúng tài khoản
        await Service.ForgotPasswordAsync(new ForgotPasswordRequest { Email = "  AN@example.test " }, default);

        // Kiểm tra
        CapturedResetMail sent = Assert.Single(mail.ResetMails);
        Assert.Equal(user.Email, sent.ToEmail);
        string rawToken = CapturingPasswordResetEmailSender.TokenFromUrl(sent.ResetUrl);
        PasswordResetToken stored = Assert.Single(resetTokens.Tokens);
        Assert.Equal("hash-" + rawToken, stored.TokenHash);
        Assert.NotEqual(rawToken, stored.TokenHash);
        Assert.Equal(clock.Now.AddMinutes(30), stored.ExpiresAt);
        Assert.Null(stored.UsedAt);
    }

    [Fact]
    public async Task Forgot_UnknownEmail_DoesNothingButSucceeds()
    {
        await Service.ForgotPasswordAsync(new ForgotPasswordRequest { Email = "khongco@example.test" }, default);

        Assert.Empty(mail.ResetMails);
        Assert.Empty(resetTokens.Tokens);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Theory]
    [InlineData("google-only")]
    [InlineData("disabled")]
    public async Task Forgot_AccountWithoutPasswordOrNotActive_DoesNotSendLink(string reason)
    {
        if (reason == "google-only")
        {
            user.PasswordHash = null;
        }
        if (reason == "disabled")
        {
            user.Status = UserStatus.Disabled;
        }

        await Service.ForgotPasswordAsync(new ForgotPasswordRequest { Email = user.Email }, default);

        Assert.Empty(mail.ResetMails);
        Assert.Empty(resetTokens.Tokens);
    }

    [Fact]
    public async Task Forgot_TwiceWithinCooldown_SendsOnlyOneMail_ThenNewLinkReplacesOld()
    {
        await Service.ForgotPasswordAsync(new ForgotPasswordRequest { Email = user.Email }, default);
        clock.Now = clock.Now.AddSeconds(30);
        await Service.ForgotPasswordAsync(new ForgotPasswordRequest { Email = user.Email }, default);
        Assert.Single(mail.ResetMails);

        clock.Now = clock.Now.AddSeconds(31);
        await Service.ForgotPasswordAsync(new ForgotPasswordRequest { Email = user.Email }, default);

        Assert.Equal(2, mail.ResetMails.Count);
        Assert.Equal(2, resetTokens.Tokens.Count);
        Assert.NotNull(resetTokens.Tokens[0].UsedAt);
        Assert.Null(resetTokens.Tokens[1].UsedAt);
    }

    [Fact]
    public async Task Reset_ChangesPassword_UsesToken_RevokesAllSessions_VerifiesEmail()
    {
        // Chuẩn bị: email chưa xác minh, hai phiên đăng nhập trên hai thiết bị
        user.EmailVerifiedAt = null;
        RefreshToken phone = AddSession();
        RefreshToken laptop = AddSession();
        await Service.ForgotPasswordAsync(new ForgotPasswordRequest { Email = user.Email }, default);
        string rawToken = CapturingPasswordResetEmailSender.TokenFromUrl(mail.ResetMails[0].ResetUrl);

        // Hành động
        clock.Now = clock.Now.AddMinutes(5);
        await Service.ResetPasswordAsync(Reset(rawToken, "MatKhauMoi123!"), default);

        // Kiểm tra
        Assert.Equal("hashed-MatKhauMoi123!", user.PasswordHash);
        Assert.Equal(clock.Now, user.EmailVerifiedAt);
        Assert.Equal(clock.Now, resetTokens.Tokens[0].UsedAt);
        Assert.Equal(clock.Now, phone.RevokedAt);
        Assert.Equal(clock.Now, laptop.RevokedAt);
    }

    [Fact]
    public async Task Reset_SameTokenTwice_SecondFails()
    {
        await Service.ForgotPasswordAsync(new ForgotPasswordRequest { Email = user.Email }, default);
        string rawToken = CapturingPasswordResetEmailSender.TokenFromUrl(mail.ResetMails[0].ResetUrl);
        await Service.ResetPasswordAsync(Reset(rawToken, "MatKhauMoi123!"), default);

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => Service.ResetPasswordAsync(Reset(rawToken, "MatKhauKhac123!"), default));

        Assert.Equal(ErrorCode.InvalidResetToken, error.Code);
        Assert.Equal(AppErrorType.Validation, error.Type);
        Assert.Equal("hashed-MatKhauMoi123!", user.PasswordHash);
    }

    [Fact]
    public async Task Reset_ExpiredToken_Fails()
    {
        await Service.ForgotPasswordAsync(new ForgotPasswordRequest { Email = user.Email }, default);
        string rawToken = CapturingPasswordResetEmailSender.TokenFromUrl(mail.ResetMails[0].ResetUrl);
        clock.Now = clock.Now.AddMinutes(30);

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => Service.ResetPasswordAsync(Reset(rawToken, "MatKhauMoi123!"), default));

        Assert.Equal(ErrorCode.InvalidResetToken, error.Code);
        Assert.Equal("hashed-MatKhauCu123!", user.PasswordHash);
    }

    [Fact]
    public async Task Reset_UnknownToken_Fails()
    {
        AppException error = await Assert.ThrowsAsync<AppException>(
            () => Service.ResetPasswordAsync(Reset("khong-ton-tai", "MatKhauMoi123!"), default));

        Assert.Equal(ErrorCode.InvalidResetToken, error.Code);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Reset_AccountDisabledAfterLinkSent_Fails()
    {
        await Service.ForgotPasswordAsync(new ForgotPasswordRequest { Email = user.Email }, default);
        string rawToken = CapturingPasswordResetEmailSender.TokenFromUrl(mail.ResetMails[0].ResetUrl);
        user.Status = UserStatus.Disabled;

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => Service.ResetPasswordAsync(Reset(rawToken, "MatKhauMoi123!"), default));

        Assert.Equal(ErrorCode.InvalidResetToken, error.Code);
        Assert.Equal("hashed-MatKhauCu123!", user.PasswordHash);
    }

    private RefreshToken AddSession()
    {
        RefreshToken token = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = Guid.NewGuid().ToString("N"),
            FamilyId = Guid.NewGuid(),
            SessionStartedAt = clock.Now,
            CreatedAt = clock.Now,
            ExpiresAt = clock.Now.AddMinutes(30),
        };
        refreshTokens.Add(token);
        return token;
    }

    private static ResetPasswordRequest Reset(string token, string password)
    {
        return new ResetPasswordRequest { Token = token, NewPassword = password, ConfirmPassword = password };
    }
}
