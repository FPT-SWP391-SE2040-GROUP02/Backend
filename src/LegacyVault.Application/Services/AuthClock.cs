namespace LegacyVault.Application.Services;

/// <summary>
/// Đồng hồ riêng của phần đăng nhập (tạo/kiểm JWT, refresh token, khóa đăng nhập sai). Mặc định chạy theo giờ thật nên nút
/// "Tua thời gian" chỉ tua mốc nghiệp vụ, không làm phiên đăng nhập hết hạn. Admin bật tham số <see cref="FollowDemoSettingKey"/>
/// thì đăng nhập theo giờ ảo như các phần khác (để demo việc hết phiên).
/// </summary>
/// <remarks>
/// Đăng ký trong DI dạng keyed service với khóa <see cref="ServiceKey"/>; nơi cần thì nhận qua
/// <c>[FromKeyedServices(AuthClock.ServiceKey)] TimeProvider</c>. Ở Production đồng hồ hệ thống vốn là giờ thật nên công tắc không
/// đổi gì. Gạt công tắc khi đang đăng nhập thì phiên hiện tại bị coi là hết hạn hoặc chưa tới giờ, phải đăng nhập lại một lần.
/// </remarks>
public sealed class AuthClock : TimeProvider
{
    public const string ServiceKey = "auth-clock";
    public const string FollowDemoSettingKey = "Time.AuthFollowsDemoClock";

    private readonly TimeProvider systemClock;

    /// <param name="systemClock">Đồng hồ chung của hệ thống (DemoClock ở Development/Staging, giờ thật ở Production).</param>
    public AuthClock(TimeProvider systemClock)
    {
        this.systemClock = systemClock;
    }

    /// <summary>true khi Admin bật công tắc cho đăng nhập chạy theo giờ ảo.</summary>
    public static bool FollowsDemoClock
    {
        get { return RuntimeSettings.Bool(FollowDemoSettingKey, false); }
    }

    public override DateTimeOffset GetUtcNow()
    {
        if (FollowsDemoClock)
        {
            return systemClock.GetUtcNow();
        }
        return TimeProvider.System.GetUtcNow();
    }
}
