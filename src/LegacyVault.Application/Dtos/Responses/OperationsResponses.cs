namespace LegacyVault.Application.Dtos.Responses;

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

    /// <summary>True khi máy chủ thư đã báo không giao được thư tới email này (bounce).</summary>
    public bool EmailUndeliverable { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    public string Etag { get; set; } = string.Empty;
}

/// <summary>Chi tiết một tài khoản cho Admin: thông tin tài khoản cộng việc đang giữ, quyết định và hoạt động gần đây.</summary>
public class AdminUserDetailResponse : AdminUserResponse
{
    /// <summary>
    /// Việc đang giữ của luật sư: biên bản bàn giao chưa hoàn tất, chưa hủy và hồ sơ báo tin còn chờ xét được giao cho
    /// người này; rỗng với vai trò khác.
    /// </summary>
    public List<AdminUserCaseResponse> AssignedCases { get; set; } = new List<AdminUserCaseResponse>();

    /// <summary>
    /// Mười lần xét gần nhất của luật sư (yêu cầu nhận tài sản và hồ sơ báo tin), không kèm lý do hay nội dung; rỗng với
    /// vai trò khác.
    /// </summary>
    public List<AdminUserDecisionResponse> RecentDecisions { get; set; } = new List<AdminUserDecisionResponse>();

    /// <summary>Kho mà tài khoản đang là Người thực thi hiện tại; rỗng với vai trò khác.</summary>
    public List<AdminUserHeldVaultResponse> HeldVaults { get; set; } = new List<AdminUserHeldVaultResponse>();

    /// <summary>Mười thao tác gần nhất do tài khoản này thực hiện (từ nhật ký, không kèm lý do).</summary>
    public List<AdminUserActivityResponse> RecentActivity { get; set; } = new List<AdminUserActivityResponse>();
}

/// <summary>Một việc luật sư đang giữ: biên bản bàn giao hoặc hồ sơ báo tin. Không kèm nội dung hay lý do.</summary>
public class AdminUserCaseResponse
{
    public const string KindHandoverRecord = "HANDOVER_RECORD";
    public const string KindHandoverReport = "HANDOVER_REPORT";

    /// <summary>HANDOVER_RECORD (biên bản bàn giao) hoặc HANDOVER_REPORT (hồ sơ báo tin).</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Id biên bản hoặc hồ sơ báo tin.</summary>
    public Guid CaseId { get; set; }

    /// <summary>Mã rút gọn 8 ký tự cuối của Id, chữ hoa.</summary>
    public string CaseCode { get; set; } = string.Empty;

    /// <summary>HandoverRecordStatus (biên bản) hoặc HandoverClaimStatus (hồ sơ báo tin) dạng API.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Lúc luật sư nhận việc: lúc có yêu cầu (biên bản) hoặc lúc nộp (hồ sơ báo tin).</summary>
    public DateTimeOffset ReceivedAt { get; set; }
}

/// <summary>Một lần luật sư xét giấy tờ. Không kèm lý do hay nội dung hồ sơ.</summary>
public class AdminUserDecisionResponse
{
    /// <summary>HANDOVER_RECORD (xét yêu cầu nhận tài sản trong biên bản) hoặc HANDOVER_REPORT (xét hồ sơ báo tin).</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Id biên bản hoặc hồ sơ báo tin.</summary>
    public Guid CaseId { get; set; }

    public string CaseCode { get; set; } = string.Empty;

    /// <summary>Kết quả xét (HandoverClaimStatus dạng API): ACCEPTED, REJECTED hoặc SUPPLEMENT_REQUESTED.</summary>
    public string Decision { get; set; } = string.Empty;

    public DateTimeOffset DecidedAt { get; set; }
}

/// <summary>Một kho Người thực thi đang giữ. Không có CCCD, tên tài sản hay email người nhận.</summary>
public class AdminUserHeldVaultResponse
{
    public Guid VaultId { get; set; }

    /// <summary>Họ tên Chủ kho đã che: chỉ giữ chữ đầu mỗi từ (ví dụ "N. V. A.").</summary>
    public string OwnerNameMasked { get; set; } = string.Empty;

    /// <summary>PlanStatus dạng API.</summary>
    public string PlanStatus { get; set; } = string.Empty;

    /// <summary>Bước biên bản đang mở (HandoverRecordStatus dạng API); null khi chưa có biên bản.</summary>
    public string? HandoverRecordStatus { get; set; }

    /// <summary>Kho đang bàn giao: đổi Người thực thi thì người mới nhận việc ngay.</summary>
    public bool HandoverUnderway { get; set; }

    /// <summary>Admin đổi được Người thực thi không (false khi kho hoặc kế hoạch đã đóng).</summary>
    public bool CanChangeExecutor { get; set; }
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
    public int ActiveVerifiers { get; set; }

    public int ActiveAdmins { get; set; }

    /// <summary>
    /// Việc chưa có luật sư làm: biên bản bàn giao chưa hoàn tất, chưa hủy và hồ sơ báo tin còn chờ xét mà luật sư được
    /// giao không còn hoạt động.
    /// </summary>
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
    /// <summary>PlanStatus dạng API, ví dụ ACTIVE, HANDOVER_IN_PROGRESS.</summary>
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
