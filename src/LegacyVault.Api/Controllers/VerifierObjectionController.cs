using LegacyVault.Api.DeathCases;
using LegacyVault.Api.Exceptions;
using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>Verifier xem bằng chứng và kết luận báo còn sống (docs/api-contract.md mục 6.15).</summary>
[ApiController]
[Authorize]
[Route("api/v1/verifier/death-cases/{caseId:guid}/objection")]
public sealed class VerifierObjectionController : ControllerBase
{
    private readonly IVerifierObjectionService objectionService;
    private readonly ILogger<VerifierObjectionController> logger;

    public VerifierObjectionController(
        IVerifierObjectionService objectionService,
        ILogger<VerifierObjectionController> logger)
    {
        this.objectionService = objectionService;
        this.logger = logger;
    }

    [HttpGet]
    [ProducesResponseType<ObjectionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ObjectionResponse> Get(Guid caseId, CancellationToken ct)
    {
        return objectionService.GetAsync(caseId, ct);
    }

    /// <summary>
    /// Ảnh selfie của lần thử gần nhất (đạt hoặc không, xem selfieImageMatched), để đối chiếu với ảnh CCCD lúc eKYC
    /// (BR-50 sửa). Trả nội dung ảnh (không phải JSON). 404 IDENTITY_IMAGE_NOT_FOUND khi chưa có lần thử nào được so khớp.
    /// </summary>
    [HttpGet("selfie-image")]
    [Produces("image/jpeg", "image/png", "image/webp", "application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetSelfieImage(Guid caseId, CancellationToken ct)
    {
        PreparedDownload download;
        try
        {
            download = await objectionService.PrepareSelfieImageAsync(caseId, ct);
        }
        catch (ContentIntegrityException)
        {
            // Chỉ log id, không log nội dung hay khóa lưu trữ (DATA-02, SEC-07).
            logger.LogError("Ảnh selfie báo còn sống không qua kiểm toàn vẹn: hồ sơ {CaseId}", caseId);

            ProblemDetails problem = GlobalExceptionHandler.Problem(
                StatusCodes.Status500InternalServerError, ErrorCode.ContentIntegrityFailed,
                "Ảnh selfie không vượt qua kiểm tra toàn vẹn nên không thể xem.");
            problem.Extensions["traceId"] = HttpContext.TraceIdentifier;

            ObjectResult result = new ObjectResult(problem);
            result.StatusCode = StatusCodes.Status500InternalServerError;
            result.ContentTypes.Add("application/problem+json");
            return result;
        }

        logger.LogInformation("Verifier đã xem ảnh selfie báo còn sống: hồ sơ {CaseId}", caseId);
        return new IdentityImageResult(download);
    }

    [HttpPost("conclude")]
    [Consumes("application/json")]
    [ProducesResponseType<ObjectionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ObjectionResponse> Conclude(Guid caseId, ConcludeObjectionRequest request, CancellationToken ct)
    {
        return objectionService.ConcludeAsync(caseId, request, ct);
    }
}
