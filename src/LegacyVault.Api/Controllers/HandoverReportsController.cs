using LegacyVault.Api.Handover;
using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LegacyVault.Api.Controllers;

/// <summary>
/// Người thân nộp hồ sơ báo tin Chủ kho đã mất (không cần được khai làm người nhận từ trước) và theo dõi kết quả.
/// </summary>
/// <remarks>Phản hồi không bao giờ cho biết người mất có kho hay không.</remarks>
[ApiController]
[Authorize]
[Route("api/v1/handover/reports")]
[Produces("application/json")]
public sealed class HandoverReportsController : ControllerBase
{
    private readonly IHandoverReportService reportService;

    public HandoverReportsController(IHandoverReportService reportService)
    {
        this.reportService = reportService;
    }

    /// <summary>
    /// Nộp hồ sơ: họ tên, số căn cước người mất, cam đoan giấy tờ là thật, giấy chứng tử và di chúc (bắt buộc), giấy tờ khác (nếu có), người nhận
    /// gợi ý (JSON mảng {fullName, email, phone}). Tổng cộng tối đa 10 tệp, mỗi tệp 10 MiB, PDF hoặc ảnh.
    /// </summary>
    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(UploadedFileReader.MaxRequestBytes)]
    [EnableRateLimiting("plan-setup")]
    [ProducesResponseType<MyHandoverReportResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<MyHandoverReportResponse> Submit(
        [FromForm] SubmitHandoverReportForm form, CancellationToken ct)
    {
        HandoverReportUploads uploads = new HandoverReportUploads();
        uploads.DeathCertificates = await UploadedFileReader.ReadAllAsync(form.DeathCertificates, ct);
        uploads.Wills = await UploadedFileReader.ReadAllAsync(form.Wills, ct);
        uploads.Others = await UploadedFileReader.ReadAllAsync(form.Others, ct);
        return await reportService.SubmitAsync(form, uploads, ct);
    }

    /// <summary>Các hồ sơ tôi đã nộp, mới nhất trước.</summary>
    [HttpGet("mine")]
    [ProducesResponseType<List<MyHandoverReportResponse>>(StatusCodes.Status200OK)]
    public Task<List<MyHandoverReportResponse>> Mine(CancellationToken ct)
    {
        return reportService.ListMineAsync(ct);
    }

    /// <summary>Nộp thêm giấy tờ khi luật sư yêu cầu bổ sung.</summary>
    [HttpPost("{reportId:guid}/supplement")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(UploadedFileReader.MaxRequestBytes)]
    [EnableRateLimiting("plan-setup")]
    [ProducesResponseType<MyHandoverReportResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<MyHandoverReportResponse> Supplement(
        Guid reportId, [FromForm] string? note, [FromForm] List<IFormFile> files, CancellationToken ct)
    {
        List<UploadedFile> uploaded = await UploadedFileReader.ReadAllAsync(files, ct);
        return await reportService.SupplementAsync(reportId, note, uploaded, ct);
    }

    /// <summary>Tải lại một tệp tôi đã nộp.</summary>
    [HttpGet("{reportId:guid}/files/{fileId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadFile(Guid reportId, Guid fileId, CancellationToken ct)
    {
        PreparedDownload download;
        try
        {
            download = await reportService.PrepareMyFileAsync(reportId, fileId, ct);
        }
        catch (ContentIntegrityException)
        {
            return VerifierHandoverRecordsController.IntegrityFailed(HttpContext);
        }
        return new HandoverFileResult(download);
    }
}
