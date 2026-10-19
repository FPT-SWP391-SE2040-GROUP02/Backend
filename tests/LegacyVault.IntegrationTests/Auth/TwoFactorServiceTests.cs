using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.GoogleAuth;
using Xunit;

namespace LegacyVault.IntegrationTests.Auth;

/// <summary>Test TOTP RFC 6238 và luồng xác thực hai bước của Admin bằng fake (không cần DB).</summary>
public sealed class TwoFactorServiceTests
{
    // Bí mật mẫu của RFC 6238: ASCII "12345678901234567890".
    private static readonly byte[] RfcSecret = System.Text.Encoding.ASCII.GetBytes("12345678901234567890");

    private readonly FakeUserRepository _users = new();
    private readonly FakeRefreshTokenRepository _refreshTokens = new();
    private readonly FakeTwoFactorRepository _twoFactor;
    private readonly FakeTotpSecretProtector _protector = new();
    private readonly FakeTokenService _tokenService = new();
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly FixedTimeProvider _clock = new(new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero));
    private readonly TestCurrentUser _currentUser = new();
    private readonly TwoFactorService _service;

    public TwoFactorServiceTests()
    {
        _twoFactor = new FakeTwoFactorRepository(_users);
        LoginSessionIssuer issuer = new LoginSessionIssuer(_refreshTokens, _tokenService, _unitOfWork, _clock);
        _service = new TwoFactorService(
            _currentUser, _users, _twoFactor, _protector, _tokenService, _unitOfWork, issuer, _clock);
    }

    [Fact]
    public void Totp_MatchesRfcVectors()
    {
        Assert.Equal("755224", Totp.ComputeCode(RfcSecret, 0));
        Assert.Equal("287082", Totp.ComputeCode(RfcSecret, 1));
        Assert.Equal("359152", Totp.ComputeCode(RfcSecret, 2));
    }

    [Fact]
    public void Totp_VerifyAcceptsAdjacentStepsButNotReplay()
    {
        DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(59);
        string previousStepCode = Totp.ComputeCode(RfcSecret, 0);

        Assert.Equal(0L, Totp.Verify(RfcSecret, previousStepCode, now, -1));
        Assert.Null(Totp.Verify(RfcSecret, previousStepCode, now, 0));
        Assert.Null(Totp.Verify(RfcSecret, "000000", now, -1));
        Assert.Null(Totp.Verify(RfcSecret, "12345", now, -1));
    }

    [Fact]
    public void Totp_Base32IsStandard()
    {
        Assert.Equal("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", Totp.ToBase32(RfcSecret));
    }

    [Fact]
    public async Task Setup_NonAdmin_Forbidden()
    {
        AddUser(UserRole.User);

        await Assert.ThrowsAsync<TwoFactorException>(() => _service.SetupAsync(default));
        Assert.Empty(_twoFactor.Totps);
    }

    [Fact]
    public async Task Setup_ReturnsSecretAndUri_NotEnforcedUntilEnabled()
    {
        User admin = AddUser(UserRole.Admin);

        TwoFactorSetupResponse setup = await _service.SetupAsync(default);

        Assert.StartsWith("otpauth://totp/LegacyVault:", setup.OtpAuthUri);
        Assert.Contains("secret=" + setup.Secret, setup.OtpAuthUri);
        TwoFactorChallengeResponse? challenge = await _service.ChallengeIfRequiredAsync(admin, false, default);
        Assert.Null(challenge);
        TwoFactorStatusResponse status = await _service.GetStatusAsync(default);
        Assert.False(status.Enabled);
        Assert.True(status.SetupPending);
    }

    [Fact]
    public async Task Enable_WrongCode_Rejected()
    {
        AddUser(UserRole.Admin);
        await _service.SetupAsync(default);

        await Assert.ThrowsAsync<TwoFactorException>(() => _service.EnableAsync(new TwoFactorCodeRequest { Code = "000000" }, default));
        Assert.Null(_twoFactor.Totps[0].EnabledAt);
    }

    [Fact]
    public async Task Enable_ValidCode_ReturnsEightRecoveryCodes()
    {
        AddUser(UserRole.Admin);
        TwoFactorEnableResponse enabled = await SetupAndEnableAsync();

        TwoFactorStatusResponse status = await _service.GetStatusAsync(default);
        Assert.True(status.Enabled);
        Assert.Equal(8, status.RecoveryCodesRemaining);
        Assert.Equal(8, enabled.RecoveryCodes.Count);
        // Mã khôi phục chỉ lưu hash, không có bản rõ trong DB.
        foreach (string plain in enabled.RecoveryCodes)
        {
            string raw = plain.Replace("-", string.Empty);
            Assert.DoesNotContain(_twoFactor.RecoveryCodes, c => c.CodeHash == raw);
        }
    }

    [Fact]
    public async Task Login_AdminWithTwoFactor_GetsChallengeThenSessionWithTotp()
    {
        User admin = AddUser(UserRole.Admin);
        await SetupAndEnableAsync();
        _clock.Now = _clock.Now.AddMinutes(1);

        TwoFactorChallengeResponse? challenge = await _service.ChallengeIfRequiredAsync(admin, true, default);

        Assert.NotNull(challenge);
        Assert.True(challenge!.TwoFactorRequired);
        Assert.Empty(_refreshTokens.Tokens);

        AuthResult result = await _service.VerifyLoginAsync(
            new TwoFactorVerifyRequest { ChallengeToken = challenge.ChallengeToken, Code = CurrentCode() }, default);

        Assert.Equal(admin.Email, result.Session.User.Email);
        Assert.True(result.IsPersistent);
        Assert.Single(_refreshTokens.Tokens);
    }

    [Fact]
    public async Task Login_NonAdmin_NoChallenge()
    {
        User user = AddUser(UserRole.User);

        TwoFactorChallengeResponse? challenge = await _service.ChallengeIfRequiredAsync(user, false, default);

        Assert.Null(challenge);
    }

    [Fact]
    public async Task Verify_ReusedTotpCode_Rejected()
    {
        User admin = AddUser(UserRole.Admin);
        await SetupAndEnableAsync();
        _clock.Now = _clock.Now.AddMinutes(1);
        string code = CurrentCode();
        TwoFactorChallengeResponse first = (await _service.ChallengeIfRequiredAsync(admin, false, default))!;
        await _service.VerifyLoginAsync(new TwoFactorVerifyRequest { ChallengeToken = first.ChallengeToken, Code = code }, default);
        TwoFactorChallengeResponse second = (await _service.ChallengeIfRequiredAsync(admin, false, default))!;

        await Assert.ThrowsAsync<TwoFactorException>(
            () => _service.VerifyLoginAsync(new TwoFactorVerifyRequest { ChallengeToken = second.ChallengeToken, Code = code }, default));
    }

    [Fact]
    public async Task Verify_ChallengeIsOneTime()
    {
        User admin = AddUser(UserRole.Admin);
        await SetupAndEnableAsync();
        _clock.Now = _clock.Now.AddMinutes(1);
        TwoFactorChallengeResponse challenge = (await _service.ChallengeIfRequiredAsync(admin, false, default))!;
        await _service.VerifyLoginAsync(
            new TwoFactorVerifyRequest { ChallengeToken = challenge.ChallengeToken, Code = CurrentCode() }, default);
        _clock.Now = _clock.Now.AddSeconds(Totp.StepSeconds * 2);

        TwoFactorException failure = await Assert.ThrowsAsync<TwoFactorException>(
            () => _service.VerifyLoginAsync(
                new TwoFactorVerifyRequest { ChallengeToken = challenge.ChallengeToken, Code = CurrentCode() }, default));
        Assert.Equal("TWO_FACTOR_CHALLENGE_INVALID", failure.ErrorCode);
    }

    [Fact]
    public async Task Verify_FiveWrongCodes_BurnsChallenge()
    {
        User admin = AddUser(UserRole.Admin);
        await SetupAndEnableAsync();
        _clock.Now = _clock.Now.AddMinutes(1);
        TwoFactorChallengeResponse challenge = (await _service.ChallengeIfRequiredAsync(admin, false, default))!;
        TwoFactorVerifyRequest wrong = new TwoFactorVerifyRequest { ChallengeToken = challenge.ChallengeToken, Code = "000000" };

        for (int i = 0; i < 5; i++)
        {
            TwoFactorException failure = await Assert.ThrowsAsync<TwoFactorException>(() => _service.VerifyLoginAsync(wrong, default));
            Assert.Equal("INVALID_TWO_FACTOR_CODE", failure.ErrorCode);
        }

        // Sau 5 lần sai, ngay cả mã đúng cũng không dùng được thử thách này nữa.
        TwoFactorVerifyRequest correct = new TwoFactorVerifyRequest { ChallengeToken = challenge.ChallengeToken, Code = CurrentCode() };
        TwoFactorException burned = await Assert.ThrowsAsync<TwoFactorException>(() => _service.VerifyLoginAsync(correct, default));
        Assert.Equal("TWO_FACTOR_CHALLENGE_INVALID", burned.ErrorCode);
        Assert.Empty(_refreshTokens.Tokens);
    }

    [Fact]
    public async Task Verify_ExpiredChallenge_Rejected()
    {
        User admin = AddUser(UserRole.Admin);
        await SetupAndEnableAsync();
        _clock.Now = _clock.Now.AddMinutes(1);
        TwoFactorChallengeResponse challenge = (await _service.ChallengeIfRequiredAsync(admin, false, default))!;
        _clock.Now = _clock.Now.AddMinutes(6);

        TwoFactorException failure = await Assert.ThrowsAsync<TwoFactorException>(
            () => _service.VerifyLoginAsync(
                new TwoFactorVerifyRequest { ChallengeToken = challenge.ChallengeToken, Code = CurrentCode() }, default));
        Assert.Equal("TWO_FACTOR_CHALLENGE_INVALID", failure.ErrorCode);
    }

    [Fact]
    public async Task Verify_RecoveryCode_WorksOnce()
    {
        User admin = AddUser(UserRole.Admin);
        TwoFactorEnableResponse enabled = await SetupAndEnableAsync();
        string recovery = enabled.RecoveryCodes[0].ToLowerInvariant();

        TwoFactorChallengeResponse first = (await _service.ChallengeIfRequiredAsync(admin, false, default))!;
        AuthResult result = await _service.VerifyLoginAsync(
            new TwoFactorVerifyRequest { ChallengeToken = first.ChallengeToken, Code = recovery }, default);
        Assert.NotNull(result.Session.AccessToken);

        TwoFactorChallengeResponse second = (await _service.ChallengeIfRequiredAsync(admin, false, default))!;
        await Assert.ThrowsAsync<TwoFactorException>(
            () => _service.VerifyLoginAsync(new TwoFactorVerifyRequest { ChallengeToken = second.ChallengeToken, Code = recovery }, default));
        TwoFactorStatusResponse status = await _service.GetStatusAsync(default);
        Assert.Equal(7, status.RecoveryCodesRemaining);
    }

    [Fact]
    public async Task Disable_WithValidCode_RemovesEverything()
    {
        User admin = AddUser(UserRole.Admin);
        await SetupAndEnableAsync();
        _clock.Now = _clock.Now.AddMinutes(1);

        await _service.DisableAsync(new TwoFactorCodeRequest { Code = CurrentCode() }, default);

        Assert.Empty(_twoFactor.Totps);
        Assert.Empty(_twoFactor.RecoveryCodes);
        Assert.Null(await _service.ChallengeIfRequiredAsync(admin, false, default));
    }

    [Fact]
    public async Task Disable_WrongCode_KeepsTwoFactor()
    {
        AddUser(UserRole.Admin);
        await SetupAndEnableAsync();

        await Assert.ThrowsAsync<TwoFactorException>(() => _service.DisableAsync(new TwoFactorCodeRequest { Code = "000000" }, default));
        Assert.Single(_twoFactor.Totps);
    }

    private async Task<TwoFactorEnableResponse> SetupAndEnableAsync()
    {
        await _service.SetupAsync(default);
        return await _service.EnableAsync(new TwoFactorCodeRequest { Code = CurrentCode() }, default);
    }

    /// <summary>Mã TOTP đúng tại thời điểm giả lập, tính từ bí mật mà fake protector đang giữ.</summary>
    private string CurrentCode()
    {
        byte[] secret = _protector.Unprotect(_twoFactor.Totps[0]);
        return Totp.ComputeCode(secret, Totp.StepAt(_clock.GetUtcNow()));
    }

    private User AddUser(UserRole role)
    {
        User user = new User
        {
            Email = role.ToString().ToLowerInvariant() + "@example.com",
            FullName = "Quản trị",
            Role = role,
            Status = UserStatus.Active,
            CreatedAt = _clock.GetUtcNow(),
        };
        _users.Users.Add(user);
        _currentUser.UserId = user.Id;
        return user;
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

public sealed class FakeTotpSecretProtector : ITotpSecretProtector
{
    public void Protect(UserTotp totp, byte[] secret)
    {
        totp.KeyId = "test";
        totp.WrappedSecret = (byte[])secret.Clone();
    }

    public byte[] Unprotect(UserTotp totp)
    {
        return (byte[])totp.WrappedSecret.Clone();
    }
}

public sealed class FakeTwoFactorRepository(FakeUserRepository userRepository) : ITwoFactorRepository
{
    public List<UserTotp> Totps { get; } = new List<UserTotp>();

    public List<TwoFactorRecoveryCode> RecoveryCodes { get; } = new List<TwoFactorRecoveryCode>();

    public List<TwoFactorChallenge> Challenges { get; } = new List<TwoFactorChallenge>();

    public Task<UserTotp?> FindTotpAsync(Guid userId, CancellationToken ct)
    {
        return Task.FromResult(Totps.FirstOrDefault(t => t.UserId == userId));
    }

    public void AddTotp(UserTotp totp)
    {
        Totps.Add(totp);
    }

    public void RemoveTotp(UserTotp totp)
    {
        Totps.Remove(totp);
    }

    public Task<List<TwoFactorRecoveryCode>> FindUnusedRecoveryCodesAsync(Guid userId, CancellationToken ct)
    {
        return Task.FromResult(RecoveryCodes.Where(c => c.UserId == userId && c.UsedAt == null).ToList());
    }

    public Task<List<TwoFactorRecoveryCode>> FindAllRecoveryCodesAsync(Guid userId, CancellationToken ct)
    {
        return Task.FromResult(RecoveryCodes.Where(c => c.UserId == userId).ToList());
    }

    public void AddRecoveryCode(TwoFactorRecoveryCode code)
    {
        RecoveryCodes.Add(code);
    }

    public void RemoveRecoveryCode(TwoFactorRecoveryCode code)
    {
        RecoveryCodes.Remove(code);
    }

    public Task<TwoFactorChallenge?> FindChallengeByHashAsync(string tokenHash, CancellationToken ct)
    {
        TwoFactorChallenge? challenge = Challenges.FirstOrDefault(c => c.TokenHash == tokenHash);
        if (challenge != null)
        {
            // Giống Include(User) khi đọc lại từ DB.
            challenge.User = userRepository.Users.First(u => u.Id == challenge.UserId);
        }
        return Task.FromResult(challenge);
    }

    public void AddChallenge(TwoFactorChallenge challenge)
    {
        Challenges.Add(challenge);
    }
}
