using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>
/// Bước xác minh danh tính của người nhận trong giai đoạn nhận tài sản (SRS §7.2). Chi tiết ở docs/HANDOVER-RECEIVE-API.md.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/handover/turns/{turnId:guid}/identity")]
// Cố ý KHÔNG đặt [Produces("application/json")]: giống HandoverController, để lỗi validate 400 giữ problem+json.
public sealed class HandoverIdentityController : ControllerBase
{
    private readonly IHandoverIdentityService identityService;

    public HandoverIdentityController(IHandoverIdentityService identityService)
    {
        this.identityService = identityService;
    }

    /// <summary>Chốt xác minh danh tính: eKYC đạt và khớp người được chỉ định thì lượt sang READY_TO_ACCEPT.</summary>
    [HttpPost("complete")]
    [ProducesResponseType<HandoverTurnResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<HandoverTurnResponse> Complete(Guid turnId, CancellationToken ct)
    {
        return identityService.CompleteIdentityAsync(turnId, ct);
    }
}
