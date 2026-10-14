using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>
/// Xác minh danh tính điện tử (eKYC) của người dùng đang đăng nhập: đọc mặt trước giấy tờ rồi so khớp selfie.
/// </summary>
/// <remarks>
/// Luật chung:
/// - Người dùng lấy từ token (SEC-04), không nhận userId từ FE. Ngưỡng so khớp do server cố định.
/// - Chỉ lưu kết quả, không lưu ảnh (xem EkycVerification). Ảnh chỉ nhận JPEG/PNG, tối đa 8 MiB.
/// - eKYC chỉ là bằng chứng hỗ trợ; không tự quyết định xác minh người nhận ở giai đoạn C của luồng bàn giao.
/// Lỗi chung: 401 SESSION_EXPIRED, 400 EKYC_IMAGE_REJECTED, 413 FILE_TOO_LARGE, 503 EKYC_SERVICE_UNAVAILABLE.
/// </remarks>
public interface IEkycService
{
    /// <summary>
    /// Đọc mặt trước giấy tờ và giữ khuôn mặt trên thẻ để so khớp. Mỗi lần gọi là một lượt eKYC mới,
    /// lượt Pending trước đó bị hủy.
    /// Lỗi: 400 EKYC_IMAGE_REJECTED (ảnh sai định dạng, rỗng, nhầm mặt thẻ).
    /// </summary>
    Task<EkycDocumentResponse> SubmitDocumentAsync(
        Stream content, string fileName, string contentType, CancellationToken ct);

    /// <summary>
    /// Gửi selfie để so khớp với khuôn mặt trên thẻ vừa đọc. Mỗi lượt chỉ được so khớp MỘT lần
    /// (kết quả Passed hoặc Failed); muốn thử lại phải đọc lại mặt trước.
    /// Lỗi: 409 EKYC_DOCUMENT_EXPIRED (chưa đọc giấy tờ, đã so khớp rồi, hoặc quá 30 phút),
    /// 400 EKYC_IMAGE_REJECTED (selfie không có đúng một khuôn mặt).
    /// </summary>
    Task<EkycStatusResponse> SubmitSelfieAsync(
        Stream content, string fileName, string contentType, CancellationToken ct);

    /// <summary>Kết quả eKYC mới nhất; chưa từng làm thì Status NOT_STARTED.</summary>
    Task<EkycStatusResponse> GetMyStatusAsync(CancellationToken ct);
}
