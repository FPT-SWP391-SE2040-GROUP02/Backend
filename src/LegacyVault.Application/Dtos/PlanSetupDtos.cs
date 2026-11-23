using System.ComponentModel.DataAnnotations;
using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Dtos;

/// <summary>Owner-only metadata; token hashes and raw tokens are never serialized.</summary>
public sealed record ExecutorInvitationResponse(Guid Id, string Status, string InviteeEmail,
    DateTimeOffset ExpiresAt, DateTimeOffset? AcceptedAt,
    DateTimeOffset? InvitationSentAt, string? AcceptedScopeVersion, string Etag, bool AccountExists = false,
    string? FullName = null, bool EmailUndeliverable = false);
public sealed record CheckInviteeEmailResponse(string Email, bool Exists);
/// <summary>Tài khoản có sẵn mà Chủ kho có thể chọn làm Người thực thi.</summary>
public sealed record ExecutorCandidateResponse(Guid UserId, string FullName, string Email);
public sealed record PlanSetupResponse(string PlanStatus, int ConfigVersion, bool CanActivate,
    string[] MissingRequirements, string PolicyVersion, string PolicyText, string ExecutorScopeVersion,
    string ExecutorScopeText, List<ExecutorInvitationResponse> Executors,
    int AssignedAssetCount, List<PlanSetupAssetResponse> UnassignedAssets, string[] Warnings,
    string OwnerFullName = "", string OwnerEmail = "",
    DateTimeOffset? ActivatedAt = null, List<PlanSetupRecipientResponse>? Recipients = null,
    int? WillVersion = null, DateTimeOffset? WillSignedAt = null, bool HasUnsignedChanges = false,
    UnsignedWillChangesResponse? UnsignedChanges = null);

/// <summary>
/// Một tài sản được gán cho người nhận trên màn kích hoạt (chỉ tên và loại, không có nội dung).
/// SharedWithCount là số người nhận tài sản này, tính cả người đang xem; lớn hơn 1 nghĩa là nhận chung.
/// </summary>
public sealed record PlanSetupRecipientAssetResponse(Guid Id, string Name, string Kind, int SharedWithCount);

/// <summary>Người nhận theo từng người (di chúc điện tử). Chỉ cho biết đã có lời nhắn hay chưa, không trả nội dung lời nhắn.</summary>
public sealed record PlanSetupRecipientResponse(Guid BeneficiaryId, string FullName, bool HasMessage,
    List<PlanSetupRecipientAssetResponse> Assets);
public sealed record ExecutorInvitationInfo(DateTimeOffset ExpiresAt, string Status,
    string ScopeVersion, string ScopeText);

public sealed class InviteExecutorRequest
{
    [Required, EmailAddress, MaxLength(254)] public string Email { get; set; } = "";
}
public sealed class ResendExecutorRequest
{
    [Required, MaxLength(64)] public string Etag { get; set; } = "";
}
public class InspectExecutorInvitationRequest
{
    [Required, MinLength(32), MaxLength(512)] public string Token { get; set; } = "";
}
public sealed class RespondExecutorInvitationRequest : InspectExecutorInvitationRequest
{
    public bool Accept { get; set; }
    public bool AcknowledgeScope { get; set; }
    [Required, MaxLength(64)] public string ScopeVersion { get; set; } = "";

    /// <summary>Mật khẩu tài khoản để ký nhận vai khi chấp nhận; từ chối thì bỏ qua.</summary>
    [MaxLength(128)] public string? Password { get; set; }
}

/// <summary>Trả lời lời mời theo id (lấy từ GET /executor-invitations/mine), không cần token trong link email.</summary>
public sealed class RespondMyExecutorInvitationRequest
{
    public bool Accept { get; set; }

    public bool AcknowledgeScope { get; set; }

    [Required, MaxLength(64)]
    public string ScopeVersion { get; set; } = "";

    /// <summary>Mật khẩu tài khoản để ký nhận vai khi chấp nhận; từ chối thì bỏ qua.</summary>
    [MaxLength(128)]
    public string? Password { get; set; }
}
public sealed class ActivatePlanRequest
{
    [Range(1, int.MaxValue)] public int ConfigVersion { get; set; }
    [Required, MaxLength(64)] public string PolicyVersion { get; set; } = "";
    public bool AcknowledgePolicy { get; set; }
}
