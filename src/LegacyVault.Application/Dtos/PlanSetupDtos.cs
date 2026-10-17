using System.ComponentModel.DataAnnotations;
using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Dtos;

/// <summary>Owner-only metadata; token hashes and raw tokens are never serialized.</summary>
public sealed record ExecutorInvitationResponse(Guid Id, string Role, string Status, string InviteeEmail,
    bool IsBeneficiary, DateTimeOffset ExpiresAt, DateTimeOffset? AcceptedAt,
    DateTimeOffset? InvitationSentAt, string? AcceptedScopeVersion, string Etag);
public sealed record PlanSetupResponse(string PlanStatus, int ConfigVersion, bool CanActivate,
    string[] MissingRequirements, string PolicyVersion, string PolicyText, string ExecutorScopeVersion,
    string ExecutorScopeText, List<ExecutorInvitationResponse> Executors,
    List<PlanSetupContainerResponse> Containers, List<PlanSetupAssetResponse> AssetsOutsideContainer, string[] Warnings);
public sealed record ExecutorInvitationInfo(string Role, DateTimeOffset ExpiresAt, string Status,
    string ScopeVersion, string ScopeText);

public sealed class InviteExecutorRequest
{
    [Required, EmailAddress, MaxLength(254)] public string Email { get; set; } = "";
    [Required, RegularExpression("^(PRIMARY|BACKUP)$")] public string Role { get; set; } = "PRIMARY";
    public Guid? BeneficiaryId { get; set; }
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
public sealed class ActivatePlanRequest
{
    [Range(1, int.MaxValue)] public int ConfigVersion { get; set; }
    [Required, MaxLength(64)] public string PolicyVersion { get; set; } = "";
    public bool AcknowledgePolicy { get; set; }
}
