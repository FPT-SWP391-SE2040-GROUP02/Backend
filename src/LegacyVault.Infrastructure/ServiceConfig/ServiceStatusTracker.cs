using System.Collections.Concurrent;
using LegacyVault.Application.ServiceConfig;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Infrastructure.ServiceConfig;

/// <summary>Trạng thái gọi gần nhất của từng dịch vụ, giữ trong bộ nhớ; mỗi lần ghi thay thế lần trước.</summary>
public sealed class ServiceStatusTracker : IServiceStatusTracker
{
    private readonly ConcurrentDictionary<string, ServiceStatus> statuses = new ConcurrentDictionary<string, ServiceStatus>();

    public void Record(string serviceKey, SystemConfigSlot slot, bool succeeded, string? errorCode, DateTimeOffset at)
    {
        SystemConfigSlot? active = null;
        string? code = errorCode;
        if (succeeded)
        {
            active = slot;
            code = null;
        }
        statuses[serviceKey] = new ServiceStatus(active, succeeded, at, code);
    }

    public ServiceStatus Get(string serviceKey)
    {
        ServiceStatus? status = null;
        if (statuses.TryGetValue(serviceKey, out status))
        {
            return status;
        }
        return new ServiceStatus(null, null, null, null);
    }
}
