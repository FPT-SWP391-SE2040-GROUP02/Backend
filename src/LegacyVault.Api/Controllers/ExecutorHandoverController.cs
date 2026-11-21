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
/// Người gọi phải là Người thực thi đang hiệu lực của kho và không phải Chủ kho; không đủ điều kiện thì 404.
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

    public ExecutorHandoverController(
        IHandoverRecordService recordService,
        IHandoverProgressService handoverProgressService,
        IHandoverKeyMailService handoverKeyMailService)
    {
        this.recordService = recordService;
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
    /// Mở bàn giao cho kho: tạo biên bản và phân công một luật sư. Không cần nộp giấy tờ nào.
    /// Gọi lặp thì trả biên bản đã có.
    /// </summary>
    [HttpPost("vaults/{vaultId:guid}/handover-record")]
    [EnableRateLimiting("plan-setup")]
    [ProducesResponseType<HandoverRecordResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<HandoverRecordResponse> OpenRecord(Guid vaultId, CancellationToken ct)
    {
        return recordService.OpenAsync(vaultId, ct);
    }

    /// <summary>Người thực thi ký biên bản (bước 3). Ký là bấm xác nhận, lưu người ký và thời điểm.</summary>
    [HttpPost("vaults/{vaultId:guid}/handover-record/sign")]
    [ProducesResponseType<HandoverRecordResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<HandoverRecordResponse> SignRecord(Guid vaultId, CancellationToken ct)
    {
        return recordService.SignAsExecutorAsync(vaultId, ct);
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
    /// Người thực thi gửi mã nhận (khóa mở gói) cho người nhận của lượt qua email đã lưu. Chỉ khi biên bản đã đủ chữ ký.
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
}
