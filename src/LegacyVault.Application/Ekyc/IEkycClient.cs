namespace LegacyVault.Application.Ekyc;

/// <summary>
/// Cổng gọi dịch vụ eKYC (OCR giấy tờ, chống giả mạo, so khớp khuôn mặt). Cài đặt ở Infrastructure/Ekyc.
/// </summary>
/// <remarks>
/// Dịch vụ eKYC là chương trình Python riêng, không nằm trong solution này. Ảnh chỉ đi qua, không được lưu.
/// Lỗi (mọi method): 400 EKYC_IMAGE_REJECTED khi dịch vụ từ chối ảnh (không thấy mặt, nhầm mặt thẻ...),
/// 409 EKYC_DOCUMENT_EXPIRED khi mã tham chiếu đã hết hạn, 503 EKYC_SERVICE_UNAVAILABLE khi không gọi được dịch vụ.
/// </remarks>
public interface IEkycClient
{
    /// <summary>Đọc mặt trước giấy tờ; dịch vụ giữ khuôn mặt trên thẻ và trả mã tham chiếu.</summary>
    Task<EkycDocumentReading> ReadFrontAsync(byte[] image, string fileName, string contentType, CancellationToken ct);

    /// <summary>Đọc mặt sau giấy tờ để lấy mã định danh trong MRZ; không giữ khuôn mặt, không trả mã tham chiếu.</summary>
    Task<EkycBackReading> ReadBackAsync(byte[] image, string fileName, string contentType, CancellationToken ct);

    /// <summary>Kiểm tra dấu hiệu giả mạo (ảnh in, màn hình) trên một ảnh selfie.</summary>
    Task<EkycSpoofCheck> CheckSpoofAsync(byte[] image, string fileName, string contentType, CancellationToken ct);

    /// <summary>So khớp selfie với khuôn mặt đã giữ theo documentToken, dùng đúng ngưỡng server truyền vào.</summary>
    Task<EkycFaceComparison> CompareFaceAsync(
        string documentToken, byte[] selfie, string fileName, string contentType, double threshold, CancellationToken ct);

    /// <summary>Tạo phiên thử thách camera: dịch vụ chọn ngẫu nhiên động tác và trả mã phiên (EKYC-10).</summary>
    Task<EkycLivenessState> StartLivenessAsync(CancellationToken ct);

    /// <summary>
    /// Gửi một khung hình của phiên thử thách. Phiên hết hạn (410) thì báo EKYC_LIVENESS_REQUIRED để bắt đầu lại.
    /// </summary>
    Task<EkycLivenessState> SendLivenessFrameAsync(
        string sessionId, byte[] frame, string fileName, string contentType, CancellationToken ct);

    /// <summary>
    /// Lấy ảnh selfie tốt nhất của phiên đã đạt (EKYC-13). Phiên chưa đạt cả động tác và chống giả mạo thì
    /// 409 EKYC_LIVENESS_NOT_PASSED; phiên hết hạn thì 409 EKYC_LIVENESS_REQUIRED.
    /// </summary>
    Task<byte[]> GetLivenessSelfieAsync(string sessionId, CancellationToken ct);

    /// <summary>Hủy phiên thử thách. Không báo lỗi nếu phiên không còn.</summary>
    Task DeleteLivenessAsync(string sessionId, CancellationToken ct);

    /// <summary>Xóa khuôn mặt đã giữ theo documentToken. Không báo lỗi nếu mã không còn.</summary>
    Task DeleteDocumentAsync(string documentToken, CancellationToken ct);

    /// <summary>
    /// Lấy vector đặc trưng của đúng một khuôn mặt trong ảnh, để tạo hoặc so với mẫu khuôn mặt Owner (EKYC-22, EKYC-26).
    /// Dịch vụ không lưu vector. Ảnh không có đúng một khuôn mặt thì 400 EKYC_IMAGE_REJECTED; phản hồi sai dạng
    /// (không đủ 128 số) thì 503 EKYC_SERVICE_UNAVAILABLE.
    /// </summary>
    Task<EkycFaceEmbedding> GetFaceEmbeddingAsync(byte[] image, string fileName, string contentType, CancellationToken ct);
}
