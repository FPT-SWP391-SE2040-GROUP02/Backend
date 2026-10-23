using LegacyVault.Api.DeathCases;
using LegacyVault.Api.Exceptions;
using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>Verifier xét hồ sơ chứng tử (docs/api-contract.md mục 6.12).</summary>
/// <remarks>
/// Controller chỉ [Authorize]; vai trò Verifier được service kiểm từ DB (ROLE-04), không tin claim trong token.
/// Hồ sơ không giao cho tôi (phân công đang hiệu lực) luôn là 404 (SEC-04, SEC-05).
/// </remarks>
[ApiController]
[Authorize]
[Route("api/v1/verifier")]
// Cố ý KHÔNG đặt [Produces("application/json")]: attribute đó ghi đè content-type problem+json của lỗi validate 400.
public sealed class VerifierDeathCasesController : ControllerBase
{
    private readonly IDeathCaseReviewService reviewService;
    private readonly IAiNotaryAssistantService aiAssistantService;
    private readonly ILogger<VerifierDeathCasesController> logger;

    public VerifierDeathCasesController(
        IDeathCaseReviewService reviewService,
        IAiNotaryAssistantService aiAssistantService,
        ILogger<VerifierDeathCasesController> logger)
    {
        this.reviewService = reviewService;
        this.aiAssistantService = aiAssistantService;
        this.logger = logger;
    }

    /// <summary>Hồ sơ đang giao cho tôi. sortBy: dueAt (mặc định) | assignedAt; searchTerm bị bỏ qua.</summary>
    [HttpGet("death-cases")]
    [ProducesResponseType<PaginatedList<VerifierDeathCaseSummaryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<PaginatedList<VerifierDeathCaseSummaryResponse>> List(
        [FromQuery] PaginationRequest pagination, CancellationToken ct)
    {
        return reviewService.ListAsync(pagination, ct);
    }

    /// <summary>Chi tiết hồ sơ (có CCCD đầy đủ của bản đang xét), không cho lưu bộ nhớ đệm.</summary>
    [HttpGet("death-cases/{caseId:guid}")]
    [ProducesResponseType<VerifierDeathCaseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<VerifierDeathCaseResponse> Get(Guid caseId, CancellationToken ct)
    {
        // DATA-02: chi tiết có dữ liệu nhạy cảm nên cấm lưu đệm.
        Response.Headers["Cache-Control"] = "no-store";
        return await reviewService.GetAsync(caseId, ct);
    }

    /// <summary>
    /// Xem một trang giấy của bản đã gửi. Nội dung được kiểm toàn vẹn trước khi gửi byte đầu tiên;
    /// nếu hỏng trả 500 CONTENT_INTEGRITY_FAILED mà không kèm byte nội dung nào (DATA-06).
    /// </summary>
    [HttpGet("death-cases/{caseId:guid}/files/{fileId:guid}/content")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetFileContent(Guid caseId, Guid fileId, CancellationToken ct)
    {
        PreparedDeathCaseFile prepared;
        try
        {
            prepared = await reviewService.PrepareFileAsync(caseId, fileId, ct);
        }
        catch (ContentIntegrityException)
        {
            // Chỉ log id, không log nội dung, tên tệp hay khóa lưu trữ (DATA-02, SEC-07).
            logger.LogError("Trang giấy chứng tử không qua kiểm toàn vẹn: hồ sơ {CaseId}, tệp {FileId}", caseId, fileId);

            ProblemDetails problem = GlobalExceptionHandler.Problem(
                StatusCodes.Status500InternalServerError, ErrorCode.ContentIntegrityFailed,
                "Nội dung trang giấy không vượt qua kiểm tra toàn vẹn nên không thể xem.");
            problem.Extensions["traceId"] = HttpContext.TraceIdentifier;

            ObjectResult result = new ObjectResult(problem);
            result.StatusCode = StatusCodes.Status500InternalServerError;
            result.ContentTypes.Add("application/problem+json");
            return result;
        }

        // TODO: chuyển sang nhật ký kiểm toán khi có (SEC-07, DATA-07). Chỉ ghi id, không ghi nội dung.
        logger.LogInformation(
            "Verifier đã xem trang giấy chứng tử: hồ sơ {CaseId}, tệp {FileId}, người xem {VerifierUserId}",
            prepared.CaseId, prepared.FileId, prepared.ViewerUserId);
        return new DeathCaseFileResult(prepared);
    }

    /// <summary>
    /// Xem xét một cờ của bản đang xét; body có etag và note. Gọi lặp giữ ghi chú đầu.
    /// Thao tác làm đổi etag hồ sơ: dùng etag mới trong response cho lần duyệt.
    /// </summary>
    [HttpPost("death-cases/{caseId:guid}/flags/{flagId:guid}/review")]
    [Consumes("application/json")]
    [ProducesResponseType<VerifierDeathCaseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<VerifierDeathCaseResponse> ReviewFlag(
        Guid caseId, Guid flagId, ReviewDeathCaseFlagRequest request, CancellationToken ct)
    {
        Response.Headers["Cache-Control"] = "no-store";
        return await reviewService.ReviewFlagAsync(caseId, flagId, request, ct);
    }

    /// <summary>
    /// Yêu cầu Executor bổ sung hồ sơ; body có etag, reason và requestedParts (mã viết hoa như missingRequirements).
    /// </summary>
    [HttpPost("death-cases/{caseId:guid}/request-supplement")]
    [Consumes("application/json")]
    [ProducesResponseType<VerifierDeathCaseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<VerifierDeathCaseResponse> RequestSupplement(
        Guid caseId, RequestDeathCaseSupplementRequest request, CancellationToken ct)
    {
        Response.Headers["Cache-Control"] = "no-store";
        return await reviewService.RequestSupplementAsync(caseId, request, ct);
    }

    /// <summary>
    /// Từ chối hồ sơ (lý do bắt buộc, Executor sẽ thấy). Hồ sơ đóng lại, kế hoạch về trạng thái trước khi gửi
    /// (có thể trễ tối đa 60 giây so với lịch điểm danh), thông báo Owner đang chờ bị hủy.
    /// </summary>
    [HttpPost("death-cases/{caseId:guid}/reject")]
    [Consumes("application/json")]
    [ProducesResponseType<VerifierDeathCaseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<VerifierDeathCaseResponse> Reject(
        Guid caseId, RejectDeathCaseRequest request, CancellationToken ct)
    {
        Response.Headers["Cache-Control"] = "no-store";
        return await reviewService.RejectAsync(caseId, request, ct);
    }

    /// <summary>
    /// Duyệt hồ sơ và mở bàn giao F3 (tạo lượt, ghim snapshot, gửi lời mời cho người nhận). Chưa duyệt được thì 409
    /// DEATH_CASE_NOT_ALLOWED kèm reason và approveBlockers. Gọi lặp trên hồ sơ đã duyệt trả 200 (handover = null).
    /// </summary>
    [HttpPost("death-cases/{caseId:guid}/approve")]
    [Consumes("application/json")]
    [ProducesResponseType<VerifierDeathCaseResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<VerifierDeathCaseResponse> Approve(
        Guid caseId, ApproveDeathCaseRequest request, CancellationToken ct)
    {
        Response.Headers["Cache-Control"] = "no-store";
        return await reviewService.ApproveAsync(caseId, request, ct);
    }

    /// <summary>
    /// Trợ lý Gemini AI Co-Pilot hỗ trợ Công chứng viên thẩm định và rà soát rủi ro pháp lý của hồ sơ báo tử.
    /// </summary>
    [HttpPost("death-cases/{caseId:guid}/ai-assess")]
    [ProducesResponseType<AiDossierAnalysisResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AiDossierAnalysisResponse>> AssessWithAi(
        Guid caseId, CancellationToken ct)
    {
        Response.Headers["Cache-Control"] = "no-store";
        return Ok(await aiAssistantService.AssessDossierAsync(caseId, ct));
    }
}
