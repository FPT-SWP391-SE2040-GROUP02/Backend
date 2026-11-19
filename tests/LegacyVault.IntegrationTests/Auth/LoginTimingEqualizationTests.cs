using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.GoogleAuth;
using Xunit;

namespace LegacyVault.IntegrationTests.Auth;

/// <summary>
/// Đăng nhập sai phải tốn cùng số lần băm tốn kém dù email không tồn tại, hash mới hay hash cũ,
/// để thời gian phản hồi không lộ email nào có thật (đếm số lần Verify thay cho đo thời gian).
/// </summary>
public sealed class LoginTimingEqualizationTests
{
    private readonly FakeUserRepository _users = new();
    private readonly CountingHasher _hasher = new();
    private readonly AuthService _service;

    public LoginTimingEqualizationTests()
    {
        _service = new AuthService(
            _users, new FakeLoginThrottleRepository(), new FakeRefreshTokenRepository(), new FakeUnitOfWork(), _hasher,
            new FakeTokenService(), new TestPrincipal(), new FixedTimeProvider(new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero)));
    }

    private int WrongLoginVerifyCount(string email, string ip)
    {
        _hasher.Verifies = 0;
        Assert.ThrowsAsync<InvalidCredentialsException>(
            () => _service.LoginAsync(new LoginRequest { Email = email, Password = "sai" }, ip, default)).GetAwaiter().GetResult();
        return _hasher.Verifies;
    }

    private void AddUser(string email, string passwordHash)
    {
        _users.Users.Add(new User { Email = email, FullName = "A", Status = UserStatus.Active, Role = UserRole.User, PasswordHash = passwordHash });
    }

    [Fact]
    public void WrongPassword_VerifyCount_UnknownEmailAndNewHashOne_LegacyHashTwo()
    {
        AddUser("moi@example.test", CountingHasher.NewFormat("dung"));
        AddUser("cu@example.test", CountingHasher.LegacyFormat("dung"));

        int unknown = WrongLoginVerifyCount("khong-co@example.test", "ip1");
        int newHash = WrongLoginVerifyCount("moi@example.test", "ip2");

        // Hash cũ xác thực rẻ: phải bù thêm một lần băm giả (AuthService giữ _dummyHash static dùng chung, nên chỉ đếm số lần Verify).
        int legacy = WrongLoginVerifyCount("cu@example.test", "ip3");

        Assert.Equal(1, unknown);
        Assert.Equal(1, newHash);
        Assert.Equal(2, legacy);
    }

    private sealed class TestPrincipal : ICurrentUser
    {
        public Guid? UserId => null;

        public bool IsAuthenticated => false;
    }

    /// <summary>Hash "new-" là định dạng mới (Verify tốn kém), "old-" là định dạng cũ (Verify rẻ, NeedsRehash = true).</summary>
    private sealed class CountingHasher : IPasswordHasher
    {
        public int Verifies { get; set; }

        public static string NewFormat(string password) => "new-" + password;

        public static string LegacyFormat(string password) => "old-" + password;

        public string Hash(string password) => NewFormat(password);

        public bool Verify(string password, string passwordHash)
        {
            Verifies++;
            return passwordHash == NewFormat(password) || passwordHash == LegacyFormat(password);
        }

        public bool NeedsRehash(string passwordHash) => !passwordHash.StartsWith("new-", StringComparison.Ordinal);
    }
}
