namespace LegacyVault.Application.Notifications;

/// <summary>
/// Cung cấp địa chỉ URL liên kết tới ứng dụng (trang web người dùng).
/// Cài đặt ở Infrastructure thông qua cấu hình hệ thống.
/// </summary>
public interface IAppUrlProvider
{
    string GetEmailVerificationUrl(string token);
}
