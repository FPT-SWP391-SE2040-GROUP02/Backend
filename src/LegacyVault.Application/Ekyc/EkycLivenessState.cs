namespace LegacyVault.Application.Ekyc;

/// <summary>
/// Trạng thái một phiên thử thách camera trong dịch vụ eKYC (EKYC-10..14). Chỉ lấy những trường cần cho người dùng;
/// điểm chống giả mạo không được chuyển tiếp.
/// </summary>
public sealed class EkycLivenessState
{
    /// <summary>Mã phiên của dịch vụ eKYC. Chỉ server biết, không trả cho FE.</summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>Số giây phiên còn hiệu lực kể từ lúc tạo (chỉ có khi vừa tạo phiên).</summary>
    public int ExpiresInSeconds { get; set; }

    public List<EkycLivenessAction> Actions { get; set; } = new List<EkycLivenessAction>();

    /// <summary>Các động tác đã hoàn thành, theo thứ tự.</summary>
    public List<string> Completed { get; set; } = new List<string>();

    public int CurrentIndex { get; set; }

    /// <summary>Câu hướng dẫn hiện tại.</summary>
    public string Prompt { get; set; } = string.Empty;

    public bool Calibrated { get; set; }

    /// <summary>Đã làm xong mọi động tác.</summary>
    public bool ChallengeCompleted { get; set; }

    /// <summary>Chống giả mạo qua đủ số mẫu và đạt ngưỡng của dịch vụ.</summary>
    public bool PadPassed { get; set; }

    /// <summary>Cả động tác và chống giả mạo đều đạt (prototype_passed của dịch vụ).</summary>
    public bool Passed { get; set; }

    /// <summary>Lý do phiên thất bại (ví dụ đổi người giữa chừng); rỗng khi chưa thất bại.</summary>
    public string FailedReason { get; set; } = string.Empty;

    /// <summary>Gợi ý cho khung hình vừa gửi (không thấy mặt, khung hình trùng...); rỗng nếu không có.</summary>
    public string FrameHint { get; set; } = string.Empty;
}
