namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Kết quả Executor xác nhận đúng người bằng tay cho một lượt bàn giao (BR-122).</summary>
public class ConfirmHandoverIdentityResponse
{
    public Guid TurnId { get; set; }

    /// <summary>Trạng thái lượt sau khi xác nhận, dạng UPPER_SNAKE_CASE (READY_TO_ACCEPT).</summary>
    public string Status { get; set; } = string.Empty;

    public DateTimeOffset IdentityConfirmedAt { get; set; }

    /// <summary>Hạn đồng ý hoặc từ chối của người nhận: lúc xác nhận + 7 ngày.</summary>
    public DateTimeOffset DecisionDeadline { get; set; }
}
