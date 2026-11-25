using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>Người thực thi xem danh sách tài sản của kho theo từng người nhận (chỉ danh sách, không có nội dung).</summary>
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

    /// <summary>Các người nhận của kho kèm tài sản được gán (tên, loại, dung lượng). Không trả nội dung tài sản.</summary>
    [HttpGet("vaults/{vaultId:guid}/assets")]
    [ProducesResponseType<List<ExecutorVaultRecipientResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<List<ExecutorVaultRecipientResponse>> ListRecipients(Guid vaultId, CancellationToken ct)
    {
        return executorAssetService.ListRecipientsAsync(vaultId, ct);
    }

    /// <summary>Tên và loại các tài sản của kho, xem được cả trước khi bàn giao. Không trả người nhận hay nội dung.</summary>
    [HttpGet("vaults/{vaultId:guid}/asset-names")]
    [ProducesResponseType<List<ExecutorAssetNameResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<List<ExecutorAssetNameResponse>> ListAssetNames(Guid vaultId, CancellationToken ct)
    {
        return executorAssetService.ListAssetNamesAsync(vaultId, ct);
    }
}
