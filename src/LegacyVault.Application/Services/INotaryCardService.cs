namespace LegacyVault.Application.Services;

/// <summary>Ảnh thẻ công chứng viên của Người xét duyệt, do Admin tải lên. Ảnh lưu mã hóa trong kho lưu trữ private.</summary>
public interface INotaryCardService
{
    /// <summary>
    /// Đặt ảnh thẻ công chứng cho một Người xét duyệt (thay ảnh cũ nếu có). content phải đọc lại từ đầu được (CanSeek).
    /// Lỗi: 400 VALIDATION_ERROR (rỗng, quá 2 MiB, không phải JPEG/PNG/WebP, tài khoản không phải VERIFIER),
    /// 404 khi không phải Admin hoặc không có tài khoản.
    /// </summary>
    Task UploadAsync(Guid userId, Stream content, long length, CancellationToken ct);
}
