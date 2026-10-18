using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace LegacyVault.Api.Controllers;

/// <summary>Kho của người đang đăng nhập (docs/api-contract.md mục 6.5).</summary>
[ApiController]
[Authorize]
[Route("api/v1/vaults")]
[Produces("application/json")]
public sealed class VaultsController(IVaultService vaultService) : ControllerBase
{
    /// <summary>"Tạo kho của tôi": 201 khi vừa tạo, 200 khi đã có kho (trả lại kho cũ).</summary>
    [HttpPost]
    [ProducesResponseType<VaultResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<VaultResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<VaultResponse>> OpenOrCreateMyVault(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] CreateVaultRequest? request, CancellationToken ct)
    {
        var result = await vaultService.OpenOrCreateMyVaultAsync(request, ct);
        return result.Created
            ? CreatedAtAction(nameof(GetVault), new { vaultId = result.Vault.Id }, result.Vault)
            : Ok(result.Vault);
    }

    /// <summary>Kho của mình. Chưa tạo → 404 VAULT_NOT_FOUND (FE hiện nút "Tạo kho của tôi").</summary>
    [HttpGet("me")]
    [ProducesResponseType<VaultResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<VaultResponse> GetMyVault(CancellationToken ct)
    {
        return vaultService.GetMyVaultAsync(ct);
    }

    [HttpGet("{vaultId:guid}")]
    [ProducesResponseType<VaultResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<VaultResponse> GetVault(Guid vaultId, CancellationToken ct)
    {
        return vaultService.GetVaultAsync(vaultId, ct);
    }

    /// <summary>Sửa tên/mô tả kho; body có etag lấy từ lần đọc gần nhất.</summary>
    [HttpPut("{vaultId:guid}")]
    [ProducesResponseType<VaultResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<VaultResponse> UpdateVault(Guid vaultId, UpdateVaultRequest request, CancellationToken ct)
    {
        return vaultService.UpdateVaultAsync(vaultId, request, ct);
    }
}
