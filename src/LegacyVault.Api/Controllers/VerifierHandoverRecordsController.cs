using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using LegacyVault.Api.Exceptions;
using LegacyVault.Api.Handover;
using LegacyVault.Application.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>
/// Luật sư lập và kiểm biên bản bàn giao theo đúng thứ tự: xét giấy tờ và lập biên bản → danh tính người nhận →
/// luật sư ký.
/// Danh tính người nhận kiểm ở VerifierHandoverSubmissionsController.
/// </summary>
/// <remarks>Vai trò luật sư được service kiểm từ DB; biên bản không được giao cho mình luôn là 404.</remarks>
[ApiController]
[Authorize]
[Route("api/v1/verifier/handover-records")]
[Produces("application/json")]
public sealed class VerifierHandoverRecordsController : ControllerBase
{
    private readonly IHandoverRecordService recordService;

    public VerifierHandoverRecordsController(IHandoverRecordService recordService)
    {
        this.recordService = recordService;
    }

    /// <summary>Các biên bản được giao cho tôi, mới nhất trước.</summary>
    [HttpGet]
    [ProducesResponseType<List<HandoverRecordSummaryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<List<HandoverRecordSummaryResponse>> List(CancellationToken ct)
    {
        return recordService.ListForVerifierAsync(ct);
    }

    /// <summary>Một biên bản kèm tài sản, người nhận và biên bản phân chia của Chủ kho để lập.</summary>
    [HttpGet("{recordId:guid}")]
    [ProducesResponseType<HandoverRecordResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<HandoverRecordResponse> Get(Guid recordId, CancellationToken ct)
    {
        return recordService.GetForVerifierAsync(recordId, ct);
    }

    /// <summary>Lấy biên bản phân chia của Chủ kho làm bản đầu (thay toàn bộ dòng đang có).</summary>
    [HttpPost("{recordId:guid}/use-owner-split")]
    [ProducesResponseType<HandoverRecordResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<HandoverRecordResponse> UseOwnerSplit(Guid recordId, CancellationToken ct)
    {
        return recordService.UseOwnerSplitAsync(recordId, ct);
    }

    /// <summary>Thay toàn bộ danh sách "ai nhận tài sản nào". Chỉ khi biên bản đang lập.</summary>
    [HttpPut("{recordId:guid}/lines")]
    [Consumes("application/json")]
    [ProducesResponseType<HandoverRecordResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<HandoverRecordResponse> SetLines(Guid recordId, SetHandoverRecordLinesRequest request, CancellationToken ct)
    {
        return recordService.SetLinesAsync(recordId, request, ct);
    }

    /// <summary>Chốt biên bản (xong bước 1): tạo lượt cho từng người nhận và gửi thư mời.</summary>
    [HttpPost("{recordId:guid}/finalize")]
    [ProducesResponseType<HandoverRecordResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<HandoverRecordResponse> Finalize(Guid recordId, CancellationToken ct)
    {
        return recordService.FinalizeAsync(recordId, ct);
    }

    /// <summary>Mở lại biên bản đã chốt để sửa. Mọi chữ ký cũ mất hiệu lực, phải ký lại từ đầu.</summary>
    [HttpPost("{recordId:guid}/reopen")]
    [ProducesResponseType<HandoverRecordResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<HandoverRecordResponse> Reopen(Guid recordId, CancellationToken ct)
    {
        return recordService.ReopenAsync(recordId, ct);
    }

    /// <summary>Luật sư ký xác minh biên bản (bước 3).</summary>
    [HttpPost("{recordId:guid}/sign")]
    [ProducesResponseType<HandoverRecordResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<HandoverRecordResponse> Sign(Guid recordId, CancellationToken ct)
    {
        return recordService.SignAsVerifierAsync(recordId, ct);
    }

    /// <summary>Xét giấy tờ một yêu cầu nhận: ACCEPT, REQUEST_SUPPLEMENT hoặc REJECT (hai cái sau bắt buộc lý do).</summary>
    [HttpPost("{recordId:guid}/claims/{claimId:guid}/review")]
    [ProducesResponseType<HandoverRecordResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<HandoverRecordResponse> ReviewClaim(
        Guid recordId, Guid claimId, ReviewHandoverClaimRequest request, CancellationToken ct)
    {
        return recordService.ReviewClaimAsync(recordId, claimId, request, ct);
    }

    /// <summary>Thêm người nhận mới vào kho (họ tên, CCCD, ngày sinh, email); chỉ khi biên bản đang lập.</summary>
    [HttpPost("{recordId:guid}/recipients")]
    [ProducesResponseType<HandoverRecordResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<HandoverRecordResponse> AddRecipient(
        Guid recordId, AddHandoverRecipientRequest request, CancellationToken ct)
    {
        return recordService.AddRecipientAsync(recordId, request, ct);
    }

    /// <summary>Đính kèm tệp căn cứ (PDF hoặc ảnh, tối đa 10 tệp, mỗi tệp 10 MiB); chỉ khi biên bản đang lập.</summary>
    [HttpPost("{recordId:guid}/evidence")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(UploadedFileReader.MaxRequestBytes)]
    [ProducesResponseType<HandoverRecordResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<HandoverRecordResponse> AddEvidence(
        Guid recordId, [FromForm] List<IFormFile> files, CancellationToken ct)
    {
        List<UploadedFile> uploaded = await UploadedFileReader.ReadAllAsync(files, ct);
        return await recordService.AddEvidenceAsync(recordId, uploaded, ct);
    }

    /// <summary>Gỡ một tệp căn cứ đã đính kèm; chỉ khi biên bản đang lập.</summary>
    [HttpDelete("{recordId:guid}/evidence/{fileId:guid}")]
    [ProducesResponseType<HandoverRecordResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<HandoverRecordResponse> RemoveEvidence(Guid recordId, Guid fileId, CancellationToken ct)
    {
        return recordService.RemoveEvidenceAsync(recordId, fileId, ct);
    }

    /// <summary>Tải một tệp đính kèm biên bản (giấy tờ người nhận hoặc tệp căn cứ). Hỏng thì 500 CONTENT_INTEGRITY_FAILED.</summary>
    [HttpGet("{recordId:guid}/files/{fileId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> DownloadFile(Guid recordId, Guid fileId, CancellationToken ct)
    {
        PreparedDownload download;
        try
        {
            download = await recordService.PrepareVerifierFileAsync(recordId, fileId, ct);
        }
        catch (ContentIntegrityException)
        {
            return IntegrityFailed(HttpContext);
        }
        return new HandoverFileResult(download);
    }

    /// <summary>500 CONTENT_INTEGRITY_FAILED dạng ProblemDetails, không kèm byte nội dung nào.</summary>
    public static IActionResult IntegrityFailed(HttpContext context)
    {
        ProblemDetails problem = GlobalExceptionHandler.Problem(
            StatusCodes.Status500InternalServerError, ErrorCode.ContentIntegrityFailed,
            "Tệp không vượt qua kiểm tra toàn vẹn nên không thể tải.");
        problem.Extensions["traceId"] = context.TraceIdentifier;
        ObjectResult result = new ObjectResult(problem);
        result.StatusCode = StatusCodes.Status500InternalServerError;
        result.ContentTypes.Add("application/problem+json");
        return result;
    }
}
