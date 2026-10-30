using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace LegacyVault.Api.Controllers;

/// <summary>Mã PIN 6 chữ số của kho (docs/api-contract.md mục 6.20).</summary>
[ApiController]
[Authorize]
[Route("api/v1/vaults/{vaultId:guid}/pin")]
[Produces("application/json")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
public sealed class VaultPinController(IVaultPinService service) : ControllerBase
{
    /// <summary>Trạng thái PIN: đã đặt chưa, có bắt buộc không, khóa đến khi nào, còn mấy lần thử.</summary>
    [HttpGet]
    [ProducesResponseType<VaultPinStatusResponse>(StatusCodes.Status200OK)]
    public Task<VaultPinStatusResponse> Get(Guid vaultId, CancellationToken ct)
    {
        return service.GetStatusAsync(vaultId, ct);
    }

    /// <summary>Đặt PIN lần đầu. Đã có PIN → 409 VAULT_PIN_ALREADY_SET.</summary>
    [HttpPost]
    [EnableRateLimiting("vault-pin")]
    [Consumes("application/json")]
    [ProducesResponseType<VaultPinStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<VaultPinStatusResponse> Set(Guid vaultId, SetVaultPinRequest request, CancellationToken ct)
    {
        return service.SetPinAsync(vaultId, request, ct);
    }

    /// <summary>Đổi PIN bằng PIN cũ. Sai PIN cũ → 403 INVALID_VAULT_PIN, bị khóa → 423 VAULT_PIN_LOCKED.</summary>
    [HttpPost("change")]
    [EnableRateLimiting("vault-pin")]
    [Consumes("application/json")]
    [ProducesResponseType<VaultPinStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status423Locked)]
    public Task<VaultPinStatusResponse> Change(Guid vaultId, ChangeVaultPinRequest request, CancellationToken ct)
    {
        return service.ChangePinAsync(vaultId, request, ct);
    }

    /// <summary>Quên PIN: nhập đúng mật khẩu tài khoản thì đặt PIN mới ngay và gỡ khóa.</summary>
    [HttpPost("reset")]
    [EnableRateLimiting("vault-pin")]
    [Consumes("application/json")]
    [ProducesResponseType<VaultPinStatusResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<VaultPinStatusResponse> Reset(Guid vaultId, ResetVaultPinRequest request, CancellationToken ct)
    {
        return service.ResetPinAsync(vaultId, request, ct);
    }
}
