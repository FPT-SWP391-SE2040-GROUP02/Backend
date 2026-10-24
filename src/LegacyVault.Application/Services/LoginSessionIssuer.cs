using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Services;

/// <summary>
/// Mở phiên đăng nhập cho một user đã được xác thực: tạo refresh token (lưu hash) và access token.
/// </summary>
/// <remarks>
/// Cách mở phiên giống hệt bước 7 trong AuthService.LoginAsync (cùng IdleTimeout 30 phút, tối đa 12 giờ, NFR-06).
/// Tách thành class riêng để đăng nhập Google dùng lại mà không phải sửa AuthService (tránh conflict khi nhóm
/// còn đang sửa đăng nhập thường). Sau này AuthService có thể gọi class này để bỏ phần code trùng.
/// </remarks>
public sealed class LoginSessionIssuer
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MaxSessionLifetime = TimeSpan.FromHours(12);

    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly ITokenService _tokenService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _timeProvider;

    public LoginSessionIssuer(
        IRefreshTokenRepository refreshTokenRepository,
        ITokenService tokenService,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        _refreshTokenRepository = refreshTokenRepository;
        _tokenService = tokenService;
        _unitOfWork = unitOfWork;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Tạo phiên mới rồi lưu DB (kèm mọi thay đổi đang chờ khác của request, ví dụ LastLoginAt).
    /// </summary>
    public async Task<AuthResult> OpenSessionAsync(User user, bool rememberMe, CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow();

        // Phiên hết khi không dùng 30 phút, nhưng không bao giờ quá 12 giờ kể từ lúc đăng nhập.
        var idleExpiresAt = now.Add(IdleTimeout);
        var maxExpiresAt = now.Add(MaxSessionLifetime);
        if (idleExpiresAt > maxExpiresAt)
        {
            idleExpiresAt = maxExpiresAt;
        }

        // Chỉ lưu HASH của refresh token; token gốc chỉ nằm trong cookie của trình duyệt (SEC-07).
        var refreshToken = _tokenService.CreateOpaqueToken();
        _refreshTokenRepository.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshToken.Hash,
            FamilyId = Guid.CreateVersion7(), // một family = một lần đăng nhập trên một thiết bị
            SessionStartedAt = now,
            CreatedAt = now,
            ExpiresAt = idleExpiresAt,
            IsPersistent = rememberMe,
        });

        var accessToken = _tokenService.CreateAccessToken(user);
        await _unitOfWork.SaveChangesAsync(ct);

        return new AuthResult
        {
            Session = new AuthSessionResponse
            {
                AccessToken = accessToken.Value,
                ExpiresIn = (int)Math.Max(0, (accessToken.ExpiresAt - now).TotalSeconds),
                User = ToUserResponse(user),
            },
            RefreshToken = refreshToken.Value,
            RefreshTokenExpiresAt = maxExpiresAt,
            IsPersistent = rememberMe,
        };
    }

    /// <summary>Đổi entity User thành JSON trả cho FE (không có PasswordHash).</summary>
    public static UserResponse ToUserResponse(User user)
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
