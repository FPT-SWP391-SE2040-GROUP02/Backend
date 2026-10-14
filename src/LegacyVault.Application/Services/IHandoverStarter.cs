namespace LegacyVault.Application.Services;

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

/// <summary>
/// Mở giai đoạn nhận tài sản cho một kho: tạo mỗi chỉ định một lượt, ghim snapshot và gửi lời mời.
/// Chưa có endpoint gọi nó; luồng F2B (xác nhận qua đời) sau này sẽ gọi.
/// </summary>
public interface IHandoverStarter
{
    Task<HandoverStartResult> StartAsync(Guid vaultId, CancellationToken ct);
}
