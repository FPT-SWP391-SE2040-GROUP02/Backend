using LegacyVault.Api.Exceptions;
using LegacyVault.Api.Handover;
using LegacyVault.Application.Dtos.Requests;
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
    private readonly IInvitationMailCheckService mailCheckService;
    private readonly ILogger<HandoverController> logger;

    public HandoverController(
        IHandoverService handoverService,
        IHandoverDownloadService downloadService,
        IInvitationMailCheckService mailCheckService,
        ILogger<HandoverController> logger)
    {
        this.handoverService = handoverService;
        this.downloadService = downloadService;
        this.mailCheckService = mailCheckService;
        this.logger = logger;
    }

    /// <summary>
    /// Công khai: kiểm link mời còn hợp lệ không. Trả thêm email được mời đã che bớt và cờ "đã có tài khoản"
    /// để FE chọn nút Đăng nhập hay Tạo tài khoản.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("invitations/{token}")]
    [ProducesResponseType<InvitationStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<InvitationStatusResponse> GetInvitation(string token, CancellationToken ct)
    {
        return handoverService.GetInvitationAsync(token, ct);
    }

    /// <summary>
    /// Công khai: người nhận nhập mã hồ sơ và 4 số cuối căn cước ghi trong thư để biết thư có thật không.
    /// Có giới hạn số lần thử theo lượt mời; hết lượt thì 423.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("invitations/{token}/check-mail")]
    [Consumes("application/json")]
    [ProducesResponseType<InvitationMailCheckResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status423Locked)]
    public Task<InvitationMailCheckResponse> CheckMail(
        string token, InvitationMailCheckRequest request, CancellationToken ct)
    {
        return mailCheckService.CheckAsync(token, request.CaseCode, request.CitizenIdLast4, ct);
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

    /// <summary>
    /// Lời mời nhận gói đang chờ tôi (chưa ai nhận, đúng email đã xác minh của tài khoản). Rỗng khi chưa xác minh email.
    /// </summary>
    [HttpGet("invitations/mine")]
    [ProducesResponseType<List<PendingInvitationResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public Task<List<PendingInvitationResponse>> GetMyInvitations(CancellationToken ct)
    {
        return handoverService.GetMyInvitationsAsync(ct);
    }

    /// <summary>Nhận lượt trong app (bấm Đồng ý/Từ chối nhận ở thẻ thư mời). Gọi lặp bởi cùng người là an toàn.</summary>
    [HttpPost("turns/{turnId:guid}/claim")]
    [ProducesResponseType<HandoverTurnResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<HandoverTurnResponse> ClaimByTurn(Guid turnId, CancellationToken ct)
    {
        return handoverService.ClaimByTurnAsync(turnId, ct);
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

    /// <summary>Người nhận xác nhận đã nhận đủ tài sản.</summary>
    [HttpPost("turns/{turnId:guid}/received-all")]
    [ProducesResponseType<HandoverTurnResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<HandoverTurnResponse> ConfirmReceivedAll(Guid turnId, CancellationToken ct)
    {
        return handoverService.ConfirmReceivedAllAsync(turnId, ct);
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

    /// <summary>Nhập khóa mở gói do Người thực hiện đưa. Sai thì 403 HANDOVER_KEY_INVALID, quá giới hạn thì 423.</summary>
    [HttpPost("turns/{turnId:guid}/verify-key")]
    [ProducesResponseType<HandoverTurnResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status423Locked)]
    public Task<HandoverTurnResponse> VerifyKey(Guid turnId, VerifyHandoverKeyRequest request, CancellationToken ct)
    {
        return handoverService.VerifyKeyAsync(turnId, request.Key, ct);
    }

    [HttpPost("turns/{turnId:guid}/decline")]
    [ProducesResponseType<HandoverTurnResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<HandoverTurnResponse> Decline(Guid turnId, CancellationToken ct)
    {
        return handoverService.DeclineAsync(turnId, ct);
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
