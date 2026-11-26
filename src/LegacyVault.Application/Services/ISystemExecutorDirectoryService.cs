using LegacyVault.Application.Dtos.Responses;

namespace LegacyVault.Application.Services;

/// <summary>
/// Dịch vụ tra cứu danh bạ Người thực thi hệ thống cho Chủ tài sản lựa chọn.
/// </summary>
public interface ISystemExecutorDirectoryService
{
    Task<List<SystemExecutorResponse>> ListAvailableExecutorsAsync(CancellationToken ct);
}
