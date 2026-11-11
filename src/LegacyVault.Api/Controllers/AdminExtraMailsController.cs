using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>
/// Admin thêm, sửa, bật tắt và xóa các thư bổ sung gửi cho Chủ kho theo ngày tính từ hạn điểm danh. Không phải Admin đang hoạt động thì 404 (SEC-04).
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/admin/extra-mails")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminExtraMailsController : ControllerBase
{
    private readonly IExtraMailService extraMails;

    public AdminExtraMailsController(IExtraMailService extraMails)
    {
        this.extraMails = extraMails;
    }

    /// <summary>Các thư bổ sung kèm giới hạn và biến chèn được.</summary>
    [HttpGet]
    [ProducesResponseType<ExtraMailListResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ExtraMailListResponse> List(CancellationToken ct)
    {
        return extraMails.ListAsync(ct);
    }

    [HttpPost]
    [ProducesResponseType<ExtraMailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ExtraMailResponse> Create([FromBody] SaveExtraMailRequest request, CancellationToken ct)
    {
        return extraMails.CreateAsync(request, ct);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<ExtraMailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ExtraMailResponse> Update(Guid id, [FromBody] SaveExtraMailRequest request, CancellationToken ct)
    {
        return extraMails.UpdateAsync(id, request, ct);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await extraMails.DeleteAsync(id, ct);
        return NoContent();
    }
}
