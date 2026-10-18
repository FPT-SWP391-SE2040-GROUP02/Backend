using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using Xunit;

namespace LegacyVault.IntegrationTests.GoogleAuth;

/// <summary>
/// Test logic đăng nhập Google bằng các fake (không cần DB, không gọi Google thật).
/// </summary>
public sealed class GoogleAuthServiceTests
{
    private const string ValidToken = "valid-google-token";
    private const string IpRange = "10.0.0.0/24";

    private readonly FakeGoogleTokenVerifier _verifier = new();
    private readonly FakeUserRepository _users = new();
    private readonly FakeExternalLoginRepository _logins;
    private readonly FakeLinkTicketRepository _tickets;
    private readonly FakeRefreshTokenRepository _refreshTokens = new();
    private readonly FakeLoginThrottleRepository _throttles = new();
    private readonly FakePasswordHasher _passwordHasher = new();
    private readonly FakeTokenService _tokenService = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 10, 13, 8, 0, 0, TimeSpan.Zero));
    private readonly GoogleAuthService _service;

    public GoogleAuthServiceTests()
    {
        _logins = new FakeExternalLoginRepository(_users);
        _tickets = new FakeLinkTicketRepository(_users);

        var sessionIssuer = new LoginSessionIssuer(_refreshTokens, _tokenService, _unitOfWork, _clock);
        var throttleGuard = new LoginThrottleGuard(_throttles, _unitOfWork, _clock);

        _service = new GoogleAuthService(
            _verifier, _users, _logins, _tickets, _passwordHasher, _tokenService, _unitOfWork,
            sessionIssuer, throttleGuard, _clock);

        _verifier.Accept(ValidToken, new GoogleIdentity
        {
            Subject = "google-sub-123",
            Email = "An.Nguyen@Gmail.com",
            EmailVerified = true,
            FullName = "Nguyễn An",
        });
    }

    [Fact]
    public async Task InvalidToken_Throws401()
    {
        var request = new GoogleLoginRequest { IdToken = "token-gia-mao" };

        await Assert.ThrowsAsync<InvalidGoogleTokenException>(() => _service.LoginWithGoogleAsync(request, default));
        Assert.Empty(_users.Users);
    }

    [Fact]
    public async Task NewGoogleUser_CreatesAccountWithoutPassword_AndSignsIn()
    {
        var result = await _service.LoginWithGoogleAsync(new GoogleLoginRequest { IdToken = ValidToken }, default);

        Assert.True(result.SignedIn);
        Assert.True(result.AccountCreated);
        Assert.NotNull(result.Auth);

        var user = Assert.Single(_users.Users);
        Assert.Equal("an.nguyen@gmail.com", user.Email); // email đã chuẩn hóa
        Assert.Null(user.PasswordHash);                  // tài khoản chỉ có Google (AUTH-03)
        Assert.NotNull(user.EmailVerifiedAt);            // Google đã xác minh email (AUTH-04)
        Assert.Equal(UserRole.User, user.Role);

        var link = Assert.Single(_logins.Logins);
        Assert.Equal("google-sub-123", link.ProviderSubject);
        Assert.Equal(user.Id, link.UserId);

        Assert.Single(_refreshTokens.Tokens);
    }

    [Fact]
    public async Task UnverifiedGoogleEmail_IsNotMarkedVerified()
    {
        _verifier.Accept("unverified-token", new GoogleIdentity
        {
            Subject = "google-sub-999",
            Email = "chua.xac.minh@gmail.com",
            EmailVerified = false,
        });

        await _service.LoginWithGoogleAsync(new GoogleLoginRequest { IdToken = "unverified-token" }, default);

        Assert.Null(Assert.Single(_users.Users).EmailVerifiedAt);
    }

    [Fact]
    public async Task AlreadyLinkedGoogle_SignsInSameUser_WithoutCreatingAnother()
    {
        await _service.LoginWithGoogleAsync(new GoogleLoginRequest { IdToken = ValidToken }, default);

        var second = await _service.LoginWithGoogleAsync(new GoogleLoginRequest { IdToken = ValidToken }, default);

        Assert.True(second.SignedIn);
        Assert.False(second.AccountCreated);
        Assert.Single(_users.Users);
        Assert.Equal(2, _refreshTokens.Tokens.Count);
    }

    [Fact]
    public async Task LockedUser_CannotSignInWithGoogle()
    {
        await _service.LoginWithGoogleAsync(new GoogleLoginRequest { IdToken = ValidToken }, default);
        _users.Users[0].Status = UserStatus.Locked;

        await Assert.ThrowsAsync<AccountNotActiveException>(
            () => _service.LoginWithGoogleAsync(new GoogleLoginRequest { IdToken = ValidToken }, default));
    }

    [Fact]
    public async Task EmailMatchesPasswordAccount_DoesNotAutoMerge_ReturnsLinkTicket()
    {
        var existing = AddPasswordUser("an.nguyen@gmail.com", "MatKhau123!");

        var result = await _service.LoginWithGoogleAsync(new GoogleLoginRequest { IdToken = ValidToken }, default);

        Assert.False(result.SignedIn);
        Assert.Null(result.Auth);
        Assert.False(string.IsNullOrEmpty(result.LinkTicket));
        Assert.Equal("an***@gmail.com", result.MaskedEmail);

        Assert.Empty(_logins.Logins);        // chưa liên kết khi chưa nhập mật khẩu
        Assert.Empty(_refreshTokens.Tokens); // chưa mở phiên
        var ticket = Assert.Single(_tickets.Tickets);
        Assert.Equal(existing.Id, ticket.UserId);
        Assert.NotEqual(result.LinkTicket, ticket.TicketHash); // DB chỉ lưu hash
    }

    [Fact]
    public async Task ConfirmLink_WithCorrectPassword_LinksGoogle_AndSignsIn()
    {
        var existing = AddPasswordUser("an.nguyen@gmail.com", "MatKhau123!");
        var loginResult = await _service.LoginWithGoogleAsync(new GoogleLoginRequest { IdToken = ValidToken }, default);

        var auth = await _service.ConfirmGoogleLinkAsync(
            new ConfirmGoogleLinkRequest { LinkTicket = loginResult.LinkTicket!, Password = "MatKhau123!" }, IpRange, default);

        Assert.Equal(existing.Id, auth.Session.User.Id);
        var link = Assert.Single(_logins.Logins);
        Assert.Equal(existing.Id, link.UserId);
        Assert.NotNull(_tickets.Tickets[0].UsedAt);

        // Lần sau đăng nhập Google vào thẳng tài khoản cũ.
        var next = await _service.LoginWithGoogleAsync(new GoogleLoginRequest { IdToken = ValidToken }, default);
        Assert.True(next.SignedIn);
        Assert.Equal(existing.Id, next.Auth!.Session.User.Id);
    }

    [Fact]
    public async Task ConfirmLink_WithWrongPassword_Throws401_AndCountsFailure()
    {
        AddPasswordUser("an.nguyen@gmail.com", "MatKhau123!");
        var loginResult = await _service.LoginWithGoogleAsync(new GoogleLoginRequest { IdToken = ValidToken }, default);

        await Assert.ThrowsAsync<InvalidCredentialsException>(() => _service.ConfirmGoogleLinkAsync(
            new ConfirmGoogleLinkRequest { LinkTicket = loginResult.LinkTicket!, Password = "sai-mat-khau" }, IpRange, default));

        Assert.Empty(_logins.Logins);
        Assert.Equal(1, Assert.Single(_throttles.Throttles).FailedCount);
    }

    [Fact]
    public async Task ConfirmLink_FiveWrongPasswords_LocksAccount()
    {
        AddPasswordUser("an.nguyen@gmail.com", "MatKhau123!");
        var loginResult = await _service.LoginWithGoogleAsync(new GoogleLoginRequest { IdToken = ValidToken }, default);
        var wrong = new ConfirmGoogleLinkRequest { LinkTicket = loginResult.LinkTicket!, Password = "sai" };

        for (var i = 0; i < 4; i++)
        {
            await Assert.ThrowsAsync<InvalidCredentialsException>(() => _service.ConfirmGoogleLinkAsync(wrong, IpRange, default));
        }
        await Assert.ThrowsAsync<AccountLockedException>(() => _service.ConfirmGoogleLinkAsync(wrong, IpRange, default));

        // Đang khóa thì nhập đúng mật khẩu cũng bị chặn.
        var correct = new ConfirmGoogleLinkRequest { LinkTicket = loginResult.LinkTicket!, Password = "MatKhau123!" };
        await Assert.ThrowsAsync<AccountLockedException>(() => _service.ConfirmGoogleLinkAsync(correct, IpRange, default));
    }

    [Fact]
    public async Task ConfirmLink_WithExpiredTicket_Throws400()
    {
        AddPasswordUser("an.nguyen@gmail.com", "MatKhau123!");
        var loginResult = await _service.LoginWithGoogleAsync(new GoogleLoginRequest { IdToken = ValidToken }, default);

        _clock.Now = _clock.Now.AddMinutes(11); // vé chỉ sống 10 phút

        await Assert.ThrowsAsync<InvalidLinkTicketException>(() => _service.ConfirmGoogleLinkAsync(
            new ConfirmGoogleLinkRequest { LinkTicket = loginResult.LinkTicket!, Password = "MatKhau123!" }, IpRange, default));
    }

    [Fact]
    public async Task ConfirmLink_TicketCannotBeUsedTwice()
    {
        AddPasswordUser("an.nguyen@gmail.com", "MatKhau123!");
        var loginResult = await _service.LoginWithGoogleAsync(new GoogleLoginRequest { IdToken = ValidToken }, default);
        var request = new ConfirmGoogleLinkRequest { LinkTicket = loginResult.LinkTicket!, Password = "MatKhau123!" };

        await _service.ConfirmGoogleLinkAsync(request, IpRange, default);

        await Assert.ThrowsAsync<InvalidLinkTicketException>(() => _service.ConfirmGoogleLinkAsync(request, IpRange, default));
    }

    [Fact]
    public async Task ConfirmLink_WithUnknownTicket_Throws400()
    {
        await Assert.ThrowsAsync<InvalidLinkTicketException>(() => _service.ConfirmGoogleLinkAsync(
            new ConfirmGoogleLinkRequest { LinkTicket = "khong-ton-tai", Password = "x" }, IpRange, default));
    }

    [Theory]
    [InlineData("/invitations/abc", "/invitations/abc")]
    [InlineData("https://evil.com", null)]
    [InlineData("//evil.com", null)]
    [InlineData("/\\evil.com", null)]
    [InlineData("", null)]
    public async Task ReturnUrl_OnlyKeepsLocalPaths(string returnUrl, string? expected)
    {
        var result = await _service.LoginWithGoogleAsync(
            new GoogleLoginRequest { IdToken = ValidToken, ReturnUrl = returnUrl }, default);

        Assert.Equal(expected, result.ReturnUrl);
    }

    [Theory]
    [InlineData("an.nguyen@gmail.com", "an***@gmail.com")]
    [InlineData("ab@gmail.com", "a***@gmail.com")]
    [InlineData("khong-co-a-cong", "***")]
    public void EmailMasker_HidesMostOfLocalPart(string email, string expected)
    {
        Assert.Equal(expected, EmailMasker.Mask(email));
    }

    private User AddPasswordUser(string email, string password)
    {
        var user = new User
        {
            Email = email,
            PasswordHash = _passwordHasher.Hash(password),
            FullName = "Tài khoản có sẵn",
            Role = UserRole.User,
            Status = UserStatus.Active,
            CreatedAt = _clock.Now,
        };
        _users.Users.Add(user);
        return user;
    }
}
