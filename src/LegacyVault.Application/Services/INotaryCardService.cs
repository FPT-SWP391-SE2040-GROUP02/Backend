using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>Ảnh thẻ công chứng viên của Người xét duyệt, do Admin tải lên (lúc tạo tài khoản hoặc nộp lại sau đó). Người xét duyệt chỉ xem. Ảnh lưu mã hóa trong kho lưu trữ private.</summary>
public interface INotaryCardService
{
    /// <summary>
    /// Đặt ảnh thẻ công chứng cho một Người xét duyệt (thay ảnh cũ nếu có). content phải đọc lại từ đầu được (CanSeek).
    /// Lỗi: 400 VALIDATION_ERROR (rỗng, quá 2 MiB, không phải JPEG/PNG/WebP, tài khoản không phải VERIFIER),
    /// 404 khi không phải Admin hoặc không có tài khoản.
    /// </summary>
    Task UploadAsync(Guid userId, Stream content, long length, CancellationToken ct);

    /// <summary>Tình trạng thẻ của Người xét duyệt đang đăng nhập. Lỗi: 403 ROLE_NOT_ALLOWED khi không phải Người xét duyệt.</summary>
    Task<NotaryCardStatusResponse> GetOwnStatusAsync(CancellationToken ct);

    /// <summary>
    /// Chuẩn bị tải ảnh thẻ của chính mình, đã kiểm toàn vẹn. Lỗi: 404 NOTARY_CARD_NOT_FOUND khi chưa có,
    /// ContentIntegrityException khi ảnh hỏng.
    /// </summary>
    Task<PreparedDownload> PrepareOwnAsync(CancellationToken ct);

    /// <summary>
    /// Admin xem ảnh thẻ của một Người xét duyệt, đã kiểm toàn vẹn. Lỗi: 404 khi không phải Admin hoặc không có tài khoản,
    /// 404 NOTARY_CARD_NOT_FOUND khi chưa có ảnh, ContentIntegrityException khi ảnh hỏng.
    /// </summary>
    Task<PreparedDownload> PrepareForAdminAsync(Guid userId, CancellationToken ct);
}
