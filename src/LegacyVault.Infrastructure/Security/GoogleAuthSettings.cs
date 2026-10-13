namespace LegacyVault.Infrastructure.Security;

/// <summary>Cấu hình đăng nhập Google, đọc từ mục "GoogleAuth" (appsettings hoặc biến môi trường).</summary>
public sealed class GoogleAuthSettings
{
    public const string SectionName = "GoogleAuth";

    /// <summary>
    /// OAuth Client ID kiểu "Web application" tạo trên Google Cloud Console, dạng "...apps.googleusercontent.com".
    /// FE dùng CÙNG Client ID này để lấy ID token; token có aud khác sẽ bị từ chối.
    /// </summary>
    public string ClientId { get; set; } = string.Empty;
}
