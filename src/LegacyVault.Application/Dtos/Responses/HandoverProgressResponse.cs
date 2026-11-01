namespace LegacyVault.Application.Dtos.Responses;

/// <summary>
/// Tiến độ bàn giao của một hồ sơ chứng tử cho Executor
/// (GET /api/v1/executor/death-cases/{caseId}/handover-progress).
/// </summary>
/// <remarks>
/// Chỉ có trạng thái, hạn và việc đã tải hay chưa. Không có nội dung tài sản hay số CCCD đầy đủ.
/// Họ tên và số điện thoại người nhận có ở lượt còn hiệu lực hoặc đã bàn giao để Executor liên hệ; lượt đã kết thúc thì không.
/// </remarks>
public class HandoverProgressResponse
{
    public Guid CaseId { get; set; }

    /// <summary>Hồ sơ đã được duyệt và đã mở bàn giao. Chưa duyệt thì danh sách lượt rỗng.</summary>
    public bool HandoverStarted { get; set; }

    /// <summary>Giờ máy chủ để FE tính thời gian còn lại, không dùng giờ trình duyệt.</summary>
    public DateTimeOffset ServerTime { get; set; }

    public HandoverProgressSummaryResponse Summary { get; set; } = new HandoverProgressSummaryResponse();

    /// <summary>Mỗi phần tử là một lượt bàn giao (một gói cho một người nhận), xếp theo gói rồi theo lúc mời.</summary>
    public List<HandoverProgressTurnResponse> Turns { get; set; } = new List<HandoverProgressTurnResponse>();
}
