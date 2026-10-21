namespace LegacyVault.Application.Dtos.Requests;

/// <summary>
/// Nội dung webhook SePay (tiền vào/ra tài khoản), đúng tên trường của SePay. Chỉ khai báo các trường backend dùng;
/// trường khác SePay gửi (subAccount, description, accumulated...) bị bỏ qua khi bind.
/// </summary>
/// <remarks>Không ghi log nội dung này: có số tài khoản và nội dung chuyển khoản.</remarks>
public sealed class SePayWebhookRequest
{
    /// <summary>ID giao dịch phía SePay. Thiếu (null) hoặc không dương → 400.</summary>
    public long? Id { get; set; }

    public string? Gateway { get; set; }

    /// <summary>Giờ giao dịch theo giờ Việt Nam, định dạng "yyyy-MM-dd HH:mm:ss".</summary>
    public string? TransactionDate { get; set; }

    public string? AccountNumber { get; set; }

    /// <summary>Mã thanh toán SePay nhận diện được từ nội dung (có thể null).</summary>
    public string? Code { get; set; }

    public string? Content { get; set; }

    /// <summary>"in" (tiền vào) hoặc "out" (tiền ra).</summary>
    public string? TransferType { get; set; }

    /// <summary>Số tiền VND. Thiếu (null) hoặc âm → 400.</summary>
    public long? TransferAmount { get; set; }

    public string? ReferenceCode { get; set; }
}
