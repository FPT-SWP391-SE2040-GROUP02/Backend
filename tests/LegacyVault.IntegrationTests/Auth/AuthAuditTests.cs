using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.Audit;
using LegacyVault.IntegrationTests.GoogleAuth;
using Xunit;

namespace LegacyVault.IntegrationTests.Auth;

/// <summary>Sự kiện xác thực được ghi nhật ký, và nhật ký không chứa mật khẩu (SEC-07). Dùng fake, không cần DB.</summary>
public sealed class AuthAuditTests
{
    private const string Password = "Matkhau123!";

    private readonly FakeUserRepository _users = new();
    private readonly FakeRefreshTokenRepository _tokens = new();
    private readonly FakeLoginThrottleRepository _throttles = new();
    private readonly FakeAuditLogRepository _audit = new();
    private readonly FakePasswordHasher _hasher = new();
    private readonly FakeUnitOfWork _unit = new();
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero));
    private readonly User _user;
    private readonly AuthService _service;

    public AuthAuditTests()
    {
        _user = new User
        {
            Email = "an@example.test", FullName = "An", Status = UserStatus.Active, Role = UserRole.User,
            PasswordHash = _hasher.Hash(Password),
        };
        _users.Users.Add(_user);
        _service = new AuthService(
            _users, _throttles, _tokens, _unit, _hasher, new FakeTokenService(), new TestPrincipal(), _clock,
            auditLogRepository: _audit);
    }

    [Fact]
    public async Task Login_Success_WritesLoginSucceeded_WithoutSecrets()
    {
        await _service.LoginAsync(new LoginRequest { Email = _user.Email, Password = Password }, "ip", default);

        AuditLog log = Assert.Single(_audit.Logs);
        Assert.Equal(AuditAction.LoginSucceeded, log.Action);
        Assert.Equal(AuditOutcome.Succeeded, log.Outcome);
        Assert.Equal(_user.Id, log.ActorUserId);
        Assert.Null(log.VaultId);
        Assert.DoesNotContain(Password, log.Reason ?? string.Empty);
    }

    [Fact]
    public async Task Login_WrongPasswordThenLock_WritesFailedThenLocked()
    {
        LoginRequest wrong = new LoginRequest { Email = _user.Email, Password = "sai" };
        for (int i = 0; i < AuthPolicy.MaxFailedAttempts - 1; i++)
        {
            await Assert.ThrowsAsync<InvalidCredentialsException>(() => _service.LoginAsync(wrong, "ip", default));
        }
        await Assert.ThrowsAsync<AccountLockedException>(() => _service.LoginAsync(wrong, "ip", default));

        Assert.Equal(AuthPolicy.MaxFailedAttempts - 1, _audit.Logs.Count(l => l.Action == AuditAction.LoginFailed));
        AuditLog locked = Assert.Single(_audit.Logs, l => l.Action == AuditAction.LoginLocked);
        Assert.Equal(AuditOutcome.Denied, locked.Outcome);
    }

    [Fact]
    public async Task Login_UnknownEmail_WritesNothing()
    {
        await Assert.ThrowsAsync<InvalidCredentialsException>(
            () => _service.LoginAsync(new LoginRequest { Email = "la@example.test", Password = "x" }, "ip", default));

        Assert.Empty(_audit.Logs);
    }

    [Fact]
    public async Task Login_WithLegacyHash_UpgradesToPbkdf2()
    {
        // Hasher thật (ít vòng) thay cho fake để thấy hash đổi định dạng sau đăng nhập đúng.
        LegacyVault.Infrastructure.Security.Pbkdf2PasswordHasher real = new(1000);
        _user.PasswordHash = new LegacyVault.Infrastructure.Security.Sha256PasswordHasher().Hash(Password);
        AuthService service = new(
            _users, _throttles, _tokens, _unit, real, new FakeTokenService(), new TestPrincipal(), _clock);

        await service.LoginAsync(new LoginRequest { Email = _user.Email, Password = Password }, "ip", default);

        Assert.StartsWith("pbkdf2$1000$", _user.PasswordHash);
        // Đăng nhập lần sau bằng hash mới vẫn được.
        await service.LoginAsync(new LoginRequest { Email = _user.Email, Password = Password }, "ip", default);
    }

    [Fact]
    public void EnumNames_MapToApiStrings()
    {
        Assert.Equal("LOGIN_SUCCEEDED", LegacyVault.Application.Mappings.EnumText.ToUpperSnake(nameof(AuditAction.LoginSucceeded)));
        Assert.Equal("EKYC_PASSED", LegacyVault.Application.Mappings.EnumText.ToUpperSnake(nameof(AuditAction.EkycPassed)));
        Assert.Equal("PASSWORD_RESET_COMPLETED", LegacyVault.Application.Mappings.EnumText.ToUpperSnake(nameof(AuditAction.PasswordResetCompleted)));
    }

    private sealed class TestPrincipal : LegacyVault.Application.Security.ICurrentUser
    {
        public Guid? UserId => null;

        public bool IsAuthenticated => false;
    }
}
