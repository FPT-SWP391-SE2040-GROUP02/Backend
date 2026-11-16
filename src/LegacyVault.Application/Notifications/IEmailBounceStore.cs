using System.Security.Cryptography;
using System.Text;

namespace LegacyVault.Application.Notifications;

/// <summary>Nơi ghi và tra các email đã bị máy chủ thư báo "không giao được".</summary>
public interface IEmailBounceStore
{
    /// <summary>Ghi nhận một bounce; email đã có thì cập nhật mã và thời điểm.</summary>
    Task RecordAsync(string email, string failureCode, DateTimeOffset detectedAt, CancellationToken ct);

    /// <summary>Trong các email cho trước, trả về tập (chữ thường) những email đã bị bounce.</summary>
    Task<HashSet<string>> FindBouncedAsync(IEnumerable<string> emails, CancellationToken ct);
}

public static class EmailBounceHash
{
    public static string Of(string email)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant()));
        return Convert.ToHexStringLower(digest);
    }
}
