using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>Số điện thoại của người đang đăng nhập (mọi vai trò).</summary>
[ApiController]
[Authorize]
[Route("api/v1/me/phone")]
[Produces("application/json")]
public sealed class MePhoneController(IAccountPhoneService accountPhoneService) : ControllerBase
{
    /// <summary>
    /// Đặt hoặc đổi số điện thoại. Chỉ nhận số di động Việt Nam (03, 05, 07, 08, 09; nhận cả +84). Trả người dùng mới.
    /// </summary>
    [HttpPut]
    [ProducesResponseType<UserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public Task<UserResponse> Put([FromBody] SetPhoneRequest request, CancellationToken ct)
    {
        return accountPhoneService.SetMyPhoneAsync(request, ct);
    }
}
