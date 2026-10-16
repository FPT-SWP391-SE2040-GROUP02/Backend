namespace LegacyVault.Application.Dtos.Responses;

public class IncidentSummaryResponse
{
    public Guid Id { get; set; }

    /// <summary>NO_VERIFIER | NO_EXECUTOR | CHECK_IN_OVERDUE | NOTIFICATION_FAILED | OWNER_ALIVE | INTEGRATION | MANUAL.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>OPEN | IN_PROGRESS | RESOLVED.</summary>
    public string Status { get; set; } = string.Empty;

    public Guid? VaultId { get; set; }

    public string TargetType { get; set; } = string.Empty;

    public Guid? TargetId { get; set; }

    public string Summary { get; set; } = string.Empty;

    /// <summary>null = chưa có ai nhận ("bị kẹt"), luôn kèm nextReviewAt.</summary>
    public Guid? AssigneeUserId { get; set; }

    public DateTimeOffset DueAt { get; set; }

    public bool Overdue { get; set; }

    public DateTimeOffset NextReviewAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

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

    /// <summary>OWNER_ALIVE | DISPUTE | ADMIN.</summary>
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
}
