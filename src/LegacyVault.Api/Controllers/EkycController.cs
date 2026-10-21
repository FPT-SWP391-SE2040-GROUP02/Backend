using LegacyVault.Api.Models;
using LegacyVault.Application.Dtos.Requests;
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
// Response chứa kết quả đọc giấy tờ và trạng thái xác minh nên cấm trình duyệt và proxy lưu đệm.
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class EkycController : ControllerBase
{
    /// <summary>Ảnh tối đa 8 MiB (EkycRules.MaxImageBytes) cộng phần đầu multipart.</summary>
    private const long MaxRequestBytes = 9 * 1024 * 1024;

    private readonly IEkycService ekycService;

    public EkycController(IEkycService ekycService)
    {
        this.ekycService = ekycService;
    }

    /// <summary>
    /// Kết quả eKYC mới nhất. Chưa từng làm → status NOT_STARTED. Có ?purpose=OWNER_ENROLLMENT thì chỉ xét mục đích đó
    /// và giữ lượt đã đạt của Owner (xác minh một lần).
    /// </summary>
    [HttpGet("me")]
    [ProducesResponseType<EkycStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public Task<EkycStatusResponse> GetMyStatus([FromQuery] string? purpose, CancellationToken ct)
    {
        return ekycService.GetMyStatusAsync(purpose, ct);
    }

    /// <summary>Người dùng đồng ý cho chụp giấy tờ và khuôn mặt (EKYC-03). Gọi trước khi gửi mặt trước.</summary>
    [HttpPost("consent")]
    [ProducesResponseType<EkycConsentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public Task<EkycConsentResponse> RecordConsent([FromBody] EkycConsentRequest request, CancellationToken ct)
    {
        return ekycService.RecordConsentAsync(request, ct);
    }

    /// <summary>Gửi ảnh mặt trước CCCD: trả thông tin đọc được (không lưu) và mở bước so khớp khuôn mặt.</summary>
    [HttpPost("document")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [ProducesResponseType<EkycDocumentResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<EkycDocumentResponse> SubmitDocument([FromForm] EkycDocumentForm form, CancellationToken ct)
    {
        IFormFile file = form.File!;
        await using (Stream content = file.OpenReadStream())
        {
            return await ekycService.SubmitDocumentAsync(
                content, file.FileName, file.ContentType, form.HandoverTurnId, ct);
        }
    }

    /// <summary>Kiểm tra thông tin người dùng đã đối chiếu/sửa sau OCR; không thay kết quả từ ảnh.</summary>
    [HttpPost("document/confirm")]
    [ProducesResponseType<EkycStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<EkycStatusResponse> ConfirmDocument([FromBody] EkycDocumentConfirmationRequest request, CancellationToken ct)
    {
        return ekycService.ConfirmDocumentAsync(request, ct);
    }

    /// <summary>Gửi ảnh mặt sau của tấm thẻ vừa đọc mặt trước để kiểm tra hai mặt cùng một thẻ (EKYC-04).</summary>
    [HttpPost("document-back")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [ProducesResponseType<EkycBackResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<EkycBackResponse> SubmitBack([FromForm] EkycImageForm form, CancellationToken ct)
    {
        IFormFile file = form.File!;
        await using (Stream content = file.OpenReadStream())
        {
            return await ekycService.SubmitBackAsync(content, file.FileName, file.ContentType, ct);
        }
    }

    /// <summary>Bắt đầu thử thách camera (chớp mắt, quay đầu) cho lượt eKYC đang chờ selfie (EKYC-10).</summary>
    [HttpPost("liveness/start")]
    [ProducesResponseType<EkycLivenessResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public Task<EkycLivenessResponse> StartLiveness(CancellationToken ct)
    {
        return ekycService.StartLivenessAsync(ct);
    }

    /// <summary>Gửi một khung hình camera của thử thách và nhận tiến độ.</summary>
    [HttpPost("liveness/frame")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MaxRequestBytes)]
    [ProducesResponseType<EkycLivenessResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<EkycLivenessResponse> SubmitLivenessFrame([FromForm] EkycImageForm form, CancellationToken ct)
    {
        IFormFile file = form.File!;
        await using (Stream content = file.OpenReadStream())
        {
            return await ekycService.SubmitLivenessFrameAsync(content, file.FileName, file.ContentType, ct);
        }
    }

    /// <summary>Chốt thử thách: so khớp selfie của phiên đã đạt với khuôn mặt trên thẻ (EKYC-13, EKYC-14).</summary>
    [HttpPost("liveness/complete")]
    [ProducesResponseType<EkycStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public Task<EkycStatusResponse> CompleteLiveness(CancellationToken ct)
    {
        return ekycService.CompleteLivenessAsync(ct);
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
