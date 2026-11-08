using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>Người thực thi xem danh sách tài sản của kho theo từng gói bàn giao (chỉ danh sách, không có nội dung).</summary>
/// <remarks>Người gọi phải là Người thực thi đang hiệu lực của kho và không phải chủ kho; không đủ điều kiện thì 404.</remarks>
[ApiController]
[Authorize]
[Route("api/v1/executor")]
public sealed class ExecutorAssetsController : ControllerBase
{
    private readonly IExecutorAssetService executorAssetService;

    public ExecutorAssetsController(IExecutorAssetService executorAssetService)
    {
        this.executorAssetService = executorAssetService;
    }

    /// <summary>Các gói Ready của kho kèm người nhận và tài sản (tên, loại, dung lượng). Không trả nội dung tài sản.</summary>
    [HttpGet("vaults/{vaultId:guid}/assets")]
    [ProducesResponseType<List<ExecutorVaultPackageResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<List<ExecutorVaultPackageResponse>> ListPackages(Guid vaultId, CancellationToken ct)
    {
        return executorAssetService.ListPackagesAsync(vaultId, ct);
    }
}
