using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>
/// Body của POST /api/v1/vaults/{vaultId}/containers/{containerId}/beneficiaries: chỉ định một hồ sơ người nhận có sẵn cho gói.
/// </summary>
public class AddContainerBeneficiaryRequest
{
    [Required(ErrorMessage = "Thiếu người nhận.")]
    public Guid BeneficiaryId { get; set; }
}
