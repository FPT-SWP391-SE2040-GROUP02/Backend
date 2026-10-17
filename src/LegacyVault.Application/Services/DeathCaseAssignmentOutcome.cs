namespace LegacyVault.Application.Services;

/// <summary>Kết quả xét một hồ sơ của job phân công Verifier (ASSIGN-04); monitor và test đọc giá trị này.</summary>
public enum DeathCaseAssignmentOutcome
{
    /// <summary>Không có gì để làm (hồ sơ đã đóng, đã xử lý, hoặc chưa tới lúc).</summary>
    None = 0,

    /// <summary>Hồ sơ chưa có người giữ, đã giao cho một Verifier (không đổi UpdatedAt, D4).</summary>
    Assigned = 1,

    /// <summary>Phân công cũ đã kết thúc (OVERDUE hoặc VERIFIER_UNAVAILABLE) và giao cho người khác (D4, D5).</summary>
    Reassigned = 2,

    /// <summary>Đã gửi thư nhắc Verifier đang giữ hồ sơ (D3).</summary>
    Reminded = 3,

    /// <summary>Cần giao người nhưng không có Verifier phù hợp; vòng sau thử lại (D5).</summary>
    NoCandidate = 4,

    /// <summary>Gửi thư nhắc lỗi; vòng sau thử lại (D3).</summary>
    ReminderFailed = 5,

    /// <summary>Lần lưu xung đột với request khác (D4); đã bỏ thay đổi, vòng sau đọc lại.</summary>
    Conflict = 6,
}
