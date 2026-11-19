using System.Collections.Concurrent;
using System.Security.Cryptography;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Security;

namespace LegacyVault.Infrastructure.Security;

/// <summary>
/// Kho nonce trong bộ nhớ của tiến trình (một instance API). Khởi động lại hoặc chạy nhiều instance thì nonce chưa dùng
/// mất hiệu lực: người dùng chỉ cần tải lại nút Google để lấy nonce mới.
/// </summary>
public sealed class GoogleNonceStore : IGoogleNonceStore
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    /// <summary>Trần số nonce chưa dùng cùng lúc (mỗi nonce vài chục byte).</summary>
    public const int MaxPending = 20000;

    private readonly ConcurrentDictionary<string, DateTimeOffset> pending = new ConcurrentDictionary<string, DateTimeOffset>();
    private readonly TimeProvider timeProvider;

    public GoogleNonceStore(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider;
    }

    public string Issue()
    {
        DateTimeOffset now = timeProvider.GetUtcNow();
        if (pending.Count >= MaxPending)
        {
            Purge(now);
            if (pending.Count >= MaxPending)
            {
                throw new AppException(AppErrorType.ServiceUnavailable, ErrorCode.EditConflict,
                    "Hệ thống đang bận. Vui lòng thử lại sau ít phút.");
            }
        }
        string nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        pending[nonce] = now + Lifetime;
        return nonce;
    }

    public bool TryConsume(string nonce)
    {
        if (string.IsNullOrEmpty(nonce) || !pending.TryRemove(nonce, out DateTimeOffset expiresAt))
        {
            return false;
        }
        return expiresAt > timeProvider.GetUtcNow();
    }

    private void Purge(DateTimeOffset now)
    {
        foreach (KeyValuePair<string, DateTimeOffset> entry in pending)
        {
            if (entry.Value <= now)
            {
                pending.TryRemove(entry.Key, out _);
            }
        }
    }
}
