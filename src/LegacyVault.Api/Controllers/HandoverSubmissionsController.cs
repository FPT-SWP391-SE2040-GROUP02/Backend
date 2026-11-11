using System.ComponentModel.DataAnnotations;
using LegacyVault.Api.DeathCases;
using LegacyVault.Api.Exceptions;
using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>Biểu mẫu nộp hồ sơ người nhận: thông tin khai kèm hai ảnh căn cước.</summary>
public sealed class SubmitHandoverSubmissionForm : SubmitHandoverSubmissionRequest
{
    [Required(ErrorMessage = "Vui lòng chọn ảnh căn cước mặt trước.")]
    public IFormFile? FrontImage { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn ảnh căn cước mặt sau.")]
    public IFormFile? BackImage { get; set; }
}

/// <summary>Người nhận nộp hồ sơ (Chấp nhận hoặc Từ chối kèm ảnh căn cước) và xem trạng thái hồ sơ của lượt mình.</summary>
/// <remarks>Chỉ người đã nhận lượt này; lượt của người khác hoặc id lạ cùng một lỗi 404 (SEC-04).</remarks>
[ApiController]
[Authorize]
[Route("api/v1/handover/turns/{turnId:guid}/submission")]
public sealed class HandoverSubmissionsController : ControllerBase
{
    /// <summary>Hai ảnh tối đa 8 MiB mỗi ảnh cộng phần đầu multipart.</summary>
    private const long MaxRequestBytes = 17 * 1024 * 1024;

    private readonly IHandoverSubmissionService submissionService;

    public HandoverSubmissionsController(IHandoverSubmissionService submissionService)
    {
        this.submissionService = submissionService;
    }

    /// <summary>Nộp hồ sơ. Chỉ khi lượt ở bước xác minh danh tính; nộp lại thì bản cũ bị thay thế.</summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [ProducesResponseType<HandoverSubmissionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<HandoverSubmissionResponse> Submit(
        Guid turnId, [FromForm] SubmitHandoverSubmissionForm form, CancellationToken ct)
    {
        byte[] front = await ReadAsync(form.FrontImage!, ct);
        byte[] back = await ReadAsync(form.BackImage!, ct);
        return await submissionService.SubmitAsync(turnId, form, front, back, ct);
    }

    /// <summary>Hồ sơ mới nhất của lượt này (trạng thái, lý do cần bổ sung); 204 khi chưa nộp.</summary>
    [HttpGet]
    [ProducesResponseType<HandoverSubmissionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HandoverSubmissionResponse>> GetLatest(Guid turnId, CancellationToken ct)
    {
        HandoverSubmissionResponse? latest = await submissionService.GetLatestAsync(turnId, ct);
        if (latest == null)
        {
            return NoContent();
        }
        return latest;
    }

    private static async Task<byte[]> ReadAsync(IFormFile file, CancellationToken ct)
    {
        if (file.Length > EkycRules.MaxImageBytes)
        {
            throw new AppException(AppErrorType.PayloadTooLarge, ErrorCode.FileTooLarge,
                "Ảnh căn cước quá lớn. Vui lòng chọn ảnh nhỏ hơn 8 MiB.");
        }
        await using (Stream content = file.OpenReadStream())
        using (MemoryStream buffer = new MemoryStream())
        {
            await content.CopyToAsync(buffer, ct);
            return buffer.ToArray();
        }
    }
}

/// <summary>Người xét duyệt duyệt hồ sơ người nhận của các kho mà mình đã duyệt hồ sơ chứng tử.</summary>
/// <remarks>Vai trò Verifier được service kiểm từ DB; hồ sơ không thuộc kho của mình luôn là 404.</remarks>
[ApiController]
[Authorize]
[Route("api/v1/verifier/handover-submissions")]
public sealed class VerifierHandoverSubmissionsController : ControllerBase
{
    private readonly IHandoverSubmissionReviewService reviewService;
    private readonly ILogger<VerifierHandoverSubmissionsController> logger;

    public VerifierHandoverSubmissionsController(
        IHandoverSubmissionReviewService reviewService,
        ILogger<VerifierHandoverSubmissionsController> logger)
    {
        this.reviewService = reviewService;
        this.logger = logger;
    }

    /// <summary>Hồ sơ người nhận đang chờ tôi duyệt, cũ nhất trước.</summary>
    [HttpGet]
    [ProducesResponseType<List<VerifierHandoverSubmissionSummaryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<List<VerifierHandoverSubmissionSummaryResponse>> List(CancellationToken ct)
    {
        return reviewService.ListPendingAsync(ct);
    }

    [HttpGet("{submissionId:guid}")]
    [ProducesResponseType<VerifierHandoverSubmissionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<VerifierHandoverSubmissionResponse> Get(Guid submissionId, CancellationToken ct)
    {
        return reviewService.GetAsync(submissionId, ct);
    }

    /// <summary>Ảnh căn cước một mặt: side = front hoặc back. Trả nội dung ảnh, không phải JSON.</summary>
    [HttpGet("{submissionId:guid}/images/{side}")]
    [Produces("image/jpeg", "image/png", "image/webp", "application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetImage(Guid submissionId, string side, CancellationToken ct)
    {
        PreparedDownload download;
        try
        {
            download = await reviewService.PrepareImageAsync(submissionId, side, ct);
        }
        catch (ContentIntegrityException)
        {
            // Chỉ log id, không log nội dung hay khóa lưu trữ.
            logger.LogError("Ảnh căn cước của người nhận không qua kiểm toàn vẹn: hồ sơ {SubmissionId}", submissionId);
            ProblemDetails problem = GlobalExceptionHandler.Problem(
                StatusCodes.Status500InternalServerError, ErrorCode.ContentIntegrityFailed,
                "Ảnh không vượt qua kiểm tra toàn vẹn nên không thể xem.");
            problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
            ObjectResult result = new ObjectResult(problem);
            result.StatusCode = StatusCodes.Status500InternalServerError;
            result.ContentTypes.Add("application/problem+json");
            return result;
        }

        logger.LogInformation("Người xét duyệt đã xem ảnh căn cước của người nhận: hồ sơ {SubmissionId}", submissionId);
        return new IdentityImageResult(download);
    }

    /// <summary>Duyệt hồ sơ: chỉ khi cả ba trường khớp. Lượt sang bước quyết định.</summary>
    [HttpPost("{submissionId:guid}/approve")]
    [ProducesResponseType<VerifierHandoverSubmissionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<VerifierHandoverSubmissionResponse> Approve(Guid submissionId, CancellationToken ct)
    {
        return reviewService.ApproveAsync(submissionId, ct);
    }

    /// <summary>
    /// Luồng Biên Bản: kiểm tra thủ công (tick đủ hai mục và xác nhận trách nhiệm) rồi gửi.
    /// Hoàn tất biên bản: Chấp nhận thì mở quyền tải tài sản, Từ chối thì ghi nhận từ chối.
    /// </summary>
    [HttpPost("{submissionId:guid}/complete")]
    [Consumes("application/json")]
    [ProducesResponseType<VerifierHandoverSubmissionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<VerifierHandoverSubmissionResponse> Complete(
        Guid submissionId, ApproveHandoverDocumentRequest request, CancellationToken ct)
    {
        return reviewService.CompleteAsync(submissionId, request, ct);
    }

    /// <summary>Yêu cầu người nhận bổ sung, kèm lý do.</summary>
    [HttpPost("{submissionId:guid}/request-supplement")]
    [Consumes("application/json")]
    [ProducesResponseType<VerifierHandoverSubmissionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<VerifierHandoverSubmissionResponse> RequestSupplement(
        Guid submissionId, RequestHandoverSupplementRequest request, CancellationToken ct)
    {
        return reviewService.RequestSupplementAsync(submissionId, request, ct);
    }
}
