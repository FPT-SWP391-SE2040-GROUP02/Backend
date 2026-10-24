using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>Ảnh đại diện của người đang đăng nhập (mọi vai trò). Ảnh lưu mã hóa trong kho lưu trữ private.</summary>
public interface IAvatarService
{
    /// <summary>
    /// Đặt ảnh đại diện mới (thay ảnh cũ nếu có). content phải đọc lại từ đầu được (CanSeek). Trả thông tin người dùng mới.
    /// Lỗi: 400 VALIDATION_ERROR (rỗng, quá 2 MiB, không phải JPEG/PNG/WebP), 401 SESSION_EXPIRED.
    /// </summary>
    Task<UserResponse> UploadAsync(Stream content, long length, CancellationToken ct);

    /// <summary>
    /// Chuẩn bị tải ảnh của chính mình, đã kiểm toàn vẹn. Lỗi: 404 AVATAR_NOT_FOUND khi chưa có ảnh,
    /// ContentIntegrityException khi ảnh hỏng.
    /// </summary>
    Task<PreparedDownload> PrepareAsync(CancellationToken ct);

    /// <summary>Xóa ảnh đại diện. Chưa có ảnh vẫn thành công. Trả thông tin người dùng mới.</summary>
    Task<UserResponse> DeleteAsync(CancellationToken ct);
}
