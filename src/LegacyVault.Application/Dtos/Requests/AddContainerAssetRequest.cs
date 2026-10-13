using System.ComponentModel.DataAnnotations;

namespace LegacyVault.Application.Dtos.Requests;

/// <summary>Body của POST /api/v1/vaults/{vaultId}/containers/{containerId}/assets: đưa một tài sản có sẵn vào gói.</summary>
public class AddContainerAssetRequest
{
    [Required(ErrorMessage = "Thiếu tài sản.")]
    public Guid AssetId { get; set; }
}
