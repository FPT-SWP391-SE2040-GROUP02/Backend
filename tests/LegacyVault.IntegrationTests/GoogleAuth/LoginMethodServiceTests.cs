using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using Xunit;

namespace LegacyVault.IntegrationTests.GoogleAuth;

/// <summary>Test quản lý phương thức đăng nhập (AUTH-03) bằng fake, không cần DB hay Google thật.</summary>
public sealed class LoginMethodServiceTests
{
    private const string GoogleToken = "google-token";
    private const string OtherGoogleToken = "other-google-token";
    private const string Password = "Matkhau123!";

    private readonly FakeGoogleTokenVerifier _verifier = new();
    private readonly FakeUserRepository _users = new();
    private readonly FakeExternalLoginRepository _logins;
    private readonly FakePasswordHasher _hasher = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero));
    private readonly TestCurrentUser _currentUser = new();
    private readonly FakeRefreshTokenRepository _refreshTokens = new();
    private readonly FakeLoginThrottleRepository _throttles = new();
    private readonly LoginMethodService _service;

    public LoginMethodServiceTests()
    {
        _logins = new FakeExternalLoginRepository(_users);
        _service = new LoginMethodService(
            _currentUser, _users, _logins, _verifier, _hasher, _refreshTokens,
            new LoginThrottleGuard(_throttles, _unitOfWork, _clock), _unitOfWork, _clock);

        _verifier.Accept(GoogleToken, new GoogleIdentity
        {
            Subject = "sub-1",
            Email = "An@Gmail.com",
            EmailVerified = true,
        });
        _verifier.Accept(OtherGoogleToken, new GoogleIdentity
        {
            Subject = "sub-2",
            Email = "khac@gmail.com",
            EmailVerified = true,
        });
    }

    [Fact]
    public async Task List_PasswordOnly_CannotRemove()
    {
        User user = AddUser(true);

        List<LoginMethodResponse> methods = await _service.GetMyLoginMethodsAsync(default);

        Assert.Single(methods);
        Assert.Equal("PASSWORD", methods[0].Method);
        Assert.False(methods[0].CanRemove);
        Assert.Equal(user.Id, _currentUser.UserId);
    }

    [Fact]
    public async Task List_PasswordAndGoogle_BothCanRemove()
    {
        User user = AddUser(true);
        AddGoogleLink(user, "sub-1");

        List<LoginMethodResponse> methods = await _service.GetMyLoginMethodsAsync(default);

        Assert.Equal(2, methods.Count);
        Assert.All(methods, m => Assert.True(m.CanRemove));
    }

    [Fact]
    public async Task NoSession_Throws401()
    {
        _currentUser.UserId = null;

        await Assert.ThrowsAsync<SessionExpiredException>(() => _service.GetMyLoginMethodsAsync(default));
    }

    [Fact]
    public async Task LinkGoogle_WrongPassword_ReauthFailed_NothingSaved()
    {
        AddUser(true);
        LinkGoogleRequest request = new LinkGoogleRequest { IdToken = GoogleToken, CurrentPassword = "sai" };

        await Assert.ThrowsAsync<ReauthenticationFailedException>(() => _service.LinkGoogleAsync(request, default));
        Assert.Empty(_logins.Logins);
    }

    [Fact]
    public async Task LinkGoogle_InvalidToken_Throws()
    {
        AddUser(true);
        LinkGoogleRequest request = new LinkGoogleRequest { IdToken = "gia-mao", CurrentPassword = Password };

        await Assert.ThrowsAsync<InvalidGoogleTokenException>(() => _service.LinkGoogleAsync(request, default));
    }

    [Fact]
    public async Task LinkGoogle_Success_KeepsAccountEmail()
    {
        User user = AddUser(true);
        LinkGoogleRequest request = new LinkGoogleRequest { IdToken = GoogleToken, CurrentPassword = Password };

        LoginMethodResponse created = await _service.LinkGoogleAsync(request, default);

        Assert.Equal("GOOGLE", created.Method);
        Assert.Equal("an@gmail.com", created.Email);
        Assert.True(created.CanRemove);
        Assert.Single(_logins.Logins);
        Assert.Equal("an@example.com", user.Email);
    }

    [Fact]
    public async Task LinkGoogle_AlreadyLinkedToThisAccount_Conflict()
    {
        User user = AddUser(true);
        AddGoogleLink(user, "sub-2");
        LinkGoogleRequest request = new LinkGoogleRequest { IdToken = GoogleToken, CurrentPassword = Password };

        await Assert.ThrowsAsync<GoogleAccountAlreadyLinkedException>(() => _service.LinkGoogleAsync(request, default));
    }

    [Fact]
    public async Task LinkGoogle_SubjectBelongsToAnotherUser_Conflict()
    {
        User other = AddUser(true, "nguoi-khac@example.com");
        AddGoogleLink(other, "sub-1");
        User me = AddUser(true);
        _currentUser.UserId = me.Id;
        LinkGoogleRequest request = new LinkGoogleRequest { IdToken = GoogleToken, CurrentPassword = Password };

        await Assert.ThrowsAsync<GoogleAccountAlreadyLinkedException>(() => _service.LinkGoogleAsync(request, default));
        Assert.Single(_logins.Logins);
    }

    [Fact]
    public async Task VerifyPassword_Correct_ChangesNothing()
    {
        User user = AddUser(true);
        string? hashBefore = user.PasswordHash;

        await _service.VerifyPasswordAsync(new VerifyPasswordRequest { CurrentPassword = Password }, default);

        Assert.Equal(hashBefore, user.PasswordHash);
        Assert.Empty(_logins.Logins);
    }

    [Fact]
    public async Task VerifyPassword_Wrong_ReauthFailed()
    {
        AddUser(true);

        await Assert.ThrowsAsync<ReauthenticationFailedException>(
            () => _service.VerifyPasswordAsync(new VerifyPasswordRequest { CurrentPassword = "sai" }, default));
    }

    [Fact]
    public async Task VerifyPassword_GoogleOnlyAccount_ReauthFailed()
    {
        User user = AddUser(false);
        AddGoogleLink(user, "sub-1");

        await Assert.ThrowsAsync<ReauthenticationFailedException>(
            () => _service.VerifyPasswordAsync(new VerifyPasswordRequest { CurrentPassword = Password }, default));
    }

    [Fact]
    public async Task UnlinkGoogle_NotLinked_IsIdempotent()
    {
        AddUser(true);

        await _service.UnlinkGoogleAsync(new UnlinkGoogleRequest { CurrentPassword = "bat-ky" }, default);

        Assert.Empty(_logins.Logins);
    }

    [Fact]
    public async Task UnlinkGoogle_GoogleOnly_LastLoginMethod()
    {
        User user = AddUser(false);
        AddGoogleLink(user, "sub-1");

        await Assert.ThrowsAsync<LastLoginMethodException>(
            () => _service.UnlinkGoogleAsync(new UnlinkGoogleRequest { CurrentPassword = Password }, default));
        Assert.Single(_logins.Logins);
    }

    [Fact]
    public async Task UnlinkGoogle_WrongPassword_ReauthFailed()
    {
        User user = AddUser(true);
        AddGoogleLink(user, "sub-1");

        await Assert.ThrowsAsync<ReauthenticationFailedException>(
            () => _service.UnlinkGoogleAsync(new UnlinkGoogleRequest { CurrentPassword = "sai" }, default));
        Assert.Single(_logins.Logins);
    }

    [Fact]
    public async Task UnlinkGoogle_Success_RemovesLink()
    {
        User user = AddUser(true);
        AddGoogleLink(user, "sub-1");

        await _service.UnlinkGoogleAsync(new UnlinkGoogleRequest { CurrentPassword = Password }, default);

        Assert.Empty(_logins.Logins);
    }

    [Fact]
    public async Task SetPassword_AlreadyHasPassword_Conflict()
    {
        User user = AddUser(true);
        AddGoogleLink(user, "sub-1");

        await Assert.ThrowsAsync<LoginMethodAlreadyExistsException>(() => _service.SetPasswordAsync(NewSetPassword(GoogleToken), default));
    }

    [Fact]
    public async Task SetPassword_GoogleOfAnotherAccount_ReauthFailed()
    {
        User user = AddUser(false);
        AddGoogleLink(user, "sub-1");

        await Assert.ThrowsAsync<ReauthenticationFailedException>(() => _service.SetPasswordAsync(NewSetPassword(OtherGoogleToken), default));
        Assert.Null(user.PasswordHash);
    }

    [Fact]
    public async Task SetPassword_Success_StoresHash()
    {
        User user = AddUser(false);
        AddGoogleLink(user, "sub-1");

        await _service.SetPasswordAsync(NewSetPassword(GoogleToken), default);

        Assert.Equal(_hasher.Hash("MatKhauMoi123!"), user.PasswordHash);
    }

    [Fact]
    public async Task RemovePassword_NoGoogle_LastLoginMethod()
    {
        User user = AddUser(true);

        await Assert.ThrowsAsync<LastLoginMethodException>(() => _service.RemovePasswordAsync(NewRemovePassword(Password, GoogleToken), default));
        Assert.NotNull(user.PasswordHash);
    }

    [Fact]
    public async Task RemovePassword_WrongPassword_ReauthFailed()
    {
        User user = AddUser(true);
        AddGoogleLink(user, "sub-1");

        await Assert.ThrowsAsync<ReauthenticationFailedException>(() => _service.RemovePasswordAsync(NewRemovePassword("sai", GoogleToken), default));
        Assert.NotNull(user.PasswordHash);
    }

    [Fact]
    public async Task RemovePassword_WrongGoogleAccount_ReauthFailed()
    {
        User user = AddUser(true);
        AddGoogleLink(user, "sub-1");

        await Assert.ThrowsAsync<ReauthenticationFailedException>(() => _service.RemovePasswordAsync(NewRemovePassword(Password, OtherGoogleToken), default));
        Assert.NotNull(user.PasswordHash);
    }

    [Fact]
    public async Task RemovePassword_Success_ClearsHash()
    {
        User user = AddUser(true);
        AddGoogleLink(user, "sub-1");

        await _service.RemovePasswordAsync(NewRemovePassword(Password, GoogleToken), default);

        Assert.Null(user.PasswordHash);
        List<LoginMethodResponse> methods = await _service.GetMyLoginMethodsAsync(default);
        Assert.Single(methods);
        Assert.False(methods[0].CanRemove);
    }

    private User AddUser(bool withPassword)
    {
        return AddUser(withPassword, "an@example.com");
    }

    private User AddUser(bool withPassword, string email)
    {
        User user = new User
        {
            Email = email,
            FullName = "Nguyễn An",
            Role = UserRole.User,
            Status = UserStatus.Active,
            CreatedAt = _clock.GetUtcNow(),
        };
        if (withPassword)
        {
            user.PasswordHash = _hasher.Hash(Password);
        }
        _users.Users.Add(user);
        _currentUser.UserId = user.Id;
        return user;
    }

    [Fact]
    public async Task ChangePassword_RevokesOtherSessions_KeepsCurrent()
    {
        User user = AddUser(true);
        Guid current = Guid.NewGuid();
        Guid other = Guid.NewGuid();
        _currentUser.SessionFamilyId = current;
        RefreshToken keep = AddSession(user, current);
        RefreshToken drop = AddSession(user, other);

        await _service.ChangePasswordAsync(
            new ChangePasswordRequest { CurrentPassword = Password, NewPassword = "MatKhauMoi123!" }, default);

        Assert.Null(keep.RevokedAt);
        Assert.NotNull(drop.RevokedAt);
        // Chặn cả refresh: token phiên khác không còn nằm trong danh sách còn hiệu lực.
        Assert.DoesNotContain(drop, await _refreshTokens.FindActiveByUserAsync(user.Id, default));
    }

    [Fact]
    public async Task RemovePassword_RevokesOtherSessions()
    {
        User user = AddUser(true);
        AddGoogleLink(user, "sub-1");
        _currentUser.SessionFamilyId = Guid.NewGuid();
        RefreshToken drop = AddSession(user, Guid.NewGuid());

        await _service.RemovePasswordAsync(NewRemovePassword(Password, GoogleToken), default);

        Assert.NotNull(drop.RevokedAt);
    }

    [Fact]
    public async Task ChangePassword_WrongPassword_LocksAfterMaxAttempts()
    {
        AddUser(true);
        ChangePasswordRequest wrong = new ChangePasswordRequest { CurrentPassword = "sai", NewPassword = "MatKhauMoi123!" };

        for (int i = 0; i < AuthPolicy.MaxFailedAttempts - 1; i++)
        {
            await Assert.ThrowsAsync<ReauthenticationFailedException>(() => _service.ChangePasswordAsync(wrong, default));
        }
        await Assert.ThrowsAsync<AccountLockedException>(() => _service.ChangePasswordAsync(wrong, default));

        // Đã khóa: mật khẩu đúng cũng bị từ chối.
        ChangePasswordRequest right = new ChangePasswordRequest { CurrentPassword = Password, NewPassword = "MatKhauMoi123!" };
        await Assert.ThrowsAsync<AccountLockedException>(() => _service.ChangePasswordAsync(right, default));
    }

    [Fact]
    public async Task VerifyPassword_Correct_ClearsFailureCounter()
    {
        AddUser(true);
        await Assert.ThrowsAsync<ReauthenticationFailedException>(
            () => _service.VerifyPasswordAsync(new VerifyPasswordRequest { CurrentPassword = "sai" }, default));
        Assert.Single(_throttles.Throttles);

        await _service.VerifyPasswordAsync(new VerifyPasswordRequest { CurrentPassword = Password }, default);

        Assert.Empty(_throttles.Throttles);
    }

    private RefreshToken AddSession(User user, Guid familyId)
    {
        RefreshToken token = new RefreshToken
        {
            UserId = user.Id,
            FamilyId = familyId,
            TokenHash = Guid.NewGuid().ToString("N"),
        };
        _refreshTokens.Tokens.Add(token);
        return token;
    }

    private void AddGoogleLink(User user, string subject)
    {
        _logins.Logins.Add(new ExternalLogin
        {
            UserId = user.Id,
            User = user,
            Provider = LoginProvider.Google,
            ProviderSubject = subject,
            ProviderEmail = "an@gmail.com",
            LinkedAt = _clock.GetUtcNow(),
        });
    }

    private static SetPasswordRequest NewSetPassword(string googleToken)
    {
        return new SetPasswordRequest
        {
            GoogleIdToken = googleToken,
            NewPassword = "MatKhauMoi123!",
            ConfirmPassword = "MatKhauMoi123!",
        };
    }

    private static RemovePasswordRequest NewRemovePassword(string password, string googleToken)
    {
        return new RemovePasswordRequest { CurrentPassword = password, GoogleIdToken = googleToken };
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public Guid? UserId { get; set; }

        public Guid? SessionFamilyId { get; set; }

        public string ClientIpRange { get; set; } = "10.0.0.0/24";

        public bool IsAuthenticated
        {
            get { return UserId != null; }
        }
    }
}
