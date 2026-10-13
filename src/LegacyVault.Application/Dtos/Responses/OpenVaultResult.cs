namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Kết quả "Tạo kho của tôi": Created cho controller chọn 201 (vừa tạo) hay 200 (mở lại kho cũ).</summary>
public class OpenVaultResult
{
    public bool Created { get; set; }

    public VaultResponse Vault { get; set; } = new VaultResponse();
}
