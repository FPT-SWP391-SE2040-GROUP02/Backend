using LegacyVault.Application.Dtos.Requests;

namespace LegacyVault.Application.Services;

/// <summary>Nhận giao dịch tiền vào từ webhook SePay (SRS §8.3 bước 6, §8.5, PAY-05).</summary>
public interface ISePayWebhookService
{
    /// <summary>
    /// Xác thực key, kiểm dữ liệu, chống trùng, lưu giao dịch Received (lưu trước), rồi đối chiếu với đơn và cấp quyền
    /// hoặc đánh dấu cần đối chiếu kèm sự cố (PAY-05, PAY-07). Giao dịch lệch vẫn là xử lý xong (trả bình thường):
    /// webhook gửi lặp cũng trả bình thường. Controller khi đó mới trả {"success": true}.
    /// Key sai/thiếu/chưa cấu hình → AppException 401; dữ liệu sai → AppException 400; lỗi lưu hoặc xung đột
    /// (409) → ném tiếp, không success, để SePay gửi lại; giao dịch chưa đối chiếu xong vẫn Received và được xử lý tiếp.
    /// </summary>
    Task HandleAsync(string? authorizationHeader, SePayWebhookRequest request, CancellationToken ct);
}
