using LegacyVault.Application.ServiceConfig;
using LegacyVault.Domain.Enums;
using LegacyVault.Infrastructure.ServiceConfig;
using Xunit;

namespace LegacyVault.IntegrationTests.ServiceConfig;

/// <summary>Trạng thái gọi gần nhất của từng dịch vụ, giữ trong bộ nhớ.</summary>
public sealed class ServiceStatusTrackerTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 11, 3, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Get_UnknownService_ReturnsEmptyStatus()
    {
        ServiceStatusTracker tracker = new ServiceStatusTracker();

        ServiceStatus status = tracker.Get(SystemConfigServices.Ekyc);

        Assert.Null(status.ActiveSlot);
        Assert.Null(status.LastSucceeded);
        Assert.Null(status.LastAt);
        Assert.Null(status.LastErrorCode);
    }

    [Fact]
    public void Record_PrimaryFailsThenFallbackSucceeds_ActiveSlotIsFallback()
    {
        ServiceStatusTracker tracker = new ServiceStatusTracker();

        tracker.Record(SystemConfigServices.Ekyc, SystemConfigSlot.Primary, false, "HTTP_503", Now);
        tracker.Record(SystemConfigServices.Ekyc, SystemConfigSlot.Fallback, true, null, Now.AddSeconds(1));

        ServiceStatus status = tracker.Get(SystemConfigServices.Ekyc);
        Assert.Equal(SystemConfigSlot.Fallback, status.ActiveSlot);
        Assert.True(status.LastSucceeded);
        Assert.Equal(Now.AddSeconds(1), status.LastAt);
        Assert.Null(status.LastErrorCode);
    }

    [Fact]
    public void Record_Failure_IsReportedWithErrorCodeAndNoActiveSlot()
    {
        ServiceStatusTracker tracker = new ServiceStatusTracker();

        tracker.Record(SystemConfigServices.Ekyc, SystemConfigSlot.Primary, false, "TIMEOUT", Now);

        ServiceStatus status = tracker.Get(SystemConfigServices.Ekyc);
        Assert.Null(status.ActiveSlot);
        Assert.False(status.LastSucceeded);
        Assert.Equal("TIMEOUT", status.LastErrorCode);
    }

    [Fact]
    public void Record_KeepsEachServiceSeparate()
    {
        ServiceStatusTracker tracker = new ServiceStatusTracker();

        tracker.Record(SystemConfigServices.Ekyc, SystemConfigSlot.Primary, true, null, Now);

        Assert.Null(tracker.Get(SystemConfigServices.Smtp).LastSucceeded);
    }
}
