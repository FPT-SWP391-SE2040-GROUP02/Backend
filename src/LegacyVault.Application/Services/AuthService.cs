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

        // Đang bị tạm khóa vì sai mật khẩu nhiều lần (đếm theo cặp email và dải IP): từ chối ngay, báo số giây còn lại.
        var throttle = await _loginThrottleRepository.FindAsync(email, ipRange, ct);
        if (throttle != null && throttle.LockedUntil.HasValue && throttle.LockedUntil.Value > now)
        {
            var remaining = (int)Math.Ceiling((throttle.LockedUntil.Value - now).TotalSeconds);
            throw new AccountLockedException(remaining);
        }

        var user = await _userRepository.FindByEmailAsync(email, ct);

        if (user == null || string.IsNullOrEmpty(user.PasswordHash))
        {
            // Không có tài khoản hoặc tài khoản chỉ dùng Google: vẫn chạy băm giả để thời gian phản hồi
            // giống khi có tài khoản, người lạ không đo thời gian để dò email nào đã đăng ký.
            _dummyHash ??= _passwordHasher.Hash(Guid.NewGuid().ToString());
            _passwordHasher.Verify(request.Password, _dummyHash);

            throw await RecordFailedLoginAsync(throttle, email, ipRange, now, ct);
        }

        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw await RecordFailedLoginAsync(throttle, email, ipRange, now, ct);
        }

        // Chỉ báo tài khoản bị khóa khi mật khẩu đã đúng, để người lạ không dò được trạng thái tài khoản.
        if (user.Status != UserStatus.Active)
        {
            throw new AccountNotActiveException();
        }

        // Đăng nhập đúng thì xóa bộ đếm sai của cặp email và dải IP này.
        if (throttle != null)
        {
            _loginThrottleRepository.Remove(throttle);
        }

        user.LastLoginAt = now;

        // Mỗi lần đăng nhập là một FamilyId mới; các refresh token xoay vòng về sau đều mang FamilyId này,
        // nhờ đó thu hồi được cả phiên khi phát hiện token cũ bị dùng lại.
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

        // Quá 12 giờ kể từ lúc đăng nhập, hoặc nghỉ quá 30 phút từ lần cấp token gần nhất: hết phiên.
        var maxSessionLimit = tokenEntity.SessionStartedAt.Add(MaxSessionLifetime);
        if (maxSessionLimit <= now || tokenEntity.ExpiresAt <= now)
        {
            tokenEntity.RevokedAt = now;
            await _unitOfWork.SaveChangesAsync(ct);
            return null;
        }

        // Tài khoản đã bị khóa thì không cấp token mới, thu hồi luôn token này.
        if (tokenEntity.User.Status != UserStatus.Active)
        {
            tokenEntity.RevokedAt = now;
            await _unitOfWork.SaveChangesAsync(ct);
            return null;
        }

        // Xoay vòng: thu hồi token cũ, cấp token mới cùng FamilyId và giữ mốc bắt đầu phiên để vẫn tính đủ 12 giờ.
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
