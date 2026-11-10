using LegacyVault.Application.Services;

namespace LegacyVault.Infrastructure.Services;

/// <summary>
/// Đồng hồ DEMO: TimeProvider trả giờ thật cộng một độ lệch, để nút "Tua thời gian" cho cả hệ thống (job nền, hạn lời mời,
/// hạn token...) coi "hôm nay" là một ngày khác. Chỉ đăng ký ở Development/Staging (xem InfrastructureConfig).
/// Độ lệch đọc/ghi an toàn giữa nhiều luồng.
/// </summary>
public sealed class DemoClock : TimeProvider, IDemoClock
{
    private long offsetTicks;

    public bool IsEnabled
    {
        get { return true; }
    }

    public DateTimeOffset RealNow
    {
        get { return TimeProvider.System.GetUtcNow(); }
    }

    public TimeSpan Offset
    {
        get { return TimeSpan.FromTicks(Interlocked.Read(ref offsetTicks)); }
    }

    public void SetOffset(TimeSpan offset)
    {
        Interlocked.Exchange(ref offsetTicks, offset.Ticks);
    }

    public override DateTimeOffset GetUtcNow()
    {
        return RealNow + Offset;
    }
}

/// <summary>Bản dùng ở môi trường không cho tua (Production): luôn là giờ thật, không cho đặt độ lệch.</summary>
public sealed class DisabledDemoClock : IDemoClock
{
    public bool IsEnabled
    {
        get { return false; }
    }

    public DateTimeOffset RealNow
    {
        get { return TimeProvider.System.GetUtcNow(); }
    }

    public TimeSpan Offset
    {
        get { return TimeSpan.Zero; }
    }

    public void SetOffset(TimeSpan offset)
    {
        throw new InvalidOperationException("Đồng hồ DEMO chỉ dùng được ở môi trường Development hoặc Staging.");
    }
}
