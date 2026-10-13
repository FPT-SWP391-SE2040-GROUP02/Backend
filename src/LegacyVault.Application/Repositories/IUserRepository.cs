using LegacyVault.Domain.Entities;

namespace LegacyVault.Application.Repositories;

/// <summary>Truy cập bảng Users.</summary>
public interface IUserRepository
{
    /// <summary>Tìm theo email ĐÃ chuẩn hóa (trim, chữ thường). Không thấy → null.</summary>
    Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken ct);

    /// <summary>Tìm theo Id (có theo dõi thay đổi, sửa rồi SaveChanges là lưu). Không thấy → null.</summary>
    Task<User?> FindByIdAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Thêm user mới rồi lưu ngay. Trả false khi DB báo trùng email (hai request đăng ký cùng email đồng thời).
    /// </summary>
    Task<bool> TryAddAsync(User user, CancellationToken ct);
}
