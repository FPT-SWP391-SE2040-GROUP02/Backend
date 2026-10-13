namespace LegacyVault.Domain.Enums;

/// <summary>
/// Nhà cung cấp đăng nhập bên ngoài gắn với tài khoản (AUTH-01..03).
/// </summary>
/// <remarks>
/// Mật khẩu app không nằm ở đây: tài khoản có mật khẩu khi User.PasswordHash khác null.
/// Thêm nhà cung cấp mới thì thêm giá trị mới, không đổi số của giá trị cũ.
/// </remarks>
public enum LoginProvider
{
    Google = 0,
}
