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
    private readonly LoginMethodService _service;

    public LoginMethodServiceTests()
    {
        _logins = new FakeExternalLoginRepository(_users);
        _service = new LoginMethodService(_currentUser, _users, _logins, _verifier, _hasher, _unitOfWork, _clock);

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

        public bool IsAuthenticated
        {
            get { return UserId != null; }
        }
    }
}
