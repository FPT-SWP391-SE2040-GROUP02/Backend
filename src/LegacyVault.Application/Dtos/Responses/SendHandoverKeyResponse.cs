namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Kết quả gửi khóa mở gói cho người nhận. Không có email hay khóa trong phản hồi (OPS-04).</summary>
public class SendHandoverKeyResponse
{
    public Guid TurnId { get; set; }

    public DateTimeOffset SentAt { get; set; }
}
