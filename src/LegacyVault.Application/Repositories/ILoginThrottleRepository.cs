using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy cập bảng LoginThrottles (SEC-10).</summary>
public interface ILoginThrottleRepository
{
    /// <summary>Bộ đếm của cặp email đã chuẩn hóa + dải IP. Chưa có → null.</summary>
    Task<LoginThrottle?> FindAsync(string normalizedEmail, string ipRange, CancellationToken ct);

    /// <summary>Đánh dấu "sẽ INSERT".</summary>
    void Add(LoginThrottle throttle);

    /// <summary>Đánh dấu "sẽ DELETE" (đăng nhập đúng thì xóa bộ đếm).</summary>
    void Remove(LoginThrottle throttle);
}
