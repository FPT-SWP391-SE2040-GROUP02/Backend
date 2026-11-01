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
    private readonly ITwoFactorService? _twoFactorService;
    private readonly ISessionRevocationValidator? _sessionValidator;
    private readonly IIncidentService? _incidentService;

    public AuthService(
        IUserRepository userRepository,
        ILoginThrottleRepository loginThrottleRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        ICurrentUser currentUser,
        TimeProvider timeProvider,
        ITwoFactorService? twoFactorService = null,
        ISessionRevocationValidator? sessionValidator = null,
        IIncidentService? incidentService = null)
    {
        _twoFactorService = twoFactorService;
        _sessionValidator = sessionValidator;
        _incidentService = incidentService;
        _userRepository = userRepository;
        _loginThrottleRepository = loginThrottleRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _currentUser = currentUser;
        _timeProvider = timeProvider;
    }

    /// <summary>Đăng nhập cùng bộ đếm dưới Serializable; lỗi credentials chỉ phát sau commit bộ đếm.</summary>
    public async Task<AuthResult> LoginAsync(LoginRequest request, string ipRange, CancellationToken ct)
    {
        var outcome = await _unitOfWork.ExecuteSerializableAsync(async () =>
        {
            try
            {
                var result = await LoginCoreAsync(request, ipRange, ct);
                return (Result: (AuthResult?)result, Failure: (ApiException?)null);
            }
            catch (InvalidCredentialsException failure)
            {
                return (Result: (AuthResult?)null, Failure: (ApiException?)failure);
            }
            catch (AccountLockedException failure)
            {
                return (Result: (AuthResult?)null, Failure: (ApiException?)failure);
            }
        }, ct);
        if (outcome.Failure != null)
        {
            throw outcome.Failure;
        }
        return outcome.Result!;
    }

    private async Task<AuthResult> LoginCoreAsync(LoginRequest request, string ipRange, CancellationToken ct)
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

            throw await RecordFailedLoginAsync(throttle, user, email, ipRange, now, ct);
        }

        // 4. Kiểm tra mật khẩu
        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw await RecordFailedLoginAsync(throttle, user, email, ipRange, now, ct);
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

        // 6b. Admin đã bật xác thực hai bước: chưa cấp phiên, trả thử thách để FE hỏi mã TOTP.
        if (_twoFactorService != null)
        {
            var challenge = await _twoFactorService.ChallengeIfRequiredAsync(user, request.RememberMe, ct);
            if (challenge != null)
            {
                return new AuthResult { TwoFactor = challenge };
            }
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

        var accessToken = _tokenService.CreateAccessToken(user, familyId);
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

    /// <summary>Xoay token nguyên tử theo invariant đã chuyển từ test; giữ idle 30 phút/max 12 giờ của Backend.</summary>
    public async Task<AuthResult> RefreshAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            throw new SessionExpiredException();
        }
        var result = await _unitOfWork.ExecuteSerializableAsync(() => RefreshCoreAsync(refreshToken, ct), ct);
        if (result == null)
        {
            // Thu hồi family do replay phải commit trước khi phát lỗi HTTP.
            throw new SessionExpiredException();
        }
        return result;
    }

    private async Task<AuthResult?> RefreshCoreAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow();
        var hash = _tokenService.HashOpaqueToken(refreshToken);

        var tokenEntity = await _refreshTokenRepository.FindByHashAsync(hash, ct);
        if (tokenEntity == null)
        {
            return null;
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
            return null;
        }

        // Kiểm tra thời hạn phiên (tối đa 12 giờ hoặc idle 30 phút, NFR-06)
        var maxSessionLimit = tokenEntity.SessionStartedAt.Add(MaxSessionLifetime);
        if (maxSessionLimit <= now || tokenEntity.ExpiresAt <= now)
        {
            tokenEntity.RevokedAt = now;
            await _unitOfWork.SaveChangesAsync(ct);
            return null;
        }

        // Kiểm tra tài khoản còn Active không
        if (tokenEntity.User.Status != UserStatus.Active)
        {
            tokenEntity.RevokedAt = now;
            await _unitOfWork.SaveChangesAsync(ct);
            return null;
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

        var newAccessToken = _tokenService.CreateAccessToken(tokenEntity.User, tokenEntity.FamilyId);
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

    /// <summary>Thu hồi theo family/tài khoản dưới Serializable; không cho token revoked/expired thu hồi mọi thiết bị.</summary>
    public async Task LogoutAsync(string? refreshToken, bool revokeAllDevices, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            if (!revokeAllDevices)
            {
                return;
            }
            if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue)
            {
                throw new SessionExpiredException();
            }
        }
        await _unitOfWork.ExecuteSerializableAsync(async () =>
        {
            await LogoutCoreAsync(refreshToken, revokeAllDevices, ct);
            return true;
        }, ct);
    }

    private async Task LogoutCoreAsync(string? refreshToken, bool revokeAllDevices, CancellationToken ct)
    {
        RefreshToken? cookieToken = null;
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            cookieToken = await _refreshTokenRepository.FindByHashAsync(_tokenService.HashOpaqueToken(refreshToken), ct);
        }
        var now = _timeProvider.GetUtcNow();
        if (revokeAllDevices)
        {
            Guid userId;
            if (_currentUser.IsAuthenticated && _currentUser.UserId.HasValue)
            {
                userId = _currentUser.UserId.Value;
                var user = await _userRepository.FindByIdAsync(userId, ct);
                if (user == null || user.Status != UserStatus.Active)
                {
                    throw new SessionExpiredException();
                }
                if (!string.IsNullOrWhiteSpace(refreshToken) && (cookieToken == null || cookieToken.UserId != userId))
                {
                    throw new SessionExpiredException();
                }
            }
            else
            {
                if (cookieToken == null || cookieToken.RevokedAt != null || cookieToken.ExpiresAt <= now
                    || cookieToken.SessionStartedAt.Add(MaxSessionLifetime) <= now
                    || cookieToken.User.Status != UserStatus.Active)
                {
                    throw new SessionExpiredException();
                }
                userId = cookieToken.UserId;
            }
            var allTokens = await _refreshTokenRepository.FindActiveByUserAsync(userId, ct);
            now = _timeProvider.GetUtcNow();
            foreach (var token in allTokens)
            {
                token.RevokedAt = now;
                _sessionValidator?.InvalidateFamily(token.FamilyId);
            }
        }
        else
        {
            if (cookieToken == null)
            {
                return;
            }
            var familyTokens = await _refreshTokenRepository.FindActiveByFamilyAsync(cookieToken.FamilyId, ct);
            now = _timeProvider.GetUtcNow();
            foreach (var token in familyTokens)
            {
                token.RevokedAt = now;
                _sessionValidator?.InvalidateFamily(token.FamilyId);
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

    /// <param name="user">Tài khoản của email; null khi email chưa đăng ký (khi đó không mở sự cố).</param>
    private async Task<ApiException> RecordFailedLoginAsync(
        LoginThrottle? throttle, User? user, string email, string ipRange, DateTimeOffset now, CancellationToken ct)
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
            await RaiseSuspiciousLoginAsync(user, ipRange, ct);
            await _unitOfWork.SaveChangesAsync(ct);
            return new AccountLockedException((int)LockoutDuration.TotalSeconds);
        }

        await _unitOfWork.SaveChangesAsync(ct);
        return new InvalidCredentialsException(MaxFailedAttempts - throttle.FailedCount);
    }

    /// <summary>
    /// Cảnh báo đăng nhập bất thường (SEC-10): vừa khóa một tài khoản có thật thì mở sự cố cho Admin ít việc nhất.
    /// Tài khoản đang có sự cố mở thì RaiseAsync không tạo thêm. Lưu cùng lần khóa, trong cùng giao dịch đăng nhập.
    /// </summary>
    private async Task RaiseSuspiciousLoginAsync(User? user, string ipRange, CancellationToken ct)
    {
        if (user == null || _incidentService == null)
        {
            return;
        }
        string summary = "Nhập sai mật khẩu " + MaxFailedAttempts + " lần trong "
            + (int)FailureWindow.TotalMinutes + " phút từ dải IP " + ipRange
            + "; tài khoản bị tạm khóa " + (int)LockoutDuration.TotalMinutes + " phút.";
        await _incidentService.RaiseAsync(
            IncidentType.SuspiciousLogin, null, IncidentRules.TargetUser, user.Id, summary, null, ct);
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
            HasAvatar = user.AvatarStorageKey != null,
            AvatarUpdatedAt = user.AvatarUpdatedAt,
            CreatedAt = user.CreatedAt,
        };
    }
}
