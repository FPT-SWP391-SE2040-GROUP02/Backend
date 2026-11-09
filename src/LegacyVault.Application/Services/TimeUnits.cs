namespace LegacyVault.Application.Services;

/// <summary>
/// Đơn vị thời gian của các mốc nghiệp vụ (chu kỳ điểm danh, ân hạn, hạn xét duyệt, hạn bổ sung, cửa sổ bàn giao...).
/// Mặc định là ngày, giờ, phút thật. Khi Admin bật tham số <see cref="DemoSettingKey"/> thì "1 ngày", "1 giờ", "1 phút"
/// đều chỉ còn 1 giây, để buổi demo chạy hết luồng trong vài phút mà không phải tua giờ.
/// </summary>
/// <remarks>
/// Chỉ đổi đơn vị của mốc do nghiệp vụ đặt ra. Thời hạn bảo mật (phiên đăng nhập, mã OTP, khóa đăng nhập, token) giữ nguyên.
/// </remarks>
public static class TimeUnits
{
    public const string DemoSettingKey = "Time.DemoSeconds";

    /// <summary>true khi đang ở chế độ demo (mọi ngày/giờ/phút nghiệp vụ tính bằng giây).</summary>
    public static bool IsDemo => RuntimeSettings.Bool(DemoSettingKey, false);

    public static TimeSpan Days(int count)
    {
        if (IsDemo)
        {
            return TimeSpan.FromSeconds(count);
        }
        return TimeSpan.FromDays(count);
    }

    public static TimeSpan Hours(int count)
    {
        if (IsDemo)
        {
            return TimeSpan.FromSeconds(count);
        }
        return TimeSpan.FromHours(count);
    }

    public static TimeSpan Minutes(int count)
    {
        if (IsDemo)
        {
            return TimeSpan.FromSeconds(count);
        }
        return TimeSpan.FromMinutes(count);
    }
}
