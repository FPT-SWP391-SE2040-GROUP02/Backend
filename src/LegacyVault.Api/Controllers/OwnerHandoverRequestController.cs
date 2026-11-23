using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>
/// Chủ kho xem yêu cầu nhận tài sản đang mở của kho mình và hủy được tới khi luật sư ký biên bản.
/// </summary>
/// <remarks>Kho không phải của mình luôn là 404.</remarks>
[ApiController]
[Authorize]
[Route("api/v1/vaults/{vaultId:guid}/handover-request")]
[Produces("application/json")]
public sealed class OwnerHandoverRequestController : ControllerBase
{
    private readonly IHandoverRecordService recordService;

    public OwnerHandoverRequestController(IHandoverRecordService recordService)
    {
        this.recordService = recordService;
    }

    /// <summary>Yêu cầu nhận tài sản đang mở; 204 khi không có.</summary>
    [HttpGet]
    [ProducesResponseType<OwnerHandoverRequestResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OwnerHandoverRequestResponse>> Get(Guid vaultId, CancellationToken ct)
    {
        OwnerHandoverRequestResponse? request = await recordService.GetOwnerRequestAsync(vaultId, ct);
        if (request == null)
        {
            return NoContent();
        }
        return request;
    }

    /// <summary>Hủy yêu cầu: hủy biên bản và các lượt, kho mở khóa, kế hoạch về Đang hoạt động.</summary>
    [HttpPost("cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(Guid vaultId, CancellationToken ct)
    {
        await recordService.CancelByOwnerAsync(vaultId, ct);
        return NoContent();
    }
}
