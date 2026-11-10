using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Api.DeathCases;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

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
    private readonly IExecutorIdentityConfirmationService identityConfirmationService;
    private readonly IHandoverKeyMailService handoverKeyMailService;
    private readonly IExecutorDeclinePaperService declinePaperService;

    public ExecutorHandoverController(
        IHandoverProgressService handoverProgressService,
        IExecutorIdentityConfirmationService identityConfirmationService,
        IHandoverKeyMailService handoverKeyMailService,
        IExecutorDeclinePaperService declinePaperService)
    {
        this.declinePaperService = declinePaperService;
        this.handoverKeyMailService = handoverKeyMailService;
        this.handoverProgressService = handoverProgressService;
        this.identityConfirmationService = identityConfirmationService;
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
    /// <summary>Ảnh văn bản từ chối hoặc biên bản đồng ý nhận của một lượt đã có quyết định. Trả ảnh PNG, không phải JSON.</summary>
    [HttpGet("death-cases/{caseId:guid}/handover-turns/{turnId:guid}/decline-paper")]
    [HttpGet("death-cases/{caseId:guid}/handover-turns/{turnId:guid}/paper")]
    [Produces("image/png", "application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDeclinePaper(Guid caseId, Guid turnId, CancellationToken ct)
    {
        PreparedDownload download = await declinePaperService.PrepareAsync(caseId, turnId, ct);
        return new IdentityImageResult(download);
    }

    /// <summary>
    /// Người thực hiện gửi khóa mở gói cho người nhận của lượt qua email đã lưu. Body là khóa mình đang giữ để Backend kiểm;
    /// khóa sai trả 403 HANDOVER_KEY_INVALID. Khóa không được lưu và không có trong phản hồi.
    /// </summary>
    [HttpPost("death-cases/{caseId:guid}/handover-turns/{turnId:guid}/send-key")]
    [EnableRateLimiting("plan-setup")]
    [ProducesResponseType<SendHandoverKeyResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public Task<SendHandoverKeyResponse> SendKey(Guid caseId, Guid turnId, [FromBody] SendHandoverKeyRequest request, CancellationToken ct)
    {
        return handoverKeyMailService.SendAsync(caseId, turnId, request.Key, ct);
    }

    /// <summary>
    /// Executor xác nhận đúng người bằng tay khi người nhận không qua được eKYC (BR-122). Lượt sang READY_TO_ACCEPT và
    /// người nhận có 7 ngày để đồng ý hoặc từ chối. Không dùng được cho lượt do chính tài khoản này nhận.
    /// </summary>
    [HttpPost("death-cases/{caseId:guid}/handover-turns/{turnId:guid}/confirm-identity")]
    [ProducesResponseType<ConfirmHandoverIdentityResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ConfirmHandoverIdentityResponse> ConfirmIdentity(Guid caseId, Guid turnId, CancellationToken ct)
    {
        return identityConfirmationService.ConfirmAsync(caseId, turnId, ct);
    }
}
