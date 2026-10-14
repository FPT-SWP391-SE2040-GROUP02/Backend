namespace LegacyVault.Application.Dtos.Responses;

/// <summary>
/// Kết quả tạo hồ sơ người nhận: Created cho controller chọn 201 (vừa tạo) hay 200 (đã có hồ sơ cùng CCCD và cùng danh tính).
/// </summary>
public class CreateBeneficiaryResult
{
    public bool Created { get; set; }

    public BeneficiaryResponse Beneficiary { get; set; } = new BeneficiaryResponse();
}
