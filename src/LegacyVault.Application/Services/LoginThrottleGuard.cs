using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Services;

/// <summary>
/// Đếm số lần nhập sai mật khẩu theo cặp email + dải IP và tạm khóa khi sai quá nhiều (SEC-10).
/// </summary>
/// <remarks>
/// Luật giống AuthService (5 lần sai trong 15 phút → khóa 15 phút) và dùng CHUNG bảng LoginThrottles,
/// nên sai mật khẩu ở màn hình liên kết Google cũng bị tính chung với đăng nhập thường:
/// kẻ dò mật khẩu không lách được giới hạn bằng cách đổi sang endpoint Google.
/// </remarks>
public sealed class LoginThrottleGuard
{
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);
    private const int MaxFailedAttempts = 5;

    private readonly ILoginThrottleRepository _loginThrottleRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public LoginThrottleGuard(
        ILoginThrottleRepository loginThrottleRepository,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _loginThrottleRepository = loginThrottleRepository;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    /// <summary>Đang bị tạm khóa → ném 423 ACCOUNT_LOCKED.</summary>
    public async Task ThrowIfLockedAsync(string email, string ipRange, CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow();
        var throttle = await _loginThrottleRepository.FindAsync(email, ipRange, ct);

        if (throttle != null && throttle.LockedUntil.HasValue && throttle.LockedUntil.Value > now)
        {
            var remainingSeconds = (int)Math.Ceiling((throttle.LockedUntil.Value - now).TotalSeconds);
            throw new AccountLockedException(remainingSeconds);
        }
    }

    /// <summary>
    /// Ghi thêm một lần sai, lưu DB, rồi trả về lỗi để nơi gọi ném ra:
    /// 401 INVALID_CREDENTIALS (còn lượt) hoặc 423 ACCOUNT_LOCKED (vừa hết lượt).
    /// </summary>
    public async Task<ApiException> RecordFailureAsync(string email, string ipRange, CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow();
        var throttle = await _loginThrottleRepository.FindAsync(email, ipRange, ct);

        if (throttle == null)
        {
            throttle = new LoginThrottle
            {
                Email = email,
                IpRange = ipRange,
                FailedCount = 1,
                WindowStartedAt = now,
            };
            _loginThrottleRepository.Add(throttle);
        }
        else if (throttle.WindowStartedAt.Add(FailureWindow) <= now)
        {
            // Cửa sổ 15 phút cũ đã qua: đếm lại từ đầu.
            throttle.FailedCount = 1;
            throttle.WindowStartedAt = now;
            throttle.LockedUntil = null;
        }
        else
        {
            throttle.FailedCount++;
        }

        if (throttle.FailedCount >= MaxFailedAttempts)
        {
            throttle.LockedUntil = now.Add(LockoutDuration);
            await _unitOfWork.SaveChangesAsync(ct);
            return new AccountLockedException((int)LockoutDuration.TotalSeconds);
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return new InvalidCredentialsException(MaxFailedAttempts - throttle.FailedCount);
    }

    /// <summary>Nhập đúng mật khẩu: xóa bộ đếm (chỉ đánh dấu, lưu cùng lần SaveChanges sau).</summary>
    public async Task ClearAsync(string email, string ipRange, CancellationToken ct)
    {
        var throttle = await _loginThrottleRepository.FindAsync(email, ipRange, ct);
        if (throttle != null)
        {
            _loginThrottleRepository.Remove(throttle);
        }
    }
}
