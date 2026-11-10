using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.IntegrationTests.CheckIn;
using LegacyVault.IntegrationTests.GoogleAuth;
using Xunit;

namespace LegacyVault.IntegrationTests.Auth;

/// <summary>Mỗi số điện thoại chỉ thuộc một tài khoản, mọi vai trò: đăng ký và tự đổi số đều bị chặn khi số đã có người dùng.</summary>
public sealed class UniquePhoneTests
{
    private const string TakenPhone = "0934806311";

    private readonly FakeUserRepository users = new FakeUserRepository();
    private readonly FakeUnitOfWork unitOfWork = new FakeUnitOfWork();
    private readonly FixedTimeProvider clock = new FixedTimeProvider(new DateTimeOffset(2026, 11, 10, 8, 0, 0, TimeSpan.Zero));

    private User AddUser(string email, UserRole role, string? phone)
    {
        User user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            FullName = "Người Dùng",
            Phone = phone,
            Role = role,
            Status = UserStatus.Active,
            CreatedAt = clock.Now,
        };
        users.Users.Add(user);
        return user;
    }

    private AccountPhoneService PhoneServiceFor(User user)
    {
        FakeCurrentUser currentUser = new FakeCurrentUser { UserId = user.Id };
        return new AccountPhoneService(currentUser, users, unitOfWork, clock);
    }

    private RegistrationService Registration()
    {
        return new RegistrationService(
            users,
            new NoEmailVerificationTokens(),
            unitOfWork,
            new FakePasswordHasher(),
            new FakeTokenService(),
            new CapturingPasswordResetEmailSender(),
            new FakeAppUrlProvider(),
            clock);
    }

    [Theory]
    [InlineData(UserRole.User)]
    [InlineData(UserRole.Verifier)]
    [InlineData(UserRole.Admin)]
    public async Task Register_PhoneOwnedByAnyRole_Returns409AndCreatesNothing(UserRole holderRole)
    {
        AddUser("holder@example.com", holderRole, TakenPhone);

        AppException error = await Assert.ThrowsAsync<AppException>(() => Registration().RegisterAsync(
            new RegisterRequest { Email = "new@example.com", FullName = "Người Mới", Password = "MatKhauThu123!", Phone = "0934 806 311" },
            CancellationToken.None));

        Assert.Equal(AppErrorType.Conflict, error.Type);
        Assert.Equal(ErrorCode.PhoneAlreadyExists, error.Code);
        Assert.Equal(PhoneRules.AlreadyUsedMessage, error.Message);
        Assert.DoesNotContain(users.Users, u => u.Email == "new@example.com");
    }

    [Fact]
    public async Task Register_ExistingEmail_StillReportsEmailFirst()
    {
        AddUser("same@example.com", UserRole.User, TakenPhone);

        AppException error = await Assert.ThrowsAsync<AppException>(() => Registration().RegisterAsync(
            new RegisterRequest { Email = "same@example.com", FullName = "Người Mới", Password = "MatKhauThu123!", Phone = TakenPhone },
            CancellationToken.None));

        Assert.Equal(ErrorCode.EmailAlreadyExists, error.Code);
    }

    [Fact]
    public async Task SetMyPhone_NumberOfAnotherAccount_Returns409AndKeepsOldNumber()
    {
        AddUser("verifier@example.com", UserRole.Verifier, TakenPhone);
        User me = AddUser("me@example.com", UserRole.User, "0900000001");

        AppException error = await Assert.ThrowsAsync<AppException>(() => PhoneServiceFor(me).SetMyPhoneAsync(
            new SetPhoneRequest { Phone = "0934.806.311" }, CancellationToken.None));

        Assert.Equal(ErrorCode.PhoneAlreadyExists, error.Code);
        Assert.Equal("0900000001", me.Phone);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task SetMyPhone_SameNumberAsMine_IsAccepted()
    {
        User me = AddUser("me@example.com", UserRole.User, TakenPhone);

        await PhoneServiceFor(me).SetMyPhoneAsync(new SetPhoneRequest { Phone = "0934 806 311" }, CancellationToken.None);

        Assert.Equal(TakenPhone, me.Phone);
    }

    [Fact]
    public async Task SetMyPhone_FreeNumber_IsSaved()
    {
        AddUser("other@example.com", UserRole.User, TakenPhone);
        User me = AddUser("me@example.com", UserRole.User, null);

        await PhoneServiceFor(me).SetMyPhoneAsync(new SetPhoneRequest { Phone = "0911222333" }, CancellationToken.None);

        Assert.Equal("0911222333", me.Phone);
        Assert.Equal(1, unitOfWork.SaveCount);
    }

    private sealed class NoEmailVerificationTokens : Application.Repositories.IEmailVerificationTokenRepository
    {
        public Task<EmailVerificationToken?> FindByHashAsync(string tokenHash, CancellationToken ct)
        {
            return Task.FromResult<EmailVerificationToken?>(null);
        }

        public Task<List<EmailVerificationToken>> FindUnusedByUserAsync(Guid userId, CancellationToken ct)
        {
            return Task.FromResult(new List<EmailVerificationToken>());
        }

        public void Add(EmailVerificationToken token)
        {
        }
    }
}
