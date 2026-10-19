using LegacyVault.Application.Dtos.Requests;

namespace LegacyVault.Application.Services;

/// <summary>Nhận giao dịch tiền vào từ webhook SePay (SRS §8.3 bước 6, §8.5, PAY-05).</summary>
public interface ISePayWebhookService
{
    /// <summary>
    /// Xác thực key, kiểm dữ liệu, chống trùng rồi lưu giao dịch ở trạng thái Received (lưu trước, đối chiếu sau).
    /// Trả bình thường khi giao dịch đã được lưu (kể cả webhook gửi lặp); controller khi đó mới trả {"success": true}.
    /// Key sai/thiếu/chưa cấu hình → AppException 401; dữ liệu sai → AppException 400; lỗi lưu → ném tiếp để trả 500.
    /// </summary>
    Task HandleAsync(string? authorizationHeader, SePayWebhookRequest request, CancellationToken ct);
}
