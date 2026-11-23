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
/// Ảnh thẻ công chứng viên / thẻ luật sư của Người xét duyệt (xem INotaryCardService). Cùng quy tắc ảnh với ảnh đại diện: loại ảnh nhận theo nội dung thật,
/// tối đa 2 MiB, không lưu tên tệp gốc, không có link công khai.
/// </summary>
public sealed class NotaryCardService : INotaryCardService
{
    private readonly ICurrentUser currentUser;
    private readonly IUserRepository userRepository;
    private readonly IAssetContentStore contentStore;
    private readonly IUnitOfWork unitOfWork;
    private readonly TimeProvider timeProvider;

    public NotaryCardService(
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

    public async Task UploadAsync(Guid userId, Stream content, long length, CancellationToken ct)
    {
        await RequireAdminAsync(ct);

        User? target = await userRepository.FindByIdAsync(userId, ct);
        if (target == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.UserNotFound, "Không tìm thấy tài khoản.");
        }
        if (target.Role != UserRole.Verifier)
        {
            throw ValidationErrors.For("userId", "Chỉ Người xét duyệt mới có ảnh thẻ.");
        }
        if (length <= 0)
        {
            throw ValidationErrors.For("file", "Vui lòng chọn ảnh thẻ công chứng viên.");
        }
        if (length > AvatarRules.MaxBytes)
        {
            throw ValidationErrors.For("file", "Ảnh tối đa 2 MB.");
        }

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

        string? oldStorageKey = target.NotaryCardStorageKey;
        DateTimeOffset now = timeProvider.GetUtcNow();
        target.NotaryCardStorageKey = stored.StorageKey;
        target.NotaryCardContentType = contentType;
        target.NotaryCardSizeBytes = stored.SizeBytes;
        target.NotaryCardEncryptionKeyId = stored.EncryptionKeyId;
        target.NotaryCardWrappedDataKey = stored.WrappedDataKey;
        target.NotaryCardUploadedAt = now;
        target.UpdatedAt = now;

        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch
        {
            // Bù trừ: không để lại nội dung không có bản ghi.
            await contentStore.DeleteAsync(stored.StorageKey, CancellationToken.None);
            throw;
        }

        await TryDeleteStoredAsync(oldStorageKey);
    }

    public async Task<NotaryCardStatusResponse> GetStatusAsync(CancellationToken ct)
    {
        User user = await RequireCardHolderAsync(ct);
        NotaryCardStatusResponse response = new NotaryCardStatusResponse();
        response.HasCard = user.NotaryCardStorageKey != null;
        response.UploadedAt = user.NotaryCardUploadedAt;
        return response;
    }

    public async Task<PreparedDownload> PrepareAsync(CancellationToken ct)
    {
        User user = await RequireCardHolderAsync(ct);
        return await PrepareCardAsync(user, ct);
    }

    public async Task<PreparedDownload> PrepareForAdminAsync(Guid userId, CancellationToken ct)
    {
        await RequireAdminAsync(ct);

        User? target = await userRepository.FindByIdAsync(userId, ct);
        if (target == null || target.Role != UserRole.Verifier)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.UserNotFound, "Không tìm thấy tài khoản.");
        }
        return await PrepareCardAsync(target, ct);
    }

    /// <summary>Giải mã thử để kiểm toàn vẹn rồi trả luồng tải ảnh thẻ của một tài khoản.</summary>
    private async Task<PreparedDownload> PrepareCardAsync(User user, CancellationToken ct)
    {
        if (user.NotaryCardStorageKey == null || user.NotaryCardWrappedDataKey == null)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.UserNotFound, "Chưa có ảnh thẻ công chứng viên.");
        }

        StoredContent stored = new StoredContent();
        stored.StorageKey = user.NotaryCardStorageKey;
        stored.SizeBytes = user.NotaryCardSizeBytes ?? 0;
        stored.EncryptionKeyId = user.NotaryCardEncryptionKeyId ?? string.Empty;
        stored.WrappedDataKey = user.NotaryCardWrappedDataKey;

        // Lượt 1: giải mã vào Stream.Null để kiểm kích thước và SHA-256 trước khi gửi byte đầu tiên.
        try
        {
            await contentStore.CopyToAsync(stored, Stream.Null, ct);
        }
        catch (Exception ex) when (ex is CryptographicException || ex is IOException)
        {
            throw new ContentIntegrityException("Ảnh thẻ không vượt qua kiểm tra toàn vẹn.", ex);
        }

        PreparedDownload download = new PreparedDownload();
        download.FileName = "notary-card";
        download.ContentType = user.NotaryCardContentType ?? "application/octet-stream";
        download.Length = stored.SizeBytes;
        download.WriteToAsync = (destination, token) => contentStore.CopyToAsync(stored, destination, token);
        return download;
    }

    /// <summary>Vai trò kiểm từ DB (ROLE-04): chỉ Người xét duyệt đang hoạt động, còn lại (kể cả Người thực thi) 404.</summary>
    private async Task<User> RequireCardHolderAsync(CancellationToken ct)
    {
        Guid userId = currentUser.RequireUserId();
        User? user = await userRepository.FindByIdAsync(userId, ct);
        if (user == null || user.Status != UserStatus.Active
            || user.Role != UserRole.Verifier)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.UserNotFound, "Không tìm thấy tài khoản.");
        }
        return user;
    }

    /// <summary>Không phải Admin đang hoạt động thì 404 như khu vực không tồn tại (SEC-04).</summary>
    private async Task RequireAdminAsync(CancellationToken ct)
    {
        Guid adminId = currentUser.RequireUserId();
        User? admin = await userRepository.FindByIdAsync(adminId, ct);
        if (admin == null || admin.Role != UserRole.Admin || admin.Status != UserStatus.Active)
        {
            throw new AppException(AppErrorType.NotFound, ErrorCode.UserNotFound, "Không tìm thấy tài khoản.");
        }
    }

    private static async Task<string> ReadImageContentTypeAsync(Stream content, CancellationToken ct)
    {
        byte[] header = new byte[FileSignatureRules.HeaderLength];
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
}
