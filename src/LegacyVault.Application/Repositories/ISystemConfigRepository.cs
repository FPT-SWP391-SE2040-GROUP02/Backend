using LegacyVault.Domain.Entities;
using LegacyVault.Domain.Enums;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy cập bảng SystemConfigs: cấu hình dịch vụ ngoài do Admin sửa trên web.</summary>
public interface ISystemConfigRepository
{
    /// <summary>Dòng của (dịch vụ, ô); chưa có thì null. Trả entity đang được theo dõi để sửa trực tiếp.</summary>
    Task<SystemConfig?> FindAsync(string serviceKey, SystemConfigSlot slot, CancellationToken ct);

    Task<List<SystemConfig>> ListAsync(CancellationToken ct);

    void Add(SystemConfig row);

    void Remove(SystemConfig row);
}
