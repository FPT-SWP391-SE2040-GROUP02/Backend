using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

public sealed class AuthService : IAuthService
{
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MaxSessionLifetime = TimeSpan.FromHours(12);
    private const int MaxFailedAttempts = 5;

    private static string? _dummyHash;

    private readonly IUserRepository _userRepository;
    private readonly ILoginThrottleRepository _loginThrottleRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly ICurrentUser _currentUser;
    private readonly TimeProvider _timeProvider;

    public AuthService(
        IUserRepository userRepository,
        ILoginThrottleRepository loginThrottleRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        ICurrentUser currentUser,
        TimeProvider timeProvider)
    {
        _userRepository = userRepository;
        _loginThrottleRepository = loginThrottleRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, string ipRange, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var now = _timeProvider.GetUtcNow();

        // 1. Kiểm tra tạm khóa theo cặp email + dải IP (SEC-10)
        var throttle = await _loginThrottleRepository.FindAsync(email, ipRange, ct);
        if (throttle != null && throttle.LockedUntil.HasValue && throttle.LockedUntil.Value > now)
        {
            var remaining = (int)Math.Ceiling((throttle.LockedUntil.Value - now).TotalSeconds);
            throw new AccountLockedException(remaining);
        }

        // 2. Tìm tài khoản
        var user = await _userRepository.FindByEmailAsync(email, ct);

        // 3. Nếu không có user hoặc tài khoản không có mật khẩu (chỉ dùng Google)
        if (user == null || string.IsNullOrEmpty(user.PasswordHash))
        {
            _dummyHash ??= _passwordHasher.Hash(Guid.NewGuid().ToString());
            _passwordHasher.Verify(request.Password, _dummyHash);

            throw await RecordFailedLoginAsync(throttle, email, ipRange, now, ct);
        }

        // 4. Kiểm tra mật khẩu
        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw await RecordFailedLoginAsync(throttle, email, ipRange, now, ct);
        }

        // 5. Kiểm tra trạng thái tài khoản SAU khi mật khẩu đúng (để người lạ không dò được trạng thái)
        if (user.Status != UserStatus.Active)
        {
            throw new AccountNotActiveException();
        }

        // 6. Đăng nhập đúng: xóa bộ đếm sai nếu có
        if (throttle != null)
        {
            _loginThrottleRepository.Remove(throttle);
        }

        user.LastLoginAt = now;

        // 7. Mở phiên mới (FamilyId đại diện cho một lần đăng nhập của thiết bị)
        var familyId = Guid.CreateVersion7();
        var opaque = _tokenService.CreateOpaqueToken();
        var initialExpiresAt = now.Add(IdleTimeout);
        var maxExpiresAt = now.Add(MaxSessionLifetime);
        if (initialExpiresAt > maxExpiresAt)
        {
            initialExpiresAt = maxExpiresAt;
        }

        var refreshTokenEntity = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = opaque.Hash,
            FamilyId = familyId,
            SessionStartedAt = now,
            CreatedAt = now,
            ExpiresAt = initialExpiresAt,
            IsPersistent = request.RememberMe,
            RevokedAt = null,
        };

        _refreshTokenRepository.Add(refreshTokenEntity);

        var accessToken = _tokenService.CreateAccessToken(user);
        await _unitOfWork.SaveChangesAsync(ct);

        return new AuthResult
        {
            Session = new AuthSessionResponse
            {
                AccessToken = accessToken.Value,
                ExpiresIn = (int)Math.Max(0, (accessToken.ExpiresAt - now).TotalSeconds),
                User = MapUser(user),
            },
            RefreshToken = opaque.Value,
            RefreshTokenExpiresAt = maxExpiresAt,
            IsPersistent = request.RememberMe,
        };
    }

    public async Task<AuthResult> RefreshAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new SessionExpiredException();
        }

        var now = _timeProvider.GetUtcNow();
        var hash = _tokenService.HashOpaqueToken(refreshToken);

        var tokenEntity = await _refreshTokenRepository.FindByHashAsync(hash, ct);
        if (tokenEntity == null)
        {
            throw new SessionExpiredException();
        }

        // Phát hiện dùng lại token đã thu hồi: xâm phạm bảo mật, thu hồi cả family
        if (tokenEntity.RevokedAt != null)
        {
            var familyTokens = await _refreshTokenRepository.FindActiveByFamilyAsync(tokenEntity.FamilyId, ct);
            foreach (var t in familyTokens)
            {
                t.RevokedAt = now;
            }
            await _unitOfWork.SaveChangesAsync(ct);
            throw new SessionExpiredException();
        }

        // Kiểm tra thời hạn phiên (tối đa 12 giờ hoặc idle 30 phút, NFR-06)
        var maxSessionLimit = tokenEntity.SessionStartedAt.Add(MaxSessionLifetime);
        if (maxSessionLimit <= now || tokenEntity.ExpiresAt <= now)
        {
            tokenEntity.RevokedAt = now;
            await _unitOfWork.SaveChangesAsync(ct);
            throw new SessionExpiredException();
        }

        // Kiểm tra tài khoản còn Active không
        if (tokenEntity.User.Status != UserStatus.Active)
        {
            tokenEntity.RevokedAt = now;
            await _unitOfWork.SaveChangesAsync(ct);
            throw new SessionExpiredException();
        }

        // Xoay vòng token
        tokenEntity.RevokedAt = now;

        var newOpaque = _tokenService.CreateOpaqueToken();
        var newExpiresAt = now.Add(IdleTimeout);
        if (newExpiresAt > maxSessionLimit)
        {
            newExpiresAt = maxSessionLimit;
        }

        var newTokenEntity = new RefreshToken
        {
            UserId = tokenEntity.UserId,
            TokenHash = newOpaque.Hash,
            FamilyId = tokenEntity.FamilyId,
            SessionStartedAt = tokenEntity.SessionStartedAt,
            CreatedAt = now,
            ExpiresAt = newExpiresAt,
            IsPersistent = tokenEntity.IsPersistent,
            RevokedAt = null,
        };

        tokenEntity.ReplacedByTokenId = newTokenEntity.Id;
        _refreshTokenRepository.Add(newTokenEntity);

        var newAccessToken = _tokenService.CreateAccessToken(tokenEntity.User);
        await _unitOfWork.SaveChangesAsync(ct);

        return new AuthResult
        {
            Session = new AuthSessionResponse
            {
                AccessToken = newAccessToken.Value,
                ExpiresIn = (int)Math.Max(0, (newAccessToken.ExpiresAt - now).TotalSeconds),
                User = MapUser(tokenEntity.User),
            },
            RefreshToken = newOpaque.Value,
            RefreshTokenExpiresAt = maxSessionLimit,
            IsPersistent = tokenEntity.IsPersistent,
        };
    }

    public async Task LogoutAsync(string? refreshToken, bool revokeAllDevices, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        var hash = _tokenService.HashOpaqueToken(refreshToken);

        var tokenEntity = await _refreshTokenRepository.FindByHashAsync(hash, ct);
        if (tokenEntity == null)
        {
            return;
        }

        if (revokeAllDevices)
        {
            var allTokens = await _refreshTokenRepository.FindActiveByUserAsync(tokenEntity.UserId, ct);
            foreach (var t in allTokens)
            {
                t.RevokedAt = now;
            }
        }
        else
        {
            var familyTokens = await _refreshTokenRepository.FindActiveByFamilyAsync(tokenEntity.FamilyId, ct);
            foreach (var t in familyTokens)
            {
                t.RevokedAt = now;
            }
        }

        await _unitOfWork.SaveChangesAsync(ct);
    }

    public async Task<UserResponse> GetCurrentUserAsync(CancellationToken ct)
    {
        var userId = _currentUser.UserId;
        if (!userId.HasValue)
        {
            throw new SessionExpiredException();
        }

        var user = await _userRepository.FindByIdAsync(userId.Value, ct);
        if (user == null || user.Status != UserStatus.Active)
        {
            throw new SessionExpiredException();
        }

        return MapUser(user);
    }

    private async Task<ApiException> RecordFailedLoginAsync(
        LoginThrottle? throttle, string email, string ipRange, DateTimeOffset now, CancellationToken ct)
    {
        if (throttle == null)
        {
            throttle = new LoginThrottle
            {
                Email = email,
                IpRange = ipRange,
                FailedCount = 1,
                WindowStartedAt = now,
                LockedUntil = null,
            };
            _loginThrottleRepository.Add(throttle);
        }
        else
        {
            if (throttle.WindowStartedAt.Add(FailureWindow) <= now)
            {
                throttle.FailedCount = 1;
                throttle.WindowStartedAt = now;
                throttle.LockedUntil = null;
            }
            else
            {
                throttle.FailedCount++;
            }
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

    private static UserResponse MapUser(User user)
    {
        return new UserResponse
        {
            Id = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            Phone = user.Phone,
            Role = user.Role.ToString().ToUpperInvariant(),
            Status = user.Status.ToString().ToUpperInvariant(),
            EmailVerified = user.EmailVerifiedAt != null,
            HasPassword = !string.IsNullOrEmpty(user.PasswordHash),
            CreatedAt = user.CreatedAt,
        };
    }
}
