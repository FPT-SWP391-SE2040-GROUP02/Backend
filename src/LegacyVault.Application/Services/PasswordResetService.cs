using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

public sealed class PasswordResetService(
    IUserRepository userRepository,
    IPasswordResetTokenRepository resetTokenRepository,
    IRefreshTokenRepository refreshTokenRepository,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IEmailSender emailSender,
    IAppUrlProvider appUrlProvider,
    TimeProvider timeProvider) : IPasswordResetService
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);

    public async Task ForgotPasswordAsync(ForgotPasswordRequest request, CancellationToken ct)
    {
        string email = request.Email.Trim().ToLowerInvariant();
        User? user = await userRepository.FindByEmailAsync(email, ct);
        if (user == null || !CanResetPassword(user))
        {
            // Không có tài khoản, bị khóa hoặc chỉ dùng Google: vẫn trả thành công để không lộ thông tin tài khoản.
            return;
        }

        // Ghi token trong transaction; gửi email sau khi đã commit để thư không chứa token chưa được lưu.
        string? tokenValue = await unitOfWork.ExecuteSerializableAsync(() => IssueTokenAsync(user, ct), ct);
        if (tokenValue == null)
        {
            return;
        }

        string resetUrl = appUrlProvider.GetPasswordResetUrl(tokenValue);
        await emailSender.SendPasswordResetAsync(user.Email, user.FullName, resetUrl, ct);
    }

    public Task ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct)
    {
        string tokenHash = tokenService.HashOpaqueToken(request.Token);
        return unitOfWork.ExecuteSerializableAsync(() => ResetCoreAsync(tokenHash, request.NewPassword, ct), ct);
    }

    /// <summary>Tạo link mới và vô hiệu các link cũ. Trả null khi link gần nhất còn trong thời gian chờ gửi lại.</summary>
    private async Task<string?> IssueTokenAsync(User user, CancellationToken ct)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        List<PasswordResetToken> unused = await resetTokenRepository.FindUnusedByUserAsync(user.Id, ct);
        foreach (PasswordResetToken token in unused)
        {
            if (token.CreatedAt > now.Subtract(ResendCooldown) && token.ExpiresAt > now)
            {
                return null;
            }
        }

        foreach (PasswordResetToken token in unused)
        {
            token.UsedAt = now;
        }

        OpaqueToken opaque = tokenService.CreateOpaqueToken();
        PasswordResetToken resetToken = new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = opaque.Hash,
            CreatedAt = now,
            ExpiresAt = now.Add(TokenLifetime),
            UsedAt = null,
        };
        resetTokenRepository.Add(resetToken);
        await unitOfWork.SaveChangesAsync(ct);
        return opaque.Value;
    }

    private async Task<bool> ResetCoreAsync(string tokenHash, string newPassword, CancellationToken ct)
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        PasswordResetToken? token = await resetTokenRepository.FindByHashAsync(tokenHash, ct);
        if (token == null || token.UsedAt != null || token.ExpiresAt <= now)
        {
            throw InvalidResetToken();
        }

        User user = token.User;
        if (!CanResetPassword(user))
        {
            // Tài khoản bị khóa sau khi nhận link: báo như link không hợp lệ, không nói rõ trạng thái tài khoản.
            throw InvalidResetToken();
        }

        user.PasswordHash = passwordHasher.Hash(newPassword);
        user.UpdatedAt = now;
        if (user.EmailVerifiedAt == null)
        {
            // Mở được link trong hộp thư cũng chứng minh người dùng sở hữu email này.
            user.EmailVerifiedAt = now;
        }

        token.UsedAt = now;
        List<PasswordResetToken> otherLinks = await resetTokenRepository.FindUnusedByUserAsync(user.Id, ct);
        foreach (PasswordResetToken other in otherLinks)
        {
            other.UsedAt = now;
        }

        // Mật khẩu cũ có thể đã lộ: đăng xuất mọi thiết bị đang giữ refresh token.
        List<RefreshToken> sessions = await refreshTokenRepository.FindActiveByUserAsync(user.Id, ct);
        foreach (RefreshToken session in sessions)
        {
            session.RevokedAt = now;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return true;
    }

    private static bool CanResetPassword(User user)
    {
        if (user.Status != UserStatus.Active)
        {
            return false;
        }
        if (string.IsNullOrEmpty(user.PasswordHash))
        {
            return false;
        }
        return true;
    }

    private static AppException InvalidResetToken()
    {
        return new AppException(AppErrorType.Validation, ErrorCode.InvalidResetToken,
            "Liên kết đặt lại mật khẩu không hợp lệ, đã hết hạn hoặc đã được sử dụng.");
    }
}
