using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Services;

/// <summary>
/// DEATH-03: Verifier nhập lại mật khẩu khi Duyệt/Từ chối. Sai 5 lần liên tiếp thì khóa chức năng duyệt/từ chối
/// 15 phút → 1 giờ → 24 giờ; dùng lại hằng của VaultPinRules (cách viết theo VaultPinService.CheckPinAsync).
/// </summary>
/// <remarks>
/// - Chỉ khóa chức năng duyệt/từ chối, không khóa đăng nhập hay xem hồ sơ.
/// - SEC-07: không log, không ghi nhật ký mật khẩu.
/// - Ngoại lệ có chủ đích của "một lần lưu mỗi use case": nhập sai thì lưu bộ đếm ngay trước khi ném lỗi,
///   lúc này chưa có thay đổi nào khác đang chờ lưu.
/// </remarks>
public sealed class SigningPasswordChecker : ISigningPasswordChecker
{
    private readonly IVerifierSigningGuardRepository signingGuardRepository;
    private readonly IPasswordHasher passwordHasher;
    private readonly IUnitOfWork unitOfWork;

    public SigningPasswordChecker(
        IVerifierSigningGuardRepository signingGuardRepository,
        IPasswordHasher passwordHasher,
        IUnitOfWork unitOfWork)
    {
        this.signingGuardRepository = signingGuardRepository;
        this.passwordHasher = passwordHasher;
        this.unitOfWork = unitOfWork;
    }

    public async Task CheckAsync(User user, string password, DateTimeOffset now, CancellationToken ct)
    {
        // 1) Đang khóa thì chặn, kể cả nhập đúng.
        VerifierSigningGuard? guard = await signingGuardRepository.FindGuardAsync(user.Id, ct);
        if (guard != null && guard.LockedUntil != null && guard.LockedUntil.Value > now)
        {
            throw Locked(guard.LockedUntil.Value);
        }

        // 2) Tài khoản chỉ đăng nhập Google thì chưa xác nhận được; không tính là một lần đoán.
        if (string.IsNullOrEmpty(user.PasswordHash))
        {
            throw new AppException(AppErrorType.Forbidden, ErrorCode.SigningPasswordInvalid,
                "Tài khoản chưa có mật khẩu nên chưa xác nhận được. Vui lòng đặt mật khẩu trước.");
        }

        // 3) Đúng: bộ đếm và bậc khóa về 0, lưu cùng lần lưu của quyết định.
        bool isCorrect = false;
        if (!string.IsNullOrEmpty(password))
        {
            isCorrect = passwordHasher.Verify(password, user.PasswordHash);
        }
        if (isCorrect)
        {
            if (guard != null && (guard.FailedCount != 0 || guard.LockLevel != 0 || guard.LockedUntil != null))
            {
                guard.FailedCount = 0;
                guard.LockLevel = 0;
                guard.LockedUntil = null;
                guard.UpdatedAt = now;
            }
            return;
        }

        // 4) Sai: chưa có dòng thì tạo; đủ số lần thì khóa và tăng bậc.
        if (guard == null)
        {
            guard = new VerifierSigningGuard();
            guard.UserId = user.Id;
            signingGuardRepository.AddGuard(guard);
        }
        guard.FailedCount = guard.FailedCount + 1;
        guard.UpdatedAt = now;
        bool locks = guard.FailedCount >= VaultPinRules.MaxFailedAttempts;
        DateTimeOffset lockedUntil = now;
        if (locks)
        {
            guard.LockLevel = Math.Min(guard.LockLevel + 1, VaultPinRules.MaxLockLevel);
            lockedUntil = now + VaultPinRules.LockDuration(guard.LockLevel);
            guard.LockedUntil = lockedUntil;
            guard.FailedCount = 0;
        }

        // 5) Lưu ngay để request thất bại vẫn đếm được lần sai.
        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (IsGuardRace(ex))
        {
            // Request khác cùng lúc vừa sửa hoặc vừa tạo dòng đầu tiên: báo thử lại, không trả 500.
            throw new AppException(AppErrorType.Conflict, ErrorCode.EditConflict,
                "Đang có thao tác xác nhận khác cùng lúc. Vui lòng thử lại.");
        }

        if (locks)
        {
            throw Locked(lockedUntil);
        }
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["attemptsRemaining"] = VaultPinRules.MaxFailedAttempts - guard.FailedCount;
        throw new AppException(AppErrorType.Forbidden, ErrorCode.SigningPasswordInvalid,
            "Mật khẩu xác nhận chưa đúng.", extensions);
    }

    private bool IsGuardRace(Exception exception)
    {
        AppException? appException = exception as AppException;
        if (appException != null && appException.Code == ErrorCode.EditConflict)
        {
            return true;
        }
        return signingGuardRepository.IsUniqueViolation(exception);
    }

    private static AppException Locked(DateTimeOffset lockedUntil)
    {
        Dictionary<string, object?> extensions = new Dictionary<string, object?>();
        extensions["lockedUntil"] = lockedUntil;
        return new AppException(AppErrorType.Locked, ErrorCode.SigningLocked,
            "Chức năng duyệt/từ chối đang tạm khóa vì nhập sai mật khẩu nhiều lần. Vui lòng thử lại sau.", extensions);
    }
}
