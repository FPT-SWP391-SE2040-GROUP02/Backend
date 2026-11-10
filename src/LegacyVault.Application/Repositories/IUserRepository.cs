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

    /// <summary>
    /// true khi số điện thoại ĐÃ chuẩn hóa (PhoneRules) thuộc một tài khoản khác <paramref name="exceptUserId"/>.
    /// Mỗi số chỉ thuộc một tài khoản, mọi vai trò (index unique có lọc IX_Users_Phone).
    /// </summary>
    Task<bool> PhoneExistsAsync(string normalizedPhone, Guid? exceptUserId, CancellationToken ct);

    /// <summary>true khi lỗi lưu là do vi phạm index unique (request khác vừa ghi cùng giá trị).</summary>
    bool IsUniqueViolation(Exception exception);
}
