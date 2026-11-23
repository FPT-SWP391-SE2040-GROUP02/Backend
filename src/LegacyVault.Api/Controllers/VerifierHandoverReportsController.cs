using LegacyVault.Api.Handover;
using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>Luật sư xét hồ sơ báo tin Chủ kho đã mất. Chấp nhận thì mở biên bản bàn giao và khóa kho.</summary>
/// <remarks>Vai trò luật sư được service kiểm từ DB; hồ sơ không được giao cho mình luôn là 404.</remarks>
[ApiController]
[Authorize]
[Route("api/v1/verifier/handover-reports")]
[Produces("application/json")]
public sealed class VerifierHandoverReportsController : ControllerBase
{
    private readonly IHandoverReportService reportService;

    public VerifierHandoverReportsController(IHandoverReportService reportService)
    {
        this.reportService = reportService;
    }

    /// <summary>Các hồ sơ được giao cho tôi, mới nhất trước.</summary>
    [HttpGet]
    [ProducesResponseType<List<VerifierHandoverReportResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<List<VerifierHandoverReportResponse>> List(CancellationToken ct)
    {
        return reportService.ListForVerifierAsync(ct);
    }

    /// <summary>Xét hồ sơ: ACCEPT, REQUEST_SUPPLEMENT hoặc REJECT (hai cái sau bắt buộc lý do).</summary>
    [HttpPost("{reportId:guid}/review")]
    [ProducesResponseType<VerifierHandoverReportResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<VerifierHandoverReportResponse> Review(
        Guid reportId, ReviewHandoverClaimRequest request, CancellationToken ct)
    {
        return reportService.ReviewAsync(reportId, request, ct);
    }

    /// <summary>Tải một tệp giấy tờ của hồ sơ.</summary>
    [HttpGet("{reportId:guid}/files/{fileId:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadFile(Guid reportId, Guid fileId, CancellationToken ct)
    {
        PreparedDownload download;
        try
        {
            download = await reportService.PrepareVerifierFileAsync(reportId, fileId, ct);
        }
        catch (ContentIntegrityException)
        {
            return VerifierHandoverRecordsController.IntegrityFailed(HttpContext);
        }
        return new HandoverFileResult(download);
    }
}
