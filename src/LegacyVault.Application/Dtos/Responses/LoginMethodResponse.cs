namespace LegacyVault.Application.Dtos.Responses;

/// <summary>Một phương thức đăng nhập của tài khoản (màn hình "Phương thức đăng nhập", AUTH-03).</summary>
public class LoginMethodResponse
{
    /// <summary>"PASSWORD" hoặc "GOOGLE".</summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>Email Google đã liên kết (chỉ với GOOGLE).</summary>
    public string? Email { get; set; }

    public DateTimeOffset? LinkedAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>false khi đây là phương thức cuối cùng: FE khóa nút "Gỡ" (backend vẫn chặn).</summary>
    public bool CanRemove { get; set; }
}
