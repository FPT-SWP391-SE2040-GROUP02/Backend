namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Một dòng nhật ký hoạt động trả cho FE (SEC-13). Enum trả dạng UPPER_SNAKE_CASE.</summary>
public class AuditLogResponse
{
    public Guid Id { get; set; }

    public DateTimeOffset At { get; set; }

    /// <summary>OWNER / EXECUTOR / BENEFICIARY / VERIFIER / ADMIN / SYSTEM.</summary>
    public string ActorRole { get; set; } = string.Empty;

    /// <summary>Tài khoản thực hiện; null khi job nền thực hiện.</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>Ví dụ CONTAINER_CREATED, BENEFICIARY_UPDATED, CHECKED_IN.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>VAULT / PLAN / ASSET / CONTAINER / BENEFICIARY / CHECK_IN.</summary>
    public string TargetType { get; set; } = string.Empty;

    public Guid? TargetId { get; set; }

    /// <summary>Phiên bản đối tượng sau thao tác (ví dụ config_version của kế hoạch), nếu có.</summary>
    public int? TargetVersion { get; set; }

    /// <summary>SUCCEEDED / DENIED / FAILED.</summary>
    public string Outcome { get; set; } = string.Empty;

    public string? Reason { get; set; }
}
