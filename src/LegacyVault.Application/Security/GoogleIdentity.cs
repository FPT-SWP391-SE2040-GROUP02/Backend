namespace LegacyVault.Application.Security;

/// <summary>Các claim cần dùng từ một Google ID token ĐÃ được IGoogleTokenVerifier kiểm tra.</summary>
public class GoogleIdentity
{
    /// <summary>Claim <c>sub</c>: định danh ổn định, dùng để tìm liên kết (AUTH-01).</summary>
    public string Subject { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    /// <summary>Claim <c>email_verified</c>. false thì không coi email là đã xác minh (AUTH-04).</summary>
    public bool EmailVerified { get; set; }

    /// <summary>Chỉ để điền sẵn họ tên khi tạo tài khoản; không xác nhận danh tính (AUTH-05).</summary>
    public string? FullName { get; set; }
}
