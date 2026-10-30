using LegacyVault.Api.Security;
using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>Gói bàn giao (Container) trong kho (docs/api-contract.md mục 6.6).</summary>
[ApiController]
[Authorize]
[Route("api/v1/vaults/{vaultId:guid}/containers")]
[Produces("application/json")]
public sealed class ContainersController(IContainerService containerService) : ControllerBase
{
    /// <summary>Danh sách gói. sortBy: createdAt (mặc định) | name. includeArchived mặc định false.</summary>
    [HttpGet]
    [ProducesResponseType<PaginatedList<ContainerResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<PaginatedList<ContainerResponse>> GetContainers(
        Guid vaultId, [FromQuery] PaginationRequest pagination, CancellationToken ct,
        [FromQuery] bool includeArchived = false)
    {
        return containerService.GetContainersAsync(vaultId, includeArchived, pagination, ct);
    }

    /// <summary>Tạo gói rỗng ở DRAFT (hạng Free cũng được tạo, SETUP-17).</summary>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<ContainerResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ContainerResponse>> CreateContainer(
        Guid vaultId, CreateContainerRequest request, CancellationToken ct)
    {
        ContainerResponse container = await containerService.CreateContainerAsync(vaultId, request, ct);
        return CreatedAtAction(nameof(GetContainer), new { vaultId, containerId = container.Id }, container);
    }

    [HttpGet("{containerId:guid}")]
    [ProducesResponseType<ContainerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ContainerResponse> GetContainer(Guid vaultId, Guid containerId, CancellationToken ct)
    {
        return containerService.GetContainerAsync(vaultId, containerId, ct);
    }

    /// <summary>Đổi tên/mô tả gói; etag bắt buộc trong body.</summary>
    [HttpPut("{containerId:guid}")]
    [Consumes("application/json")]
    [ProducesResponseType<ContainerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ContainerResponse> UpdateContainer(
        Guid vaultId, Guid containerId, UpdateContainerRequest request, CancellationToken ct)
    {
        return containerService.UpdateContainerAsync(vaultId, containerId, request, ct);
    }

    /// <summary>Lưu trữ gói (không xóa hẳn): gỡ hết tài sản, chuyển ARCHIVED.</summary>
    [RequireVaultPin]
    [HttpDelete("{containerId:guid}")]
    [ProducesResponseType<ContainerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ContainerResponse> ArchiveContainer(
        Guid vaultId, Guid containerId, [FromQuery] ConfirmPlanImpactRequest request, CancellationToken ct)
    {
        return containerService.ArchiveContainerAsync(vaultId, containerId, request, ct);
    }

    /// <summary>Đưa một tài sản của kho vào gói (hạng Free bị chặn, SETUP-17).</summary>
    [HttpPost("{containerId:guid}/assets")]
    [Consumes("application/json")]
    [ProducesResponseType<ContainerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ContainerResponse> AddContainerAsset(
        Guid vaultId, Guid containerId, AddContainerAssetRequest request, CancellationToken ct)
    {
        return containerService.AddAssetAsync(vaultId, containerId, request, ct);
    }

    /// <summary>Gỡ tài sản khỏi gói (không xóa tài sản, không giải phóng dung lượng — QUOTA-02).</summary>
    [RequireVaultPin]
    [HttpDelete("{containerId:guid}/assets/{assetId:guid}")]
    [ProducesResponseType<ContainerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ContainerResponse> RemoveContainerAsset(
        Guid vaultId, Guid containerId, Guid assetId, [FromQuery] ConfirmPlanImpactRequest request,
        CancellationToken ct)
    {
        return containerService.RemoveAssetAsync(vaultId, containerId, assetId, request, ct);
    }

    /// <summary>Chỉ định một hồ sơ người nhận của kho cho gói (hạng Free bị chặn, SETUP-17; kiểm suất, QUOTA-05).</summary>
    [RequireVaultPin]
    [HttpPost("{containerId:guid}/beneficiaries")]
    [Consumes("application/json")]
    [ProducesResponseType<ContainerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ContainerResponse> AddContainerBeneficiary(
        Guid vaultId, Guid containerId, AddContainerBeneficiaryRequest request, CancellationToken ct)
    {
        return containerService.AddBeneficiaryAsync(vaultId, containerId, request, ct);
    }

    /// <summary>Gỡ người nhận khỏi gói (không xóa hồ sơ; người còn ở gói khác vẫn chiếm suất).</summary>
    [RequireVaultPin]
    [HttpDelete("{containerId:guid}/beneficiaries/{beneficiaryId:guid}")]
    [ProducesResponseType<ContainerResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ContainerResponse> RemoveContainerBeneficiary(
        Guid vaultId, Guid containerId, Guid beneficiaryId, [FromQuery] ConfirmPlanImpactRequest request,
        CancellationToken ct)
    {
        return containerService.RemoveBeneficiaryAsync(vaultId, containerId, beneficiaryId, request, ct);
    }
}
