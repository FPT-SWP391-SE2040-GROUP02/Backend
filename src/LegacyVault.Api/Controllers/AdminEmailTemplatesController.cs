using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>
/// Admin xem và sửa chữ của các thư thông báo, bật hoặc tắt loại thư cho phép tắt. Không phải Admin đang hoạt động thì 404 (SEC-04).
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/admin/email-templates")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminEmailTemplatesController : ControllerBase
{
    private readonly IEmailTemplateService templates;

    public AdminEmailTemplatesController(IEmailTemplateService templates)
    {
        this.templates = templates;
    }

    /// <summary>Mọi loại thư kèm chữ gốc và chữ đã sửa.</summary>
    [HttpGet]
    [ProducesResponseType<List<EmailTemplateResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<List<EmailTemplateResponse>> List(CancellationToken ct)
    {
        return templates.ListAsync(ct);
    }

    /// <summary>Lưu chữ cho một loại thư. Trường trống là dùng chữ gốc.</summary>
    [HttpPut("{key}")]
    [ProducesResponseType<EmailTemplateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<EmailTemplateResponse> Save(string key, [FromBody] SaveEmailTemplateRequest request, CancellationToken ct)
    {
        return templates.SaveAsync(key, request, ct);
    }

    /// <summary>Bỏ chữ đã sửa, về chữ gốc và bật lại.</summary>
    [HttpDelete("{key}")]
    [ProducesResponseType<EmailTemplateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<EmailTemplateResponse> Reset(string key, CancellationToken ct)
    {
        return templates.ResetAsync(key, ct);
    }
}
