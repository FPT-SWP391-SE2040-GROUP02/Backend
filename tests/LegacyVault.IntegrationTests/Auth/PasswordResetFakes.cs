using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Domain.Entities;
using LegacyVault.IntegrationTests.GoogleAuth;

namespace LegacyVault.IntegrationTests.Auth;

/// <summary>Bảng PasswordResetTokens trong bộ nhớ; gắn User từ FakeUserRepository như Include của bản thật.</summary>
public sealed class FakePasswordResetTokenRepository(FakeUserRepository users) : IPasswordResetTokenRepository
{
    public List<PasswordResetToken> Tokens { get; } = new List<PasswordResetToken>();

    public Task<PasswordResetToken?> FindByHashAsync(string tokenHash, CancellationToken ct)
    {
        foreach (PasswordResetToken token in Tokens)
        {
            if (token.TokenHash == tokenHash)
            {
                token.User = users.Users.Single(u => u.Id == token.UserId);
                return Task.FromResult<PasswordResetToken?>(token);
            }
        }
        return Task.FromResult<PasswordResetToken?>(null);
    }

    public Task<List<PasswordResetToken>> FindUnusedByUserAsync(Guid userId, CancellationToken ct)
    {
        List<PasswordResetToken> result = new List<PasswordResetToken>();
        foreach (PasswordResetToken token in Tokens)
        {
            if (token.UserId == userId && token.UsedAt == null)
            {
                result.Add(token);
            }
        }
        result.Reverse();
        return Task.FromResult(result);
    }

    public void Add(PasswordResetToken token)
    {
        Tokens.Add(token);
    }
}

/// <summary>Ghi lại thư đặt lại mật khẩu thay vì gửi thật; dùng cho cả test fake và test SQL.</summary>
public sealed class CapturingPasswordResetEmailSender : IEmailSender
{
    private readonly object sync = new object();
    private readonly List<CapturedResetMail> resetMails = new List<CapturedResetMail>();
    private readonly List<(string ToEmail, VaultPinNoticeKind Kind)> pinNotices = new List<(string, VaultPinNoticeKind)>();

    public List<CapturedResetMail> ResetMails
    {
        get
        {
            lock (sync)
            {
                return new List<CapturedResetMail>(resetMails);
            }
        }
    }

    public Task SendEmailVerificationAsync(string toEmail, string fullName, string verificationUrl, CancellationToken ct)
    {
        return Task.CompletedTask;
    }

    public Task SendPasswordResetAsync(string toEmail, string fullName, string resetUrl, CancellationToken ct)
    {
        lock (sync)
        {
            resetMails.Add(new CapturedResetMail(toEmail, resetUrl));
        }
        return Task.CompletedTask;
    }

    /// <summary>Các email báo về mã PIN kho (loại thư, người nhận).</summary>
    public List<(string ToEmail, VaultPinNoticeKind Kind)> PinNotices
    {
        get
        {
            lock (sync)
            {
                return new List<(string, VaultPinNoticeKind)>(pinNotices);
            }
        }
    }

    public Task SendVaultPinNoticeAsync(
        string toEmail, string fullName, VaultPinNoticeKind kind, DateTimeOffset? lockedUntil, CancellationToken ct)
    {
        lock (sync)
        {
            pinNotices.Add((toEmail, kind));
        }
        return Task.CompletedTask;
    }

    /// <summary>Lấy token gốc từ link "…/reset-password?token=…".</summary>
    public static string TokenFromUrl(string resetUrl)
    {
        string marker = "token=";
        int index = resetUrl.IndexOf(marker, StringComparison.Ordinal);
        return Uri.UnescapeDataString(resetUrl.Substring(index + marker.Length));
    }
}

public sealed class CapturedResetMail(string toEmail, string resetUrl)
{
    public string ToEmail { get; } = toEmail;

    public string ResetUrl { get; } = resetUrl;
}

public sealed class FakeAppUrlProvider : IAppUrlProvider
{
    public string GetEmailVerificationUrl(string token)
    {
        return "http://fe.test/verify-email?token=" + Uri.EscapeDataString(token);
    }

    public string GetPasswordResetUrl(string token)
    {
        return "http://fe.test/reset-password?token=" + Uri.EscapeDataString(token);
    }

    public string GetExecutorInvitationUrl(string token)
    {
        return "http://fe.test/executor-invitation?token=" + Uri.EscapeDataString(token);
    }
}
