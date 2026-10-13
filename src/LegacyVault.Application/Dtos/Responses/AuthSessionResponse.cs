namespace LegacyVault.Application.Dtos.Responses;

/// <summary>
/// JSON trả cho FE sau đăng nhập/refresh (trả thẳng object, không bọc envelope — khớp axiosClient của FE).
/// </summary>
/// <remarks>
/// Cách truyền token đã chốt với FE (mô hình lai):
/// - Access token (ngắn hạn) nằm Ở ĐÂY; FE lưu và gửi qua header Authorization: Bearer.
/// - Refresh token KHÔNG nằm ở đây mà trong cookie HttpOnly, Path=/api/v1/auth, để JavaScript không đọc được.
///   FE gọi POST /api/v1/auth/refresh với withCredentials khi gặp 401.
/// </remarks>
public class AuthSessionResponse
{
    public string AccessToken { get; set; } = string.Empty;

    /// <summary>Số giây access token còn hiệu lực, để FE hẹn giờ refresh.</summary>
    public int ExpiresIn { get; set; }

    public UserResponse User { get; set; } = new UserResponse();
}
