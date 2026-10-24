using System.Security.Cryptography;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Repositories;
using LegacyVault.Application.Security;
using LegacyVault.Application.Storage;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Ảnh đại diện của người đang đăng nhập (xem IAvatarService).
/// </summary>
/// <remarks>
/// - Ảnh đi qua cùng kho lưu trữ mã hóa với tài sản nên không có link công khai; chỉ chủ tài khoản đọc được qua API.
/// - Loại ảnh nhận theo nội dung thật (JPEG/PNG/WebP), không tin đuôi tệp hay Content-Type.
/// - Ảnh không tính vào dung lượng kho (quota chỉ tính tài sản).
/// - Tên tệp gốc không được lưu.
/// </remarks>
public sealed class AvatarService : IAvatarService
{
    private readonly ICurrentUser currentUser;
    private readonly IUserRepository userRepository;
    private readonly IAssetContentStore contentStore;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public AvatarService(
        ICurrentUser currentUser,
        IUserRepository userRepository,
        IAssetContentStore contentStore,
        IUnitOfWork unitOfWork,
        TimeProvider timeProvider)
    {
        this.currentUser = currentUser;
        this.userRepository = userRepository;
        this.contentStore = contentStore;
        this.unitOfWork = unitOfWork;
        this.timeProvider = timeProvider;
    }

    public async Task<UserResponse> UploadAsync(Stream content, long length, CancellationToken ct)
    {
        if (length <= 0)
        {
            throw ValidationErrors.For("file", "Vui lòng chọn ảnh.");
        }
        if (length > AvatarRules.MaxBytes)
        {
            throw ValidationErrors.For("file", "Ảnh tối đa 2 MB.");
        }

        User user = await LoadUserAsync(ct);

        string contentType = await ReadImageContentTypeAsync(content, ct);

        StoredContent stored;
        try
        {
            stored = await contentStore.SaveAsync(content, AvatarRules.MaxBytes, ct);
        }
        catch (AppException ex) when (ex.Code == ErrorCode.FileTooLarge)
        {
            throw ValidationErrors.For("file", "Ảnh tối đa 2 MB.");
        }

        string? oldStorageKey = user.AvatarStorageKey;
        DateTimeOffset now = timeProvider.GetUtcNow();
        user.AvatarStorageKey = stored.StorageKey;
        user.AvatarContentType = contentType;
        user.AvatarSizeBytes = stored.SizeBytes;
        user.AvatarSha256 = stored.Sha256;
        user.AvatarEncryptionKeyId = stored.EncryptionKeyId;
        user.AvatarWrappedDataKey = stored.WrappedDataKey;
        user.AvatarUpdatedAt = now;
        user.UpdatedAt = now;

        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch
        {
            // Bù trừ: không để lại nội dung không có bản ghi. Không dùng ct vì request có thể đã bị hủy.
            await contentStore.DeleteAsync(stored.StorageKey, CancellationToken.None);
            throw;
        }

        await TryDeleteStoredAsync(oldStorageKey);
        return LoginSessionIssuer.ToUserResponse(user);
    }

    public async Task<PreparedDownload> PrepareAsync(CancellationToken ct)
    {
        User user = await LoadUserAsync(ct);
        if (user.AvatarStorageKey == null || user.AvatarWrappedDataKey == null)
        {
            throw NotFound();
        }

        StoredContent stored = new StoredContent();
        stored.StorageKey = user.AvatarStorageKey;
        stored.SizeBytes = user.AvatarSizeBytes ?? 0;
        stored.Sha256 = user.AvatarSha256 ?? string.Empty;
        stored.EncryptionKeyId = user.AvatarEncryptionKeyId ?? string.Empty;
        stored.WrappedDataKey = user.AvatarWrappedDataKey;

        // Lượt 1: giải mã vào Stream.Null để kiểm kích thước và SHA-256 trước khi gửi byte đầu tiên.
        try
        {
            await contentStore.CopyToAsync(stored, Stream.Null, ct);
        }
        catch (Exception ex) when (ex is CryptographicException || ex is IOException)
        {
            throw new ContentIntegrityException("Ảnh đại diện không vượt qua kiểm tra toàn vẹn.", ex);
        }

        PreparedDownload download = new PreparedDownload();
        download.FileName = "avatar";
        download.ContentType = user.AvatarContentType ?? "application/octet-stream";
        download.Length = stored.SizeBytes;
        download.WriteToAsync = (destination, token) => contentStore.CopyToAsync(stored, destination, token);
        return download;
    }

    public async Task<UserResponse> DeleteAsync(CancellationToken ct)
    {
        User user = await LoadUserAsync(ct);
        string? oldStorageKey = user.AvatarStorageKey;
        if (oldStorageKey == null)
        {
            return LoginSessionIssuer.ToUserResponse(user);
        }

        DateTimeOffset now = timeProvider.GetUtcNow();
        user.AvatarStorageKey = null;
        user.AvatarContentType = null;
        user.AvatarSizeBytes = null;
        user.AvatarSha256 = null;
        user.AvatarEncryptionKeyId = null;
        user.AvatarWrappedDataKey = null;
        user.AvatarUpdatedAt = null;
        user.UpdatedAt = now;
        await unitOfWork.SaveChangesAsync(ct);

        await TryDeleteStoredAsync(oldStorageKey);
        return LoginSessionIssuer.ToUserResponse(user);
    }

    private async Task<User> LoadUserAsync(CancellationToken ct)
    {
        Guid? userId = currentUser.UserId;
        if (userId == null)
        {
            throw new SessionExpiredException();
        }

        User? user = await userRepository.FindByIdAsync(userId.Value, ct);
        if (user == null || user.Status != UserStatus.Active)
        {
            throw new SessionExpiredException();
        }
        return user;
    }

    /// <summary>Đọc vài byte đầu để nhận loại ảnh thật rồi đưa stream về đầu. Không phải JPEG/PNG/WebP → 400.</summary>
    private static async Task<string> ReadImageContentTypeAsync(Stream content, CancellationToken ct)
    {
        byte[] header = new byte[DeathCaseFileRules.HeaderLength];
        int read = 0;
        while (read < header.Length)
        {
            int count = await content.ReadAsync(header.AsMemory(read, header.Length - read), ct);
            if (count == 0)
            {
                break;
            }
            read += count;
        }

        string? contentType = AvatarRules.DetectImageContentType(header);
        if (contentType == null)
        {
            throw ValidationErrors.For("file", "Chỉ nhận ảnh JPEG, PNG hoặc WebP.");
        }

        content.Seek(0, SeekOrigin.Begin);
        return contentType;
    }

    /// <summary>Xóa ảnh cũ SAU khi bản ghi đã lưu. Lỗi thì để lại object mồ côi (đã mã hóa), không che việc đã thành công.</summary>
    private async Task TryDeleteStoredAsync(string? storageKey)
    {
        if (storageKey == null)
        {
            return;
        }
        try
        {
            await contentStore.DeleteAsync(storageKey, CancellationToken.None);
        }
        catch (Exception)
        {
            // Object còn lại cần dọn thủ công.
        }
    }

    private static AppException NotFound()
    {
        return new AppException(AppErrorType.NotFound, ErrorCode.AvatarNotFound, "Chưa có ảnh đại diện.");
    }
}
