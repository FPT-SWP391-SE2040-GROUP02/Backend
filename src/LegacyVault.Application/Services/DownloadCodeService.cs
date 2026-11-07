using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Notifications;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

public interface IDownloadCodeService
{
    /// <summary>Admin bật "mã tải" (cài đặt Handover.DownloadCodeRequired) thì mỗi lần tải phải kèm mã gửi qua email.</summary>
    bool Required { get; }

    /// <summary>Tạo mã 6 số dùng một lần cho người đang đăng nhập và gửi qua email của họ. Người gọi đã kiểm quyền với quyền tải.</summary>
    Task RequestAsync(Guid grantId, CancellationToken ct);

    /// <summary>
    /// Kiểm mã của người đang đăng nhập cho quyền tải này. Tắt mã thì luôn qua. Thiếu mã → 403 DOWNLOAD_CODE_REQUIRED;
    /// sai, hết hạn hoặc nhập sai quá 5 lần → 403 DOWNLOAD_CODE_INVALID. Mã đúng chỉ dùng được một lần.
    /// </summary>
    void Consume(Guid grantId, string? code);
}

/// <remarks>
/// Mã giữ trong bộ nhớ, chỉ lưu băm, hạn 10 phút: máy chủ khởi động lại thì mã mất và người nhận xin mã mới.
/// Một người một quyền tải chỉ có một mã còn hiệu lực; xin mã mới thì mã cũ hết tác dụng.
/// </remarks>
public sealed class DownloadCodeService : IDownloadCodeService
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    public const int MaxAttempts = 5;

    private static readonly ConcurrentDictionary<(Guid UserId, Guid GrantId), Entry> Store =
        new ConcurrentDictionary<(Guid UserId, Guid GrantId), Entry>();

    private readonly ICurrentUser currentUser;
    private readonly IUserRepository users;
    private readonly IDownloadCodeSender sender;
    private readonly TimeProvider timeProvider;

    public DownloadCodeService(
        ICurrentUser currentUser, IUserRepository users, IDownloadCodeSender sender, TimeProvider timeProvider)
    {
        this.currentUser = currentUser;
        this.users = users;
        this.sender = sender;
        this.timeProvider = timeProvider;
    }

    public bool Required => RuntimeSettings.Bool("Handover.DownloadCodeRequired", false);

    public async Task RequestAsync(Guid grantId, CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        User? user = await users.FindByIdAsync(userId, ct);
        if (user == null || user.Status != UserStatus.Active || string.IsNullOrWhiteSpace(user.Email))
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.UserNotFound, "Không tìm thấy tài khoản.");
        }

        string code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        DateTimeOffset expiresAt = timeProvider.GetUtcNow() + Lifetime;
        Store[(userId, grantId)] = new Entry(Hash(grantId, code), expiresAt);
        // Gửi lỗi thì xóa mã vừa tạo để không còn mã nào mà người nhận không nhận được.
        try
        {
            await sender.SendAsync(user.Email, user.FullName, code, expiresAt, ct);
        }
        catch
        {
            Store.TryRemove((userId, grantId), out _);
            throw;
        }
    }

    public void Consume(Guid grantId, string? code)
    {
        if (!Required)
        {
            return;
        }
        Guid userId = currentUser.RequireUserId();
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new AppException(AppErrorType.Forbidden, ErrorCode.DownloadCodeRequired,
                "Cần mã tải gửi qua email để tải tài sản.");
        }
        (Guid, Guid) key = (userId, grantId);
        if (!Store.TryGetValue(key, out Entry? entry) || entry.ExpiresAt <= timeProvider.GetUtcNow())
        {
            Store.TryRemove(key, out _);
            throw Invalid();
        }
        byte[] given = Hash(grantId, code.Trim());
        if (!CryptographicOperations.FixedTimeEquals(given, entry.Hash))
        {
            if (Interlocked.Increment(ref entry.Attempts) >= MaxAttempts)
            {
                Store.TryRemove(key, out _);
            }
            throw Invalid();
        }
        // Mã đúng chỉ dùng một lần.
        Store.TryRemove(key, out _);
    }

    private static AppException Invalid()
    {
        return new AppException(AppErrorType.Forbidden, ErrorCode.DownloadCodeInvalid,
            "Mã tải không đúng hoặc đã hết hạn. Hãy xin mã mới.");
    }

    private static byte[] Hash(Guid grantId, string code)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(grantId.ToString("N") + ":" + code));
    }

    private sealed class Entry
    {
        public Entry(byte[] hash, DateTimeOffset expiresAt)
        {
            Hash = hash;
            ExpiresAt = expiresAt;
        }

        public byte[] Hash { get; }

        public DateTimeOffset ExpiresAt { get; }

        public int Attempts;
    }
}
