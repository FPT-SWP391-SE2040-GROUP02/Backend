using LegacyVault.Application.Dtos.Responses;

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

    /// <summary>Ảnh thẻ của chính mình đã có chưa (chỉ Người xét duyệt hoặc Người thực thi hệ thống; vai trò khác 404).</summary>
    Task<NotaryCardStatusResponse> GetStatusAsync(CancellationToken ct);

    /// <summary>
    /// Chuẩn bị tải ảnh thẻ của chính mình, đã giải mã và kiểm toàn vẹn (ContentIntegrityException khi hỏng).
    /// 404 khi chưa có ảnh hoặc vai trò không có ảnh thẻ.
    /// </summary>
    Task<PreparedDownload> PrepareAsync(CancellationToken ct);

    /// <summary>
    /// Admin xem ảnh thẻ của một Người xét duyệt hoặc Người thực thi hệ thống, đã giải mã và kiểm toàn vẹn.
    /// 404 khi không phải Admin, không có tài khoản, tài khoản không giữ ảnh thẻ hoặc chưa có ảnh.
    /// </summary>
    Task<PreparedDownload> PrepareForAdminAsync(Guid userId, CancellationToken ct);
}
