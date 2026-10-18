using LegacyVault.Application.Dtos.Requests;
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
    /// Ghi nhận người dùng đồng ý cho chụp giấy tờ và khuôn mặt (EKYC-03). Phải gọi trước khi gửi mặt trước;
    /// đồng ý có hiệu lực 30 phút và gắn mục đích cho lượt eKYC kế tiếp (EKYC-01).
    /// Lỗi: 400 VALIDATION_ERROR (mục đích lạ hoặc phiên bản thông báo đã cũ).
    /// </summary>
    Task<EkycConsentResponse> RecordConsentAsync(EkycConsentRequest request, CancellationToken ct);

    /// <summary>
    /// Đọc mặt trước giấy tờ và giữ khuôn mặt trên thẻ để so khớp. Mỗi lần gọi là một lượt eKYC mới,
    /// lượt Pending trước đó bị hủy.
    /// Mục đích BENEFICIARY_CLAIM bắt buộc kèm handoverTurnId và được đối chiếu với người được chỉ định của lượt đó
    /// (EKYC-02, MATCH-01..05); mục đích khác không được gửi kèm lượt. Kết quả đối chiếu chỉ ghi lại, không tự đóng lượt.
    /// Lỗi: 409 EKYC_CONSENT_REQUIRED (chưa đồng ý hoặc đã quá hạn), 400 EKYC_IMAGE_REJECTED (ảnh sai định dạng,
    /// rỗng, nhầm mặt thẻ), 400 VALIDATION_ERROR (thiếu hoặc thừa handoverTurnId),
    /// 404 HANDOVER_NOT_FOUND (lượt không tồn tại hoặc không phải của người gọi),
    /// 409 HANDOVER_NOT_ALLOWED (lượt không ở bước xác minh danh tính).
    /// </summary>
    Task<EkycDocumentResponse> SubmitDocumentAsync(
        Stream content, string fileName, string contentType, Guid? handoverTurnId, CancellationToken ct);

    /// <summary>
    /// Gửi mặt sau của chính tấm thẻ vừa đọc mặt trước (EKYC-04). Mã trong MRZ mặt sau phải trùng 4 số cuối mặt
    /// trước. Thẻ không có MRZ chứa số căn cước thì không kết luận được và BackSideChecked giữ false.
    /// Lỗi: 409 EKYC_DOCUMENT_EXPIRED (chưa đọc mặt trước hoặc quá 30 phút), 400 EKYC_DOCUMENT_MISMATCH
    /// (hai mặt không cùng thẻ), 400 EKYC_IMAGE_REJECTED.
    /// </summary>
    Task<EkycStatusResponse> SubmitBackAsync(
        Stream content, string fileName, string contentType, CancellationToken ct);

    /// <summary>
    /// Gửi selfie để so khớp với khuôn mặt trên thẻ vừa đọc. Mỗi lượt chỉ được so khớp MỘT lần
    /// (kết quả Passed hoặc Failed); muốn thử lại phải đọc lại mặt trước.
    /// Lỗi: 409 EKYC_DOCUMENT_EXPIRED (chưa đọc giấy tờ, đã so khớp rồi, hoặc quá 30 phút),
    /// 409 EKYC_LIVENESS_REQUIRED (chưa bắt đầu thử thách camera), 409 EKYC_LIVENESS_NOT_PASSED (đã bắt đầu nhưng chưa đạt),
    /// 400 EKYC_IMAGE_REJECTED (selfie không có đúng một khuôn mặt).
    /// </summary>
    Task<EkycStatusResponse> SubmitSelfieAsync(
        Stream content, string fileName, string contentType, CancellationToken ct);

    /// <summary>
    /// Bắt đầu thử thách camera cho lượt eKYC đang chờ selfie (EKYC-10): dịch vụ eKYC chọn ngẫu nhiên các động tác
    /// (chớp mắt, quay đầu trái hoặc phải). Mã phiên do backend giữ; gọi lại thì phiên cũ bị hủy.
    /// Lỗi: 409 EKYC_DOCUMENT_EXPIRED (chưa đọc mặt trước hoặc quá 30 phút), 503 EKYC_SERVICE_UNAVAILABLE.
    /// </summary>
    Task<EkycLivenessResponse> StartLivenessAsync(CancellationToken ct);

    /// <summary>
    /// Gửi một khung hình camera của phiên thử thách và trả tiến độ. Phiên là phiên của chính người gọi, không nhận
    /// mã phiên từ FE. Phiên thất bại (đổi người, quá nhiều khung hình) thì phải bắt đầu lại.
    /// Lỗi: 409 EKYC_DOCUMENT_EXPIRED, 409 EKYC_LIVENESS_REQUIRED (chưa bắt đầu hoặc phiên hết hạn),
    /// 400 EKYC_IMAGE_REJECTED, 413 FILE_TOO_LARGE, 503 EKYC_SERVICE_UNAVAILABLE.
    /// </summary>
    Task<EkycLivenessResponse> SubmitLivenessFrameAsync(
        Stream content, string fileName, string contentType, CancellationToken ct);

    /// <summary>
    /// Chốt thử thách: lấy selfie của chính phiên đã đạt (EKYC-13) rồi so khớp với khuôn mặt trên thẻ. Chống giả mạo
    /// và động tác phải đạt riêng từng cái trong phiên (EKYC-14). Mỗi lượt chỉ so khớp một lần.
    /// Lỗi: 409 EKYC_DOCUMENT_EXPIRED, 409 EKYC_LIVENESS_REQUIRED, 409 EKYC_LIVENESS_NOT_PASSED (chưa làm xong
    /// động tác hoặc chống giả mạo chưa đạt), 503 EKYC_SERVICE_UNAVAILABLE.
    /// </summary>
    Task<EkycStatusResponse> CompleteLivenessAsync(CancellationToken ct);

    /// <summary>Kết quả eKYC mới nhất; chưa từng làm thì Status NOT_STARTED.</summary>
    Task<EkycStatusResponse> GetMyStatusAsync(CancellationToken ct);
}
