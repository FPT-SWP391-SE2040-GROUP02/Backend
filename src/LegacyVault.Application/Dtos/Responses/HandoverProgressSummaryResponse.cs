namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Đếm số lượt bàn giao theo nhóm trạng thái để Executor nhìn nhanh.</summary>
public class HandoverProgressSummaryResponse
{
    public int TotalTurns { get; set; }

    /// <summary>Chưa ai nhận lượt: INVITED, INITIAL_RESPONSE_EXPIRED.</summary>
    public int WaitingResponse { get; set; }

    /// <summary>Đã nhận lượt, đang xác minh hoặc chờ quyết định: IDENTITY_IN_PROGRESS, READY_TO_ACCEPT.</summary>
    public int InVerification { get; set; }

    /// <summary>Người nhận đã đồng ý: HANDOVER_COMMITTED.</summary>
    public int Accepted { get; set; }

    /// <summary>Người nhận đã từ chối: DECLINED.</summary>
    public int Declined { get; set; }

    /// <summary>Lượt đã kết thúc vì lý do khác: DECISION_EXPIRED, CANCELLED, LAPSED.</summary>
    public int Ended { get; set; }
}
