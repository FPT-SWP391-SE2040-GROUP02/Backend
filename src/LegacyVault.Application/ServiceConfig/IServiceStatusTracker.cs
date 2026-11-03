using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.ServiceConfig;

/// <summary>Kết quả của lần gọi gần nhất tới một dịch vụ ngoài.</summary>
public sealed class ServiceStatus
{
    public ServiceStatus(SystemConfigSlot? activeSlot, bool? lastSucceeded, DateTimeOffset? lastAt, string? lastErrorCode)
    {
        ActiveSlot = activeSlot;
        LastSucceeded = lastSucceeded;
        LastAt = lastAt;
        LastErrorCode = lastErrorCode;
    }

    /// <summary>Ô đã trả lời thành công ở lần gọi gần nhất; null khi lần đó thất bại hoặc chưa có lần nào.</summary>
    public SystemConfigSlot? ActiveSlot { get; }

    public bool? LastSucceeded { get; }

    public DateTimeOffset? LastAt { get; }

    /// <summary>Mã lỗi ngắn như HTTP_503, TIMEOUT, NETWORK; không bao giờ chứa khóa hay nội dung.</summary>
    public string? LastErrorCode { get; }
}

/// <summary>
/// Ghi và đọc trạng thái gọi gần nhất của từng dịch vụ ngoài. Giữ trong bộ nhớ: mất khi restart và chỉ đúng với
/// instance hiện tại. Cài đặt ở Infrastructure.
/// </summary>
public interface IServiceStatusTracker
{
    void Record(string serviceKey, SystemConfigSlot slot, bool succeeded, string? errorCode, DateTimeOffset at);

    /// <summary>Chưa có lần gọi nào thì mọi trường đều null.</summary>
    ServiceStatus Get(string serviceKey);
}
