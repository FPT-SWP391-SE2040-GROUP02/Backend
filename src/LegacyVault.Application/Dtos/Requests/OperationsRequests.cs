using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

public class AssignIncidentRequest
{
    [Required]
    public Guid? AssigneeUserId { get; set; }

    [Required(ErrorMessage = "Vui lòng ghi lý do.")]
    [MaxLength(1000)]
    public string Reason { get; set; } = string.Empty;

    [Required]
    public string Etag { get; set; } = string.Empty;
}

public class IncidentNoteRequest
{
    [Required(ErrorMessage = "Vui lòng ghi ghi chú.")]
    [MaxLength(1000)]
    public string Note { get; set; } = string.Empty;
}

public class ReviewIncidentRequest
{
    [Required]
    public DateTimeOffset? NextReviewAt { get; set; }

    [Required(ErrorMessage = "Vui lòng ghi kết quả xem lại.")]
    [MaxLength(1000)]
    public string Note { get; set; } = string.Empty;

    [Required]
    public string Etag { get; set; } = string.Empty;
}

public class ResolveIncidentRequest
{
    [Required(ErrorMessage = "Vui lòng ghi kết quả xử lý.")]
    [MaxLength(1000)]
    public string Note { get; set; } = string.Empty;

    [Required]
    public string Etag { get; set; } = string.Empty;
}

public class PlaceHoldRequest
{
    [Required]
    public Guid? VaultId { get; set; }

    /// <summary>VAULT hoặc CONTAINER; bỏ trống = VAULT.</summary>
    [MaxLength(32)]
    public string? Scope { get; set; }

    /// <summary>Bắt buộc khi Scope là CONTAINER.</summary>
    public Guid? TargetId { get; set; }

    [Required(ErrorMessage = "Vui lòng ghi lý do.")]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    /// <summary>Ngày xem lại đầu tiên, tối đa 7 ngày kể từ bây giờ; bỏ trống = 7 ngày.</summary>
    public DateTimeOffset? ReviewAt { get; set; }
}

public class ReleaseHoldRequest
{
    [Required(ErrorMessage = "Vui lòng ghi lý do gỡ.")]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;

    [Required]
    public string Etag { get; set; } = string.Empty;
}

public class ReviewHoldRequest
{
    [Required]
    public DateTimeOffset? NextReviewAt { get; set; }

    [Required]
    public string Etag { get; set; } = string.Empty;
}

public class ChangeUserRoleRequest
{
    /// <summary>USER | VERIFIER | ADMIN.</summary>
    [Required]
    [MaxLength(16)]
    public string Role { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng ghi lý do.")]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}

public class ChangeUserStatusRequest
{
    /// <summary>ACTIVE | LOCKED | DISABLED.</summary>
    [Required]
    [MaxLength(16)]
    public string Status { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng ghi lý do.")]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}

public class ReassignVerifierRequest
{
    [Required]
    public Guid? VerifierUserId { get; set; }

    [Required(ErrorMessage = "Vui lòng ghi lý do.")]
    [MaxLength(500)]
    public string Reason { get; set; } = string.Empty;
}
