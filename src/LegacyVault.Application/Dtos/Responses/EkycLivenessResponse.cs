namespace LegacyVault.Application.Dtos.Responses;

/// <summary>
/// Tiến độ thử thách camera (POST /api/v1/ekyc/liveness/start và /frame). Không có mã phiên và không có điểm số
/// chống giả mạo: backend giữ mã phiên, người dùng chưa xác minh không được thấy điểm (CLAIM-05).
/// </summary>
public class EkycLivenessResponse
{
    /// <summary>Các động tác phải làm, theo thứ tự ngẫu nhiên do dịch vụ chọn.</summary>
    public List<EkycLivenessActionResponse> Actions { get; set; } = new List<EkycLivenessActionResponse>();

    /// <summary>Các động tác đã hoàn thành (key), theo thứ tự.</summary>
    public List<string> Completed { get; set; } = new List<string>();

    public int CurrentIndex { get; set; }

    /// <summary>Câu hướng dẫn hiện tại để hiển thị cho người dùng.</summary>
    public string Prompt { get; set; } = string.Empty;

    public bool Calibrated { get; set; }

    /// <summary>Đã làm xong mọi động tác.</summary>
    public bool ChallengeCompleted { get; set; }

    public bool PadPassed { get; set; }

    /// <summary>true khi đủ điều kiện gọi POST /ekyc/liveness/complete.</summary>
    public bool Passed { get; set; }

    /// <summary>Lý do phiên thất bại; rỗng khi chưa thất bại. Thất bại thì phải gọi /liveness/start lại.</summary>
    public string FailedReason { get; set; } = string.Empty;

    /// <summary>Gợi ý cho khung hình vừa gửi; rỗng nếu không có.</summary>
    public string FrameHint { get; set; } = string.Empty;

    /// <summary>Số giây phiên còn hiệu lực kể từ lúc tạo; chỉ có ở /liveness/start.</summary>
    public int? ExpiresInSeconds { get; set; }
}
