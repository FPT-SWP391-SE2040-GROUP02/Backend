using LegacyVault.Api.Handover;
using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LegacyVault.Api.Controllers;

/// <summary>
/// Người nhận gửi yêu cầu nhận tài sản kèm giấy tờ, qua link riêng trong thư báo kích hoạt kế hoạch hoặc ngay trong app
/// (mục "Nhận tài sản"), rồi theo dõi kết quả luật sư xét và nộp bổ sung khi được yêu cầu.
/// </summary>
/// <remarks>
/// Phải đăng nhập đúng email Chủ tài sản đã khai cho người nhận (sai thì 403 HANDOVER_EMAIL_MISMATCH). Link sai là 404.
/// </remarks>
[ApiController]
[Authorize]
[Route("api/v1/handover/requests")]
[Produces("application/json")]
public sealed class HandoverRequestsController : ControllerBase
{
    private readonly IHandoverRecordService recordService;

    public HandoverRequestsController(IHandoverRecordService recordService)
    {
        this.recordService = recordService;
    }

    /// <summary>Thông tin link riêng: tên Chủ tài sản, tên người nhận và email đã che. Không cần đăng nhập.</summary>
    [HttpGet("preview")]
    [AllowAnonymous]
    [EnableRateLimiting("plan-setup")]
    [ProducesResponseType<HandoverRequestPreviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<HandoverRequestPreviewResponse> Preview([FromQuery] string token, CancellationToken ct)
    {
        return recordService.PreviewRequestAsync(token, ct);
    }

    /// <summary>
    /// Gửi yêu cầu nhận tài sản kèm giấy chứng tử (deathCertificates), di chúc (wills), cả hai bắt buộc, giấy tờ khác (files) và
    /// lời nhắn cho luật sư. PDF hoặc ảnh, tổng tối đa 10 tệp, mỗi tệp tối đa 10 MiB.
    /// </summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(UploadedFileReader.MaxRequestBytes)]
    [EnableRateLimiting("plan-setup")]
    [ProducesResponseType<MyHandoverClaimResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<MyHandoverClaimResponse> Submit(
        [FromForm] string token, [FromForm] string? note, [FromForm] List<IFormFile>? deathCertificates,
        [FromForm] List<IFormFile>? wills, [FromForm] List<IFormFile>? files, CancellationToken ct)
    {
        HandoverDocumentUploads uploads = await ReadDocumentsAsync(deathCertificates, wills, files, ct);
        return await recordService.SubmitClaimAsync(token, note, uploads, ct);
    }

    /// <summary>
    /// Gửi yêu cầu nhận tài sản trong app, không cần link: server tự tìm người nhận của kho mang đúng email đã xác minh của
    /// tài khoản (không có thì 404). Tệp và lời nhắn giống gửi bằng link.
    /// </summary>
    [HttpPost("by-account")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(UploadedFileReader.MaxRequestBytes)]
    [EnableRateLimiting("plan-setup")]
    [ProducesResponseType<MyHandoverClaimResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<MyHandoverClaimResponse> SubmitByAccount(
        [FromForm] Guid vaultId, [FromForm] string? note, [FromForm] List<IFormFile>? deathCertificates,
        [FromForm] List<IFormFile>? wills, [FromForm] List<IFormFile>? files, CancellationToken ct)
    {
        HandoverDocumentUploads uploads = await ReadDocumentsAsync(deathCertificates, wills, files, ct);
        return await recordService.SubmitClaimByAccountAsync(vaultId, note, uploads, ct);
    }

    /// <summary>
    /// Các kho mà tôi là người nhận (email đã xác minh trùng email người nhận, kế hoạch đã kích hoạt trở đi), kèm bước hiện
    /// tại của tôi và cờ needsAction. Không có tên gói, tài sản hay số căn cước. Chưa xác minh email thì danh sách rỗng.
    /// </summary>
    [HttpGet("available")]
    [ProducesResponseType<List<AvailableHandoverVaultResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public Task<List<AvailableHandoverVaultResponse>> Available(CancellationToken ct)
    {
        return recordService.ListAvailableVaultsAsync(ct);
    }

    /// <summary>Các yêu cầu tôi đã gửi, mới nhất trước, kèm kết quả xét và tệp đã nộp.</summary>
    [HttpGet("mine")]
    [ProducesResponseType<List<MyHandoverClaimResponse>>(StatusCodes.Status200OK)]
    public Task<List<MyHandoverClaimResponse>> Mine(CancellationToken ct)
    {
        return recordService.ListMyClaimsAsync(ct);
    }

    /// <summary>Nộp thêm giấy tờ khi luật sư yêu cầu bổ sung.</summary>
    [HttpPost("{claimId:guid}/supplement")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(UploadedFileReader.MaxRequestBytes)]
    [ProducesResponseType<MyHandoverClaimResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<MyHandoverClaimResponse> Supplement(
        Guid claimId, [FromForm] string? note, [FromForm] List<IFormFile> files, CancellationToken ct)
    {
        List<UploadedFile> uploaded = await UploadedFileReader.ReadAllAsync(files, ct);
        return await recordService.SupplementClaimAsync(claimId, note, uploaded, ct);
    }

    /// <summary>Tải lại một tệp tôi đã nộp.</summary>
    [HttpGet("{claimId:guid}/files/{fileId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadFile(Guid claimId, Guid fileId, CancellationToken ct)
    {
        PreparedDownload download;
        try
        {
            download = await recordService.PrepareMyClaimFileAsync(claimId, fileId, ct);
        }
        catch (ContentIntegrityException)
        {
            return VerifierHandoverRecordsController.IntegrityFailed(HttpContext);
        }
        return new HandoverFileResult(download);
    }

    private static async Task<HandoverDocumentUploads> ReadDocumentsAsync(
        List<IFormFile>? deathCertificates, List<IFormFile>? wills, List<IFormFile>? files, CancellationToken ct)
    {
        HandoverDocumentUploads uploads = new HandoverDocumentUploads();
        uploads.DeathCertificates = await UploadedFileReader.ReadAllAsync(deathCertificates, ct);
        uploads.Wills = await UploadedFileReader.ReadAllAsync(wills, ct);
        uploads.Others = await UploadedFileReader.ReadAllAsync(files, ct);
        return uploads;
    }
}
