using LegacyVault.Api.Security;
using LegacyVault.Application.Dtos;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LegacyVault.Api.Controllers;

/// <summary>Owner-scoped setup and activation. Bearer authentication; does not mutate login cookies.</summary>
[ApiController, Authorize, Route("api/v1/vaults/{vaultId:guid}")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
public sealed class PlanSetupController(IPlanSetupService service) : ControllerBase
{
    /// <summary>Read readiness, missing requirements, policy and invitation metadata; never activates.</summary>
    [HttpGet("plan-setup")]
    public Task<PlanSetupResponse> Get(Guid vaultId, CancellationToken ct) => service.GetAsync(vaultId, ct);

    /// <summary>Create invitation. Null invitationSentAt means delivery is not confirmed; token is never returned.</summary>
    [RequireVaultPin]
    [HttpPost("executors"), EnableRateLimiting("plan-setup")]
    public async Task<ActionResult<ExecutorInvitationResponse>> Invite(Guid vaultId, InviteExecutorRequest request, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await service.InviteAsync(vaultId, request, ct));

    /// <summary>Explicit resend with etag. Rotates the token; an old link no longer works.</summary>
    [HttpPost("executors/{executorId:guid}/resend"), EnableRateLimiting("plan-setup")]
    public Task<ExecutorInvitationResponse> Resend(Guid vaultId, Guid executorId, ResendExecutorRequest request, CancellationToken ct) =>
        service.ResendAsync(vaultId, executorId, request, ct);

    /// <summary>Recheck all requirements and atomically activate/open the first check-in period. Duplicate request does not reset deadlines.</summary>
    [HttpPost("activate"), EnableRateLimiting("plan-setup")]
    public Task<PlanSetupResponse> Activate(Guid vaultId, ActivatePlanRequest request, CancellationToken ct) =>
        service.ActivateAsync(vaultId, request, ct);
}

/// <summary>Invitation token is supplied in JSON body, not URL/access logs. No raw token in responses.</summary>
[ApiController, Route("api/v1/executor-invitations"), EnableRateLimiting("executor-invitations")]
public sealed class ExecutorInvitationsController(IPlanSetupService service) : ControllerBase
{
    /// <summary>Inspect minimal role/scope/expiry without exposing owner, vault contents or invitee identity.</summary>
    [HttpPost("inspect"), AllowAnonymous]
    public Task<ExecutorInvitationInfo> Inspect(InspectExecutorInvitationRequest request, CancellationToken ct) =>
        service.InspectAsync(request.Token, ct);

    /// <summary>Bind the verified signed-in account only after explicit accept/decline. Owner cannot accept their own invitation.</summary>
    [HttpPost("respond"), Authorize]
    public Task<ExecutorInvitationResponse> Respond(RespondExecutorInvitationRequest request, CancellationToken ct) =>
        service.RespondAsync(request, ct);
}
