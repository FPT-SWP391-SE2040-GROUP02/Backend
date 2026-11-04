using LegacyVault.Application.Exceptions;
using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Services;

/// <summary>
/// Đọc cờ bảo trì toàn hệ thống (Features.MaintenanceMode, Admin bật ở trang cấu hình tham số) và chặn
/// tài khoản không phải Quản trị viên.
/// </summary>
/// <remarks>
/// - Cờ chỉ có hiệu lực khi dòng cấu hình đang bật và giá trị là true; thiếu dòng thì coi như tắt.
/// - Admin luôn được qua để còn vào hệ thống tắt bảo trì.
/// - Giá trị đọc qua ISystemSettingService nên được lưu đệm; Admin lưu thay đổi thì bộ đệm được xóa ngay.
/// </remarks>
public sealed class MaintenanceModeGuard
{
    /// <summary>Khóa tham số trong bảng SystemSettings.</summary>
    public const string SettingKey = "Features.MaintenanceMode";

    /// <summary>Câu báo cho người dùng khi bị chặn vì bảo trì.</summary>
    public const string BlockedMessage = "Hệ thống đang bảo trì. Vui lòng quay lại sau.";

    private readonly ISystemSettingService settings;

    public MaintenanceModeGuard(ISystemSettingService settings)
    {
        this.settings = settings;
    }

    /// <summary>Hệ thống có đang ở chế độ bảo trì không.</summary>
    public Task<bool> IsOnAsync(CancellationToken ct)
    {
        return settings.GetBoolAsync(SettingKey, false, ct);
    }

    /// <summary>Tài khoản có vai trò này có được dùng hệ thống không, với trạng thái bảo trì đã đọc trước.</summary>
    public static bool IsAllowed(bool maintenanceOn, UserRole role)
    {
        if (!maintenanceOn)
        {
            return true;
        }
        if (role == UserRole.Admin)
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// Chặn tài khoản không phải Admin khi đang bảo trì, với trạng thái bảo trì đã đọc trước
    /// (dùng bên trong giao dịch để không đọc cấu hình giữa chừng).
    /// </summary>
    /// <exception cref="AppException">503 MAINTENANCE_MODE khi đang bảo trì và tài khoản không phải Admin.</exception>
    public static void EnsureAllowed(bool maintenanceOn, User user)
    {
        if (IsAllowed(maintenanceOn, user.Role))
        {
            return;
        }
        throw CreateBlockedException();
    }

    /// <summary>Đọc trạng thái bảo trì rồi chặn tài khoản không phải Admin. Admin không cần đọc cấu hình.</summary>
    /// <exception cref="AppException">503 MAINTENANCE_MODE khi đang bảo trì và tài khoản không phải Admin.</exception>
    public async Task EnsureAllowedAsync(User user, CancellationToken ct)
    {
        if (user.Role == UserRole.Admin)
        {
            return;
        }
        bool maintenanceOn = await IsOnAsync(ct);
        EnsureAllowed(maintenanceOn, user);
    }

    /// <summary>Lỗi 503 MAINTENANCE_MODE dùng chung cho service và middleware.</summary>
    public static AppException CreateBlockedException()
    {
        return new AppException(AppErrorType.ServiceUnavailable, ErrorCode.MaintenanceMode, BlockedMessage);
    }
}
