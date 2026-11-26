using LegacyVault.Api.Security;
using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>Chủ tài sản gán tài sản cho người nhận (docs/api-contract.md mục 6.6). Thêm hoặc gỡ gán cần mã PIN của kho.</summary>
[ApiController]
[Authorize]
[Route("api/v1/vaults/{vaultId:guid}")]
[Produces("application/json")]
public sealed class AssetAssignmentsController(IAssetAssignmentService assignmentService) : ControllerBase
{
    /// <summary>Mọi dòng gán của kho. FE gom theo tài sản hoặc theo người nhận.</summary>
    [HttpGet("assignments")]
    [ProducesResponseType<List<AssetAssignmentResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<List<AssetAssignmentResponse>> GetAssignments(Guid vaultId, CancellationToken ct)
    {
        return assignmentService.GetAssignmentsAsync(vaultId, ct);
    }

    /// <summary>Đặt lại toàn bộ người nhận của một tài sản. Nhiều người nghĩa là nhận chung; danh sách rỗng là gỡ hết.</summary>
    [RequireVaultPin]
    [HttpPut("assets/{assetId:guid}/assignments")]
    [Consumes("application/json")]
    [ProducesResponseType<List<AssetAssignmentResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<List<AssetAssignmentResponse>> SetAssetRecipients(
        Guid vaultId, Guid assetId, SetAssetRecipientsRequest request, CancellationToken ct)
    {
        return assignmentService.SetAssetRecipientsAsync(vaultId, assetId, request, ct);
    }

    /// <summary>Gỡ một người nhận khỏi một tài sản. confirmPlanDeactivation=true (query) khi Chủ tài sản đã xác nhận kế hoạch về bản nháp.</summary>
    [RequireVaultPin]
    [HttpDelete("assets/{assetId:guid}/assignments/{beneficiaryId:guid}")]
    [ProducesResponseType<List<AssetAssignmentResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<List<AssetAssignmentResponse>> RemoveAssignment(
        Guid vaultId, Guid assetId, Guid beneficiaryId, [FromQuery] ConfirmPlanImpactRequest? request, CancellationToken ct)
    {
        return assignmentService.RemoveAssignmentAsync(vaultId, assetId, beneficiaryId, request, ct);
    }
}
