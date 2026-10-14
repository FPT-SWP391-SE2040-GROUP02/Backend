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

    /// <summary>Kiểm tra dấu hiệu giả mạo (ảnh in, màn hình) trên một ảnh selfie.</summary>
    Task<EkycSpoofCheck> CheckSpoofAsync(byte[] image, string fileName, string contentType, CancellationToken ct);

    /// <summary>So khớp selfie với khuôn mặt đã giữ theo documentToken, dùng đúng ngưỡng server truyền vào.</summary>
    Task<EkycFaceComparison> CompareFaceAsync(
        string documentToken, byte[] selfie, string fileName, string contentType, double threshold, CancellationToken ct);

    /// <summary>Xóa khuôn mặt đã giữ theo documentToken. Không báo lỗi nếu mã không còn.</summary>
    Task DeleteDocumentAsync(string documentToken, CancellationToken ct);
}
