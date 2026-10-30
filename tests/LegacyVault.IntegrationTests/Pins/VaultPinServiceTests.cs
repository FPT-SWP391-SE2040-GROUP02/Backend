using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Services;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.Security;
using LegacyVault.IntegrationTests.Auth;
using LegacyVault.IntegrationTests.CheckIn;
using LegacyVault.IntegrationTests.GoogleAuth;
using Xunit;

namespace LegacyVault.IntegrationTests.Pins;

/// <summary>
/// Test VaultPinService với dữ liệu trong RAM (không cần SQL Server): đặt, đổi, quên PIN, sai PIN và khóa lũy tiến.
/// </summary>
public sealed class VaultPinServiceTests
{
    private const string GoodPin = "482913";
    private const string OtherPin = "715284";
    private const string Password = "Matkhau-cua-toi-1";

    private readonly Guid ownerId = Guid.NewGuid();
    private readonly Vault vault;
    private readonly User owner;
    private readonly FakeCurrentUser currentUser = new FakeCurrentUser();
    private readonly FakeVaultRepository vaults = new FakeVaultRepository();
    private readonly FakePinRepository pins = new FakePinRepository();
    private readonly FakeAssetCounter assets = new FakeAssetCounter();
    private readonly FakeUserRepository users = new FakeUserRepository();
    private readonly CapturingPasswordResetEmailSender email = new CapturingPasswordResetEmailSender();
    private readonly CapturingAuditRepository audit = new CapturingAuditRepository();
    private readonly SteppingTimeProvider clock = new SteppingTimeProvider(new DateTimeOffset(2026, 10, 30, 8, 0, 0, TimeSpan.Zero));
    private readonly FakePasswordHasher passwordHasher = new FakePasswordHasher();
    private readonly VaultPinService service;

    public VaultPinServiceTests()
    {
        owner = new User { Id = ownerId, Email = "owner@test.local", FullName = "Chủ kho", PasswordHash = passwordHasher.Hash(Password) };
        users.Users.Add(owner);
        vault = new Vault { OwnerId = ownerId };
        vaults.Vaults.Add(vault);
        currentUser.UserId = ownerId;
        service = new VaultPinService(
            currentUser, vaults, pins, assets, new VaultPinHasher(new Sha256PasswordHasher()), passwordHasher, users, email,
            audit, new FakeUnitOfWork(), clock);
    }

    // ---- Kiểm tra PIN trước thao tác được bảo vệ -------------------------------------------------------------

    [Fact]
    public async Task Verify_PassesWhenVaultHasNoPinAndNoAssets()
    {
        await service.VerifyAsync(vault.Id, null, CancellationToken.None);
    }

    [Fact]
    public async Task Verify_RequiresPinToBeSetOnceVaultHasAssets()
    {
        assets.Count = 1;

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.VerifyAsync(vault.Id, GoodPin, CancellationToken.None));

        Assert.Equal(AppErrorType.Conflict, error.Type);
        Assert.Equal(ErrorCode.VaultPinNotSet, error.Code);
    }

    [Fact]
    public async Task Verify_AcceptsCorrectPin()
    {
        await SetPinAsync();

        await service.VerifyAsync(vault.Id, GoodPin, CancellationToken.None);
    }

    [Fact]
    public async Task Verify_OnAnotherUsersVault_Returns404LikeOtherApis()
    {
        await SetPinAsync();
        currentUser.UserId = Guid.NewGuid();

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.VerifyAsync(vault.Id, GoodPin, CancellationToken.None));

        Assert.Equal(AppErrorType.NotFound, error.Type);
        Assert.Equal(ErrorCode.VaultNotFound, error.Code);
    }

    [Fact]
    public async Task Verify_WithoutPinHeader_AsksForPinAndDoesNotCountAsAGuess()
    {
        await SetPinAsync();

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.VerifyAsync(vault.Id, null, CancellationToken.None));

        Assert.Equal(ErrorCode.VaultPinRequired, error.Code);
        Assert.Equal(AppErrorType.Forbidden, error.Type);
        Assert.Equal(0, pins.Current!.FailedCount);
    }

    [Fact]
    public async Task Verify_WrongPin_Returns403WithAttemptsRemaining()
    {
        await SetPinAsync();

        AppException first = await Assert.ThrowsAsync<AppException>(
            () => service.VerifyAsync(vault.Id, OtherPin, CancellationToken.None));
        AppException second = await Assert.ThrowsAsync<AppException>(
            () => service.VerifyAsync(vault.Id, OtherPin, CancellationToken.None));

        Assert.Equal(AppErrorType.Forbidden, first.Type);
        Assert.Equal(ErrorCode.InvalidVaultPin, first.Code);
        Assert.Equal(4, first.Extensions["attemptsRemaining"]);
        Assert.Equal(3, second.Extensions["attemptsRemaining"]);
        Assert.Equal(2, audit.Logs.Count(l => l.Action == AuditAction.VaultPinWrong));
    }

    [Fact]
    public async Task Verify_MalformedPinCountsAsWrong()
    {
        await SetPinAsync();

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.VerifyAsync(vault.Id, "12ab", CancellationToken.None));

        Assert.Equal(ErrorCode.InvalidVaultPin, error.Code);
        Assert.Equal(1, pins.Current!.FailedCount);
    }

    [Fact]
    public async Task Verify_CorrectPinResetsTheFailureCounter()
    {
        await SetPinAsync();
        await WrongGuessesAsync(3);

        await service.VerifyAsync(vault.Id, GoodPin, CancellationToken.None);

        Assert.Equal(0, pins.Current!.FailedCount);
    }

    // ---- Khóa lũy tiến -------------------------------------------------------------------------------------

    [Fact]
    public async Task Verify_FiveWrongPins_LockForFifteenMinutesAndEmailTheOwner()
    {
        await SetPinAsync();
        await WrongGuessesAsync(4);

        AppException locked = await Assert.ThrowsAsync<AppException>(
            () => service.VerifyAsync(vault.Id, OtherPin, CancellationToken.None));

        Assert.Equal(AppErrorType.Locked, locked.Type);
        Assert.Equal(ErrorCode.VaultPinLocked, locked.Code);
        Assert.Equal(clock.Now.AddMinutes(15), locked.Extensions["lockedUntil"]);
        Assert.Contains(email.PinNotices, n => n.Kind == VaultPinNoticeKind.Locked && n.ToEmail == owner.Email);
        Assert.Contains(audit.Logs, l => l.Action == AuditAction.VaultPinLocked);
    }

    [Fact]
    public async Task Verify_WhileLocked_EvenTheCorrectPinIsRefused()
    {
        await SetPinAsync();
        await LockAsync();

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.VerifyAsync(vault.Id, GoodPin, CancellationToken.None));

        Assert.Equal(ErrorCode.VaultPinLocked, error.Code);
    }

    [Fact]
    public async Task Verify_AfterTheLockExpires_CorrectPinWorksAgain()
    {
        await SetPinAsync();
        await LockAsync();
        clock.Now = clock.Now.AddMinutes(16);

        await service.VerifyAsync(vault.Id, GoodPin, CancellationToken.None);

        Assert.Equal(0, pins.Current!.LockLevel);
        Assert.Null(pins.Current.LockedUntil);
    }

    [Fact]
    public async Task Verify_RepeatedLocks_EscalateToOneHourThenTwentyFourHoursAndStayThere()
    {
        await SetPinAsync();
        DateTimeOffset firstStart = clock.Now;
        DateTimeOffset firstLockEnd = await LockAsync();
        clock.Now = firstLockEnd.AddSeconds(1);

        DateTimeOffset secondStart = clock.Now;
        DateTimeOffset secondLockEnd = await LockAsync();
        clock.Now = secondLockEnd.AddSeconds(1);

        DateTimeOffset thirdStart = clock.Now;
        DateTimeOffset thirdLockEnd = await LockAsync();
        clock.Now = thirdLockEnd.AddSeconds(1);

        DateTimeOffset fourthStart = clock.Now;
        DateTimeOffset fourthLockEnd = await LockAsync();

        Assert.Equal(TimeSpan.FromMinutes(15), firstLockEnd - firstStart);
        Assert.Equal(TimeSpan.FromHours(1), secondLockEnd - secondStart);
        Assert.Equal(TimeSpan.FromHours(24), thirdLockEnd - thirdStart);
        Assert.Equal(TimeSpan.FromHours(24), fourthLockEnd - fourthStart);
        Assert.Equal(3, pins.Current!.LockLevel);
    }

    // ---- Đặt PIN lần đầu -------------------------------------------------------------------------------------

    [Fact]
    public async Task Set_StoresAHashNeverThePinAndEmailsTheOwner()
    {
        VaultPinStatusResponse status = await SetPinAsync();

        Assert.True(status.IsSet);
        Assert.DoesNotContain(GoodPin, pins.Current!.PinHash);
        Assert.Contains(audit.Logs, l => l.Action == AuditAction.VaultPinSet && l.TargetId == vault.Id);
        Assert.Contains(email.PinNotices, n => n.Kind == VaultPinNoticeKind.Set && n.ToEmail == owner.Email);
        Assert.DoesNotContain(audit.Logs, l => (l.Reason ?? string.Empty).Contains(GoodPin));
    }

    [Fact]
    public async Task Set_WhenAlreadySet_ReturnsConflict()
    {
        await SetPinAsync();

        AppException error = await Assert.ThrowsAsync<AppException>(() => SetPinAsync());

        Assert.Equal(AppErrorType.Conflict, error.Type);
        Assert.Equal(ErrorCode.VaultPinAlreadySet, error.Code);
    }

    [Fact]
    public async Task Set_RejectsWeakPin()
    {
        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.SetPinAsync(vault.Id, new SetVaultPinRequest { Pin = "123456", ConfirmPin = "123456" }, CancellationToken.None));

        Assert.Equal(ErrorCode.VaultPinTooWeak, error.Code);
        Assert.Null(pins.Current);
    }

    [Fact]
    public async Task Set_RejectsMismatchedConfirmation()
    {
        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.SetPinAsync(vault.Id, new SetVaultPinRequest { Pin = GoodPin, ConfirmPin = OtherPin }, CancellationToken.None));

        Assert.Equal(ErrorCode.ValidationError, error.Code);
        Assert.Null(pins.Current);
    }

    // ---- Chặn thêm tài sản khi chưa có PIN ------------------------------------------------------------------

    [Fact]
    public async Task EnsurePinSetBeforeNewAsset_PassesForTheFirstAsset()
    {
        assets.Count = 0;

        await service.EnsurePinSetBeforeNewAssetAsync(vault.Id, CancellationToken.None);
    }

    [Fact]
    public async Task EnsurePinSetBeforeNewAsset_BlocksTheSecondAssetUntilPinIsSet()
    {
        assets.Count = 1;

        AppException error = await Assert.ThrowsAsync<AppException>(
            () => service.EnsurePinSetBeforeNewAssetAsync(vault.Id, CancellationToken.None));
        Assert.Equal(ErrorCode.VaultPinNotSet, error.Code);

        await SetPinAsync();
        await service.EnsurePinSetBeforeNewAssetAsync(vault.Id, CancellationToken.None);
    }

    // ---- Đổi PIN -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Change_WithCurrentPin_SwitchesToTheNewPinAndEmailsTheOwner()
    {
        await SetPinAsync();

        await service.ChangePinAsync(vault.Id,
            new ChangeVaultPinRequest { CurrentPin = GoodPin, NewPin = OtherPin, ConfirmPin = OtherPin }, CancellationToken.None);

        await service.VerifyAsync(vault.Id, OtherPin, CancellationToken.None);
        AppException oldPin = await Assert.ThrowsAsync<AppException>(
            () => service.VerifyAsync(vault.Id, GoodPin, CancellationToken.None));
        Assert.Equal(ErrorCode.InvalidVaultPin, oldPin.Code);
        Assert.Contains(email.PinNotices, n => n.Kind == VaultPinNoticeKind.Changed);
        Assert.Contains(audit.Logs, l => l.Action == AuditAction.VaultPinChanged);
    }

    [Fact]
    public async Task Change_WithWrongCurrentPin_IsCountedAndKeepsTheOldPin()
    {
        await SetPinAsync();

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.ChangePinAsync(vault.Id,
            new ChangeVaultPinRequest { CurrentPin = "999000", NewPin = OtherPin, ConfirmPin = OtherPin }, CancellationToken.None));

        Assert.Equal(ErrorCode.InvalidVaultPin, error.Code);
        Assert.Equal(1, pins.Current!.FailedCount);
        await service.VerifyAsync(vault.Id, GoodPin, CancellationToken.None);
    }

    [Fact]
    public async Task Change_WithoutAnyPin_ReturnsNotSet()
    {
        AppException error = await Assert.ThrowsAsync<AppException>(() => service.ChangePinAsync(vault.Id,
            new ChangeVaultPinRequest { CurrentPin = GoodPin, NewPin = OtherPin, ConfirmPin = OtherPin }, CancellationToken.None));

        Assert.Equal(ErrorCode.VaultPinNotSet, error.Code);
    }

    // ---- Quên PIN: nhập mật khẩu rồi đặt lại ngay ------------------------------------------------------------------

    [Fact]
    public async Task Reset_WithCorrectPassword_ReplacesThePinAtOnceAndClearsTheLock()
    {
        await SetPinAsync();
        await LockAsync();

        VaultPinStatusResponse status = await service.ResetPinAsync(vault.Id,
            new ResetVaultPinRequest { Password = Password, NewPin = OtherPin, ConfirmPin = OtherPin }, CancellationToken.None);

        Assert.Null(status.LockedUntil);
        await service.VerifyAsync(vault.Id, OtherPin, CancellationToken.None);
        Assert.Equal(0, pins.Current!.LockLevel);
        Assert.Contains(email.PinNotices, n => n.Kind == VaultPinNoticeKind.Reset && n.ToEmail == owner.Email);
        Assert.Contains(audit.Logs, l => l.Action == AuditAction.VaultPinReset);
    }

    [Fact]
    public async Task Reset_WithWrongPassword_IsRefusedAndKeepsThePin()
    {
        await SetPinAsync();

        await Assert.ThrowsAsync<ReauthenticationFailedException>(() => service.ResetPinAsync(vault.Id,
            new ResetVaultPinRequest { Password = "sai-mat-khau", NewPin = OtherPin, ConfirmPin = OtherPin }, CancellationToken.None));

        await service.VerifyAsync(vault.Id, GoodPin, CancellationToken.None);
        Assert.DoesNotContain(email.PinNotices, n => n.Kind == VaultPinNoticeKind.Reset);
    }

    [Fact]
    public async Task Reset_ForAccountWithoutPassword_ReturnsConflict()
    {
        await SetPinAsync();
        owner.PasswordHash = null;

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.ResetPinAsync(vault.Id,
            new ResetVaultPinRequest { Password = Password, NewPin = OtherPin, ConfirmPin = OtherPin }, CancellationToken.None));

        Assert.Equal(AppErrorType.Conflict, error.Type);
        Assert.Equal(ErrorCode.VaultPinResetNeedsPassword, error.Code);
    }

    [Fact]
    public async Task Reset_RejectsWeakNewPin()
    {
        await SetPinAsync();

        AppException error = await Assert.ThrowsAsync<AppException>(() => service.ResetPinAsync(vault.Id,
            new ResetVaultPinRequest { Password = Password, NewPin = "000000", ConfirmPin = "000000" }, CancellationToken.None));

        Assert.Equal(ErrorCode.VaultPinTooWeak, error.Code);
    }

    [Fact]
    public async Task Reset_WhenNoPinWasEverSet_ReturnsNotSet()
    {
        AppException error = await Assert.ThrowsAsync<AppException>(() => service.ResetPinAsync(vault.Id,
            new ResetVaultPinRequest { Password = Password, NewPin = OtherPin, ConfirmPin = OtherPin }, CancellationToken.None));

        Assert.Equal(ErrorCode.VaultPinNotSet, error.Code);
    }

    // ---- Trạng thái ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Status_ReportsRequiredSetAttemptsAndLock()
    {
        assets.Count = 1;
        VaultPinStatusResponse before = await service.GetStatusAsync(vault.Id, CancellationToken.None);
        Assert.False(before.IsSet);
        Assert.True(before.IsRequired);
        Assert.Null(before.AttemptsRemaining);

        await SetPinAsync();
        await WrongGuessesAsync(2);
        VaultPinStatusResponse afterGuesses = await service.GetStatusAsync(vault.Id, CancellationToken.None);
        Assert.True(afterGuesses.IsSet);
        Assert.Equal(3, afterGuesses.AttemptsRemaining);
        Assert.Null(afterGuesses.LockedUntil);

        DateTimeOffset lockedUntil = await LockAsync(alreadyWrong: 2);
        VaultPinStatusResponse locked = await service.GetStatusAsync(vault.Id, CancellationToken.None);
        Assert.Equal(lockedUntil, locked.LockedUntil);
    }

    // ---- Hỗ trợ ---------------------------------------------------------------------------------------------------

    private Task<VaultPinStatusResponse> SetPinAsync()
    {
        return service.SetPinAsync(vault.Id, new SetVaultPinRequest { Pin = GoodPin, ConfirmPin = GoodPin }, CancellationToken.None);
    }

    private async Task WrongGuessesAsync(int count)
    {
        for (int i = 0; i < count; i++)
        {
            await Assert.ThrowsAsync<AppException>(() => service.VerifyAsync(vault.Id, OtherPin, CancellationToken.None));
        }
    }

    /// <summary>Nhập sai cho tới khi bị khóa (tính cả số lần đã sai sẵn), trả thời điểm hết khóa.</summary>
    private async Task<DateTimeOffset> LockAsync(int alreadyWrong = 0)
    {
        await WrongGuessesAsync(VaultPinRules.MaxFailedAttempts - 1 - alreadyWrong);
        AppException locked = await Assert.ThrowsAsync<AppException>(
            () => service.VerifyAsync(vault.Id, OtherPin, CancellationToken.None));
        Assert.Equal(ErrorCode.VaultPinLocked, locked.Code);
        return (DateTimeOffset)locked.Extensions["lockedUntil"]!;
    }

    private sealed class SteppingTimeProvider(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakePinRepository : IVaultPinRepository
    {
        public VaultPin? Current { get; private set; }

        public Task<VaultPin?> FindByVaultIdAsync(Guid vaultId, CancellationToken ct)
        {
            return Task.FromResult(Current != null && Current.VaultId == vaultId ? Current : null);
        }

        public void Add(VaultPin pin)
        {
            Current = pin;
        }

        public bool IsUniqueViolation(Exception exception) => false;

        public void DiscardPendingChanges()
        {
        }
    }

    private sealed class FakeAssetCounter : IAssetRepository
    {
        public int Count { get; set; }

        public Task<int> CountByVaultIdAsync(Guid vaultId, CancellationToken ct) => Task.FromResult(Count);

        public Task<Asset?> FindByIdAsync(Guid vaultId, Guid assetId, CancellationToken ct) => throw new NotSupportedException();

        public Task<List<Asset>> FindOutsideContainerByVaultIdAsync(Guid vaultId, CancellationToken ct) => throw new NotSupportedException();

        public Task<(List<Asset> Items, int TotalCount)> FindPageByVaultIdAsync(
            Guid vaultId, int pageIndex, int pageSize, string? searchTerm, string sortBy, bool isAscending, CancellationToken ct)
            => throw new NotSupportedException();

        public void Add(Asset asset) => throw new NotSupportedException();

        public Task<bool> IsPinnedInHandoverAsync(Guid assetId, CancellationToken ct) => throw new NotSupportedException();

        public void Remove(Asset asset) => throw new NotSupportedException();
    }

    private sealed class CapturingAuditRepository : IAuditLogRepository
    {
        public List<AuditLog> Logs { get; } = new List<AuditLog>();

        public void Add(AuditLog log) => Logs.Add(log);

        public Task<(List<AuditLog> Items, int TotalCount)> FindPageByVaultIdAsync(
            Guid vaultId, AuditAction? action, DateTimeOffset? from, DateTimeOffset? to,
            int pageIndex, int pageSize, bool isAscending, CancellationToken ct)
            => throw new NotSupportedException();
    }
}
