using LegacyVault.Application.Dtos;
using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LegacyVault.Api.Controllers;

/// <summary>
/// Người thực thi: mở bàn giao, xem và ký biên bản bàn giao, theo dõi tiến độ, gửi mã nhận cho người nhận.
/// </summary>
/// <remarks>
/// Người gọi phải là Người thực thi đang hiệu lực của kho và không phải Chủ tài sản; không đủ điều kiện thì 404.
/// Người thực thi chỉ giữ và giao tài sản theo biên bản, không sửa được biên bản.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/v1/executor")]
[Produces("application/json")]
public sealed class ExecutorHandoverController : ControllerBase
{
    private readonly IHandoverRecordService recordService;
    private readonly IHandoverProgressService handoverProgressService;
    private readonly IHandoverKeyMailService handoverKeyMailService;
    private readonly IHandoverReceiptService receiptService;
    private readonly IExecutorWillService willService;

    public ExecutorHandoverController(
        IHandoverRecordService recordService,
        IHandoverProgressService handoverProgressService,
        IHandoverKeyMailService handoverKeyMailService,
        IHandoverReceiptService receiptService,
        IExecutorWillService willService)
    {
        this.willService = willService;
        this.recordService = recordService;
        this.receiptService = receiptService;
        this.handoverKeyMailService = handoverKeyMailService;
        this.handoverProgressService = handoverProgressService;
    }

    /// <summary>Các kho tôi đang là Người thực thi, kèm tình trạng biên bản bàn giao.</summary>
    [HttpGet("vaults")]
    [ProducesResponseType<List<ExecutorVaultResponse>>(StatusCodes.Status200OK)]
    public Task<List<ExecutorVaultResponse>> GetMyVaults(CancellationToken ct)
    {
        return recordService.ListMyVaultsAsync(ct);
    }

    /// <summary>
    /// Đồng ý (ký bằng mật khẩu) hoặc không đồng ý phiên bản biên bản phân chia Chủ tài sản vừa ký lại.
    /// </summary>
    [HttpPost("vaults/{vaultId:guid}/will/respond")]
    [EnableRateLimiting("executor-invitations")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RespondWill(Guid vaultId, RespondWillVersionRequest request, CancellationToken ct)
    {
        await willService.RespondAsync(vaultId, request, ct);
        return NoContent();
    }

    /// <summary>
    /// Biên bản phân chia đầy đủ (bản Chủ tài sản ký gần nhất) cho Người thực thi: danh tính, tên tài sản và người nhận.
    /// Không có nội dung tài sản. Mỗi lần xem ghi nhật ký; response không được cache.
    /// </summary>
    [HttpGet("vaults/{vaultId:guid}/will")]
    [EnableRateLimiting("executor-invitations")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<ExecutorWillResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ExecutorWillResponse> GetWill(Guid vaultId, CancellationToken ct)
    {
        return willService.GetForVaultAsync(vaultId, ct);
    }

    /// <summary>
    /// Biên bản phân chia đầy đủ cho người đang được mời đọc trước khi ký nhận vai. Chỉ tài khoản có email đã xác minh
    /// trùng email được mời; response không được cache.
    /// </summary>
    [HttpGet("invitations/{invitationId:guid}/will")]
    [EnableRateLimiting("executor-invitations")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [ProducesResponseType<ExecutorWillResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ExecutorWillResponse> GetInvitationWill(Guid invitationId, CancellationToken ct)
    {
        return willService.GetForInvitationAsync(invitationId, ct);
    }

    /// <summary>Biên bản bàn giao của kho. Chưa mở bàn giao thì 204 (không có nội dung).</summary>
    [HttpGet("vaults/{vaultId:guid}/handover-record")]
    [ProducesResponseType<HandoverRecordResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetRecord(Guid vaultId, CancellationToken ct)
    {
        HandoverRecordResponse? record = await recordService.GetForExecutorAsync(vaultId, ct);
        if (record == null)
        {
            return NoContent();
        }
        return Ok(record);
    }

    /// <summary>
    /// Người thực thi ký phần của một người nhận đã được xác nhận danh tính, sau khi luật sư đã ký phần đó (chưa thì 409
    /// HANDOVER_NOT_ALLOWED reason VERIFIER_NOT_SIGNED). Ký là bấm xác nhận, lưu người ký và thời điểm; ký xong thì người
    /// nhận được báo ký phần của mình.
    /// </summary>
    [HttpPost("vaults/{vaultId:guid}/handover-record/recipients/{beneficiaryId:guid}/sign")]
    [ProducesResponseType<HandoverRecordResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<HandoverRecordResponse> SignRecord(Guid vaultId, Guid beneficiaryId, CancellationToken ct)
    {
        return recordService.SignAsExecutorAsync(vaultId, beneficiaryId, ct);
    }

    /// <summary>
    /// Trạng thái từng lượt bàn giao và từng tài sản đã tải hay chưa. Không có nội dung tài sản.
    /// Biên bản chưa chốt lần nào thì handoverStarted = false.
    /// </summary>
    [HttpGet("handover-records/{recordId:guid}/handover-progress")]
    [ProducesResponseType<HandoverProgressResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<HandoverProgressResponse> GetHandoverProgress(Guid recordId, CancellationToken ct)
    {
        return handoverProgressService.GetProgressAsync(recordId, ct);
    }

    /// <summary>
    /// Người thực thi gửi mã nhận cho người nhận của lượt qua email đã lưu. Chỉ khi biên bản đã đủ chữ ký.
    /// Body là khóa mình đang giữ để Backend kiểm; khóa sai trả 403 HANDOVER_KEY_INVALID. Khóa không được lưu.
    /// </summary>
    [HttpPost("handover-records/{recordId:guid}/handover-turns/{turnId:guid}/send-key")]
    [EnableRateLimiting("plan-setup")]
    [ProducesResponseType<SendHandoverKeyResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public Task<SendHandoverKeyResponse> SendKey(Guid recordId, Guid turnId, [FromBody] SendHandoverKeyRequest request, CancellationToken ct)
    {
        return handoverKeyMailService.SendAsync(recordId, turnId, request.Key, ct);
    }

    /// <summary>Người thực thi ký bên giao của phiếu giao nhận, sau khi người nhận xác nhận đã nhận đủ.</summary>
    [HttpPost("handover-records/{recordId:guid}/handover-turns/{turnId:guid}/sign-receipt")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SignReceipt(Guid recordId, Guid turnId, CancellationToken ct)
    {
        await receiptService.SignAsExecutorAsync(recordId, turnId, ct);
        return NoContent();
    }
}
