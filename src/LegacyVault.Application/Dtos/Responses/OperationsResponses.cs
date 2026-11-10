namespace LegacyVault.Application.Dtos.Responses;

public class IncidentSummaryResponse
{
    public Guid Id { get; set; }

    /// <summary>NO_VERIFIER | NO_EXECUTOR | CHECK_IN_OVERDUE | NOTIFICATION_FAILED | INTEGRATION | MANUAL.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>OPEN | IN_PROGRESS | RESOLVED.</summary>
    public string Status { get; set; } = string.Empty;

    public Guid? VaultId { get; set; }

    public string TargetType { get; set; } = string.Empty;

    public Guid? TargetId { get; set; }

    public string Summary { get; set; } = string.Empty;

    /// <summary>null = chưa có ai nhận ("bị kẹt"), luôn kèm nextReviewAt.</summary>
    public Guid? AssigneeUserId { get; set; }

    /// <summary>Họ tên người phụ trách để hiện thẳng trên danh sách; null khi chưa có người nhận hoặc tài khoản không còn.</summary>
    public string? AssigneeFullName { get; set; }

    /// <summary>Vai trò tài khoản người phụ trách (USER | VERIFIER | ADMIN); null khi chưa có người nhận hoặc tài khoản không còn.</summary>
    public string? AssigneeRole { get; set; }

    /// <summary>ADMIN | EXECUTOR | VERIFIER: ai chịu trách nhiệm xử lý loại sự cố này; Admin chỉ theo dõi khi không phải ADMIN.</summary>
    public string ResponsibleRole { get; set; } = string.Empty;

    public DateTimeOffset DueAt { get; set; }

    public bool Overdue { get; set; }

    public DateTimeOffset NextReviewAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public bool HasActiveHold { get; set; }

    public Guid? HoldId { get; set; }

    public string Etag { get; set; } = string.Empty;
}

public class IncidentEventResponse
{
    public string Kind { get; set; } = string.Empty;

    public Guid? ActorUserId { get; set; }

    public string? Note { get; set; }

    public DateTimeOffset At { get; set; }
}

public class IncidentDetailResponse : IncidentSummaryResponse
{
    public int RemindedCount { get; set; }

    public DateTimeOffset? EscalatedAt { get; set; }

    public DateTimeOffset? ResolvedAt { get; set; }

    public string? ResolutionNote { get; set; }

    public List<IncidentEventResponse> Events { get; set; } = new List<IncidentEventResponse>();
}

public class HoldResponse
{
    public Guid Id { get; set; }

    public Guid VaultId { get; set; }

    /// <summary>VAULT | CONTAINER | DESIGNATION | CLAIM | GRANT.</summary>
    public string Scope { get; set; } = string.Empty;

    public Guid? TargetId { get; set; }

    /// <summary>DISPUTE | ADMIN.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>ACTIVE | RELEASED.</summary>
    public string Status { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public Guid? ResponsibleUserId { get; set; }

    public DateTimeOffset ReviewAt { get; set; }

    public bool ReviewOverdue { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ReleasedAt { get; set; }

    public string? ReleaseReason { get; set; }

    public string Etag { get; set; } = string.Empty;
}

/// <summary>Tài khoản cho Admin: không có mật khẩu, không có thông tin kho hay tài sản.</summary>
public class AdminUserResponse
{
    public Guid Id { get; set; }

    public string Email { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    /// <summary>USER | VERIFIER | ADMIN.</summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>ACTIVE | LOCKED | DISABLED.</summary>
    public string Status { get; set; } = string.Empty;

    public bool EmailVerified { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    public string Etag { get; set; } = string.Empty;
}

/// <summary>Chi tiết một tài khoản cho Admin: thông tin tài khoản cộng việc đang giữ, quyết định và hoạt động gần đây.</summary>
public class AdminUserDetailResponse : AdminUserResponse
{
    /// <summary>Hồ sơ chứng tử đang xét mà Verifier này được giao; rỗng với vai trò khác.</summary>
    public List<AdminUserCaseResponse> AssignedCases { get; set; } = new List<AdminUserCaseResponse>();

    /// <summary>Mười quyết định gần nhất của Verifier này (không kèm lý do hay nội dung hồ sơ).</summary>
    public List<AdminUserDecisionResponse> RecentDecisions { get; set; } = new List<AdminUserDecisionResponse>();

    /// <summary>Mười thao tác gần nhất do tài khoản này thực hiện (từ nhật ký, không kèm lý do).</summary>
    public List<AdminUserActivityResponse> RecentActivity { get; set; } = new List<AdminUserActivityResponse>();
}

public class AdminUserCaseResponse
{
    public Guid CaseId { get; set; }

    /// <summary>DeathCaseStatus dạng API.</summary>
    public string Status { get; set; } = string.Empty;

    public DateTimeOffset DueAt { get; set; }

    public bool Overdue { get; set; }
}

public class AdminUserDecisionResponse
{
    public Guid CaseId { get; set; }

    /// <summary>DeathCaseDecisionKind dạng API.</summary>
    public string Kind { get; set; } = string.Empty;

    public DateTimeOffset DecidedAt { get; set; }
}

public class AdminUserActivityResponse
{
    public DateTimeOffset At { get; set; }

    /// <summary>AuditAction dạng API.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>AuditOutcome dạng API.</summary>
    public string Outcome { get; set; } = string.Empty;
}

public class AdminDashboardResponse
{
    public int OpenIncidents { get; set; }

    public int OverdueIncidents { get; set; }

    /// <summary>Sự cố chưa có người phụ trách ("bị kẹt").</summary>
    public int UnassignedIncidents { get; set; }

    public int ActiveHolds { get; set; }

    public int HoldsPastReview { get; set; }

    public int ActiveVerifiers { get; set; }

    public int ActiveAdmins { get; set; }

    /// <summary>Hồ sơ chứng tử đang chờ Verifier mà chưa có người được giao.</summary>
    public int CasesWithoutVerifier { get; set; }

    public DateTimeOffset ServerTime { get; set; }

    /// <summary>Số kế hoạch theo giai đoạn (PlanStatus dạng API), chỉ giai đoạn có kế hoạch; không tính kho đã đóng.</summary>
    public List<DashboardPlanStageResponse> PlanStages { get; set; } = new List<DashboardPlanStageResponse>();

    /// <summary>Số kế hoạch đã đóng, không nằm trong PlanStages.</summary>
    public int ClosedPlans { get; set; }

    /// <summary>Tải việc của từng người xét duyệt đang hoạt động, nhiều hồ sơ nhất trước.</summary>
    public List<DashboardVerifierLoadResponse> VerifierLoad { get; set; } = new List<DashboardVerifierLoadResponse>();

    /// <summary>Năm hoạt động gần nhất trên toàn hệ thống (từ nhật ký), mới nhất trước.</summary>
    public List<DashboardRecentActivityResponse> RecentActivity { get; set; } = new List<DashboardRecentActivityResponse>();
}

public class DashboardPlanStageResponse
{
    /// <summary>PlanStatus dạng API, ví dụ ACTIVE, CHECKIN_OVERDUE.</summary>
    public string Stage { get; set; } = string.Empty;

    public int Count { get; set; }
}

public class DashboardVerifierLoadResponse
{
    public Guid UserId { get; set; }

    public string FullName { get; set; } = string.Empty;

    /// <summary>Hồ sơ đang xét hoặc cần bổ sung mà người này đang được giao.</summary>
    public int OpenCases { get; set; }

    /// <summary>Trong số đó, hồ sơ đã quá hạn xét duyệt.</summary>
    public int OverdueCases { get; set; }
}

public class DashboardRecentActivityResponse
{
    public DateTimeOffset At { get; set; }

    /// <summary>Họ tên người thực hiện; null khi job nền thực hiện hoặc tài khoản không còn.</summary>
    public string? ActorFullName { get; set; }

    /// <summary>AuditAction dạng API.</summary>
    public string Action { get; set; } = string.Empty;
}
