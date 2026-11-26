using LegacyVault.Api.Security;
using LegacyVault.Application.Dtos;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LegacyVault.Api.Controllers;

/// <summary>Di chúc số của Chủ tài sản: xem danh tính đầy đủ để lập di chúc. Response không được cache.</summary>
[ApiController]
[Authorize]
[Route("api/v1/vaults/{vaultId:guid}/will")]
[Produces("application/json")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
public sealed class WillController : ControllerBase
{
    private readonly IWillIdentityService service;

    public WillController(IWillIdentityService service)
    {
        this.service = service;
    }

    /// <summary>
    /// Họ tên, ngày sinh, CCCD đủ 12 số của Chủ tài sản (kèm nơi thường trú) và của từng người nhận trong kho.
    /// Cần PIN (header X-Vault-Pin) và bị giới hạn tần suất. Chỉ Chủ tài sản của kho; người khác kể cả Admin → 404. Mỗi lần gọi thành công ghi nhật ký WILL_IDENTITY_VIEWED.
    /// </summary>
    [RequireVaultPin]
    [EnableRateLimiting("plan-setup")]
    [HttpGet("identity")]
    [ProducesResponseType<WillIdentityResponse>(StatusCodes.Status200OK)]
    public Task<WillIdentityResponse> GetIdentity(Guid vaultId, CancellationToken ct)
    {
        return service.GetAsync(vaultId, ct);
    }
}
