using LegacyVault.Application.Dtos.Requests;
using LegacyVault.Application.Dtos.Responses;
using LegacyVault.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LegacyVault.Api.Controllers;

/// <summary>Hồ sơ người nhận cấp kho (docs/api-contract.md mục 6.8).</summary>
[ApiController]
[Authorize]
[Route("api/v1/vaults/{vaultId:guid}/beneficiaries")]
[Produces("application/json")]
public sealed class BeneficiariesController : ControllerBase
{
    private readonly IBeneficiaryService beneficiaryService;

    public BeneficiariesController(IBeneficiaryService beneficiaryService)
    {
        this.beneficiaryService = beneficiaryService;
    }

    /// <summary>Danh sách hồ sơ. sortBy: createdAt (mặc định). searchTerm: 1–4 số cuối CCCD.</summary>
    [HttpGet]
    [ProducesResponseType<PaginatedList<BeneficiaryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<PaginatedList<BeneficiaryResponse>> GetBeneficiaries(
        Guid vaultId, [FromQuery] PaginationRequest pagination, CancellationToken ct)
    {
        return beneficiaryService.GetBeneficiariesAsync(vaultId, pagination, ct);
    }

    /// <summary>Tạo hồ sơ: 201 khi vừa tạo, 200 khi đã có hồ sơ cùng CCCD và cùng danh tính (trả hồ sơ cũ).</summary>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<BeneficiaryResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<BeneficiaryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BeneficiaryResponse>> CreateBeneficiary(
        Guid vaultId, CreateBeneficiaryRequest request, CancellationToken ct)
    {
        CreateBeneficiaryResult result = await beneficiaryService.CreateBeneficiaryAsync(vaultId, request, ct);
        if (result.Created)
        {
            return CreatedAtAction(nameof(GetBeneficiary),
                new { vaultId, beneficiaryId = result.Beneficiary.Id }, result.Beneficiary);
        }
        return Ok(result.Beneficiary);
    }

    [HttpGet("{beneficiaryId:guid}")]
    [ProducesResponseType<BeneficiaryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<BeneficiaryResponse> GetBeneficiary(Guid vaultId, Guid beneficiaryId, CancellationToken ct)
    {
        return beneficiaryService.GetBeneficiaryAsync(vaultId, beneficiaryId, ct);
    }

    /// <summary>Sửa họ tên/ngày sinh/liên hệ (không đổi được CCCD); etag bắt buộc trong body.</summary>
    [HttpPut("{beneficiaryId:guid}")]
    [Consumes("application/json")]
    [ProducesResponseType<BeneficiaryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<BeneficiaryResponse> UpdateBeneficiary(
        Guid vaultId, Guid beneficiaryId, UpdateBeneficiaryRequest request, CancellationToken ct)
    {
        return beneficiaryService.UpdateBeneficiaryAsync(vaultId, beneficiaryId, request, ct);
    }

    /// <summary>Xóa hồ sơ (vẫn cho khi dịch vụ đang ân hạn).</summary>
    [HttpDelete("{beneficiaryId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteBeneficiary(Guid vaultId, Guid beneficiaryId, CancellationToken ct)
    {
        await beneficiaryService.DeleteBeneficiaryAsync(vaultId, beneficiaryId, ct);
        return NoContent();
    }
}
