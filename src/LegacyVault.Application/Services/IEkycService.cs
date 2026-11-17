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
/// - Chỉ phục vụ xác minh danh tính của Chủ kho (mục đích OWNER_ENROLLMENT).
/// Lỗi chung: 401 SESSION_EXPIRED, 400 EKYC_IMAGE_REJECTED, 413 FILE_TOO_LARGE, 503 EKYC_SERVICE_UNAVAILABLE.
/// </remarks>
public interface IEkycService
{
    /// <summary>Kiểm tra bản tự khai đã sửa, giữ nguyên kết quả đối chiếu từ ảnh.</summary>
    Task<EkycStatusResponse> ConfirmDocumentAsync(EkycDocumentConfirmationRequest request, CancellationToken ct);

    /// <summary>
    /// Ghi nhận người dùng đồng ý cho chụp giấy tờ và khuôn mặt (EKYC-03). Phải gọi trước khi gửi mặt trước;
    /// đồng ý có hiệu lực 30 phút và gắn mục đích cho lượt eKYC kế tiếp (EKYC-01).
    /// Lỗi: 400 VALIDATION_ERROR (mục đích lạ hoặc phiên bản thông báo đã cũ).
    /// </summary>
    Task<EkycConsentResponse> RecordConsentAsync(EkycConsentRequest request, CancellationToken ct);

    /// <summary>
    /// Đọc mặt trước giấy tờ và giữ khuôn mặt trên thẻ để so khớp. Mỗi lần gọi là một lượt eKYC mới,
    /// lượt Pending trước đó bị hủy.
    /// Lỗi: 409 EKYC_CONSENT_REQUIRED (chưa đồng ý hoặc đã quá hạn), 400 EKYC_IMAGE_REJECTED (ảnh sai định dạng,
    /// rỗng, nhầm mặt thẻ).
    /// </summary>
    Task<EkycDocumentResponse> SubmitDocumentAsync(
        Stream content, string fileName, string contentType, CancellationToken ct);

    /// <summary>
    /// Gửi mặt sau của chính tấm thẻ vừa đọc mặt trước (EKYC-04). Mã trong MRZ mặt sau phải trùng 4 số cuối mặt
    /// trước. Thẻ không có MRZ chứa số căn cước thì không kết luận được và BackSideChecked giữ false.
    /// Phản hồi kèm nơi cư trú đọc từ mặt sau (rỗng khi không đọc được) để người dùng đối chiếu; không lưu (BR-88).
    /// Có thể gọi trước khi xác nhận bản khai: chỉ cần đã đọc mặt trước.
    /// Lỗi: 409 EKYC_DOCUMENT_EXPIRED (chưa đọc mặt trước hoặc quá 30 phút), 400 EKYC_DOCUMENT_MISMATCH
    /// (hai mặt không cùng thẻ), 400 EKYC_IMAGE_REJECTED.
    /// </summary>
    Task<EkycBackResponse> SubmitBackAsync(
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

    /// <summary>
    /// Kết quả eKYC theo đúng một mục đích (UPPER_SNAKE_CASE). Với OWNER_ENROLLMENT, đã từng đạt thì trả lượt đạt gần nhất:
    /// Owner xác minh một lần, chụp lại giữa chừng không làm mất kết quả đó.
    /// Mục đích không hợp lệ: 400. Chưa từng làm: Status NOT_STARTED.
    /// </summary>
    Task<EkycStatusResponse> GetMyStatusAsync(string? purpose, CancellationToken ct);
}
