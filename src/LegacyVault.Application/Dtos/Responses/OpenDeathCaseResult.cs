namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Kết quả "mở hồ sơ chứng tử": Created cho controller chọn 201 (vừa tạo) hay 200 (đã có hồ sơ mở).</summary>
public class OpenDeathCaseResult
{
    public bool Created { get; set; }

    public DeathCaseResponse DeathCase { get; set; } = new DeathCaseResponse();
}
