using LegacyVault.Api.Exceptions;
using LegacyVault.Api.Handover;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>Ảnh thẻ công chứng viên của chính Người xét duyệt, chỉ xem (Admin là người nộp, xem PUT admin/users/{id}/notary-card).</summary>
/// <remarks>Controller chỉ [Authorize]; vai trò được service kiểm từ DB (ROLE-04), chỉ Người xét duyệt (luật sư) có ảnh thẻ, vai trò khác (kể cả Người thực thi) nhận 404.</remarks>
[ApiController]
[Authorize]
[Route("api/v1/verifier/notary-card")]
public sealed class VerifierNotaryCardController(
    INotaryCardService notaryCardService, ILogger<VerifierNotaryCardController> logger) : ControllerBase
{
    /// <summary>Đã có ảnh thẻ chưa và nộp lúc nào.</summary>
    [HttpGet]
    [ProducesResponseType<NotaryCardStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<NotaryCardStatusResponse> GetStatus(CancellationToken ct)
    {
        return notaryCardService.GetStatusAsync(ct);
    }

    /// <summary>Tải ảnh thẻ của chính mình (nội dung ảnh, không phải JSON). 404 khi chưa có.</summary>
    [HttpGet("content")]
    [Produces("image/jpeg", "image/png", "image/webp", "application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetContent(CancellationToken ct)
    {
        PreparedDownload download;
        try
        {
            download = await notaryCardService.PrepareAsync(ct);
        }
        catch (ContentIntegrityException)
        {
            logger.LogError("Ảnh thẻ công chứng viên không qua kiểm toàn vẹn");

            ProblemDetails problem = GlobalExceptionHandler.Problem(
                StatusCodes.Status500InternalServerError, ErrorCode.ContentIntegrityFailed,
                "Ảnh thẻ không vượt qua kiểm tra toàn vẹn nên không thể tải.");
            problem.Extensions["traceId"] = HttpContext.TraceIdentifier;

            ObjectResult result = new ObjectResult(problem);
            result.StatusCode = StatusCodes.Status500InternalServerError;
            result.ContentTypes.Add("application/problem+json");
            return result;
        }

        return new AvatarFileResult(download);
    }
}
