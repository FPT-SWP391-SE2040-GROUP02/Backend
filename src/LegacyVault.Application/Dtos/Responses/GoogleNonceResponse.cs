namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Body của GET /api/v1/auth/google/nonce: nonce một lần cho nút Đăng nhập Google.</summary>
public class GoogleNonceResponse
{
    public string Nonce { get; set; } = string.Empty;
}
