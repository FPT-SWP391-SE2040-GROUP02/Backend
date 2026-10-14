using LegacyVault.Api.Models;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>Xác minh danh tính điện tử của người dùng đang đăng nhập (docs/api-contract.md mục 6.9).</summary>
[ApiController]
[Authorize]
[Route("api/v1/ekyc")]
// Cố ý KHÔNG đặt [Produces("application/json")]: giống CheckInsController, để lỗi validate 400 giữ problem+json.
public sealed class EkycController : ControllerBase
{
    /// <summary>Ảnh tối đa 8 MiB (EkycRules.MaxImageBytes) cộng phần đầu multipart.</summary>
    private const long MaxRequestBytes = 9 * 1024 * 1024;

    private readonly IEkycService ekycService;

    public EkycController(IEkycService ekycService)
    {
        this.ekycService = ekycService;
    }

    /// <summary>Kết quả eKYC mới nhất. Chưa từng làm → status NOT_STARTED.</summary>
    [HttpGet("me")]
    [ProducesResponseType<EkycStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public Task<EkycStatusResponse> GetMyStatus(CancellationToken ct)
    {
        return ekycService.GetMyStatusAsync(ct);
    }

    /// <summary>Gửi ảnh mặt trước CCCD: trả thông tin đọc được (không lưu) và mở bước so khớp khuôn mặt.</summary>
    [HttpPost("document")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [ProducesResponseType<EkycDocumentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<EkycDocumentResponse> SubmitDocument([FromForm] EkycImageForm form, CancellationToken ct)
    {
        IFormFile file = form.File!;
        await using (Stream content = file.OpenReadStream())
        {
            return await ekycService.SubmitDocumentAsync(content, file.FileName, file.ContentType, ct);
        }
    }

    /// <summary>Gửi selfie để so khớp với khuôn mặt trên thẻ vừa đọc. Mỗi lần đọc thẻ chỉ so khớp một lần.</summary>
    [HttpPost("face-match")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [ProducesResponseType<EkycStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<EkycStatusResponse> SubmitSelfie([FromForm] EkycImageForm form, CancellationToken ct)
    {
        IFormFile file = form.File!;
        await using (Stream content = file.OpenReadStream())
        {
            return await ekycService.SubmitSelfieAsync(content, file.FileName, file.ContentType, ct);
        }
    }
}
