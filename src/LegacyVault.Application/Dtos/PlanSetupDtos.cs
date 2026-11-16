using System.ComponentModel.DataAnnotations;
using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Dtos;

/// <summary>Owner-only metadata; token hashes and raw tokens are never serialized.</summary>
public sealed record ExecutorInvitationResponse(Guid Id, string Status, string InviteeEmail,
    DateTimeOffset ExpiresAt, DateTimeOffset? AcceptedAt,
    DateTimeOffset? InvitationSentAt, string? AcceptedScopeVersion, string Etag, bool AccountExists = false,
    string? FullName = null);
public sealed record CheckInviteeEmailResponse(string Email, bool Exists);
/// <summary>Tài khoản có sẵn mà Chủ kho có thể chọn làm Người thực thi.</summary>
public sealed record ExecutorCandidateResponse(Guid UserId, string FullName, string Email);
public sealed record PlanSetupResponse(string PlanStatus, int ConfigVersion, bool CanActivate,
    string[] MissingRequirements, string PolicyVersion, string PolicyText, string ExecutorScopeVersion,
    string ExecutorScopeText, List<ExecutorInvitationResponse> Executors,
    List<PlanSetupContainerResponse> Containers, List<PlanSetupAssetResponse> AssetsOutsideContainer, string[] Warnings,
    string OwnerFullName = "", string OwnerEmail = "",
    DateTimeOffset? ActivatedAt = null, List<PlanSetupRecipientResponse>? Recipients = null);

/// <summary>Một gói của người nhận trên màn kích hoạt (chỉ tên, số tài sản và trạng thái, không có nội dung).</summary>
public sealed record PlanSetupRecipientContainerResponse(Guid Id, string Name, int AssetCount, string Status);

/// <summary>Người nhận theo từng người (di chúc điện tử). Chỉ cho biết đã có lời nhắn hay chưa, không trả nội dung lời nhắn.</summary>
public sealed record PlanSetupRecipientResponse(Guid BeneficiaryId, string FullName, bool HasMessage,
    List<PlanSetupRecipientContainerResponse> Containers);
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
}

/// <summary>Trả lời lời mời theo id (lấy từ GET /executor-invitations/mine), không cần token trong link email.</summary>
public sealed class RespondMyExecutorInvitationRequest
{
    public bool Accept { get; set; }

    public bool AcknowledgeScope { get; set; }

    [Required, MaxLength(64)]
    public string ScopeVersion { get; set; } = "";
}
public sealed class ActivatePlanRequest
{
    [Range(1, int.MaxValue)] public int ConfigVersion { get; set; }
    [Required, MaxLength(64)] public string PolicyVersion { get; set; } = "";
    public bool AcknowledgePolicy { get; set; }
}
