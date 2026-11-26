using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>
/// Danh bạ Người thực thi hệ thống cho Chủ tài sản lựa chọn (giảm scope, chỉ mời Người thực thi hệ thống). Người thực thi là nhân viên hệ thống, không có ảnh thẻ hay thông tin xác minh pháp lý.
/// </summary>
[ApiController]
[Authorize]
[Route("api/v1/system-executors")]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
public sealed class SystemExecutorsController(ISystemExecutorDirectoryService service) : ControllerBase
{
    /// <summary>Danh sách Người thực thi hệ thống đang hoạt động và sẵn sàng nhận nhiệm vụ.</summary>
    [HttpGet]
    [ProducesResponseType<List<SystemExecutorResponse>>(StatusCodes.Status200OK)]
    public Task<List<SystemExecutorResponse>> List(CancellationToken ct) =>
        service.ListAvailableExecutorsAsync(ct);
}
