using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>
/// Admin xem và sửa mẫu biên bản đồng ý nhận và văn bản từ chối nhận di sản. Mỗi lần lưu là một phiên bản mới; biên bản đã tạo giữ
/// nguyên chữ của phiên bản lúc tạo. Không phải Admin đang hoạt động thì 404 (SEC-04).
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/admin/handover-templates")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminHandoverTemplatesController : ControllerBase
{
    private readonly IHandoverTemplateService templates;

    public AdminHandoverTemplatesController(IHandoverTemplateService templates)
    {
        this.templates = templates;
    }

    /// <summary>Mẫu đang dùng cho biên bản mới, kèm danh sách trường được chèn và mẫu gốc.</summary>
    [HttpGet("current")]
    [ProducesResponseType<AdminHandoverTemplateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<AdminHandoverTemplateResponse> Current(CancellationToken ct)
    {
        return templates.GetCurrentAsync(ct);
    }

    /// <summary>Lịch sử các lần lưu, mới nhất trước.</summary>
    [HttpGet]
    [ProducesResponseType<List<HandoverTemplateVersionResponse>>(StatusCodes.Status200OK)]
    public Task<List<HandoverTemplateVersionResponse>> List(CancellationToken ct)
    {
        return templates.ListVersionsAsync(ct);
    }

    /// <summary>Một phiên bản cũ theo số.</summary>
    [HttpGet("{version:int}")]
    [ProducesResponseType<AdminHandoverTemplateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<AdminHandoverTemplateResponse> Get(int version, CancellationToken ct)
    {
        return templates.GetVersionAsync(version, ct);
    }

    /// <summary>Lưu phiên bản mới của bộ mẫu.</summary>
    [HttpPost]
    [ProducesResponseType<AdminHandoverTemplateResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<AdminHandoverTemplateResponse> Save([FromBody] SaveHandoverTemplateRequest request, CancellationToken ct)
    {
        return templates.SaveAsync(request, ct);
    }
}
