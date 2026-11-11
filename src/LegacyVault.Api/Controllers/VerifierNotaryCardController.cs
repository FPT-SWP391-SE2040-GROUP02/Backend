using LegacyVault.Api.Exceptions;
using LegacyVault.Api.Handover;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Exceptions;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>Ảnh thẻ công chứng viên của Người xét duyệt đang đăng nhập: chỉ xem tình trạng và xem ảnh. Ảnh do Admin nộp.</summary>
/// <remarks>Vai trò Verifier được service kiểm từ DB.</remarks>
[ApiController]
[Authorize]
[Route("api/v1/verifier/notary-card")]
[Produces("application/json")]
public sealed class VerifierNotaryCardController : ControllerBase
{
    private readonly INotaryCardService notaryCardService;
    private readonly ILogger<VerifierNotaryCardController> logger;

    public VerifierNotaryCardController(INotaryCardService notaryCardService, ILogger<VerifierNotaryCardController> logger)
    {
        this.notaryCardService = notaryCardService;
        this.logger = logger;
    }

    /// <summary>Đã có ảnh thẻ chưa và nộp lúc nào.</summary>
    [HttpGet]
    [ProducesResponseType<NotaryCardStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public Task<NotaryCardStatusResponse> GetStatus(CancellationToken ct)
    {
        return notaryCardService.GetOwnStatusAsync(ct);
    }

    /// <summary>Tải ảnh thẻ của chính mình (nội dung ảnh, không phải JSON). 404 NOTARY_CARD_NOT_FOUND khi chưa có.</summary>
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
            download = await notaryCardService.PrepareOwnAsync(ct);
        }
        catch (ContentIntegrityException)
        {
            logger.LogError("Ảnh thẻ công chứng viên không qua kiểm toàn vẹn");

            ProblemDetails problem = GlobalExceptionHandler.Problem(
                StatusCodes.Status500InternalServerError, ErrorCode.ContentIntegrityFailed,
                "Ảnh thẻ công chứng viên không vượt qua kiểm tra toàn vẹn nên không thể tải.");
            problem.Extensions["traceId"] = HttpContext.TraceIdentifier;

            ObjectResult result = new ObjectResult(problem);
            result.StatusCode = StatusCodes.Status500InternalServerError;
            result.ContentTypes.Add("application/problem+json");
            return result;
        }

        return new AvatarFileResult(download);
    }
}
