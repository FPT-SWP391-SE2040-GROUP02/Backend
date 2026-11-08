namespace LegacyVault.Domain.Enums;

/// <summary>Loại sự cố cần người xử lý (SRS §6.4, INC-*, ASSIGN-04, DMS-05).</summary>
public enum IncidentType
{
    /// <summary>Không có Verifier phù hợp để giao hồ sơ chứng tử (ASSIGN-04).</summary>
    NoVerifier = 0,
    /// <summary>Không có Executor phù hợp để giao việc ("bị kẹt", ASSIGN-05).</summary>
    NoExecutor = 1,
    /// <summary>Quá hạn điểm danh, cần Executor kiểm tra tình trạng Owner (DMS-05).</summary>
    CheckInOverdue = 2,
    /// <summary>Thông báo quan trọng gửi lỗi đủ số lần (DEATH-08, RETENTION-05).</summary>
    NotificationFailed = 3,
    /// <summary>Owner báo còn sống, chờ Verifier kết luận (ALIVE-01).</summary>
    OwnerAlive = 4,
    /// <summary>Lỗi kỹ thuật eKYC hoặc dịch vụ ngoài.</summary>
    Integration = 5,
    /// <summary>Admin hoặc nhân viên tạo thủ công.</summary>
    Manual = 6,
    /// <summary>Giao dịch thanh toán không khớp đơn, cần Admin đối chiếu thủ công (PAY-07).</summary>
    Payment = 7,
    /// <summary>Tài khoản bị tạm khóa vì nhập sai mật khẩu quá nhiều lần, có thể đang bị dò mật khẩu (SEC-10).</summary>
    SuspiciousLogin = 8,
    /// <summary>Hồ sơ chứng tử quá hạn xét duyệt mà Verifier được giao chưa quyết định.</summary>
    StaleCase = 9,
    /// <summary>Hồ sơ chứng tử bị yêu cầu bổ sung nhiều lần liên tiếp, cần Admin xem vì sao chưa xong.</summary>
    SupplementLoop = 10,
    /// <summary>Gói trả phí của kho sắp hết hạn; hết hạn rồi kho sẽ bị khóa rồi xóa.</summary>
    PlanExpiring = 11,
    /// <summary>Executor đã dùng hết lượt điểm danh thay liên tiếp mà chủ kho vẫn quá hạn; không ai điểm danh thay được nữa.</summary>
    ProxyLimit = 12,
}

public enum IncidentStatus
{
    Open = 0,
    InProgress = 1,
    Resolved = 2,
}

public enum IncidentEventKind
{
    Raised = 0,
    Assigned = 1,
    Note = 2,
    Reviewed = 3,
    Reminded = 4,
    Escalated = 5,
    Resolved = 6,
}
