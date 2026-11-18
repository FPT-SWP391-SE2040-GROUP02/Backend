namespace LegacyVault.Application.Services;

/// <summary>Kết quả một lần mở bàn giao: số lượt hiện có và số lời mời đã gửi hoặc gửi lỗi.</summary>
public sealed class HandoverStartResult
{
    public Guid PlanId { get; set; }

    /// <summary>Số lượt hiện có của kế hoạch sau lần gọi này.</summary>
    public int TurnCount { get; set; }

    /// <summary>true: kế hoạch đã mở bàn giao từ trước, lần gọi này không tạo gì mới.</summary>
    public bool AlreadyStarted { get; set; }

    public int InvitationsSent { get; set; }

    public int InvitationsFailed { get; set; }
}
