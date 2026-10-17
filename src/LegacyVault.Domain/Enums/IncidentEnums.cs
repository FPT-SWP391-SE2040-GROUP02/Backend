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
