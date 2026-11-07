using LegacyVault.Api.Exceptions;
using LegacyVault.Api.Handover;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>
/// Giai đoạn nhận tài sản (phía người nhận). Chi tiết ở docs/HANDOVER-RECEIVE-API.md.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/handover")]
// Cố ý KHÔNG đặt [Produces("application/json")]: attribute đó ghi đè content-type problem+json của lỗi validate 400.
public sealed class HandoverController : ControllerBase
{
    private readonly IHandoverService handoverService;
    private readonly IHandoverDownloadService downloadService;
    private readonly IDownloadCodeService downloadCodes;
    private readonly ILogger<HandoverController> logger;

    public HandoverController(
        IHandoverService handoverService,
        IHandoverDownloadService downloadService,
        IDownloadCodeService downloadCodes,
        ILogger<HandoverController> logger)
    {
        this.downloadCodes = downloadCodes;
        this.handoverService = handoverService;
        this.downloadService = downloadService;
        this.logger = logger;
    }

    /// <summary>Công khai: kiểm link mời còn hợp lệ không. Chỉ trả hai cờ, không lộ dữ liệu nào khác.</summary>
    [AllowAnonymous]
    [HttpGet("invitations/{token}")]
    [ProducesResponseType<InvitationStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<InvitationStatusResponse> GetInvitation(string token, CancellationToken ct)
    {
        return handoverService.GetInvitationAsync(token, ct);
    }

    /// <summary>Gắn tài khoản đang đăng nhập vào lượt. Gọi lặp bởi cùng người là an toàn.</summary>
    [HttpPost("invitations/{token}/claim")]
    [ProducesResponseType<HandoverTurnResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<HandoverTurnResponse> Claim(string token, CancellationToken ct)
    {
        return handoverService.ClaimAsync(token, ct);
    }

    /// <summary>Các lượt tôi đã nhận, mới nhất trước.</summary>
    [HttpGet("turns/me")]
    [ProducesResponseType<List<HandoverTurnResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public Task<List<HandoverTurnResponse>> GetMyTurns(CancellationToken ct)
    {
        return handoverService.GetMyTurnsAsync(ct);
    }

    [HttpGet("turns/{turnId:guid}")]
    [ProducesResponseType<HandoverTurnResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<HandoverTurnResponse> GetTurn(Guid turnId, CancellationToken ct)
    {
        return handoverService.GetTurnAsync(turnId, ct);
    }

    /// <summary>Đồng ý nhận: mở quyền xem và tải 30 ngày.</summary>
    [HttpPost("turns/{turnId:guid}/accept")]
    [ProducesResponseType<HandoverTurnResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<HandoverTurnResponse> Accept(Guid turnId, CancellationToken ct)
    {
        return handoverService.AcceptAsync(turnId, ct);
    }

    [HttpPost("turns/{turnId:guid}/decline")]
    [ProducesResponseType<HandoverTurnResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<HandoverTurnResponse> Decline(Guid turnId, CancellationToken ct)
    {
        return handoverService.DeclineAsync(turnId, ct);
    }

    /// <summary>Cho biết Admin có bật mã tải không, để màn tải biết cần hỏi mã hay không.</summary>
    [HttpGet("download-code")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult DownloadCodeStatus()
    {
        return Ok(new { required = downloadCodes.Required });
    }

    /// <summary>Gửi mã tải 6 số dùng một lần (hạn 10 phút) qua email của người đang đăng nhập; chỉ cho người có quyền tải này.</summary>
    [HttpPost("grants/{grantId:guid}/download-code")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RequestDownloadCode(Guid grantId, CancellationToken ct)
    {
        // Kiểm quyền bằng chính việc xem danh sách tài sản của quyền tải này.
        await handoverService.ListAssetsAsync(grantId, ct);
        await downloadCodes.RequestAsync(grantId, ct);
        return NoContent();
    }

    /// <summary>Danh sách tài sản trong quyền xem của tôi.</summary>
    [HttpGet("grants/{grantId:guid}/assets")]
    [ProducesResponseType<List<HandoverItemResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<List<HandoverItemResponse>> ListAssets(Guid grantId, CancellationToken ct)
    {
        return handoverService.ListAssetsAsync(grantId, ct);
    }

    /// <summary>
    /// Tải nội dung một tài sản. Nội dung được kiểm toàn vẹn trước khi gửi byte đầu tiên;
    /// nếu hỏng trả 500 CONTENT_INTEGRITY_FAILED mà không kèm byte nội dung nào.
    /// </summary>
    [HttpGet("grants/{grantId:guid}/assets/{itemId:guid}/content")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DownloadContent(Guid grantId, Guid itemId, CancellationToken ct)
    {
        // Admin bật mã tải: kiểm mã trước khi chuẩn bị nội dung; mã đúng chỉ dùng được một lần.
        downloadCodes.Consume(grantId, Request.Headers["X-Download-Code"].ToString());
        PreparedDownload download;
        try
        {
            download = await downloadService.PrepareAsync(grantId, itemId, ct);
        }
        catch (ContentIntegrityException)
        {
            // Chỉ log id, không log nội dung hay tên tệp (OPS-04).
            logger.LogError("Nội dung tài sản không qua kiểm toàn vẹn: grant {GrantId}, mục {ItemId}", grantId, itemId);

            ProblemDetails problem = GlobalExceptionHandler.Problem(
                StatusCodes.Status500InternalServerError, ErrorCode.ContentIntegrityFailed,
                "Nội dung tài sản không vượt qua kiểm tra toàn vẹn nên không thể tải.");
            problem.Extensions["traceId"] = HttpContext.TraceIdentifier;

            ObjectResult result = new ObjectResult(problem);
            result.StatusCode = StatusCodes.Status500InternalServerError;
            result.ContentTypes.Add("application/problem+json");
            return result;
        }

        return new HandoverFileResult(download);
    }
}
