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

/// <summary>
/// Mở giai đoạn nhận tài sản cho một kho: tạo mỗi chỉ định một lượt, ghim danh sách tài sản và gửi lời mời.
/// Được DeathCaseReviewService gọi ngay khi Người xét duyệt duyệt hồ sơ chứng tử, trong cùng một lần lưu.
/// </summary>
public interface IHandoverStarter
{
    /// <summary>Mở bàn giao cho kho. Gọi lặp thì trả kết quả cũ (AlreadyStarted), không tạo lượt hay gửi thư lần nữa.</summary>
    Task<HandoverStartResult> StartAsync(Guid vaultId, CancellationToken ct);
}
