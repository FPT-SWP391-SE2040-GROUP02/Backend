using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.GoogleAuth;
using Xunit;

namespace LegacyVault.IntegrationTests.Auth;

/// <summary>Use-case regression từ test được chuyển sang repository API Backend; fake không chứng minh SQL locking.</summary>
public sealed class AuthSessionInvariantTests
{
    private readonly FakeUserRepository _users = new();
    private readonly FakeRefreshTokenRepository _tokens = new();
    private readonly FakeLoginThrottleRepository _throttles = new();
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero));
    private readonly Principal _principal = new();
    private readonly TransactionSpy _unit = new();
    private readonly User _user = new() { Email = "user@example.test", FullName = "Fixture", Status = UserStatus.Active, Role = UserRole.User, PasswordHash = "hashed-fixture" };
    private AuthService Service => new(_users, _throttles, _tokens, _unit, new FakePasswordHasher(), new FakeTokenService(), _principal, _clock);

    public AuthSessionInvariantTests() { _users.Users.Add(_user); }

    [Fact]
    public async Task InvalidPasswordCommitsFailureCountBeforeReportingFailure()
    {
        await Assert.ThrowsAsync<InvalidCredentialsException>(() => Service.LoginAsync(new LoginRequest { Email = _user.Email, Password = "wrong" }, "fixture-ip", default));
        Assert.Equal(1, _unit.Committed);
        Assert.Equal(1, Assert.Single(_throttles.Throttles).FailedCount);
        Assert.Empty(_tokens.Tokens);
    }

    [Fact]
    public async Task RefreshPreservesMaximumLifetimeAndReplayRevokesReplacement()
    {
        var original = Token("old");
        original.SessionStartedAt = _clock.Now.AddHours(-11.9);
        await Service.RefreshAsync("old", default);
        var replacement = _tokens.Tokens.Single(token => token.Id != original.Id);
        Assert.NotNull(original.RevokedAt);
        Assert.Equal(original.FamilyId, replacement.FamilyId);
        Assert.Equal(original.SessionStartedAt.AddHours(12), replacement.ExpiresAt);
        await Assert.ThrowsAsync<SessionExpiredException>(() => Service.RefreshAsync("old", default));
        Assert.NotNull(replacement.RevokedAt);
        Assert.Equal(2, _unit.Committed);
    }

    [Theory]
    [InlineData("revoked")]
    [InlineData("expired")]
    [InlineData("disabled")]
    public async Task InvalidCookieCannotRevokeOtherDevices(string reason)
    {
        var old = Token("old");
        var other = Token("other");
        other.FamilyId = Guid.NewGuid();
        if (reason == "revoked") { old.RevokedAt = _clock.Now; }
        if (reason == "expired") { old.ExpiresAt = _clock.Now; }
        if (reason == "disabled") { _user.Status = UserStatus.Disabled; }
        await Assert.ThrowsAsync<SessionExpiredException>(() => Service.LogoutAsync("old", true, default));
        Assert.Null(other.RevokedAt);
        Assert.Equal(0, _unit.Committed);
    }

    [Fact]
    public async Task PrincipalCanLogoutAllWithoutCookieButCannotUseAnotherUsersCookie()
    {
        _principal.UserId = _user.Id;
        var own = Token("own");
        await Service.LogoutAsync(null, true, default);
        Assert.NotNull(own.RevokedAt);
        var other = Token("other");
        other.UserId = Guid.NewGuid();
        await Assert.ThrowsAsync<SessionExpiredException>(() => Service.LogoutAsync("other", true, default));
        Assert.Null(other.RevokedAt);
    }

    [Fact]
    public async Task SingleDeviceLogoutRevokesOnlyItsFamily()
    {
        var own = Token("own");
        var other = Token("other");
        other.FamilyId = Guid.NewGuid();
        await Service.LogoutAsync("own", false, default);
        Assert.NotNull(own.RevokedAt);
        Assert.Null(other.RevokedAt);
    }

    [Fact]
    public async Task DisabledAccountCannotReadSession()
    {
        _principal.UserId = _user.Id;
        _user.Status = UserStatus.Disabled;
        await Assert.ThrowsAsync<SessionExpiredException>(() => Service.GetCurrentUserAsync(default));
    }

    private RefreshToken Token(string value)
    {
        var token = new RefreshToken { User = _user, UserId = _user.Id, TokenHash = "hash-" + value, FamilyId = Guid.NewGuid(), SessionStartedAt = _clock.Now, CreatedAt = _clock.Now, ExpiresAt = _clock.Now.AddMinutes(30) };
        _tokens.Tokens.Add(token);
        return token;
    }

    private sealed class Principal : ICurrentUser
    {
        public Guid? UserId { get; set; }
        public bool IsAuthenticated => UserId.HasValue;
    }
    private sealed class TransactionSpy : IUnitOfWork
    {
        public int Committed { get; private set; }
        public Task SaveChangesAsync(CancellationToken ct) => Task.CompletedTask;
        public async Task<T> ExecuteSerializableAsync<T>(Func<Task<T>> operation, CancellationToken ct)
        {
            var result = await operation();
            Committed++;
            return result;
        }
    }
}
