using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>Executor theo dõi tiến độ bàn giao của hồ sơ chứng tử (docs/api-contract.md, mục tiến độ bàn giao).</summary>
/// <remarks>
/// Controller riêng cùng tiền tố với ExecutorDeathCasesController để không sửa controller đó.
/// Người gọi phải là Executor đang hiệu lực của kho và không phải Owner; không đủ điều kiện thì 404.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/v1/executor")]
[Produces("application/json")]
public sealed class ExecutorHandoverController : ControllerBase
{
    private readonly IHandoverProgressService handoverProgressService;
    private readonly IHandoverInvitationService handoverInvitationService;

    public ExecutorHandoverController(
        IHandoverProgressService handoverProgressService,
        IHandoverInvitationService handoverInvitationService)
    {
        this.handoverProgressService = handoverProgressService;
        this.handoverInvitationService = handoverInvitationService;
    }

    /// <summary>
    /// Trạng thái từng lượt bàn giao và từng tài sản đã tải hay chưa. Không có nội dung tài sản
    /// và thông tin liên hệ người nhận. Hồ sơ chưa duyệt trả handoverStarted = false.
    /// </summary>
    [HttpGet("death-cases/{caseId:guid}/handover-progress")]
    [ProducesResponseType<HandoverProgressResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<HandoverProgressResponse> GetHandoverProgress(Guid caseId, CancellationToken ct)
    {
        return handoverProgressService.GetProgressAsync(caseId, ct);
    }

    /// <summary>
    /// Gửi lại lời mời nhận tài sản cho một lượt chưa ai nhận (INVITE-02, INVITE-04). Link cũ hết dùng, hạn phản hồi không đặt lại
    /// (trừ khi lời mời chưa từng được giao). Giãn cách tối thiểu 15 phút giữa hai lần gửi.
    /// </summary>
    [HttpPost("death-cases/{caseId:guid}/handover-turns/{turnId:guid}/resend-invitation")]
    [ProducesResponseType<ResendHandoverInvitationResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public Task<ResendHandoverInvitationResponse> ResendInvitation(Guid caseId, Guid turnId, CancellationToken ct)
    {
        return handoverInvitationService.ResendAsync(caseId, turnId, ct);
    }
}
